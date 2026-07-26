using CHDSharp.Models;

namespace CHDSharp.Tests;

public class ErrorExtensionTests
{
    [Fact]
    public void GetMessage_none_returns_no_error()
    {
        Assert.Equal("No error", ChdError.Chderrnone.GetMessage());
    }

    [Fact]
    public void GetMessage_all_defined_codes_return_non_empty()
    {
        var allErrors = Enum.GetValues<ChdError>();
        foreach (var error in allErrors)
        {
            var msg = error.GetMessage();
            Assert.False(string.IsNullOrWhiteSpace(msg),
                $"GetMessage for {error} returned empty or null");
        }
    }

    [Fact]
    public void GetMessage_undefined_enum_returns_unknown()
    {
        const ChdError undefined = (ChdError)9999;
        var msg = undefined.GetMessage();
        Assert.Contains("Unknown error", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void GetMessage_filnotfound_returns_file_not_found()
    {
        Assert.Equal("File not found", ChdError.Chderrfilenotfound.GetMessage());
    }

    [Fact]
    public void GetMessage_requiresparent_mentions_parent()
    {
        Assert.Contains("parent", ChdError.Chderrrequiresparent.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetMessage_decompression_mentions_decompression()
    {
        Assert.Contains("Decompression", ChdError.Chderrdecompressionerror.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }
}
