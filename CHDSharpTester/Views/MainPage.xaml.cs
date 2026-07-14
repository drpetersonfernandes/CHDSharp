using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CHDSharpTester.Models;
using CHDSharpTester.ViewModels;

namespace CHDSharpTester.Views;

/// <summary>The main page of the CHDSharp Tester, serving as the primary content for the <see cref="MainWindow"/>.</summary>
public partial class MainPage
{
    /// <summary>Initializes a new instance of the <see cref="MainPage"/> class and sets the data context to a new <see cref="MainViewModel"/>.</summary>
    public MainPage()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}

/// <summary>Converts a <see cref="TestStatus"/> enum value to a display icon string for the WPF view.</summary>
public class StatusIconConverter : IValueConverter
{
    /// <summary>Converts a <see cref="TestStatus"/> value to a single-character status icon.</summary>
    /// <param name="value">A <see cref="TestStatus"/> value.</param>
    /// <param name="targetType">The target type (ignored).</param>
    /// <param name="parameter">An optional converter parameter (ignored).</param>
    /// <param name="culture">The culture to use (ignored).</param>
    /// <returns>A string containing a checkmark, cross, circle, or question mark depending on the status.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is TestStatus status
            ? status switch
            {
                TestStatus.Passed => "✓",
                TestStatus.Failed => "✗",
                TestStatus.Skipped => "○",
                _ => "?"
            }
            : "?";
    }

    /// <summary>Converting back is not supported.</summary>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
