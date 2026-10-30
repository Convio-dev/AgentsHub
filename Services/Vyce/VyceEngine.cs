using System.Text;
using System.Text.Json;

namespace AgentsHub.Services.Vyce;

/// <summary>One answer, and the session file it was read from.</summary>
internal sealed record VyceReply(string Text, string? SessionFile, string Model);

/// <summary>One row of the CLI's own model menu.</summary>
/// <param name="Available">False for the rows it lists under "Coming soon"; those cannot be picked.</param>
internal sealed record VyceModel(string Name, string Note, bool Available);

/// <summary>What the model menu offers, and which row it marks as current.</summary>
internal sealed record VyceModelList(IReadOnlyList<VyceModel> Models, string Current);

/// <summary>
/// Drives the Vyce Code CLI from a hidden console.
///
/// Vyce has no headless mode — it refuses piped input outright ("needs an interactive TTY") — so it
/// cannot be driven like `agy`. What it does have is a per-session JSON file under
/// <c>~/.vyce/sessions</c> holding the whole conversation, which means the answer can be read from
/// disk instead of scraped from an ANSI screen. That is the whole trick: a real console to talk to
/// it, and a file to hear it.
///
/// Nothing here is visible to the user: the console is created hidden, owns no taskbar entry, and
/// dies with the app.
/// </summary>
internal sealed class VyceEngine : IDisposable
{
    public static VyceEngine Instance { get; } = new();

    /// <summary>How long the TUI gets to draw itself before the first keystroke.</summary>
    private const int ColdStartDelayMs = 5000;

    /// <summary>How long a console that is already drawn gets before the next prompt.</summary>
    private const int WarmStartDelayMs = 1200;

    /// <summary>How often the session file is re-read while an answer is arriving.</summary>
    private const int PollMs = 250;

    /// <summary>Silence long enough to call the answer finished.</summary>
    private const int IdleMs = 2500;

    /// <summary>How long a final read is retried before an empty one is believed.</summary>
    private const int SettleMs = 4000;

    /// <summary>How long to wait for the TUI to finish the previous turn before typing.</summary>
    private const int ReadyWaitMs = 8000;

    /// <summary>When a turn with no content earns a screen dump in the log.</summary>
    private const int StallDumpMs = 10000;

    /// <summary>How long a prompt may go unanswered before the console is declared deaf.</summary>
    private const int AcceptTimeoutMs = 45000;

    /// <summary>Ceiling for one turn, so a stuck TUI cannot hold the session forever.</summary>
    private const int TurnTimeoutMs = 180000;

    /// <summary>
    /// What one read of a session file says about the turn that is running.
    ///
    /// The assistant count, not the text, tells a new answer apart: asking a model to repeat itself
    /// produces byte-identical text, and comparing text called that turn unfinished.
    /// </summary>
    private readonly record struct SessionSnapshot(int Messages, int Assistants, string LastAssistant, string Model);

    private static readonly SessionSnapshot EmptySession = new(0, 0, string.Empty, string.Empty);

    private readonly SemaphoreSlim _turn = new(1, 1);
    private VyceTty? _tty;
    private string? _root;
    private bool _disposed;

    /// <summary>The model last seen answering, switching, or in a session file. Cheap to ask for.</summary>
    private string _model = string.Empty;

    private readonly string _sessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vyce", "sessions");

    private readonly string _logPath = Path.Combine(Path.GetTempPath(), "agentshub-vyce.log");

