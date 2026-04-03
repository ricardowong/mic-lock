using System.ComponentModel;
using System.Windows;
using MicLock.App.ViewModels;

namespace MicLock.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isExplicitExit;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    public void RequestExplicitExit()
    {
        _isExplicitExit = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExplicitExit)
        {
            // Instead of quitting, minimize to system notification area (tray)
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }
}
