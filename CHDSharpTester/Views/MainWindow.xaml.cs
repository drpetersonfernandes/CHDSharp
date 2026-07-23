using System.ComponentModel;
using System.Windows;
using CHDSharpTester.ViewModels;

namespace CHDSharpTester.Views;

internal partial class MainWindow
{
    /// <summary>Initializes a new instance of the <see cref="MainWindow"/> WPF window.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (MainPageView.DataContext is MainViewModel { IsRunning: true })
        {
            var result = MessageBox.Show(
                "A test run is currently in progress. Are you sure you want to exit?",
                "Tests Running",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        base.OnClosing(e);
    }
}
