# Agents Hub

**One desktop window for every AI coding agent on your machine — free engines included.**

Agents Hub is a thin desktop shell around a compiled **sidecar engine**. The sidecar owns process
lifecycle, PTY sessions, provider routing, telemetry and the local HTTP API; the window is a WebView2
dashboard that talks to it. Add an agent, a free web model, or a paid provider and they all appear in
the same workspace, the same transcript and the same Git tab.

[![GitHub Release](https://img.shields.io/github/v/release/ZerroDevs/AgentsHub?style=flat-square&color=2563eb&label=Release)](https://github.com/ZerroDevs/AgentsHub/releases/latest)
[![Download Setup](https://img.shields.io/badge/Download-Installer%20%26%20Portable-10b981?style=flat-square&logo=windows)](https://github.com/ZerroDevs/AgentsHub/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-3fb950?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D6?style=flat-square)]()
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WebView2-0284c7?style=flat-square)]()
[![Security Policy](https://img.shields.io/badge/Security-Policy-blue?style=flat-square)](SECURITY.md)
[![Contributing](https://img.shields.io/badge/Contributing-Guidelines-orange?style=flat-square)](CONTRIBUTING.md)

---

## What you get for free

The point of this project is that a capable agent setup should not require a subscription stack.

| Free, no API key | What it does |
| :--- | :--- |
| **DeepSeek chat** | A full coding agent driven through chat.deepseek.com in a hidden window, with the same tool protocol as the CLI engines. Uses your own free account. |
| **Qwen chat** | chat.qwen.ai as a third engine. Works signed out; signing in raises the quota. **Under construction.** |
| **Antigravity CLI (`agy`)** | Multiple isolated accounts on one machine, each with its own home directory and port. |
| **The whole app** | MIT-licensed source, portable build, no account, no telemetry, no licence server. |

Everything below runs locally. There is no cloud component and no telemetry endpoint. The only traffic
is what an engine you started sends to its own service: the CLI you launched, or the web chat you
chose to use.

---

## The sidecar is the product

```
AgentsHub.exe ── WebView2 dashboard ── HTTP/JSON ── AgentsEngine (sidecar)
                                                      ├── process supervisor
                                                      ├── PTY sessions
                                                      ├── telemetry
                                                      └── local API
```

The sidecar is a single compiled binary (`AgentsEngine.exe`, built by `build.ts` for six
architectures) that the host starts and supervises. That split is what makes the rest work:

- **The window can crash without losing your agents.** The sidecar keeps running, and the dashboard
  reconnects to the same state.
- **The dashboard is optional.** The sidecar serves the same UI over HTTP, so the app can be driven
  headless on Linux and macOS.
- **One place owns the truth.** Ports, profiles, sessions and telemetry live in the sidecar, not in
  the window.

---

## Engines

| Engine | How it runs | Tools | Notes |
| :--- | :--- | :--- | :--- |
| **Antigravity** | `agy` CLI in a PTY | Yes | Multiple isolated accounts, per-account ports |
| **DeepSeek** | chat.deepseek.com in a hidden WebView2 | Yes | Free account, no API key |
| **Qwen** | chat.qwen.ai in a hidden WebView2 | Yes | Free, guest allowed; **under construction** |
| **Claude Code** | CLI in a PTY | Yes | Uses your own account |
| **Custom CLIs** | Any command you define | Depends | Per-CLI working directory and shell |

### One tool protocol, two kinds of engine

CLI engines use their own tool loops. The web engines speak the same protocol through a guide the
client sends at the start of a conversation: a fenced `tool` block holding one JSON object, executed
by the app, with the result returned as the next message.

```json
{"action": "read_file", "path": "app.py"}
```

Actions: `list_dir`, `read_file`, `write_file`, `apply_patch`, `search_text`, `run_command`,
`move_file`, `delete_file`, `read_skill`, `mcp_call`.

**Skills, rules and MCP servers** are discovered from the workspace and offered to every engine that
supports tools, so a skill you add once works everywhere.

### Permissions you can see

Destructive actions — writing, patching, running commands, moving, deleting, calling an MCP tool —
ask first unless the profile has **Full access**. The approval card names the action and previews it.
A web engine reading a page it does not control cannot be trusted with silent writes, so it is not
given them.

---

## Features

### Chat that reads like one product
Unified transcript across engines, streamed answers, a reasoning block when the model exposes one,
tool rows (`Ran`, `Wrote`, `Explored`) with their result, per-session model and mode pickers, and an
approval card that appears in the conversation rather than in a dialog you have to find.

### Multi-account isolation
Each Antigravity profile runs in its own **VirtualHome**: `USERPROFILE`, `HOME`, `APPDATA` and
`LOCALAPPDATA` are redirected, credentials live in Windows Credential Manager under separate target
names, and ports allocate per identity. Accounts never share a token.

### Terminals
Real Windows pseudoconsole sessions streamed to xterm.js with resize, ANSI handling, buffer replay
and tab state shared with the detached window. Split one workspace into two independent panes, or
pull a terminal out into its own window.

### Quake-style HUD
A global hotkey slides a semi-transparent terminal down over whatever you are doing, so you can check
an agent without leaving the game or the editor.

### Git workstation
Interactive commit graph with branch lanes, working-tree inspector, side-by-side and unified diffs,
branch-from-commit, stash and worktree management, and direct GitHub push with credential detection.

### MCP and skills
Inspect MCP servers, tools, skills and rules across user and system locations, and hand the ones you
enable to any engine that supports tools.

### Automations
Recurring commands and agent triggers on a cron or interval schedule, with run history, pause/resume
and manual triggers.

### LAN and mobile
Opt-in reverse proxy over your Wi-Fi with a QR code for pairing, a connected-client list, and a
one-click firewall repair. Off by default; binds `127.0.0.1` until you turn it on.

### Everything else
Hardware and network telemetry, prompt enhancer with presets, soundpacks for events, a filterable
live log console, secret scanning before commit, and a theme engine with custom accents.

---

## Interface

| View | Purpose |
| :--- | :--- |
| **Dashboard** | Engine controls, ports, telemetry, quota, system shells |
| **Chat** | One transcript per session, any engine behind it |
| **Terminals** | PTY sessions, split panes, popouts |
| **Repositories** | Git graph, diffs, stashes, worktrees, GitHub sync |
| **Profiles** | Account creation, switching, isolation |
| **AI Dev Tools** | Discover installed IDEs and clients, check path health |
| **LAN Remote** | Broadcast, QR pairing, clients, firewall |
| **Settings** | Appearance, engines, MCP, audio, webhooks, security, logs |

---

## Ports

| Port | Service | Bind | Notes |
| :--- | :--- | :--- | :--- |
| `9119` | Hermes dashboard | `127.0.0.1` | Legacy engine UI |
| `9191` | LAN proxy | `0.0.0.0` | Opt-in only |
| `9229` | Claude Code terminal | `127.0.0.1` | Web pseudoconsole server |
| `9339`+ | Antigravity engines | `127.0.0.1` | Allocated per profile |

---

## Quick start

1. Download the installer or the portable archive from [Releases](https://github.com/ZerroDevs/AgentsHub/releases/latest).
2. Run it. The .NET runtime is not required.
3. Open **Settings → AI Engines & Runtimes** to see what was detected. Missing tools can be installed
   from there.
4. Start the engine and create a session with whichever engine you want.

`run.cmd` on Windows builds the sidecar, builds the UI and launches. It checks for the Antigravity CLI
and installs it if missing, because the engine has nothing to drive without it.

### Command line

| Flag | Effect |
| :--- | :--- |
| `--tray` | Start minimised to the tray |
| `-minimized` | Alias for `--tray` |

---

## Building

Prerequisites: .NET 10 SDK, [Bun](https://bun.sh) (for the sidecar), Node.js (for the UI).

```powershell
dotnet run                      # development
dotnet publish -c Release       # single-file portable exe
.\build-installer.cmd           # all platforms into dist\
```

The sidecar is compiled separately, because TypeScript edits are inert until it is rebuilt:

```powershell
bun run build:win               # bin/win-x64/AgentsEngine.exe
```

Cross-platform checks — script pairings, dual targeting, six sidecar architectures, spawn handling —
run with:

```powershell
.\pcheck.cmd                    # or ./pcheck.sh
```

---

## Project layout

```text
AgentsHub/
├── Program.cs                     # entry point, single-instance guard
├── MainForm.cs                    # window, WebView2 host, hotkeys, tray
├── src/                           # sidecar source (TypeScript, compiled to AgentsEngine)
├── Services/
│   ├── DashboardWebBridge.cs      # the entire JS <-> C# API surface
│   ├── CliManagerService.cs       # process lifecycle
│   ├── Sidecar/                   # sidecar host, session transport
│   ├── DeepSeek/                  # free web engine, tool layer, MCP client
│   ├── Qwen/                      # second web engine (under construction)
│   ├── Platform/                  # PlatformHelper, ToolWindowHost
│   ├── ConPty/                    # Windows pseudoconsole interop
│   └── …                          # git, profiles, LAN, telemetry
├── ui/src/                        # React dashboard (Vite + Tailwind)
├── tools/                         # test harnesses and probes
└── dist/                          # packaged output
```

---

## Configuration

- `%AppData%\AgentsHub\ai_config.json` — providers, models, workspaces, custom CLIs, shortcuts.
- `HKCU\Software\AgentsHub` — ports, theme, hotkeys, startup, privacy masks.
- Credentials never live in either: Antigravity profiles use Windows Credential Manager, and web
  engine sessions live in their own browser profiles under `%AppData%\AgentsHub\`.

---

## Security and privacy

- **No secrets in this repository.** The tree is scanned before release for API keys, tokens and
  private keys; none are tracked, and no personal paths or session logs are committed.
- **Nothing leaves the machine** unless you configure a provider that needs the internet. There is no
  analytics endpoint and no licence check.
- **Local by default.** Every socket binds `127.0.0.1`; LAN broadcast is opt-in and reversible.
- **Approvals before writes.** Destructive tool actions ask first unless the profile has Full access.
- **Secret scanner** runs before commits, covering provider keys, cloud credentials, private keys and
  shell injection patterns.

Report a vulnerability through [SECURITY.md](SECURITY.md).

---

## Known limitations

- **Qwen is under construction.** Sending, tools, thinking and the model/mode pickers work; there is
  no conversation continuity yet, no attachments, and its answer selectors follow Qwen's current
  markup.
- Web engines drive a real page, so their behaviour follows that page. When the provider changes its
  UI, the selectors need updating — the engine logs what it looked for so the fix is a one-line change.
- Windows 10/11 x64 is the supported desktop target. Linux and macOS builds are the headless sidecar
  plus the web dashboard.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, standards and Conventional Commits. C# must build
with zero errors; run `pcheck` before opening a pull request that touches scripts or process spawning.

---

## License

Source-available, non-commercial, copyleft, trademark-protected. See [LICENSE](LICENSE).

Copyright © 2026 [ZerroDevs](https://github.com/ZerroDevs). All Rights Reserved.
