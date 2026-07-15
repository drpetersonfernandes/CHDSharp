using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace CHDSharpTester.Views;

internal partial class AboutWindow
{
    internal AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersionTextBlock.Text = $"Version: {version?.ToString() ?? "Unknown"}";

        DescriptionTextBlock.Text =
            "A WPF desktop application for batch-testing CHD (Compressed Hunks of Data) files " +
            "using the CHDSharp library. Cross-checks the C# CHD reader against the official " +
            "MAME chdman tool, with support for header verification, deep decompression, " +
            "SHA1 integrity checks, random-access byte-range comparison, zstd/cdzs codec " +
            "tests, and parent/child delta chain validation.";

        GitHubLink.NavigateUri = new Uri("https://github.com/drpetersonfernandes/CHDSharp");
        WebLink.NavigateUri = new Uri("https://www.purelogiccode.com");
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open link: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        e.Handled = true;
    }
}
