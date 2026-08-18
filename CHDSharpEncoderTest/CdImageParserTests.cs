using CHDSharpEncoder;
using CHDSharpEncoder.Models;

namespace CHDSharpEncoderTest;

/// <summary>Verifies the ISO/GDI/TOC parsers and the extension-based dispatcher.</summary>
public class CdImageParserTests : IDisposable
{
    private readonly string _dir;

    public CdImageParserTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cd_image_parser_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }

    // ----- ISO -----

    [Fact]
    public void Iso_2048Sectors_IsMode1()
    {
        string path = WriteFile("data.iso", new byte[2048 * 100]);
        var toc = new IsoParser().Parse(path);

        Assert.Single(toc.Tracks);
        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(2048, toc.Tracks[0].DataSize);
        Assert.Equal(100, toc.Tracks[0].Frames);
        Assert.Equal(path, toc.Tracks[0].FileName);
        Assert.False(toc.Tracks[0].Swap);
    }

    [Fact]
    public void Iso_2336Sectors_IsMode2()
    {
        string path = WriteFile("data.iso", new byte[2336 * 50]);
        var toc = new IsoParser().Parse(path);

        Assert.Equal(CdTrackType.Mode2, toc.Tracks[0].TrackType);
        Assert.Equal(2336, toc.Tracks[0].DataSize);
        Assert.Equal(50, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Iso_2352Sectors_IsMode2Raw()
    {
        string path = WriteFile("data.iso", new byte[2352 * 40]);
        var toc = new IsoParser().Parse(path);

        Assert.Equal(CdTrackType.Mode2Raw, toc.Tracks[0].TrackType);
        Assert.Equal(2352, toc.Tracks[0].DataSize);
        Assert.Equal(40, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Iso_UnrecognizedSize_Throws()
    {
        string path = WriteFile("bad.iso", new byte[1000]);
        Assert.Throws<InvalidDataException>(() => new IsoParser().Parse(path));
    }

    // ----- GDI -----

    [Fact]
    public void Gdi_TwoTracks_WithLbaGap_ComputesPadFrames()
    {
        // track 1: 100 frames at LBA 0; track 2 (audio): 50 frames at LBA 200 → 100 pad frames
        WriteFile("track01.bin", new byte[2352 * 100]);
        WriteFile("track02.raw", new byte[2352 * 50]);
        string path = WriteFile("game.gdi", """
            2
            1 0 4 2352 "track01.bin" 0
            2 200 0 2352 "track02.raw" 0
            """);

        var toc = new GdiParser().Parse(path);

        Assert.True((toc.Flags & CdTocFlags.GdRom) != 0);
        Assert.Equal(2, toc.Tracks.Count);
        Assert.Equal(CdTrackType.Mode1Raw, toc.Tracks[0].TrackType);
        Assert.Equal(200, toc.Tracks[0].Frames); // 100 data + 100 pad
        Assert.Equal(100, toc.Tracks[0].PadFrames);
        Assert.Equal(CdTrackType.Audio, toc.Tracks[1].TrackType);
        Assert.Equal(50, toc.Tracks[1].Frames);
        Assert.Equal(0, toc.Tracks[1].PadFrames);
        Assert.True(toc.Tracks[1].Swap, "GDI audio tracks must be byte-swapped");
    }

    [Fact]
    public void Gdi_2048Sectors_IsMode1()
    {
        WriteFile("track01.bin", new byte[2048 * 60]);
        string path = WriteFile("game.gdi", """
            1
            1 0 4 2048 "track01.bin" 0
            """);

        var toc = new GdiParser().Parse(path);

        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(2048, toc.Tracks[0].DataSize);
        Assert.Equal(60, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Gdi_ThreeTracks_Contiguous()
    {
        WriteFile("track01.bin", new byte[2352 * 300]);
        WriteFile("track02.raw", new byte[2352 * 100]);
        WriteFile("track03.raw", new byte[2352 * 100]);
        string path = WriteFile("game.gdi", """
            3
            1 0 4 2352 "track01.bin" 0
            2 300 0 2352 "track02.raw" 0
            3 400 0 2352 "track03.raw" 0
            """);

        var toc = new GdiParser().Parse(path);

        Assert.Equal(3, toc.Tracks.Count);
        Assert.Equal(300, toc.Tracks[0].Frames);
        Assert.Equal(0, toc.Tracks[0].PadFrames);
        Assert.Equal(100, toc.Tracks[1].Frames);
        Assert.Equal(100, toc.Tracks[2].Frames);
    }

    [Fact]
    public void Gdi_MissingTracks_Throws()
    {
        WriteFile("track01.bin", new byte[2352 * 10]);
        string path = WriteFile("game.gdi", """
            2
            1 0 4 2352 "track01.bin" 0
            """);

        Assert.Throws<InvalidDataException>(() => new GdiParser().Parse(path));
    }

    [Fact]
    public void Gdi_WrongParamCount_Throws()
    {
        string path = WriteFile("game.gdi", """
            1
            1 0 4 2352 "track01.bin"
            """);

        Assert.Throws<InvalidDataException>(() => new GdiParser().Parse(path));
    }

    [Fact]
    public void Gdi_UnknownTrackType_Throws()
    {
        string path = WriteFile("game.gdi", """
            1
            1 0 3 2352 "track01.bin" 0
            """);

        Assert.Throws<InvalidDataException>(() => new GdiParser().Parse(path));
    }

    [Fact]
    public void Gdi_MissingDataFile_Throws()
    {
        string path = WriteFile("game.gdi", """
            1
            1 0 4 2352 "nope.bin" 0
            """);

        Assert.Throws<FileNotFoundException>(() => new GdiParser().Parse(path));
    }

    // ----- TOC -----

    [Fact]
    public void Toc_DataTrack_WithMsfLength()
    {
        WriteFile("data.bin", new byte[2048 * 120]);
        string path = WriteFile("disc.toc", """
            TRACK MODE1
            DATAFILE "data.bin" 0 01:36:00
            """);

        var toc = new TocParser().Parse(path);

        Assert.Single(toc.Tracks);
        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(2048, toc.Tracks[0].DataSize);
        Assert.Equal(7200, toc.Tracks[0].Frames); // 01:36:00 = 96 s = 7200 frames
        Assert.Equal(Path.Combine(_dir, "data.bin"), toc.Tracks[0].FileName);
        Assert.Equal(0L, toc.Tracks[0].FileOffset);
    }

    [Fact]
    public void Toc_AudioTrack_WithStartPregap()
    {
        WriteFile("audio.wav", new byte[2352 * 50]);
        string path = WriteFile("disc.toc", """
            TRACK AUDIO
            AUDIOFILE "audio.wav" 0 00:00:50
            START 00:02:00
            """);

        var toc = new TocParser().Parse(path);

        Assert.Single(toc.Tracks);
        Assert.Equal(CdTrackType.Audio, toc.Tracks[0].TrackType);
        Assert.Equal(50, toc.Tracks[0].Frames);
        Assert.Equal(150, toc.Tracks[0].Pregap);
        Assert.True(toc.Tracks[0].Swap == false, "TOC files must not be byte-swapped by default");
    }

    [Fact]
    public void Toc_SwapFlag_AndDecimalOffset()
    {
        WriteFile("data.bin", new byte[2352 * 60]);
        string path = WriteFile("disc.toc", """
            TRACK AUDIO
            FILE "data.bin" SWAP #2352 00:00:59
            """);

        var toc = new TocParser().Parse(path);

        Assert.True(toc.Tracks[0].Swap);
        Assert.Equal(2352L, toc.Tracks[0].FileOffset);
        Assert.Equal(59, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Toc_OffsetAndLength_AreDistinguished()
    {
        WriteFile("data.bin", new byte[2352 * 100]);
        string path = WriteFile("disc.toc", """
            TRACK AUDIO
            AUDIOFILE "data.bin" 00:00:10 00:00:50
            """);

        var toc = new TocParser().Parse(path);

        // offset 10 frames in bytes + length 50 frames
        Assert.Equal(10L * 2352, toc.Tracks[0].FileOffset);
        Assert.Equal(50, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Toc_UnknownTrackType_Throws()
    {
        string path = WriteFile("disc.toc", """
            TRACK MYSTERY
            DATAFILE "data.bin" 0 00:01:00
            """);

        Assert.Throws<InvalidDataException>(() => new TocParser().Parse(path));
    }

    // ----- dispatcher -----

    [Fact]
    public void Dispatcher_Cue_RoutesToCueParser()
    {
        string cue = WriteFile("disc.cue", "FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");
        WriteFile("game.bin", new byte[2352 * 20]);

        var toc = CdImageParser.Parse(cue);

        Assert.Equal(0u, toc.Flags);
        Assert.Equal(CdTrackType.Mode1Raw, toc.Tracks[0].TrackType);
        Assert.Equal(20, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Dispatcher_Gdi_RoutesToGdiParser()
    {
        WriteFile("track01.bin", new byte[2352 * 20]);
        string gdi = WriteFile("disc.gdi", "1\n1 0 4 2352 \"track01.bin\" 0\n");

        var toc = CdImageParser.Parse(gdi);

        Assert.True((toc.Flags & CdTocFlags.GdRom) != 0);
        Assert.Equal(20, toc.Tracks[0].Frames);
    }

    [Theory]
    [InlineData("disc.iso")]
    [InlineData("disc.cdr")]
    [InlineData("disc.toast")]
    public void Dispatcher_IsoExtensions_RouteToIsoParser(string fileName)
    {
        string iso = WriteFile(fileName, new byte[2048 * 20]);

        var toc = CdImageParser.Parse(iso);

        Assert.Equal(0u, toc.Flags);
        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(20, toc.Tracks[0].Frames);
    }

    [Fact]
    public void Dispatcher_Toc_RoutesToTocParser()
    {
        WriteFile("data.bin", new byte[2048 * 20]);
        string tocFile = WriteFile("disc.toc", "TRACK MODE1\nDATAFILE \"data.bin\" 0 00:01:00\n");

        var toc = CdImageParser.Parse(tocFile);

        Assert.Equal(0u, toc.Flags);
        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(75, toc.Tracks[0].Frames); // 00:01:00 = 75 frames
    }

    [Fact]
    public void Dispatcher_UnknownExtension_RoutesToTocParser()
    {
        WriteFile("data.bin", new byte[2048 * 20]);
        string tocFile = WriteFile("disc.bin.cue2", "TRACK MODE1\nDATAFILE \"data.bin\" 0 00:01:00\n");

        var toc = CdImageParser.Parse(tocFile);

        Assert.Equal(CdTrackType.Mode1, toc.Tracks[0].TrackType);
        Assert.Equal(75, toc.Tracks[0].Frames);
    }

    // ----- helpers -----

    private string WriteFile(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteFile(string name, byte[] content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }
}

