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

public class KeyRemapActionItem
{
    public KeyRemapAction Action { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}

public class RemapPresetItem
{
    public string DisplayName { get; set; } = string.Empty;
    public uint VirtualKey { get; set; }
    public bool ModifiersCtrl { get; set; }
    public bool ModifiersAlt { get; set; }
    public bool ModifiersShift { get; set; }
    public bool ModifiersWin { get; set; }
    public string ComboText { get; set; } = string.Empty;

    public override string ToString() => DisplayName;
}

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAudioLockEngine _audioEngine;
    private readonly SettingsManager _settingsManager;
    private readonly StartupManager _startupManager;
    private readonly KeyRemapManager _keyRemapManager;
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

    // SoundSwitch parity settings
    private bool _keepOverlayWhenMuted;
    private double _overlayOpacityWhenMuted;

    // Key remap UI state
    private KeyRemapActionItem _selectedRemapAction;
    private string _currentRemapBindingText = "None";
    private bool _isRecordingKey;
    private RemapPresetItem? _selectedPreset;

    public ObservableCollection<AudioDeviceInfo> AvailableDevices { get; } = new();
    public ObservableCollection<ActivityLogEntry> ActivityLogs { get; } = new();
    public IReadOnlyList<KeyRemapActionItem> RemapActions { get; }
    public IReadOnlyList<RemapPresetItem> RemapPresets { get; }

    public IAudioLockEngine AudioEngine => _audioEngine;
    public KeyRemapManager KeyRemapManager => _keyRemapManager;

    public ICommand SetPresetCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand CycleRecordingDeviceCommand { get; }
    public ICommand StartRecordKeyCommand { get; }
    public ICommand CancelRecordKeyCommand { get; }
    public ICommand ClearKeyBindingCommand { get; }

    public event EventHandler<AudioDeviceInfo>? DeviceSwitched;
    public event EventHandler<int>? VolumeLockEnforced;
    public event EventHandler? OverlaySettingsChanged;

