using System.Text.Json;
using AgentsHub.Services;
using AgentsHub.Services.DeepSeek;
using AgentsHub.Services.Qwen;
using AgentsHub.Services.Vyce;

// Touched once before any mode runs. .NET opens stdout lazily, and a write first made while this
// process is attached to the Vyce console goes to that console instead of the caller's pipe.
Console.Out.Flush();

if (args.Contains("--mcp"))
{
    Console.WriteLine("servers: " + string.Join(", ", McpRegistry.Load().Select(c => c.Name)));
    var listed = McpRegistry.Tools();
    Console.WriteLine($"mcp tools: {listed.Count}");
    foreach (var t in listed.Take(12)) Console.WriteLine($"  {t.Server}.{t.Name}: {t.Description}");
    return;
}


if (args.Contains("--screen"))
{
    // Scripted drive-and-look: opens the CLI, runs a list of steps, prints the screen. The only way
    // to learn what a TUI menu offers without a human watching it.
    var screenArgs = args.SkipWhile(a => a != "--screen").Skip(1).ToArray();
    string script = screenArgs.FirstOrDefault() ?? string.Empty;

    // Console output is captured before the first attach: .NET opens stdout lazily, and a write
    // made after attaching to another console goes to that console instead of the caller's pipe.
    Console.WriteLine("--- driving ---");
    Console.Out.Flush();

    using var tty = new VyceTty(Path.Combine(Path.GetTempPath(), "agentshub-vyce.log"));
    tty.Start(Directory.GetCurrentDirectory(), 6000, "cmd.exe /c vyce");

    foreach (string step in script.Split('|', StringSplitOptions.RemoveEmptyEntries))
    {
        if (step.StartsWith("wait:", StringComparison.Ordinal))
        {
            await Task.Delay(int.Parse(step[5..]));
        }
        else if (step.StartsWith("type:", StringComparison.Ordinal))
        {
            tty.TypeText(step[5..]);
            await Task.Delay(400);
        }
        else if (step.StartsWith("ctrl:", StringComparison.Ordinal))
        {
            tty.PressControl(step[5]);
            await Task.Delay(400);
        }
        else if (step == "shot")
        {
            Console.WriteLine("--- shot ---");
            Console.WriteLine(tty.ReadScreen());
            Console.Out.Flush();
        }
        else
        {
            switch (step)
            {
                case "enter": tty.PressEnter(); break;
                case "up": tty.PressArrowUp(); break;
                case "down": tty.PressArrowDown(); break;
                case "esc": tty.SendEscape(); break;
            }

            await Task.Delay(500);
        }
    }

    Console.WriteLine("--- screen ---");
    Console.WriteLine(tty.ReadScreen());
    return;
}

if (args.Contains("--inject"))
{
    // Isolates the console injector from the Vyce TUI: a plain shell either receives the line or it
    // does not, and the screen says which.
    using var tty = new VyceTty(Path.Combine(Path.GetTempPath(), "agentshub-vyce.log"));
    var injectArgs = args.SkipWhile(a => a != "--inject").Skip(1).ToArray();
    string command = injectArgs.FirstOrDefault() ?? "cmd.exe /k";
    int delay = injectArgs.Length > 1 && int.TryParse(injectArgs[1], out var parsed) ? parsed : 2500;
    string typed = injectArgs.Length > 2 ? injectArgs[2] : "echo INJECT-OK";

    tty.Start(Directory.GetCurrentDirectory(), delay, command);
    Console.WriteLine("--- before ---");
    Console.WriteLine(tty.ReadScreen());

    // "noenter" leaves the text in the box so a slash command opens its menu instead of running.
    bool pressEnter = !injectArgs.Contains("noenter");

    if (typed == "ctrl+p")
    {
        tty.PressControl('p');
    }
    else
    {
        tty.TypeLine(typed, pressEnter);
    }

    await Task.Delay(2000);

    Console.WriteLine("--- after ---");
    Console.WriteLine(tty.ReadScreen());
    return;
}

