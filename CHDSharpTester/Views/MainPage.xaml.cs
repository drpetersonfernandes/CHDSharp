using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CHDSharpTester.Models;
using CHDSharpTester.ViewModels;

namespace CHDSharpTester.Views;

public partial class MainPage
{
    public MainPage()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}

public class StatusIconConverter : IValueConverter
{
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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
