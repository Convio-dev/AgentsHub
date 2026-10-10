using System.Drawing;
using System.IO;
using HermesHub.Helpers;
using HermesHub.Services;

namespace HermesHub;

public class SettingsForm : Form
{
    private readonly Panel topTabPanel = new();
    private readonly Button tabToolsButton = new();
    private readonly Button tabProvidersButton = new();

    private readonly Panel contentContainer = new();
    private readonly Panel toolsPanel = new();
    private readonly Panel providersPanel = new();

    private readonly Panel bottomPanel = new();
    private readonly Panel bottomActionPanel = new();
    private readonly Button saveButton = new();
    private readonly Button cancelButton = new();
    private readonly Label statusLabel = new();

    // Tools Tab Controls
    private readonly Dictionary<string, TextBox> _toolTextBoxes = new(StringComparer.OrdinalIgnoreCase);

    // Providers Tab Controls
    private readonly ListBox providersListBox = new();
    private readonly Button addProviderButton = new();
    private readonly Button removeProviderButton = new();

    private readonly TextBox providerNameTextBox = new();
    private readonly CheckBox providerEnabledCheckBox = new();
    private readonly TextBox providerUrlTextBox = new();
    private readonly TextBox providerKeyTextBox = new();
    private readonly Button toggleKeyVisibilityButton = new();
    private bool _isKeyVisible;

    private readonly ListBox modelsListBox = new();
    private readonly TextBox newModelTextBox = new();
    private readonly Button addModelButton = new();
    private readonly Button removeModelButton = new();
    private readonly Button fetchModelsButton = new();
    private readonly Label fetchStatusLabel = new();

    // In-card provider actions
    private readonly Button saveCurrentProviderButton = new();
    private readonly Label providerSaveStatusLabel = new();

    // In-memory working copy
    private readonly AiConfig _workingConfig;
    private AiProvider? _selectedProvider;
    private bool _isUpdatingProviderUi;

    public SettingsForm()
    {
        // Clone current config
        var current = AiConfigService.Instance.CurrentConfig;
        _workingConfig = new AiConfig
        {
            SelectedProviderId = current.SelectedProviderId,
            SelectedModel = current.SelectedModel,
            ToolTargets = new Dictionary<string, string>(current.ToolTargets, StringComparer.OrdinalIgnoreCase),
            Providers = current.Providers.Select(p => new AiProvider
            {
                Id = p.Id,
                Name = p.Name,
                BaseUrl = p.BaseUrl,
                ApiKey = p.ApiKey,
                IsEnabled = p.IsEnabled,
                Models = new List<string>(p.Models)
            }).ToList()
        };

        InitializeUi();
        ApplyTheme();
        SwitchTab(false); // Default to providers tab as requested
        LoadDataToUi();
    }

