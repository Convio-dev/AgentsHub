# Agents Hub

**One desktop window for every AI coding agent on your machine.**

Agents Hub is a portable Windows control plane for CLI coding agents. It runs Hermes, Claude Code and Antigravity side by side, gives each one a real terminal, isolates multiple accounts, routes Claude through your own gateway, and exposes the whole workspace to your phone over the LAN, all from a single ~17 MB executable.

[![License](https://img.shields.io/badge/license-MIT-3fb950?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D6?style=flat-square)]()
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)]()
[![Version](https://img.shields.io/badge/version-1.0.3-2563eb?style=flat-square)]()
[![UI](https://img.shields.io/badge/UI-WebView2-2ea44f?style=flat-square)]()
[![Portable](https://img.shields.io/badge/dist-single%20file-f59e0b?style=flat-square)]()
[![PRs](https://img.shields.io/badge/PRs-welcome-brightgreen?style=flat-square)]()

---

## Why it exists

Running several AI coding agents usually means several terminals, several config files, and several sets of credentials that quietly overwrite each other. Agents Hub treats that as one workspace:

- **Process lifecycle**: start, stop and restart agents without hunting for stray child processes.
- **Real terminals**: every agent gets a genuine ConPTY session, not a text box.
- **Account isolation**: multiple Google accounts for Antigravity, each with its own credentials and port.
- **Project workspaces**: bind working directories, environment variables, and specific agent profiles into cohesive setups.
- **Quake HUD terminal**: drop down a top-screen terminal console instantly from anywhere with a global hotkey.
- **Bring your own gateway**: Claude Code can run against OpenRouter, OpenAI, Groq, DeepSeek or local Ollama.
- **Reach it from anywhere on the LAN**: scan a QR code and the dashboard opens on your phone.

---

## Feature highlights

### Multi-agent process control
Manages Hermes, Claude Code and Antigravity CLI as first-class engines, plus any custom command you define. Every engine gets a live state (`Starting`, `Running`, `Error`, `Offline`), PID, port and start time, with tree-aware termination that never leaves orphans behind.

### Native ConPTY terminals and interactive sync
Each agent runs inside a real Windows pseudoconsole, streamed into an xterm.js surface. Sessions are addressed individually, resized to fit, buffered, and replayed when you switch back to a tab. The embedded dashboard terminal and standalone workspace window synchronize their active tabs in real time.

### Quake-style HUD terminal
A drop-down top-of-screen terminal inspired by classic gaming consoles. Pressing the configurable global hotkey (such as `F12` or `~`) smoothly slides down a semi-transparent terminal overlay across 94% of the screen width. Switch between all active agent tabs, trigger fast run/restart actions, or clear buffers without leaving your current application.

### Project workspaces and directory isolation
Create, configure and switch full project workspaces. Each workspace specifies its own project root directory, linked Antigravity profile, primary and secondary CLIs, custom environment variables, and color tag. Activating a workspace automatically switches the active agent profile and redirects paths seamlessly.

### Dual-pane split view workspace
Run two agent sessions side by side in the standalone workspace window. Supports horizontal and vertical split modes with a resizable divider, independent pane tab selectors, and optional cloned streams for simultaneous monitoring.

### Antigravity multi-account, properly isolated
Each profile gets its own **VirtualHome**: `USERPROFILE`, `HOME`, `APPDATA` and `LOCALAPPDATA` are redirected into a per-profile sandbox, so two accounts never share a session. Credentials are read from and written to the Windows Credential Manager, ports are allocated per profile, and the account email is decoded from the token so the log tells you exactly which identity launched.

### Claude Code gateway and token analytics
A local translation proxy on `127.0.0.1:9230` speaks the Anthropic Messages API and forwards to any OpenAI-compatible backend, including full streaming SSE and `tool_use` / `tool_result` mapping. Live proxy telemetry tracks prompt and completion tokens, requests per minute, model usage distributions, and estimated cost savings compared to direct Anthropic API pricing.

### AI client and VPN auto-discovery
A fast background scanner detects 15+ installed AI developer tools and VPN clients across user and system directories. Automatically discovers tools such as FreeBuff Desktop, WorkBuddy, Devin, Antigravity IDE, Cursor, VS Code, Ollama, LM Studio, Proton VPN, Cloudflare WARP, Tailscale, WireGuard, and Mullvad. Missing entries can be added to the AI Dev Tools launch deck with a single click.

### Dev Tools health diagnostics
Configured tools undergo live path verification and normalization, automatically stripping wrapping quotes from Explorer's "Copy as path" and resolving UWP App Execution Aliases (`shell:AppsFolder\...`). Clear badges (`Ready`, `Missing`, `NeedsFixing`, `Unsupported`) report status before launching.

### Windows Firewall diagnostics and 1-click repair
Integrated firewall probe analyzes Domain, Private, and Public profiles to verify whether inbound traffic to the LAN proxy is permitted. If rules are missing or partial, a one-click elevated action configures the required rules via `netsh` so mobile devices on the local network connect reliably.

### Dependencies and runtimes manager
Detects key tools the hub relies on: Node.js, Git, Python, Claude Code, Antigravity, Hermes, Cloudflare WARP and Proton VPN. Runtimes are checked in parallel on a background thread, reporting resolved paths and versions. Missing tools install in place via `winget`, official PowerShell scripts, or download pages, with installation output streamed live.

### LAN broadcast and remote control
A transparent TCP reverse proxy on `0.0.0.0:9191` forwards to the local engine, handling HTTP, WebSocket and Server-Sent Events alike. It resolves your LAN address, generates a QR code, identifies connecting devices, and serves a themed offline page when the engine is down.

### Prompt Enhancer
Rewrite rough prompts into precise instructions. Built-in presets for General, Code, Concise and Structured work out of the box. System prompts are fully customizable with temperature adjustments, one-click reset to defaults, and a Refine button that feeds generated outputs back for another pass.

### Quota and IP rotation
Resets provider quota by rotating your public IP through Cloudflare WARP or Proton VPN, guarded by cooldown limits, dual-stack verification, and an optional public-IP mask for screenshots and streaming.

### OpenCode Desktop integration
Watches the OpenCode sidecar, subscribes to its event stream, and surfaces interactive question and permission prompts directly in the dashboard so you never miss a blocked agent.

### System telemetry and theming
Live RAM and network throughput with sparklines, sampled on a background thread. Dark and light modes with an adaptive accent system that derives a full palette from a single hex value, extending to the native Windows title bar.

---

## Dashboard tour

The entire UI is a WebView2 app served from the `ui/` folder.

| View | What it does |
| :--- | :--- |
| **Dashboard & Engine** | Engine host and port, LAN toggle, active ports, standalone terminal launcher, Quake HUD trigger, token telemetry card, OpenCode quota and IP controls, live telemetry, CLI controllers, and quick shortcuts |
| **Interactive Terminals** | Embedded xterm.js terminals with real-time tab sync, split view launcher, clear buffer, and external browser link |
| **Prompt Enhancer** | Presets, customizable system instructions, temperature slider, and iterative prompt refinement |
| **Antigravity Profiles** | Create, switch, rename and isolate accounts; configure Project Workspaces with custom working directories and linked profiles; manage Claude gateway settings |
| **AI Dev Tools** | Launch external AI desktop applications, auto-detect installed AI and VPN clients, inspect launch path health, and set custom arguments |
| **AI Providers & Models** | Provider CRUD, base URLs, API keys, model discovery |
| **LAN Remote & QR** | Broadcast control, LAN address, QR code, connected device list, and Windows Firewall status with 1-click elevation |
| **Live Logs** | Filterable event stream from every subsystem |
| **Settings & About** | Theme, accent, startup options, global hotkey (Window and Quake HUD), notifications, dependencies manager, and privacy toggles |

---

## Supported engines

| Engine | Command | Default port | Notes |
| :--- | :--- | :--- | :--- |
| **Hermes** | `hermes dashboard` | `9119` | Web dashboard served in-app and over LAN |
| **Claude Code** | `claude` | `9229` | Optional gateway injection and token tracking |
| **Antigravity CLI** | `agy` | `9339`+ | One isolated profile per account |
| **Custom** | user-defined | N/A | Any command, with its own working directory |
| **Shells** | `cmd` / `powershell` | N/A | Embedded tabs, HUD dropdown, or external windows |

---

## Ports

| Port | Purpose |
| :--- | :--- |
| `9119` | Hermes dashboard (engine) |
| `9191` | LAN broadcast reverse proxy |
| `9229` | Claude Code web terminal server |
| `9230` | Claude Code to OpenRouter translation gateway |
| `9339`+ | Antigravity profiles, allocated per account |

All listeners bind to `127.0.0.1` except the LAN proxy, which is opt-in.

---

## Quick start

1. **Download** `AgentsHub-Setup-v1.0.3.exe` from [Releases](../../releases), or build from source below.
2. **Run** it. No .NET runtime install required: the binary is self-contained.
3. Open the **Settings & About** tab and check the **Dependencies** card to see which runtimes and agents are already installed.
4. Hit **Start Engine**, then launch whichever agents you want from the Dashboard.

### Command-line arguments

| Flag | Description |
| :--- | :--- |
| `--tray` | Start minimized to the system tray (used by the startup shortcut) |
| `-minimized` | Alias for `--tray` |

---

## Building from source

**Prerequisites**

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows 10 or 11 (x64)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (only for the installer)

**Run in development**

```powershell
dotnet run
```

**Build the portable single-file executable**

```powershell
dotnet publish -c Release
```

Output:

```text
bin\Release\net10.0-windows\win-x64\publish\HermesHub.exe
```

**Build the installer**

```cmd
.\build-installer.cmd
```

Or invoke the compiler directly:

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" AgentsHub.iss
```

Output:

```text
dist\AgentsHub-Setup-v1.0.3.exe
```

---

## Project structure

```text
HermesOn/
├── Program.cs                          # Single-instance guard and startup
├── MainForm.cs                         # WebView2 host, global hotkeys, and tray lifecycle
├── CliWebviewForm.cs                   # Standalone terminal workspace window with split view
├── HudTerminalForm.cs                  # Quake-style drop-down sliding terminal overlay
├── Services/
│   ├── DashboardWebBridge.cs           # The JS <-> C# API surface
│   ├── CliManagerService.cs            # Agent lifecycle and terminal sessions
│   ├── HermesProcessService.cs         # Hermes engine process controller
│   ├── LanProxyService.cs              # LAN reverse proxy
│   ├── AntigravityProfileService.cs    # Multi-account isolation
│   ├── ClaudeOpenRouterProxyService.cs # Anthropic <-> OpenAI gateway
│   ├── ProxyTelemetryService.cs        # Token usage and cost savings telemetry
│   ├── OpenCodeWatcherService.cs       # OpenCode sidecar integration
│   ├── DependencyCheckerService.cs     # Runtime and agent detection
│   ├── DevToolHealth.cs                # Target normalization and health verification
│   ├── NetworkTelemetryService.cs      # Background network sampler
│   ├── AiConfigService.cs              # Configuration store (workspaces, tools, providers)
│   ├── PromptEnhancerService.cs        # Prompt rewriting
│   ├── ProtonVpnService.cs             # VPN-driven IP rotation
│   ├── CliWebTerminalServer.cs         # Terminal web and WebSocket server
│   └── ConPty/                         # Native pseudoconsole interop
├── Helpers/                            # Firewall, client discovery, theme, network, QR, startup, shell
│   ├── FirewallHelper.cs               # Windows Firewall status check and elevated rule creator
│   ├── InstalledClients.cs             # Auto-discovery for AI tools, IDEs, and VPNs
│   ├── AppInfo.cs                      # Application identity and metadata
│   ├── ThemeManager.cs                 # Accent and theme color derivation
│   ├── NetworkHelper.cs                # Network address resolution
│   ├── QrCodeHelper.cs                 # QR code generator for LAN access
│   ├── ShellLauncher.cs                # System terminal and external process launcher
│   └── StartupHelper.cs                # Registry settings and Windows startup configuration
├── ui/                                 # Consolidated WebView2 dashboard (HTML/CSS/JS)
├── assets/                             # Icons and shared application resources
├── AgentsHub.iss                       # Inno Setup installer script
├── build-installer.cmd                 # Publish + compile installer
└── dist/                               # Installer output
```

---

## Configuration & storage

**Application config**: `%AppData%\AgentsHub\ai_config.json`

Holds providers and models, project workspaces, CLI workspaces, dashboard shortcuts, dev tool targets, Claude gateway settings, prompt presets, and active workspace configurations. Legacy `%AppData%\HermesHub` configs are migrated automatically.

**Preferences**: `HKCU\Software\AgentsHub`

Ports, theme, accent colour, startup behaviour, notification, global hotkey (Window and HUD), and privacy toggles.

**Autostart**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

---

## Security & credentials

Agents Hub handles real credentials, so it is deliberate about where they live:

- **No secrets in the repository.** API keys, tokens and account credentials are never committed. Everything is read at runtime from `%AppData%`, the registry, and the Windows Credential Manager.
- **Keys stay local.** Provider API keys live in your user config file, not in the binary.
- **Credentials are isolated per profile.** Antigravity accounts each keep their own sandboxed home and their own credential entry.
- **Changes are reversible.** Anything written into another tool's config (such as Claude's `settings.json`) is backed up first and can be restored.
- **LAN access is opt-in** and binds only when you enable broadcasting.

If you fork this project, keep `.workbuddy-ai/`, `Profiles/` and `.freebuff/` out of version control: they are already covered by `.gitignore`.

---

## Contributing

Issues and pull requests are welcome. Please keep the code building cleanly with `dotnet build -c Release` (zero warnings), and avoid committing local state or credentials.

---

## License

[MIT](LICENSE) © 2026 ZeroNuxs
