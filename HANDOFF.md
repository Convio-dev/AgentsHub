# HANDOFF

State of the Agents Hub work as of 30 Oct 2026. Written so the next session can continue
without re-deriving anything.

---

## 1. Where things stand

Three engines answer in the chat: the Antigravity CLI, DeepSeek and Qwen (both through WebView2),
and now Vyce Code, which runs in a hidden console. The CLI catalogue was rebuilt around detection,
install and launch. A health panel and a redacted log bundle exist so failures stop requiring a
`grep` in `%TEMP%`.

**Nothing is half-built.** Every engine in the catalogue that claims chat support can answer.

### Repo state

```
HEAD  64bd1b3  feat: add DependencyCheckerService for runtime and tool detection
      c976833  feat(engines): health panel, log bundle, and a real CLI catalogue
      c8f82cd  feat(qwen): third engine behind a WebView2, still under construction
```

Uncommitted:

```
 M Services/DashboardWebBridge.cs          vyce branch, vyce_stop, vyce stop on abort_task
 M Services/DeepSeek/DeepSeekEngine.cs     cancellation support (Stop)
 M Services/Qwen/QwenEngine.cs             conversation id, protocol reminder, result budget, health
 M Services/DependencyCheckerService.cs    Vyce row claims chat support on Windows
 M MainForm.cs                             Vyce console is killed on shutdown
 M tools/DeepSeekToolTest/Program.cs       --tty, --inject, --vyce --twice, --redact
 M tools/DeepSeekToolTest/*.csproj         compiles QwenTools, LogBundleService, Vyce, ConPty
 M ui/src/...  engine union gains 'vyce'; Vyce entry in the engine picker; composer chip
?? Services/Vyce/VyceEngine.cs             chat turn, session-file reading, detection
?? Services/Vyce/VyceTty.cs                hidden console + keystroke injection
?? HANDOFF.md
```

Everything above builds on both target frameworks, `ui` compiles, and the tool suite passes.

---

## 2. What was built

### Vyce in chat (`Services/Vyce/`)

The CLI is a full-screen TUI with no headless mode (`--help` offers only `-v`, `-l`, `--logout`,
`-h`), so the old pseudo-console approach could never work. Two files replace it:

- **`VyceTty.cs`** creates a *real* console, hidden at creation (`CREATE_NEW_CONSOLE`, `SW_HIDE`),
  and types into it. Keys go in as Win32 `KEY_EVENT` records through `WriteConsoleInputW`. Because
  `AttachConsole` only succeeds while the caller has no console, every batch attaches, writes,
  detaches. The input handle is opened as `CONIN$`, not `GetStdHandle`: attaching does **not**
  rewrite this process's stored standard handles, so `GetStdHandle` still points at whatever the
  host inherited and writing there types into nothing.
- **`VyceEngine.cs`** sends the prompt, then reads the answer out of `~/.vyce/sessions/*.json`
  instead of scraping the ANSI screen. A turn is recognised by the session's message count growing
  or a new session file appearing, never by the answer being longer. Stop sends Esc. The console is
  reused between turns, so the conversation survives, and killed when the app exits.

Wired through: the engine picker (`Use Vyce`), the sidebar icon and tooltip, the composer chip,
`abort_task` and the dedicated `vyce_stop`.

**Models.** Vyce has no command-line flag for the model, so the picker is driven the way a user
drives it. `ListModelsAsync` sends ctrl+p (the command palette; a bare `/` is file completion, not
a command list), types `model`, presses Enter, waits for the filter box, then scrapes the list out of
the rendered screen. `SetModelAsync` opens the same screen, types enough of the id to leave one row,
and presses Enter. Both take the console lock, so a switch cannot land in the middle of a turn.
Endpoints: `vyce_models` (list), `vyce_model` (switch), and `vyce_model_current`, which answers
what is answering from the session file in ~15 ms and never starts a console. `VyceModelChip.tsx`
in the composer is the UI for all three, shaped like the Gemini selector: a borderless footer chip
with the current id, a 330 px panel with the active row ticked, unavailable rows dimmed as
"Coming soon", and an error line instead of a silent failure. The chip's label comes from
`vyce_model_current` on mount and from the panel on open; only opening the panel drives the menu.

Both menu calls refuse while a turn is running (`TurnInProgress`) instead of queueing behind the
console for up to three minutes.

Cold start sends `/home` first. A restored chat does not append to its session file, so without that
every launch after the first answered nothing and reported nothing.

### Why the pseudo-console failed