    private void InitializeUi()
    {
        Text = "Agents Hub - Settings & AI Dev Tools";
        Size = new Size(760, 680);
        MinimumSize = new Size(720, 600);
        StartPosition = FormStartPosition.CenterParent;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        // Top Tabs
        topTabPanel.Dock = DockStyle.Top;
        topTabPanel.Height = 46;
        topTabPanel.Padding = new Padding(14, 8, 14, 6);

        tabToolsButton.Text = "🛠️ AI Dev Tools Targets";
        tabToolsButton.Size = new Size(190, 32);
        tabToolsButton.Location = new Point(14, 7);
        tabToolsButton.FlatStyle = FlatStyle.Flat;
        tabToolsButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        tabToolsButton.Cursor = Cursors.Hand;
        tabToolsButton.UseMnemonic = false;
        tabToolsButton.Click += (_, _) => SwitchTab(true);

        tabProvidersButton.Text = "🤖 OpenAI Providers & Models";
        tabProvidersButton.Size = new Size(220, 32);
        tabProvidersButton.Location = new Point(tabToolsButton.Right + 8, 7);
        tabProvidersButton.FlatStyle = FlatStyle.Flat;
        tabProvidersButton.Font = new Font("Segoe UI", 8.75F, FontStyle.Bold);
        tabProvidersButton.Cursor = Cursors.Hand;
        tabProvidersButton.UseMnemonic = false;
        tabProvidersButton.Click += (_, _) => SwitchTab(false);

        topTabPanel.Controls.Add(tabToolsButton);
        topTabPanel.Controls.Add(tabProvidersButton);

        // Bottom Bar (Fixed Height, edge-docked)
        bottomPanel.Dock = DockStyle.Bottom;
        bottomPanel.Height = 52;
        bottomPanel.Padding = new Padding(16, 8, 16, 8);

        statusLabel.AutoSize = true;
        statusLabel.Location = new Point(16, 17);
        statusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

        bottomActionPanel.Dock = DockStyle.Right;
        bottomActionPanel.Width = 260;
        bottomActionPanel.Height = 36;

        saveButton.Text = "💾 Save All Settings";
        saveButton.Size = new Size(150, 34);
        saveButton.Dock = DockStyle.Right;
        saveButton.FlatStyle = FlatStyle.Flat;
        saveButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        saveButton.Cursor = Cursors.Hand;
        saveButton.Click += SaveButton_Click;

        var buttonSpacer = new Panel { Dock = DockStyle.Right, Width = 8 };

        cancelButton.Text = "Close";
        cancelButton.Size = new Size(85, 34);
        cancelButton.Dock = DockStyle.Right;
        cancelButton.FlatStyle = FlatStyle.Flat;
        cancelButton.Font = new Font("Segoe UI", 8.75F);
        cancelButton.Cursor = Cursors.Hand;
        cancelButton.Click += (_, _) => Close();

        bottomActionPanel.Controls.Add(cancelButton);
        bottomActionPanel.Controls.Add(buttonSpacer);
        bottomActionPanel.Controls.Add(saveButton);

        bottomPanel.Controls.Add(statusLabel);
        bottomPanel.Controls.Add(bottomActionPanel);

        // Content Container
        contentContainer.Dock = DockStyle.Fill;
        contentContainer.Controls.Add(toolsPanel);
        contentContainer.Controls.Add(providersPanel);

        // Build Tools Panel
        BuildToolsPanel();

        // Build Providers Panel
        BuildProvidersPanel();

        // Dock layout: add edges first, then fill, and force bring to front
        Controls.Add(contentContainer);
        Controls.Add(bottomPanel);
        Controls.Add(topTabPanel);

        topTabPanel.BringToFront();
        bottomPanel.BringToFront();
        contentContainer.SendToBack();
    }

    private void BuildToolsPanel()
    {
        toolsPanel.Dock = DockStyle.Fill;
        toolsPanel.AutoScroll = true;
        toolsPanel.Padding = new Padding(16);

        var toolNames = new[]
        {
            ("FreeBuff", "⚡ FreeBuff", "Main executable for FreeBuff Desktop"),
            ("Cline", "🤖 Cline", "Executable path for Cline App"),
            ("DeepSeek", "🧠 DeepSeek", "DeepSeek Harness executable path"),
            ("OpenCode", "💻 OpenCode", "OpenCode desktop client executable"),
            ("Antigravity-ide", "🪐 Antigravity-ide", "Path to Antigravity IDE.exe"),
            ("Claude", "✳️ Claude", "Executable path or shell:AppsFolder package ID"),
            ("ChatGPT", "💬 ChatGPT", "Executable path or shell:AppsFolder package ID")
        };

        var headerLabel = new Label
        {
            Text = "Configure launch targets and paths for AI Dev Tools buttons:",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 12)
        };
        toolsPanel.Controls.Add(headerLabel);

        int currentY = 40;
        foreach (var (key, title, desc) in toolNames)
        {
            var card = new Panel
            {
                Location = new Point(16, currentY),
                Width = 700,
                Height = 64,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            var nameLbl = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 8.75F, FontStyle.Bold),
                Location = new Point(6, 6),
                AutoSize = true
            };

            var descLbl = new Label
            {
                Text = desc,
                Font = new Font("Segoe UI", 7.5F),
                Location = new Point(140, 7),
                AutoSize = true
            };