    public MainViewModel(IAudioLockEngine audioEngine, SettingsManager settingsManager, StartupManager startupManager, KeyRemapManager? keyRemapManager = null)
    {
        _audioEngine = audioEngine;
        _settingsManager = settingsManager;
        _startupManager = startupManager;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        RemapActions = new List<KeyRemapActionItem>
        {
            new KeyRemapActionItem { Action = KeyRemapAction.ToggleMute, DisplayName = "Toggle Mute", Description = "Press to toggle microphone mute on and off" },
            new KeyRemapActionItem { Action = KeyRemapAction.PushToTalk, DisplayName = "Push-to-Talk", Description = "Hold to unmute microphone, release to mute" },
            new KeyRemapActionItem { Action = KeyRemapAction.PushToMute, DisplayName = "Push-to-Mute", Description = "Hold to mute microphone (cough button), release to unmute" },
            new KeyRemapActionItem { Action = KeyRemapAction.CycleRecordingDevice, DisplayName = "Switch / Cycle Recording Device", Description = "Cycle to the next active microphone and set as Windows default" },
            new KeyRemapActionItem { Action = KeyRemapAction.LockVolume, DisplayName = "Lock Volume to Target %", Description = "Instantly restore and enforce the target volume level" }
        };

        RemapPresets = new List<RemapPresetItem>
        {
            new RemapPresetItem { DisplayName = "Select a Preset...", VirtualKey = 0 },
            new RemapPresetItem { DisplayName = "Keychron Mic Key (Win+C)", VirtualKey = 0x43, ModifiersWin = true, ComboText = "Win+C" },
            new RemapPresetItem { DisplayName = "PowerToys Remap (Ctrl+Alt+M)", VirtualKey = 0x4D, ModifiersCtrl = true, ModifiersAlt = true, ComboText = "Ctrl+Alt+M" },
            new RemapPresetItem { DisplayName = "Dedicated Hardware Mic Key", VirtualKey = 0x83, ComboText = "Dedicated Mic Key" },
            new RemapPresetItem { DisplayName = "Standard Mute Shortcut (Ctrl+Shift+Alt+M)", VirtualKey = 0x4D, ModifiersCtrl = true, ModifiersAlt = true, ModifiersShift = true, ComboText = "Ctrl+Shift+Alt+M" },
            new RemapPresetItem { DisplayName = "Cycle Microphone Shortcut (Ctrl+Shift+Alt+D)", VirtualKey = 0x44, ModifiersCtrl = true, ModifiersAlt = true, ModifiersShift = true, ComboText = "Ctrl+Shift+Alt+D" },
            new RemapPresetItem { DisplayName = "Keyboard Mute Key", VirtualKey = 0xAD, ComboText = "Mute Key" },
            new RemapPresetItem { DisplayName = "Pause / Break Key", VirtualKey = 0x13, ComboText = "Pause" },
            new RemapPresetItem { DisplayName = "Scroll Lock Key", VirtualKey = 0x91, ComboText = "Scroll Lock" },
            new RemapPresetItem { DisplayName = "Ctrl+Shift+Space", VirtualKey = 0x20, ModifiersCtrl = true, ModifiersShift = true, ComboText = "Ctrl+Shift+Space" },
            new RemapPresetItem { DisplayName = "Alt+` (Backquote)", VirtualKey = 0xC0, ModifiersAlt = true, ComboText = "Alt+`" }
        };

        _selectedRemapAction = RemapActions[0];

        SetPresetCommand = new RelayCommand<string>(ExecuteSetPreset);
        RefreshDevicesCommand = new RelayCommand(_ => RefreshDevices());
        ClearLogsCommand = new RelayCommand(_ => ActivityLogs.Clear());
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute());
        CycleRecordingDeviceCommand = new RelayCommand(_ => CycleRecordingDevice());
        StartRecordKeyCommand = new RelayCommand(_ => StartRecordingKey());
        CancelRecordKeyCommand = new RelayCommand(_ => CancelRecordingKey());
        ClearKeyBindingCommand = new RelayCommand(_ => ClearBinding());
        CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync());

        // Load persisted settings
        var settings = _settingsManager.Current;
        _targetVolumePercent = settings.TargetVolumePercent;
        _isLocked = settings.IsLockEnabled;
        _startWithWindows = settings.StartWithWindows;
        _startMinimized = settings.StartMinimized;
        _showNotifications = settings.ShowNotifications;
        _keepOverlayWhenMuted = settings.KeepOverlayWhenMuted;
        _overlayOpacityWhenMuted = settings.OverlayOpacityWhenMuted;

        // Initialize Keyboard Remapping Engine
        _keyRemapManager = keyRemapManager ?? new KeyRemapManager(settings.KeyBindings);
        _keyRemapManager.IsRemapEnabled = settings.IsKeyRemapEnabled;
        _keyRemapManager.ActionTriggered += OnRemapActionTriggered;
        _keyRemapManager.RecordingStarted += () => _dispatcher.Invoke(() => IsRecordingKey = true);
        _keyRemapManager.RecordingEnded += () => _dispatcher.Invoke(() => IsRecordingKey = false);

        UpdateRemapDisplayForCurrentAction();

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

    public bool KeepOverlayWhenMuted
    {
        get => _keepOverlayWhenMuted;
        set
        {
            if (SetProperty(ref _keepOverlayWhenMuted, value))
            {
                _settingsManager.Current.KeepOverlayWhenMuted = value;
                _settingsManager.Save();
                OverlaySettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int OverlayOpacityPercent
    {
        get => (int)Math.Round(_overlayOpacityWhenMuted * 100);
        set
        {
            int clamped = Math.Clamp(value, 10, 100);
            double dec = clamped / 100.0;
            if (SetProperty(ref _overlayOpacityWhenMuted, dec))
            {
                _settingsManager.Current.OverlayOpacityWhenMuted = dec;
                _settingsManager.Save();
                OnPropertyChanged(nameof(OverlayOpacityPercent));
                OverlaySettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsKeyRemapEnabled
    {
        get => _keyRemapManager.IsRemapEnabled;
        set
        {
            if (_keyRemapManager.IsRemapEnabled != value)
            {
                _keyRemapManager.IsRemapEnabled = value;
                _settingsManager.Current.IsKeyRemapEnabled = value;
                _settingsManager.Save();
                OnPropertyChanged(nameof(IsKeyRemapEnabled));
            }
        }
    }

    public KeyRemapActionItem SelectedRemapAction
    {
        get => _selectedRemapAction;
        set
        {
            if (SetProperty(ref _selectedRemapAction, value))
            {
                UpdateRemapDisplayForCurrentAction();
            }
        }
    }

    public string CurrentRemapBindingText
    {
        get => _currentRemapBindingText;
        private set => SetProperty(ref _currentRemapBindingText, value);
    }

    public bool IsRecordingKey
    {
        get => _isRecordingKey;
        private set
        {
            if (SetProperty(ref _isRecordingKey, value))
            {
                OnPropertyChanged(nameof(RecordButtonText));
            }
        }
    }

    public string RecordButtonText => _isRecordingKey ? "Recording... (Press key or Esc)" : "Record Key";

    public RemapPresetItem? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value))
            {
                if (value != null && value.VirtualKey != 0)
                {
                    ApplyPreset(value);
                }
            }
        }
    }

    public void StartRecordingKey()
    {
        IsRecordingKey = true;
        _keyRemapManager.StartRecording(
            onKeyRecorded: rule =>
            {
                _dispatcher.Invoke(() =>
                {
                    rule.Action = SelectedRemapAction.Action;
                    _keyRemapManager.UpdateBinding(rule);

                    var settingRule = _settingsManager.Current.KeyBindings.FirstOrDefault(b => b.Action == rule.Action);
                    if (settingRule != null)
                    {
                        settingRule.VirtualKey = rule.VirtualKey;
                        settingRule.ModifiersCtrl = rule.ModifiersCtrl;
                        settingRule.ModifiersAlt = rule.ModifiersAlt;
                        settingRule.ModifiersShift = rule.ModifiersShift;
                        settingRule.ModifiersWin = rule.ModifiersWin;
                        settingRule.SuppressOriginalKey = rule.SuppressOriginalKey;
                        settingRule.DisplayText = rule.DisplayText;
                    }
                    else
                    {
                        _settingsManager.Current.KeyBindings.Add(rule.Clone());
                    }
                    _settingsManager.Save();

                    CurrentRemapBindingText = rule.DisplayText;
                    IsRecordingKey = false;
                });
            },
            onCancelled: () =>
            {
                _dispatcher.Invoke(() =>
                {
                    IsRecordingKey = false;
                });
            });
    }

    public void CancelRecordingKey()
    {
        _keyRemapManager.CancelRecording();
        IsRecordingKey = false;
    }

    public void ClearBinding()
    {
        var rule = new KeyBindingRule
        {
            Action = SelectedRemapAction.Action,
            VirtualKey = 0,
            ModifiersCtrl = false,
            ModifiersAlt = false,
            ModifiersShift = false,
            ModifiersWin = false,
            SuppressOriginalKey = true,
            DisplayText = "None"
        };

        _keyRemapManager.UpdateBinding(rule);

        var settingRule = _settingsManager.Current.KeyBindings.FirstOrDefault(b => b.Action == rule.Action);
        if (settingRule != null)
        {
            settingRule.VirtualKey = 0;
            settingRule.ModifiersCtrl = false;
            settingRule.ModifiersAlt = false;
            settingRule.ModifiersShift = false;
            settingRule.ModifiersWin = false;
            settingRule.DisplayText = "None";
        }
        _settingsManager.Save();

        CurrentRemapBindingText = "None";
    }

    public void ApplyPreset(RemapPresetItem? preset)
    {
        if (preset == null || preset.VirtualKey == 0) return;

        var rule = new KeyBindingRule
        {
            Action = SelectedRemapAction.Action,
            VirtualKey = preset.VirtualKey,
            ModifiersCtrl = preset.ModifiersCtrl,
            ModifiersAlt = preset.ModifiersAlt,
            ModifiersShift = preset.ModifiersShift,
            ModifiersWin = preset.ModifiersWin,
            SuppressOriginalKey = true,
            DisplayText = preset.ComboText
        };

        _keyRemapManager.UpdateBinding(rule);

        var settingRule = _settingsManager.Current.KeyBindings.FirstOrDefault(b => b.Action == rule.Action);
        if (settingRule != null)
        {
            settingRule.VirtualKey = rule.VirtualKey;
            settingRule.ModifiersCtrl = rule.ModifiersCtrl;
            settingRule.ModifiersAlt = rule.ModifiersAlt;
            settingRule.ModifiersShift = rule.ModifiersShift;
            settingRule.ModifiersWin = rule.ModifiersWin;
            settingRule.SuppressOriginalKey = rule.SuppressOriginalKey;
            settingRule.DisplayText = rule.DisplayText;
        }
        else
        {
            _settingsManager.Current.KeyBindings.Add(rule.Clone());
        }
        _settingsManager.Save();

        CurrentRemapBindingText = rule.DisplayText;
    }

    private void UpdateRemapDisplayForCurrentAction()
    {
        var rule = _keyRemapManager.GetBindingForAction(SelectedRemapAction.Action);
        CurrentRemapBindingText = string.IsNullOrEmpty(rule?.DisplayText) ? "None" : rule.DisplayText;
    }

    private void OnRemapActionTriggered(KeyRemapAction action, KeyPressState state)
    {
        _dispatcher.Invoke(() =>
        {
            switch (action)
            {
                case KeyRemapAction.ToggleMute:
                    if (state == KeyPressState.Down)
                    {
                        ToggleMute();
                    }
                    break;

                case KeyRemapAction.PushToTalk:
                    if (state == KeyPressState.Down)
                    {
                        _audioEngine.SetMute(false);
                    }
                    else if (state == KeyPressState.Up)
                    {
                        _audioEngine.SetMute(true);
                    }
                    break;

                case KeyRemapAction.PushToMute:
                    if (state == KeyPressState.Down)
                    {
                        _audioEngine.SetMute(true);
                    }
                    else if (state == KeyPressState.Up)
                    {
                        _audioEngine.SetMute(false);
                    }
                    break;

                case KeyRemapAction.CycleRecordingDevice:
                    if (state == KeyPressState.Down)
                    {
                        CycleRecordingDevice();
                    }
                    break;

                case KeyRemapAction.LockVolume:
                    if (state == KeyPressState.Down)
                    {
                        IsLocked = true;
                        _audioEngine.SetVolume(TargetVolumePercent);
                        VolumeLockEnforced?.Invoke(this, TargetVolumePercent);
                    }
                    break;
            }
        });
    }

    public AudioDeviceInfo? CycleRecordingDevice()
    {
        var devices = _audioEngine.GetAvailableCaptureDevices();
        if (devices.Count == 0) return null;

        string? currentId = _audioEngine.ActiveDevice?.Id ?? SelectedDevice?.Id;
        int currentIndex = -1;
        if (!string.IsNullOrEmpty(currentId))
        {
            for (int i = 0; i < devices.Count; i++)
            {
                if (string.Equals(devices[i].Id, currentId, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        int nextIndex = (currentIndex + 1) % devices.Count;
        var nextDevice = devices[nextIndex];

        // 1. Set Windows default device via PolicyConfig COM
        PolicyConfigClient.SetDefaultDeviceAllRoles(nextDevice.Id);

        // 2. Attach AudioLockEngine to the new device
        _audioEngine.SelectDevice(nextDevice.Id);

        // 3. Update settings and save
        _settingsManager.Current.SelectedDeviceId = nextDevice.Id;
        _settingsManager.Current.SelectedDeviceName = nextDevice.Name;
        _settingsManager.Save();

        // 4. Refresh devices list and selection in UI
        RefreshDevices();

        // 5. Notify listeners (TrayController -> OSD Banner with microphone name)
        DeviceSwitched?.Invoke(this, nextDevice);

        return nextDevice;
    }

    public ICommand CheckForUpdatesCommand { get; }

    private string _updateStatusText = "v1.0.0 (Up to date)";
    public string UpdateStatusText
    {
        get => _updateStatusText;
        set => SetProperty(ref _updateStatusText, value);
    }

    private bool _isCheckingUpdates;
    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        set => SetProperty(ref _isCheckingUpdates, value);
    }

    private string? _latestReleaseUrl;
    public string? LatestReleaseUrl
    {
        get => _latestReleaseUrl;
        set => SetProperty(ref _latestReleaseUrl, value);
    }

    public async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates) return;
        IsCheckingUpdates = true;
        UpdateStatusText = "Checking GitHub...";

        try
        {
            var checker = new GitHubUpdateChecker();
            var result = await checker.CheckForUpdatesAsync("1.0.0");
            if (result.IsUpdateAvailable)
            {
                UpdateStatusText = $"New version: v{result.LatestVersion} (Click to open)";
                LatestReleaseUrl = result.HtmlUrl;

                var choice = System.Windows.MessageBox.Show(
                    $"A new version of MicAnchor is available!\n\nLatest: v{result.LatestVersion} (Current: v1.0.0)\n\n{result.ReleaseTitle}\n\nWould you like to open the GitHub releases page to download it?",
                    "MicAnchor Update Available",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);

                if (choice == System.Windows.MessageBoxResult.Yes)
                {
                    GitHubUpdateChecker.OpenReleaseUrl(result.HtmlUrl);
                }
            }
            else if (result.Success)
            {
                UpdateStatusText = "v1.0.0 (Up to date)";
                System.Windows.MessageBox.Show(
                    "You are using the latest version of MicAnchor (v1.0.0).",
                    "MicAnchor Up to Date",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                UpdateStatusText = "Could not check updates (Offline)";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Check failed: {ex.Message}";
        }
        finally
        {
            IsCheckingUpdates = false;
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
        ? $"MicAnchor: Locked at {_targetVolumePercent}% | {_audioEngine.ActiveDevice?.DisplayName ?? "Default Mic"}"
        : "MicAnchor: Protection Paused";

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
        _keyRemapManager.ActionTriggered -= OnRemapActionTriggered;
        _keyRemapManager.Dispose();

        _audioEngine.VolumeChanged -= OnAudioEngineVolumeChanged;
        _audioEngine.MuteChanged -= OnAudioEngineMuteChanged;
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
