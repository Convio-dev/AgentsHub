---
name: pcheck
description: Cross-platform compatibility checker for Windows, macOS, and Linux. Verifies .cmd/.sh pairings, command extensions, build targets, and multi-OS publishing. Trigger with /pcheck.
---

# Cross-Platform Compatibility Checker (`/pcheck`)

When the user sends `/pcheck` or asks to check Windows, macOS, and Linux compatibility, activate this skill and perform a complete verification of cross-platform support.

## Execution

Run the automated cross-platform auditor:

```bash
# On Windows
.\pcheck.cmd
# or
bun run tools/pcheck.ts

# On macOS & Linux
./pcheck.sh
# or
bun run tools/pcheck.ts
```

## Mandatory Cross-Platform Rules

Always verify these 6 pillars whenever adding or modifying code, scripts, or build processes:

### 1. Command & Script Extension Handling (`.cmd` vs Extensionless)
* **Windows:** CLI npm packages and batch files end in `.cmd`, `.bat`, or `.exe` (e.g. `claude.cmd`, `agy.cmd`, `opencode.cmd`).
* **macOS & Linux:** Binaries and scripts have **no extension** (`""`) or `.sh` (e.g. `claude`, `agy`, `opencode`).
* **Rule:** Never check only for `.cmd` or `.exe`. Always check `PlatformHelper.IsWindows` / `process.platform === 'win32'` and probe both Windows extensions and extensionless Unix binaries.
* **Script Pairings:** Every Windows script (`.cmd`) MUST have an equivalent POSIX bash script (`.sh`):
  - `build-sidecar.cmd` ⟷ `build-sidecar.sh`
  - `build-installer.cmd` ⟷ `build-installer.sh`
  - `publish-release.cmd` ⟷ `publish-release.sh`
  - `pcheck.cmd` ⟷ `pcheck.sh`

### 2. Shell Execution & COMSPEC
* **Windows:** Use `COMSPEC` or `cmd.exe` (`/d /s /c` or `/c`). For PowerShell scripts, invoke `powershell.exe -ExecutionPolicy Bypass -File`.
* **macOS & Linux:** Use `SHELL` or `/bin/bash` / `/bin/sh` (`-c`).
* **Rule:** Never spawn `cmd.exe` or `powershell.exe` on non-Windows platforms. Use `PlatformHelper.GetDefaultShell()` in C# and `ShellExecutor.getShellCommand()` in TypeScript.

### 3. File System & Path Separators
* **Rule:** Never hardcode Windows drive letters (`C:\...`), user names (`C:\Users\Gaming\...`), or backslashes (`\`) as string literals.
* Use `Path.Combine` in C# and `path.join` / `homedir()` in TypeScript/JavaScript.
* `PATH` environment delimiter is `;` on Windows and `:` on POSIX systems (`Path.PathSeparator`).

### 4. .NET Multi-Targeting (`AgentsHub.csproj`)
* `AgentsHub.csproj` must maintain dual targeting:
  - `net10.0-windows`: Desktop GUI mode using Windows Forms and Microsoft WebView2.
  - `net10.0`: Cross-platform headless/server mode for macOS and Linux hosting the web dashboard and CLI proxy (`Program.RunUnixHost`).

### 5. Sidecar Engine Multi-Architecture Targets (`build.ts`)
* `build.ts` must maintain support for all 6 targets via Bun compilation:
  - `win-x64` (`bun-windows-x64`)
  - `win-arm64` (`bun-windows-arm64`)
  - `osx-arm64` (`bun-darwin-arm64` - Apple Silicon M1/M2/M3/M4)
  - `osx-x64` (`bun-darwin-x64` - Intel Mac)
  - `linux-x64` (`bun-linux-x64` - Ubuntu/Debian/Arch/RHEL)
  - `linux-arm64` (`bun-linux-arm64` - ARM64 / AWS Graviton / Raspberry Pi)

### 6. Process Watchdogs & Terminal Subsystems
* **Windows:** ConPTY PseudoConsole subsystem and Win32 Job Objects (guarded with `#if WINDOWS` or `OperatingSystem.IsWindows()`).
* **macOS & Linux:** Standard POSIX pipes, pseudo-terminals (`ptyRunner.ts`), and POSIX process lifecycles (`ConPtySession.StartPosixSession`).
