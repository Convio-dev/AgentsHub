# Agents Hub

**The desktop control plane for AI coding agents on Windows.**

Agents Hub runs Hermes, Claude Code, and Antigravity side by side in a unified desktop workspace. Every agent gets a native Windows pseudoconsole (ConPTY), isolated environment credentials, local gateway routing, visual Git management with direct GitHub synchronization, and LAN broadcasting for mobile access—packaged into a single portable binary or installer.

[![GitHub Release](https://img.shields.io/github/v/release/ZerroDevs/AgentsHub?style=flat-square&color=2563eb&label=Release)](https://github.com/ZerroDevs/AgentsHub/releases/latest)
[![Download Setup](https://img.shields.io/badge/Download-Installer%20%26%20Portable-10b981?style=flat-square&logo=windows)](https://github.com/ZerroDevs/AgentsHub/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-3fb950?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?style=flat-square)]()
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WebView2-0284c7?style=flat-square)]()
[![Git](https://img.shields.io/badge/Git-Integrated%20Repo%20Manager-f05032?style=flat-square&logo=git&logoColor=white)]()
[![Security Policy](https://img.shields.io/badge/Security-Policy-blue?style=flat-square)](SECURITY.md)
[![Contributing](https://img.shields.io/badge/Contributing-Guidelines-orange?style=flat-square)](CONTRIBUTING.md)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen?style=flat-square)](https://github.com/ZerroDevs/AgentsHub/pulls)

---

## Downloads

Download the latest pre-built multi-platform binaries directly from the **[GitHub Releases Page](https://github.com/ZerroDevs/AgentsHub/releases/latest)**:

| Asset | Platform | Type | Description |
| :--- | :--- | :--- | :--- |
| **`AgentsHub-Setup-v1.0.5.exe`** | Windows x64 | Inno Setup Installer | Standard installer with desktop shortcut, Start Menu entry, and optional Windows startup integration. |
| **`AgentsHub-v1.0.5-win-x64.zip`** | Windows x64 | Portable Archive | Self-contained single-file portable archive. Runs immediately with no installer and no .NET runtime prerequisites. |
| **`AgentsHub-v1.0.5-linux-x64.tar.gz`** | Linux x64 | Standalone Archive | Self-contained headless control-plane runtime with embedded web dashboard. |
| **`AgentsHub-v1.0.5-linux-arm64.tar.gz`** | Linux ARM64 | Standalone Archive | Self-contained package for ARM64 Linux servers, SBCs, and cloud instances. |
| **`AgentsHub-v1.0.5-osx-arm64.tar.gz`** | macOS ARM64 | Standalone Archive | Self-contained package for Apple Silicon Macs (M1/M2/M3/M4). |
| **`AgentsHub-v1.0.5-osx-x64.tar.gz`** | macOS x64 | Standalone Archive | Self-contained package for Intel-based Macs. |

---

## Why Agents Hub

Running multiple AI coding agents typically creates fragmented terminal windows, port collisions, and credentials that overwrite one another. Agents Hub unifies your agent toolchain into one cohesive desktop application:

- **Unified Process Lifecycle**: Start, stop, restart, and monitor background agent engines without hunting for stray PIDs.
- **Genuine ConPTY Terminals**: Real Windows pseudoconsole sessions streamed directly to hardware-accelerated xterm.js terminals.
- **Account & Environment Isolation**: Multiple accounts for Antigravity, each with isolated home directories and dedicated ports.
- **Visual Git & GitHub Integration**: Inspect branches, commit trees, working tree changes, interactive diffs, and push/pull to GitHub without leaving the app.
- **Quake-Style HUD Console**: Slide down a terminal overlay across the top of your screen from any Windows application with a global hotkey.
- **Bring-Your-Own-Gateway**: Route Claude Code through your own OpenRouter, Groq, DeepSeek, OpenAI, or local Ollama endpoints.
- **LAN Remote Access**: Broadcast the dashboard over your local Wi-Fi with instant QR code pairing for phones and tablets.
- **Secret & Pre-Commit Protection**: Automatic security scanner inspects staged and uncommitted files for API keys, tokens, and private keys.

---

## Features

### 1. Multi-Agent Process Engine
Controls Hermes, Claude Code, Antigravity CLI (`agy`), and user-defined custom CLIs as native managed processes. Features live status reporting (`Starting`, `Running`, `Error`, `Offline`), PID and port allocation tracking, and tree-aware process termination that prevents orphaned child workers.

### 2. Native ConPTY Terminals & Real-Time Sync
Every agent executes in an authentic Windows pseudoconsole (ConPTY) streamed to xterm.js. Sessions support dynamic window resizing, ANSI escape sequences, full buffer replay, and real-time tab state synchronization between the embedded dashboard terminal and detached workspace windows.

### 3. Dual-Pane Split Workspace
Run two independent agent sessions side by side inside the standalone workspace window. Supports horizontal and vertical split layouts, draggable dividers, independent tab selection per pane, and synchronized stream cloning.

### 4. Quake-Style Drop-Down HUD Terminal
Pressing a configurable global hotkey (default: `F12` or `~`) smoothly slides down a semi-transparent terminal overlay across 94% of your display width. Switch agent tabs, restart engines, or review output from any game, browser, or code editor without task-switching.

### 5. Git Repository Manager & Visual Graph
A dedicated Git workstation tab directly inside the hub:
- **Interactive Commit Graph**: Visual multi-lane commit history with branch tracks, author metadata, and commit timestamps.
- **Working Tree Inspector**: Staged and unstaged change tracking with status indicators (`Modified`, `Added`, `Deleted`, `Untracked`).
- **Side-by-Side & Unified Diff Viewer**: Color-coded syntax-highlighted diff inspector with file trees and patch hunk views.
- **Commit Revert**: Undo mistakes directly from the commit graph with confirmation modals, dirty working-tree safety guards, and choice between reverting on the active branch or creating a dedicated revert branch.
- **Stash & Worktree Manager**: Create, view, pop, apply, drop, and clear Git stashes, or spawn isolated working trees for parallel feature branches.
- **Direct GitHub Push & Remote Sync**: Authenticate via GitHub Personal Access Token (PAT) with automated credential detection, link repositories, fetch, pull, push commits, and publish local repositories directly to GitHub.

### 6. Pre-Commit Security & Secret Scanner
Integrated scanner analyzes uncommitted and staged files before code changes are committed. Identifies high-entropy secrets including OpenAI keys, Anthropic tokens, AWS credentials, GitHub PATs, private keys, SSH certificates, and shell injection hazards.

### 7. Antigravity Multi-Account Isolation
Each profile operates within an isolated **VirtualHome** sandbox: `USERPROFILE`, `HOME`, `APPDATA`, and `LOCALAPPDATA` are isolated per profile so accounts never cross-contaminate. Credentials store securely in Windows Credential Manager, ports allocate automatically per account, and the account identity decodes directly from the token.

### 8. Claude Code Gateway & Token Analytics
A built-in HTTP proxy on `127.0.0.1:9230` translates Anthropic Messages API calls to OpenAI-compatible endpoints with Server-Sent Events (SSE) streaming and `tool_use` / `tool_result` translation. Live telemetry records prompt and completion token volumes, requests per minute, model usage breakdown, and estimated cost savings against standard Anthropic API rates.

### 9. OpenCode Quota & Dual-Stack IP Rotation
Manage provider rate limits and quotas from the Dashboard:
- Dual-stack IPv4 and IPv6 status detection with location geocoding.
- One-click IP rotation via Cloudflare WARP with automatic cooldown protection.
- Secondary Proton VPN provider switcher with strict parent-child dependency toggles in Settings.
- Quick IP masking toggle for streaming, recordings, and screenshots.
- OpenCode Desktop sidecar session monitoring and interactive question prompt notifications.

### 10. Audio & Soundpack System
Event-driven sound engine with sound themes (`Modern`, `Retro`, `Subtle`, `Arcade`). Provides distinct audio cues for task completions, process crashes, quota rotations, Git commits, and branch switches. Includes master volume controls, mute toggles, and per-event sound preferences in Settings.

### 11. Scheduled Automations & Background Tasks
Configure recurring CLI commands, maintenance scripts, and agent triggers using Cron expressions or interval schedules. Includes an execution history log, pause/resume toggles, and one-click manual triggers.

### 12. MCP & Skills Discovery
Inspect installed Model Context Protocol (MCP) servers, tools, skills, rules, and sidecars across user and system directories. Monitor active agent tool capabilities and verify server statuses.

### 13. System Telemetry & Balanced Dashboard Grid
- Real-time CPU/RAM memory utilization and network throughput charts with smooth Bézier sparklines.
- Dynamic 2-column balanced CSS grid layout: eliminates whitespace gaps regardless of whether OpenCode Quota or Hardware Telemetry is enabled or disabled.
- Full-width dual-tile system shell launcher for standard and elevated (Administrator) Command Prompt and PowerShell.

### 14. LAN Remote Broadcast & Mobile Access
An opt-in reverse proxy on `0.0.0.0:9191` forwards the local engine over your Wi-Fi network. Generates a QR code for instant mobile phone pairing, identifies connected LAN clients, tests Windows Firewall rules, and provides a 1-click elevated firewall repair action.

### 15. Prompt Enhancer
Turn rough prompts into structured instructions using built-in presets (`General`, `Code`, `Concise`, `Structured`). Supports custom system instructions, temperature adjustment, and iterative prompt refinement.

### 16. Modern Category Settings & Internal Live Logs
- Master-detail category sidebar navigation with real-time settings search across all categories.
- Dedicated **Live Logs** diagnostic console inside Settings: filterable event stream (System, CLIs, OpenCode, WARP/VPN, Errors, Warnings) with keyword search, auto-scroll, and 1-click clipboard export.
- Discord-inspired adaptive theme engine with preset palettes, custom hex color picker, and saturation slider.

---

## Interface Tour

The user interface is an integrated WebView2 dashboard:

| View | Purpose |
| :--- | :--- |
| **Dashboard & Engine** | Active web services, port switcher, standalone terminal popout, system shells, OpenCode quota rotation, hardware telemetry, quick app shortcuts, and CLI process controls. |
| **Interactive Terminals** | Embedded ConPTY xterm.js terminals with live tab sync, split view launcher, buffer clear, and browser popouts. |
| **Prompt Enhancer** | Prompt rewriting workspace with built-in presets, system prompt editing, and iterative refinement. |
| **Antigravity Profiles** | Profile creation, account switching, credential isolation, and Project Workspace root directory bindings. |
| **AI Dev Tools** | Launch external AI developer tools, auto-discover installed IDEs and clients, and verify executable path health. |
| **LAN Remote & QR** | Broadcast server controls, LAN URL, QR code generator, connected client table, and Windows Firewall 1-click repair. |
| **Repositories & Git** | Visual Git repository manager: branch switching, interactive commit graph, working tree diffs, commit reverts, stashes, worktrees, and live GitHub push/sync. |
| **Settings & Preferences** | Master-detail category navigation: Appearance & Themes, General Preferences, AI Runtimes, MCP & Skills, Audio & Soundpacks, Webhooks & Discord, Security & Health, Live Logs Console, and About & Updates. |

---

## Network Architecture & Ports

| Port | Service | Bind Address | Description |
| :--- | :--- | :--- | :--- |
| **`9119`** | Hermes Dashboard | `127.0.0.1` | Core Hermes web dashboard interface. |
| **`9191`** | LAN Remote Proxy | `0.0.0.0` (Opt-in) | Reverse proxy for local network mobile device access. |
| **`9229`** | Claude Code Terminal | `127.0.0.1` | Claude Code web pseudoconsole server. |
| **`9230`** | Claude Gateway Proxy | `127.0.0.1` | Anthropic-to-OpenAI translation gateway with token telemetry. |
| **`9339`+** | Antigravity Engines | `127.0.0.1` | Isolated ports dynamically allocated per profile identity. |

---

## Quick Start

1. **Download**: Grab `AgentsHub-Setup-v1.0.5.exe` or portable archive from **[Releases](https://github.com/ZerroDevs/AgentsHub/releases/latest)**.
2. **Launch**: Run the executable. No .NET runtime installation required.
3. **Verify Dependencies**: Open **Settings -> AI Engines & Runtimes** to review detected tools (Node.js, Git, Python, Claude Code, Antigravity, Hermes). Missing tools can be installed with one click via `winget`.
4. **Start Engine**: Click **Start Engine** on the Dashboard and launch your preferred agents.

### Command-Line Arguments

| Flag | Function |
| :--- | :--- |
| `--tray` | Start application minimized to the system tray. |
| `-minimized` | Alias for `--tray`. |

---

## Building from Source

### Prerequisites
- Windows 10 or 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) *(required only when building the installer package)*

### Development Run
```powershell
dotnet run
```

### Build Single-File Portable Executable
```powershell
dotnet publish -c Release
```
Output path:
```text
bin\Release\net10.0-windows\win-x64\publish\AgentsHub.exe
```

### Build Multi-Target Packages
```cmd
.\build-installer.cmd
```
Builds all platforms (Windows installer & zip, Linux tarballs, macOS tarballs) into:
```text
dist\AgentsHub-Setup-v1.0.5.exe
dist\AgentsHub-v1.0.5-win-x64.zip
dist\AgentsHub-v1.0.5-linux-x64.tar.gz
dist\AgentsHub-v1.0.5-osx-arm64.tar.gz
```

---

## Project Structure

```text
AgentsHub/
├── Program.cs                          # Application entry point, single-instance guard
├── MainForm.cs                         # Main window, WebView2 host, global hotkeys, tray lifecycle
├── CliWebviewForm.cs                   # Standalone terminal workspace window with split view
├── HudTerminalForm.cs                  # Quake-style drop-down sliding overlay terminal
├── Services/
│   ├── DashboardWebBridge.cs           # C# <-> JavaScript API RPC bridge (3,900+ lines)
│   ├── CliManagerService.cs            # Process lifecycle and ConPTY terminal sessions
│   ├── HermesProcessService.cs         # Hermes engine process supervisor
│   ├── LanProxyService.cs              # LAN broadcast TCP reverse proxy
│   ├── AntigravityProfileService.cs    # Multi-account isolation and credential management
│   ├── ClaudeOpenRouterProxyService.cs # Anthropic Messages <-> OpenAI translation proxy
│   ├── ProxyTelemetryService.cs        # Token usage metrics and cost estimation
│   ├── OpenCodeWatcherService.cs       # OpenCode Desktop sidecar event subscriber
│   ├── DependencyCheckerService.cs     # Parallel runtime and agent detection
│   ├── DevToolHealth.cs                # Path validation and executable health inspection
│   ├── NetworkTelemetryService.cs      # Background network and RAM throughput sampler
│   ├── AiConfigService.cs              # JSON store for workspaces, tools, and providers
│   ├── PromptEnhancerService.cs        # Prompt refinement engine
│   ├── ProtonVpnService.cs             # Proton VPN provider controller
│   ├── CliWebTerminalServer.cs         # Web and WebSocket terminal server
│   └── ConPty/                         # Native Windows pseudoconsole P/Invoke interop
├── Helpers/
│   ├── FirewallHelper.cs               # Windows Firewall status probe and elevated netsh rule creator
│   ├── InstalledClients.cs             # Discovery scanner for AI tools, IDEs, and VPNs
│   ├── AppInfo.cs                      # Application identity, version, and assembly metadata
│   ├── ThemeManager.cs                 # Accent color generator and Windows DWM title bar themer
│   ├── NetworkHelper.cs                # Local IP and network interface resolver
│   ├── QrCodeHelper.cs                 # Base64 QR code generator for LAN pairing
│   ├── ShellLauncher.cs                # External terminal and process launcher
│   └── StartupHelper.cs                # Registry settings and Windows startup configuration
├── ui/                                 # Consolidated WebView2 frontend assets
│   ├── index.html                      # Core single-page interface shell
│   ├── css/                            # Modulized styling (components, themes, views)
│   ├── js/                             # Frontend modules (terminals, git, telemetry, logs, settings)
│   ├── views/                          # Modular view templates (dashboard, settings, repositories, etc.)
│   └── modals/                         # Interactive dialog modals
├── assets/                             # Application icons and branding assets
├── AgentsHub.iss                       # Inno Setup compiler configuration
├── build-installer.cmd                 # Automated build and packaging script
└── dist/                               # Final installer distribution output
```

---

## Configuration & Storage

- **User Configuration**: `%AppData%\AgentsHub\ai_config.json`  
  Stores AI providers, models, project workspaces, custom CLIs, dashboard shortcuts, dev tool targets, Claude gateway settings, and prompt presets.
- **Registry Preferences**: `HKCU\Software\AgentsHub`  
  Stores ports, theme mode, custom accent color, startup behavior, notification preferences, global hotkeys, quota options, and privacy masks.
- **Windows Autostart**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

---

## Security & Privacy

- **Zero Secrets in Repository**: API keys, tokens, and personal credentials are never checked into version control.
- **Local Storage Only**: Provider API keys, credentials, and Git tokens stay on your local machine.
- **Isolated Credential Stores**: Antigravity profile credentials reside in Windows Credential Manager under separate target names.
- **Safe Modifications**: External tool configuration edits (e.g. Claude Code proxy redirection) create automatic backups before saving.
- **Opt-In Network Exposure**: All server sockets bind strictly to `127.0.0.1` by default; LAN broadcast binds only when explicitly toggled.

For vulnerability disclosure, reporting policies, and architecture details, see **[SECURITY.md](SECURITY.md)**.

---

## Contributing

Contributions, bug reports, and feature proposals are welcome. Please read our **[CONTRIBUTING.md](CONTRIBUTING.md)** guide for development setup, code standards, Conventional Commits formatting, and pull request procedures. All C# code must compile cleanly with `dotnet build` with zero warnings.

---

## License

This project is licensed under a custom Source-Available (Non-Commercial, Copyleft & Trademark-Protected) License. See the **[LICENSE](LICENSE)** file for complete terms and conditions.

Copyright © 2026 [ZerroDevs](https://github.com/ZerroDevs). All Rights Reserved.
