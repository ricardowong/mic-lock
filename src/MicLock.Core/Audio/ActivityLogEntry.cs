namespace MicLock.Core.Audio;

public class VolumeRestoredEventArgs : EventArgs
{
    public int PreviousPercent { get; }
    public int RestoredPercent { get; }
    public string DeviceName { get; }
    public DateTime Timestamp { get; }

    public VolumeRestoredEventArgs(int previousPercent, int restoredPercent, string deviceName)
    {
        PreviousPercent = previousPercent;
        RestoredPercent = restoredPercent;
        DeviceName = deviceName;
        Timestamp = DateTime.Now;
    }
}

public enum LogLevel
{
    Info,
    Success,
    Warning,
    Revert,
    Error
}

public class ActivityLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Message { get; set; } = string.Empty;
    public LogLevel Level { get; set; } = LogLevel.Info;

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");

    public override string ToString() => $"[{FormattedTime}] {Message}";
}
