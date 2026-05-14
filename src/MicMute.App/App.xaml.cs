using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using MicLock.Core.Audio;
using MicLock.Core.Sys;
using MicLock.Core.UI;
using Application = System.Windows.Application;

namespace MicMute.App;

public partial class App : Application
{
    private const string MutexName = @"Global\MicMute_SingleInstance_Mutex_99182a";
    private const string PipeName = "MicMute_IPC_Pipe_99182a";

    private SingleInstanceManager? _singleInstanceManager;
    private AppIpcServer? _ipcServer;
    private GlobalHotkeyManager? _hotkeyManager;
    private AudioLockEngine? _audioEngine;
    private OsdBannerWindow? _osdBanner;
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private NativeWindow? _wheelHook;

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
        if (!_singleInstanceManager.TryAcquire(MutexName))
        {
            if (isToggleMuteReq)
            {
                AppIpcClient.SendCommand("TOGGLE_MUTE", PipeName);
            }
            else
            {
                AppIpcClient.SendCommand("SHOW", PipeName);
            }

            Shutdown();
            return;
        }

        // 2. Start IPC Server for CLI / PowerToys invocations
        _ipcServer = new AppIpcServer(PipeName);
        _ipcServer.CommandReceived += OnIpcCommandReceived;
        _ipcServer.Start();

        // 3. Initialize Audio Engine & OSD Banner
        _audioEngine = new AudioLockEngine { IsLocked = false };
        _audioEngine.SelectDevice(null);
        _osdBanner = new OsdBannerWindow();

        // 4. Initialize Main Window
        _mainWindow = new MainWindow(_audioEngine);
        MainWindow = _mainWindow;

        // 5. Initialize System Tray Icon
        InitTrayIcon();

        // 6. Global Hotkey (Ctrl + Shift + Alt + M)
        _hotkeyManager = new GlobalHotkeyManager();
        _hotkeyManager.HotkeyPressed += () => _audioEngine.ToggleMute();
        _hotkeyManager.Register();

        // 7. Wire OSD Banner to Audio Mute Events
        _audioEngine.MuteChanged += (s, muted) =>
        {
            Dispatcher.Invoke(() =>
            {
                _osdBanner.ShowMuteState(muted, _audioEngine.ActiveDevice?.Name);
                UpdateTrayTooltip(muted);
            });
        };

        if (isToggleMuteReq)
        {
            _audioEngine.ToggleMute();
        }

        bool startMinimized = !forceShow && (isToggleMuteReq
                              || e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)));

        if (!startMinimized)
        {
            _mainWindow.Show();
        }
    }

    private void InitTrayIcon()
    {
        var contextMenu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
        var openItem = new ToolStripMenuItem("Open MicMute") { Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold) };
        openItem.Click += (s, e) => ShowMainWindow();

        var toggleItem = new ToolStripMenuItem("Toggle Mute");
        toggleItem.Click += (s, e) => _audioEngine?.ToggleMute();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (s, e) =>
        {
            if (_notifyIcon != null) _notifyIcon.Visible = false;
            Shutdown();
        };

        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(toggleItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "MicMute: Active",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        _notifyIcon.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
            else if (e.Button == MouseButtons.Middle)
            {
                _audioEngine?.ToggleMute();
            }
        };

        // Tray mouse wheel volume adjustment hook
        try
        {
            var windowField = typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
            if (windowField?.GetValue(_notifyIcon) is NativeWindow nativeWin && nativeWin.Handle != IntPtr.Zero)
            {
                _wheelHook = new MicMuteWheelHook(nativeWin.Handle, OnTrayWheel);
            }
        }
        catch { }
    }

    private void OnTrayWheel(int delta)
    {
        if (_audioEngine == null) return;
        Dispatcher.Invoke(() =>
        {
            int step = (delta > 0) ? 5 : -5;
            int newVol = Math.Clamp(_audioEngine.CurrentVolumePercent + step, 0, 100);
            _audioEngine.SetVolume(newVol);

            _osdBanner?.ShowVolumeLevel(newVol, _audioEngine.ActiveDevice?.Name);
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = $"Microphone: {newVol}%";
            }
        });
    }

    private void UpdateTrayTooltip(bool isMuted)
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Text = isMuted ? "MicMute: Muted" : "MicMute: Active";
        }
    }

    private void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow == null) return;
            if (!_mainWindow.IsVisible) _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    private void OnIpcCommandReceived(string command)
    {
        Dispatcher.Invoke(() =>
        {
            switch (command.ToUpperInvariant())
            {
                case "TOGGLE_MUTE":
                    _audioEngine?.ToggleMute();
                    break;
                case "SHOW":
                    ShowMainWindow();
                    break;
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _wheelHook?.ReleaseHandle();
        _hotkeyManager?.Dispose();
        _ipcServer?.Dispose();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        _osdBanner?.Close();
        _audioEngine?.Dispose();
        _singleInstanceManager?.Dispose();

        base.OnExit(e);
    }

    private sealed class MicMuteWheelHook : NativeWindow
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private readonly Action<int> _onWheel;

        public MicMuteWheelHook(IntPtr hwnd, Action<int> onWheel)
        {
            _onWheel = onWheel;
            AssignHandle(hwnd);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEWHEEL)
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                _onWheel(delta);
                m.Result = IntPtr.Zero;
                return;
            }

            int lParam = (int)(m.LParam.ToInt64() & 0xFFFF);
            if (lParam == WM_MOUSEWHEEL)
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                if (delta == 0) delta = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                _onWheel(delta != 0 ? delta : 120);
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
