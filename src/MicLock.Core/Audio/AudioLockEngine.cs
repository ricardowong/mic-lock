using System.Runtime.Versioning;
using NAudio.CoreAudioApi;

namespace MicLock.Core.Audio;

[SupportedOSPlatform("windows")]
public class AudioLockEngine : IAudioLockEngine
{
    private readonly MMDeviceEnumerator _deviceEnumerator;
    private readonly DeviceNotificationClient _notificationClient;
    private readonly object _syncLock = new();

    private MMDevice? _activeEndpoint;
    private AudioEndpointVolume? _activeEndpointVolume;
    private AudioEndpointVolumeNotificationDelegate? _volumeNotificationHandler;
    private string? _pinnedDeviceId;
    private bool _isRevertingVolume;
    private bool _disposed;

    public bool IsLocked { get; set; } = true;
    public int TargetVolumePercent { get; set; } = 100;
    public AudioDeviceInfo? ActiveDevice { get; private set; }

    public int CurrentVolumePercent
    {
        get
        {
            lock (_syncLock)
            {
                if (_activeEndpointVolume != null)
                {
                    return (int)Math.Round(_activeEndpointVolume.MasterVolumeLevelScalar * 100f);
                }
                return 0;
            }
        }
    }

    public bool IsMuted
    {
        get
        {
            lock (_syncLock)
            {
                return _activeEndpointVolume?.Mute ?? false;
            }
        }
    }

    public event EventHandler<int>? VolumeChanged;
    public event EventHandler<VolumeRestoredEventArgs>? VolumeRestored;
    public event EventHandler<AudioDeviceInfo?>? ActiveDeviceChanged;
    public event EventHandler<ActivityLogEntry>? LogEntryAdded;

    public AudioLockEngine()
    {
        _deviceEnumerator = new MMDeviceEnumerator();
        _notificationClient = new DeviceNotificationClient();

        _notificationClient.DefaultDeviceChanged += OnSystemDefaultDeviceChanged;
        _notificationClient.DeviceAdded += OnSystemDeviceAdded;
        _notificationClient.DeviceStateChanged += OnSystemDeviceStateChanged;

        try
        {
            _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
        }
        catch (Exception ex)
        {
            EmitLog(LogLevel.Warning, $"Could not register endpoint notifications: {ex.Message}");
        }
    }

    public IReadOnlyList<AudioDeviceInfo> GetAvailableCaptureDevices()
    {
        var result = new List<AudioDeviceInfo>();

        lock (_syncLock)
        {
            try
            {
                string? defaultId = null;
                try
                {
                    var defaultDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                    defaultId = defaultDevice.ID;
                }
                catch { }

                var endpoints = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                foreach (var ep in endpoints)
                {
                    int vol = 0;
                    bool muted = false;
                    try
                    {
                        vol = (int)Math.Round(ep.AudioEndpointVolume.MasterVolumeLevelScalar * 100f);
                        muted = ep.AudioEndpointVolume.Mute;
                    }
                    catch { }

                    result.Add(new AudioDeviceInfo
                    {
                        Id = ep.ID,
                        Name = ep.FriendlyName,
                        IsDefault = (ep.ID == defaultId),
                        CurrentVolumePercent = vol,
                        IsMuted = muted
                    });
                }
            }
            catch (Exception ex)
            {
                EmitLog(LogLevel.Error, $"Error enumerating devices: {ex.Message}");
            }
        }

        return result;
    }

    public bool SelectDevice(string? deviceId)
    {
        lock (_syncLock)
        {
            _pinnedDeviceId = string.IsNullOrWhiteSpace(deviceId) || deviceId.Equals("Default", StringComparison.OrdinalIgnoreCase)
                ? null
                : deviceId;

            return BindToTargetDevice();
        }
    }

    private bool BindToTargetDevice()
    {
        // Detach existing callback
        DetachCurrentEndpoint();

        MMDevice? candidate = null;
        bool isDefault = false;

        try
        {
            if (_pinnedDeviceId != null)
            {
                try
                {
                    candidate = _deviceEnumerator.GetDevice(_pinnedDeviceId);
                }
                catch
                {
                    EmitLog(LogLevel.Warning, $"Pinned microphone {_pinnedDeviceId} not found. Falling back to default.");
                }
            }

            if (candidate == null)
            {
                candidate = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                isDefault = true;
            }

            _activeEndpoint = candidate;

            if (_activeEndpoint != null)
            {
                _activeEndpointVolume = _activeEndpoint.AudioEndpointVolume;
                _volumeNotificationHandler = new AudioEndpointVolumeNotificationDelegate(OnEndpointVolumeNotification);
                _activeEndpointVolume.OnVolumeNotification += _volumeNotificationHandler;

                int currentVol = (int)Math.Round(_activeEndpointVolume.MasterVolumeLevelScalar * 100f);
                ActiveDevice = new AudioDeviceInfo
                {
                    Id = _activeEndpoint.ID,
                    Name = _activeEndpoint.FriendlyName,
                    IsDefault = isDefault,
                    CurrentVolumePercent = currentVol,
                    IsMuted = _activeEndpointVolume.Mute
                };

                EmitLog(LogLevel.Info, $"Monitoring audio endpoint: {ActiveDevice.DisplayName} (Current: {currentVol}%)");
                ActiveDeviceChanged?.Invoke(this, ActiveDevice);

                // Enforce lock immediately if enabled
                if (IsLocked && currentVol != TargetVolumePercent)
                {
                    SetVolume(TargetVolumePercent);
                    EmitLog(LogLevel.Success, $"Initialized and locked volume to {TargetVolumePercent}%");
                }

                return true;
            }
        }
        catch (Exception ex)
        {
            EmitLog(LogLevel.Error, $"Failed to bind audio endpoint: {ex.Message}");
        }

        ActiveDevice = null;
        ActiveDeviceChanged?.Invoke(this, null);
        return false;
    }

