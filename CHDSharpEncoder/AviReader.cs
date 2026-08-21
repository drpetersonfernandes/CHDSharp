using System.Buffers.Binary;
using System.Text;

namespace CHDSharpEncoder;

/// <summary>
/// Minimal AVI container reader for laserdisc CHD creation (chdman's <c>avi_file</c> read
/// path, src/lib/util/aviio.cpp). Parses RIFF/AVIX headers, stream descriptions ('strh'/
/// 'strf') and frame indexes ('idx1', or a sequential 'movi' scan when no index exists),
/// and serves YUY-family video frames and PCM sound samples.
///
/// Supported video: uncompressed YUY2/VYUY (bytes pass through unchanged) and UYVY
/// (byte-swapped per pixel pair), matching MAME's conversion to its native YUY16 bitmap.
/// Supported audio: uncompressed PCM, 8 or 16 bits per sample.
/// </summary>
public sealed class AviReader : IDisposable
{
    /// <summary>Video format fourccs (little-endian, like MAME's AVI_FOURCC).</summary>
    private const uint FormatUyvy = 0x59565955; // 'UYVY'
    private const uint FormatVyuy = 0x59555956; // 'VYUY'
    private const uint FormatYuy2 = 0x32595559; // 'YUY2'
    private const uint FormatHfyu = 0x55594648; // 'HFYU'

    private const uint ChunkTypeRiff = 0x46464952; // 'RIFF'
    private const uint ChunkTypeList = 0x5453494C; // 'LIST'
    private const uint ListTypeAvi = 0x20495641;   // 'AVI '
    private const uint ListTypeAvix = 0x58495641;  // 'AVIX'
    private const uint ListTypeHdrl = 0x6C726468;  // 'hdrl'
    private const uint ListTypeStrl = 0x6C727473;  // 'strl'
    private const uint ListTypeMovi = 0x69766F6D;  // 'movi'
    private const uint ChunkAvih = 0x68697661;     // 'avih'
    private const uint ChunkStrh = 0x68727473;     // 'strh'
    private const uint ChunkStrf = 0x66727473;     // 'strf'
    private const uint ChunkIdx1 = 0x31786469;     // 'idx1'
    private const uint StreamTypeVids = 0x73646976; // 'vids'
    private const uint StreamTypeAuds = 0x73647561; // 'auds'

    private readonly FileStream _file;
    private readonly List<AviStream> _streams = [];

    private AviReader(FileStream file)
    {
        _file = file;
    }

    /// <summary>Describes one parsed AVI stream.</summary>
    private sealed class AviStream
    {
        public uint Type;                // 'vids' / 'auds'
        public uint Scale = 1;
        public uint Rate;
        public uint SamplesFromHeader;   // dwLength from 'strh'

        public int Width, Height;        // video
        public uint Depth;               // video
        public uint Format;              // video fourcc

        public ushort Channels;          // audio
        public uint SampleRate;          // audio
        public ushort SampleBits;        // audio

        public readonly List<(long Offset, int Length)> Chunks = [];
    }

    /// <summary>Movie description (MAME's <c>avi_file::movie_info</c> subset).</summary>
    public sealed class MovieInfo
    {
        /// <summary>Video timescale ('strh' rate).</summary>
        public uint VideoTimescale { get; internal set; }

        /// <summary>Duration of a single video frame ('strh' scale).</summary>
        public uint VideoSampletime { get; internal set; }

        /// <summary>Total number of video frames.</summary>
        public uint VideoNumsamples { get; internal set; }

        /// <summary>Video width in pixels.</summary>
        public int Width { get; internal set; }

        /// <summary>Video height in lines (as stored; may describe a field for interlaced sources).</summary>
        public int Height { get; internal set; }

        /// <summary>Video format fourcc ('YUY2', 'UYVY', ...).</summary>
        public uint VideoFormat { get; internal set; }

        /// <summary>Total audio channels across all audio streams.</summary>
        public uint AudioChannels { get; internal set; }

