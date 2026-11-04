# Keyboard Diagnostics Suite (.NET 8)

A standalone, high-precision diagnostic and testing module for low-level keyboard event hooks (`WH_KEYBOARD_LL`) in C# .NET 8.

## Location
`C:\Users\ricar\Downloads\mic-lock\tools\KeyboardDiagnostics`

---

## Capabilities & Telemetry

The tool captures every low-level keyboard event with sub-millisecond / microsecond precision without dropping hooks:
1. **Timestamp & Delta:** Exact timestamp (`HH:mm:ss.fff` / `HH:mm:ss.ffffff`) and microsecond delta `(+XXX.X µs)` from the preceding event using `Stopwatch.GetTimestamp()`.
2. **Message Type:** `WM_KEYDOWN` (0x0100), `WM_KEYUP` (0x0101), `WM_SYSKEYDOWN` (0x0104), `WM_SYSKEYUP` (0x0105).
3. **Key Information:** Virtual Key Code in hex (e.g. `0x5B`) and human-readable name (e.g. `Left Windows`), plus raw Scan Code.
4. **LLKHF Flags Bitmask:**
   - Extended key bit (`LLKHF_EXTENDED` = 0x01)
   - Injected bit (`LLKHF_INJECTED` = 0x10)
   - Lower integrity level injected bit (`LLKHF_LOWER_IL_INJECTED` = 0x02)
   - Alt down bit (`LLKHF_ALTDOWN` = 0x20)
   - Key-up transition bit (`LLKHF_UP` = 0x80)
5. **dwExtraInfo:** Exact hexadecimal extra info and signature identification:
   - `0x00002000`: MicLock Dummy Key (`DUMMY_KEY_FLAG`)
   - `0x00000101`: PowerToys Shortcut Flag (`POWERTOYS_SHORTCUT_FLAG`)
   - `0x00001001`: AutoHotkey MenuMask Flag
   - `0x00003003`: Synthetic Win-Up Diagnostic Flag
6. **OS Asynchronous Key State:** Direct snapshot via `GetAsyncKeyState`:
   - `VK_LWIN` (0x5B)
   - `VK_RWIN` (0x5C)
   - `VK_CONTROL` (0x11)
   - `VK_MENU` (0x12)
   - `VK_SHIFT` (0x10)
7. **Foreground Window Context:** Active Window Title, Process Name, and Process ID at the instant the key was intercepted.

---

## Selectable Hook Test Modes

Switchable on-the-fly via **F1–F5** (globally or within console), numerical keys **0–4**, or interactive menu `[M]`:

| Mode | Name | Description | Key Behavior |
|---|---|---|---|
| **Mode 0** | Pure Monitor | 100% Passthrough | Intercepts, logs all telemetry, never swallows or injects events. |
| **Mode 1** | AutoHotkey / MenuMask Mode | AutoHotkey Start Menu suppression | Injects `VK_MENU_MASK` (0xE8) down/up when Win is held and trigger arrives; **forwards** physical Win keyup to OS. |
| **Mode 2** | PowerToys Dummy Key Mode | PowerToys Keyboard Manager suppression | Injects `VK_DUMMY` (0xFF) down/up with `extraInfo = 0x101`; swallows trigger key, forwards physical Win keyup. |
| **Mode 3** | Synthetic Win-Up on Trigger | Win-Up Injection & Trap Testing | Injects synthetic `VK_LWIN` keyup with `KEYEVENTF_EXTENDEDKEY` on trigger. Includes toggle `[S]` to test **Swallowing** vs **Forwarding** physical Win keyup (reproducing the stuck Win key OS trap). |
| **Mode 4** | MicLock Production Engine | Real Engine Validation | Instantiates and runs `KeyRemapManager` from `MicLock.Core.Sys` directly, streaming production engine events alongside raw hook telemetry. |

---

## Interactive Controls

- **`[F1]` / `[0]`**: Switch to Mode 0 (Pure Monitor)
- **`[F2]` / `[1]`**: Switch to Mode 1 (AutoHotkey MenuMask)
- **`[F3]` / `[2]`**: Switch to Mode 2 (PowerToys Dummy Key)
- **`[F4]` / `[3]`**: Switch to Mode 3 (Synthetic Win-Up on Trigger)
- **`[F5]` / `[4]`**: Switch to Mode 4 (MicLock Production Engine)
- **`[T]`**: Toggle Trigger Filter between `Win+C (0x43) Only` and `Any Key while Win held`
- **`[S]`**: In Mode 3, toggle physical Win keyup handling between `FORWARD (Safe)` and `SWALLOW (Stuck Win Bug Test)`
- **`[M]`**: Open interactive mode selection prompt
- **`[C]`**: Clear screen and re-render header
- **`[H]`**: Display help guide
- **`[Q]` / `[Esc]`**: Gracefully uninstall hook and quit

---

## How to Build & Run

### Build
```powershell
$env:PATH = "C:\Users\ricar\.dotnet;$env:PATH"
cd C:\Users\ricar\Downloads\mic-lock
dotnet build tools\KeyboardDiagnostics\KeyboardDiagnostics.csproj
```

### Run
```powershell
& "C:\Users\ricar\Downloads\mic-lock\tools\KeyboardDiagnostics\bin\Debug\net8.0-windows\KeyboardDiagnostics.exe"
```
