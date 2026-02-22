using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicLock.Core.Audio;

[SupportedOSPlatform("windows")]
internal class DeviceNotificationClient : IMMNotificationClient
{
    public event Action<string>? DeviceAdded;
    public event Action<string>? DeviceRemoved;
    public event Action<string, DeviceState>? DeviceStateChanged;
    public event Action<DataFlow, Role, string>? DefaultDeviceChanged;

    public void OnDeviceAdded(string pwstrDeviceId) => DeviceAdded?.Invoke(pwstrDeviceId);

    public void OnDeviceRemoved(string pwstrDeviceId) => DeviceRemoved?.Invoke(pwstrDeviceId);

    public void OnDeviceStateChanged(string pwstrDeviceId, DeviceState dwNewState) =>
        DeviceStateChanged?.Invoke(pwstrDeviceId, dwNewState);

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string pwstrDefaultDeviceId)
    {
        if (flow == DataFlow.Capture && (role == Role.Console || role == Role.Communications))
        {
            DefaultDeviceChanged?.Invoke(flow, role, pwstrDefaultDeviceId);
        }
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        // Unused property changes (friendly name, display icon)
    }
}
