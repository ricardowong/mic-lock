using System.Text;
using MicLock.Tools.KeyboardDiagnostics.Hooks;
using MicLock.Tools.KeyboardDiagnostics.Modes;
using MicLock.Tools.KeyboardDiagnostics.Telemetry;

namespace MicLock.Tools.KeyboardDiagnostics;

internal class Program
{
    private static LowLevelKeyboardHook? _hook;
    private static volatile bool _isPromptActive = false;
    private static volatile bool _isRunning = true;

    private static void Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (!Console.IsOutputRedirected)
            {
                Console.Title = "MicLock - Low-Level Keyboard Diagnostics Suite (.NET 8)";
            }
        }
        catch { }

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            _isRunning = false;
            CleanupAndExit();
        };

        try
        {
            _hook = new LowLevelKeyboardHook();

            // Wire up production engine telemetry
            var mode4 = _hook.GetMode<Mode4MicLockProduction>();
            if (mode4 != null)
            {
                mode4.EngineNotification += (msg, color) =>
                {
                    if (!_isPromptActive)
                    {
                        TelemetryFormatter.PrintMessage($" >>> {msg}", color);
                    }
                };
            }

            _hook.EventLogged += record =>
            {
                if (!_isPromptActive)
                {
                    TelemetryFormatter.PrintEvent(record);
                }
            };

            _hook.ModeChanged += (modeId, modeName) =>
            {
                if (!_isPromptActive)
                {
                    TelemetryFormatter.PrintMessage(
                        $"\n[*** TEST MODE SWITCHED ***] Now Active: Mode {modeId} - {modeName}\n",
                        ConsoleColor.Green);
                }
            };

            RefreshHeader();

            RunInteractiveConsoleLoop();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Fatal Error: {ex.Message}\n{ex.StackTrace}");
            Console.ResetColor();
        }
        finally
        {
            CleanupAndExit();
        }
    }

    private static void SafeClear()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.Clear();
            }
        }
        catch { }
    }

    private static void RefreshHeader()
    {
        if (_hook == null) return;

        SafeClear();
        string triggerDesc = _hook.ActiveMode.TargetTriggerOnly
            ? "Win+C (0x43) Only"
            : "Any Key while Win held";

        TelemetryFormatter.PrintHeader(
            _hook.ActiveModeIndex,
            _hook.ActiveMode.Name,
            triggerDesc,
            _hook.IsMode3SwallowingWinUp());
    }

    private static void RunInteractiveConsoleLoop()
    {
        if (Console.IsInputRedirected)
        {
            // Fallback loop for non-interactive / redirected console execution
            while (_isRunning)
            {
                string? line = Console.ReadLine();
                if (line == null) break;

                line = line.Trim();
                if (line.Equals("q", StringComparison.OrdinalIgnoreCase) || line.Equals("quit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                HandleCommandString(line);
            }
            return;
        }

        while (_isRunning)
        {
            ConsoleKeyInfo keyInfo;
            try
            {
                keyInfo = Console.ReadKey(intercept: true);
            }
            catch
            {
                break;
            }

            switch (keyInfo.Key)
            {
                case ConsoleKey.F1 or ConsoleKey.D0 or ConsoleKey.NumPad0:
                    _hook?.SetMode(0);
                    break;

                case ConsoleKey.F2 or ConsoleKey.D1 or ConsoleKey.NumPad1:
                    _hook?.SetMode(1);
                    break;

                case ConsoleKey.F3 or ConsoleKey.D2 or ConsoleKey.NumPad2:
                    _hook?.SetMode(2);
                    break;

                case ConsoleKey.F4 or ConsoleKey.D3 or ConsoleKey.NumPad3:
                    _hook?.SetMode(3);
                    break;

                case ConsoleKey.F5 or ConsoleKey.D4 or ConsoleKey.NumPad4:
                    _hook?.SetMode(4);
                    break;

                case ConsoleKey.T:
                    _hook?.ToggleTriggerFilter();
                    RefreshHeader();
                    TelemetryFormatter.PrintMessage(
                        $"[TRIGGER FILTER TOGGLED] Mode Target Trigger Only: {_hook?.ActiveMode.TargetTriggerOnly}",
                        ConsoleColor.Yellow);
                    break;

                case ConsoleKey.S:
                    _hook?.ToggleMode3WinUpSubmode();
                    RefreshHeader();
                    bool swallowing = _hook?.IsMode3SwallowingWinUp() ?? false;
                    TelemetryFormatter.PrintMessage(
                        swallowing
                            ? "[MODE 3 SUBMODE] Physical Win KeyUp SWALLOWING enabled! (Simulating Stuck Win Key Trap)"
                            : "[MODE 3 SUBMODE] Physical Win KeyUp FORWARDING enabled (Safe)",
                        swallowing ? ConsoleColor.Red : ConsoleColor.Green);
                    break;

                case ConsoleKey.C:
                    RefreshHeader();
                    break;

                case ConsoleKey.M:
                    ShowInteractivePrompt();
                    break;

                case ConsoleKey.H:
                    PrintHelp();
                    break;

                case ConsoleKey.Q or ConsoleKey.Escape:
                    _isRunning = false;
                    return;
            }
        }
    }

    private static void HandleCommandString(string line)
    {
        switch (line.ToLowerInvariant())
        {
            case "0" or "mode 0":
                _hook?.SetMode(0);
                break;
            case "1" or "mode 1":
                _hook?.SetMode(1);
                break;
            case "2" or "mode 2":
                _hook?.SetMode(2);
                break;
            case "3" or "mode 3":
                _hook?.SetMode(3);
                break;
            case "4" or "mode 4":
                _hook?.SetMode(4);
                break;
            case "t":
                _hook?.ToggleTriggerFilter();
                RefreshHeader();
                break;
            case "s":
                _hook?.ToggleMode3WinUpSubmode();
                RefreshHeader();
                break;
            case "c":
                RefreshHeader();
                break;
            case "h" or "help":
                PrintHelp();
                break;
        }
    }

    private static void ShowInteractivePrompt()
    {
        _isPromptActive = true;
        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.WriteLine("\n=== INTERACTIVE MODE SELECTION ===");
        Console.ResetColor();
        Console.WriteLine(" Select a test mode:");
        Console.WriteLine("  [0] Mode 0: Pure Monitor (100% passthrough, logs everything)");
        Console.WriteLine("  [1] Mode 1: AutoHotkey / MenuMask (Injects 0xE8 down/up, forwards physical Win keyup)");
        Console.WriteLine("  [2] Mode 2: PowerToys Dummy Key (Injects 0xFF down/up extra:0x101, forwards Win keyup)");
        Console.WriteLine("  [3] Mode 3: Synthetic Win-Up on Trigger (Tests swallowing vs forwarding physical Win keyup)");
        Console.WriteLine("  [4] Mode 4: MicLock Production Engine (Full KeyRemapManager integration)");
        Console.Write("Enter mode number (0-4) or press Enter to cancel: ");

        string? input = Console.ReadLine();
        if (int.TryParse(input, out int mode) && mode >= 0 && mode <= 4)
        {
            _hook?.SetMode(mode);
        }

        _isPromptActive = false;
        RefreshHeader();
    }

    private static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n--- DIAGNOSTIC SUITE HELP ---");
        Console.WriteLine(" F1-F5 (Global or Console) : Switch between Modes 0 through 4");
        Console.WriteLine(" 0-4 (Console)             : Switch between Modes 0 through 4");
        Console.WriteLine(" T                         : Toggle trigger filter (Win+C only vs Any Win+Key)");
        Console.WriteLine(" S                         : In Mode 3, toggle swallowing vs forwarding physical Win keyup");
        Console.WriteLine(" M                         : Open interactive mode menu");
        Console.WriteLine(" C                         : Clear screen and re-render header");
        Console.WriteLine(" Q / Escape                : Quit diagnostic tool");
        Console.WriteLine("-----------------------------\n");
        Console.ResetColor();
    }

    private static void CleanupAndExit()
    {
        _isRunning = false;
        _hook?.Dispose();
        _hook = null;
        Console.ResetColor();
        Environment.Exit(0);
    }
}
