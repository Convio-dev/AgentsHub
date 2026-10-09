# HermesHub: Ultra-Lightweight Standalone Hermes Desktop Hub (v1.0.1)

HermesHub is a high-performance Windows desktop tray utility and LAN reverse proxy for the Hermes agent engine built with C# and .NET 10 WinForms, optimized for fast startup and low memory footprint (< 25 MB RAM).

## Key Features

- **System Tray Lifecycle**: Runs quietly in the Windows system tray with single-instance mutex enforcement. Minimizes directly to tray on minimize or close.
- **Hermes Process Controller**:
  - Starts `hermes dashboard --port <PORT>` in a detached background process.
  - Safely stops the agent process tree, child node processes, and Python runtimes.
  - Real-time heartbeat poller continuously probes the engine port to report live status (`Online`, `Starting`, `Offline`).
- **LAN Broadcast Reverse Proxy**:
  - Transparent TCP reverse proxy listening on `0.0.0.0:<LAN_PORT>` that bridges requests to `127.0.0.1:<ENGINE_PORT>`.
  - Full support for HTTP, WebSocket upgrades, and Server-Sent Events (SSE) with strict CRLF preservation.
  - Automatically resolves active local LAN IPv4 address (Wi-Fi or Ethernet) with 1-click clipboard copy.
  - Interactive offline landing page allowing users to launch Hermes directly over the LAN with live auto-refresh.
  - Releases the port immediately when disabled.
- **Windows Defender Firewall Helper**:
  - Real-time status indicator (`Firewall: Allowed` or `Allow in Firewall`).
  - 1-click elevation action that automatically creates the inbound TCP firewall rule for the active LAN port.
- **Custom Port Configuration**:
  - Custom configurable ports for both Hermes Daemon (default: `9119`) and LAN Broadcast (default: `9191`).
  - Persistent registry storage with dynamic proxy rebound on change.
- **Remote Connection Alerts**:
  - Displays a Windows notification whenever a new remote IP address on the local network connects to the Hermes LAN broadcast.
- **Sleek Modern Dark UI**:
  - Borderless 450x600 window with draggable header.
  - Dark Emerald and Zinc palette (`#09090b`, `#18181b`, `#27272a`, `#10b981`).
  - High-contrast typography and full keyboard navigation.
- **Automation & Persistence**:
  - Toggle Windows autostart via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
  - Optional auto-start for Hermes engine on launch.
  - Optional auto-enable for LAN broadcast on launch.
  - Live process working set memory monitor in the status bar.

## Project Structure

```text
HermesOn/
├── HermesHub.csproj               # .NET 10 project with Native AOT & Trimming roots
├── Program.cs                     # Mutex guard, DPI awareness, startup dispatch
├── MainForm.cs                    # Controller logic, tray integration, proxy binding
├── MainForm.Designer.cs           # Dark-mode GUI layout and control definitions
├── Services/
│   ├── HermesProcessService.cs    # Background process spawning, heartbeat & cleanup
│   └── LanProxyService.cs         # Low-overhead TCP forwarder with remote alerts
└── Helpers/
    ├── FirewallHelper.cs          # Windows Defender Firewall inspection and registration
    ├── NetworkHelper.cs           # Active LAN IPv4 resolver (Ethernet & Wi-Fi)
    └── StartupHelper.cs           # Windows registry autostart and settings storage
```

## Building and Publishing

### Development Build
```powershell
dotnet build -c Release
```

### Single-File Self-Contained Executable
```powershell
dotnet publish -c Release -r win-x64 /p:PublishAot=false /p:PublishTrimmed=true /p:_SuppressWinFormsTrimError=true /p:PublishSingleFile=true
```

Output binary:
`bin\Release\net10.0-windows\win-x64\publish\HermesHub.exe`

### Native AOT Build (requires Visual Studio C++ Build Tools)
```powershell
dotnet publish -c Release -r win-x64
```
