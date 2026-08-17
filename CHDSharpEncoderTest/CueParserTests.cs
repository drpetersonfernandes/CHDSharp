using CHDSharpEncoder;

namespace CHDSharpEncoderTest;

public class CueParserTests : IDisposable
{
    private readonly string _dir;

    public CueParserTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cue_parser_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void ParseSimpleCue_TwoTracks()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 01 03:00:00
            """);
        // track 1: 3:00:00 = 13500 frames; track 2: 8 frames at end of file
        WriteBin(2352 * (13500 + 8));

        var toc = Parse();

        Assert.Equal(2, toc.Tracks.Count);
        Assert.Equal(CdTrackType.Mode1Raw, toc.Tracks[0].TrackType);
        Assert.Equal(CdTrackType.Audio, toc.Tracks[1].TrackType);
        Assert.Equal(2352, toc.Tracks[0].DataSize);
        Assert.Equal(2352, toc.Tracks[1].DataSize);
        Assert.Equal(13500, toc.Tracks[0].Frames);
        Assert.Equal(8, toc.Tracks[1].Frames);
        Assert.Equal(0L, toc.Tracks[0].FileOffset);
        Assert.Equal(13500L * 2352, toc.Tracks[1].FileOffset);
        Assert.True(toc.Tracks[0].Swap == false, "data track should not be byte-swapped");
        Assert.True(toc.Tracks[1].Swap, "audio track must be byte-swapped");
    }

    [Fact]
    public void ParseCue_WithPregap()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 AUDIO
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 02:30:00
                INDEX 01 02:32:00
            """);
        WriteBin(2352 * (2 * 60 * 75 + 30 * 75 + 8)); // 2:30:00 + 8 frames

        var toc = Parse();

        Assert.Equal(150, toc.Tracks[1].Pregap);
        Assert.Equal(CdTrackType.Audio, toc.Tracks[1].PgType);
        Assert.Equal(2352, toc.Tracks[1].PgDataSize);
        // track 1 length is measured up to track 2's INDEX 00 (2:30:00)
        Assert.Equal(2 * 60 * 75 + 30 * 75, toc.Tracks[0].Frames);
    }

    [Fact]
    public void ParseCue_WithPregapKeyword()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 AUDIO
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                PREGAP 00:02:00
                INDEX 01 02:00:00
            """);
        WriteBin(2352 * (2 * 60 * 75 + 8));

        var toc = Parse();

        Assert.Equal(150, toc.Tracks[1].Pregap);
        Assert.Equal(0, toc.Tracks[1].PgType);
    }

    [Fact]
    public void ParseCue_WithPostgap()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 AUDIO
                INDEX 01 00:00:00
                POSTGAP 00:00:30
              TRACK 02 AUDIO
                INDEX 01 01:00:00
            """);
        WriteBin(2352 * (60 * 75 + 8));

        var toc = Parse();

        Assert.Equal(30, toc.Tracks[0].Postgap);
    }

    [Fact]
    public void ParseMsf_Conversion()
    {
        Assert.Equal(16905, CueParser.ParseMsfToFrames("03:45:30"));
        Assert.Equal(0, CueParser.ParseMsfToFrames("00:00:00"));
        Assert.Equal(75, CueParser.ParseMsfToFrames("00:01:00"));
        Assert.Equal(12345, CueParser.ParseMsfToFrames("12345"));
    }

    [Fact]
    public void ParseMsf_Invalid_Throws()
    {
        Assert.Throws<InvalidDataException>(() => CueParser.ParseMsfToFrames("03:45"));
        Assert.Throws<InvalidDataException>(() => CueParser.ParseMsfToFrames("abc"));
    }

    [Fact]
    public void MultipleTracks_IndexBasedLengths()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                INDEX 00 03:00:00
                INDEX 01 03:02:00
              TRACK 03 AUDIO
                INDEX 01 06:02:00
            """);
        WriteBin(2352 * (13500 + 13650 + 100));

        var toc = Parse();

        Assert.Equal(3, toc.Tracks.Count);
        Assert.Equal(13500, toc.Tracks[0].Frames);
        Assert.Equal(0L, toc.Tracks[0].FileOffset);
        // track 2 length comes from track 3's INDEX 00 (defaulted to INDEX 01)
        Assert.Equal(13650, toc.Tracks[1].Frames);
        Assert.Equal(13500L * 2352, toc.Tracks[1].FileOffset);
        Assert.Equal(150, toc.Tracks[1].Pregap);
        // track 3 is last in the file: remainder
        Assert.Equal(100, toc.Tracks[2].Frames);
        Assert.Equal((13500L + 13650) * 2352, toc.Tracks[2].FileOffset);
    }

    [Fact]
    public void SeparateFiles_PerTrack()
    {
        WriteCue("""
            FILE "data.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            FILE "audio.bin" BINARY
              TRACK 02 AUDIO
                INDEX 01 00:00:00
            """);
        WriteFile("data.bin", new byte[2352 * 300]);
        WriteFile("audio.bin", new byte[2352 * 100]);

        var toc = Parse();

        Assert.Equal(2, toc.Tracks.Count);
        Assert.Equal(300, toc.Tracks[0].Frames);
        Assert.Equal(0L, toc.Tracks[0].FileOffset);
        Assert.Equal(100, toc.Tracks[1].Frames);
        Assert.Equal(0L, toc.Tracks[1].FileOffset);
        Assert.NotEqual(toc.Tracks[0].FileName, toc.Tracks[1].FileName);
    }

    [Fact]
    public void Filename_WithSpaces()
    {
        WriteCue("""
            FILE "my game (disc 1).bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """);
        WriteFile("my game (disc 1).bin", new byte[2352 * 50]);

        var toc = Parse();

        Assert.Single(toc.Tracks);
        Assert.Equal(50, toc.Tracks[0].Frames);
        Assert.Equal(Path.Combine(_dir, "my game (disc 1).bin"), toc.Tracks[0].FileName);
    }

    [Theory]
    [InlineData("MODE1", CdTrackType.Mode1, 2048)]
    [InlineData("MODE1/2048", CdTrackType.Mode1, 2048)]
    [InlineData("MODE1_RAW", CdTrackType.Mode1Raw, 2352)]
    [InlineData("MODE1/2352", CdTrackType.Mode1Raw, 2352)]
    [InlineData("MODE2", CdTrackType.Mode2, 2336)]
    [InlineData("MODE2/2336", CdTrackType.Mode2, 2336)]
    [InlineData("MODE2_FORM1", CdTrackType.Mode2Form1, 2048)]
    [InlineData("MODE2/2048", CdTrackType.Mode2Form1, 2048)]
    [InlineData("MODE2_FORM2", CdTrackType.Mode2Form2, 2324)]
    [InlineData("MODE2/2324", CdTrackType.Mode2Form2, 2324)]
    [InlineData("MODE2_FORM_MIX", CdTrackType.Mode2FormMix, 2336)]
    [InlineData("MODE2_RAW", CdTrackType.Mode2Raw, 2352)]
    [InlineData("MODE2/2352", CdTrackType.Mode2Raw, 2352)]
    [InlineData("CDI/2352", CdTrackType.Mode2Raw, 2352)]
    [InlineData("AUDIO", CdTrackType.Audio, 2352)]
    public void TrackTypes_MappedCorrectly(string typeString, int expectedType, int expectedDataSize)
    {
        WriteCue($"""
            FILE "game.bin" BINARY
              TRACK 01 {typeString}
                INDEX 01 00:00:00
            """);
        WriteBin(2352 * 8);

        var toc = Parse();

        Assert.Equal(expectedType, toc.Tracks[0].TrackType);
        Assert.Equal(expectedDataSize, toc.Tracks[0].DataSize);
    }

    [Fact]
    public void SubType_Rw_And_RwRaw()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352 RW
                INDEX 01 00:00:00
              TRACK 02 AUDIO RW_RAW
                INDEX 01 01:00:00
            """);
        WriteBin(2448 * (60 * 75 + 8)); // frames include 96 subcode bytes each

        var toc = Parse();

        Assert.Equal(CdSubType.Normal, toc.Tracks[0].SubType);
        Assert.Equal(96, toc.Tracks[0].SubSize);
        Assert.Equal(CdSubType.Raw, toc.Tracks[1].SubType);
        Assert.Equal(96, toc.Tracks[1].SubSize);
        Assert.Equal(60 * 75, toc.Tracks[0].Frames);
    }

    [Fact]
    public void RemComments_AreIgnored()
    {
        WriteCue("""
            REM This is a comment
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            REM another comment
            """);
        WriteBin(2352 * 8);

        var toc = Parse();

        Assert.Single(toc.Tracks);
    }

    [Fact]
    public void EmptyCue_ProducesNoTracks()
    {
        WriteCue("");

        var toc = Parse();

        Assert.Empty(toc.Tracks);
    }

    [Fact]
    public void MissingIndex01_Throws()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 00 00:00:00
            """);
        WriteBin(2352 * 8);

        Assert.Throws<InvalidDataException>(() => Parse());
    }

    [Fact]
    public void UnknownTrackType_Throws()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE3/2352
                INDEX 01 00:00:00
            """);
        WriteBin(2352 * 8);

        Assert.Throws<InvalidDataException>(() => Parse());
    }

    [Fact]
    public void UnhandledFileType_Throws()
    {
        WriteCue("""
            FILE "game.bin" MP3
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """);

        Assert.Throws<InvalidDataException>(() => Parse());
    }

    [Fact]
    public void MissingBinFile_Throws()
    {
        WriteCue("""
            FILE "game.bin" BINARY
              TRACK 01 MODE1/2352
                INDEX 01 00:00:00
            """);

        Assert.Throws<FileNotFoundException>(() => Parse());
    }

    [Fact]
    public void MissingCueFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => new CueParser().Parse(Path.Combine(_dir, "nope.cue")));
    }

    [Fact]
    public void WavFile_IsParsed()
    {
        WriteCue("""
            FILE "audio.wav" WAVE
              TRACK 01 AUDIO
                INDEX 01 00:00:00
            """);
        WriteWav(8); // 8 frames of audio = 18816 bytes

        var toc = Parse();

        Assert.Single(toc.Tracks);
        Assert.Equal(8, toc.Tracks[0].Frames);
        Assert.Equal(44L, toc.Tracks[0].FileOffset); // data chunk starts after the 44-byte header
        Assert.True(toc.Tracks[0].Swap, "audio track must be byte-swapped");
    }

    [Fact]
    public void WavFile_Invalid_Throws()
    {
        WriteCue("""
            FILE "audio.wav" WAVE
              TRACK 01 AUDIO
                INDEX 01 00:00:00
            """);
        WriteFile("audio.wav", new byte[100]);

        Assert.Throws<InvalidDataException>(() => Parse());
    }

    // ----- helpers -----

    private CdToc Parse()
    {
        return new CueParser().Parse(Path.Combine(_dir, "test.cue"));
    }

    private void WriteCue(string content)
    {
        WriteFile("test.cue", content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
    }

    private void WriteBin(long size)
    {
        WriteFile("game.bin", new byte[size]);
    }

    private void WriteFile(string name, string content)
    {
        File.WriteAllText(Path.Combine(_dir, name), content);
    }

    private void WriteFile(string name, byte[] content)
    {
        File.WriteAllBytes(Path.Combine(_dir, name), content);
    }

    private void WriteWav(int frames)
    {
        uint dataLength = (uint)(frames * CdConstants.MaxSectorData);
        using var fs = new FileStream(Path.Combine(_dir, "audio.wav"), FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);

        void WriteFourCc(string tag) => w.Write(System.Text.Encoding.ASCII.GetBytes(tag));

        WriteFourCc("RIFF");
        w.Write(36 + dataLength); // RIFF chunk size
        WriteFourCc("WAVE");
        WriteFourCc("fmt ");
        w.Write(16u);       // fmt chunk size
        w.Write((ushort)1); // PCM
        w.Write((ushort)2); // stereo
        w.Write(44100u);    // sample rate
        w.Write(176400u);   // byte rate
        w.Write((ushort)4); // block align
        w.Write((ushort)16);// bits per sample
        WriteFourCc("data");
        w.Write(dataLength);

        byte[] silence = new byte[CdConstants.MaxSectorData];
        for (int i = 0; i < frames; i++)
            w.Write(silence);
    }
}