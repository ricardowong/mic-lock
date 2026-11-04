namespace MicLock.Tools.KeyboardDiagnostics.Telemetry;

public static class TelemetryFormatter
{
    private static readonly object ConsoleLock = new();

    public static void PrintHeader(int activeMode, string modeName, string triggerDescription, bool isMode3Swallowing)
    {
        lock (ConsoleLock)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("=========================================================================================================");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("                         MicLock Low-Level Keyboard Diagnostics Suite (.NET 8)                           ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("=========================================================================================================");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" Active Mode   : ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Mode {activeMode}: {modeName}]");

            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(" Trigger Filter: ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[{triggerDescription}] (Press [T] to toggle)");

            if (activeMode == 3)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(" Mode 3 WinUp  : ");
                Console.ForegroundColor = isMode3Swallowing ? ConsoleColor.Red : ConsoleColor.Green;
                Console.WriteLine(isMode3Swallowing ? "[SWALLOW Physical Win KeyUp (Bug Simulation)]" : "[FORWARD Physical Win KeyUp (Safe)]");
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("---------------------------------------------------------------------------------------------------------");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(" Select Hook Test Mode via F1-F5 (Global/Console) or 0-4 (Console):");
            Console.WriteLine("   [F1] / [0] : Mode 0 - Pure Monitor (100% Passthrough, telemetry logging only)");
            Console.WriteLine("   [F2] / [1] : Mode 1 - AutoHotkey / MenuMask (Injects 0xE8 down/up, forwards physical Win keyup)");
            Console.WriteLine("   [F3] / [2] : Mode 2 - PowerToys Dummy Key (Injects 0xFF down/up extra:0x101, forwards Win keyup)");
            Console.WriteLine("   [F4] / [3] : Mode 3 - Synthetic Win-Up on Trigger (Tests Swallow vs Forward Win keyup)");
            Console.WriteLine("   [F5] / [4] : Mode 4 - MicLock Production Engine (Full KeyRemapManager integration)");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" Additional Commands: [M] Mode Menu | [T] Toggle Trigger Filter | [S] Toggle Mode 3 Submode | [C] Clear | [Q] Quit");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("=========================================================================================================");
            Console.ResetColor();
        }
    }

    public static void PrintEvent(KeyboardTelemetryRecord record)
    {
        lock (ConsoleLock)
        {
            // Timestamp and microsecond delta
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"[{record.Timestamp:HH:mm:ss.fff} | {record.Timestamp:HH:mm:ss.ffffff}] ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"(+{record.MicrosecondsDelta,8:F1} µs) ");

            // Message name
            switch (record.MessageId)
            {
                case Native.NativeMethods.WM_KEYDOWN:
                    Console.ForegroundColor = ConsoleColor.Green;
                    break;
                case Native.NativeMethods.WM_KEYUP:
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    break;
                case Native.NativeMethods.WM_SYSKEYDOWN:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case Native.NativeMethods.WM_SYSKEYUP:
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.White;
                    break;
            }
            Console.Write($"{record.MessageName,-14} ");

            // Key Info
            Console.ForegroundColor = record.IsInjected ? ConsoleColor.Magenta : ConsoleColor.White;
            Console.Write($"VK: 0x{record.VirtualKeyCode:X2} ({record.KeyName,-15}) ");

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write($"SC: 0x{record.ScanCode:X2} ");

            // Flags
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("Flags: ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write($"0x{record.Flags:X2} [");
            Console.ForegroundColor = record.IsExtended ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            Console.Write($"Ext:{(record.IsExtended ? 1 : 0)} ");
            Console.ForegroundColor = record.IsInjected ? ConsoleColor.Magenta : ConsoleColor.DarkGray;
            Console.Write($"Inj:{(record.IsInjected ? 1 : 0)} ");
            Console.ForegroundColor = record.IsAltDown ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            Console.Write($"Alt:{(record.IsAltDown ? 1 : 0)} ");
            Console.ForegroundColor = record.IsKeyUp ? ConsoleColor.Cyan : ConsoleColor.DarkGray;
            Console.Write($"Up:{(record.IsKeyUp ? 1 : 0)}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("] ");

            // Extra Info
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("Extra: ");
            Console.ForegroundColor = record.ExtraInfo != UIntPtr.Zero ? ConsoleColor.Yellow : ConsoleColor.DarkGray;
            Console.WriteLine($"0x{record.ExtraInfo.ToUInt64():X8} ({record.ExtraInfoDescription})");

            // Line 2: OS Async State & Foreground Window
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("    Async: [");

            WriteAsyncKey("LWin", record.AsyncLWinDown);
            Console.Write(" ");
            WriteAsyncKey("RWin", record.AsyncRWinDown);
            Console.Write(" ");
            WriteAsyncKey("Ctrl", record.AsyncCtrlDown);
            Console.Write(" ");
            WriteAsyncKey("Alt", record.AsyncAltDown);
            Console.Write(" ");
            WriteAsyncKey("Shift", record.AsyncShiftDown);

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("] | Window: ");

            Console.ForegroundColor = ConsoleColor.White;
            string titleTrimmed = record.WindowTitle.Length > 35 ? record.WindowTitle.Substring(0, 32) + "..." : record.WindowTitle;
            Console.Write($"\"{titleTrimmed}\" ");

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($"({record.ProcessName}:{record.ProcessId})");

            if (record.Swallowed)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write(" | [SWALLOWED]");
            }

            if (!string.IsNullOrEmpty(record.ActionNote))
            {
                Console.ForegroundColor = record.Swallowed ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                Console.Write($" ➔ {record.ActionNote}");
            }

            Console.WriteLine();
            Console.ResetColor();
        }
    }

    private static void WriteAsyncKey(string name, bool isDown)
    {
        if (isDown)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"{name}:DOWN");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"{name}:up  ");
        }
    }

    public static void PrintMessage(string message, ConsoleColor color = ConsoleColor.Cyan)
    {
        lock (ConsoleLock)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }
}