A ConPTY child inherits this process's standard handles. With the host's stdout on a pipe, `node`
inside the pseudo-console reported `process.stdin.isTTY === undefined` and even its own output went
to the parent's pipe. Reproduced from a console-less host too, so it is the wiring, not the harness.
`tools/DeepSeekToolTest --tty` is that probe and stays in the suite.

### Qwen web engine (`Services/Qwen/`)

- `QwenEngine.cs` drives `chat.qwen.ai` in a hidden WebView2, one turn at a time.
- `QwenTools.cs` owns everything specific to a *rendered* page: bare action objects, the language
  label and line-number gutter, and an unclosed object held back while streaming. Execution,
  approval, describe and preview delegate to the DeepSeek layer.
- Tool loop: `tool` JSON blocks → `QwenTools.Extract` → approval → execute → results back to the model.
- `ApprovalRequested` is wired to the **same** `deepseek_approval_request` card DeepSeek uses.
- Reports `tool_start` / `tool_output`, so a Qwen turn shows `Wrote` / `Explored` rows.
- Primes per **conversation**, not per workspace: the page path is compared with the path the primer
  was sent in, so a new chat re-primes.
- `ProtocolReminder` rides on every prompt; tool results are capped at 4000 per result, 12000 per turn.
- Conversation id: navigates to `/c/<id>` before a turn when the session has one, reports the id back
  through `conversation_started` afterwards. **If Qwen's path is not `/c/<id>`, nothing is captured
  and behaviour is unchanged** — the failure mode is silence.

### DeepSeek engine

Restored byte-identical to `408dfbf` after a regression, then extended minimally:

- `Stop()` + a linked `CancellationTokenSource` per turn. Before this, a DeepSeek turn could not be
  interrupted at all.
- A CDP retry on `COMException` in `EvalAsync` ("Class not registered" during renderer swaps).
- The host window is a `ToolWindowHost` (below).

**Diff against `408dfbf` is now: the tool-window swap, three removed `ShowInTaskbar` lines, the
retry, and the stop plumbing.** Nothing else in that engine was touched.

### Windows that Alt+Tab cannot see (`Services/Platform/ToolWindowHost.cs`)

`WS_EX_TOOLWINDOW` set in `CreateParams`, `WS_EX_APPWINDOW` cleared, `ShowWithoutActivation`.
Both web engines park their host at `-32000` so Chromium keeps rendering; the windows are simply not
listed. `ShowInTaskbar` is never toggled — that toggle was what left `WS_EX_APPWINDOW` stuck on.

### CLI catalogue (`Services/DependencyCheckerService.cs`)

Current ids: `aider, antigravity, claude, codex, crush, git, kimi, node, opencode, python, qwen-code,
vyce, warp`.

- Removed **Hub** and **Proton** — from the catalogue *and* from a hardcoded fallback list in
  `useSettingsStore` that was seeding the panel with stale rows and invented versions. That list is
  now empty.
- Added **Vyce Code**, **Qwen Code**, **Crush**, **Aider**, **Kimi Code**, plus Codex and OpenCode.
- **Kimi** uses `InstallKind = "browser"`: no package name could be verified, so it opens the vendor
  site rather than running a guessed command.
- `ChatSupported` is set for **antigravity** and, on Windows only, for **vyce**. Every other agent row
  shows a **Not used** chip, and the group carries a line saying the runtimes are required while the
  agents are optional.

### Diagnostics

- **Health panel** (Settings → Engines): `engine_health` returns passive state only — it never
  navigates a page or probes a token. Qwen reports initialized / page / primed-in / last turn / last
  error. DeepSeek reports only what it tracks.
- **Log bundle** (`Services/LogBundleService.cs`): one plain-text file from `%TEMP%\agentshub-*.log`,
  redacting bearer headers, `sk-`/`ghp_`/`AKIA`/`xox` shapes, JSON credential fields, query-style
  tokens, `C:\Users\<name>` paths and e-mail addresses. `--redact` in the harness proves it.
- `open_log_bundle_location` reveals the file (`explorer /select`, `open -R`, `xdg-open`).
- A Vyce turn that never lands writes the hidden console's screen into `agentshub-vyce.log`, so
  "it was on the wrong screen" is answerable from the log alone.

### Install feedback

The install buttons were never broken — **nothing subscribed** to `dependency_install_progress` or
`dependency_install_complete`. Now the store tracks `installingId` and a last-line map, the button
disables and reads "Installing...", the live line appears under the row, and a 180 s safety net
clears a stuck button.

### README

