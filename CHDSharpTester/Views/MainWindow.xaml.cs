using System.ComponentModel;
using System.Windows;
using CHDSharpTester.ViewModels;

namespace CHDSharpTester.Views;

internal partial class MainWindow
{
    private bool _isClosing;

    /// <summary>Initializes a new instance of the <see cref="MainWindow"/> WPF window.</summary>
    public MainWindow()
    {
        InitializeComponent();
        MainPageView.DataContext = new MainViewModel();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        try
        {
            if (_isClosing)
            {
                base.OnClosing(e);
                return;
            }

            if (MainPageView.DataContext is MainViewModel { IsRunning: true } vm)
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

                _isClosing = true;
                e.Cancel = true;
                await vm.CancelAndShutdownAsync();
                Close();
                return;
            }

            base.OnClosing(e);
        }
        catch (Exception)
        {
            // ignore
        }
    }
}
