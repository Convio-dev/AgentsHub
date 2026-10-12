using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using HermesHub.Helpers;
using HermesHub.Services;

namespace HermesHub;

public class PromptEnhancerForm : Form
{
    private class DropdownModelItem
    {
        public bool IsHeader { get; set; }
        public string DisplayText { get; set; } = string.Empty;
        public AiProvider? Provider { get; set; }
        public string? ModelName { get; set; }

        public override string ToString() => DisplayText;
    }

    private readonly Panel topBarPanel = new();
    private readonly Label titleLabel = new();
    private readonly Label modelLabel = new();
    private readonly ComboBox modelComboBox = new();
    private readonly Label styleLabel = new();
    private readonly ComboBox styleComboBox = new();
    private readonly Button openSettingsButton = new();

    private readonly SplitContainer mainSplitContainer = new();

    // Input Section
    private readonly Panel inputPanel = new();
    private readonly Label inputHeaderLabel = new();
    private readonly TextBox promptInputTextBox = new();
    private readonly Panel inputActionBar = new();
    private readonly Button enhanceButton = new();
    private readonly Button clearInputButton = new();
    private readonly Button pasteInputButton = new();
    private readonly Label inputCountLabel = new();

    // Output Section
    private readonly Panel outputPanel = new();
    private readonly Label outputHeaderLabel = new();
    private readonly TextBox promptOutputTextBox = new();
    private readonly Panel outputActionBar = new();
    private readonly Button copyOutputButton = new();
    private readonly Button sendToInputButton = new();
    private readonly Label outputCountLabel = new();
    private readonly Label statusMessageLabel = new();

    private CancellationTokenSource? _enhancementCts;

