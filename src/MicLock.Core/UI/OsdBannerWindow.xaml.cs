using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MicLock.Core.UI;

public partial class OsdBannerWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly DispatcherTimer _hideTimer;
    private readonly SolidColorBrush _tealBrush = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#06B6D4"));
    private readonly SolidColorBrush _mutedBrush = new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#94A3B8"));

    private const double StandardWidth = 290.0;
    private const double StandardHeight = 84.0;
    private const double CompactWidth = 175.0;
    private const double CompactHeight = 48.0;

    private bool _isCompactMode;
    private string? _lastDeviceName;

    public bool KeepOverlayWhenMuted { get; set; } = false;
    public double OverlayOpacityWhenMuted { get; set; } = 0.70;
    public bool IsCurrentlyMuted { get; private set; }

    public OsdBannerWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2400)
        };
        _hideTimer.Tick += OnHideTimerTick;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyClickThrough();
        Reposition();
    }

    public void ApplyClickThrough()
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle != IntPtr.Zero)
            {
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
            }
        }
        catch { }
    }

    public void Reposition()
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            double w = double.IsNaN(Width) || Width <= 0 ? StandardWidth : Width;
            double h = double.IsNaN(Height) || Height <= 0 ? StandardHeight : Height;
            Left = workArea.Right - w - 20;
            Top = workArea.Bottom - h - 20;
        }
        catch { }
    }

    public void UpdateSettings(bool keepWhenMuted, double opacity)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateSettings(keepWhenMuted, opacity));
            return;
        }

        KeepOverlayWhenMuted = keepWhenMuted;
        OverlayOpacityWhenMuted = Math.Clamp(opacity, 0.10, 1.0);

        if (IsCurrentlyMuted)
        {
            if (KeepOverlayWhenMuted)
            {
                _hideTimer.Stop();
                ApplyCompactBadgeMode(true);
                Opacity = OverlayOpacityWhenMuted;
                if (!IsVisible)
                {
                    try { Show(); } catch { }
                    ApplyClickThrough();
                }
                Reposition();
            }
            else
            {
                FadeOut();
            }
        }
    }

    public void ShowMuteState(bool isMuted, string? deviceName)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowMuteState(isMuted, deviceName));
            return;
        }

        IsCurrentlyMuted = isMuted;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            _lastDeviceName = deviceName;
        }

        if (isMuted)
        {
            MicBodyPath.Fill = _mutedBrush;
            MuteSlash.Visibility = Visibility.Visible;
            TitleText.Text = "Microphone Off";
            SubtitleText.Text = string.IsNullOrWhiteSpace(_lastDeviceName) ? "Microphone" : _lastDeviceName;

            if (KeepOverlayWhenMuted)
            {
                _hideTimer.Stop();
                ApplyCompactBadgeMode(true);
                Opacity = OverlayOpacityWhenMuted;

                if (!IsVisible)
                {
                    try { Show(); } catch { }
                    ApplyClickThrough();
                }
                Reposition();
                return;
            }
        }
        else
        {
            MicBodyPath.Fill = _tealBrush;
            MuteSlash.Visibility = Visibility.Collapsed;
            TitleText.Text = "Microphone On";
            SubtitleText.Text = string.IsNullOrWhiteSpace(_lastDeviceName) ? "Microphone" : _lastDeviceName;
        }

        ApplyCompactBadgeMode(false);
        DisplayBannerTimed();
    }

    public void ShowVolumeLevel(int volumePercent, string? deviceName)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowVolumeLevel(volumePercent, deviceName));
            return;
        }

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            _lastDeviceName = deviceName;
        }

        ApplyCompactBadgeMode(false);
        TitleText.Text = $"Microphone {volumePercent}%";
        SubtitleText.Text = string.IsNullOrWhiteSpace(_lastDeviceName) ? "Microphone" : _lastDeviceName;

        MicBodyPath.Fill = _tealBrush;
        MuteSlash.Visibility = Visibility.Collapsed;

        DisplayBannerTimed();
    }

    public void ShowDeviceSwitched(string deviceName, bool isMuted)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowDeviceSwitched(deviceName, isMuted));
            return;
        }

        IsCurrentlyMuted = isMuted;
        _lastDeviceName = deviceName;

        ApplyCompactBadgeMode(false);
        TitleText.Text = isMuted ? "Microphone Off" : "Active Microphone";
        SubtitleText.Text = deviceName;

        if (isMuted)
        {
            MicBodyPath.Fill = _mutedBrush;
            MuteSlash.Visibility = Visibility.Visible;
        }
        else
        {
            MicBodyPath.Fill = _tealBrush;
            MuteSlash.Visibility = Visibility.Collapsed;
        }

        DisplayBannerTimed();
    }

    private void ApplyCompactBadgeMode(bool compact)
    {
        _isCompactMode = compact;
        if (compact)
        {
            Width = CompactWidth;
            Height = CompactHeight;
            CardBorder.Padding = new Thickness(10, 6, 10, 6);
            CardBorder.CornerRadius = new CornerRadius(6);
            IconContainer.Width = 22;
            IconContainer.Height = 22;
            IconContainer.Margin = new Thickness(0, 0, 8, 0);
            MicBodyPath.Width = 18;
            MicBodyPath.Height = 18;
            TitleText.FontSize = 13;
            SubtitleText.Visibility = Visibility.Collapsed;
        }
        else
        {
            Width = StandardWidth;
            Height = StandardHeight;
            CardBorder.Padding = new Thickness(16, 12, 16, 12);
            CardBorder.CornerRadius = new CornerRadius(8);
            IconContainer.Width = 32;
            IconContainer.Height = 32;
            IconContainer.Margin = new Thickness(0, 0, 14, 0);
            MicBodyPath.Width = 22;
            MicBodyPath.Height = 22;
            TitleText.FontSize = 16;
            SubtitleText.Visibility = Visibility.Visible;
        }

        Reposition();
    }

    private void DisplayBannerTimed()
    {
        _hideTimer.Stop();

        if (!IsVisible)
        {
            Opacity = 0;
            try { Show(); } catch { }
            ApplyClickThrough();
        }

        var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var slideIn = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        BannerTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
        BeginAnimation(OpacityProperty, fadeIn);

        _hideTimer.Start();
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();

        if (IsCurrentlyMuted && KeepOverlayWhenMuted)
        {
            ApplyCompactBadgeMode(true);
            TitleText.Text = "Microphone Off";
            MicBodyPath.Fill = _mutedBrush;
            MuteSlash.Visibility = Visibility.Visible;

            var fadeToBadge = new DoubleAnimation(OverlayOpacityWhenMuted, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            BeginAnimation(OpacityProperty, fadeToBadge);
            Reposition();
        }
        else
        {
            FadeOut();
        }
    }

    private void FadeOut()
    {
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) =>
        {
            if (Opacity == 0.0)
            {
                try { Hide(); } catch { }
            }
        };

        BeginAnimation(OpacityProperty, fadeOut);
    }
}
