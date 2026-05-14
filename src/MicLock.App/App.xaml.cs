using System.IO;
using System.Windows;
using MicLock.App.Tray;
using MicLock.App.ViewModels;
using MicLock.Core.Audio;
using MicLock.Core.Config;
using MicLock.Core.Sys;
using MicLock.Core.UI;
using Application = System.Windows.Application;

namespace MicLock.App;

public partial class App : Application
{
    private SingleInstanceManager? _singleInstanceManager;
    private AppIpcServer? _ipcServer;
    private GlobalHotkeyManager? _hotkeyManager;
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

        bool isToggleMuteReq = e.Args.Any(a => a.Equals("--toggle-mute", StringComparison.OrdinalIgnoreCase)
                                            || a.Equals("-m", StringComparison.OrdinalIgnoreCase)
                                            || a.Equals("/mute", StringComparison.OrdinalIgnoreCase));

        bool forceShow = e.Args.Any(a => a.Equals("--show", StringComparison.OrdinalIgnoreCase)
                                      || a.Equals("--window", StringComparison.OrdinalIgnoreCase));

        // 1. Single Instance Check & IPC Dispatch
        _singleInstanceManager = new SingleInstanceManager();
        if (!_singleInstanceManager.TryAcquire())
        {
            if (isToggleMuteReq)
            {
                AppIpcClient.SendCommand("TOGGLE_MUTE");
            }
            else if (forceShow)
            {
                AppIpcClient.SendCommand("SHOW");
            }
            else
            {
                AppIpcClient.SendCommand("SHOW");
            }

            Shutdown();
            return;
        }

        // 2. Start IPC Server for subsequent instances (PowerToys / CLI)
        _ipcServer = new AppIpcServer();
        _ipcServer.CommandReceived += OnIpcCommandReceived;
        _ipcServer.Start();

        // 3. Initialize Core Services
        var settingsManager = new SettingsManager();
        var startupManager = new StartupManager();
        var audioEngine = new AudioLockEngine();

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

        // 6. Initialize Global Hotkey (Default: Ctrl+Shift+Alt+M for SoundSwitch / PowerToys compatibility)
        _hotkeyManager = new GlobalHotkeyManager();
        _hotkeyManager.HotkeyPressed += () =>
        {
            _viewModel.ToggleMute();
        };
        _hotkeyManager.Register();

        // 7. Handle Initial Launch Arguments
        if (isToggleMuteReq)
        {
            _viewModel.ToggleMute();
        }

        bool startMinimized = !forceShow && (isToggleMuteReq
                              || e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                              || settingsManager.Current.StartMinimized);

        if (!startMinimized)
        {
            _mainWindow.Show();
        }
    }

    private void OnIpcCommandReceived(string command)
    {
        Dispatcher.Invoke(() =>
        {
            switch (command.ToUpperInvariant())
            {
                case "TOGGLE_MUTE":
                    _viewModel?.ToggleMute();
                    break;
                case "SHOW":
                    _trayController?.ShowMainWindow();
                    break;
                case "VOLUME_UP":
                    _viewModel?.AdjustVolume(5);
                    break;
                case "VOLUME_DOWN":
                    _viewModel?.AdjustVolume(-5);
                    break;
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyManager?.Dispose();
        _ipcServer?.Dispose();
        _trayController?.Dispose();
        _viewModel?.Dispose();
        _singleInstanceManager?.Dispose();

        base.OnExit(e);
    }
}
