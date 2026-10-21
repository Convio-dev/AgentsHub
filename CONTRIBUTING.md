# Contributing to Agents Hub

Thank you for your interest in contributing to Agents Hub! This document provides instructions and guidelines for opening issues, developing features, and submitting pull requests.

---

## Code of Conduct & Expectations

- **Focused Changes**: Keep pull requests focused on a single feature, bug fix, or optimization.
- **Maintain High Quality**: All C# code must build cleanly with `dotnet build` with **0 warnings and 0 errors**.
- **No Fluff or AI Slop**: Keep code comments and user interface copy clear, direct, and human. Avoid generic AI buzzwords or filler prose.
- **Strictly Use SVG Icons**: Use inline SVG vector icons for UI elements and buttons. Do not use emoji characters in application interface controls or tabs.
- **Zero Secrets Policy**: Never commit API keys, personal access tokens, credentials, or private machine paths to version control.

---

## Development Setup

### Prerequisites
- **Operating System**: Windows 10 or 11 (x64)
- **.NET SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download)
- **Node.js**: [Node.js 18+](https://nodejs.org/) *(used for validating frontend module syntax via `node -c`)*
- **Git**: [Git for Windows](https://git-scm.com/)
- **Inno Setup 6**: [Inno Setup](https://jrsoftware.org/isdl.php) *(required only if compiling installer packages)*

### Getting Started

1. **Fork and Clone** the repository:
   ```powershell
   git clone https://github.com/ZerroDevs/AgentsHub.git
   cd AgentsHub
   ```

2. **Restore dependencies and build**:
   ```powershell
   dotnet build AgentsHub.csproj
   ```

3. **Run in development mode**:
   ```powershell
   dotnet run
   ```

---

## Project Structure & Architecture

- **`Program.cs` / `MainForm.cs`**: Single-instance mutex guard, WinForms shell, WebView2 initialization, system tray icon lifecycle, and global hotkey hooks (`RegisterHotKey`).
- **`CliWebviewForm.cs`**: Detached standalone workspace window with dual-pane split view support.
- **`HudTerminalForm.cs`**: Quake-style drop-down sliding overlay terminal.
- **`Services/`**: Background services and business logic:
  - `DashboardWebBridge.cs`: Central bidirectional JSON-RPC bridge between WebView2 JavaScript and C#.
  - `CliManagerService.cs`: Manages agent execution, PIDs, and ConPTY pseudoconsole streams.
  - `LanProxyService.cs`: TCP reverse proxy for mobile device LAN broadcasting.
  - `AntigravityProfileService.cs`: Multi-account credential isolation and VirtualHome directory sandboxing.
  - `ClaudeOpenRouterProxyService.cs`: Anthropic Messages to OpenAI translation gateway proxy.
  - `PreCommitSecurityService.cs`: High-entropy secret and token scanner.
- **`ui/`**: Frontend single-page application served inside WebView2:
  - `index.html`: Main shell, top navigation bar, and partial template loader.
  - `views/`: Modular HTML views (`dashboard.html`, `settings.html`, `repositories.html`, etc.).
  - `js/`: Modular JavaScript modules (`dashboard.js`, `network-quota.js`, `settings-view.js`, `repositories.js`).
  - `css/`: Theme variables, components, and view-specific stylesheets.

---

## Coding Standards

### C# / Backend
- **Modern C# 12+ Features**: Use collection expressions (`[...]`), pattern matching, primary constructors, and expression-bodied members where appropriate.
- **Safe JSON Parsing**: In `DashboardWebBridge.cs`, use `JsonElement.TryGetProperty()` and guard against missing or malformed fields.
- **Process Safety**: Always ensure process termination uses process tree walking or job objects so that child CLI workers are never orphaned.
- **Zero Compiler Warnings**: Verify that `dotnet build` outputs `0 Warning(s), 0 Error(s)`.

### Frontend (HTML / CSS / JavaScript)
- **Vanilla JavaScript**: Write modular vanilla JS. Avoid adding bulky npm dependencies or heavy frontend frameworks.
- **Syntax Check**: Verify that all modified JS scripts pass Node.js syntax parsing:
  ```powershell
  node -c ui/js/core/*.js ui/js/modules/*.js ui/js/*.js
  ```
- **CSS Variables & Theming**: Reference CSS variables (`var(--accent-color)`, `var(--bg-card)`, `var(--text-main)`) rather than hardcoding static colors to maintain light and dark theme compatibility.
- **SVG Icons**: Use clean, scalable inline SVGs (`<svg class="icon-svg" viewBox="0 0 24 24">...</svg>`).

---

## Commit Guidelines

We follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```text
<type>(<scope>): <short imperative description>

[optional body explaining intent and motivation]
```

### Commit Types
- `feat`: A new user-facing feature or capability.
- `fix`: A bug fix or correction.
- `refactor`: Code change that neither fixes a bug nor adds a feature.
- `perf`: Performance improvement.
- `docs`: Documentation updates or additions.
- `style`: Formatting, spacing, or CSS styling adjustments.
- `chore`: Maintenance tasks, build script updates, or dependency version bumps.

---

## Pull Request Process

1. **Create a Feature Branch**:
   ```powershell
   git checkout -b feat/my-new-feature
   ```

2. **Test Your Changes**:
   - Run `dotnet build AgentsHub.csproj` and ensure `0 Warning(s), 0 Error(s)`.
   - Run `node -c` on all touched JavaScript files.
   - Test UI responsiveness in WebView2 across light and dark theme modes.

3. **Submit the Pull Request**:
   - Provide a clear summary of what the PR accomplishes and why.
   - Include screenshots or GIFs for visual changes where applicable.
   - Note any configuration or registry changes introduced.

---

## Security Inquiries

For vulnerability disclosures or sensitive security concerns, please refer to [SECURITY.md](SECURITY.md) instead of creating a public issue.
