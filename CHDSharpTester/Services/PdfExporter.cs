using CHDSharpTester.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CHDSharpTester.Services;

/// <summary>Generates a PDF report from a <see cref="TestSessionResult"/> using QuestPDF.</summary>
public static class PdfExporter
{
    static PdfExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Exports the test session results to a PDF file at the specified output path.</summary>
    /// <param name="session">The test session results to export.</param>
    /// <param name="chdmanVersion">The chdman version string to include in the report header, or null.</param>
    /// <param name="outputPath">The full path where the PDF file will be written.</param>
    public static void Export(TestSessionResult session, string? chdmanVersion, string outputPath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(header =>
                {
                    header.Item().Text("CHDSharp Tester — Results Report")
                        .Bold().FontSize(16).FontColor(Colors.Blue.Darken3);

                    var genText = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    if (chdmanVersion != null)
                        genText += $"    chdman: {chdmanVersion}";
                    header.Item().Text(genText).FontSize(8).FontColor(Colors.Grey.Medium);

                    header.Item().PaddingTop(5).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text($"Files: {session.TotalFiles}  |  " +
                                $"Passed: {session.PassedFiles}  |  " +
                                $"Failed: {session.FailedFiles}  |  " +
                                $"Skipped: {session.SkippedFiles}").Bold();
                            c.Item().Text($"SubTests: {session.TotalSubTests} total, " +
                                $"{session.PassedSubTests} passed, " +
                                $"{session.FailedSubTests} failed, " +
                                $"{session.SkippedSubTests} skipped").FontSize(8);
                        });
                        row.ConstantItem(100).Text($"Time: {session.TotalElapsedSeconds:N1}s")
                            .FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2.5f);
                        cols.RelativeColumn(1.2f);
                        cols.RelativeColumn(1f);
                        cols.RelativeColumn(5f);
                        cols.RelativeColumn(1f);
                        cols.RelativeColumn(1f);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Background(Colors.Grey.Lighten3)
                            .Padding(3).Text("File").Bold();
                        header.Cell().Background(Colors.Grey.Lighten3)
                            .Padding(3).Text("Size").Bold();
                        header.Cell().Background(Colors.Grey.Lighten3)
                            .Padding(3).Text("Time").Bold();
                        header.Cell().Background(Colors.Grey.Lighten3)
                            .Padding(3).Text("Tests").Bold();
                        header.Cell().Background(Colors.Grey.Lighten3)
                            .Padding(3).Text("Status").Bold();
                        header.Cell().Background(Colors.Grey.Lighten3)
                            .Padding(3).Text("P/F/S").Bold();
                    });

                    foreach (var file in session.FileResults)
                    {
                        var bgColor = file.AllPassed ? Colors.Green.Lighten5 :
                            file.Failed > 0 ? Colors.Red.Lighten5 : Colors.Grey.Lighten4;

                        var statusText = file.AllPassed ? "PASS" :
                            file.Failed > 0 ? "FAIL" : "SKIP";

                        table.Cell().Background(bgColor).Padding(3)
                            .Text(file.FileName).FontSize(8);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(file.FileSize).FontSize(8);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text($"{file.ElapsedSeconds:N1}s").FontSize(8);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(FormatSubTests(file)).FontSize(7);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(statusText).Bold().FontSize(8)
                            .FontColor(file.AllPassed ? Colors.Green.Darken2 : Colors.Red.Darken2);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text($"{file.Passed}/{file.Failed}/{file.Skipped}").FontSize(8);
                    }
                });

                page.Footer().AlignCenter()
                    .Text("CHDSharp Tester — generated with QuestPDF").FontSize(7)
                    .FontColor(Colors.Grey.Medium);
            });
        }).GeneratePdf(outputPath);
    }

    private static string FormatSubTests(PerFileResult file)
    {
        var parts = file.SubTests.Select(t =>
            $"{(t.Status == TestStatus.Passed ? "✓" : t.Status == TestStatus.Failed ? "✗" : "○")} {t.TestName}");
        return string.Join("  ", parts);
    }
}
