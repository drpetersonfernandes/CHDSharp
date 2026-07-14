namespace CHDSharpTester.Models;

public class TestConfiguration
{
    public string ChdmanExePath { get; set; } = string.Empty;
    public List<ChdFileEntry> Files { get; set; } = [];
}
