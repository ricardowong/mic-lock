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

    public OsdBannerWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(2400)
        };
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            FadeOut();
        };

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Apply Win32 non-activating click-through transparent styles
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);

        Reposition();
    }

    public void Reposition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 20;
        Top = workArea.Bottom - Height - 20;
    }

    public void ShowMuteState(bool isMuted, string? deviceName)
    {
        Dispatcher.Invoke(() =>
        {
            Reposition();

            TitleText.Text = isMuted ? "Microphone Off" : "Microphone On";
            SubtitleText.Text = string.IsNullOrWhiteSpace(deviceName) ? "Microphone" : deviceName;

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

            DisplayBanner();
        });
    }

    public void ShowVolumeLevel(int volumePercent, string? deviceName)
    {
        Dispatcher.Invoke(() =>
        {
            Reposition();

            TitleText.Text = $"Microphone {volumePercent}%";
            SubtitleText.Text = string.IsNullOrWhiteSpace(deviceName) ? "Microphone" : deviceName;

            MicBodyPath.Fill = _tealBrush;
            MuteSlash.Visibility = Visibility.Collapsed;

            DisplayBanner();
        });
    }

    private void DisplayBanner()
    {
        _hideTimer.Stop();

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        // Animate Slide up and Fade In
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
                Hide();
            }
        };

        BeginAnimation(OpacityProperty, fadeOut);
    }
}
