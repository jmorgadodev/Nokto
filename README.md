<p align="center">
  <b>🌐 Language:</b> &nbsp;
  <b>English</b> &nbsp;|&nbsp;
  <a href="README.es.md">Español</a>
</p>

# Nokto 🌒

> **Local Desktop Operations, Telemetry, and Automation Console**

[![GitHub Release](https://img.shields.io/github/v/release/jmorgadodev/Nokto?color=00D2FF&label=Release)](https://github.com/jmorgadodev/Nokto/releases/latest)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010%20(x64)-0078D6)](https://github.com/jmorgadodev/Nokto)
[![Runtime](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Privacy](https://img.shields.io/badge/External%20Telemetry-0%25%20(100%25%20Offline)-success)](https://github.com/jmorgadodev/Nokto)

**Nokto** is an offline-first Windows operations console engineered for power users, developers, and workstation monitoring. It centralizes real-time hardware telemetry, low-latency WASAPI audio management, concurrent routine workflows, and local LAN mobile remote control into a single unified cockpit—with **zero cloud dependencies and absolute privacy**.

<p align="center">
  <img src="assets/screenshots/01_cockpit_telemetria.png" alt="Nokto Operations Cockpit" width="100%">
</p>

---

## ⚡ Quick CLI Installation (PowerShell)

Open **PowerShell** and run the one-liner command for your preferred distribution:

### Option A: Portable Standalone Executable (Single-File)
Downloads the self-contained executable directly to your Downloads folder and starts it:
```powershell
irm https://github.com/jmorgadodev/Nokto/releases/latest/download/Nokto-Portable-x64.exe -OutFile "$HOME\Downloads\Nokto.exe"; Start-Process "$HOME\Downloads\Nokto.exe"
```

### Option B: Unattended Silent Installation (Windows Installer)
Downloads the signed installer and installs it silently in the background:
```powershell
$setup = "$env:TEMP\Nokto-Setup.exe"; irm https://github.com/jmorgadodev/Nokto/releases/latest/download/Nokto-Setup-x64.exe -OutFile $setup; Start-Process $setup -ArgumentList "/VERYSILENT /NORESTART" -Wait; Remove-Item $setup
```

---

## 📦 Direct Downloads

| Format | Download Link | Description |
| :--- | :--- | :--- |
| **Portable (x64)** | [⬇ Nokto-Portable-x64.exe](https://github.com/jmorgadodev/Nokto/releases/latest/download/Nokto-Portable-x64.exe) | Single standalone binary (~65 MB). Zero installation required; ideal for USB drives or isolated testing. |
| **Installer (x64)** | [⬇ Nokto-Setup-x64.exe](https://github.com/jmorgadodev/Nokto/releases/latest/download/Nokto-Setup-x64.exe) | Standard Windows setup wizard with Start Menu shortcuts and a clean uninstaller. |

---

## 🛠 Key Features

### 1. Operations Cockpit & Hardware Telemetry
- **Real-Time Passive Metrics:** Continuous kernel/user CPU monitoring, RAM allocation, dual-GPU utilization (NVIDIA + Intel Iris Xe), and network throughput with near-zero overhead.
- **Low-Latency WASAPI Audio & Microphone Switcher:** Instant default endpoint switching, master volume slider, and global mute shortcuts (`Ctrl+Shift+M` for mic / `Ctrl+Shift+S` for output).
- **AI Dev Radar:** Automatically detects local development environments (Codex Desktop, Antigravity IDE, Claude, OpenCode) and reads local token quota usage on disk without credentials.

<p align="center">
  <img src="assets/screenshots/01_cockpit_telemetria.png" alt="Operations Cockpit and Telemetry" width="100%">
</p>

### 2. Immediate Manual Control & Concurrent Tasks
- **Multi-Condition Triggers:** Fire one-off actions via Countdown timers, specific timestamps, or peripheral idle detection.
- **Concurrent Execution Engine:** Run multiple tasks simultaneously without interference; each active countdown gets its own live capsule with an instant cancel button.
- **Grace Overlay:** Customizable on-screen warning window before executing critical power actions (shutdown, sleep, lock) with options to snooze or abort.

<p align="center">
  <img src="assets/screenshots/02_control_manual.png" alt="Immediate Manual Control and Multi-Variable Triggers" width="100%">
</p>

### 3. Routine Configurator (Sequential Pipeline)
- **Block-Based Pipeline:** Chain automated actions without forced shutdown steps (launch apps, adjust audio levels, pause playback, turn off displays, or hibernate).
- **Native Start Menu App Discovery:** Automatically scans Windows Start Menu shortcuts, extracting official 32×32 high-res application icons for rapid selection.
- **Advanced Triggers:** Trigger workflows upon process exit (e.g., render completion), scheduled times (recurring day selector), peripheral idle, or network traffic drops.

<p align="center">
  <img src="assets/screenshots/03_rutinas_pipeline.png" alt="Routine Configurator and Sequential Pipeline" width="100%">
</p>

### 4. Offline Mobile LAN Remote Control & Evidence Gallery
- **Embedded Lightweight HTTP Server:** Operates strictly within your private local network (e.g., `http://192.168.1.X:4884`).
- **Live Screen Snapshots:** View low-latency desktop screen captures directly from your smartphone browser to monitor long-running builds or renders remotely.
- **Instant QR Pairing:** Scan the native QR code generated on the desktop to launch the mobile web UI without installing any phone apps.
- **Evidence Audit Gallery:** Automated screen captures before executing destructive or power routines, with instant in-app gallery review.

<p align="center">
  <img src="assets/screenshots/07_control_remoto_evidencias.png" alt="Mobile LAN Remote Control and Evidence Gallery" width="100%">
</p>

### 5. System Settings, Hardware Guardian & AI Prioritization
- **Auto-Update Engine & Visual Themes:** Native GitHub release verification with changelog review, hot-swapping portable updater, language switching (ES/EN), and ergonomic Day/Night visual themes.
- **Panic Hotkey & Battery Guardian:** System-wide panic abort hotkey (`Pause`) and automated smart battery protection (10% critical threshold with zero-drain hibernation).
- **Audio Switcher & AI Environment Manager:** Low-latency WASAPI input/output device selector, global mute hotkeys, and custom priority ordering for local AI development tools.

<p align="center">
  <img src="assets/screenshots/04_ajustes_actualizaciones_apariencia.png" alt="System Updates, Visual Appearance and Startup Settings" width="100%">
</p>

<p align="center">
  <img src="assets/screenshots/05_ajustes_guardian_modularidad.png" alt="Panic Hotkey, Laptop Battery Guardian and Modularity" width="100%">
</p>

<p align="center">
  <img src="assets/screenshots/06_ajustes_audio_herramientas_ia.png" alt="WASAPI Audio Configuration and Local AI Dev Tools Manager" width="100%">
</p>

---

## 🔒 Privacy & Architecture
- **0% External Telemetry:** Nokto never transmits diagnostic metrics, analytics, or credentials to external cloud services.
- **Strict LAN Scoping:** The mobile remote control server binds solely to private subnets (`192.168.x.x`, `10.x.x.x`, `127.0.0.1`) and enforces local origin boundaries.
- **Isolated Persistence:** All presets, user settings, and audit logs are stored locally as standard JSON files inside the application's `data/` directory.

---

## 💻 Development & Building
For build prerequisites, automated test execution, and compilation from source code, please refer to [CONTRIBUTING.md](CONTRIBUTING.md).

---

## 👤 Author
Developed by **Jorge Morgado**  
- LinkedIn: [in/jorge-morgado](https://www.linkedin.com/in/jorge-morgado/)  
- GitHub: [@jmorgadodev](https://github.com/jmorgadodev)

---

## 📄 License
This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
