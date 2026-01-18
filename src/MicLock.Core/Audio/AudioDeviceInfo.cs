namespace MicLock.Core.Audio;

public class AudioDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public int CurrentVolumePercent { get; set; }
    public bool IsMuted { get; set; }

    public string DisplayName => IsDefault ? $"{Name} (Default)" : Name;

    public override string ToString() => DisplayName;
}
