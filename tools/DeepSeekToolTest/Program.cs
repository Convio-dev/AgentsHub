using System.Text.Json;
using AgentsHub.Services.DeepSeek;

if (args.Contains("--mcp"))
{
    Console.WriteLine("servers: " + string.Join(", ", McpRegistry.Load().Select(c => c.Name)));
    var listed = McpRegistry.Tools();
    Console.WriteLine($"mcp tools: {listed.Count}");
    foreach (var t in listed.Take(12)) Console.WriteLine($"  {t.Server}.{t.Name}: {t.Description}");
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

// --- the streaming filter (P0: the block must never reach the prose)
var openFence = DeepSeekTools.Visible("Sure\n```tool\n{\"action\"");
Check("holds an open fence", !openFence.Contains("```") && !openFence.Contains("action"), openFence.Replace("\n", "\\n"));

var closed = DeepSeekTools.Visible("Sure\n```tool\n{\"action\":\"read_file\",\"path\":\"a\"}\n```\nDone.");
Check("drops a tool block", !closed.Contains("action") && closed.Contains("Sure") && closed.Contains("Done"), closed.Replace("\n", "\\n"));

var code = DeepSeekTools.Visible("Try://n```python\nprint(1)\n```\n");
Check("keeps ordinary code", code.Contains("print(1)"), code.Replace("\n", "\\n"));

Console.WriteLine("\nroot: " + root);
