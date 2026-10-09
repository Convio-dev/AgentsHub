# Hermes Desktop Hub

A lightweight, standalone Windows tray utility and LAN reverse proxy for the [Hermes](https://github.com/anthropics) agent engine. Hermes Desktop Hub packages process lifecycle management, LAN broadcasting, QR code generation, and firewall management into a portable, single-file Windows executable (~16 MB) with low memory usage (< 25 MB RAM).

[![License: MIT](https://img.shields.io/badge/License-MIT-emerald.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-blue.svg)]()
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)]()

---

## Features

- **Portable Single-File Executable**: Completely self-contained binary. No .NET runtime installation required. Copy `HermesHub.exe` to any folder or USB drive and run.
- **Hermes Process Controller**:
  - Spawns `hermes dashboard --port <PORT>` in a detached background process.
  - Clean process termination across child process trees (Python, Node, shell).
  - Background heartbeat poller reporting real-time status (`Online`, `Starting`, `Offline`, `Error`).
- **LAN Broadcast Reverse Proxy**:
  - TCP proxy listening on `0.0.0.0:<LAN_PORT>` that forwards traffic to `127.0.0.1:<ENGINE_PORT>`.
  - Transparent support for HTTP, WebSocket connections, and Server-Sent Events (SSE).
  - Resolves local network IPv4 address (Ethernet and Wi-Fi) with 1-click clipboard copy.
  - Serves a built-in offline landing page over LAN with 1-click engine startup and auto-refresh.
- **Mobile QR Code Sharing**:
  - Generates an instant, scannable QR code for the LAN address to quickly open the dashboard on phones or tablets connected to the same Wi-Fi.
- **Windows Defender Firewall Integration**:
  - Automatically detects whether an inbound firewall rule allows the LAN proxy port.
  - One-click elevation to create the inbound rule without manual firewall configuration.
- **Remote Client Notifications**:
  - Notifies you via Windows balloon alert when a new device on your LAN connects to the hub.
  - Status bar client counter showing active connections.
- **System Tray Integration**:
  - Minimizes to the Windows notification area.
  - Single-instance mutex prevents duplicate processes; launching a second copy focuses the existing window.
  - Tray context menu provides quick controls to start/stop the engine, toggle LAN broadcast, open WebUI, or quit.
- **Configuration Persistence**:
  - Custom configurable ports for the engine and LAN broadcast.
  - Preferences saved under `HKCU\Software\HermesHub`.
  - Optional Windows startup on boot (`--tray` flag).

---

## Quick Start

### 1. Download or Build
Grab the published `HermesHub.exe` or build it from source (see [Building from Source](#building-from-source)).

### 2. Run
Launch `HermesHub.exe`. By default:
- **Hermes Engine Port**: `9119`
- **LAN Broadcast Port**: `9191`

### 3. Usage
- Click **Start Engine** to launch the local Hermes agent dashboard.
- Toggle **Broadcast on Local Network** to allow access from other devices on your Wi-Fi/LAN.
- Click **QR Code** to scan and connect directly from mobile devices.
- Click **Allow in Firewall** if prompted so devices on your local network can reach the port.

---

## Command-Line Arguments

| Flag | Description |
| :--- | :--- |
| `--tray` | Starts the application minimized to the system tray. Used for Windows startup. |
| `-minimized` | Alias for `--tray`. |

---

## Configuration & Registry Storage

All application settings are stored under the current user's registry hive:

```text
HKEY_CURRENT_USER\Software\HermesHub
  ├── EnginePort          (DWORD, default: 9119)
  ├── LanPort             (DWORD, default: 9191)
  ├── AutoStartHermes     (DWORD, 0 or 1)
  ├── AutoEnableLan       (DWORD, 0 or 1)
  └── NotifyRemoteClients (DWORD, 0 or 1)
```

Autostart is toggled via:
```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
  └── HermesHub = "<Path>\HermesHub.exe --tray"
```

---

## Project Structure

```text
HermesOn/
├── HermesHub.csproj               # .NET 10 project file (configured for single-file self-contained publish)
├── HermesHub.iss                  # Inno Setup 6 installer script
├── build-installer.cmd            # Automated publish and setup compilation script
├── Program.cs                     # Mutex guard, single-instance signaling, startup handler
├── MainForm.cs                    # Main form logic, tray lifecycle, event coordination
├── MainForm.Designer.cs           # Dark-mode UI components and layout
├── Services/
│   ├── HermesProcessService.cs    # Process controller, background heartbeat, stdout/stderr capture
│   └── LanProxyService.cs         # Low-overhead TCP forwarder, WebSocket support, remote client tracking
├── Helpers/
│   ├── FirewallHelper.cs          # Windows Defender Firewall inspection and inbound rule creation
│   ├── NetworkHelper.cs           # Active LAN IPv4 resolver (Ethernet & Wi-Fi adapter priority)
│   ├── QrCodeHelper.cs            # Vector QR code generation for LAN connection sharing
│   └── StartupHelper.cs           # Windows registry settings and autostart persistence
├── assets/
│   └── app.ico                    # High-resolution application and installer icon
├── dist/
│   └── HermesHub-Setup-v1.0.2.exe # Generated Windows setup installer
├── LICENSE                        # MIT License
└── README.md                      # Project documentation
```

---

## Building from Source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows 10 or 11 (x64)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (for creating the installer `.exe`)

### Development Run
To run the project in development mode:
```powershell
dotnet run
```

### Build Single-File Standalone Executable
To produce a single, portable, self-contained binary:
```powershell
dotnet publish -c Release
```

The resulting standalone executable will be located at:
```text
bin\Release\net10.0-windows\win-x64\publish\HermesHub.exe
```

### Build Windows Installer (.exe)
To compile both the standalone executable and the Inno Setup installer package in one step:
```cmd
.\build-installer.cmd
```

Or manually via the Inno Setup CLI:
```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" HermesHub.iss
```

The resulting setup installer is saved to:
```text
dist\HermesHub-Setup-v1.0.2.exe
```

---

## License

This project is licensed under the [MIT License](LICENSE).
