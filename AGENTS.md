<!-- antislop:start -->

## antislop

For UI, copy, people, mobile layout, or code comments work, read these installed skill files directly (use these paths even if a same-named global skill exists):

- Core filter, always on: `antislop`: `.agents/skills/antislop/SKILL.md`
- UI / visual: `antislop-ui`: `.agents/skills/antislop-ui/SKILL.md`
- Copy & text: `antislop-copywriting`: `.agents/skills/antislop-copywriting/SKILL.md`
- People: `antislop-human`: `.agents/skills/antislop-human/SKILL.md`
- Mobile / responsive: `antislop-layoutmobile`: `.agents/skills/antislop-layoutmobile/SKILL.md`
- Code comments: `antislop-code`: `.agents/skills/antislop-code/SKILL.md`
Before starting, follow the core's "Two Usage Modes" section in strict order: explicit session instruction first, then global preference, then ask. A session instruction always wins. For a resolved mode, say `antislop active: <mode> (session override).` or `antislop active: <mode> (global preference).` once before presenting findings or making edits, using the actual mode and source. Acknowledging the user's request without naming the source does not replace this notice.
Only an explicit choice of antislop during or after selects a session mode. A request to review, audit, or avoid file edits does not select a mode; read the global preference in that case. Another skill's mode does not select antislop's mode.
If the mode is unresolved, ask during/after and end the response; wait for the answer before any UI review, planning, or concept. For read-only tasks, put the active-mode notice only at the start of the final answer, never in progress messages. For editing tasks, announce before the first edit and omit it from the final answer.
To update antislop later: `npx antislop-ai --update`, or run `npx antislop-ai` and pick Overwrite them.
  <!-- antislop:end -->

<!-- caveman:start -->

## caveman

When requested or in caveman mode, read this installed skill file directly:

- Terse Voice: `caveman`: `.agents/skills/caveman/SKILL.md`
Answer first, fluff gone, every technical fact and constraint preserved.
  <!-- caveman:end -->

<!-- defuddle:start -->

## defuddle

For web page extraction and clean markdown fetching, read this installed skill file directly:

- Web Page Content Extractor: `defuddle`: `.agents/skills/defuddle/SKILL.md`
Prefer Defuddle CLI (`defuddle parse <url> --md`) over raw web fetches for clean, token-efficient markdown.
  <!-- defuddle:end -->

<!-- pcheck:start -->

## pcheck & Cross-Platform Compatibility (Windows, macOS, Linux)

When requested or when `/pcheck` is sent, read the installed skill file directly and run the audit:

- Cross-Platform Checker: `pcheck`: `.agents/skills/pcheck/SKILL.md`

### Core Cross-Platform Rules:

1. **Always Check Windows, macOS, and Linux Compatibility:**
    - Every feature, file path, process spawn, and shell command MUST function seamlessly across Windows, macOS, and Linux.
    - Never assume Windows-only environment (e.g. `cmd.exe`, `powershell.exe`, backslashes `\`, `C:\...`).
    - Use `PlatformHelper.IsWindows` / `OperatingSystem.IsWindows()` in C# and `process.platform === 'win32'` in TypeScript.

2. **Check Command Extensions (`.cmd` vs Extensionless):**
    - On Windows, CLI npm packages and scripts use `.cmd`, `.bat`, or `.exe`.
    - On Linux and macOS, binaries have NO extension (`""`) or use `.sh`.
    - When searching or resolving CLI binaries (e.g. `claude`, `agy`, `opencode`), always check both Windows `.cmd` / `.exe` and Unix extensionless binaries.
    - For every `.cmd` script in the repository, a corresponding POSIX bash script (`.sh`) MUST exist and be kept synchronized (`build-sidecar.cmd` <-> `build-sidecar.sh`, `build-installer.cmd` <-> `build-installer.sh`, `publish-release.cmd` <-> `publish-release.sh`, `pcheck.cmd` <-> `pcheck.sh`).

3. **Multi-Target Build & Publish Pipelines:**
    - .NET: `AgentsHub.csproj` must target both `net10.0-windows` (desktop GUI with WebView2) and `net10.0` (cross-platform server/headless mode via `Program.RunUnixHost`).
    - Sidecar Engine: `build.ts` must maintain support for all 6 architectures (`win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`).
    - Release Packaging: `tools/BuildTargets.ps1` and `build-installer.sh` must support building and packaging zip/tarball artifacts for Windows, macOS, and Linux.

4. **Slash Command `/pcheck`:**
    - Whenever `/pcheck` is triggered, run `bun run tools/pcheck.ts` (or `.\pcheck.cmd` on Windows / `./pcheck.sh` on Unix) to verify script pairings, multi-targeting, path portability, and command extension handling.
      <!-- pcheck:end -->
