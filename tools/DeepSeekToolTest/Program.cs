using System.Text.Json;
using AgentsHub.Services;
using AgentsHub.Services.DeepSeek;
using AgentsHub.Services.Qwen;

if (args.Contains("--mcp"))
{
    Console.WriteLine("servers: " + string.Join(", ", McpRegistry.Load().Select(c => c.Name)));
    var listed = McpRegistry.Tools();
    Console.WriteLine($"mcp tools: {listed.Count}");
    foreach (var t in listed.Take(12)) Console.WriteLine($"  {t.Server}.{t.Name}: {t.Description}");
    return;
}


if (args.Contains("--redact"))
{
    // A log line shaped like the ones this app writes, carrying everything that must never leave.
    const string dirty = """
        2026-10-30T00:00:00Z token found via userToken (json-object)
        Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature
        {"userToken":"sk-live-abcdef0123456789","api_key":"AKIAIOSFODNN7EXAMPLE"}
        profile=C:\Users\Gaming\AppData\Roaming\AgentsHub\deepseek-accounts\default
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
        clean.Contains("Users\\Gaming");

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

Console.WriteLine("\nroot: " + root);