    private void DetachCurrentEndpoint()
    {
        if (_activeEndpoint != null)
        {
            if (_activeEndpointVolume != null && _volumeNotificationHandler != null)
            {
                try
                {
                    _activeEndpointVolume.OnVolumeNotification -= _volumeNotificationHandler;
                }
                catch { }
            }
            _volumeNotificationHandler = null;
            _activeEndpointVolume = null;

            _activeEndpoint.Dispose();
            _activeEndpoint = null;
        }
    }

    private void OnEndpointVolumeNotification(AudioVolumeNotificationData data)
    {
        if (_disposed) return;

        int currentPercent = (int)Math.Round(data.MasterVolume * 100f);
        VolumeChanged?.Invoke(this, currentPercent);

        if (!IsLocked || _isRevertingVolume) return;

        if (currentPercent != TargetVolumePercent)
        {
            lock (_syncLock)
            {
                if (_isRevertingVolume || _activeEndpointVolume == null) return;
                _isRevertingVolume = true;

                try
                {
                    _activeEndpointVolume.MasterVolumeLevelScalar = TargetVolumePercent / 100f;
                    EmitLog(LogLevel.Revert, $"Blocked external volume change to {currentPercent}% -> Reverted to {TargetVolumePercent}%");
                    VolumeRestored?.Invoke(this, new VolumeRestoredEventArgs(currentPercent, TargetVolumePercent, ActiveDevice?.Name ?? "Microphone"));
                }
                catch (Exception ex)
                {
                    EmitLog(LogLevel.Error, $"Failed to revert volume: {ex.Message}");
                }
                finally
                {
                    _isRevertingVolume = false;
                }
            }
        }
    }

    public void SetVolume(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        lock (_syncLock)
        {
            if (_activeEndpointVolume != null)
            {
                _isRevertingVolume = true;
                try
                {
                    _activeEndpointVolume.MasterVolumeLevelScalar = percent / 100f;
                    VolumeChanged?.Invoke(this, percent);
                }
                finally
                {
                    _isRevertingVolume = false;
                }
            }
        }
    }

    public void RefreshCurrentState()
    {
        lock (_syncLock)
        {
            if (_activeEndpointVolume != null)
            {
                int current = (int)Math.Round(_activeEndpointVolume.MasterVolumeLevelScalar * 100f);
                VolumeChanged?.Invoke(this, current);
            }
        }
    }

    private void OnSystemDefaultDeviceChanged(DataFlow flow, Role role, string pwstrDefaultDeviceId)
    {
        if (_pinnedDeviceId == null)
        {
            EmitLog(LogLevel.Info, "Windows default microphone changed. Re-binding endpoint...");
            lock (_syncLock)
            {
                BindToTargetDevice();
            }
        }
    }

    private void OnSystemDeviceAdded(string pwstrDeviceId)
    {
        if (_pinnedDeviceId != null && string.Equals(_pinnedDeviceId, pwstrDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            EmitLog(LogLevel.Success, "Pinned microphone plugged in! Re-attaching...");
            lock (_syncLock)
            {
                BindToTargetDevice();
            }
        }
    }

    private void OnSystemDeviceStateChanged(string pwstrDeviceId, DeviceState dwNewState)
    {
        if (ActiveDevice?.Id == pwstrDeviceId && dwNewState != DeviceState.Active)
        {
            EmitLog(LogLevel.Warning, "Active microphone disconnected or disabled. Re-evaluating default device...");
            lock (_syncLock)
            {
                BindToTargetDevice();
            }
        }
    }

    private void EmitLog(LogLevel level, string message)
    {
        var entry = new ActivityLogEntry { Level = level, Message = message };
        LogEntryAdded?.Invoke(this, entry);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_syncLock)
        {
            try
            {
                _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
            }
            catch { }

            DetachCurrentEndpoint();
            _deviceEnumerator.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
