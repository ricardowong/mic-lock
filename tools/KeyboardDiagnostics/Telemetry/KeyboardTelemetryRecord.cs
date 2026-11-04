namespace MicLock.Tools.KeyboardDiagnostics.Telemetry;

public record KeyboardTelemetryRecord
{
    public DateTime Timestamp { get; init; }
    public long StopwatchTimestamp { get; init; }
    public double MicrosecondsDelta { get; set; }

    public uint MessageId { get; init; }
    public string MessageName { get; init; } = "";

    public uint VirtualKeyCode { get; init; }
    public string KeyName { get; init; } = "";
    public uint ScanCode { get; init; }

    public uint Flags { get; init; }
    public bool IsExtended => (Flags & Native.NativeMethods.LLKHF_EXTENDED) != 0;
    public bool IsLowerIlInjected => (Flags & Native.NativeMethods.LLKHF_LOWER_IL_INJECTED) != 0;
    public bool IsInjected => (Flags & Native.NativeMethods.LLKHF_INJECTED) != 0;
    public bool IsAltDown => (Flags & Native.NativeMethods.LLKHF_ALTDOWN) != 0;
    public bool IsKeyUp => (Flags & Native.NativeMethods.LLKHF_UP) != 0;

    public UIntPtr ExtraInfo { get; init; }
    public string ExtraInfoDescription { get; init; } = "";

    // OS Asynchronous Key States (GetAsyncKeyState at moment of hook)
    public bool AsyncLWinDown { get; init; }
    public bool AsyncRWinDown { get; init; }
    public bool AsyncCtrlDown { get; init; }
    public bool AsyncAltDown { get; init; }
    public bool AsyncShiftDown { get; init; }

    // Foreground Window context
    public string WindowTitle { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public uint ProcessId { get; init; }

    // Mode handling telemetry
    public int ModeId { get; init; }
    public bool Swallowed { get; set; }
    public string ActionNote { get; set; } = "";

    public static string FormatMessageName(uint msg)
    {
        return msg switch
        {
            Native.NativeMethods.WM_KEYDOWN => "WM_KEYDOWN",
            Native.NativeMethods.WM_KEYUP => "WM_KEYUP",
            Native.NativeMethods.WM_SYSKEYDOWN => "WM_SYSKEYDOWN",
            Native.NativeMethods.WM_SYSKEYUP => "WM_SYSKEYUP",
            _ => $"WM_0x{msg:X4}"
        };
    }

    public static string DescribeExtraInfo(UIntPtr extra)
    {
        ulong val = extra.ToUInt64();
        return val switch
        {
            0 => "None",
            Native.NativeMethods.EXTRA_INFO_MICLOCK_DUMMY => "MicLock Dummy",
            Native.NativeMethods.EXTRA_INFO_POWERTOYS_SHORTCUT => "PowerToys Shortcut",
            Native.NativeMethods.EXTRA_INFO_AHK_MASK => "AHK MenuMask",
            Native.NativeMethods.EXTRA_INFO_SYNTHETIC_DIAGNOSTIC => "Diagnostic Synthetic",
            _ => $"Custom (0x{val:X})"
        };
    }
}
