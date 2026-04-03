using System.IO;
using System.Windows;
using MicLock.App.Tray;
using MicLock.App.ViewModels;
using MicLock.Core.Audio;
using MicLock.Core.Config;
using MicLock.Core.System;
using Application = System.Windows.Application;

namespace MicLock.App;

public partial class App : Application
{
    private SingleInstanceManager? _singleInstanceManager;
    private TrayIconController? _trayController;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Single Instance Check
        _singleInstanceManager = new SingleInstanceManager();
        if (!_singleInstanceManager.TryAcquire())
        {
            System.Windows.MessageBox.Show(
                "MicLock is already running in the Windows System Tray.",
                "MicLock",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Shutdown();
            return;
        }

        // 2. Initialize Core Services
        var settingsManager = new SettingsManager();
        var startupManager = new StartupManager();
        var audioEngine = new AudioLockEngine();

        // 3. Sync Startup Registry with Settings
        if (settingsManager.Current.StartWithWindows)
        {
            startupManager.SetStartup(true);
        }

        // 4. Initialize ViewModel & View
        _viewModel = new MainViewModel(audioEngine, settingsManager, startupManager);
        _mainWindow = new MainWindow(_viewModel);
        MainWindow = _mainWindow;

        // 5. Initialize System Tray Controller
        _trayController = new TrayIconController(_viewModel, _mainWindow);
        audioEngine.VolumeRestored += (s, args) =>
        {
            _trayController.ShowVolumeRestoredNotification(args.PreviousPercent, args.RestoredPercent, args.DeviceName);
        };

        // 6. Handle Launch Mode (Silent Tray vs Open Window)
        bool startMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                              || settingsManager.Current.StartMinimized;

        if (!startMinimized)
        {
            _mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayController?.Dispose();
        _viewModel?.Dispose();
        _singleInstanceManager?.Dispose();

        base.OnExit(e);
    }
}