        /// <summary>Audio sample rate.</summary>
        public uint AudioSamplerate { get; internal set; }

        /// <summary>Audio bits per sample (8 or 16).</summary>
        public uint AudioSamplebits { get; internal set; }
    }

    /// <summary>Gets the parsed movie information.</summary>
    public MovieInfo Info { get; } = new();

    /// <summary>Opens and parses an AVI file.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable AVI movie.</exception>
    public static AviReader Open(string path)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reader = new AviReader(file);
        try
        {
            reader.ReadMovieData();
            return reader;
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads one video frame as YUY2-ordered bytes (Cb,Y,Cr,Y pixel pairs), the exact layout
    /// of the raw 'chav' video payload. For interlaced laserdisc sources the caller slices
    /// fields from the full-size frame.
    /// </summary>
    /// <param name="frameNum">Zero-based frame number.</param>
    /// <param name="dest">Destination buffer of at least <c>width * height * 2</c> bytes;
    /// short chunks leave the remainder untouched.</param>
    /// <exception cref="ArgumentOutOfRangeException">The frame number is out of range.</exception>
    /// <exception cref="NotSupportedException">The video format is not YUY2/VYUY/UYVY.</exception>
    public void ReadVideoFrame(uint frameNum, Span<byte> dest)
    {
        var stream = GetVideoStream() ?? throw new InvalidDataException("AVI file contains no video stream");
        if (stream.Format is not (FormatUyvy or FormatVyuy or FormatYuy2))
            throw new NotSupportedException(
                $"Unsupported AVI video format '{FourCcToString(stream.Format)}'; YUY2, VYUY or UYVY is required");
        if (frameNum >= (uint)stream.Chunks.Count)
            throw new ArgumentOutOfRangeException(nameof(frameNum), $"AVI frame {frameNum} is out of range (0..{stream.Chunks.Count - 1})");

        var chunk = stream.Chunks[(int)frameNum];
        var data = ReadChunkData(chunk.Offset, chunk.Length);

        // yuv_decompress_to_yuy16: YUY2/VYUY copy verbatim, UYVY swaps each pixel pair
        int count = Math.Min(data.Length, dest.Length);
        if (stream.Format == FormatUyvy)
        {
            for (int i = 0; i + 1 < count; i += 2)
            {
                dest[i] = data[i + 1];
                dest[i + 1] = data[i];
            }
        }
        else
        {
            data.AsSpan(0, count).CopyTo(dest);
        }
    }

    /// <summary>
    /// Reads PCM sound samples of one logical channel (MAME's
    /// <c>avi_file::read_sound_samples</c>). Channels are numbered across all audio streams.
    /// </summary>
    /// <param name="channel">Logical channel index.</param>
    /// <param name="firstSample">First sample to read (per channel).</param>
    /// <param name="numSamples">Number of samples to read; clamped at the end of the stream.</param>
    /// <param name="output">Destination array of <paramref name="numSamples"/> entries.</param>
    /// <exception cref="ArgumentOutOfRangeException">The channel or first sample is out of range.</exception>
    /// <exception cref="NotSupportedException">The audio format is not 8/16-bit PCM.</exception>
    public void ReadSoundSamples(int channel, uint firstSample, uint numSamples, Span<short> output)
    {
        var stream = GetAudioStream(channel, out int offset)
            ?? throw new ArgumentOutOfRangeException(nameof(channel), $"AVI file has no audio channel {channel}");
        if (stream.Format != 0 || (stream.SampleBits != 8 && stream.SampleBits != 16))
            throw new NotSupportedException($"Unsupported AVI audio format (PCM 8/16-bit required, got {stream.SampleBits}-bit)");

        uint totalSamples = (uint)stream.Chunks.Count > 0 ? PerChannelSampleCount(stream) : 0;
        if (firstSample >= totalSamples)
            throw new ArgumentOutOfRangeException(nameof(firstSample), $"AVI sample {firstSample} is out of range (0..{totalSamples - 1})");
        if (firstSample + numSamples > totalSamples)
            numSamples = totalSamples - firstSample;

        uint bytesPerSample = (uint)(stream.SampleBits / 8) * stream.Channels;
        int outPos = 0;

        while (numSamples > 0)
        {
            // locate the chunk containing the first sample
            uint chunkBase = 0, chunkEnd = 0;
            int chunkNum;
            for (chunkNum = 0; chunkNum < stream.Chunks.Count; chunkNum++)
            {
                chunkEnd = chunkBase + (uint)(stream.Chunks[chunkNum].Length - 8) / bytesPerSample;
                if (firstSample < chunkEnd)
                    break;
                chunkBase = chunkEnd;
            }

            // if we hit the end, fill the rest with silence
            if (chunkNum == stream.Chunks.Count)
            {
                output.Slice(outPos, (int)numSamples).Clear();
                break;
            }

            var data = ReadChunkData(stream.Chunks[chunkNum].Offset, stream.Chunks[chunkNum].Length);
            uint samplesThisChunk = Math.Min(chunkEnd - firstSample, numSamples);

            int baseIndex = (int)(stream.Channels * (firstSample - chunkBase) + offset);
            if (stream.SampleBits == 16)
            {
                for (int i = 0; i < samplesThisChunk; i++, baseIndex += stream.Channels)
                    output[outPos++] = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(8 + baseIndex * 2));
            }
            else
            {
                for (int i = 0; i < samplesThisChunk; i++, baseIndex += stream.Channels)
                    output[outPos++] = (short)((data[8 + baseIndex] << 8) - 0x8000);
            }

            firstSample += samplesThisChunk;
            numSamples -= samplesThisChunk;
        }
    }

