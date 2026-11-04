using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    public KeyMappingType MappingType { get; set; } = KeyMappingType.Shortcut;
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
    private bool _isMicLockEnabled = true;
    private bool _isSettingsOpen;
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
    private string _currentInputKeyText = "Win+C";
    private string _currentRemapTargetText = "Ctrl+Shift+Alt+M";
    private bool _isSendRemapKeysEnabled = true;
    private string _lastDetectedKeyText = "None";
    private string _remapStatusText = "Ready - Listening for configured keys";
    private string _remapSummaryText = string.Empty;
    private bool _isCapturingInput;
    private bool _isCapturingTarget;

    public ObservableCollection<AudioDeviceInfo> AvailableDevices { get; } = new();
    public ObservableCollection<ActivityLogEntry> ActivityLogs { get; } = new();
    public IReadOnlyList<KeyRemapActionItem> RemapActions { get; }

    public IAudioLockEngine AudioEngine => _audioEngine;
    public KeyRemapManager KeyRemapManager => _keyRemapManager;

    public ICommand SetPresetCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand CycleRecordingDeviceCommand { get; }

    public ICommand ToggleSettingsCommand { get; }
    public ICommand BackupSettingsCommand { get; }
    public ICommand RestoreSettingsCommand { get; }
    public ICommand OpenSettingsFolderCommand { get; }

    public ICommand StartCaptureInputCommand { get; }
    public ICommand StartCaptureTargetCommand { get; }
    public ICommand CancelCaptureCommand { get; }
    public ICommand ClearInputCommand { get; }
    public ICommand ClearTargetCommand { get; }

    // Backwards compatibility commands
    public ICommand StartRecordKeyCommand => StartCaptureInputCommand;
    public ICommand CancelRecordKeyCommand => CancelCaptureCommand;
    public ICommand ClearKeyBindingCommand => ClearInputCommand;

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
            new KeyRemapActionItem { Action = KeyRemapAction.ToggleMute, DisplayName = "Toggle Microphone Mute", Description = "Press to toggle microphone mute on and off" },
            new KeyRemapActionItem { Action = KeyRemapAction.CycleRecordingDevice, DisplayName = "Switch / Cycle Recording Device", Description = "Cycle to the next active microphone and set as Windows default" },
            new KeyRemapActionItem { Action = KeyRemapAction.LockVolume, DisplayName = "Lock Volume to Target %", Description = "Instantly restore and enforce the target volume level" },
            new KeyRemapActionItem { Action = KeyRemapAction.PushToTalk, DisplayName = "Push-to-Talk", Description = "Hold to unmute microphone, release to mute" },
            new KeyRemapActionItem { Action = KeyRemapAction.PushToMute, DisplayName = "Push-to-Mute", Description = "Hold to mute microphone (cough button), release to unmute" },
            new KeyRemapActionItem { Action = KeyRemapAction.None, DisplayName = "Pure Key Remap (Passthrough Only)", Description = "Remap keystrokes without performing any audio actions" }
        };

        _selectedRemapAction = RemapActions[0];

        SetPresetCommand = new RelayCommand<string>(ExecuteSetPreset);
        RefreshDevicesCommand = new RelayCommand(_ => RefreshDevices());
        ClearLogsCommand = new RelayCommand(_ => ActivityLogs.Clear());
        ToggleMuteCommand = new RelayCommand(_ => ToggleMute());
        CycleRecordingDeviceCommand = new RelayCommand(_ => CycleRecordingDevice());

        ToggleSettingsCommand = new RelayCommand(_ => IsSettingsOpen = !IsSettingsOpen);
        BackupSettingsCommand = new RelayCommand(_ => ExecuteBackupSettings());
        RestoreSettingsCommand = new RelayCommand(_ => ExecuteRestoreSettings());
        OpenSettingsFolderCommand = new RelayCommand(_ => ExecuteOpenSettingsFolder());

        StartCaptureInputCommand = new RelayCommand(_ => StartCaptureInput());
        StartCaptureTargetCommand = new RelayCommand(_ => StartCaptureTarget());
        CancelCaptureCommand = new RelayCommand(_ => CancelCapture());
        ClearInputCommand = new RelayCommand(_ => ClearInput());
        ClearTargetCommand = new RelayCommand(_ => ClearTarget());

        CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync());

        // Load persisted settings
        var settings = _settingsManager.Current;
        _targetVolumePercent = settings.TargetVolumePercent;
        _isLocked = settings.IsLockEnabled;
        _isMicLockEnabled = settings.IsMicLockEnabled;
        _startWithWindows = settings.StartWithWindows;
        _startMinimized = settings.StartMinimized;
        _showNotifications = settings.ShowNotifications;
        _keepOverlayWhenMuted = settings.KeepOverlayWhenMuted;
        _overlayOpacityWhenMuted = settings.OverlayOpacityWhenMuted;

        // Initialize Keyboard Remapping Engine
        _keyRemapManager = keyRemapManager ?? new KeyRemapManager(settings.KeyBindings);
        _keyRemapManager.IsRemapEnabled = settings.IsKeyRemapEnabled;
        _keyRemapManager.ActionTriggered += OnRemapActionTriggered;
        _keyRemapManager.RemapExecuted += OnKeyRemapExecuted;
        _keyRemapManager.LogMessage += OnKeyRemapLogMessage;
        _keyRemapManager.CaptureEnded += () => _dispatcher.Invoke(() =>
        {
            IsCapturingInput = false;
            IsCapturingTarget = false;
        });

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
        _audioEngine.IsMicDeviceLocked = _isMicLockEnabled;

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

    public bool IsMicLockEnabled
    {
        get => _isMicLockEnabled;
        set
        {
            if (SetProperty(ref _isMicLockEnabled, value))
            {
                _audioEngine.IsMicDeviceLocked = value;
                _settingsManager.Current.IsMicLockEnabled = value;
                _settingsManager.Save();

                if (value && SelectedDevice != null && !SelectedDevice.IsDefault)
                {
                    PolicyConfigClient.SetDefaultDeviceAllRoles(SelectedDevice.Id);
                }

                AddActivityLog(value ? $"[Mic Lock] Locked Windows default to '{SelectedDevice?.Name}'" : "[Mic Lock] Unlocked default microphone selection");
            }
        }
    }

    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => SetProperty(ref _isSettingsOpen, value);
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

    public string CurrentInputKeyText
    {
        get => _currentInputKeyText;
        private set => SetProperty(ref _currentInputKeyText, value);
    }

    public string CurrentRemapBindingText => CurrentInputKeyText;

    public string CurrentRemapTargetText
    {
        get => _currentRemapTargetText;
        private set => SetProperty(ref _currentRemapTargetText, value);
    }

    public bool IsSendRemapKeysEnabled
    {
        get => _isSendRemapKeysEnabled;
        set
        {
            if (SetProperty(ref _isSendRemapKeysEnabled, value))
            {
                var rule = _keyRemapManager.GetBindingForAction(SelectedRemapAction.Action);
                if (rule != null)
                {
                    rule.SendRemapKeys = value;
                    _keyRemapManager.UpdateBinding(rule);
                    var settingRule = _settingsManager.Current.KeyBindings.FirstOrDefault(b => b.Action == rule.Action);
                    if (settingRule != null)
                    {
                        settingRule.SendRemapKeys = value;
                        _settingsManager.Save();
                    }
                    UpdateRemapSummary();
                }
            }
        }
    }

    public string LastDetectedKeyText
    {
        get => _lastDetectedKeyText;
        private set => SetProperty(ref _lastDetectedKeyText, value);
    }

    public string RemapStatusText
    {
        get => _remapStatusText;
        private set => SetProperty(ref _remapStatusText, value);
    }

    public string RemapSummaryText
    {
        get => _remapSummaryText;
        private set => SetProperty(ref _remapSummaryText, value);
    }

    public bool IsCapturingInput
    {
        get => _isCapturingInput;
        private set
        {
            if (SetProperty(ref _isCapturingInput, value))
            {
                OnPropertyChanged(nameof(CaptureInputButtonText));
                OnPropertyChanged(nameof(RecordButtonText));
                OnPropertyChanged(nameof(IsRecordingKey));
            }
        }
    }

    public bool IsRecordingKey => IsCapturingInput || IsCapturingTarget;

    public bool IsCapturingTarget
    {
        get => _isCapturingTarget;
        private set
        {
            if (SetProperty(ref _isCapturingTarget, value))
            {
                OnPropertyChanged(nameof(CaptureTargetButtonText));
                OnPropertyChanged(nameof(IsRecordingKey));
            }
        }
    }

    public string CaptureInputButtonText => _isCapturingInput ? "Press key now... (Esc)" : "Capture Physical Key";
    public string CaptureTargetButtonText => _isCapturingTarget ? "Press keys now... (Esc)" : "Capture Remap Target";
    public string RecordButtonText => CaptureInputButtonText;

    public void StartCaptureInput()
    {
        IsKeyRemapEnabled = true;
        IsCapturingInput = true;
        _keyRemapManager.StartCaptureInput(
            onCaptured: rule =>
            {
                _dispatcher.Invoke(() =>
                {
                    var current = _keyRemapManager.GetBindingForAction(SelectedRemapAction.Action) ?? new KeyBindingRule { Action = SelectedRemapAction.Action };
                    current.VirtualKey = rule.VirtualKey;
                    current.ModifiersCtrl = rule.ModifiersCtrl;
                    current.ModifiersAlt = rule.ModifiersAlt;
                    current.ModifiersShift = rule.ModifiersShift;
                    current.ModifiersWin = rule.ModifiersWin;
                    current.MappingType = rule.MappingType;
                    current.DisplayText = rule.DisplayText;
                    current.SuppressOriginalKey = true;

                    _keyRemapManager.UpdateBinding(current);

                    var settingRule = _settingsManager.Current.KeyBindings.FirstOrDefault(b => b.Action == current.Action);
                    if (settingRule != null)
                    {
                        settingRule.VirtualKey = current.VirtualKey;
                        settingRule.ModifiersCtrl = current.ModifiersCtrl;
                        settingRule.ModifiersAlt = current.ModifiersAlt;
                        settingRule.ModifiersShift = current.ModifiersShift;
                        settingRule.ModifiersWin = current.ModifiersWin;
                        settingRule.MappingType = current.MappingType;
                        settingRule.DisplayText = current.DisplayText;
                    }
                    else
                    {
                        _settingsManager.Current.KeyBindings.Add(current.Clone());
                    }
                    _settingsManager.Current.IsKeyRemapEnabled = true;
                    _settingsManager.Save();

                    CurrentInputKeyText = current.DisplayText;
                    IsCapturingInput = false;
                    UpdateRemapSummary();
                    AddActivityLog($"[Remap Config] Set input key for '{SelectedRemapAction.DisplayName}' to '{current.DisplayText}'");
                });
            },
            onCancelled: () =>
            {
                _dispatcher.Invoke(() =>
                {
                    IsCapturingInput = false;
                });
            });
    }

    public void StartCaptureTarget()
    {
        IsKeyRemapEnabled = true;
        IsCapturingTarget = true;
        _keyRemapManager.StartCaptureTarget(
            onCaptured: (vk, ctrl, alt, shift, win, displayCombo) =>
            {
                _dispatcher.Invoke(() =>
                {
                    var current = _keyRemapManager.GetBindingForAction(SelectedRemapAction.Action) ?? new KeyBindingRule { Action = SelectedRemapAction.Action };
                    current.RemapVirtualKey = vk;
                    current.RemapModifiersCtrl = ctrl;
                    current.RemapModifiersAlt = alt;
                    current.RemapModifiersShift = shift;
                    current.RemapModifiersWin = win;
                    current.RemapDisplayText = displayCombo;
                    current.SendRemapKeys = true;

                    _keyRemapManager.UpdateBinding(current);

                    var settingRule = _settingsManager.Current.KeyBindings.FirstOrDefault(b => b.Action == current.Action);
                    if (settingRule != null)
                    {
                        settingRule.RemapVirtualKey = current.RemapVirtualKey;
                        settingRule.RemapModifiersCtrl = current.RemapModifiersCtrl;
                        settingRule.RemapModifiersAlt = current.RemapModifiersAlt;
                        settingRule.RemapModifiersShift = current.RemapModifiersShift;
                        settingRule.RemapModifiersWin = current.RemapModifiersWin;
                        settingRule.RemapDisplayText = current.RemapDisplayText;
                        settingRule.SendRemapKeys = current.SendRemapKeys;
                    }
                    else
                    {
                        _settingsManager.Current.KeyBindings.Add(current.Clone());
                    }
                    _settingsManager.Save();

                    CurrentRemapTargetText = current.RemapDisplayText;
                    IsSendRemapKeysEnabled = true;
                    IsCapturingTarget = false;
                    UpdateRemapSummary();
                    AddActivityLog($"[Remap Config] Set target remapped shortcut for '{SelectedRemapAction.DisplayName}' to '{current.RemapDisplayText}'");
                });
            },
            onCancelled: () =>
            {
                _dispatcher.Invoke(() =>
                {
                    IsCapturingTarget = false;
                });
            });
    }

    public void CancelCapture()
    {
        _keyRemapManager.CancelCapture();
        IsCapturingInput = false;
        IsCapturingTarget = false;
    }

    public void ClearInput()
    {
        var current = _keyRemapManager.GetBindingForAction(SelectedRemapAction.Action);
        if (current != null)
        {
            current.VirtualKey = 0;
            current.ModifiersCtrl = false;
            current.ModifiersAlt = false;
            current.ModifiersShift = false;
            current.ModifiersWin = false;
            current.DisplayText = "None";
            _keyRemapManager.UpdateBinding(current);

            var settingRule = _settingsManager.Current.KeyBindings.FirstOrDefault(b => b.Action == current.Action);
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
            CurrentInputKeyText = "None";
            UpdateRemapSummary();
            AddActivityLog($"[Remap Config] Cleared physical input key for '{SelectedRemapAction.DisplayName}'");
        }
    }

    public void ClearTarget()
    {
        var current = _keyRemapManager.GetBindingForAction(SelectedRemapAction.Action);
        if (current != null)
        {
            current.RemapVirtualKey = 0;
            current.RemapModifiersCtrl = false;
            current.RemapModifiersAlt = false;
            current.RemapModifiersShift = false;
            current.RemapModifiersWin = false;
            current.RemapDisplayText = "None";
            current.SendRemapKeys = false;
            _keyRemapManager.UpdateBinding(current);

            var settingRule = _settingsManager.Current.KeyBindings.FirstOrDefault(b => b.Action == current.Action);
            if (settingRule != null)
            {
                settingRule.RemapVirtualKey = 0;
                settingRule.RemapModifiersCtrl = false;
                settingRule.RemapModifiersAlt = false;
                settingRule.RemapModifiersShift = false;
                settingRule.RemapModifiersWin = false;
                settingRule.RemapDisplayText = "None";
                settingRule.SendRemapKeys = false;
            }
            _settingsManager.Save();
            CurrentRemapTargetText = "None";
            IsSendRemapKeysEnabled = false;
            UpdateRemapSummary();
            AddActivityLog($"[Remap Config] Cleared remap target for '{SelectedRemapAction.DisplayName}'");
        }
    }

    private void UpdateRemapDisplayForCurrentAction()
    {
        var rule = _keyRemapManager.GetBindingForAction(SelectedRemapAction.Action);
        if (rule != null)
        {
            CurrentInputKeyText = string.IsNullOrEmpty(rule.DisplayText) ? "None" : rule.DisplayText;
            CurrentRemapTargetText = string.IsNullOrEmpty(rule.RemapDisplayText) ? "None" : rule.RemapDisplayText;
            _isSendRemapKeysEnabled = rule.SendRemapKeys;
            OnPropertyChanged(nameof(IsSendRemapKeysEnabled));
        }
        else
        {
            CurrentInputKeyText = "None";
            CurrentRemapTargetText = "None";
            _isSendRemapKeysEnabled = false;
            OnPropertyChanged(nameof(IsSendRemapKeysEnabled));
        }
        UpdateRemapSummary();
    }

    private void UpdateRemapSummary()
    {
        string input = CurrentInputKeyText;
        string target = CurrentRemapTargetText;
        string action = SelectedRemapAction.DisplayName;

        if (input == "None")
        {
            RemapSummaryText = $"No physical input key assigned for '{action}'. Click 'Capture Physical Key' to bind.";
        }
        else if (target != "None" && IsSendRemapKeysEnabled)
        {
            RemapSummaryText = $"When [{input}] is pressed: Windows key is suppressed, remapped to [{target}] via SendInput, and triggers [{action}].";
        }
        else
        {
            RemapSummaryText = $"When [{input}] is pressed: Windows key is suppressed and directly triggers [{action}].";
        }
    }

    private void OnKeyRemapExecuted(string input, string target, string action)
    {
        _dispatcher.Invoke(() =>
        {
            LastDetectedKeyText = $"{input} (at {DateTime.Now:HH:mm:ss})";
            RemapStatusText = $"Active: {input} ➔ {target} [{action}]";
        });
    }

    private void OnKeyRemapLogMessage(string msg)
    {
        _dispatcher.Invoke(() =>
        {
            AddActivityLog(msg);
        });
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

    public static string AppVersionString
    {
        get
        {
            var ver = typeof(MainViewModel).Assembly.GetName().Version;
            return ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.2.2";
        }
    }

    private string _updateStatusText = $"v{AppVersionString} (Up to date)";
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
            var result = await checker.CheckForUpdatesAsync(AppVersionString);
            if (result.IsUpdateAvailable)
            {
                UpdateStatusText = $"New version: v{result.LatestVersion} (Click to open)";
                LatestReleaseUrl = result.HtmlUrl;

                var choice = System.Windows.MessageBox.Show(
                    $"A new version of MicAnchor is available!\n\nLatest: v{result.LatestVersion} (Current: v{result.CurrentVersion})\n\n{result.ReleaseTitle}\n\nWould you like to open the GitHub releases page to download it?",
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
                UpdateStatusText = $"v{result.CurrentVersion} (Up to date)";
                System.Windows.MessageBox.Show(
                    $"You are using the latest version of MicAnchor (v{result.CurrentVersion}).",
                    "MicAnchor Up to Date",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                UpdateStatusText = !string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? $"GitHub: {result.ErrorMessage}"
                    : "Unable to reach GitHub";

                System.Windows.MessageBox.Show(
                    $"Could not check for updates: {result.ErrorMessage}",
                    "Update Check",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
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

    private void ExecuteBackupSettings()
    {
        try
        {
            var sfd = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"micanchor-settings-backup-{DateTime.Now:yyyyMMdd}.json",
                DefaultExt = ".json",
                Filter = "JSON Configuration Files (*.json)|*.json|All Files (*.*)|*.*"
            };
            if (sfd.ShowDialog() == true)
            {
                _settingsManager.ExportSettings(sfd.FileName);
                AddActivityLog($"[Settings Backup] Exported configuration to: {Path.GetFileName(sfd.FileName)}");
                System.Windows.MessageBox.Show($"Settings successfully exported to:\n{sfd.FileName}", "MicAnchor Backup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to export settings:\n{ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteRestoreSettings()
    {
        try
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".json",
                Filter = "JSON Configuration Files (*.json)|*.json|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                bool success = _settingsManager.ImportSettings(ofd.FileName);
                if (success)
                {
                    var s = _settingsManager.Current;
                    TargetVolumePercent = s.TargetVolumePercent;
                    IsLocked = s.IsLockEnabled;
                    IsMicLockEnabled = s.IsMicLockEnabled;
                    StartWithWindows = s.StartWithWindows;
                    StartMinimized = s.StartMinimized;
                    ShowNotifications = s.ShowNotifications;
                    KeepOverlayWhenMuted = s.KeepOverlayWhenMuted;
                    OverlayOpacityPercent = (int)Math.Round(s.OverlayOpacityWhenMuted * 100);

                    foreach (var rule in s.KeyBindings)
                    {
                        _keyRemapManager.UpdateBinding(rule);
                    }
                    IsKeyRemapEnabled = s.IsKeyRemapEnabled;
                    UpdateRemapDisplayForCurrentAction();
                    RefreshDevices();
                    AddActivityLog($"[Settings Restore] Successfully imported settings from: {Path.GetFileName(ofd.FileName)}");
                    System.Windows.MessageBox.Show("Settings successfully restored and applied!", "MicAnchor Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show("Could not parse settings JSON file. Please check file format.", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to restore settings:\n{ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteOpenSettingsFolder()
    {
        try
        {
            string dir = Path.GetDirectoryName(_settingsManager.SettingsFilePath)!;
            if (Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch { }
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

    public void AddActivityLog(string message, LogLevel level = LogLevel.Info)
    {
        _dispatcher.Invoke(() =>
        {
            var entry = new ActivityLogEntry
            {
                Timestamp = DateTime.Now,
                Message = message,
                Level = level
            };
            ActivityLogs.Insert(0, entry);
            if (ActivityLogs.Count > _settingsManager.Current.MaxLogEntries)
            {
                ActivityLogs.RemoveAt(ActivityLogs.Count - 1);
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
