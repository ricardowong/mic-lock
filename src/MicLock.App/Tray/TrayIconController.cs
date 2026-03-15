using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using MicLock.App.ViewModels;

namespace MicLock.App.Tray;

public class TrayIconController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainViewModel _viewModel;
    private readonly Window _mainWindow;

    private readonly Icon _lockedIcon;
    private readonly Icon _unlockedIcon;

    private readonly ToolStripMenuItem _openItem;
    private readonly ToolStripMenuItem _lockToggleItem;
    private readonly ToolStripMenuItem _targetMenu;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _exitItem;

    public TrayIconController(MainViewModel viewModel, Window mainWindow)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;

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
            System.Windows.Application.Current?.Shutdown();
        };

        contextMenu.Items.Add(_openItem);
        contextMenu.Items.Add(new ToolStripSeparator());
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
        _notifyIcon.Click += (s, e) =>
        {
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
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
            else if (e.PropertyName == nameof(MainViewModel.StartWithWindows))
            {
                _startupItem.Checked = _viewModel.StartWithWindows;
            }
        };
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
        var app = System.Windows.Application.Current;
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

    public void ShowVolumeRestoredNotification(int previous, int restored, string device)
    {
        if (_viewModel.ShowNotifications)
        {
            _notifyIcon.ShowBalloonTip(
                2500,
                "MicLock: Volume Protected",
                $"Blocked attempt to lower mic volume ({previous}% -> {restored}%).",
                ToolTipIcon.Info);
        }
    }

    private static string TruncateTooltip(string text)
    {
        // Windows NotifyIcon.Text max limit is 63 characters (or 127 in modern Win32)
        if (text.Length > 63)
        {
            return text.Substring(0, 60) + "...";
        }
        return text;
    }

    private static Icon LoadIconFromResource(string resourceName, Icon fallback)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/{resourceName}", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo?.Stream != null)
            {
                return new Icon(streamInfo.Stream);
            }
        }
        catch { }

        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(baseDir, "Assets", resourceName);
            if (File.Exists(localPath))
            {
                return new Icon(localPath);
            }
        }
        catch { }

        return fallback;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _lockedIcon.Dispose();
        _unlockedIcon.Dispose();
        GC.SuppressFinalize(this);
    }
}