    public PromptEnhancerForm()
    {
        InitializeUi();
        ApplyTheme();
        PopulateModelsDropdown();
        AiConfigService.Instance.ConfigChanged += OnConfigChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AiConfigService.Instance.ConfigChanged -= OnConfigChanged;
            _enhancementCts?.Cancel();
            _enhancementCts?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnConfigChanged()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(OnConfigChanged));
            return;
        }
        PopulateModelsDropdown();
    }

    private void InitializeUi()
    {
        Text = "Agents Hub - Prompt Enhancer";
        Size = new Size(820, 680);
        MinimumSize = new Size(700, 540);
        StartPosition = FormStartPosition.CenterParent;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        // Top Bar
        topBarPanel.Dock = DockStyle.Top;
        topBarPanel.Height = 56;
        topBarPanel.Padding = new Padding(16, 10, 16, 10);

        titleLabel.Text = "✨ PROMPT ENHANCER";
        titleLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        titleLabel.AutoSize = true;
        titleLabel.Location = new Point(16, 16);

        modelLabel.Text = "Model:";
        modelLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        modelLabel.AutoSize = true;
        modelLabel.Location = new Point(200, 18);

        modelComboBox.Location = new Point(252, 14);
        modelComboBox.Size = new Size(260, 26);
        modelComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        modelComboBox.DrawMode = DrawMode.OwnerDrawFixed;
        modelComboBox.ItemHeight = 22;
        modelComboBox.Font = new Font("Segoe UI", 8.75F);
        modelComboBox.DrawItem += ModelComboBox_DrawItem;
        modelComboBox.SelectedIndexChanged += ModelComboBox_SelectedIndexChanged;

        styleLabel.Text = "Preset:";
        styleLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        styleLabel.AutoSize = true;
        styleLabel.Location = new Point(524, 18);

        styleComboBox.Location = new Point(576, 14);
        styleComboBox.Size = new Size(155, 26);
        styleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        styleComboBox.Font = new Font("Segoe UI", 8.5F);
        styleComboBox.Items.AddRange(new object[]
        {
            "General & Balanced",
            "Code & Architecture",
            "Direct & Concise",
            "Comprehensive & Structured"
        });
        styleComboBox.SelectedIndex = 0;

        openSettingsButton.Text = "⚙️ Providers";
        openSettingsButton.Location = new Point(740, 13);
        openSettingsButton.Size = new Size(95, 28);
        openSettingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openSettingsButton.FlatStyle = FlatStyle.Flat;
        openSettingsButton.Font = new Font("Segoe UI", 8F);
        openSettingsButton.Cursor = Cursors.Hand;
        openSettingsButton.Click += (_, _) =>
        {
            using var settings = new SettingsForm();
            settings.ShowDialog(this);
            PopulateModelsDropdown();
        };

        topBarPanel.Controls.Add(titleLabel);
        topBarPanel.Controls.Add(modelLabel);
        topBarPanel.Controls.Add(modelComboBox);
        topBarPanel.Controls.Add(styleLabel);
        topBarPanel.Controls.Add(styleComboBox);
        topBarPanel.Controls.Add(openSettingsButton);

        // Split Container (Top: Input, Bottom: Output)
        mainSplitContainer.Dock = DockStyle.Fill;
        mainSplitContainer.Orientation = Orientation.Horizontal;
        mainSplitContainer.SplitterDistance = 260;
        mainSplitContainer.SplitterWidth = 6;

        // Top Panel: User Prompt Input
        inputPanel.Dock = DockStyle.Fill;
        inputPanel.Padding = new Padding(16, 8, 16, 4);

        inputHeaderLabel.Text = "ORIGINAL RAW PROMPT (Ctrl+Enter to Enhance)";
        inputHeaderLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        inputHeaderLabel.Dock = DockStyle.Top;
        inputHeaderLabel.Height = 22;

        promptInputTextBox.Dock = DockStyle.Fill;
        promptInputTextBox.Multiline = true;
        promptInputTextBox.ScrollBars = ScrollBars.Vertical;
        promptInputTextBox.Font = new Font("Consolas", 9.75F);
        promptInputTextBox.PlaceholderText = "Type or paste your prompt here...\n(e.g., 'Build a C# winforms notification drawer' or 'Write python fast api endpoint')";
        promptInputTextBox.TextChanged += PromptInputTextBox_TextChanged;
        promptInputTextBox.KeyDown += PromptInputTextBox_KeyDown;

        inputActionBar.Dock = DockStyle.Bottom;
        inputActionBar.Height = 44;
        inputActionBar.Padding = new Padding(0, 6, 0, 0);

        enhanceButton.Text = "✨ Enhance Prompt";
        enhanceButton.Size = new Size(160, 32);
        enhanceButton.Location = new Point(0, 6);
        enhanceButton.FlatStyle = FlatStyle.Flat;
        enhanceButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        enhanceButton.Cursor = Cursors.Hand;
        enhanceButton.Click += async (_, _) => await StartEnhanceAsync();

        pasteInputButton.Text = "📋 Paste";
        pasteInputButton.Size = new Size(80, 32);
        pasteInputButton.Location = new Point(168, 6);
        pasteInputButton.FlatStyle = FlatStyle.Flat;
        pasteInputButton.Font = new Font("Segoe UI", 8.5F);
        pasteInputButton.Cursor = Cursors.Hand;
        pasteInputButton.Click += (_, _) =>
        {
            if (Clipboard.ContainsText())
            {
                promptInputTextBox.Text = Clipboard.GetText();
            }
        };

        clearInputButton.Text = "Clear";
        clearInputButton.Size = new Size(70, 32);
        clearInputButton.Location = new Point(254, 6);
        clearInputButton.FlatStyle = FlatStyle.Flat;
        clearInputButton.Font = new Font("Segoe UI", 8.5F);
        clearInputButton.Cursor = Cursors.Hand;
        clearInputButton.Click += (_, _) => promptInputTextBox.Clear();

        inputCountLabel.AutoSize = true;
        inputCountLabel.Location = new Point(334, 14);
        inputCountLabel.Font = new Font("Segoe UI", 8F);
        inputCountLabel.Text = "0 chars | 0 words";

        statusMessageLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        statusMessageLabel.AutoSize = true;
        statusMessageLabel.Location = new Point(540, 14);
        statusMessageLabel.Font = new Font("Segoe UI", 8F);
        statusMessageLabel.Text = "Ready";

        inputActionBar.Controls.Add(enhanceButton);
        inputActionBar.Controls.Add(pasteInputButton);
        inputActionBar.Controls.Add(clearInputButton);
        inputActionBar.Controls.Add(inputCountLabel);
        inputActionBar.Controls.Add(statusMessageLabel);

        inputPanel.Controls.Add(promptInputTextBox);
        inputPanel.Controls.Add(inputActionBar);
        inputPanel.Controls.Add(inputHeaderLabel);
        mainSplitContainer.Panel1.Controls.Add(inputPanel);

        // Bottom Panel: Enhanced Prompt Output
        outputPanel.Dock = DockStyle.Fill;
        outputPanel.Padding = new Padding(16, 4, 16, 12);

        outputHeaderLabel.Text = "ENHANCED OPTIMIZED PROMPT";
        outputHeaderLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        outputHeaderLabel.Dock = DockStyle.Top;
        outputHeaderLabel.Height = 22;

        promptOutputTextBox.Dock = DockStyle.Fill;
        promptOutputTextBox.Multiline = true;
        promptOutputTextBox.ReadOnly = true;
        promptOutputTextBox.ScrollBars = ScrollBars.Vertical;
        promptOutputTextBox.Font = new Font("Consolas", 9.75F);
        promptOutputTextBox.PlaceholderText = "Enhanced prompt result will appear here...";
        promptOutputTextBox.TextChanged += PromptOutputTextBox_TextChanged;

        outputActionBar.Dock = DockStyle.Bottom;
        outputActionBar.Height = 44;
        outputActionBar.Padding = new Padding(0, 6, 0, 0);

        copyOutputButton.Text = "📋 Copy Enhanced Prompt";
        copyOutputButton.Size = new Size(190, 32);
        copyOutputButton.Location = new Point(0, 6);
        copyOutputButton.FlatStyle = FlatStyle.Flat;
        copyOutputButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        copyOutputButton.Cursor = Cursors.Hand;
        copyOutputButton.Click += CopyOutputButton_Click;

        sendToInputButton.Text = "⬆️ Refine Further (Send to Input)";
        sendToInputButton.Size = new Size(200, 32);
        sendToInputButton.Location = new Point(198, 6);
        sendToInputButton.FlatStyle = FlatStyle.Flat;
        sendToInputButton.Font = new Font("Segoe UI", 8.5F);
        sendToInputButton.Cursor = Cursors.Hand;
        sendToInputButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(promptOutputTextBox.Text))
            {
                promptInputTextBox.Text = promptOutputTextBox.Text;
                promptInputTextBox.Focus();
                promptInputTextBox.SelectAll();
            }
        };

        outputCountLabel.AutoSize = true;
        outputCountLabel.Location = new Point(408, 14);
        outputCountLabel.Font = new Font("Segoe UI", 8F);
        outputCountLabel.Text = "0 chars | 0 words";

        outputActionBar.Controls.Add(copyOutputButton);
        outputActionBar.Controls.Add(sendToInputButton);
        outputActionBar.Controls.Add(outputCountLabel);

        outputPanel.Controls.Add(promptOutputTextBox);
        outputPanel.Controls.Add(outputActionBar);
        outputPanel.Controls.Add(outputHeaderLabel);
        mainSplitContainer.Panel2.Controls.Add(outputPanel);

        Controls.Add(mainSplitContainer);
        Controls.Add(topBarPanel);
    }

    private void PopulateModelsDropdown()
    {
        modelComboBox.Items.Clear();
        var config = AiConfigService.Instance.CurrentConfig;

        int selectedIndexToRestore = -1;
        int firstSelectableIndex = -1;

        var enabledProviders = config.Providers.Where(p => p.IsEnabled).ToList();
        if (enabledProviders.Count == 0)
        {
            modelComboBox.Items.Add(new DropdownModelItem
            {
                IsHeader = false,
                DisplayText = "(All providers disabled - click ⚙️ Providers to enable)",
                Provider = null,
                ModelName = null
            });
            modelComboBox.SelectedIndex = 0;
            return;
        }

        foreach (var prov in enabledProviders)
        {
            // Dropdown shows like:
            // --Provider
            // -Models
            modelComboBox.Items.Add(new DropdownModelItem
            {
                IsHeader = true,
                DisplayText = $"-- {prov.Name}",
                Provider = prov
            });

            if (prov.Models.Count == 0)
            {
                modelComboBox.Items.Add(new DropdownModelItem
                {
                    IsHeader = false,
                    DisplayText = "   - (No models configured)",
                    Provider = prov,
                    ModelName = null
                });
            }
            else
            {
                foreach (var m in prov.Models)
                {
                    var item = new DropdownModelItem
                    {
                        IsHeader = false,
                        DisplayText = $"  - {m}",
                        Provider = prov,
                        ModelName = m
                    };
                    int curIdx = modelComboBox.Items.Add(item);

                    if (firstSelectableIndex == -1)
                    {
                        firstSelectableIndex = curIdx;
                    }

                    if (string.Equals(config.SelectedProviderId, prov.Id, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(config.SelectedModel, m, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndexToRestore = curIdx;
                    }
                }
            }
        }

        if (selectedIndexToRestore >= 0)
        {
            modelComboBox.SelectedIndex = selectedIndexToRestore;
        }
        else if (firstSelectableIndex >= 0)
        {
            modelComboBox.SelectedIndex = firstSelectableIndex;
        }
        else if (modelComboBox.Items.Count > 0)
        {
            modelComboBox.SelectedIndex = 0;
        }
    }

    private void ModelComboBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= modelComboBox.Items.Count) return;

        var c = ThemeManager.Colors;
        if (modelComboBox.Items[e.Index] is not DropdownModelItem item) return;

        e.DrawBackground();

        if (item.IsHeader)
        {
            using var headerBg = new SolidBrush(Color.FromArgb(28, 30, 38));
            e.Graphics.FillRectangle(headerBg, e.Bounds);

            using var headerFont = new Font(e.Font ?? Font, FontStyle.Bold);
            using var headerBrush = new SolidBrush(c.AccentGreen);
            e.Graphics.DrawString(item.DisplayText, headerFont, headerBrush, e.Bounds.X + 4, e.Bounds.Y + 3);

            // Subtle divider line
            using var linePen = new Pen(Color.FromArgb(45, 48, 60));
            e.Graphics.DrawLine(linePen, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        }
        else
        {
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var textBrush = new SolidBrush(isSelected ? c.AccentGreenText : c.TextPrimary);
            using var itemFont = new Font("Segoe UI", 8.5F);
            e.Graphics.DrawString(item.DisplayText, itemFont, textBrush, e.Bounds.X + 10, e.Bounds.Y + 3);
        }

        e.DrawFocusRectangle();
    }

    private void ModelComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (modelComboBox.SelectedItem is DropdownModelItem item)
        {
            if (item.IsHeader)
            {
                // Auto-advance to the first model under this provider
                int nextIdx = modelComboBox.SelectedIndex + 1;
                if (nextIdx < modelComboBox.Items.Count &&
                    modelComboBox.Items[nextIdx] is DropdownModelItem nextItem &&
                    !nextItem.IsHeader)
                {
                    modelComboBox.SelectedIndex = nextIdx;
                    return;
                }
            }
            else if (item.Provider != null && !string.IsNullOrEmpty(item.ModelName))
            {
                var cfg = AiConfigService.Instance.CurrentConfig;
                cfg.SelectedProviderId = item.Provider.Id;
                cfg.SelectedModel = item.ModelName;
                AiConfigService.Instance.SaveConfig(cfg);
            }
        }
    }

    private void PromptInputTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            _ = StartEnhanceAsync();
        }
    }

    private void PromptInputTextBox_TextChanged(object? sender, EventArgs e)
    {
        var text = promptInputTextBox.Text;
        int chars = text.Length;
        int words = string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        inputCountLabel.Text = $"{chars} chars | {words} words";
    }

    private void PromptOutputTextBox_TextChanged(object? sender, EventArgs e)
    {
        var text = promptOutputTextBox.Text;
        int chars = text.Length;
        int words = string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        outputCountLabel.Text = $"{chars} chars | {words} words";
    }

    private async Task StartEnhanceAsync()
    {
        var rawPrompt = promptInputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawPrompt))
        {
            statusMessageLabel.Text = "Please enter a prompt to enhance.";
            statusMessageLabel.ForeColor = ThemeManager.Colors.WarningAmber;
            promptInputTextBox.Focus();
            return;
        }

        if (modelComboBox.SelectedItem is not DropdownModelItem selectedItem ||
            selectedItem.Provider == null ||
            string.IsNullOrWhiteSpace(selectedItem.ModelName))
        {
            statusMessageLabel.Text = "Please configure and select a valid AI model.";
            statusMessageLabel.ForeColor = ThemeManager.Colors.WarningAmber;
            return;
        }

        var provider = selectedItem.Provider;
        var model = selectedItem.ModelName;
        var style = styleComboBox.SelectedItem?.ToString() ?? "General & Balanced";

        enhanceButton.Enabled = false;
        statusMessageLabel.Text = $"Enhancing with {provider.Name} ({model})...";
        statusMessageLabel.ForeColor = ThemeManager.Colors.AccentGreen;
        promptOutputTextBox.Text = "Enhancing prompt in progress, please wait...";

        _enhancementCts?.Cancel();
        _enhancementCts = new CancellationTokenSource();

        var sw = Stopwatch.StartNew();
        try
        {
            var enhanced = await PromptEnhancerService.Instance.EnhancePromptAsync(
                provider,
                model,
                rawPrompt,
                style,
                customSystemPrompt: null,
                temperature: null,
                cancellationToken: _enhancementCts.Token
            );

            sw.Stop();
            promptOutputTextBox.Text = enhanced;
            statusMessageLabel.Text = $"✓ Completed in {sw.Elapsed.TotalSeconds:F1}s via {provider.Name}!";
            statusMessageLabel.ForeColor = ThemeManager.Colors.AccentGreen;
        }
        catch (OperationCanceledException)
        {
            statusMessageLabel.Text = "Enhancement cancelled.";
            statusMessageLabel.ForeColor = ThemeManager.Colors.TextMuted;
        }
        catch (Exception ex)
        {
            sw.Stop();
            promptOutputTextBox.Text = string.Empty;
            statusMessageLabel.Text = "Error: " + ex.Message;
            statusMessageLabel.ForeColor = ThemeManager.Colors.DangerRed;

            MessageBox.Show(
                this,
                $"Failed to enhance prompt:\n\n{ex.Message}\n\nEnsure API key and base URL are correct in Settings.",
                "Enhancement Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
        finally
        {
            enhanceButton.Enabled = true;
        }
    }

    private void CopyOutputButton_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(promptOutputTextBox.Text)) return;

        Clipboard.SetText(promptOutputTextBox.Text);
        copyOutputButton.Text = "✓ Copied to Clipboard!";
        copyOutputButton.ForeColor = ThemeManager.Colors.AccentGreen;

        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            copyOutputButton.Text = "📋 Copy Enhanced Prompt";
            copyOutputButton.ForeColor = ThemeManager.Colors.AccentGreenText;
        };
        timer.Start();
    }

    private void ApplyTheme()
    {
        var c = ThemeManager.Colors;
        BackColor = c.WindowBackground;
        ForeColor = c.TextPrimary;

        ThemeManager.ApplyDwmTitleBarTheme(Handle);

        topBarPanel.BackColor = c.HeaderBackground;
        titleLabel.ForeColor = c.AccentGreen;
        modelLabel.ForeColor = c.TextSecondary;
        styleLabel.ForeColor = c.TextSecondary;

        modelComboBox.BackColor = c.WellBackground;
        modelComboBox.ForeColor = c.TextPrimary;

        styleComboBox.BackColor = c.WellBackground;
        styleComboBox.ForeColor = c.TextPrimary;

        openSettingsButton.BackColor = c.ButtonBg;
        openSettingsButton.ForeColor = c.ButtonText;
        openSettingsButton.FlatAppearance.BorderColor = c.ButtonBorder;

        mainSplitContainer.BackColor = c.CardBorder;
        inputPanel.BackColor = c.WindowBackground;
        outputPanel.BackColor = c.WindowBackground;

        inputHeaderLabel.ForeColor = c.TextMuted;
        outputHeaderLabel.ForeColor = c.TextMuted;

        promptInputTextBox.BackColor = c.CardBackground;
        promptInputTextBox.ForeColor = c.TextPrimary;

        promptOutputTextBox.BackColor = c.CardBackground;
        promptOutputTextBox.ForeColor = c.TextPrimary;

        inputActionBar.BackColor = c.WindowBackground;
        outputActionBar.BackColor = c.WindowBackground;

        enhanceButton.BackColor = c.AccentGreen;
        enhanceButton.ForeColor = c.AccentGreenText;
        enhanceButton.FlatAppearance.BorderColor = c.AccentGreen;

        clearInputButton.BackColor = c.ButtonBg;
        clearInputButton.ForeColor = c.ButtonText;
        clearInputButton.FlatAppearance.BorderColor = c.ButtonBorder;

        pasteInputButton.BackColor = c.ButtonBg;
        pasteInputButton.ForeColor = c.ButtonText;
        pasteInputButton.FlatAppearance.BorderColor = c.ButtonBorder;

        copyOutputButton.BackColor = c.AccentGreen;
        copyOutputButton.ForeColor = c.AccentGreenText;
        copyOutputButton.FlatAppearance.BorderColor = c.AccentGreen;

        sendToInputButton.BackColor = c.ButtonBg;
        sendToInputButton.ForeColor = c.ButtonText;
        sendToInputButton.FlatAppearance.BorderColor = c.ButtonBorder;

        inputCountLabel.ForeColor = c.TextMuted;
        outputCountLabel.ForeColor = c.TextMuted;
        statusMessageLabel.ForeColor = c.TextSecondary;
    }
}
