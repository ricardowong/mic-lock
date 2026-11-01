# 🎙️ MicAnchor

**Lock your microphone volume. Remap custom mute hotkeys. Always know when you're muted.**

[![License: MIT](https://img.shields.io/badge/License-MIT-teal.svg)](LICENSE)
[![Platform: Windows 10 / 11](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg)](https://microsoft.com/windows)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com)
[![Build Status](https://img.shields.io/badge/Tests-69%20Passed-brightgreen.svg)]()

---

## 💡 Why MicAnchor?

Have you ever joined a Discord call, a Zoom meeting, or a game of CS:GO only for someone to tell you: *"Hey, we can barely hear you"*? 

Popular apps constantly adjust your microphone gain in the background without asking. **MicAnchor** runs quietly in your system tray to keep your mic level anchored exactly where you set it, while giving you powerful mute hotkeys, quick device switching, and a subtle on-screen overlay so you never talk on mute again.

---

## ✨ Features

### 🔒 Rock-Solid Volume Lock
Set your microphone level (e.g., 100% or 80%), and MicAnchor keeps it there. If Discord, Zoom, Steam, or Chrome tries to lower your slider, MicAnchor instantly restores it before anyone notices.

### ⌨️ Remap Any Key (Push-to-Talk, Mute & Switch)
Turn any keyboard key, dedicated mic button (like Keychron or laptop mic keys), media key, or custom shortcut combo (`Win+C`, `Ctrl+Alt+M`, `Ctrl+Shift+Alt+M`) into an instant action:
* **Toggle Mute:** One tap to mute or unmute.
* **Push-to-Talk:** Speak while holding the key down; release to stay muted.
* **Push-to-Mute:** Quick cough button—mutes only while held.
* **Switch Microphone:** Cycle between your headset, USB mic, and webcam on the fly.
* **Lock Volume:** Snap your volume back to your target level with a single press.
* *Includes an intuitive **Key Recorder**—just click "Record Key" and tap the key you want.*

### 📌 Always Know When You're Muted
Never talk to yourself for 5 minutes wondering why nobody replies:
* **Floating Banner:** A sleek, dark on-screen notification pops up whenever you mute, unmute, change volume, or switch devices.
* **Persistent Mute Badge:** While muted, a compact, semi-transparent badge stays visible in the corner of your screen. It is completely click-through, so you can keep gaming, typing, or presenting without it getting in the way.

### 🎚️ System Tray Mouse-Wheel Volume
Need a quick volume adjustment? Just hover your mouse over the microphone icon in your Windows system tray and scroll your wheel up or down.

### 🔄 Fast Microphone Switching
Switch your system-wide default recording device with a keyboard shortcut, from the dashboard, or directly from the system tray menu—no need to dig into Windows Sound Settings.

### 🔋 Invisible Performance
* **Zero battery drain:** Sleeps silently in the background and only wakes when an audio event occurs.
* **Zero Administrator rights needed:** Installs and runs per-user. No annoying UAC prompts.
* **Auto-reconnects:** Unplugged your USB microphone? MicAnchor automatically re-locks it the moment you plug it back in.

---

## 📦 Download & Quick Start

### Option 1: Installer (Recommended)
Download **`MicLock-Setup-v1.0.0.exe`** from [Releases](../../releases).
* Installs cleanly to `%LOCALAPPDATA%\Programs\MicLock`.
* Adds optional desktop shortcut and automatic Windows startup.
* Clean uninstaller included.

### Option 2: Portable
Download **`MicLock.exe`** from [Releases](../../releases).
* Single standalone file—no installation needed. Drop it in any folder and run.
* Also includes **`MicMute.exe`**, a lightweight companion dedicated solely to 1-click mute.

---

## 🚀 30-Second Setup

1. **Pick your microphone:** Select your primary mic from the dropdown list.
2. **Set your target volume:** Slide the bar to your preferred level (or click `100%`).
3. **Turn on the lock:** Toggle the switch to **Active**.
4. *(Optional)* **Set your hotkey:** Select an action (e.g. *Toggle Mute*), click **Record Key**, and press your preferred key.

Minimize the window, and MicAnchor will sit quietly in your taskbar.

---

## 🛠️ For Developers & Power Users

<details>
<summary><b>Click to expand architecture & build instructions</b></summary>

### Architecture
MicAnchor is written in modern C# (.NET 8.0) and WPF:
* **Audio Engine:** Uses Windows WASAPI CoreAudio interfaces to listen to native audio endpoint notifications rather than running continuous polling loops.
* **Input Interop:** Uses a low-level Windows keyboard hook with key-repeat suppression to handle both single hardware keys and complex modifier combinations.
* **Device Control:** Direct COM interop with Windows CoreAudio endpoints to cycle default recording roles across Console, Multimedia, and Communications.

### Building from Source
```powershell
# 1. Clone repository
git clone https://github.com/ricardowong/mic-lock.git
cd mic-lock

# 2. Run test suite (53 tests)
dotnet test

# 3. Publish single-file binary
dotnet publish src/MicLock.App/MicLock.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist/win-x64

# 4. Compile Installer (requires Inno Setup 6)
iscc installer/MicLock.iss
```
</details>

---

## 📄 License

Distributed under the [MIT License](LICENSE). Open-source, free, and telemetry-free.