    /// <summary>Total per-channel sample count across an audio stream's chunks.</summary>
    private static uint PerChannelSampleCount(AviStream stream)
    {
        ulong total = 0;
        foreach (var (_, length) in stream.Chunks)
            total += (ulong)((length - 8) / ((stream.SampleBits / 8) * stream.Channels));
        return (uint)Math.Min(total, uint.MaxValue);
    }

    /// <summary>Parses the whole file: headers, streams, and chunk indexes.</summary>
    private void ReadMovieData()
    {
        long fileLength = _file.Length;

        // walk root-level RIFF chunks (a second RIFF/'AVIX' extends files past 4 GB)
        long pos = 0;
        bool anyAvi = false;
        long firstMoviData = -1;
        while (pos + 12 <= fileLength)
        {
            ReadOnlySpan<byte> header = ReadAt(pos, 12);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != ChunkTypeRiff)
                break;

            uint riffSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4));
            uint listType = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8));
            if (listType is not (ListTypeAvi or ListTypeAvix))
                throw new InvalidDataException("Not an AVI file (RIFF type is not 'AVI ')");

            anyAvi = true;
            long bodyEnd = Math.Min(pos + 8 + riffSize, fileLength);

            // walk the chunks inside this RIFF
            long chunkPos = pos + 12;
            while (chunkPos + 8 <= bodyEnd)
            {
                ReadOnlySpan<byte> chunkHeader = ReadAt(chunkPos, 8);
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader.Slice(4));
                long dataPos = chunkPos + 8;
                long nextPos = dataPos + size + (size & 1);

                if (type == ChunkTypeList)
                {
                    ReadOnlySpan<byte> listHeader = ReadAt(dataPos, 4);
                    uint listType2 = BinaryPrimitives.ReadUInt32LittleEndian(listHeader);
                    switch (listType2)
                    {
                        case ListTypeHdrl:
                            ParseHeaderList(dataPos + 4, size - 4);
                            break;
                        case ListTypeMovi:
                            if (firstMoviData < 0)
                                firstMoviData = dataPos + 4;
                            ScanMoviList(dataPos + 4, size - 4);
                            break;
                    }
                }
                else if (type == ChunkIdx1 && firstMoviData >= 0)
                {
                    ParseIdx1(dataPos, size, firstMoviData);
                }

                if (nextPos <= chunkPos)
                    break;
                chunkPos = nextPos;
            }

            pos = bodyEnd;
        }

        if (!anyAvi)
            throw new InvalidDataException("Not an AVI file (missing RIFF/'AVI ' header)");
        ExtractMovieInfo();
    }

    /// <summary>Parses 'hdrl': the 'avih' stream count plus one 'strl' list per stream.</summary>
    private void ParseHeaderList(long pos, long size)
    {
        long end = pos + size;
        while (pos + 8 <= end)
        {
            ReadOnlySpan<byte> header = ReadAt(pos, 8);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(header);
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4));
            long dataPos = pos + 8;

            if (type == ChunkAvih && chunkSize >= 28)
            {
                ReadOnlySpan<byte> data = ReadAt(dataPos, (int)Math.Min(chunkSize, 64));
                uint streamCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24));
                for (int i = 0; i < streamCount && i < 16; i++)
                    _streams.Add(new AviStream());
            }
            else if (type == ChunkTypeList)
            {
                ReadOnlySpan<byte> listHeader = ReadAt(dataPos, 4);
                if (BinaryPrimitives.ReadUInt32LittleEndian(listHeader) == ListTypeStrl)
                    ParseStreamList(dataPos + 4, chunkSize - 4);
            }

            pos = dataPos + chunkSize + (chunkSize & 1);
        }
    }

    /// <summary>Parses one 'strl' list: 'strh' (timing/type) and 'strf' (format).</summary>
    private void ParseStreamList(long pos, long size)
    {
        // find the first stream without a type yet (streams arrive in order)
        var stream = _streams.FirstOrDefault(s => s.Type == 0);
        if (stream == null)
            return;

        long end = pos + size;
        while (pos + 8 <= end)
        {
            ReadOnlySpan<byte> header = ReadAt(pos, 8);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(header);
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4));
            long dataPos = pos + 8;

            if (type == ChunkStrh && chunkSize >= 36)
            {
                ReadOnlySpan<byte> data = ReadAt(dataPos, 40);
                stream.Type = BinaryPrimitives.ReadUInt32LittleEndian(data);
                stream.Scale = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20));
                stream.Rate = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24));
                stream.SamplesFromHeader = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32));
            }
            else if (type == ChunkStrf && chunkSize >= 16)
            {
                ReadOnlySpan<byte> data = ReadAt(dataPos, (int)Math.Min(chunkSize, 64));
                if (stream.Type == StreamTypeVids)
                {
                    stream.Width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
                    stream.Height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8));
                    stream.Depth = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14));
                    stream.Format = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16));
                }
                else if (stream.Type == StreamTypeAuds)
                {
                    stream.Channels = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2));
                    stream.SampleRate = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
                    stream.SampleBits = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14));
                }
            }

            pos = dataPos + chunkSize + (chunkSize & 1);
        }
    }

    /// <summary>Parses the 'idx1' index; offsets are relative to the first 'movi' data start.</summary>
    private void ParseIdx1(long pos, long size, long moviDataBase)
    {
        ReadOnlySpan<byte> data = ReadAt(pos, (int)size);
        int entries = data.Length / 16;
        for (int e = 0; e < entries; e++)
        {
            int baseIdx = e * 16;
            uint chunkId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(baseIdx));
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(baseIdx + 8));
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(baseIdx + 12));

            int streamNum = (int)(((chunkId >> 8) & 0xff) - '0') + 10 * (int)((chunkId & 0xff) - '0');
            if (streamNum < 0 || streamNum >= _streams.Count)
                continue;

            _streams[streamNum].Chunks.Add((moviDataBase + offset, (int)(chunkSize + 8)));
        }
    }

    /// <summary>
    /// Fallback when no 'idx1' exists: assigns 'movi' data chunks to their streams in
    /// encounter order (one chunk = one frame/sample-block).
    /// </summary>
    private void ScanMoviList(long pos, long size)
    {
        long end = pos + size;
        while (pos + 8 <= end)
        {
            ReadOnlySpan<byte> header = ReadAt(pos, 8);
            uint chunkId = BinaryPrimitives.ReadUInt32LittleEndian(header);
            uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4));

            int streamNum = (int)(((chunkId >> 8) & 0xff) - '0') + 10 * (int)((chunkId & 0xff) - '0');
            char kind = (char)((chunkId >> 24) & 0xff); // 'dc'/'db' video, 'wb' audio
            if (streamNum >= 0 && streamNum < _streams.Count && kind is 'd' or 'c' or 'w')
            {
                _streams[streamNum].Chunks.Add((pos, (int)(chunkSize + 8)));
            }

            pos += 8 + chunkSize + (chunkSize & 1);
        }
    }

    /// <summary>Fills <see cref="Info"/> from the parsed streams (MAME's <c>extract_movie_info</c>).</summary>
    private void ExtractMovieInfo()
    {
        var video = GetVideoStream();
        if (video != null)
        {
            Info.VideoTimescale = video.Rate;
            Info.VideoSampletime = video.Scale;
            Info.VideoNumsamples = video.SamplesFromHeader != 0
                ? video.SamplesFromHeader
                : (uint)video.Chunks.Count;
            Info.Width = video.Width;
            Info.Height = video.Height;
            Info.VideoFormat = video.Format;
        }

        var firstAudio = GetAudioStream(0, out _);
        if (firstAudio != null)
        {
            Info.AudioChannels = 1;
            Info.AudioSamplerate = firstAudio.SampleRate;
            Info.AudioSamplebits = firstAudio.SampleBits;

            // count channels across all compatible audio streams
            while (GetAudioStream((int)Info.AudioChannels, out _) is { } next)
            {
                Info.AudioChannels++;
                if (next.SampleRate != firstAudio.SampleRate || next.SampleBits != firstAudio.SampleBits ||
                    next.Channels != firstAudio.Channels)
                    throw new InvalidDataException("AVI file has incompatible audio streams");
            }
        }
    }

    private AviStream? GetVideoStream()
    {
        foreach (var s in _streams)
            if (s.Type == StreamTypeVids)
                return s;
        return null;
    }

    private AviStream? GetAudioStream(int channel, out int offset)
    {
        offset = 0;
        foreach (var s in _streams)
        {
            if (s.Type != StreamTypeAuds || s.Channels == 0)
                continue;
            if (channel < s.Channels)
            {
                offset = channel;
                return s;
            }

            channel -= s.Channels;
        }

        return null;
    }

    /// <summary>Reads a full chunk (including its 8-byte header) at an absolute file offset.</summary>
    private byte[] ReadChunkData(long offset, int length)
    {
        if (offset < 0 || length < 8 || offset + length > _file.Length)
            throw new InvalidDataException($"AVI chunk at {offset} (length {length}) is out of bounds");
        return ReadAt(offset, length);
    }

    private byte[] ReadAt(long offset, int count)
    {
        var buffer = new byte[count];
        _file.Position = offset;
        int read = 0;
        while (read < count)
        {
            int n = _file.Read(buffer, read, count - read);
            if (n == 0)
                throw new EndOfStreamException($"Unexpected end of AVI file at offset {offset}");
            read += n;
        }

        return buffer;
    }

    private static string FourCcToString(uint fourcc)
    {
        byte[] bytes =
        [
            (byte)(fourcc & 0xFF),
            (byte)((fourcc >> 8) & 0xFF),
            (byte)((fourcc >> 16) & 0xFF),
            (byte)((fourcc >> 24) & 0xFF)
        ];
        return Encoding.ASCII.GetString(bytes);
    }

    /// <inheritdoc/>
    public void Dispose() => _file.Dispose();
}