Rewritten around the sidecar and the free engines. Local-model routing and the OpenRouter/Groq
gateway were **deliberately removed** from the document — they were removed from the product earlier
and must not reappear.

---

## 3. Verified vs unverified

| claim | status |
|---|---|
| Both target frameworks build, 0 errors | verified |
| `ui && npm run build` (tsc + vite) | verified |
| `tools/DeepSeekToolTest` → 17 PASS | verified |
| `--redact` → nothing survives | verified |
| Qwen parsing (rendered, mid-stream, debris) | verified in harness |
| `/pcheck` → 11 passed, 0 failures | verified |
| Vyce cold turn answers (`--vyce`, unique token) | verified, repeatedly |
| Vyce model list read from the picker (`--models`) | verified, real ids |
| Vyce model switch changes the answering model (`--models <root> <id> --ask`) | verified, and switched back |
| Model label without starting a console (`vyce_model_current`) | verified: 15-18 ms, `console running=False` |
| The chip is in the built UI bundle | verified (`vyce_model_current` present in `ui/dist`) |
| Vyce second turn reuses the console and remembers the first | verified (`--twice`) |
| Vyce injection itself (`--inject` into `cmd`, into raw-mode Node) | verified |
| Vyce leaves no stray `node` processes | verified |
| DeepSeek stop works live | **unverified** |
| Qwen conversation resume (`/c/<id>`) | **unverified** |
| Vyce inside the running app (engine picker to first answer) | **unverified**: wired and compiled, never clicked |
| The model chip in the composer (`VyceModelChip`) | **unverified** in the app; the bridge calls behind it are verified |
| Vyce on macOS/Linux (`script` terminal route) | **unverified**, see §4 |

The unverified rows need a running app. Watch `%TEMP%\agentshub-qwen.log` for `opened conversation`.

---

## 4. Limits of the Vyce chat support

- **Windows only, for now.** The injector is Win32 console calls. The Unix branch runs the CLI under
  `script` (a real terminal) and writes the prompt to its stdin. It is written and compiled, and it
  has never been executed. Do not mark it verified until someone runs `--vyce` on macOS or Linux.
- **The TUI must be readable but is never read.** Everything comes from the session JSON. If a
  provider changes that schema, `ReadSession` returns empty and the turn reports that it was not
  accepted, which is the honest answer.
- **The prompt is typed as one line.** A newline would submit half a message, so `\n` becomes a
  space. Pasting multi-line text into a Vyce chat will be flattened.
- **The screen is not shown.** Model and answer are driven from the app; mode, sign-in and approval
  still need a real terminal. A user who needs those must open Vyce themselves.
- **The model list is scraped, not published.** If Vyce rewords the picker, `ParseModelPicker`
  returns empty and the chip says so instead of offering wrong names.
- **Repeating the exact previous prompt produced no new turn** in testing: the transcript stayed
  identical and the CLI never appended. Detection there is unreliable by nature, so a user who
  repeats a message may see `Vyce did not accept the prompt`. Probe with a unique token
  (`--vyce` does this by default) and do not conclude the engine is broken from a repeated prompt.
- **A session left mid-answer is restored on the next launch.** Cold start sends `/home` to avoid
  that, so the in-app conversation begins in the app's own chat rather than in whatever the CLI
  restored. The prompt is retyped once only if the screen does not show it after about a second.

---

## 5. Next steps, in priority order

1. **Confirm the unverified rows by restarting the app**: DeepSeek stop, Qwen resume, a first Vyce
   answer from the picker, and switching models from the composer chip mid-chat.
2. **Run the Vyce route on macOS or Linux** and either confirm or delete the `script` branch.
3. **First-run wizard** — detect engines, sign in, pick a workspace.
4. **Session export / search / per-session model memory** — three small, independent pieces.
5. **In-app selector check** — when a provider rewords its UI, report which selectors still match,
   inside the existing WebView2 (never a second Chromium).
6. **UI unit tests** — there is no JS test runner. `appendChatToken` appending full text is what
   produced the `tooltool` bug, and nothing covers it. Adding Vitest is a real decision, not a patch.

---

## 6. Facts that will bite you

- **The transcript is append-only.** `appendChatToken` appends; anything streamed once is permanent.
  That is why `chat_final` exists (it *replaces* the last assistant message) and why `Visible` holds
  back an unclosed object rather than filtering after the fact.
- **`DeepSeekTools.cs` is DeepSeek's.** Qwen-specific parsing belongs in `QwenTools.cs`. Editing the
  shared file broke a working engine once already.
