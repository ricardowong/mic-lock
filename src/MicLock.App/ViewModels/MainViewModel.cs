using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MicLock.Core.Audio;
using MicLock.Core.Config;
using MicLock.Core.Sys;

namespace MicLock.App.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAudioLockEngine _audioEngine;
    private readonly SettingsManager _settingsManager;
    private readonly StartupManager _startupManager;
    private readonly Dispatcher _dispatcher;

    private AudioDeviceInfo? _selectedDevice;
    private int _targetVolumePercent;
    private int _currentVolumePercent;
    private bool _isLocked;
    private bool _isMuted;
    private bool _startWithWindows;
    private bool _startMinimized;
    private bool _showNotifications;
    private int _preventionCount;

    public ObservableCollection<AudioDeviceInfo> AvailableDevices { get; } = new();
    public ObservableCollection<ActivityLogEntry> ActivityLogs { get; } = new();

    public IAudioLockEngine AudioEngine => _audioEngine;

    public ICommand SetPresetCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand ToggleMuteCommand { get; }

    public MainViewModel(IAudioLockEngine audioEngine, SettingsManager settingsManager, StartupManager startupManager)
    {
        _audioEngine = audioEngine;
        _settingsManager = settingsManager;
        _startupManager = startupManager;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        SetPresetCommand = new RelayCommand<string>(ExecuteSetPreset);
        RefreshDevicesCommand = new RelayCommand(_ => RefreshDevices());
        ClearLogsCommand = new RelayCommand(_ => ActivityLogs.Clear());
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute());

        // Load persisted settings
        var settings = _settingsManager.Current;
        _targetVolumePercent = settings.TargetVolumePercent;
        _isLocked = settings.IsLockEnabled;
        _startWithWindows = settings.StartWithWindows;
        _startMinimized = settings.StartMinimized;
        _showNotifications = settings.ShowNotifications;

        // Wire up audio engine events
        _audioEngine.VolumeChanged += OnAudioEngineVolumeChanged;
        _audioEngine.MuteChanged += OnAudioEngineMuteChanged;
        _audioEngine.VolumeRestored += OnAudioEngineVolumeRestored;
        _audioEngine.ActiveDeviceChanged += OnAudioEngineActiveDeviceChanged;
        _audioEngine.LogEntryAdded += OnAudioEngineLogEntryAdded;

        // Initialize audio engine
        _audioEngine.TargetVolumePercent = _targetVolumePercent;
        _audioEngine.IsLocked = _isLocked;

        RefreshDevices();
    }

    public AudioDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                if (value != null)
                {
                    _settingsManager.Current.SelectedDeviceId = value.IsDefault ? "Default" : value.Id;
                    _settingsManager.Current.SelectedDeviceName = value.Name;
                    _settingsManager.Save();

                    _audioEngine.SelectDevice(_settingsManager.Current.SelectedDeviceId);
                }
            }
        }
    }

    public int TargetVolumePercent
    {
        get => _targetVolumePercent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (SetProperty(ref _targetVolumePercent, clamped))
            {
                _audioEngine.TargetVolumePercent = clamped;
                _settingsManager.Current.TargetVolumePercent = clamped;
                _settingsManager.Save();

                if (_isLocked)
                {
                    _audioEngine.SetVolume(clamped);
                }

                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusTooltip));
            }
        }
    }

    public int CurrentVolumePercent
    {
        get => _currentVolumePercent;
        private set => SetProperty(ref _currentVolumePercent, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (SetProperty(ref _isLocked, value))
            {
                _audioEngine.IsLocked = value;
                _settingsManager.Current.IsLockEnabled = value;
                _settingsManager.Save();

                if (value)
                {
                    _audioEngine.SetVolume(_targetVolumePercent);
                }

                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusBackgroundBrush));
                OnPropertyChanged(nameof(StatusForegroundBrush));
                OnPropertyChanged(nameof(StatusBorderBrush));
                OnPropertyChanged(nameof(StatusTooltip));
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                _audioEngine.SetMute(value);
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusTooltip));
            }
        }
    }

    public void ToggleMute()
    {
        bool newState = _audioEngine.ToggleMute();
        _isMuted = newState;
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusTooltip));
    }

    public void AdjustVolume(int deltaPercent)
    {
        int newVol = Math.Clamp(TargetVolumePercent + deltaPercent, 0, 100);
        TargetVolumePercent = newVol;
        if (!_isLocked)
        {
            _audioEngine.SetVolume(newVol);
        }
    }

    private void OnAudioEngineMuteChanged(object? sender, bool muted)
    {
        _dispatcher.Invoke(() =>
        {
            _isMuted = muted;
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusTooltip));
        });
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                _settingsManager.Current.StartWithWindows = value;
                _settingsManager.Save();
                _startupManager.SetStartup(value);
            }
        }
    }

    public bool StartMinimized
    {
        get => _startMinimized;
        set
        {
            if (SetProperty(ref _startMinimized, value))
            {
                _settingsManager.Current.StartMinimized = value;
                _settingsManager.Save();
            }
        }
    }

    public bool ShowNotifications
    {
        get => _showNotifications;
        set
        {
            if (SetProperty(ref _showNotifications, value))
            {
                _settingsManager.Current.ShowNotifications = value;
                _settingsManager.Save();
            }
        }
    }

    public int PreventionCount
    {
        get => _preventionCount;
        private set => SetProperty(ref _preventionCount, value);
    }

    public string StatusText => _isLocked ? "Active" : "Paused";

    public System.Windows.Media.Brush StatusBrush => _isLocked
        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 148, 136)) // Teal 600
        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184)); // Slate 400

    public System.Windows.Media.Brush StatusBackgroundBrush => _isLocked
        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 253, 250)) // Teal 50
        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(241, 245, 249)); // Slate 100

    public System.Windows.Media.Brush StatusForegroundBrush => _isLocked
        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(15, 118, 110)) // Teal 700
        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139)); // Slate 500

    public System.Windows.Media.Brush StatusBorderBrush => _isLocked
        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 251, 241)) // Teal 200
        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240)); // Slate 200

    public string StatusTooltip => _isLocked
        ? $"MicLock: Locked at {_targetVolumePercent}% | {_audioEngine.ActiveDevice?.DisplayName ?? "Default Mic"}"
        : "MicLock: Protection Paused";

    public void RefreshDevices()
    {
        var devices = _audioEngine.GetAvailableCaptureDevices();
        _dispatcher.Invoke(() =>
        {
            AvailableDevices.Clear();
            foreach (var dev in devices)
            {
                AvailableDevices.Add(dev);
            }

            // Re-select active device in combobox
            string? targetId = _settingsManager.Current.SelectedDeviceId;
            if (string.IsNullOrWhiteSpace(targetId) || targetId.Equals("Default", StringComparison.OrdinalIgnoreCase))
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IsDefault) ?? AvailableDevices.FirstOrDefault();
            }
            else
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.Id == targetId)
                    ?? AvailableDevices.FirstOrDefault(d => d.IsDefault)
                    ?? AvailableDevices.FirstOrDefault();
            }
        });
    }

    private void ExecuteSetPreset(string? param)
    {
        if (int.TryParse(param, out int preset))
        {
            TargetVolumePercent = preset;
        }
    }

    private void OnAudioEngineVolumeChanged(object? sender, int volume)
    {
        _dispatcher.Invoke(() =>
        {
            CurrentVolumePercent = volume;
            IsMuted = _audioEngine.IsMuted;
        });
    }

    private void OnAudioEngineVolumeRestored(object? sender, VolumeRestoredEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            PreventionCount++;
            CurrentVolumePercent = e.RestoredPercent;
        });
    }

    private void OnAudioEngineActiveDeviceChanged(object? sender, AudioDeviceInfo? device)
    {
        _dispatcher.Invoke(() =>
        {
            if (device != null)
            {
                CurrentVolumePercent = device.CurrentVolumePercent;
                IsMuted = device.IsMuted;
                OnPropertyChanged(nameof(StatusTooltip));
            }
        });
    }

    private void OnAudioEngineLogEntryAdded(object? sender, ActivityLogEntry entry)
    {
        _dispatcher.Invoke(() =>
        {
            ActivityLogs.Insert(0, entry);
            if (ActivityLogs.Count > _settingsManager.Current.MaxLogEntries)
            {
                ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _audioEngine.VolumeChanged -= OnAudioEngineVolumeChanged;
        _audioEngine.VolumeRestored -= OnAudioEngineVolumeRestored;
        _audioEngine.ActiveDeviceChanged -= OnAudioEngineActiveDeviceChanged;
        _audioEngine.LogEntryAdded -= OnAudioEngineLogEntryAdded;
        _audioEngine.Dispose();
        GC.SuppressFinalize(this);
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
    public void Execute(object? parameter) => _execute((T?)parameter);
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