    public bool IsRunning => _tty?.IsRunning == true;

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(_logPath, $"{DateTime.UtcNow:O}\t{message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // A log that cannot be written must never fail a turn.
        }
    }

    /// <summary>
    /// Sends one prompt and reads the answer out of the session file.
    ///
    /// The console is kept between turns on purpose: the TUI holds the conversation in memory, and
    /// re-spawning it would start a new chat every message.
    /// </summary>
    public async Task<VyceReply> SendAsync(
        string prompt,
        string root,
        IProgress<string>? progress,
        CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            EnsureConsole(root);

            string? before = NewestSession();
            var baseline = ReadSession(before);

            // The baseline is what the transcript already shows. A new turn is recognised by the
            // conversation growing, not by the answer being longer: the model repeating itself is a
            // normal answer, and comparing lengths called that a failure.
            string reportedText = baseline.LastAssistant;

            Log($"turn start, {prompt.Length} chars, session={before ?? "none"}, messages={baseline.Messages}");

            // The answer reaches the session file before the TUI has finished with the turn. A key
            // typed during that window is accepted into the box and never submitted, which stalls the
            // turn until it times out.
            WaitForIdleComposer();

            _tty!.TypeLine(prompt);

            // The screen is the confirmation that the CLI took the prompt. The session file is not:
            // a slow model writes nothing until its first token, and calling that a failure turned a
            // perfectly good turn into an error after 45 seconds.
            bool accepted = ConfirmPromptLanded(prompt);
            Log($"prompt on screen={accepted}");

            var started = DateTime.UtcNow;
            var lastChange = started;
            string? file = before;

            // The idle countdown only starts once this turn has written something. A session file that
            // already holds an answer would otherwise look finished 2.5 s in, and the turn would end on
            // the previous answer, or on an empty read of a half-written file.
            bool sawContent = false;
            bool dumpedStall = false;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var elapsed = DateTime.UtcNow - started;

                if (elapsed.TotalMilliseconds > TurnTimeoutMs)
                {
                    Log("turn timed out");
                    break;
                }

                await Task.Delay(PollMs, ct).ConfigureAwait(false);

                string? newest = NewestSession();

                // A first message in a fresh chat writes a new file. Its answer starts at zero, so the
                // baseline of the previous file must not carry over.
                if (newest is not null && !string.Equals(newest, file, StringComparison.OrdinalIgnoreCase))
                {
                    file = newest;
                    reportedText = string.Empty;
                    accepted = true;
                    lastChange = DateTime.UtcNow;

                    // A new chat starts from nothing, so the previous chat's counts and answer must not
                    // be carried into it: they would hide the new answer instead of recognising it.
                    baseline = EmptySession;
                }

                var current = ReadSession(file);
                string content = current.LastAssistant;

                if (content.Length > reportedText.Length && content.StartsWith(reportedText, StringComparison.Ordinal))
                {
                    // Report only the new part: the transcript appends, so sending the whole answer
                    // again would duplicate everything already on screen.
                    progress?.Report(content[reportedText.Length..]);
                    reportedText = content;
                    lastChange = DateTime.UtcNow;
                }
                else if (content.Length > 0 && !content.StartsWith(reportedText, StringComparison.Ordinal))
                {
                    // The file was rewritten because a new message replaced the last one. Re-send the
                    // whole answer rather than pretending nothing changed. An empty read is a partial
                    // write, not a rewrite, and must not reset the count.
                    progress?.Report(content);
                    reportedText = content;
                    lastChange = DateTime.UtcNow;
                }

                if (current.Messages > baseline.Messages || !string.Equals(file, before, StringComparison.OrdinalIgnoreCase))
                {
                    accepted = true;
                }

                if (!accepted && elapsed.TotalMilliseconds > AcceptTimeoutMs)
                {
                    LogScreen();
                    throw new InvalidOperationException(NotAccepted);
                }

                // A turn that produces nothing for this long is stuck rather than slow, and the screen
                // is the only way to know why after the fact.
                if (!sawContent && !dumpedStall && elapsed.TotalMilliseconds > StallDumpMs)
                {
                    dumpedStall = true;
                    Log("no content after " + (int)elapsed.TotalSeconds + "s");
                    LogScreen();
                }

                if (current.Assistants > baseline.Assistants) sawContent = true;

                if (sawContent && (DateTime.UtcNow - lastChange).TotalMilliseconds > IdleMs)
                {
                    break;
                }
            }

            // The answer is only this turn's if the transcript grew. A read taken while the CLI is
            // writing returns nothing, which is not a failure, so it is retried before giving up.
            var finished = ReadSession(file);
            var settleUntil = DateTime.UtcNow.AddMilliseconds(SettleMs);

            while (finished.LastAssistant.Length == 0 || IsBaseline(finished, baseline, file, before))
            {
                if (DateTime.UtcNow > settleUntil) break;

                await Task.Delay(PollMs, ct).ConfigureAwait(false);
                finished = ReadSession(file);
            }

            string answer = IsBaseline(finished, baseline, file, before) ? string.Empty : finished.LastAssistant;

            if (!accepted)
            {
                LogScreen();
                throw new InvalidOperationException(NotAccepted);
            }

            if (answer.Length == 0)
            {
                LogScreen();
                throw new InvalidOperationException(
                    "Vyce ran the turn but its session file never grew, so the answer could not be "
                    + "read. Run `vyce` in a terminal to see what it said.");
            }

            if (finished.Model.Length > 0) _model = finished.Model;

            Log($"turn done, {answer.Length} chars from {file ?? "no file"}");

            return new VyceReply(answer, file, finished.Model);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>Writes what the hidden console is showing, so a failure is diagnosable later.</summary>
    private void LogScreen()
    {
        try
        {
            string screen = _tty?.ReadScreen() ?? string.Empty;

            Log(screen.Length > 0 ? "screen:\n" + screen : "screen unreadable");
        }
        catch (Exception ex)
        {
            Log($"screen dump failed: {ex.GetType().Name}");
        }
    }

    private const string TurnInProgress =
        "Vyce is still answering. Its model menu can be opened once the turn finishes.";

    private const string NotAccepted =
        "Vyce did not accept the prompt. It is running, but its input box never received the keys, "
        + "so the console is either still starting or showing a screen that is not the chat.";

    /// <summary>
    /// Retries the prompt once if the TUI never showed it.
    ///
    /// A keystroke that arrives while the TUI repaints is simply gone, and nothing acknowledges one.
    /// The screen is the only confirmation available: the prompt is either in the input box or in the
    /// transcript. Repeating it blindly would send the message twice whenever the model is slow,
    /// which is why this looks instead of waiting.
    /// </summary>
    private bool ConfirmPromptLanded(string prompt)
    {
        string marker = prompt.Replace("\r", " ").Replace("\n", " ");
        if (marker.Length > 24) marker = marker[..24];

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            Thread.Sleep(attempt == 1 ? 900 : 800);

            if (PromptOnScreen(marker)) return true;

            if (attempt == 1)
            {
                Log("prompt not on screen, typing it again");
                _tty!.TypeLine(prompt);
            }
        }

        Log("prompt never appeared on screen");
        return false;
    }

    /// <summary>True when the file still holds the answer that was there before this turn.</summary>
    private static bool IsBaseline(
        SessionSnapshot current, SessionSnapshot baseline, string? file, string? before)
    {
        return string.Equals(file, before, StringComparison.OrdinalIgnoreCase)
            && current.Assistants <= baseline.Assistants;
    }

    /// <summary>
    /// Waits until the composer stops asking to queue, which is how the TUI shows a turn still busy.
    ///
    /// The placeholder is the only status the CLI exposes without a notification hook. If it never
    /// changes, the wait gives up and the turn is typed anyway: the screen being unreadable must not
    /// stop a turn that would otherwise work.
    ///
    ///            shortcut: readiness is recognised by the composer's own wording, so a reworded CLI just makes
    /// this wait time out; upgrade to an exit-status or hook signal if the TUI ever offers one.
    /// </summary>
    private void WaitForIdleComposer()
    {
        var until = DateTime.UtcNow.AddMilliseconds(ReadyWaitMs);

        while (DateTime.UtcNow < until)
        {
            string screen = _tty?.ReadScreen() ?? string.Empty;

            if (!screen.Contains("queue a follow-up", StringComparison.OrdinalIgnoreCase)) return;

            Thread.Sleep(150);
        }

        Log("composer still queueing after the wait");
    }

    /// <summary>
    /// Looks for the prompt in the rendered screen.
    ///
    /// The screen wraps at the terminal width, so a line-based comparison misses a prompt that broke
    /// across two rows. Removing whitespace from both sides is what makes this independent of where
    /// the wrap fell.
    /// </summary>
    private bool PromptOnScreen(string marker)
    {
        try
        {
            string screen = _tty?.ReadScreen() ?? string.Empty;

            return Squash(screen).Contains(Squash(marker), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Squash(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            if (!char.IsWhiteSpace(c)) builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The model that is answering, without touching the console.
    ///
    /// A session file records the model its turns ran on, and every turn and switch updates that
    /// memory, so the chip can name the model on mount instead of starting a console and driving a
    /// menu just to fill in a label.
    /// </summary>
    public string CurrentModel()
    {
        if (_model.Length > 0) return _model;

        _model = ReadSession(NewestSession()).Model;
        return _model;
    }

    /// <summary>
    /// The models the CLI offers, read from its own menu.
    ///
    /// There is no flag and no config file that lists them: the picker is the only source, so the
    /// list is scraped from it rather than copied into this app, where it would go stale.
    /// </summary>
    public async Task<VyceModelList> ListModelsAsync(string? root, CancellationToken ct = default)
    {
        // A second attempt covers the menu failing to draw while the TUI was still painting.
        // The console carries one thing at a time. Waiting out a whole answer would leave the menu
        // spinning for minutes, so say what is happening instead.
        if (_turn.CurrentCount == 0) throw new InvalidOperationException(TurnInProgress);

        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            VyceModelList list = new(Array.Empty<VyceModel>(), string.Empty);

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                await OpenModelPickerAsync(root, ct).ConfigureAwait(false);

                string screen = _tty!.ReadScreen();
                _tty.SendEscape();
                await Task.Delay(200, ct).ConfigureAwait(false);

                list = ParseModelPicker(screen);

                Log($"model list: {list.Models.Count} rows, current={list.Current} (attempt {attempt})");

                if (list.Models.Count > 0) break;

                await Task.Delay(700, ct).ConfigureAwait(false);
            }

            return list;
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Switches the CLI to <paramref name="model"/>.
    ///
    /// The menu filters as it is typed, so the model is named rather than counted: arrow presses
    /// would depend on the order of a list someone else controls.
    /// </summary>
    public async Task<bool> SetModelAsync(string model, string? root, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        if (_turn.CurrentCount == 0) throw new InvalidOperationException(TurnInProgress);

        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await OpenModelPickerAsync(root, ct).ConfigureAwait(false);

            _tty!.TypeText(model);
            await Task.Delay(700, ct).ConfigureAwait(false);

            var filtered = ParseModelPicker(_tty.ReadScreen());
            bool pickable = filtered.Models.Any(row =>
                row.Available && string.Equals(row.Name, model, StringComparison.OrdinalIgnoreCase));

            if (!pickable)
            {
                Log($"model '{model}' is not in the filtered menu, leaving the selection alone");
                _tty.SendEscape();
                return false;
            }

            _tty.PressEnter();
            await Task.Delay(900, ct).ConfigureAwait(false);

            // Enter closes the picker; a stray menu left open would swallow the next prompt.
            _tty.SendEscape();
            await Task.Delay(200, ct).ConfigureAwait(false);

            string footer = FooterModel(_tty.ReadScreen());
            bool changed = string.Equals(footer, model, StringComparison.OrdinalIgnoreCase);

            if (changed) _model = footer;

            Log($"model switch to '{model}' -> footer='{footer}', changed={changed}");

            return changed;
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Starts the console if it is not already running in <paramref name="root"/>.
    ///
    /// The console is kept between turns on purpose: the TUI holds the conversation in memory, and
    /// re-spawning it would start a new chat every message.
    /// </summary>
    private void EnsureConsole(string? root)
    {
        string target = string.IsNullOrWhiteSpace(root)
            ? _root ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : root;

        int readyDelay = _tty is not null && _tty.IsRunning
            && string.Equals(_root, target, StringComparison.OrdinalIgnoreCase)
                ? WarmStartDelayMs
                : ColdStartDelayMs;

        if (_tty is not null && _tty.IsRunning
            && string.Equals(_root, target, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Shutdown();

        _tty = new VyceTty(_logPath);
        _tty.Start(target, readyDelay);
        _root = target;

        StartFreshChat();
    }

    /// <summary>
    /// Returns the CLI to its home screen, which is a new chat.
    ///
    /// It restores the newest session on launch, and a restored chat is read-only to us: the CLI
    /// answers on screen but never writes the turn to its session file, so the answer would be
    /// invisible to this app. A fresh chat does write it. The app already owns the transcript, so a
    /// new CLI chat per console is also the right shape.
    /// </summary>
    private void StartFreshChat()
    {
        try
        {
            _tty!.PressControl('p');
            Thread.Sleep(450);

            _tty.TypeText("home");
            Thread.Sleep(450);

            _tty.PressEnter();
            Thread.Sleep(900);

            Log("started a fresh chat");
        }
        catch (Exception ex)
        {
            // An older CLI without /home still answers; the turn will say so if it does not.
            Log($"fresh chat failed: {ex.GetType().Name}");
        }
    }

    /// <summary>ctrl+p opens the command palette; its /models entry opens the model menu.</summary>
    private async Task OpenModelPickerAsync(string? root, CancellationToken ct)
    {
        EnsureConsole(root);

        _tty!.PressControl('p');
        await Task.Delay(450, ct).ConfigureAwait(false);

        _tty.TypeText("models");
        await Task.Delay(450, ct).ConfigureAwait(false);

        _tty.PressEnter();
        await Task.Delay(900, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the rows of the model menu out of its screen.
    ///
    /// The menu draws a frame in block glyphs around the terminal, so each line is trimmed of those
    /// first. A row is a leading bullet (selected) or circle (coming soon) and a bare model id; the
    /// rest of the line is the provider's own note.
    /// </summary>
    internal static VyceModelList ParseModelPicker(string screen)
    {
        var models = new List<VyceModel>();
        string current = string.Empty;
        bool comingSoon = false;

        foreach (string rawLine in screen.Split('\n'))
        {
            string line = rawLine.Trim(' ', '\u2580', '\u2584', '\u2588').TrimEnd();

            if (line.Length == 0) continue;

            if (line.StartsWith("Coming soon", StringComparison.OrdinalIgnoreCase))
            {
                comingSoon = true;
                continue;
            }

            // Headings and the filter box, not rows.
            if (line.StartsWith("Select model", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("type to search", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("type to filter", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool selected = line.StartsWith('\u2022') || line.StartsWith('\u25cf');
            bool disabled = line.StartsWith('\u25cb') || line.StartsWith('\u25cc');

            if (selected || disabled)
            {
                line = line[1..].TrimStart();
            }
            else if (line.StartsWith('>') || line.StartsWith('\u203a'))
            {
                continue;
            }

            int space = line.IndexOf(' ');
            string name = space < 0 ? line : line[..space];

            if (!LooksLikeModelId(name)) continue;

            bool marked = line.Contains('\u2713');
            // The menu pads its columns with runs of spaces; the chip shows this line under the id,
            // so the gaps are collapsed rather than carried into the UI.
            string note = string.Join(
                ' ',
                (space < 0 ? string.Empty : line[(space + 1)..])
                    .Replace("\u2713", string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries));

            models.Add(new VyceModel(name, note, !disabled && !comingSoon));

            if (marked || (selected && current.Length == 0)) current = name;
        }

        return new VyceModelList(models, current);
    }

    /// <summary>A model id: lowercase letters, digits and dots or dashes, nothing else.</summary>
    private static bool LooksLikeModelId(string value)
    {
        if (value.Length < 3 || value.Length > 60) return false;

        // The screen's own version stamp ("v0.1.5") has a model id's shape and sits in the same
        // column, so it is excluded explicitly.
        if (value[0] == 'v' && value.AsSpan(1).Contains('.')
            && value[1..].All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            return false;
        }

        foreach (char character in value)
        {
            bool allowed = char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '.' or '-' or '_';

            if (!allowed) return false;
        }

        return char.IsAsciiLetterLower(value[0]);
    }

    /// <summary>The model the chat footer reports, e.g. the second half of "Build · glm-5.3-flash".</summary>
    private static string FooterModel(string screen)
    {
        foreach (string rawLine in screen.Split('\n'))
        {
            string line = rawLine.Trim(' ', '\u2580', '\u2584', '\u2588');
            int dot = line.IndexOf("\u00b7", StringComparison.Ordinal);

            if (dot < 0) continue;

            string head = line[..dot].Trim();

            if (!head.Equals("Build", StringComparison.OrdinalIgnoreCase)
                && !head.Equals("Plan", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string tail = line[(dot + 1)..].Trim();
            int next = tail.IndexOf(" \u00b7 ", StringComparison.Ordinal);

            return next < 0 ? tail : tail[..next].Trim();
        }

        return string.Empty;
    }

    /// <summary>Esc cancels a streaming response, per the CLI's own key list.</summary>
    public void Stop()
    {
        try
        {
            _tty?.SendEscape();
            Log("stop sent");
        }
        catch (Exception ex)
        {
            Log($"stop failed: {ex.GetType().Name}");
        }
    }

    /// <summary>The most recently written session file, which is the conversation being driven.</summary>
    private string? NewestSession()
    {
        try
        {
            if (!Directory.Exists(_sessionsDir)) return null;

            return Directory.GetFiles(_sessionsDir, "*.json")
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .FirstOrDefault()?.FullName;
        }
        catch (Exception ex)
        {
            Log($"session scan failed: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>
    /// One read of a session file: how many messages it holds and what the last answer says.
    ///
    /// The TUI writes the file while it is streaming, so a read can land on a partial document.
    /// That is expected, not an error: the next poll sees the finished one.
    /// </summary>
    private SessionSnapshot ReadSession(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return EmptySession;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);

            string model = doc.RootElement.TryGetProperty("model", out var modelElement)
                && modelElement.ValueKind == JsonValueKind.String
                    ? modelElement.GetString() ?? string.Empty
                    : string.Empty;

            if (!doc.RootElement.TryGetProperty("conversation", out var conversation)
                || conversation.ValueKind != JsonValueKind.Array)
            {
                return new SessionSnapshot(0, 0, string.Empty, model);
            }

            int messages = 0;
            int assistants = 0;
            string last = string.Empty;

            foreach (var entry in conversation.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                messages++;

                if (!entry.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String) continue;
                if (!string.Equals(role.GetString(), "assistant", StringComparison.OrdinalIgnoreCase)) continue;

                assistants++;
                last = ContentOf(entry);
            }

            return new SessionSnapshot(messages, assistants, last, model);
        }
        catch (JsonException ex)
        {
            Log($"session read: bad json ({ex.Message.Split('.')[0]})");
            return EmptySession;
        }
        catch (IOException ex)
        {
            Log($"session read: {ex.GetType().Name} ({ex.Message.Split('.')[0]})");
            return EmptySession;
        }
    }

    /// <summary>Content may be a plain string or an array of parts; both shapes are handled.</summary>
    private static string ContentOf(JsonElement entry)
    {
        if (!entry.TryGetProperty("content", out var content)) return string.Empty;

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind != JsonValueKind.Array) return string.Empty;

        var builder = new StringBuilder();

        foreach (var part in content.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                builder.Append(part.GetString());
            }
            else if (part.ValueKind == JsonValueKind.Object
                     && part.TryGetProperty("text", out var text)
                     && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.ToString();
    }

    private void Shutdown()
    {
        _tty?.Dispose();
        _tty = null;
        _root = null;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Shutdown();
        _turn.Dispose();
    }
}
