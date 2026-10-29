using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace QwenProbe;

/// <summary>
/// Phase 0 probe for chat.qwen.ai.
///
/// Opens the page offscreen with a real persistent profile, then dumps the parts of the DOM the
/// engine would have to drive: the composer, the send control, the model picker. Writes everything
/// to %TEMP%\qwen-probe.log because a window that is offscreen cannot be inspected by eye.
///
/// Pass --send to also submit one guest message and dump the response container. Without it the
/// probe only reads.
/// </summary>
internal static class Program
{
    private static bool argsMethods;
    private static bool argsOffscreen;
    private static bool argsNoThrottle;
    private static bool argsFlow;
    private static bool argsSubmit;
    private static bool argsNudge;

    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "qwen-probe.log");
    private static readonly StringBuilder Buffer = new();

    [STAThread]
    private static void Main(string[] args)
    {
        bool sendMessage = args.Any(a => string.Equals(a, "--send", StringComparison.OrdinalIgnoreCase));
        argsMethods = args.Any(a => string.Equals(a, "--methods", StringComparison.OrdinalIgnoreCase));
        argsOffscreen = args.Any(a => string.Equals(a, "--offscreen", StringComparison.OrdinalIgnoreCase));
        argsNoThrottle = args.Any(a => string.Equals(a, "--nothrottle", StringComparison.OrdinalIgnoreCase));
        argsFlow = args.Any(a => string.Equals(a, "--flow", StringComparison.OrdinalIgnoreCase));
        argsSubmit = args.Any(a => string.Equals(a, "--submit", StringComparison.OrdinalIgnoreCase));
        argsNudge = args.Any(a => string.Equals(a, "--nudge", StringComparison.OrdinalIgnoreCase));

        if (File.Exists(LogPath)) File.Delete(LogPath);
        Note($"probe start send={sendMessage}");

        ApplicationConfiguration.Initialize();

        var form = new Form
        {
            Text = "qwen-probe",
            // On screen, not at negative coordinates. A WebView2 window placed outside the desktop
            // bounds still composites, and the first version of this probe took the display down
            // with it. Small and ordinary is the safe shape.
            StartPosition = argsOffscreen ? FormStartPosition.Manual : FormStartPosition.CenterScreen,
            Location = argsOffscreen ? new Point(-32000, -32000) : new Point(100, 100),
            Size = new Size(900, 700),
            ShowInTaskbar = true,
        };

        var web = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(web);
        form.Shown += async (_, _) => await RunAsync(web, sendMessage, form);
        Application.Run(form);
    }

    private static async Task RunAsync(WebView2 web, bool sendMessage, Form form)
    {
        try
        {
            string profile = Path.Combine(Path.GetTempPath(), "qwen-probe-profile");
            var envOptions = argsNoThrottle
                ? new CoreWebView2EnvironmentOptions(
                    "--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows")
                : null;

            var env = await CoreWebView2Environment.CreateAsync(null, profile, envOptions);
            await web.EnsureCoreWebView2Async(env);

            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.NavigationCompleted += (_, e) => Note($"navigation completed ok={e.IsSuccess} url={web.Source}");

            Note("navigating https://chat.qwen.ai/");
            web.CoreWebView2.Navigate("https://chat.qwen.ai/");

            // The app hydrates after load; too early and every selector looks absent.
            await Task.Delay(9000);

            await DumpAsync(web, "composer", """
                (() => {
                  const pick = (el) => el ? {
                    tag: el.tagName.toLowerCase(),
                    type: el.getAttribute('type'),
                    cls: (el.className || '').toString().slice(0, 120),
                    ph: el.getAttribute('placeholder') || el.getAttribute('data-placeholder') || '',
                    ce: el.getAttribute('contenteditable'),
                    aria: el.getAttribute('aria-label') || '',
                    id: el.id || ''
                  } : null;

                  return {
                    textareas: [...document.querySelectorAll('textarea')].map(pick),
                    editables: [...document.querySelectorAll('[contenteditable="true"]')].map(pick),
                    textInputs: [...document.querySelectorAll('input[type="text"], input:not([type])')].map(pick)
                  };
                })()
                """);

            await DumpAsync(web, "buttons", """
                (() => {
                  const rows = [...document.querySelectorAll('button, [role="button"]')].map(b => ({
                    text: (b.textContent || '').trim().slice(0, 40),
                    aria: b.getAttribute('aria-label') || '',
                    cls: (b.className || '').toString().slice(0, 80),
                    disabled: b.disabled === true
                  }));

                  return rows.filter(r => r.text || r.aria).slice(0, 60);
                })()
                """);

            await DumpAsync(web, "modelPicker", """
                (() => {
                  // The picker reads "Auto" and opens a menu of Auto / Thinking / Fast.
                  const hit = [...document.querySelectorAll('button, [role="button"], [class*="select"]')]
                    .filter(el => /^(auto|thinking|fast)$/i.test((el.textContent || '').trim()));

                  return hit.map(el => ({
                    text: (el.textContent || '').trim(),
                    cls: (el.className || '').toString().slice(0, 120),
                    parent: (el.parentElement?.className || '').toString().slice(0, 120)
                  }));
                })()
                """);

            await DumpAsync(web, "titleBar", """
                (() => {
                  const el = [...document.querySelectorAll('button, div')]
                    .find(e => /qwen[\d.]*-?\w*/i.test((e.textContent || '').trim()) && (e.textContent || '').trim().length < 30);
                  return el ? { text: el.textContent.trim(), cls: (el.className || '').toString().slice(0, 120) } : null;
                })()
                """);

            if (sendMessage)
            {
                await SendGuestMessageAsync(web);
            }

            if (argsMethods)
            {
                await TryTypingMethodsAsync(web);
            }

            if (argsFlow)
            {
                await RunFlowAsync(web);
            }

            if (argsSubmit)
            {
                await TrySubmitAsync(web);
            }

            if (argsNudge)
            {
                await TryNudgeAsync(web);
            }

            Note("probe done");
        }
        catch (Exception ex)
        {
            Note($"FATAL {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Flush();
            form.Close();
        }
    }

    private static async Task SendGuestMessageAsync(WebView2 web)
    {
        Note("--- sending one guest message");

        // Focus whatever the composer is, insert text the way a paste would, then press Enter.
        string typed = await EvalAsync(web, """
            (() => {
              const box = document.querySelector('textarea')
                || document.querySelector('[contenteditable="true"]');
              if (!box) return 'no-composer';

              box.focus();

              if (box.tagName === 'TEXTAREA') {
                const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
                setter.call(box, 'Reply with exactly: PROBE-OK');
                box.dispatchEvent(new Event('input', { bubbles: true }));
              } else {
                box.textContent = 'Reply with exactly: PROBE-OK';
                box.dispatchEvent(new InputEvent('input', { bubbles: true }));
              }

              return 'typed';
            })()
            """);

        Note($"typed: {typed}");
        await Task.Delay(600);

        string sent = await EvalAsync(web, """
            (() => {
              const btn = [...document.querySelectorAll('button')]
                .find(b => b.getAttribute('aria-label') === 'Send' || /send/i.test(b.getAttribute('aria-label') || ''));
              if (btn) { btn.click(); return 'clicked-send'; }

              const box = document.querySelector('textarea') || document.querySelector('[contenteditable="true"]');
              if (box) {
                box.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, bubbles: true }));
                return 'pressed-enter';
              }
              return 'no-target';
            })()
            """);

        Note($"sent: {sent}");
        await Task.Delay(12000);

        await DumpAsync(web, "responseContainers", """
            (() => {
              const out = [];
              for (const el of document.querySelectorAll('[class*="markdown"], [class*="message"], [class*="answer"], [class*="response"]')) {
                const text = (el.textContent || '').trim();
                if (text.length < 4 || text.length > 2000) continue;
                out.push({
                  cls: (el.className || '').toString().slice(0, 120),
                  tag: el.tagName.toLowerCase(),
                  head: text.slice(0, 120)
                });
              }
              return out.slice(0, 12);
            })()
            """);

        await DumpAsync(web, "urlAfterSend", "location.href");

        // The fastest way to see what the page did: what it now shows, and whether the box cleared.
        await DumpAsync(web, "composerAfterSend", """
            (() => {
              const box = document.querySelector('textarea.message-input-textarea');
              return { value: (box && box.value || '').slice(0, 80), cleared: !!(box && box.value === '') };
            })()
            """);

        await DumpAsync(web, "bodyTail", "document.body.innerText.replace(/\\s+/g, ' ').slice(-700)");

        // A first guest message raises an age gate. Report its controls, then clear it and retry.
        await DumpAsync(web, "ageGate", """
            (() => {
              const text = document.body.innerText || '';
              if (!/year were you born/i.test(text)) return 'no-gate';

              const inputs = [...document.querySelectorAll('input')].map(i => ({
                type: i.getAttribute('type'), cls: (i.className || '').toString().slice(0, 60)
              }));

              const buttons = [...document.querySelectorAll('button')]
                .filter(b => /continue|confirm|submit/i.test((b.textContent || '').trim()))
                .map(b => ({ text: b.textContent.trim().slice(0, 20), cls: (b.className || '').toString().slice(0, 80) }));

              return { gate: true, inputs, buttons };
            })()
            """);

        string cleared = await EvalAsync(web, """
            (() => {
              const text = document.body.innerText || '';
              if (!/year were you born/i.test(text)) return 'no-gate';

              // The year is pre-filled ("2000") and is not an <input>, so Continue is the only
              // control that matters. Touching the file input throws, which is what failed before.
              const btn = [...document.querySelectorAll('button')]
                .find(b => /^\s*continue\s*$/i.test((b.textContent || '').trim()));

              if (btn) { btn.click(); return 'submitted'; }
              return 'no-continue-button';
            })()
            """);

        Note($"ageGate submit: {cleared}");
        await Task.Delay(2500);

        await DumpAsync(web, "afterGate", "document.body.innerText.replace(/\\s+/g, ' ').slice(-500)");

        // Retry the message now that the gate is out of the way.
        await EvalAsync(web, """
            (() => {
              const box = document.querySelector('textarea.message-input-textarea');
              if (!box) return 'no-composer';
              box.focus();
              const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
              setter.call(box, 'Reply with exactly: PROBE-OK');
              box.dispatchEvent(new Event('input', { bubbles: true }));
              return 'typed';
            })()
            """);

        await Task.Delay(500);

        await EvalAsync(web, """
            (() => {
              const btn = [...document.querySelectorAll('button')]
                .find(b => /send/i.test(b.getAttribute('aria-label') || ''));
              if (btn) { btn.click(); return 'clicked'; }
              return 'no-send';
            })()
            """);

        await Task.Delay(14000);

        await DumpAsync(web, "afterRetry", "document.body.innerText.replace(/\\s+/g, ' ').slice(-800)");
        await DumpAsync(web, "urlAfterRetry", "location.href");


        await DumpAsync(web, "messageNodes", """
            (() => {
              const hits = [...document.querySelectorAll('[class*="chat-message"], [data-message-role], [class*="assistant"], [class*="bubble"]')];
              return hits.slice(0, 10).map(el => ({
                cls: (el.className || '').toString().slice(0, 90),
                head: (el.textContent || '').trim().slice(0, 80)
              }));
            })()
            """);
    }

    /// <summary>
    /// Tries each way of getting text into the composer and reports which one leaves it filled.
    ///
    /// The app's own turn used the value setter and the box stayed empty, so this measures instead of
    /// guessing again. Each method clears the box first.
    /// </summary>
    private static async Task TryTypingMethodsAsync(WebView2 web)
    {
        Note("--- typing methods");

        // The engine compared this string against "true". Print it and see what it really is.
        Note($"boolean through EvalAsync -> '{await EvalAsync(web, "!!document.querySelector('textarea')")}'");

        string reset = """
            (() => {
              const box = document.querySelector('textarea');
              if (!box) return 'no-textarea';
              box.focus();
              box.click();
              return 'focused';
            })()
            """;
        Note($"focus: {await EvalAsync(web, reset)}");

        // A: the prototype setter plus input, which is what the DeepSeek engine uses.
        await EvalAsync(web, """
            (() => {
              const box = document.querySelector('textarea');
              const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
              setter.call(box, '');
              const s2 = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
              s2.call(box, 'METHOD-A');
              box.dispatchEvent(new Event('input', { bubbles: true }));
              return box.value;
            })()
            """);
        await Task.Delay(400);
        Note($"A value+input -> {await ReadBoxAsync(web)}");

        // B: clear, then CDP insertText.
        await ClearBoxAsync(web);
        await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",
            JsonSerializer.Serialize(new { text = "METHOD-B" }));
        await Task.Delay(400);
        Note($"B insertText -> {await ReadBoxAsync(web)}");

        // C: execCommand on the focused element.
        await ClearBoxAsync(web);
        await EvalAsync(web, """
            (() => {
              const box = document.querySelector('textarea');
              box.focus();
              document.execCommand('insertText', false, 'METHOD-C');
              return box.value;
            })()
            """);
        await Task.Delay(400);
        Note($"C execCommand -> {await ReadBoxAsync(web)}");

        // D: one key event per character, the closest thing to a person typing.
        await ClearBoxAsync(web);
        await EvalAsync(web, "document.querySelector('textarea').focus()");
        foreach (char ch in "METHOD-D")
        {
            await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new
                {
                    type = "char",
                    text = ch.ToString(),
                    unmodifiedText = ch.ToString(),
                    key = ch.ToString(),
                }));
        }
        await Task.Delay(400);
        Note($"D per-char -> {await ReadBoxAsync(web)}");

        await DumpAsync(web, "activeElement", """
            (() => {
              const el = document.activeElement;
              return el ? `${el.tagName.toLowerCase()}.${(el.className || '').toString().slice(0, 60)}` : 'none';
            })()
            """);
    }

    private static Task<string> ReadBoxAsync(WebView2 web) => EvalAsync(web, """
        (() => {
          const box = document.querySelector('textarea');
          const text = box ? (box.value || '') : '';
          return `${text.length > 0 ? 'FILLED' : 'EMPTY'}:${text.slice(0, 20)}`;
        })()
        """);

    private static async Task ClearBoxAsync(WebView2 web)
    {
        await EvalAsync(web, """
            (() => {
              const box = document.querySelector('textarea');
              if (!box) return 'none';
              box.focus();
              const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
              setter.call(box, '');
              box.dispatchEvent(new Event('input', { bubbles: true }));
              return 'cleared';
            })()
            """);
        await Task.Delay(200);
    }

    /// <summary>
    /// The whole turn: clear the age gate, type, send, then report which selector finds the answer.
    /// This is the same sequence the engine runs, so the selectors it ships are the ones proven here.
    /// </summary>
    private static async Task RunFlowAsync(WebView2 web)
    {
        Note("--- flow");

        await DumpAsync(web, "gateDom", """
            (() => {
              const text = document.body.innerText || '';
              if (!/year were you born/i.test(text)) return 'no-gate';
              const selects = [...document.querySelectorAll('select')].map(s => ({
                cls: (s.className || '').toString().slice(0, 60),
                value: s.value,
                options: [...s.options].slice(0, 6).map(o => (o.textContent || '').trim())
              }));
              const buttons = [...document.querySelectorAll('button')]
                .map(b => (b.textContent || '').trim()).filter(t => t.length > 0 && t.length < 20).slice(0, 6);
              return JSON.stringify({ selects, buttons });
            })()
            """);

        string gate = await EvalAsync(web, """
            (() => {
              const text = document.body.innerText || '';
              if (!/year were you born/i.test(text)) return 'no-gate';
              const btn = [...document.querySelectorAll('button')]
                .find(b => /^\s*continue\s*$/i.test((b.textContent || '').trim()));
              if (btn) { btn.click(); return 'cleared'; }
              return 'no-button';
            })()
            """);
        Note($"gate: {gate}");
        await Task.Delay(2000);

        await EvalAsync(web, "(() => { const b = document.querySelector('textarea'); b.click(); b.focus(); return 'focused'; })()");
        await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",
            JsonSerializer.Serialize(new { text = "Reply with exactly: PROBE-OK" }));
        await Task.Delay(400);
        Note($"typed: {await ReadBoxAsync(web)}");

        // Which buttons exist at all. The first attempt clicked something matching /send/ and the box
        // never cleared, so list the candidates before choosing one.
        await DumpAsync(web, "buttonsBeforeSend", """
            (() => {
              return [...document.querySelectorAll('button')]
                .map((b, i) => ({
                  i,
                  aria: b.getAttribute('aria-label') || '',
                  title: b.getAttribute('title') || '',
                  cls: (b.className || '').toString().slice(0, 70),
                  disabled: b.disabled === true,
                  text: (b.textContent || '').trim().slice(0, 12)
                }))
                .filter(b => b.aria || b.title || b.text);
            })()
            """);

        // Enter through CDP. A null `text` field makes the protocol reject the call ("Class not
        // registered"), so the payload is built without the key at all.
        var keyDown = new Dictionary<string, object>
        {
            ["type"] = "keyDown",
            ["key"] = "Enter",
            ["code"] = "Enter",
            ["windowsVirtualKeyCode"] = 13,
            ["nativeVirtualKeyCode"] = 13,
        };
        var keyUp = new Dictionary<string, object>(keyDown) { ["type"] = "keyUp" };

        await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(keyDown));
        await Task.Delay(60);
        await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(keyUp));

        await Task.Delay(2000);
        Note($"after enter: {await ReadBoxAsync(web)}");

        // The gate shows up after a send on a fresh profile, not before typing.
        string gateAfter = await EvalAsync(web, """
            (() => {
              const text = document.body.innerText || '';
              if (!/year were you born/i.test(text)) return 'no-gate';

              const selects = [...document.querySelectorAll('select')].map(s => ({
                cls: (s.className || '').toString().slice(0, 40),
                value: s.value,
                options: [...s.options].slice(0, 4).map(o => (o.textContent || '').trim())
              }));

              return 'GATE ' + JSON.stringify(selects);
            })()
            """);
        Note($"gate after send: {gateAfter}");

        if (gateAfter != "no-gate")
        {
            string chosen = await EvalAsync(web, """
                (() => {
                  const oldestAllowed = new Date().getFullYear() - 18;
                  const select = document.querySelector('select');
                  if (!select) return 'no-select';

                  const yearOf = (o) => parseInt((o.textContent || '').trim(), 10);
                  const valid = [...select.options].filter(o => Number.isFinite(yearOf(o)) && yearOf(o) <= oldestAllowed);
                  const target = valid.find(o => yearOf(o) === 2000) || valid.sort((a, b) => yearOf(b) - yearOf(a))[0];
                  if (!target) return 'no-adult-option';

                  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value').set;
                  setter.call(select, target.value);
                  select.dispatchEvent(new Event('input', { bubbles: true }));
                  select.dispatchEvent(new Event('change', { bubbles: true }));
                  return 'year:' + (target.textContent || '').trim();
                })()
                """);
            Note($"year chosen: {chosen}");

            await Task.Delay(400);

            string clicked = await EvalAsync(web, """
                (() => {
                  const btn = [...document.querySelectorAll('button')]
                    .find(b => /^\s*continue\s*$/i.test((b.textContent || '').trim()));
                  if (btn) { btn.click(); return 'continued'; }
                  return 'no-button';
                })()
                """);
            Note($"continue: {clicked}");

            await Task.Delay(1500);

            // Retry the message, since the first Enter was swallowed by the gate.
            await EvalAsync(web, "(() => { const b = document.querySelector('textarea'); b.click(); b.focus(); return 'focused'; })()");
            await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText",
                JsonSerializer.Serialize(new { text = "Reply with exactly: PROBE-OK" }));
            await Task.Delay(400);

            var retryDown = new Dictionary<string, object>
            {
                ["type"] = "keyDown",
                ["key"] = "Enter",
                ["code"] = "Enter",
                ["windowsVirtualKeyCode"] = 13,
                ["nativeVirtualKeyCode"] = 13,
            };
            await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(retryDown));
            await Task.Delay(60);
            await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new Dictionary<string, object>(retryDown) { ["type"] = "keyUp" }));

            await Task.Delay(1500);
            Note($"after retry: {await ReadBoxAsync(web)}");
        }

        for (int i = 1; i <= 6; i++)
        {
            await Task.Delay(3000);
            Note($"t+{i * 3}s box={await ReadBoxAsync(web)}");
            await DumpAsync(web, $"answers_{i * 3}s", """
                (() => {
                  const sels = ['[data-role="assistant"]', '[class*="assistant"]', '[class*="markdown-body"]',
                                '[class*="qwen-markdown"]', '[class*="message-content"]', '.chat-message-assistant'];
                  const found = [];
                  for (const s of sels) {
                    const n = document.querySelectorAll(s).length;
                    if (n > 0) {
                      const last = document.querySelectorAll(s)[n - 1];
                      found.push(s + ' x' + n + ' len=' + ((last.innerText || '').length) + ' head=' + JSON.stringify((last.innerText || '').slice(0, 40)));
                    }
                  }
                  return found.length ? found : ['none'];
                })()
                """);
        }

        await DumpAsync(web, "bodyAfter", "document.body.innerText.slice(-400)");

        // The guest nudge appears after the turn, which is the only moment it can be tested.
        await TryNudgeAsync(web);
    }

    /// <summary>
    /// Measures whether the app's state accepts our text: the Send button is the tell.
    ///
    /// A filled DOM box with an inert Send button means the value never reached the app's state, so
    /// every submit path is a no-op. This compares the button before and after two different ways of
    /// announcing the text.
    /// </summary>
    private static async Task TrySubmitAsync(WebView2 web)
    {
        Note("--- submit");

        string Read = """
            (() => {
              const btn = document.querySelector('button[aria-label="Send"]');
              const box = document.querySelector('textarea');
              return JSON.stringify({
                disabled: btn ? btn.disabled === true : null,
                cls: btn ? (btn.className || '').toString().slice(0, 40) : '',
                value: box ? (box.value || '').length : -1
              });
            })()
            """;

        Note($"before typing: {await EvalAsync(web, Read)}");

        // 1. setter + plain Event('input')
        await EvalAsync(web, """
            (() => {
              const box = document.querySelector('textarea');
              box.focus();
              const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
              setter.call(box, '');
              setter.call(box, 'hello there');
              box.dispatchEvent(new Event('input', { bubbles: true }));
              return box.value;
            })()
            """);
        await Task.Delay(600);
        Note($"after plain input: {await EvalAsync(web, Read)}");

        // 2. setter + InputEvent carrying inputType and data, which is what a real edit sends
        await EvalAsync(web, """
            (() => {
              const box = document.querySelector('textarea');
              box.focus();
              const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
              setter.call(box, '');
              setter.call(box, 'hello there');
              box.dispatchEvent(new InputEvent('input', {
                bubbles: true,
                cancelable: false,
                inputType: 'insertText',
                data: 'hello there'
              }));
              return box.value;
            })()
            """);
        await Task.Delay(600);
        Note($"after InputEvent: {await EvalAsync(web, Read)}");

        // 3. type character by character through CDP, then look at the button
        await EvalAsync(web, """
            (() => {
              const box = document.querySelector('textarea');
              const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
              setter.call(box, '');
              box.dispatchEvent(new Event('input', { bubbles: true }));
              box.focus();
              return 'cleared';
            })()
            """);
        await Task.Delay(300);

        foreach (char ch in "hello there")
        {
            await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new
                {
                    type = "char",
                    text = ch.ToString(),
                    unmodifiedText = ch.ToString(),
                    key = ch.ToString(),
                }));
        }

        await Task.Delay(600);
        Note($"after per-char: {await EvalAsync(web, Read)}");

        await DumpAsync(web, "bodySubmit", "document.body.innerText.slice(0, 160)");
    }

    /// <summary>
    /// Checks the "Welcome to Qwen" nudge can be closed with the same selectors the engine uses.
    /// </summary>
    private static async Task TryNudgeAsync(WebView2 web)
    {
        Note("--- nudge");

        string before = await EvalAsync(web, """
            (() => {
              const text = document.body.innerText || '';
              const present = /Welcome to Qwen|Log in to unlock/i.test(text);
              const buttons = [...document.querySelectorAll('button')].map(b => ({
                aria: b.getAttribute('aria-label') || '',
                text: (b.textContent || '').trim().slice(0, 14)
              })).slice(0, 10);
              return JSON.stringify({ present, buttons });
            })()
            """);
        Note($"before: {before}");

        string closed = await EvalAsync(web, """
            (() => {
              const text = document.body.innerText || '';
              if (!/Welcome to Qwen|Log in to unlock/i.test(text)) return 'no-nudge';

              const dialog = document.querySelector('[role="dialog"]')
                || [...document.querySelectorAll('div')]
                     .find(el => /Welcome to Qwen|Log in to unlock/i.test(el.textContent || '')
                                 && (el.textContent || '').length < 400);

              const scope = dialog || document;
              const buttons = [...scope.querySelectorAll('button')];

              const byLabel = buttons.find(b => /close|dismiss/i.test(b.getAttribute('aria-label') || ''));
              if (byLabel) { byLabel.click(); return 'clicked-close-label'; }

              const iconOnly = buttons.find(b => (b.textContent || '').trim().length === 0);
              if (iconOnly) { iconOnly.click(); return 'clicked-icon-only'; }

              return 'no-close-button';
            })()
            """);
        Note($"close attempt: {closed}");

        await Task.Delay(800);

        string after = await EvalAsync(web, """
            (() => /Welcome to Qwen|Log in to unlock/i.test(document.body.innerText || '') ? 'still-open' : 'gone')()
            """);
        Note($"after close click: {after}");

        // The dialog's X has no aria-label, so the label search can hit a message action instead.
        // Escape is the browser's own exit and does not depend on the markup.
        if (after == "still-open")
        {
            var escDown = new Dictionary<string, object>
            {
                ["type"] = "keyDown",
                ["key"] = "Escape",
                ["code"] = "Escape",
                ["windowsVirtualKeyCode"] = 27,
                ["nativeVirtualKeyCode"] = 27,
            };
            await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(escDown));
            await Task.Delay(60);
            await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                JsonSerializer.Serialize(new Dictionary<string, object>(escDown) { ["type"] = "keyUp" }));

            await Task.Delay(800);

            string afterEsc = await EvalAsync(web, """
                (() => /Welcome to Qwen|Log in to unlock/i.test(document.body.innerText || '') ? 'still-open' : 'gone')()
                """);
            Note($"after escape: {afterEsc}");
        }
    }

    private static async Task DumpAsync(WebView2 web, string label, string script)
    {
        try
        {
            var value = await EvalAsync(web, script);
            Note($"[{label}] {value}");
        }
        catch (Exception ex)
        {
            Note($"[{label}] FAILED {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<string> EvalAsync(WebView2 web, string expression)
    {
        var core = web.CoreWebView2 ?? throw new InvalidOperationException("WebView2 not ready");
        string json = await core.CallDevToolsProtocolMethodAsync(
            "Runtime.evaluate",
            JsonSerializer.Serialize(new
            {
                expression,
                returnByValue = true,
                awaitPromise = true,
                userGesture = true,
            }));

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("result", out var result)) return json;
        if (result.TryGetProperty("value", out var value)) return value.ToString();
        if (result.TryGetProperty("description", out var description)) return description.ToString();
        return json;
    }

    private static void Note(string line)
    {
        Buffer.AppendLine($"{DateTime.Now:HH:mm:ss} {line}");
        Flush();
    }

    private static void Flush()
    {
        try { File.WriteAllText(LogPath, Buffer.ToString()); } catch (IOException) { }
    }
}
