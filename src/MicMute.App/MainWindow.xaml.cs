using System.Windows;
using System.Windows.Media;
using MicLock.Core.Audio;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace MicMute.App;

public partial class MainWindow : Window
{
    private readonly AudioLockEngine _audioEngine;

    private readonly SolidColorBrush _activeBrush = new((MediaColor)MediaColorConverter.ConvertFromString("#0D9488"));
    private readonly SolidColorBrush _activeBg = new((MediaColor)MediaColorConverter.ConvertFromString("#F0FDFA"));
    private readonly SolidColorBrush _activeBorder = new((MediaColor)MediaColorConverter.ConvertFromString("#CCFBF1"));

    private readonly SolidColorBrush _mutedBrush = new((MediaColor)MediaColorConverter.ConvertFromString("#EF4444"));
    private readonly SolidColorBrush _mutedBg = new((MediaColor)MediaColorConverter.ConvertFromString("#FEF2F2"));
    private readonly SolidColorBrush _mutedBorder = new((MediaColor)MediaColorConverter.ConvertFromString("#FECACA"));

    public MainWindow(AudioLockEngine audioEngine)
    {
        InitializeComponent();
        _audioEngine = audioEngine;

        _audioEngine.MuteChanged += (s, muted) => UpdateUi(muted);
        _audioEngine.ActiveDeviceChanged += (s, device) =>
        {
            Dispatcher.Invoke(() =>
            {
                DeviceLabel.Text = device?.Name ?? "Default Microphone";
            });
        };

        UpdateUi(_audioEngine.IsMuted);
        DeviceLabel.Text = _audioEngine.ActiveDevice?.Name ?? "Default Microphone";
    }

    private void UpdateUi(bool isMuted)
    {
        Dispatcher.Invoke(() =>
        {
            if (isMuted)
            {
                StatusPill.Background = _mutedBg;
                StatusPill.BorderBrush = _mutedBorder;
                StatusDot.Fill = _mutedBrush;
                StatusLabel.Text = "MUTED";
                StatusLabel.Foreground = _mutedBrush;

                ToggleMuteBtn.Background = _mutedBrush;
                ToggleMuteBtn.BorderBrush = _mutedBorder;
                BigMuteSlash.Visibility = Visibility.Visible;
                ActionLabel.Text = "CLICK TO UNMUTE";
                ActionLabel.Foreground = _mutedBrush;
            }
            else
            {
                StatusPill.Background = _activeBg;
                StatusPill.BorderBrush = _activeBorder;
                StatusDot.Fill = _activeBrush;
                StatusLabel.Text = "ACTIVE";
                StatusLabel.Foreground = _activeBrush;

                ToggleMuteBtn.Background = _activeBrush;
                ToggleMuteBtn.BorderBrush = _activeBorder;
                BigMuteSlash.Visibility = Visibility.Collapsed;
                ActionLabel.Text = "CLICK TO MUTE";
                ActionLabel.Foreground = _activeBrush;
            }
        });
    }

    private void OnToggleMuteClick(object sender, RoutedEventArgs e)
    {
        _audioEngine.ToggleMute();
    }
}
