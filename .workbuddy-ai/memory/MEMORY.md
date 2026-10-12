# Agents Hub (HermesHub) — project notes

Windows .NET 10 WinForms + WebView2 desktop hub for multiple AI coding CLIs. Assembly name is `HermesHub`, product name is `Agents Hub`. Ships as a single-file portable exe.

## Architecture
- `Program.cs` — single-instance mutex + show-window event.
- `MainForm.cs` — no WinForms controls; hosts a `WebView2` and the tray icon.
- Live UI is `ui/`, mapped to `https://app.agentshub.local` by `MainForm.InitializeWebViewAsync`. **`assets/web/` is a stale near-duplicate of `ui/` — do not edit it.** Only `ui/` is loaded.
- `Services/DashboardWebBridge.cs` is the API surface: JS `callBridge(action, payload)` → `{responseId, success, data}`; C#→JS push events are `{event, payload}`. ~60 actions.

## Config & storage
- `%AppData%\AgentsHub\ai_config.json` via the `AiConfigService` singleton (migrates legacy `%AppData%\HermesHub`).
- Registry prefs: `HKCU\Software\AgentsHub` (legacy fallback `Software\HermesHub`).
- Ports: Hermes 9119, Claude Code 9229, Antigravity 9339+, LAN proxy 9191, Claude OpenRouter proxy 9230.

## UI conventions
- No emojis in dashboard UI. Use inline `<svg class="icon-svg" ...>` icons.
- `ui/index.html` is self-contained (all views inline); `ui/js/dashboard.js` holds the controller.

## Known issues (not yet fixed)
- `build-installer.cmd` invokes `ISCC.exe HermesHub.iss`, but the only Inno Setup script is `AgentsHub.iss` — installer build fails.
- **`save_ai_config` replaces the entire config**, so any field missing from `GetAiConfigDto()` is silently dropped on every dashboard save. `cliWorkspaces` was fixed; still missing: `ClaudeEngineMode`, `ClaudeSelectedModel`, `ClaudeCustomGatewayUrl`, `ClaudeInjectionEnabled`, `ClaudeSelectedGatewayId`, `ClaudeGatewayProviders`.
- Dead code: `PromptEnhancerForm.cs` (never instantiated), `SettingsForm.cs` (only reachable from it), `Helpers/FirewallHelper.cs` (zero references), `ui/tabs/*`, `ui/modals/add-profile.html`.
- README is stale (claims v1.0.2, documents 2 services).
- Version `1.0.3` hardcoded in ≥6 places.

## Terminal workspace presets
`AiConfig.TerminalWorkspacePreset` holds `Preset_Clean` / `Preset_Duo` / `Preset_All` / `""` (custom mix). `GetTerminalWorkspacePreset()` returns the stored value; `EnsureDefaults` re-derives it from the enabled flags on every load so the label always matches reality. `CliWebviewForm.RebuildTabs` no longer force-enables Hermes — with no enabled workspace it shows the empty-state panel.

## Tab context menu
`CliWebviewForm` right-click on a workspace tab → Open in External CMD / Rename / Remove-Close. Env and command resolution lives in `CliManagerService.BuildExternalLaunchSpec(id)` (returns `ExternalLaunchSpec`), so the external console gets the same environment as the embedded session. `Start(id)` is deliberately untouched — do not refactor it to share this path without care.

## Terminal HTML duplication
`ui/terminal.html` and `assets/web/terminal.html` are kept in sync by hand; they differ only in asset paths (`./css/`, `./js/` vs flat). Only `ui/` is loaded at runtime — `assets/web/` is a fallback. Remember to update both when touching terminal styling.

## Tooling
- antislop skills are installed at `.agents/skills/`; `AGENTS.md` routes UI/copy/layout work there. No global antislop mode is saved (`%APPDATA%\antislop\settings.json` absent), so the mode question must be asked per session.