if (args.Contains("--tty"))
{
    // Temporary probe: shows what a Node CLI sees inside this app's pseudo-console.
    string probe = args.SkipWhile(a => a != "--tty").Skip(1).FirstOrDefault()
        ?? "node -e \"console.log('IN='+process.stdin.isTTY+' OUT='+process.stdout.isTTY+' RAW='+(typeof process.stdin.setRawMode)+' COLS='+process.stdout.columns)\"";

    var pty = new AgentsHub.Services.ConPty.ConPtySession();
    var seen = new System.Text.StringBuilder();
    pty.OutputDataReceived += text => { lock (seen) seen.Append(text); };

    Console.WriteLine($"probe: {probe}");
    Console.WriteLine($"started: {pty.Start(probe, Directory.GetCurrentDirectory(), 120, 40)}");
    await Task.Delay(5000);
    lock (seen) Console.WriteLine("--- child output ---\n" + seen);
    pty.Stop();
    return;
}

if (args.Contains("--vyce"))
{
    // End-to-end: spawn the CLI in a hidden pseudo-console, send one prompt, read the answer back
    // out of its own session file. This is the only way to know the adapter works rather than compiles.
    string vyceRoot = args.SkipWhile(a => a != "--vyce").Skip(1).FirstOrDefault()
        ?? Directory.GetCurrentDirectory();

    // A unique token per run: a repeated prompt is indistinguishable from the restored transcript,
    // so a probe that asks the same thing every time cannot tell an answer from the history.
    string token = "PROBE-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    Console.WriteLine($"root={vyceRoot}");
    Console.WriteLine($"sending prompt, this can take a while on a cold start... ({token})");

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
    var chunks = new List<string>();

    try
    {
        var vyceReply = await VyceEngine.Instance.SendAsync(
            $"reply with exactly: {token}",
            vyceRoot,
            new Progress<string>(text => { lock (chunks) chunks.Add(text); }),
            cts.Token);

        Console.WriteLine($"streamed chunks: {chunks.Count}");
        Console.WriteLine($"model: {vyceReply.Model}");
        Console.WriteLine($"session: {vyceReply.SessionFile}");
        Console.WriteLine($"answer: {vyceReply.Text}");
        Console.WriteLine(vyceReply.Text.Contains(token, StringComparison.OrdinalIgnoreCase)
            ? "RESULT: PASS - the CLI answered through the console"
            : "RESULT: answered, but not with the expected text");

        // A second turn proves the console survived: a re-spawn would have started a new chat and
        // lost the first message.
        if (args.Contains("--twice"))
        {
            var second = await VyceEngine.Instance.SendAsync(
                "Repeat only the token you were asked to reply with, nothing else.",
                vyceRoot,
                new Progress<string>(_ => { }),
                cts.Token);

            Console.WriteLine($"second turn: {second.Text.Trim()}");
            Console.WriteLine($"same console: {VyceEngine.Instance.IsRunning}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"RESULT: FAIL - {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        // The CLI lives in its own process tree. Left alone it outlives this probe and locks the
        // session file the next run needs.
        VyceEngine.Instance.Dispose();
    }

    return;
}

if (args.Contains("--models"))
{
    // Live: what the CLI's own menu offers, and optionally a switch to prove the picker drive works.
    string modelRoot = args.SkipWhile(a => a != "--models").Skip(1).FirstOrDefault() ?? Directory.GetCurrentDirectory();
    string? switchTo = args.SkipWhile(a => a != "--models").Skip(2).FirstOrDefault();

    try
    {
        // Before any console exists: the chip names the model on mount, and that call must be a
        // file read, not a five-second cold start.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string cheap = VyceEngine.Instance.CurrentModel();
        watch.Stop();
        Console.WriteLine($"label from disk: '{cheap}' in {watch.ElapsedMilliseconds} ms, console running={VyceEngine.Instance.IsRunning}");

        var list = await VyceEngine.Instance.ListModelsAsync(modelRoot);

        Console.WriteLine($"current: {list.Current}");
        foreach (var row in list.Models)
        {
            Console.WriteLine($"  {(row.Available ? " " : "x")} {row.Name,-20} {row.Note}");
        }

        if (!string.IsNullOrWhiteSpace(switchTo))
        {
            bool changed = await VyceEngine.Instance.SetModelAsync(switchTo, modelRoot);
            var after = await VyceEngine.Instance.ListModelsAsync(modelRoot);
            Console.WriteLine($"switch to {switchTo}: {(changed ? "PASS" : "FAIL")}, current now {after.Current}");

            // A marker can move without the engine changing what answers. One turn proves the switch
            // reached the thing that does the work.
            if (args.Contains("--ask"))
            {
                var modelCheck = await VyceEngine.Instance.SendAsync(
                    "reply with exactly: MODEL-CHECK",
                    modelRoot,
                    new Progress<string>(_ => { }));

                Console.WriteLine($"answer model: {modelCheck.Model}, reply: {modelCheck.Text.Trim()}");
                Console.WriteLine(string.Equals(modelCheck.Model, switchTo, StringComparison.OrdinalIgnoreCase)
                    ? "RESULT: PASS - answers now come from the selected model"
                    : $"RESULT: FAIL - answered with {modelCheck.Model}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"RESULT: FAIL - {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        VyceEngine.Instance.Dispose();
    }

    return;
}

if (args.Contains("--redact"))
{
    // A log line shaped like the ones this app writes, carrying everything that must never leave.
    const string dirty = """
        2026-10-30T00:00:00Z token found via userToken (json-object)
        Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature
        {"userToken":"sk-live-abcdef0123456789","api_key":"AKIAIOSFODNN7EXAMPLE"}
        profile=C:\Users\someone\AppData\Roaming\AgentsHub\deepseek-accounts\default
        owner=gaming@example.com key=ghp_0123456789abcdefghijklmnopqrstuvwxyz
        """;

    string clean = LogBundleService.Redact(dirty);
    Console.WriteLine("--- redacted ---");
    Console.WriteLine(clean);

    bool leaked =
        clean.Contains("sk-live-abcdef") ||
        clean.Contains("AKIAIOSFODNN7EXAMPLE") ||
        clean.Contains("eyJhbGciOiJIUzI1NiJ9") ||
        clean.Contains("ghp_0123456789") ||
        clean.Contains("gaming@example.com") ||
        clean.Contains("Users\\someone");

    Console.WriteLine($"leak check: {(leaked ? "FAIL - something survived" : "PASS - nothing survived")}");
    return;
}

if (args.Contains("--guide"))
{
    // What the engine primes with: working directory, skills, MCP tools, and the rules that travel
    // inside the guide itself.
    string guideRoot = args.SkipWhile(a => a != "--guide").Skip(1).FirstOrDefault() ?? Directory.GetCurrentDirectory();

    var guideSkills = SkillCatalog.Discover(guideRoot);
    IReadOnlyList<McpToolInfo> guideMcp = Array.Empty<McpToolInfo>();
    try { guideMcp = McpRegistry.Tools(); } catch (Exception ex) { Console.WriteLine($"mcp unavailable: {ex.Message}"); }

    var guide = QwenTools.Guide(guideRoot, guideSkills, guideMcp);

    Console.WriteLine($"root={guideRoot}");
    Console.WriteLine($"skills={guideSkills.Count} mcpTools={guideMcp.Count} length={guide.Length}");
    Console.WriteLine($"has RULES: {guide.Contains("RULES")}");
    Console.WriteLine($"has SKILLS section: {guide.Contains("SKILLS")}");
    Console.WriteLine($"has MCP TOOLS section: {guide.Contains("MCP TOOLS")}");
    return;
}

if (args.Contains("--parse"))
{
    // The exact shape the Qwen page produces: a code block whose fences do not survive innerText.
    const string bare = """{"action": "write_file", "path": "h.py", "content": "print(\"hello world\")"}""";

    var bareCalls = QwenTools.Extract(bare);
    Console.WriteLine($"bare: calls={bareCalls.Count} " +
        $"action={(bareCalls.Count > 0 ? bareCalls[0].GetProperty("action").GetString() : "-")}");
    Console.WriteLine($"bare strip: '{QwenTools.Strip(bare)}'");

    string prose = "Writing that now.\n" + bare + "\nDone.";
    var proseCalls = QwenTools.Extract(prose);
    Console.WriteLine($"prose: calls={proseCalls.Count} strip='{QwenTools.Strip(prose)}'");

    // The rendered shape: prose, then the block's language label on its own line, then the object.
    string rendered = "I will create the h.py file with the requested print statement.\ntool\n" + bare;
    var renderedCalls = QwenTools.Extract(rendered);
    Console.WriteLine($"rendered: calls={renderedCalls.Count} strip='{QwenTools.Strip(rendered)}'");

    // Streaming uses Visible, not Strip. It must hide the same object, or the transcript shows it.
    Console.WriteLine($"rendered visible: '{QwenTools.Visible(rendered)}'");
    Console.WriteLine($"mid-stream visible: '{QwenTools.Visible("I will create the h.py file.\ntool\n{\"action\": \"write_fil")}'");

    // Ordinary JSON in an answer must survive both calls untouched.
    const string harmless = """{"name": "value", "count": 2}""";
    Console.WriteLine($"harmless: calls={DeepSeekTools.Extract(harmless).Count} strip='{DeepSeekTools.Strip(harmless)}'");
    return;
}

var root = Path.Combine(Path.GetTempPath(), "ds-tool-test");
if (Directory.Exists(root)) Directory.Delete(root, true);
Directory.CreateDirectory(root);

void Check(string name, bool ok, string? detail = null) =>
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail is null ? "" : "  " + detail)}");

// --- extraction
const string reply = """
    Sure, writing that now.
    ```tool
    {"action": "write_file", "path": "hi.py", "content": "print('hello world')\n"}
    ```
    """;
var calls = DeepSeekTools.Extract(reply);
Check("extract one call", calls.Count == 1, $"count={calls.Count}");
Check("strip leaves prose", DeepSeekTools.Strip(reply) == "Sure, writing that now.");

// --- write, read, list
var (writeSummary, _) = DeepSeekTools.Execute("write_file", calls[0], root);
Check("write_file", File.Exists(Path.Combine(root, "hi.py")), writeSummary);

var readArgs = JsonDocument.Parse("""{"action":"read_file","path":"hi.py"}""").RootElement.Clone();
var (readSummary, readBody) = DeepSeekTools.Execute("read_file", readArgs, root);
Check("read_file round trip", readBody.Contains("print('hello world')"), readSummary);

var listArgs = JsonDocument.Parse("""{"action":"list_dir","path":"."}""").RootElement.Clone();
var (listSummary, listBody) = DeepSeekTools.Execute("list_dir", listArgs, root);
Check("list_dir", listBody.Contains("hi.py"), listSummary);

// --- confinement
foreach (var bad in new[] { """{"action":"read_file","path":"C:/Windows/win.ini"}""", """{"action":"read_file","path":"../../etc/passwd"}""" })
{
    var call = JsonDocument.Parse(bad).RootElement.Clone();
    try
    {
        DeepSeekTools.Execute("read_file", call, root);
        Check("reject " + bad, false, "no exception");
    }
    catch (Exception ex)
    {
        Check("reject " + bad, true, ex.Message[..Math.Min(48, ex.Message.Length)]);
    }
}

// --- a reply with no tool block ends the turn
Check("no block means no calls", DeepSeekTools.Extract("Just an answer.").Count == 0);
// --- shell and search
var cmd = JsonDocument.Parse("""{"action":"run_command","command":"echo hello-from-tool"}""").RootElement.Clone();
var (cmdSummary, cmdBody) = DeepSeekTools.Execute("run_command", cmd, root);
Check("run_command captures output", cmdBody.Contains("hello-from-tool"), cmdSummary);

var fail = JsonDocument.Parse("""{"action":"run_command","command":"exit 3"}""").RootElement.Clone();
var (failSummary, failBody) = DeepSeekTools.Execute("run_command", fail, root);
Check("run_command reports the exit code", failBody.Contains("exit code 3"), failSummary);

var search = JsonDocument.Parse("""{"action":"search_text","query":"hello world"}""").RootElement.Clone();
var (searchSummary, searchBody) = DeepSeekTools.Execute("search_text", search, root);
Check("search_text finds the line", searchBody.Contains("hi.py:1"), searchSummary);

// --- the nested-args shape that broke mcp_call
var nestedCall = DeepSeekTools.Extract("```tool\n{\"action\":\"mcp_call\",\"args\":{\"server\":\"blender\",\"tool\":\"get_scene_info\"}}\n```");
Check("nested args keep the action", nestedCall.Count == 1 && nestedCall[0].GetProperty("action").GetString() == "mcp_call");
Check("nested server resolves", DeepSeekTools.Preview("mcp_call", nestedCall[0]).Contains("blender.get_scene_info"),
    DeepSeekTools.Preview("mcp_call", nestedCall[0]));

// --- a write reports what changed, not just a byte count
var write1 = JsonDocument.Parse("""{"action":"write_file","path":"diff.txt","content":"one\ntwo\nthree\n"}""").RootElement.Clone();
DeepSeekTools.Execute("write_file", write1, root);
var write2 = JsonDocument.Parse("""{"action":"write_file","path":"diff.txt","content":"one\nTWO\nthree\nfour\n"}""").RootElement.Clone();
var (_, diffBody) = DeepSeekTools.Execute("write_file", write2, root);
Check("diff shows the change", diffBody.Contains("- two") && diffBody.Contains("+ TWO") && diffBody.Contains("+ four"), diffBody.Split('\n').Length + " lines");

// --- the streaming filter (P0: the block must never reach the prose)
var openFence = DeepSeekTools.Visible("Sure\n```tool\n{\"action\"");
Check("holds an open fence", !openFence.Contains("```") && !openFence.Contains("action"), openFence.Replace("\n", "\\n"));

var closed = DeepSeekTools.Visible("Sure\n```tool\n{\"action\":\"read_file\",\"path\":\"a\"}\n```\nDone.");
Check("drops a tool block", !closed.Contains("action") && closed.Contains("Sure") && closed.Contains("Done"), closed.Replace("\n", "\\n"));

var code = DeepSeekTools.Visible("Try://n```python\nprint(1)\n```\n");
Check("keeps ordinary code", code.Contains("print(1)"), code.Replace("\n", "\\n"));

// --- the model menu is scraped from a screen, so the scraper gets a real one as a fixture
const string modelMenu = """
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀   Select model                                                           esc            ▀    ▀
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀  > type to search                                                                       ▀
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀                                                                                                ▀
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀    agnes-3.0-flash                                       [UNLIMITED]  512k     ▀
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀    gpt-6-luna                                            [UNLIMITED]  250k   ▀  ▀ ▀
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀  • glm-5.3-flash                               [SLOW] [UNLIMITED]  500k  ✓      ▀
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀  Coming soon
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀  ○ deepseek-v4.1         Careful reasoning for tests and tricky logic
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀  ○ qwen-3.8              Strong code generation with a huge window
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀  A fast all-rounder for everyday coding                                               ▀
    ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀  type to search · ↑↓ move · enter select · esc close                             ▀
    """;

var menu = VyceEngine.ParseModelPicker(modelMenu);
Check("model menu lists every row", menu.Models.Count == 5, $"rows={menu.Models.Count}");
Check("model menu marks the current one", menu.Current == "glm-5.3-flash", menu.Current);
Check(
    "model menu keeps coming-soon rows unpickable",
    menu.Models.Where(m => !m.Available).Select(m => m.Name).SequenceEqual(["deepseek-v4.1", "qwen-3.8"]),
    string.Join(",", menu.Models.Where(m => !m.Available).Select(m => m.Name)));

Console.WriteLine("\nroot: " + root);
