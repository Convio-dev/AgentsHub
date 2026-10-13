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

## Hardware telemetry
CPU **and** GPU telemetry were both removed. The telemetry card now shows a RAM tile plus a small network readout (`metric-net` badge, `net-load-val` chip, `ram-*` tile). `Services/NetworkTelemetryService.cs` owns the only sampling: a background 2s timer publishes cached `NetworkKiloBytesPerSec` (sum of `Network Interface` / `Bytes Total/sec`, virtual adapters filtered). The DTO exposes `netKbps`, `netMbps`, `ramMb`, `totalRamMb`, `memoryUsedGb`, `memoryTotalGb`, `hermesMemMb`.

**Never sample perf counters on the UI thread.** `Bytes Total/sec` is a delta counter, so the counter set is cached and *reused* across the 30s refresh — creating counters per poll, or reading one twice in a row (the DTO has two callers: the 2.5s timer push and the 2s UI poll), returns meaningless values. The service publishes a plain `int` snapshot via `Volatile.Write` so readers never take the sample lock. The first `Network Interface` enumeration in a cold process can take several seconds (Windows perflib cold start), so the network badge may read 0 briefly after launch.

## Dependency checker (Settings view)
`Services/DependencyCheckerService.cs` detects the eight external tools for the "Dependencies & Required Runtimes" card, split into two UI sections by `Category` (`runtime` → section 1; `agent` / `utility` → section 2). Resolution is managed PATH/PATHEXT scanning plus `%ENV%` fallback paths — **not** `where.exe`. Fallbacks support a single `*` directory segment (highest version first). `.cmd`/`.bat` hits must be probed via `cmd.exe /c ""path" --version"` (they cannot launch directly with `UseShellExecute = false`).

Rules that exist for real reasons — do not remove:
- **`GuiExecutableNames`** blocks `--version` probes against GUI launchers (ProtonVPN, Cloudflare WARP.exe); probing them opens the app window. GUI installs get their version from the versioned folder name instead.
- **`ExcludedPathTokens = ["WindowsApps"]`** on python rejects the Microsoft Store stub, which is not an interpreter.
- **winget is an app-execution alias** and cannot be launched with `UseShellExecute = false`; it runs via `cmd.exe /c winget install …`.

**`hermes --version` costs ~4s** (Python boot), so `VersionProbeTimeoutMs` is 8s; probes run in parallel so that ≈4s is the whole scan's floor. The scan is lazy — it only fires when the Settings view first opens.

**Proton's real install path** is `%ProgramFiles%\Proton\VPN\v<version>\ProtonVPN.Client.exe` (plus `ProtonVPN.Launcher.exe`) — *not* `…\VPN\ProtonVPN.exe`, which the original brief assumed and which does not exist.

## Antigravity profile isolation — read before touching ApplyProfile
**`agy` authenticates from the single, machine-wide `gemini:antigravity` credential**, not from the per-profile VirtualHome alone. The VirtualHome is necessary but NOT sufficient.

Therefore **`CliManagerService.Start(id)` MUST call `_profileService.ApplyProfile(profileName)` before `PrepareVirtualHome` for every agy profile launch.** That call is what writes the profile's credential into the Windows Credential Manager. Omitting it is what made tab-started sessions show the *other* profile's Google account while the dropdown path (which already applied) was correct. Order: `ApplyProfile` → `PrepareVirtualHome` → `StartVirtualHomeWatcher` → env (`USERPROFILE`/`HOME`/`APPDATA`/`LOCALAPPDATA`/`PORT`/`ANTIGRAVITY_PORT`) → `cmd.exe /c agy` with `workDir = virtualHome`.

`ApplyProfile`'s step 1 used to corrupt profiles: it ran `ExportCurrentSession(_activeProfile)` unconditionally, which reads that same global credential and writes it into the outgoing profile's saved files, so every switch overwrote the outgoing profile's account. It is now guarded with `!HasValidSavedCredentials(_activeProfile)` — **do not remove that guard**, it is what makes calling `ApplyProfile` on every launch safe.

Known remaining gap: `Start(CliType.AntigravityCli)` (tray "Start All CLIs", dashboard `toggle_cli`) passes the bare id `antigravity_cli`, which is not a profile id, so it takes the generic workspace path with no credential applied. `SwitchTab` normalises that id to the active profile; `Start` does not.

`SwitchTab` already routes `_customWebViews[profileId].Visible` per profile and calls `TriggerTerminalFit()`.

## Tooling
- antislop skills are installed at `.agents/skills/`; `AGENTS.md` routes UI/copy/layout work there. No global antislop mode is saved (`%APPDATA%\antislop\settings.json` absent), so the mode question must be asked per session.
- There is **no `.editorconfig`**, and the IDE analyzers (IDE00xx / CAxxxx) are **not** surfaced as build warnings — `dotnet build` reports 0 warnings while the IDE lists dozens. To apply them, scope `dotnet format` precisely:
  `dotnet format style|analyzers HermesHub.csproj --diagnostics <ids> --include <files> --severity info`
  (`--severity` only accepts info/warn/error.) Review with `git diff`; the tree is usually committed so rollback is safe.
- `DllImport` → `LibraryImport` conversions need `[return: MarshalAs(UnmanagedType.Bool)]` for `bool`-returning Win32 calls, plus `partial` on the containing type, or the build fails with SYSLIB1051.
