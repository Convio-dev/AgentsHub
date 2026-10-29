using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DeepSeekSpike;

/// <summary>
/// Phase 0 proof for the DeepSeek engine: can our own WebView2 replace the Playwriter extension?
///
/// It loads chat.deepseek.com in a WebView2 with its own profile, waits for a signed-in composer,
/// then proves the token works by calling the same internal endpoint the Python reference reads.
/// Everything goes through CDP `Runtime.evaluate`, which is exactly what Playwriter's extension does
/// with chrome.debugger, only in-process and without Node or a relay.
///
/// Run it, sign in once, and read the result file it writes.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SpikeForm());
    }
}

internal sealed class SpikeForm : Form
{
    private const string StartUrl = "https://chat.deepseek.com/";

    /// <summary>Separate from the app's own WebView2 profile so a reset is one folder.</summary>
    private static readonly string ProfileDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentsHub", "deepseek-spike-profile");

    private static readonly string ResultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AgentsHub", "deepseek-spike-result.json");

    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Top,
        Height = 46,
        Padding = new Padding(12, 8, 12, 8),
        Font = new Font("Consolas", 9f),
        Text = "starting WebView2...",
    };

    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private readonly Button _signOut = new()
    {
        Text = "Sign out (clear this profile)",
        Dock = DockStyle.Bottom,
        Height = 34,
        FlatStyle = FlatStyle.System,
    };

    private readonly Button _sendTest = new()
    {
        Text = "Send a test prompt (DOM path, no Playwright)",
        Dock = DockStyle.Bottom,
        Height = 34,
        FlatStyle = FlatStyle.System,
        Enabled = false,
    };

    private bool _probed;
    private bool _signOutRequested;
    private bool _sending;

    public SpikeForm()
    {
        Text = "DeepSeek spike: sign in once, then this window closes itself";
        Width = 980;
        Height = 760;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);
        Controls.Add(_status);
        Controls.Add(_sendTest);
        Controls.Add(_signOut);

        _sendTest.Click += async (_, _) => await SendTestAsync();

        _signOut.Click += (_, _) =>
        {
            _signOutRequested = true;
            Close();
        };

        Shown += async (_, _) => await InitAsync();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (!_signOutRequested) return;

        // The profile folder is the login. Clearing it is the sign-out, and it can only be
        // deleted once WebView2 has released its lock, which is why this runs after close.
        try
        {
            if (Directory.Exists(ProfileDir)) Directory.Delete(ProfileDir, recursive: true);
        }
        catch
        {
            // A locked file means the folder survives; the next run still works.
        }
    }

    private async Task InitAsync()
    {
        try
        {
            Directory.CreateDirectory(ProfileDir);
            var env = await CoreWebView2Environment.CreateAsync(null, ProfileDir);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Navigate(StartUrl);
            _poll.Tick += async (_, _) => await PollAsync();
            _poll.Start();
            Report("waiting for the page and the sign-in...");
        }
        catch (Exception ex)
        {
            Report("WebView2 failed: " + ex.Message);
            WriteResult(new JsonObject { ["ok"] = false, ["error"] = ex.Message });
        }
    }

    private async Task PollAsync()
    {
        if (_web.CoreWebView2 is null) return;

        JsonElement check;
        try
        {
            check = await EvalAsync("""
                (() => {
                  let raw = localStorage.getItem('userToken'), token = null;
                  try { token = JSON.parse(raw).value; } catch (e) { token = raw; }
                  return {
                    hasToken: !!token,
                    composer: !!document.querySelector("textarea[name='search']"),
                    url: location.href
                  };
                })()
                """);
        }
        catch (Exception ex)
        {
            Report("script failed: " + ex.Message);
            return;
        }

        var hasToken = check.TryGetProperty("hasToken", out var t) && t.GetBoolean();
        var composer = check.TryGetProperty("composer", out var c) && c.GetBoolean();
        var url = check.TryGetProperty("url", out var u) ? u.GetString() : "";

        if (!hasToken || !composer)
        {
            var google = url?.Contains("accounts.google.com", StringComparison.OrdinalIgnoreCase) == true;
            var hint = google
                ? "\nGoogle is asking. If it refuses this browser, press Back and use the email form."
                : "\nSign in normally; this window closes itself when the token appears.";
            Report($"not signed in yet  token={hasToken}  composer={composer}{hint}\n{url}");
            // Written every poll so the spike is checkable without watching the window.
            WriteResult(new JsonObject
            {
                ["phase"] = "waiting-for-signin",
                ["hasToken"] = hasToken,
                ["composer"] = composer,
                ["url"] = url,
                ["at"] = DateTime.Now.ToString("O"),
            });
            return;
        }

        Report($"signed in  token=true  composer=true\n{url}");
        _sendTest.Enabled = true;
        if (_probed) return;
        _probed = true;
        await ProbeAsync(url ?? "");

        // `--send` makes the spike runnable without clicking, which is how it gets tested.
        if (Environment.GetCommandLineArgs().Contains("--send"))
        {
            await SendTestAsync();
            await Task.Delay(1500);
            Close();
        }
    }

    /// <summary>
    /// The path the reference never proves without Playwright: type into the composer, press
    /// Enter, then read the finished assistant message from the history API.
    ///
    /// Playwright is replaced by two DOM tricks: React only notices a textarea change when the
    /// native value setter is used (a plain assignment is swallowed), and the submit is a
    /// synthetic Enter keydown because there is no stable selector for the send button.
    /// </summary>
    private async Task SendTestAsync()
    {
        if (_sending) return;
        _sending = true;
        _sendTest.Enabled = false;
        Report("sending a test prompt through the page DOM...");

        var promptJson = JsonSerializer.Serialize("Reply with exactly: PONG. Nothing else.");
        var js = $$"""
            (async () => {
              const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
              const token = (() => {
                try { return JSON.parse(localStorage.getItem('userToken')).value; }
                catch (e) { return localStorage.getItem('userToken'); }
              })();
              if (!token) return { ok: false, error: 'not_logged_in' };

              const chatIdOf = (u) => (String(u).match(/\/s\/([0-9a-fA-F-]{20,})/) || [])[1] || null;
              const api = async (p) => (await fetch(p, { headers: { Authorization: 'Bearer ' + token } })).json();
              const msgs = (j) => (((j || {}).data || {}).biz_data || {}).chat_messages || [];

              const toggle = Array.from(document.querySelectorAll(".ds-toggle-button, [role='button'], button"))
                .find((e) => /deep\s*think/i.test(e.textContent || '') && (e.textContent || '').length < 40);
              if (toggle && (toggle.getAttribute('aria-pressed') || '') !== 'true') toggle.click();

              const box = document.querySelector("textarea[name='search']") || document.querySelector('textarea');
              if (!box) return { ok: false, error: 'composer_missing' };

              let chatId = chatIdOf(location.href);
              let baseline = 0;
              if (chatId) {
                baseline = msgs(await api('/api/v0/chat/history_messages?chat_session_id=' + chatId))
                  .reduce((m, x) => Math.max(m, x.message_id || 0), 0);
              }

              const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
              setter.call(box, {{promptJson}});
              box.dispatchEvent(new Event('input', { bubbles: true }));
              await sleep(120);
              box.focus();
              box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true }));

              const started = Date.now();
              while (!chatId && Date.now() - started < 20000) {
                chatId = chatIdOf(location.href);
                if (!chatId) await sleep(500);
              }

              let answer = null;
              while (Date.now() - started < 90000) {
                if (chatId) {
                  const fresh = msgs(await api('/api/v0/chat/history_messages?chat_session_id=' + chatId))
                    .filter((m) => (m.message_id || 0) > baseline && m.role === 'ASSISTANT');
                  const last = fresh[fresh.length - 1];
                  if (last && String(last.status || '').toUpperCase() === 'FINISHED' &&
                      (last.fragments || []).some((f) => (f.content || '').length > 0)) {
                    answer = last;
                    break;
                  }
                }
                await sleep(1200);
              }

              if (!answer) return { ok: false, error: 'timed_out', chat_id: chatId };
              const frags = (answer.fragments || []).map((f) => ({
                type: f.type, content: f.content || '',
                seconds: f.elapsed_secs === undefined ? null : f.elapsed_secs
              }));
              return {
                ok: true,
                chat_id: chatId,
                fragment_types: frags.map((f) => f.type),
                reasoning: frags.filter((f) => (f.type || '').toUpperCase() === 'THINK').map((f) => f.content).join(''),
                answer: frags.filter((f) => (f.type || '').toUpperCase() === 'RESPONSE').map((f) => f.content).join(''),
                seconds: (frags.find((f) => (f.type || '').toUpperCase() === 'THINK') || {}).seconds || null
              };
            })()
            """;

        try
        {
            var result = await EvalAsync(js);
            var ok = result.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.True;
            var answer = result.TryGetProperty("answer", out var a) ? a.GetString() ?? "" : "";
            var reasoning = result.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
            var types = result.TryGetProperty("fragment_types", out var t) ? t.GetRawText() : "[]";
            var chatId = result.TryGetProperty("chat_id", out var c) ? c.GetString() : null;

            WriteResult(new JsonObject
            {
                ["phase"] = "send-test",
                ["ok"] = ok,
                ["chatId"] = chatId,
                ["fragmentTypes"] = JsonNode.Parse(types),
                ["reasoningChars"] = reasoning.Length,
                ["answer"] = answer,
                ["at"] = DateTime.Now.ToString("O"),
            });

            Report(ok
                ? $"ok  chat={chatId}\nfragments={types}  reasoning={reasoning.Length} chars\nanswer: {Trim(answer)}"
                : "send failed: " + result.GetRawText());
        }
        catch (Exception ex)
        {
            Report("send threw: " + ex.Message);
        }
        finally
        {
            _sending = false;
            _sendTest.Enabled = true;
        }
    }

    private static string Trim(string text) =>
        text.Length <= 120 ? text : text[..120] + "...";

    /// <summary>
    /// The real question: does the bearer token open the internal API from this browser?
    /// A wrong session id is used on purpose, so a JSON answer with a code proves auth worked
    /// without reading anyone's chat history.
    /// </summary>
    private async Task ProbeAsync(string url)
    {
        Report("token found, calling history_messages with a dummy id...");

        JsonElement probe;
        try
        {
            probe = await EvalAsync("""
                (async () => {
                  let raw = localStorage.getItem('userToken'), token = null;
                  try { token = JSON.parse(raw).value; } catch (e) { token = raw; }
                  if (!token) return { ok: false, error: 'no token' };
                  const r = await fetch('/api/v0/chat/history_messages?chat_session_id=00000000-0000-0000-0000-000000000000',
                                        { headers: { Authorization: 'Bearer ' + token } });
                  const body = await r.json().catch(() => null);
                  return {
                    ok: r.ok,
                    status: r.status,
                    code: body && body.code,
                    msg: body && body.msg,
                    hasBizData: !!(body && body.data && body.data.biz_data)
                  };
                })()
                """);
        }
        catch (Exception ex)
        {
            Report("probe failed: " + ex.Message);
            WriteResult(new JsonObject { ["ok"] = false, ["error"] = ex.Message, ["url"] = url });
            return;
        }

        var ok = probe.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.True;
        var status = probe.TryGetProperty("status", out var s) ? s.ToString() : "?";
        var code = probe.TryGetProperty("code", out var cd) ? cd.ToString() : "?";
        var msg = probe.TryGetProperty("msg", out var m) ? m.ToString() : "";

        Report($"probe: http={status} code={code} msg={msg}\nresult written to {ResultPath}");

        var payload = new JsonObject
        {
            ["ok"] = ok,
            ["httpStatus"] = status,
            ["code"] = code,
            ["msg"] = msg,
            ["url"] = url,
            ["profile"] = ProfileDir,
            ["at"] = DateTime.Now.ToString("O"),
            ["probe"] = JsonNode.Parse(probe.GetRawText()),
        };
        WriteResult(payload);
    }

    /// <summary>
    /// Runs script in the page and returns its value.
    ///
    /// `ExecuteScriptAsync` does not await promises, so async probes go through CDP directly with
    /// `awaitPromise`. This is the same call Playwriter's extension makes through chrome.debugger.
    /// </summary>
    private async Task<JsonElement> EvalAsync(string expression)
    {
        var request = JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true });
        var raw = await _web.CoreWebView2!.CallDevToolsProtocolMethodAsync("Runtime.evaluate", request);

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out var thrown))
        {
            var text = thrown.TryGetProperty("text", out var tt) ? tt.GetString() : "script threw";
            throw new InvalidOperationException(text);
        }
        if (!doc.RootElement.TryGetProperty("result", out var result)) return default;
        return result.TryGetProperty("value", out var value) ? value.Clone() : default;
    }

    private void Report(string text)
    {
        _status.Text = text;
        Text = "DeepSeek spike: " + text.Split('\n')[0];
    }

    private static void WriteResult(JsonObject payload)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
            File.WriteAllText(ResultPath, payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // The label already shows the same text; a write failure must not kill the spike.
        }
    }
}
