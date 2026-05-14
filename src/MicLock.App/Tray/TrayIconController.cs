using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using MicLock.Core.UI;
using MicLock.App.ViewModels;
using Application = System.Windows.Application;

namespace MicLock.App.Tray;

public class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainViewModel _viewModel;
    private readonly Window _mainWindow;
    private readonly OsdBannerWindow _osdBanner;
    private readonly TrayWheelHook? _wheelHook;

    private readonly Icon _lockedIcon;
    private readonly Icon _unlockedIcon;

    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _muteToggleItem;
    private readonly ToolStripMenuItem _lockToggleItem;
    private readonly ToolStripMenuItem _targetMenu;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _exitItem;

    public TrayIconController(MainViewModel viewModel, Window mainWindow)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        _osdBanner = new OsdBannerWindow();

        // Load tray icons from embedded WPF resources or fallback to disk
        _lockedIcon = LoadIconFromResource("mic-locked.ico", SystemIcons.Shield);
        _unlockedIcon = LoadIconFromResource("mic-unlocked.ico", SystemIcons.Application);

        var contextMenu = new ContextMenuStrip
        {
            ShowImageMargin = true,
            RenderMode = ToolStripRenderMode.System
        };

        _openItem = new ToolStripMenuItem("Open Dashboard")
        {
            Font = new Font(contextMenu.Font, System.Drawing.FontStyle.Bold)
        };
        _openItem.Click += (s, e) => ShowMainWindow();

        _muteToggleItem = new ToolStripMenuItem("Mute Microphone")
        {
            CheckOnClick = false,
            Checked = _viewModel.IsMuted
        };
        _muteToggleItem.Click += (s, e) => _viewModel.ToggleMute();

        _lockToggleItem = new ToolStripMenuItem("Lock Volume")
        {
            CheckOnClick = true,
            Checked = _viewModel.IsLocked
        };
        _lockToggleItem.Click += (s, e) => _viewModel.IsLocked = _lockToggleItem.Checked;

        _targetMenu = new ToolStripMenuItem("Lock Target");
        int[] presets = [100, 90, 80, 75, 50];
        foreach (int p in presets)
        {
            var item = new ToolStripMenuItem($"{p}%")
            {
                Checked = (_viewModel.TargetVolumePercent == p)
            };
            int presetValue = p;
            item.Click += (s, e) =>
            {
                _viewModel.TargetVolumePercent = presetValue;
                UpdateTargetMenuChecks();
            };
            _targetMenu.DropDownItems.Add(item);
        }

        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = _viewModel.StartWithWindows
        };
        _startupItem.Click += (s, e) => _viewModel.StartWithWindows = _startupItem.Checked;

        _exitItem = new ToolStripMenuItem("Exit MicLock");
        _exitItem.Click += (s, e) =>
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
            }
            Application.Current?.Shutdown();
        };

        contextMenu.Items.Add(_openItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_muteToggleItem);
        contextMenu.Items.Add(_lockToggleItem);
        contextMenu.Items.Add(_targetMenu);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_startupItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _viewModel.IsLocked ? _lockedIcon : _unlockedIcon,
            Text = TruncateTooltip(_viewModel.StatusTooltip),
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
                // Middle-click toggles microphone mute instantly!
                _viewModel.ToggleMute();
            }
        };

        // Install tray mouse wheel hook to adjust volume via scroll
        _wheelHook = new TrayWheelHook(_notifyIcon, OnTrayMouseWheel);

        // Wire Mute & Audio Engine events to OSD Banner
        _viewModel.AudioEngine.MuteChanged += (s, muted) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _muteToggleItem.Checked = muted;
                _muteToggleItem.Text = muted ? "Unmute Microphone" : "Mute Microphone";
                _osdBanner.ShowMuteState(muted, _viewModel.SelectedDevice?.Name);
            });
        };

        // Wire VM property changes to update Tray Icon & Tooltip
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsLocked))
            {
                _notifyIcon.Icon = _viewModel.IsLocked ? _lockedIcon : _unlockedIcon;
                _lockToggleItem.Checked = _viewModel.IsLocked;
                _notifyIcon.Text = TruncateTooltip(_viewModel.StatusTooltip);
            }
            else if (e.PropertyName == nameof(MainViewModel.TargetVolumePercent))
            {
                UpdateTargetMenuChecks();
                _notifyIcon.Text = TruncateTooltip(_viewModel.StatusTooltip);
            }
            else if (e.PropertyName == nameof(MainViewModel.IsMuted))
            {
                _muteToggleItem.Checked = _viewModel.IsMuted;
                _muteToggleItem.Text = _viewModel.IsMuted ? "Unmute Microphone" : "Mute Microphone";
            }
            else if (e.PropertyName == nameof(MainViewModel.StartWithWindows))
            {
                _startupItem.Checked = _viewModel.StartWithWindows;
            }
        };
    }

    private void OnTrayMouseWheel(int delta)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            int step = (delta > 0) ? 5 : -5;
            _viewModel.AdjustVolume(step);

            int current = _viewModel.TargetVolumePercent;
            _notifyIcon.Text = TruncateTooltip($"Microphone: {current}%");

            // Display SoundSwitch-style OSD volume banner
            _osdBanner.ShowVolumeLevel(current, _viewModel.SelectedDevice?.Name);
        });
    }

    private void UpdateTargetMenuChecks()
    {
        foreach (ToolStripItem item in _targetMenu.DropDownItems)
        {
            if (item is ToolStripMenuItem mi && mi.Text != null)
            {
                mi.Checked = mi.Text.StartsWith($"{_viewModel.TargetVolumePercent}%");
            }
        }
    }

    public void ShowMainWindow()
    {
        var app = Application.Current;
        if (app == null) return;

        app.Dispatcher.Invoke(() =>
        {
            if (!_mainWindow.IsVisible)
            {
                _mainWindow.Show();
            }

            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Activate();
            _mainWindow.Focus();
        });
    }

    public void ShowVolumeRestoredNotification(int previousPercent, int restoredPercent, string deviceName)
    {
        if (!_viewModel.ShowNotifications) return;

        _notifyIcon.ShowBalloonTip(
            2000,
            "MicLock Active",
            $"Reverted volume from {previousPercent}% to {restoredPercent}%\n({deviceName})",
            ToolTipIcon.Info);
    }

    private static string TruncateTooltip(string text)
    {
        return text.Length > 63 ? text[..60] + "..." : text;
    }

    private static Icon LoadIconFromResource(string iconName, Icon fallbackIcon)
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = Path.Combine(baseDir, "Assets", iconName);
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }

            string altPath = Path.Combine(baseDir, iconName);
            if (File.Exists(altPath))
            {
                return new Icon(altPath);
            }
        }
        catch { }

        return fallbackIcon;
    }

    public void Dispose()
    {
        _wheelHook?.Dispose();
        _osdBanner.Close();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _lockedIcon.Dispose();
        _unlockedIcon.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Native window subclass to intercept WM_MOUSEWHEEL on the NotifyIcon tray HWND
    /// </summary>
    private sealed class TrayWheelHook : NativeWindow, IDisposable
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private readonly Action<int> _onWheel;

        public TrayWheelHook(NotifyIcon notifyIcon, Action<int> onWheel)
        {
            _onWheel = onWheel;
            try
            {
                var windowField = typeof(NotifyIcon).GetField("window", BindingFlags.NonPublic | BindingFlags.Instance);
                if (windowField?.GetValue(notifyIcon) is NativeWindow nativeWin && nativeWin.Handle != IntPtr.Zero)
                {
                    AssignHandle(nativeWin.Handle);
                }
            }
            catch { }
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

            // In Shell NotifyIcon Version 4 callback: LOWORD(lParam) == WM_MOUSEWHEEL
            int lParam = (int)(m.LParam.ToInt64() & 0xFFFF);
            if (lParam == WM_MOUSEWHEEL)
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                if (delta == 0)
                {
                    delta = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                }
                _onWheel(delta != 0 ? delta : 120);
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            ReleaseHandle();
            GC.SuppressFinalize(this);
        }
    }
}