            var txt = new TextBox
            {
                Location = new Point(6, 28),
                Width = 530,
                Font = new Font("Segoe UI", 8.5F),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            _toolTextBoxes[key] = txt;

            var browseBtn = new Button
            {
                Text = "Browse...",
                Location = new Point(542, 27),
                Size = new Size(74, 25),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            browseBtn.Click += (_, _) =>
            {
                using var ofd = new OpenFileDialog
                {
                    Title = $"Select target for {title}",
                    Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
                    InitialDirectory = Directory.Exists(Path.GetDirectoryName(txt.Text)) ? Path.GetDirectoryName(txt.Text) : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                };
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    txt.Text = ofd.FileName;
                }
            };

            var resetBtn = new Button
            {
                Text = "Reset",
                Location = new Point(622, 27),
                Size = new Size(64, 25),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8F),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            resetBtn.Click += (_, _) =>
            {
                var defaults = AiConfigService.Instance.GetDefaultToolTargets();
                if (defaults.TryGetValue(key, out var defVal))
                {
                    txt.Text = defVal;
                }
            };

            card.Controls.Add(nameLbl);
            card.Controls.Add(descLbl);
            card.Controls.Add(txt);
            card.Controls.Add(browseBtn);
            card.Controls.Add(resetBtn);

            toolsPanel.Controls.Add(card);
            currentY += 70;
        }
    }

    private void BuildProvidersPanel()
    {
        providersPanel.Dock = DockStyle.Fill;
        providersPanel.Padding = new Padding(16, 12, 16, 12);

        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 220,
            FixedPanel = FixedPanel.Panel1
        };

        // Left Panel: Providers List
        var provListHeader = new Label
        {
            Text = "AI Providers",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Dock = DockStyle.Top,
            Height = 26
        };

        providersListBox.Dock = DockStyle.Fill;
        providersListBox.Font = new Font("Segoe UI", 8.75F);
        providersListBox.BorderStyle = BorderStyle.FixedSingle;
        providersListBox.DrawMode = DrawMode.OwnerDrawFixed;
        providersListBox.ItemHeight = 28;
        providersListBox.DrawItem += ProvidersListBox_DrawItem;
        providersListBox.SelectedIndexChanged += ProvidersListBox_SelectedIndexChanged;

        var provBtnsPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            Padding = new Padding(0, 6, 0, 0)
        };

        addProviderButton.Text = "+ Add Provider";
        addProviderButton.Size = new Size(115, 28);
        addProviderButton.Location = new Point(0, 4);
        addProviderButton.FlatStyle = FlatStyle.Flat;
        addProviderButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        addProviderButton.Cursor = Cursors.Hand;
        addProviderButton.Click += AddProviderButton_Click;

        removeProviderButton.Text = "🗑 Delete";
        removeProviderButton.Size = new Size(95, 28);
        removeProviderButton.Location = new Point(122, 4);
        removeProviderButton.FlatStyle = FlatStyle.Flat;
        removeProviderButton.Font = new Font("Segoe UI", 8.5F);
        removeProviderButton.Cursor = Cursors.Hand;
        removeProviderButton.Click += RemoveProviderButton_Click;

        provBtnsPanel.Controls.Add(addProviderButton);
        provBtnsPanel.Controls.Add(removeProviderButton);

        splitContainer.Panel1.Controls.Add(providersListBox);
        splitContainer.Panel1.Controls.Add(provBtnsPanel);
        splitContainer.Panel1.Controls.Add(provListHeader);

        // Right Panel: Provider Configuration Details
        var detailsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 0, 0, 0),
            AutoScroll = true
        };

        // Provider Name & Enable Toggle
        var nameLabel = new Label
        {
            Text = "Provider Name:",
            Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            Location = new Point(14, 4),
            AutoSize = true
        };

        providerNameTextBox.Location = new Point(14, 24);
        providerNameTextBox.Size = new Size(270, 24);
        providerNameTextBox.Font = new Font("Segoe UI", 9F);
        providerNameTextBox.TextChanged += (_, _) =>
        {
            if (!_isUpdatingProviderUi && _selectedProvider != null)
            {
                _selectedProvider.Name = providerNameTextBox.Text;
                providersListBox.Invalidate();
            }
        };

        providerEnabledCheckBox.Text = "✓ Enabled (Active in Prompt Enhancer)";
        providerEnabledCheckBox.Location = new Point(294, 23);
        providerEnabledCheckBox.Size = new Size(230, 26);
        providerEnabledCheckBox.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        providerEnabledCheckBox.Cursor = Cursors.Hand;
        providerEnabledCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_isUpdatingProviderUi && _selectedProvider != null)
            {
                _selectedProvider.IsEnabled = providerEnabledCheckBox.Checked;
                providersListBox.Invalidate();
            }
        };

        // Base URL
        var urlLabel = new Label
        {
            Text = "Base URL (OpenAI-compatible link):",
            Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            Location = new Point(14, 56),
            AutoSize = true
        };

        providerUrlTextBox.Location = new Point(14, 76);
        providerUrlTextBox.Size = new Size(480, 24);
        providerUrlTextBox.Font = new Font("Segoe UI", 9F);
        providerUrlTextBox.TextChanged += (_, _) =>
        {
            if (!_isUpdatingProviderUi && _selectedProvider != null)
            {
                _selectedProvider.BaseUrl = providerUrlTextBox.Text;
            }
        };

        // Quick Preset Buttons
        var presetsPanel = new FlowLayoutPanel
        {
            Location = new Point(14, 104),
            Size = new Size(480, 28),
            FlowDirection = FlowDirection.LeftToRight
        };

        var presetLbl = new Label { Text = "Presets:", Font = new Font("Segoe UI", 7.5F), AutoSize = true, Padding = new Padding(0, 4, 4, 0) };
        presetsPanel.Controls.Add(presetLbl);

        var presets = new (string name, string url, string[] models)[]
        {
            ("OpenRouter", "https://openrouter.ai/api/v1", new[] { "anthropic/claude-3.5-sonnet", "deepseek/deepseek-chat", "openai/gpt-4o-mini" }),
            ("OpenAI", "https://api.openai.com/v1", new[] { "gpt-4o", "gpt-4o-mini", "o3-mini" }),
            ("Groq", "https://api.groq.com/openai/v1", new[] { "llama-3.3-70b-versatile", "deepseek-r1-distill-llama-70b" }),
            ("DeepSeek", "https://api.deepseek.com/v1", new[] { "deepseek-chat", "deepseek-reasoner" }),
            ("Ollama", "http://localhost:11434/v1", new[] { "llama3.2", "qwen2.5-coder" })
        };

        foreach (var p in presets)
        {
            var btn = new Button
            {
                Text = p.name,
                Size = new Size(72, 23),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.5F),
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 0, 2, 0)
            };
            btn.Click += (_, _) =>
            {
                if (_selectedProvider != null)
                {
                    providerNameTextBox.Text = p.name;
                    providerUrlTextBox.Text = p.url;
                    _selectedProvider.Name = p.name;
                    _selectedProvider.BaseUrl = p.url;
                    foreach (var m in p.models)
                    {
                        if (!_selectedProvider.Models.Contains(m))
                        {
                            _selectedProvider.Models.Add(m);
                        }
                    }
                    RefreshModelsListUi();
                    providersListBox.Invalidate();
                }
            };
            presetsPanel.Controls.Add(btn);
        }

        // API Key
        var keyLabel = new Label
        {
            Text = "API Key (optional for local Ollama):",
            Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            Location = new Point(14, 136),
            AutoSize = true
        };

        providerKeyTextBox.Location = new Point(14, 156);
        providerKeyTextBox.Size = new Size(440, 24);
        providerKeyTextBox.Font = new Font("Segoe UI", 9F);
        providerKeyTextBox.UseSystemPasswordChar = true;
        providerKeyTextBox.TextChanged += (_, _) =>
        {
            if (!_isUpdatingProviderUi && _selectedProvider != null)
            {
                _selectedProvider.ApiKey = providerKeyTextBox.Text;
            }
        };

        toggleKeyVisibilityButton.Text = "👁";
        toggleKeyVisibilityButton.Location = new Point(458, 155);
        toggleKeyVisibilityButton.Size = new Size(36, 25);
        toggleKeyVisibilityButton.FlatStyle = FlatStyle.Flat;
        toggleKeyVisibilityButton.Font = new Font("Segoe UI", 8F);
        toggleKeyVisibilityButton.Cursor = Cursors.Hand;
        toggleKeyVisibilityButton.Click += (_, _) =>
        {
            _isKeyVisible = !_isKeyVisible;
            providerKeyTextBox.UseSystemPasswordChar = !_isKeyVisible;
        };

        // Models Section
        var modelsHeaderLabel = new Label
        {
            Text = "Configured Models (shown in Prompt Enhancer dropdown):",
            Font = new Font("Segoe UI", 8.25F, FontStyle.Bold),
            Location = new Point(14, 188),
            AutoSize = true
        };

        modelsListBox.Location = new Point(14, 208);
        modelsListBox.Size = new Size(480, 96);
        modelsListBox.Font = new Font("Segoe UI", 8.5F);
        modelsListBox.BorderStyle = BorderStyle.FixedSingle;

        newModelTextBox.Location = new Point(14, 310);
        newModelTextBox.Size = new Size(305, 24);
        newModelTextBox.Font = new Font("Segoe UI", 8.5F);
        newModelTextBox.PlaceholderText = "e.g. gpt-4o or claude-3-5-sonnet";
        newModelTextBox.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AddModelButton_Click(s, EventArgs.Empty);
            }
        };

        addModelButton.Text = "+ Add Model";
        addModelButton.Location = new Point(325, 309);
        addModelButton.Size = new Size(95, 26);
        addModelButton.FlatStyle = FlatStyle.Flat;
        addModelButton.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        addModelButton.Cursor = Cursors.Hand;
        addModelButton.Click += AddModelButton_Click;

        removeModelButton.Text = "Remove";
        removeModelButton.Location = new Point(426, 309);
        removeModelButton.Size = new Size(68, 26);
        removeModelButton.FlatStyle = FlatStyle.Flat;
        removeModelButton.Font = new Font("Segoe UI", 8F);
        removeModelButton.Cursor = Cursors.Hand;
        removeModelButton.Click += RemoveModelButton_Click;

        fetchModelsButton.Text = "⚡ Fetch Available Models from API";
        fetchModelsButton.Location = new Point(14, 342);
        fetchModelsButton.Size = new Size(240, 26);
        fetchModelsButton.FlatStyle = FlatStyle.Flat;
        fetchModelsButton.Font = new Font("Segoe UI", 8F);
        fetchModelsButton.Cursor = Cursors.Hand;
        fetchModelsButton.Click += FetchModelsButton_Click;

        fetchStatusLabel.Location = new Point(260, 346);
        fetchStatusLabel.AutoSize = true;
        fetchStatusLabel.Font = new Font("Segoe UI", 7.5F);

        // In-Card Save Provider Button (Direct inline save)
        var inlineActionPanel = new Panel
        {
            Location = new Point(14, 380),
            Size = new Size(480, 42),
            Padding = new Padding(0, 4, 0, 4)
        };

        saveCurrentProviderButton.Text = "💾 Save Provider";
        saveCurrentProviderButton.Location = new Point(0, 4);
        saveCurrentProviderButton.Size = new Size(140, 30);
        saveCurrentProviderButton.FlatStyle = FlatStyle.Flat;
        saveCurrentProviderButton.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        saveCurrentProviderButton.Cursor = Cursors.Hand;
        saveCurrentProviderButton.Click += SaveCurrentProviderButton_Click;

        providerSaveStatusLabel.Location = new Point(148, 10);
        providerSaveStatusLabel.AutoSize = true;
        providerSaveStatusLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);

        inlineActionPanel.Controls.Add(saveCurrentProviderButton);
        inlineActionPanel.Controls.Add(providerSaveStatusLabel);

        detailsPanel.Controls.Add(nameLabel);
        detailsPanel.Controls.Add(providerNameTextBox);
        detailsPanel.Controls.Add(providerEnabledCheckBox);
        detailsPanel.Controls.Add(urlLabel);
        detailsPanel.Controls.Add(providerUrlTextBox);
        detailsPanel.Controls.Add(presetsPanel);
        detailsPanel.Controls.Add(keyLabel);
        detailsPanel.Controls.Add(providerKeyTextBox);
        detailsPanel.Controls.Add(toggleKeyVisibilityButton);
        detailsPanel.Controls.Add(modelsHeaderLabel);
        detailsPanel.Controls.Add(modelsListBox);
        detailsPanel.Controls.Add(newModelTextBox);
        detailsPanel.Controls.Add(addModelButton);
        detailsPanel.Controls.Add(removeModelButton);
        detailsPanel.Controls.Add(fetchModelsButton);
        detailsPanel.Controls.Add(fetchStatusLabel);
        detailsPanel.Controls.Add(inlineActionPanel);

        splitContainer.Panel2.Controls.Add(detailsPanel);
        providersPanel.Controls.Add(splitContainer);
    }

    private void ProvidersListBox_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= providersListBox.Items.Count) return;

        var c = ThemeManager.Colors;
        if (providersListBox.Items[e.Index] is not AiProvider prov) return;

        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        // Background
        Color bg = isSelected ? Color.FromArgb(34, 52, 45) : c.CardBackground;
        using var bgBrush = new SolidBrush(bg);
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        // Status indicator circle or check
        string badge = prov.IsEnabled ? "●" : "○";
        Color badgeColor = prov.IsEnabled ? c.AccentGreen : c.TextMuted;
        using var badgeBrush = new SolidBrush(badgeColor);
        using var badgeFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        e.Graphics.DrawString(badge, badgeFont, badgeBrush, e.Bounds.X + 8, e.Bounds.Y + 5);

        // Name text
        string displayText = prov.IsEnabled ? prov.Name : $"{prov.Name} (Disabled)";
        Color textColor = prov.IsEnabled ? (isSelected ? c.AccentGreenText : c.TextPrimary) : c.TextMuted;
        using var textBrush = new SolidBrush(textColor);
        using var textFont = new Font("Segoe UI", 8.75F, isSelected ? FontStyle.Bold : FontStyle.Regular);
        e.Graphics.DrawString(displayText, textFont, textBrush, e.Bounds.X + 26, e.Bounds.Y + 6);

        // Subtle bottom border
        using var linePen = new Pen(Color.FromArgb(36, 40, 52));
        e.Graphics.DrawLine(linePen, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private void SwitchTab(bool isToolsTab)
    {
        toolsPanel.Visible = isToolsTab;
        providersPanel.Visible = !isToolsTab;

        var c = ThemeManager.Colors;
        if (isToolsTab)
        {
            tabToolsButton.BackColor = c.AccentGreen;
            tabToolsButton.ForeColor = c.AccentGreenText;
            tabProvidersButton.BackColor = c.ButtonBg;
            tabProvidersButton.ForeColor = c.ButtonText;
        }
        else
        {
            tabProvidersButton.BackColor = c.AccentGreen;
            tabProvidersButton.ForeColor = c.AccentGreenText;
            tabToolsButton.BackColor = c.ButtonBg;
            tabToolsButton.ForeColor = c.ButtonText;
        }
    }

    private void LoadDataToUi()
    {
        foreach (var (key, txt) in _toolTextBoxes)
        {
            txt.Text = _workingConfig.ToolTargets.TryGetValue(key, out var val) ? val : string.Empty;
        }

        UpdateProvidersListDisplay();
        if (_workingConfig.Providers.Count > 0)
        {
            providersListBox.SelectedIndex = 0;
        }
    }

    private void CommitCurrentProviderFields()
    {
        if (_selectedProvider != null)
        {
            _selectedProvider.Name = providerNameTextBox.Text.Trim();
            _selectedProvider.BaseUrl = providerUrlTextBox.Text.Trim();
            _selectedProvider.ApiKey = providerKeyTextBox.Text.Trim();
            _selectedProvider.IsEnabled = providerEnabledCheckBox.Checked;
        }
    }

    private void UpdateProvidersListDisplay()
    {
        _isUpdatingProviderUi = true;
        int prevIdx = providersListBox.SelectedIndex;
        providersListBox.Items.Clear();

        foreach (var p in _workingConfig.Providers)
        {
            providersListBox.Items.Add(p);
        }

        if (prevIdx >= 0 && prevIdx < providersListBox.Items.Count)
        {
            providersListBox.SelectedIndex = prevIdx;
        }
        else if (providersListBox.Items.Count > 0)
        {
            providersListBox.SelectedIndex = 0;
        }
        _isUpdatingProviderUi = false;
        providersListBox.Invalidate();
    }

    private void ProvidersListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingProviderUi) return;

        // Commit current provider before switching
        CommitCurrentProviderFields();

        if (providersListBox.SelectedItem is AiProvider p)
        {
            _selectedProvider = p;
            _isUpdatingProviderUi = true;
            providerNameTextBox.Text = p.Name;
            providerEnabledCheckBox.Checked = p.IsEnabled;
            providerUrlTextBox.Text = p.BaseUrl;
            providerKeyTextBox.Text = p.ApiKey;
            RefreshModelsListUi();
            fetchStatusLabel.Text = string.Empty;
            providerSaveStatusLabel.Text = string.Empty;
            _isUpdatingProviderUi = false;
        }
    }

    private void RefreshModelsListUi()
    {
        modelsListBox.Items.Clear();
        if (_selectedProvider != null)
        {
            foreach (var m in _selectedProvider.Models)
            {
                modelsListBox.Items.Add(m);
            }
        }
    }

    private void AddProviderButton_Click(object? sender, EventArgs e)
    {
        CommitCurrentProviderFields();

        var newProv = new AiProvider
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New Provider",
            BaseUrl = "https://api.openai.com/v1",
            ApiKey = string.Empty,
            IsEnabled = true,
            Models = new List<string> { "gpt-4o-mini" }
        };

        _workingConfig.Providers.Add(newProv);
        UpdateProvidersListDisplay();
        providersListBox.SelectedItem = newProv;
        providerNameTextBox.Focus();
        providerNameTextBox.SelectAll();
    }

    private void RemoveProviderButton_Click(object? sender, EventArgs e)
    {
        if (_selectedProvider == null) return;

        if (_workingConfig.Providers.Count <= 1)
        {
            MessageBox.Show(this, "At least one provider must remain configured.", "Cannot Remove", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(this, $"Are you sure you want to remove provider '{_selectedProvider.Name}'?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm == DialogResult.Yes)
        {
            _workingConfig.Providers.Remove(_selectedProvider);
            _selectedProvider = null;
            UpdateProvidersListDisplay();
        }
    }

    private void AddModelButton_Click(object? sender, EventArgs e)
    {
        if (_selectedProvider == null) return;
        var m = newModelTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(m)) return;

        if (!_selectedProvider.Models.Contains(m, StringComparer.OrdinalIgnoreCase))
        {
            _selectedProvider.Models.Add(m);
            RefreshModelsListUi();
            newModelTextBox.Clear();

            // Auto-persist model addition
            AiConfigService.Instance.SaveConfig(_workingConfig);
            providerSaveStatusLabel.Text = "✓ Model added & saved";
            providerSaveStatusLabel.ForeColor = ThemeManager.Colors.AccentGreen;
        }
    }

    private void RemoveModelButton_Click(object? sender, EventArgs e)
    {
        if (_selectedProvider == null) return;
        if (modelsListBox.SelectedItem is string m)
        {
            _selectedProvider.Models.Remove(m);
            RefreshModelsListUi();

            // Auto-persist model removal
            AiConfigService.Instance.SaveConfig(_workingConfig);
            providerSaveStatusLabel.Text = "✓ Model removed & saved";
            providerSaveStatusLabel.ForeColor = ThemeManager.Colors.AccentGreen;
        }
    }

    private async void FetchModelsButton_Click(object? sender, EventArgs e)
    {
        if (_selectedProvider == null) return;

        CommitCurrentProviderFields();

        if (string.IsNullOrWhiteSpace(_selectedProvider.BaseUrl))
        {
            MessageBox.Show(this, "Please enter a valid Base URL first.", "Missing Base URL", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        fetchModelsButton.Enabled = false;
        fetchStatusLabel.Text = "Fetching models...";
        fetchStatusLabel.ForeColor = ThemeManager.Colors.TextMuted;

        try
        {
            var models = await PromptEnhancerService.Instance.FetchAvailableModelsAsync(_selectedProvider);
            int addedCount = 0;
            foreach (var m in models)
            {
                if (!_selectedProvider.Models.Contains(m, StringComparer.OrdinalIgnoreCase))
                {
                    _selectedProvider.Models.Add(m);
                    addedCount++;
                }
            }
            RefreshModelsListUi();
            fetchStatusLabel.Text = $"Fetched {models.Count} models (+{addedCount} new added)!";
            fetchStatusLabel.ForeColor = ThemeManager.Colors.AccentGreen;

            // Auto-persist fetched models
            AiConfigService.Instance.SaveConfig(_workingConfig);
            providerSaveStatusLabel.Text = "✓ Models saved to configuration";
            providerSaveStatusLabel.ForeColor = ThemeManager.Colors.AccentGreen;
        }
        catch (Exception ex)
        {
            fetchStatusLabel.Text = "Failed: " + ex.Message;
            fetchStatusLabel.ForeColor = ThemeManager.Colors.DangerRed;
        }
        finally
        {
            fetchModelsButton.Enabled = true;
        }
    }

    private void SaveCurrentProviderButton_Click(object? sender, EventArgs e)
    {
        CommitCurrentProviderFields();
        AiConfigService.Instance.SaveConfig(_workingConfig);
        providersListBox.Invalidate();

        providerSaveStatusLabel.Text = "✓ Provider saved successfully!";
        providerSaveStatusLabel.ForeColor = ThemeManager.Colors.AccentGreen;
        statusLabel.Text = "✓ Provider saved!";
        statusLabel.ForeColor = ThemeManager.Colors.AccentGreen;

        var timer = new System.Windows.Forms.Timer { Interval = 2500 };
        timer.Tick += (_, _) =>
        {
            providerSaveStatusLabel.Text = string.Empty;
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        // Flush provider fields
        CommitCurrentProviderFields();

        // Flush tool targets
        foreach (var (key, txt) in _toolTextBoxes)
        {
            _workingConfig.ToolTargets[key] = txt.Text.Trim();
        }

        // Save
        AiConfigService.Instance.SaveConfig(_workingConfig);
        statusLabel.Text = "✓ All settings saved successfully!";
        statusLabel.ForeColor = ThemeManager.Colors.AccentGreen;

        var t = new System.Windows.Forms.Timer { Interval = 700 };
        t.Tick += (_, _) =>
        {
            t.Stop();
            t.Dispose();
            Close();
        };
        t.Start();
    }

    private void ApplyTheme()
    {
        var c = ThemeManager.Colors;
        BackColor = c.WindowBackground;
        ForeColor = c.TextPrimary;

        ThemeManager.ApplyDwmTitleBarTheme(Handle);

        topTabPanel.BackColor = c.HeaderBackground;
        bottomPanel.BackColor = c.FooterBackground;
        bottomActionPanel.BackColor = c.FooterBackground;
        contentContainer.BackColor = c.WindowBackground;

        toolsPanel.BackColor = c.WindowBackground;
        providersPanel.BackColor = c.WindowBackground;

        StyleButton(tabToolsButton, c.ButtonBg, c.ButtonText, c.ButtonBorder);
        StyleButton(tabProvidersButton, c.ButtonBg, c.ButtonText, c.ButtonBorder);

        StyleButton(saveButton, c.AccentGreen, c.AccentGreenText, c.AccentGreen);
        StyleButton(cancelButton, c.ButtonBg, c.ButtonText, c.ButtonBorder);
        StyleButton(addProviderButton, c.ButtonBg, c.AccentGreen, c.ButtonBorder);
        StyleButton(removeProviderButton, c.ButtonBg, c.DangerRed, c.ButtonBorder);
        StyleButton(addModelButton, c.ButtonBg, c.AccentGreen, c.ButtonBorder);
        StyleButton(removeModelButton, c.ButtonBg, c.ButtonText, c.ButtonBorder);
        StyleButton(fetchModelsButton, c.ButtonBg, c.AccentIndigo, c.ButtonBorder);
        StyleButton(toggleKeyVisibilityButton, c.ButtonBg, c.TextMuted, c.ButtonBorder);
        StyleButton(saveCurrentProviderButton, c.AccentGreen, c.AccentGreenText, c.AccentGreen);

        providerEnabledCheckBox.ForeColor = c.AccentGreen;

        providersListBox.BackColor = c.CardBackground;
        providersListBox.ForeColor = c.TextPrimary;
        modelsListBox.BackColor = c.CardBackground;
        modelsListBox.ForeColor = c.TextPrimary;

        providerNameTextBox.BackColor = c.WellBackground;
        providerNameTextBox.ForeColor = c.TextPrimary;
        providerUrlTextBox.BackColor = c.WellBackground;
        providerUrlTextBox.ForeColor = c.TextPrimary;
        providerKeyTextBox.BackColor = c.WellBackground;
        providerKeyTextBox.ForeColor = c.TextPrimary;
        newModelTextBox.BackColor = c.WellBackground;
        newModelTextBox.ForeColor = c.TextPrimary;

        foreach (Control ctl in toolsPanel.Controls)
        {
            if (ctl is Panel card)
            {
                card.BackColor = c.CardBackground;
                foreach (Control sub in card.Controls)
                {
                    if (sub is TextBox tb)
                    {
                        tb.BackColor = c.WellBackground;
                        tb.ForeColor = c.TextPrimary;
                    }
                    else if (sub is Button btn)
                    {
                        StyleButton(btn, c.ButtonBg, c.ButtonText, c.ButtonBorder);
                    }
                }
            }
        }
    }

    private static void StyleButton(Button btn, Color bg, Color fg, Color border)
    {
        btn.FlatStyle = FlatStyle.Flat;
        btn.BackColor = bg;
        btn.ForeColor = fg;
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = border;
    }
}
