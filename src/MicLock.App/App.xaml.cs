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
    private TrayIconController? _trayController;
    private MainViewModel? _viewModel;
    private MainWindow? _mainWindow;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                string errPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicLock", "unhandled_dispatcher_error.log");
                File.WriteAllText(errPath, args.Exception.ToString());
            }
            catch { }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            try
            {
                string errPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicLock", "unhandled_domain_error.log");
                File.WriteAllText(errPath, args.ExceptionObject?.ToString() ?? "Unknown domain error");
            }
            catch { }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicLock");
        Directory.CreateDirectory(logDir);
        string debugLog = Path.Combine(logDir, "startup_trace.log");
        void Log(string msg) => File.AppendAllText(debugLog, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");

        Log("OnStartup entered");

        try
        {
            bool isToggleMuteReq = e.Args.Any(a => a.Equals("--toggle-mute", StringComparison.OrdinalIgnoreCase)
                                                || a.Equals("-m", StringComparison.OrdinalIgnoreCase)
                                                || a.Equals("/mute", StringComparison.OrdinalIgnoreCase));

            bool isCycleDeviceReq = e.Args.Any(a => a.Equals("--cycle-device", StringComparison.OrdinalIgnoreCase)
                                                 || a.Equals("--cycle-mic", StringComparison.OrdinalIgnoreCase)
                                                 || a.Equals("-c", StringComparison.OrdinalIgnoreCase));

            bool forceShow = e.Args.Any(a => a.Equals("--show", StringComparison.OrdinalIgnoreCase)
                                          || a.Equals("--window", StringComparison.OrdinalIgnoreCase));

            Log($"Args evaluated: isToggleMuteReq={isToggleMuteReq}, isCycleDeviceReq={isCycleDeviceReq}, forceShow={forceShow}");

            // 1. Single Instance Check & IPC Dispatch
            _singleInstanceManager = new SingleInstanceManager();
            bool acquired = _singleInstanceManager.TryAcquire();
            Log($"TryAcquire: {acquired}");
            if (!acquired)
            {
                if (isToggleMuteReq)
                {
                    AppIpcClient.SendCommand("TOGGLE_MUTE");
                }
                else if (isCycleDeviceReq)
                {
                    AppIpcClient.SendCommand("CYCLE_DEVICE");
                }
                else if (e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
                {
                    Log("Not first instance and --minimized requested. Exiting silently without showing window.");
                }
                else if (forceShow)
                {
                    AppIpcClient.SendCommand("SHOW");
                }
                else
                {
                    AppIpcClient.SendCommand("SHOW");
                }

                Log("Not first instance, dispatched IPC and shutting down");
                Shutdown();
                return;
            }

            // 2. Start IPC Server for subsequent instances (CLI / External hotkeys)
            Log("Starting IPC Server");
            _ipcServer = new AppIpcServer();
            _ipcServer.CommandReceived += OnIpcCommandReceived;
            _ipcServer.Start();

            // 3. Initialize Core Services
            Log("Initializing Core Services");
            var settingsManager = new SettingsManager();
            var startupManager = new StartupManager();
            var audioEngine = new AudioLockEngine();

            if (settingsManager.Current.StartWithWindows)
            {
                startupManager.SetStartup(true);
            }

            // 4. Initialize ViewModel & View
            Log("Initializing ViewModel & View");
            _viewModel = new MainViewModel(audioEngine, settingsManager, startupManager);
            _mainWindow = new MainWindow(_viewModel);
            MainWindow = _mainWindow;

            // 5. Initialize System Tray Controller
            Log("Initializing Tray Controller");
            _trayController = new TrayIconController(_viewModel, _mainWindow);
            audioEngine.VolumeRestored += (s, args) =>
            {
                _trayController.ShowVolumeRestoredNotification(args.PreviousPercent, args.RestoredPercent, args.DeviceName);
            };

            // 6. Handle Initial Launch Arguments
            if (isToggleMuteReq)
            {
                _viewModel.ToggleMute();
            }
            else if (isCycleDeviceReq)
            {
                _viewModel.CycleRecordingDevice();
            }

            bool startMinimized = !forceShow && (isToggleMuteReq
                                  || isCycleDeviceReq
                                  || settingsManager.Current.StartMinimized
                                  || e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)));

            Log($"startMinimized: {startMinimized}");
            if (!startMinimized)
            {
                Log("Calling _mainWindow.Show()");
                _mainWindow.Show();
                _mainWindow.Activate();
            }
            Log("OnStartup finished successfully");
        }
        catch (Exception ex)
        {
            Log($"Exception: {ex}");
            File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicLock", "startup_error.log"), ex.ToString());
            throw;
        }
    }

    private void OnIpcCommandReceived(string command)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                switch (command.ToUpperInvariant())
                {
                    case "TOGGLE_MUTE":
                        _viewModel?.ToggleMute();
                        break;
                    case "CYCLE_DEVICE":
                    case "CYCLE_MIC":
                        _viewModel?.CycleRecordingDevice();
                        break;
                    case "LOCK_VOLUME":
                        if (_viewModel != null)
                        {
                            _viewModel.IsLocked = true;
                            _viewModel.AudioEngine.SetVolume(_viewModel.TargetVolumePercent);
                        }
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
            }
            catch (Exception ex)
            {
                string errPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicLock", "ipc_error.log");
                File.WriteAllText(errPath, ex.ToString());
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicLock");
        string debugLog = Path.Combine(logDir, "startup_trace.log");
        File.AppendAllText(debugLog, $"{DateTime.Now:HH:mm:ss.fff} OnExit entered with exitCode={e.ApplicationExitCode}\n");
        File.AppendAllText(debugLog, $"{Environment.StackTrace}\n");

        _ipcServer?.Dispose();
        _trayController?.Dispose();
        _viewModel?.Dispose();
        _singleInstanceManager?.Dispose();

        base.OnExit(e);
    }
}