- **`TokenJs` is embedded in three page scripts** as `const token = {{TokenJs}}`. Changing its return
  shape is an API change: an object broke the page's own auth call and DeepSeek stopped answering.
- **`ShowInTaskbar` must never be toggled** on a host window — see `ToolWindowHost`.
- **A ConPTY child inherits the host's standard handles**, so a Node CLI inside one sees no TTY even
  when the console is real. `--tty` proves it in one command. Anything needing a TTY has to be given
  a real console, not a pseudo one.
- **`AttachConsole` does not rewrite standard handles.** Open `CONIN$`/`CONOUT$` after attaching, or
  the writes go to whatever the process inherited and fail silently in the child.
- **Kill the Vyce process tree on exit or it outlives the app** and holds the session file the next
  launch wants. `MainForm` shutdown and the harness both do this.
- **`/` in a Vyce chat is file completion, not commands.** The command list is ctrl+p. Typing `/model`
  and expecting a menu is what made the first attempts look like a broken picker.
- **The model picker commits on Enter, and only on one row.** Filter with a prefix that leaves a
  single match; a filter that leaves several rows leaves the picker open, and Enter there does
  nothing useful. `SetModelAsync` reads the marker back rather than assuming it worked.
- **The model menu is refused during a turn, not queued.** The console carries one thing at a time;
  waiting on it would leave the picker spinning for a whole answer.
- **The model a chip shows and the model a turn uses are the same fact.** Both read the `model`
  field of the session file; there is no separate setting in this app to drift from it.
- **Never delete `obj/`.** It holds the generated global-usings file; deleting it produces a wall of
  "`Path`/`File`/`Task` not found" errors in the editor.
- **A shared const that is both C# and JavaScript has consumers on both sides.** Audit before editing.
- **`//n` is not `\n`.** A literal survived in the tool-results message for hours.
- **The editor can be wrong.** If it reports errors that `dotnet build -t:Rebuild` does not, check
  whether the file is new (stale project snapshot) or the tab holds an older buffer.

---

## 7. Commands

```bash
dotnet build AgentsHub.csproj -c Debug                       # both targets
dotnet build AgentsHub.csproj -c Debug -f net10.0            # headless only
dotnet build AgentsHub.csproj -c Debug -t:Rebuild            # when the editor disagrees

cd tools/DeepSeekToolTest
dotnet run -c Debug                     # 17 checks
dotnet run -c Debug -- --parse          # Qwen parsing
dotnet run -c Debug -- --redact         # log redaction
dotnet run -c Debug -- --guide <root>   # what the engines prime with
dotnet run -c Debug -- --vyce <root>    # Vyce end-to-end, unique token per run
dotnet run -c Debug -- --vyce <root> --twice    # second turn in the same console
dotnet run -c Debug -- --models <root>          # what models the picker offers
dotnet run -c Debug -- --models <root> <id>     # switch, then read the marker back
dotnet run -c Debug -- --models <root> <id> --ask   # switch and make that model answer
dotnet run -c Debug -- --screen                 # free-form screen probe, see Program.cs
dotnet run -c Debug -- --tty [command]  # what a child sees inside this app's pseudo-console
dotnet run -c Debug -- --inject "cmd.exe /k" [delayMs] [text]   # the injector alone

cd ui && npm run build                   # tsc + vite, after any ui/src change
bun run tools/pcheck.ts                  # 11 cross-platform checks
```

Logs: `%TEMP%\agentshub-{deepseek,qwen,vyce,permissions,spawns}.log`

---

## 8. Open decisions

- **Vyce on Unix**: confirm the `script` route, or drop the branch and say Windows-only in the panel.
- **Vyce input rhythm**: a prompt missing from the screen is retyped once. A finer signal would
  need a notification hook the TUI does not have.
- **Vyce model memory**: the CLI keeps the model in its own config, so a switch persists across
  sessions. The app reads the current one on open; it does not store a copy per chat.
- **Log bundles** currently land in `%TEMP%`. Desktop or Documents would be easier to find.
- **Kimi Code** has no verified package name. Someone with the vendor docs should confirm it.
- **UI tests**: adopt Vitest, or keep relying on the C# harness?
- **`warp`** sits in the agent group and therefore gets a `Run` button that opens `warp-cli`.

---

## 9. Privacy

The tree was audited: no API keys, tokens, private keys, credential stores, session logs or personal
absolute paths in tracked files. The redaction fixture uses `C:\Users\someone`, not a real account.
Keep it that way — `pcheck` verifies portability, **not** privacy.
