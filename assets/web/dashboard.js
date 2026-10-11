// Agents Hub - Modern WebView2 Dashboard Front-End Engine
(function () {
  'use strict';

  // Global Client State
  const state = {
    version: 'v1.0.3',
    lanIp: '127.0.0.1',
    lanUrl: 'http://127.0.0.1:9095/',
    publicIp: '',
    isIpMasked: false,
    isShortcutsMasked: localStorage.getItem('agents_hub_mask_shortcuts') === 'true',
    qrBase64: '',
    clis: {},
    antigravity: { activeProfile: '', profiles: [] },
    aiConfig: { providers: [], toolTargets: {}, shortcuts: [], selectedProviderId: null, selectedModel: null },
    settings: { autostart: false, startMinimized: false, minimizeToTray: true, notifications: true, maskPublicIp: false, maskShortcutPaths: false, theme: 'dark', accentColor: '#4049B9' },
    activeView: 'dashboard',
    activeTerminalCli: 'Hermes',
    selectedProvider: null,
    pendingRequests: new Map()
  };

  // Bridge RPC Communication Helper
  function callBridge(action, payload = {}) {
    return new Promise((resolve, reject) => {
      if (!window.chrome || !window.chrome.webview) {
        console.warn('WebView2 environment not detected.');
        resolve({});
        return;
      }
      const requestId = 'req_' + Math.random().toString(36).substring(2, 9);
      const timeout = setTimeout(() => {
        state.pendingRequests.delete(requestId);
        reject(new Error(`Request ${action} timed out.`));
      }, 30000);

      state.pendingRequests.set(requestId, { resolve, reject, timeout });
      window.chrome.webview.postMessage({ action, ...payload, requestId });
    });
  }

  // Handle Inbound Messages & Events from C#
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (event) => {
      const msg = event.data;
      if (!msg) return;

      // Handle RPC response
      if (msg.responseId && state.pendingRequests.has(msg.responseId)) {
        const { resolve, reject, timeout } = state.pendingRequests.get(msg.responseId);
        clearTimeout(timeout);
        state.pendingRequests.delete(msg.responseId);
        if (msg.success) resolve(msg.data);
        else reject(new Error(msg.data || 'Action failed'));
        return;
      }

      // Handle Push Events
      if (msg.event) {
        handleBackendEvent(msg.event, msg.payload);
      }
    });
  }

  function handleBackendEvent(eventType, payload) {
    // FIX: Do NOT flood logs with hardware metrics or stream chunks!
    if (eventType !== 'hardware_metrics' && eventType !== 'terminal_data') {
      appendLog(`[${eventType}] ${JSON.stringify(payload)}`);
    }

    switch (eventType) {
      case 'cli_status_changed':
        if (payload && payload.cli) {
          state.clis[payload.cli] = { state: payload.state, port: payload.port, pid: payload.pid };
          updateHeaderStatus();
          updateDashboardCliRows();
        }
        break;

      case 'terminal_data':
        if (payload && payload.data) {
          if (termInstance && payload.cli === state.activeTerminalCli) {
            termInstance.write(payload.data);
          }
        }
        break;

      case 'profile_changed':
      case 'profile_saved':
        if (payload) {
          if (payload.activeProfile) state.antigravity.activeProfile = payload.activeProfile;
          if (payload.profiles) state.antigravity.profiles = payload.profiles;
          renderProfiles();
          renderQuickProfileSelect();
          updateHeaderStatus();
        }
        break;

      case 'hardware_metrics':
        if (payload) {
          const cpuEl = document.getElementById('metric-cpu');
          const ramEl = document.getElementById('metric-ram');
          if (cpuEl) cpuEl.textContent = `CPU ${payload.cpu}%`;
          if (ramEl) ramEl.textContent = `RAM ${payload.ramMb} MB`;
        }
        break;

      case 'ai_config_changed':
        if (payload) {
          state.aiConfig = payload;
          renderPromptModelsDropdown();
          renderProvidersList();
          renderDevToolsList();
        }
        break;
    }
  }

  // Navigation Logic & Collapsible Sidebar
  function setupNavigation() {
    const navItems = document.querySelectorAll('.nav-item');
    navItems.forEach(item => {
      item.addEventListener('click', () => {
        const targetView = item.dataset.view;
        switchView(targetView);
      });
    });

    // Closeable / Collapsible Sidebar
    const btnToggleSidebar = document.getElementById('btn-toggle-sidebar');
    const sidebar = document.getElementById('sidebar');
    
    // Restore sidebar preference
    const savedCollapsed = localStorage.getItem('agents_hub_sidebar_collapsed') === 'true';
    if (savedCollapsed) sidebar.classList.add('collapsed');

    btnToggleSidebar?.addEventListener('click', () => {
      sidebar.classList.toggle('collapsed');
      localStorage.setItem('agents_hub_sidebar_collapsed', sidebar.classList.contains('collapsed'));
      if (fitAddonInstance && state.activeView === 'terminals') {
        setTimeout(() => fitAddonInstance.fit(), 220);
      }
    });
  }

  function switchView(viewName) {
    state.activeView = viewName;
    document.querySelectorAll('.nav-item').forEach(i => {
      i.classList.toggle('active', i.dataset.view === viewName);
    });
    document.querySelectorAll('.view-pane').forEach(p => {
      p.classList.toggle('active', p.id === `view-${viewName}`);
    });

    if (viewName === 'terminals') {
      setTimeout(() => {
        if (fitAddonInstance) fitAddonInstance.fit();
      }, 50);
    }
  }

  // Discord Adaptive Theme & Color Engine
  function hexToHsl(hex) {
    hex = (hex || '').replace(/^#/, '').trim();
    if (hex.length === 3) {
      hex = hex.split('').map(c => c + c).join('');
    }
    if (hex.length !== 6 || !/^[0-9a-fA-F]{6}$/.test(hex)) {
      return { h: 160, s: 84, l: 39 }; // Fallback Emerald
    }
    const r = parseInt(hex.substring(0, 2), 16) / 255;
    const g = parseInt(hex.substring(2, 4), 16) / 255;
    const b = parseInt(hex.substring(4, 6), 16) / 255;

    const max = Math.max(r, g, b);
    const min = Math.min(r, g, b);
    let h = 0;
    let s = 0;
    let l = (max + min) / 2;

    if (max !== min) {
      const d = max - min;
      s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
      switch (max) {
        case r: h = (g - b) / d + (g < b ? 6 : 0); break;
        case g: h = (b - r) / d + 2; break;
        case b: h = (r - g) / d + 4; break;
      }
      h = Math.round(h * 60);
    }
    s = Math.round(s * 100);
    l = Math.round(l * 100);
    return { h, s, l };
  }

  function getPerceivedLuminance(hex) {
    hex = (hex || '').replace(/^#/, '').trim();
    if (hex.length === 3) hex = hex.split('').map(c => c + c).join('');
    if (hex.length !== 6 || !/^[0-9a-fA-F]{6}$/.test(hex)) return 0.5;
    const r = parseInt(hex.substring(0, 2), 16);
    const g = parseInt(hex.substring(2, 4), 16);
    const b = parseInt(hex.substring(4, 6), 16);
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  }

  function applyAdaptiveTheme(themeName, hexColor, intensityVal = null) {
    const activeTheme = themeName === 'light' ? 'light' : 'dark';
    
    let hex = (hexColor || '').trim();
    if (!hex.startsWith('#')) hex = '#' + hex;
    if (hex.length === 4) {
      hex = '#' + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3];
    }
    if (!/^#[0-9a-fA-F]{6}$/.test(hex)) {
      hex = '#4049B9';
    }
    hex = hex.toUpperCase();

    // Color intensity support (Discord theme customizer: default 74%)
    let intensity = intensityVal !== null ? parseInt(intensityVal, 10) : parseInt(localStorage.getItem('agents_hub_theme_intensity') || '74', 10);
    if (isNaN(intensity) || intensity < 10 || intensity > 100) intensity = 74;
    const intMult = intensity / 74;

    const { h, s, l } = hexToHsl(hex);
    const rootStyle = document.documentElement.style;

    document.documentElement.setAttribute('data-theme', activeTheme);

    if (activeTheme === 'dark') {
      // Discord-style Dark Surface Hierarchy modulated by intensity
      const sTint = s < 12 
        ? Math.max(2, Math.round(s * intMult)) 
        : Math.min(32, Math.max(6, Math.round(s * 0.28 * intMult)));
      
      rootStyle.setProperty('--bg-app', `hsl(${h}, ${sTint}%, 5.5%)`);
      rootStyle.setProperty('--bg-sidebar', `hsl(${h}, ${sTint + 2}%, 7.5%)`);
      rootStyle.setProperty('--bg-header', `hsl(${h}, ${sTint + 2}%, 9%)`);
      rootStyle.setProperty('--bg-card', `hsl(${h}, ${sTint + 4}%, 12.5%)`);
      rootStyle.setProperty('--bg-card-hover', `hsl(${h}, ${sTint + 5}%, 16%)`);
      rootStyle.setProperty('--bg-input', `hsl(${h}, ${sTint}%, 7%)`);
      rootStyle.setProperty('--border-subtle', `hsl(${h}, ${sTint + 2}%, 19%)`);
      rootStyle.setProperty('--border-hover', `hsl(${h}, ${Math.min(s, 75)}%, 48%)`);
      rootStyle.setProperty('--bg-terminal', `hsl(${h}, ${sTint}%, 5%)`);
      
      rootStyle.setProperty('--text-main', '#f8fafc');
      rootStyle.setProperty('--text-muted', `hsl(${h}, 12%, 68%)`);
      rootStyle.setProperty('--text-dim', `hsl(${h}, 10%, 48%)`);

      const darkAccent = l < 42 ? `hsl(${h}, ${Math.max(s, 50)}%, 52%)` : hex;
      const lum = getPerceivedLuminance(darkAccent);
      rootStyle.setProperty('--accent-green', darkAccent);
      rootStyle.setProperty('--accent-green-hover', `hsl(${h}, ${s}%, ${Math.max(12, l - 8)}%)`);
      rootStyle.setProperty('--accent-green-bg', `hsla(${h}, ${s}%, ${l}%, 0.16)`);
      rootStyle.setProperty('--accent-text', lum > 0.62 ? '#0a0f14' : '#ffffff');
      rootStyle.setProperty('--border-focus', darkAccent);
    } else {
      // Discord-style Light Surface Hierarchy matching Discord app in screenshot
      const sTintLight = s < 12 
        ? Math.max(2, Math.round(s * 0.4 * intMult)) 
        : Math.min(26, Math.max(4, Math.round(s * 0.22 * intMult)));

      rootStyle.setProperty('--bg-app', `hsl(${h}, ${sTintLight}%, 95.5%)`);
      rootStyle.setProperty('--bg-sidebar', `hsl(${h}, ${Math.round(sTintLight * 1.35)}%, 92.5%)`);
      rootStyle.setProperty('--bg-header', `hsl(${h}, ${Math.round(sTintLight * 1.5)}%, 88%)`);
      rootStyle.setProperty('--bg-card', '#ffffff');
      rootStyle.setProperty('--bg-card-hover', `hsl(${h}, ${sTintLight}%, 96.5%)`);
      rootStyle.setProperty('--bg-input', `hsl(${h}, ${sTintLight}%, 96.8%)`);
      rootStyle.setProperty('--border-subtle', `hsl(${h}, ${Math.round(sTintLight * 1.25)}%, 83%)`);
      rootStyle.setProperty('--border-hover', `hsl(${h}, ${Math.min(s, 75)}%, 45%)`);
      rootStyle.setProperty('--bg-terminal', `hsl(${h}, 15%, 8%)`);

      rootStyle.setProperty('--text-main', `hsl(${h}, 28%, 11%)`);
      rootStyle.setProperty('--text-muted', `hsl(${h}, 16%, 36%)`);
      rootStyle.setProperty('--text-dim', `hsl(${h}, 10%, 52%)`);

      const lightAccent = l > 48 ? `hsl(${h}, ${Math.max(s, 60)}%, 42%)` : hex;
      const lum = getPerceivedLuminance(lightAccent);
      rootStyle.setProperty('--accent-green', lightAccent);
      rootStyle.setProperty('--accent-green-hover', `hsl(${h}, ${s}%, 34%)`);
      rootStyle.setProperty('--accent-green-bg', `hsla(${h}, ${s}%, 45%, 0.12)`);
      rootStyle.setProperty('--accent-text', lum > 0.62 ? '#0a0f14' : '#ffffff');
      rootStyle.setProperty('--border-focus', lightAccent);
    }

    if (termInstance) {
      termInstance.options.theme = {
        background: activeTheme === 'dark' ? `hsl(${h}, ${s < 12 ? 6 : 14}%, 5%)` : '#0d1117',
        foreground: '#f8fafc',
        cursor: hex,
        selectionBackground: `hsla(${h}, ${s}%, 50%, 0.3)`
      };
    }

    document.querySelectorAll('.btn-theme-toggle').forEach(b => {
      b.classList.toggle('active', b.dataset.theme === activeTheme);
    });

    document.querySelectorAll('.color-swatch').forEach(sw => {
      sw.classList.toggle('active', sw.dataset.color.toUpperCase() === hex);
    });

    const hexInput = document.getElementById('accent-hex-input');
    const picker = document.getElementById('accent-color-picker');
    const slider = document.getElementById('theme-intensity-slider');
    const sliderVal = document.getElementById('theme-intensity-val');
    if (hexInput && document.activeElement !== hexInput) hexInput.value = hex;
    if (picker) picker.value = hex;
    if (slider) slider.value = intensity;
    if (sliderVal) sliderVal.textContent = intensity + '%';

    localStorage.setItem('agents_hub_theme', activeTheme);
    localStorage.setItem('agents_hub_accent_color', hex);
    localStorage.setItem('agents_hub_theme_intensity', intensity);
  }

  function applyTheme(themeName) {
    const currentColor = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
    applyAdaptiveTheme(themeName, currentColor);
  }

  function applyAccentColor(hex) {
    const currentTheme = localStorage.getItem('agents_hub_theme') || 'dark';
    applyAdaptiveTheme(currentTheme, hex);
  }

  function setupSettingsView() {
    // Theme toggles
    document.querySelectorAll('.btn-theme-toggle').forEach(b => {
      b.addEventListener('click', () => {
        const theme = b.dataset.theme;
        const color = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
        applyAdaptiveTheme(theme, color);
        callBridge('save_general_settings', {
          settings: { theme, accentColor: color }
        }).catch(() => {});
      });
    });

    // Color Swatches
    document.querySelectorAll('.color-swatch').forEach(sw => {
      sw.addEventListener('click', () => {
        const color = sw.dataset.color;
        const theme = localStorage.getItem('agents_hub_theme') || 'dark';
        applyAdaptiveTheme(theme, color);
        callBridge('save_general_settings', {
          settings: { theme, accentColor: color }
        }).catch(() => {});
      });
    });

    // Color Intensity Slider
    const slider = document.getElementById('theme-intensity-slider');
    const sliderVal = document.getElementById('theme-intensity-val');
    slider?.addEventListener('input', (e) => {
      const intVal = e.target.value;
      if (sliderVal) sliderVal.textContent = intVal + '%';
      const theme = localStorage.getItem('agents_hub_theme') || 'dark';
      const color = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
      applyAdaptiveTheme(theme, color, intVal);
    });

    // Custom Color Picker & Hex Input
    const picker = document.getElementById('accent-color-picker');
    const hexInput = document.getElementById('accent-hex-input');

    picker?.addEventListener('input', (e) => {
      const color = e.target.value;
      const theme = localStorage.getItem('agents_hub_theme') || 'dark';
      applyAdaptiveTheme(theme, color);
    });

    picker?.addEventListener('change', (e) => {
      const color = e.target.value;
      const theme = localStorage.getItem('agents_hub_theme') || 'dark';
      callBridge('save_general_settings', {
        settings: { theme, accentColor: color }
      }).catch(() => {});
    });

    const triggerApplyCustomHex = () => {
      let val = hexInput.value.trim();
      if (!val) return;
      if (!val.startsWith('#')) val = '#' + val;
      const theme = localStorage.getItem('agents_hub_theme') || 'dark';
      applyAdaptiveTheme(theme, val);
      callBridge('save_general_settings', {
        settings: { theme, accentColor: val }
      }).catch(() => {});
    };

    document.getElementById('btn-apply-custom-color')?.addEventListener('click', triggerApplyCustomHex);

    hexInput?.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        triggerApplyCustomHex();
      }
    });

    // Save General Settings Button
    document.getElementById('btn-save-general-settings')?.addEventListener('click', async () => {
      const autostart = document.getElementById('setting-autostart-checkbox').checked;
      const startMinimized = document.getElementById('setting-minimized-checkbox').checked;
      const minimizeToTray = document.getElementById('setting-tray-minimize-checkbox')?.checked !== false;
      const notifications = document.getElementById('setting-notifications-checkbox')?.checked !== false;
      const maskShortcutPaths = document.getElementById('setting-mask-paths-checkbox')?.checked === true;
      const theme = document.documentElement.getAttribute('data-theme') || 'dark';
      const accentColor = localStorage.getItem('agents_hub_accent_color') || '#10b981';
      const feedback = document.getElementById('settings-save-feedback');

      state.isShortcutsMasked = maskShortcutPaths;
      localStorage.setItem('agents_hub_mask_shortcuts', maskShortcutPaths);
      renderDashboardShortcuts();

      await callBridge('save_general_settings', {
        settings: { autostart, startMinimized, minimizeToTray, notifications, maskShortcutPaths, theme, accentColor }
      });

      feedback.textContent = 'Preferences saved successfully!';
      setTimeout(() => feedback.textContent = '', 2500);
    });
  }

  // Terminal (xterm.js) Management
  let termInstance = null;
  let fitAddonInstance = null;

  function initTerminal() {
    const container = document.getElementById('xterm-container');
    if (!container || typeof Terminal === 'undefined') return;

    termInstance = new Terminal({
      cursorBlink: true,
      fontFamily: "'Cascadia Code', 'Fira Code', 'Consolas', monospace",
      fontSize: 13,
      theme: {
        background: '#09090b',
        foreground: '#f4f4f5',
        cursor: '#10b981',
        selectionBackground: 'rgba(16, 185, 129, 0.3)'
      }
    });

    if (typeof FitAddon !== 'undefined' && FitAddon.FitAddon) {
      fitAddonInstance = new FitAddon.FitAddon();
      termInstance.loadAddon(fitAddonInstance);
    }

    termInstance.open(container);
    if (fitAddonInstance) fitAddonInstance.fit();

    termInstance.onData(data => {
      callBridge('send_terminal_input', {
        cli: state.activeTerminalCli,
        input: data
      });
    });

    window.addEventListener('resize', () => {
      if (fitAddonInstance && state.activeView === 'terminals') {
        fitAddonInstance.fit();
        callBridge('resize_terminal', {
          cli: state.activeTerminalCli,
          cols: termInstance.cols,
          rows: termInstance.rows
        });
      }
    });

    // Terminal Tabs
    document.querySelectorAll('.term-tab').forEach(tab => {
      tab.addEventListener('click', () => {
        document.querySelectorAll('.term-tab').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');
        state.activeTerminalCli = tab.dataset.term;
        termInstance.clear();
        termInstance.writeln(`\x1b[32m[Connected to ${state.activeTerminalCli} Terminal]\x1b[0m\r\n`);
      });
    });

    document.getElementById('btn-clear-terminal')?.addEventListener('click', () => {
      if (termInstance) termInstance.clear();
    });

    document.getElementById('btn-term-open-browser')?.addEventListener('click', () => {
      const port = state.activeTerminalCli === 'Hermes' ? 9095 : (state.activeTerminalCli === 'ClaudeCode' ? 9229 : 9339);
      callBridge('open_url_external', { url: `http://127.0.0.1:${port}/` });
    });

    // Standalone Terminals Window Buttons
    const openStandalone = () => callBridge('open_standalone_terminals');
    document.getElementById('btn-popout-terminals')?.addEventListener('click', openStandalone);
    document.getElementById('btn-popout-terminals-dash')?.addEventListener('click', openStandalone);
    document.getElementById('btn-open-standalone-hdr')?.addEventListener('click', openStandalone);
  }

  // Header & Status Updates
  function updateHeaderStatus() {
    const updatePill = (cliName, stateVal, pillId, dotId, statusId) => {
      const dot = document.getElementById(dotId);
      const lbl = document.getElementById(statusId);
      const isRunning = stateVal === 'Running';
      if (dot) dot.className = 'dot ' + (isRunning ? 'online' : '');
      if (lbl) {
        lbl.textContent = isRunning ? 'Running' : 'Offline';
        lbl.style.color = isRunning ? 'var(--accent-green)' : 'var(--text-muted)';
      }
    };

    const hermes = state.clis['Hermes'] || {};
    const claude = state.clis['ClaudeCode'] || {};
    const agy = state.clis['AntigravityCli'] || {};

    updatePill('Hermes', hermes.state, 'pill-hermes', 'dot-hermes', 'status-hermes');
    updatePill('Claude Code', claude.state, 'pill-claude', 'dot-claude', 'status-claude');

    const agyDot = document.getElementById('dot-antigravity');
    const agyStatus = document.getElementById('status-antigravity');
    const isAgyRunning = agy.state === 'Running';
    if (agyDot) agyDot.className = 'dot ' + (isAgyRunning ? 'busy' : '');
    if (agyStatus) {
      agyStatus.textContent = isAgyRunning ? `Running (${state.antigravity.activeProfile || 'default'})` : `Offline (${state.antigravity.activeProfile || 'none'})`;
      agyStatus.style.color = isAgyRunning ? 'var(--accent-cyan)' : 'var(--text-muted)';
    }

    const summaryEl = document.getElementById('dash-active-profile-summary');
    if (summaryEl) {
      summaryEl.textContent = `Active Profile: ${state.antigravity.activeProfile || 'None'}`;
    }
  }

  function updateDashboardCliRows() {
    const bindRow = (cliKey, btnCtrlId, descId) => {
      const info = state.clis[cliKey] || {};
      const isRunning = info.state === 'Running';
      const btn = document.getElementById(btnCtrlId);
      const desc = document.getElementById(descId);

      if (btn) {
        btn.textContent = isRunning ? 'Stop' : 'Start';
        btn.className = isRunning ? 'btn btn-danger' : 'btn btn-success';
      }
      if (desc) {
        desc.textContent = isRunning ? `State: Running (PID ${info.pid || 'Active'})` : 'State: Offline';
        desc.style.color = isRunning ? 'var(--accent-green)' : 'var(--text-muted)';
      }
    };

    bindRow('Hermes', 'btn-ctrl-hermes', 'desc-hermes-state');
    bindRow('ClaudeCode', 'btn-ctrl-claude', 'desc-claude-state');
    bindRow('AntigravityCli', 'btn-ctrl-antigravity', 'desc-antigravity-state');
  }

  // Prompt Enhancer View
  function renderPromptModelsDropdown() {
    const select = document.getElementById('enhance-model-select');
    if (!select) return;

    select.innerHTML = '';
    const enabledProviders = (state.aiConfig.providers || []).filter(p => p.isEnabled);

    if (enabledProviders.length === 0) {
      const opt = document.createElement('option');
      opt.textContent = '(All providers disabled - go to AI Providers to enable)';
      opt.disabled = true;
      select.appendChild(opt);
      return;
    }

    enabledProviders.forEach(prov => {
      const optGroup = document.createElement('optgroup');
      optGroup.label = `-- ${prov.name}`;

      if (!prov.models || prov.models.length === 0) {
        const item = document.createElement('option');
        item.textContent = '   - (No models configured)';
        item.disabled = true;
        optGroup.appendChild(item);
      } else {
        prov.models.forEach(m => {
          const item = document.createElement('option');
          item.value = `${prov.id}::${m}`;
          item.textContent = `  - ${m}`;
          if (prov.id === state.aiConfig.selectedProviderId && m === state.aiConfig.selectedModel) {
            item.selected = true;
          }
          optGroup.appendChild(item);
        });
      }
      select.appendChild(optGroup);
    });
  }

  function setupPromptEnhancer() {
    const input = document.getElementById('prompt-input');
    const output = document.getElementById('prompt-output');
    const inCount = document.getElementById('prompt-input-count');
    const outCount = document.getElementById('prompt-output-count');
    const btnExec = document.getElementById('btn-enhance-exec');
    const statusLbl = document.getElementById('prompt-enhance-status');

    const updateCounts = (text, targetEl) => {
      const chars = text.length;
      const words = text.trim() ? text.trim().split(/\s+/).length : 0;
      targetEl.textContent = `${chars} chars | ${words} words`;
    };

    input?.addEventListener('input', () => updateCounts(input.value, inCount));
    output?.addEventListener('input', () => updateCounts(output.value, outCount));

    input?.addEventListener('keydown', (e) => {
      if (e.ctrlKey && e.key === 'Enter') {
        e.preventDefault();
        execEnhance();
      }
    });

    btnExec?.addEventListener('click', execEnhance);

    document.getElementById('btn-paste-prompt')?.addEventListener('click', async () => {
      try {
        const text = await navigator.clipboard.readText();
        input.value = text;
        updateCounts(text, inCount);
      } catch { }
    });

    document.getElementById('btn-copy-enhanced')?.addEventListener('click', () => {
      if (output.value) {
        navigator.clipboard.writeText(output.value);
        statusLbl.textContent = 'Copied to clipboard!';
        setTimeout(() => statusLbl.textContent = 'Ready', 2000);
      }
    });

    async function execEnhance() {
      const raw = input.value.trim();
      if (!raw) {
        statusLbl.textContent = 'Please enter a prompt first.';
        statusLbl.style.color = 'var(--accent-amber)';
        return;
      }

      const modelVal = document.getElementById('enhance-model-select').value;
      if (!modelVal || !modelVal.includes('::')) {
        statusLbl.textContent = 'Please select a valid model.';
        statusLbl.style.color = 'var(--accent-amber)';
        return;
      }

      const [providerId, model] = modelVal.split('::');
      const style = document.getElementById('enhance-style-select').value;

      btnExec.disabled = true;
      statusLbl.textContent = 'Enhancing prompt...';
      statusLbl.style.color = 'var(--accent-green)';
      output.value = 'Enhancing in progress, please wait...';

      try {
        const res = await callBridge('enhance_prompt', {
          providerId,
          model,
          prompt: raw,
          style
        });
        output.value = res.enhancedText || '';
        updateCounts(output.value, outCount);
        statusLbl.textContent = 'Enhancement complete!';
      } catch (err) {
        output.value = '';
        statusLbl.textContent = 'Failed: ' + err.message;
        statusLbl.style.color = 'var(--accent-red)';
      } finally {
        btnExec.disabled = false;
      }
    }
  }

  // Antigravity Profiles View
  function renderProfiles() {
    const container = document.getElementById('profiles-list-container');
    if (!container) return;

    container.innerHTML = '';
    const profiles = state.antigravity.profiles || [];
    const active = state.antigravity.activeProfile || '';

    profiles.forEach(p => {
      const isActive = p.toLowerCase() === active.toLowerCase();
      const card = document.createElement('div');
      card.className = 'dashboard-card';
      card.style.borderColor = isActive ? 'var(--accent-green)' : 'var(--border-subtle)';

      card.innerHTML = `
        <div class="card-header">
          <div class="card-title-group">
            <div class="card-icon" style="color: ${isActive ? 'var(--accent-green)' : 'var(--text-muted)'}">
              <svg class="icon-svg" viewBox="0 0 24 24"><path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
            </div>
            <div>
              <div class="card-title">${p}</div>
              <div class="card-subtitle">${isActive ? 'Active Session' : 'Ready to switch'}</div>
            </div>
          </div>
          ${isActive ? '<span class="brand-badge">ACTIVE</span>' : ''}
        </div>
        <div style="display: flex; gap: 8px; margin-top: 14px;">
          ${!isActive ? `<button class="btn btn-success btn-switch-prof" data-name="${p}" style="flex: 1;">Switch</button>` : ''}
          <button class="btn btn-outline btn-rename-prof" data-name="${p}">Rename</button>
          ${!isActive ? `<button class="btn btn-danger btn-delete-prof" data-name="${p}">Delete</button>` : ''}
        </div>
      `;
      container.appendChild(card);
    });

    container.querySelectorAll('.btn-switch-prof').forEach(b => {
      b.addEventListener('click', async () => {
        const name = b.dataset.name;
        b.disabled = true;
        try {
          await callBridge('switch_antigravity_profile', { profile: name });
        } finally {
          b.disabled = false;
        }
      });
    });

    container.querySelectorAll('.btn-rename-prof').forEach(b => {
      b.addEventListener('click', async () => {
        const oldName = b.dataset.name;
        const newName = prompt(`Enter new name for profile '${oldName}':`, oldName);
        if (newName && newName.trim() && newName.trim() !== oldName) {
          await callBridge('rename_antigravity_profile', { oldName, newName: newName.trim() });
        }
      });
    });

    container.querySelectorAll('.btn-delete-prof').forEach(b => {
      b.addEventListener('click', async () => {
        const name = b.dataset.name;
        if (confirm(`Are you sure you want to delete profile '${name}'?`)) {
          await callBridge('delete_antigravity_profile', { profileName: name });
        }
      });
    });
  }

  function renderQuickProfileSelect() {
    const sel = document.getElementById('quick-profile-select');
    if (!sel) return;

    sel.innerHTML = '';
    const profiles = state.antigravity.profiles || [];
    const active = state.antigravity.activeProfile || '';

    profiles.forEach(p => {
      const opt = document.createElement('option');
      opt.value = p;
      opt.textContent = p;
      if (p.toLowerCase() === active.toLowerCase()) opt.selected = true;
      sel.appendChild(opt);
    });

    sel.onchange = async () => {
      await callBridge('switch_antigravity_profile', { profile: sel.value });
    };
  }

  // AI Dev Tools View (Browse file selector & edit targets)
  function renderDevToolsList() {
    const container = document.getElementById('tools-list-container');
    if (!container) return;

    container.innerHTML = '';
    const targets = state.aiConfig.toolTargets || {};

    const toolMeta = [
      { key: 'FreeBuff', name: 'FreeBuff Desktop', desc: 'Main executable for FreeBuff Desktop' },
      { key: 'Cline', name: 'Cline App', desc: 'Executable path for Cline App' },
      { key: 'DeepSeek', name: 'DeepSeek Harness', desc: 'DeepSeek Harness executable path' },
      { key: 'OpenCode', name: 'OpenCode Desktop', desc: 'OpenCode desktop client executable' },
      { key: 'Antigravity-ide', name: 'Antigravity IDE', desc: 'Path to Antigravity IDE.exe' },
      { key: 'Claude', name: 'Claude', desc: 'Executable path or shell:AppsFolder package ID' },
      { key: 'ChatGPT', name: 'ChatGPT', desc: 'Executable path or shell:AppsFolder package ID' }
    ];

    toolMeta.forEach(t => {
      const targetPath = targets[t.key] || '';
      const row = document.createElement('div');
      row.className = 'dashboard-card';
      row.style.padding = '14px 16px';
      row.innerHTML = `
        <div style="display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 8px;">
          <div>
            <b style="font-size: 13.5px; color: var(--text-main);">${t.name}</b>
            <div style="font-size: 11.5px; color: var(--text-dim); margin-top: 2px;">${t.desc}</div>
          </div>
          <button class="btn btn-success btn-launch-tool" data-tool="${t.key}" style="padding: 5px 14px;">
            <svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"/></svg>
            <span>Launch</span>
          </button>
        </div>
        <div style="display: flex; gap: 8px; align-items: center;">
          <input type="text" class="input-text tool-path-input" data-tool="${t.key}" value="${targetPath}" placeholder="Path to executable..." style="flex: 1; font-family: var(--font-mono); font-size: 12px;">
          <button class="btn btn-outline btn-browse-tool" data-tool="${t.key}" title="Select file with file browser">
            <svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>
            <span>Browse...</span>
          </button>
          <button class="btn btn-outline btn-reset-tool" data-tool="${t.key}" title="Reset to default path">Reset</button>
        </div>
      `;
      container.appendChild(row);
    });

    // Launch buttons
    container.querySelectorAll('.btn-launch-tool').forEach(b => {
      b.addEventListener('click', () => callBridge('launch_tool', { toolName: b.dataset.tool }));
    });

    // Browse file buttons
    container.querySelectorAll('.btn-browse-tool').forEach(b => {
      b.addEventListener('click', async () => {
        const toolName = b.dataset.tool;
        try {
          const res = await callBridge('browse_tool_target', { toolName });
          if (res && res.path) {
            const input = container.querySelector(`.tool-path-input[data-tool="${toolName}"]`);
            if (input) input.value = res.path;
            state.aiConfig.toolTargets[toolName] = res.path;
          }
        } catch { }
      });
    });

    // Reset buttons
    container.querySelectorAll('.btn-reset-tool').forEach(b => {
      b.addEventListener('click', async () => {
        const toolName = b.dataset.tool;
        const res = await callBridge('reset_tool_target', { toolName });
        if (res && res.path) {
          const input = container.querySelector(`.tool-path-input[data-tool="${toolName}"]`);
          if (input) input.value = res.path;
          state.aiConfig.toolTargets[toolName] = res.path;
        }
      });
    });

    // Manual path edit
    container.querySelectorAll('.tool-path-input').forEach(inp => {
      inp.addEventListener('change', () => {
        const toolName = inp.dataset.tool;
        state.aiConfig.toolTargets[toolName] = inp.value.trim();
        callBridge('save_ai_config', { config: state.aiConfig });
      });
    });
  }

  // Dashboard Custom Shortcuts Engine
  function escapeHtml(str) {
    if (!str) return '';
    return String(str).replace(/[&<>"']/g, m => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    })[m]);
  }

  function normalizeShortcut(sc, index) {
    if (!sc) return null;
    const id = sc.id || sc.Id || ('sc_' + index);
    let name = sc.name || sc.Name || '';
    let target = sc.target || sc.Target || '';
    const category = sc.category || sc.Category || 'AI Dev Tool';
    const args = sc.arguments || sc.Arguments || '';

    // If target is empty, attempt resolution from toolTargets
    if (!target && state.aiConfig && state.aiConfig.toolTargets) {
      const targets = state.aiConfig.toolTargets;
      if (targets[name]) target = targets[name];
      else if (name.includes('Antigravity') && targets['Antigravity-ide']) target = targets['Antigravity-ide'];
      else if (name.includes('Claude') && targets['Claude']) target = targets['Claude'];
      else if (name.includes('ChatGPT') && targets['ChatGPT']) target = targets['ChatGPT'];
      else if (name.includes('OpenCode') && targets['OpenCode']) target = targets['OpenCode'];
      else if (name.includes('Cline') && targets['Cline']) target = targets['Cline'];
      else if (name.includes('DeepSeek') && targets['DeepSeek']) target = targets['DeepSeek'];
      else if (name.includes('FreeBuff') && targets['FreeBuff']) target = targets['FreeBuff'];
    }

    if (!name) name = 'Shortcut ' + (index + 1);

    return { id, name, target, category, arguments: args };
  }

  function renderDashboardShortcuts() {
    const grid = document.getElementById('dashboard-shortcuts-grid');
    if (!grid) return;
    grid.innerHTML = '';

    const rawShortcuts = state.aiConfig?.shortcuts || [];
    if (rawShortcuts.length === 0) {
      grid.innerHTML = '<div style="color: var(--text-dim); font-size: 12px; grid-column: 1 / -1; padding: 10px 0;">No shortcuts configured yet. Click "+ Add Shortcut" above to add tools, CLIs, or applications.</div>';
      return;
    }

    // Normalize and save back into state
    state.aiConfig.shortcuts = rawShortcuts.map((s, idx) => normalizeShortcut(s, idx));
    const shortcuts = state.aiConfig.shortcuts;

    shortcuts.forEach(sc => {
      const card = document.createElement('div');
      card.className = 'shortcut-card';
      const targetDisplay = state.isShortcutsMasked ? '**' : (sc.target || 'No target configured');
      const targetTooltip = state.isShortcutsMasked ? 'Location hidden (**)' : (sc.target || 'No target configured');
      card.innerHTML = `
        <div class="shortcut-top">
          <div class="shortcut-info">
            <div class="shortcut-name">${escapeHtml(sc.name)}</div>
            <span class="shortcut-cat">${escapeHtml(sc.category || 'AI Dev Tool')}</span>
          </div>
          <div class="shortcut-actions">
            <button class="btn-icon btn-edit-sc" title="Edit Shortcut" data-id="${sc.id}">
              <svg class="icon-svg" style="width: 13px; height: 13px; pointer-events: none;" viewBox="0 0 24 24"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
            </button>
            <button class="btn-icon btn-del-sc" title="Delete Shortcut" data-id="${sc.id}">
              <svg class="icon-svg" style="width: 13px; height: 13px; color: var(--accent-red); pointer-events: none;" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
            </button>
          </div>
        </div>
        <div class="shortcut-target-text" title="${escapeHtml(targetTooltip)}">${escapeHtml(targetDisplay)}</div>
        <button class="btn btn-outline btn-launch-sc" data-id="${sc.id}" style="width: 100%; margin-top: 10px; padding: 6px 12px; font-size: 12px; gap: 6px;">
          <svg class="icon-svg" style="width: 13px; height: 13px; pointer-events: none;" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"/></svg>
          <span>Launch</span>
        </button>
      `;
      grid.appendChild(card);
    });

    const maskShortcutsIcon = document.getElementById('icon-mask-shortcuts');
    if (maskShortcutsIcon) {
      if (state.isShortcutsMasked) {
        maskShortcutsIcon.innerHTML = '<path d="m2 2 20 20"/><path d="M9.88 9.88a3 3 0 1 0 4.24 4.24"/><path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68"/><path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61"/>';
      } else {
        maskShortcutsIcon.innerHTML = '<path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>';
      }
    }

    grid.querySelectorAll('.btn-launch-sc').forEach(b => {
      b.onclick = (e) => {
        e.preventDefault();
        const scId = b.getAttribute('data-id');
        const sc = state.aiConfig.shortcuts.find(s => s.id === scId);
        if (!sc) return;
        if (sc.target.startsWith('cli:')) {
          const cliKey = sc.target.split(':')[1].toLowerCase();
          if (cliKey === 'hermes') callBridge('toggle_cli', { cli: 'Hermes' });
          else if (cliKey === 'claude') callBridge('toggle_cli', { cli: 'ClaudeCode' });
          else if (cliKey === 'antigravity') callBridge('toggle_cli', { cli: 'AntigravityCli' });
          appendLog(`[Shortcuts] Toggling CLI ${sc.name}`);
        } else if (sc.target) {
          callBridge('launch_shortcut', { target: sc.target, arguments: sc.arguments || '' });
          appendLog(`[Shortcuts] Launching ${sc.name}`);
        } else {
          alert(`Shortcut '${sc.name}' has no target path configured. Click the Edit icon to configure its location.`);
        }
      };
    });

    grid.querySelectorAll('.btn-edit-sc').forEach(b => {
      b.onclick = (e) => {
        e.preventDefault();
        const scId = b.getAttribute('data-id');
        const sc = state.aiConfig.shortcuts.find(s => s.id === scId);
        if (sc) {
          openShortcutEditorModal(sc);
        }
      };
    });

    grid.querySelectorAll('.btn-del-sc').forEach(b => {
      b.onclick = async (e) => {
        e.preventDefault();
        const scId = b.getAttribute('data-id');
        const sc = state.aiConfig.shortcuts.find(s => s.id === scId);
        const scName = sc ? sc.name : 'this shortcut';
        if (confirm(`Are you sure you want to delete '${scName}' from your Dashboard shortcuts?`)) {
          state.aiConfig.shortcuts = state.aiConfig.shortcuts.filter(s => s.id !== scId);
          await callBridge('save_shortcuts', { shortcuts: state.aiConfig.shortcuts });
          renderDashboardShortcuts();
        }
      };
    });
  }

  function populatePresetDropdown() {
    const sel = document.getElementById('modal-shortcut-preset');
    if (!sel) return;

    sel.innerHTML = '<option value="">-- Custom Application (Enter manually or browse below) --</option>';

    // Group 1: Configured AI Dev Tools
    const toolTargets = state.aiConfig?.toolTargets || {};
    const devToolsOptgroup = document.createElement('optgroup');
    devToolsOptgroup.label = 'AI Dev Tools (Configured Paths)';

    const knownTools = [
      { key: 'Antigravity-ide', name: 'Antigravity IDE' },
      { key: 'Claude', name: 'Claude Desktop' },
      { key: 'ChatGPT', name: 'ChatGPT Desktop' },
      { key: 'OpenCode', name: 'OpenCode Desktop' },
      { key: 'Cline', name: 'Cline' },
      { key: 'DeepSeek', name: 'DeepSeek' },
      { key: 'FreeBuff', name: 'FreeBuff' }
    ];

    knownTools.forEach(t => {
      const opt = document.createElement('option');
      opt.value = `tool:${t.key}`;
      const configuredPath = toolTargets[t.key] || '';
      opt.textContent = `${t.name} ${configuredPath ? `(${configuredPath})` : '(Not configured)'}`;
      opt.dataset.name = t.name;
      opt.dataset.cat = 'AI Dev Tool';
      opt.dataset.target = configuredPath;
      devToolsOptgroup.appendChild(opt);
    });

    // Check if there are other keys in toolTargets not in knownTools
    Object.keys(toolTargets).forEach(k => {
      if (!knownTools.some(kt => kt.key.toLowerCase() === k.toLowerCase())) {
        const opt = document.createElement('option');
        opt.value = `tool:${k}`;
        opt.textContent = `${k} (${toolTargets[k]})`;
        opt.dataset.name = k;
        opt.dataset.cat = 'AI Dev Tool';
        opt.dataset.target = toolTargets[k];
        devToolsOptgroup.appendChild(opt);
      }
    });

    sel.appendChild(devToolsOptgroup);

    // Group 2: Core Background CLIs
    const cliOptgroup = document.createElement('optgroup');
    cliOptgroup.label = 'Core Background CLIs';

    const clis = [
      { name: 'Hermes CLI', target: 'cli:hermes', cat: 'CLI / Script' },
      { name: 'Claude Code CLI', target: 'cli:claude', cat: 'CLI / Script' },
      { name: 'Antigravity CLI (agy)', target: 'cli:antigravity', cat: 'CLI / Script' }
    ];

    clis.forEach(c => {
      const opt = document.createElement('option');
      opt.value = c.target;
      opt.textContent = `${c.name} (${c.target})`;
      opt.dataset.name = c.name;
      opt.dataset.cat = c.cat;
      opt.dataset.target = c.target;
      cliOptgroup.appendChild(opt);
    });

    sel.appendChild(cliOptgroup);
  }

  function openShortcutEditorModal(shortcut = null) {
    const modal = document.getElementById('modal-shortcut-editor');
    if (!modal) return;
    const title = document.getElementById('modal-shortcut-title');
    const idInp = document.getElementById('modal-shortcut-id');
    const nameInp = document.getElementById('modal-shortcut-name');
    const catInp = document.getElementById('modal-shortcut-cat');
    const targetInp = document.getElementById('modal-shortcut-target');
    const argsInp = document.getElementById('modal-shortcut-args');
    const presetSel = document.getElementById('modal-shortcut-preset');

    populatePresetDropdown();

    if (shortcut) {
      title.textContent = 'Edit Shortcut';
      idInp.value = shortcut.id;
      nameInp.value = shortcut.name || '';
      catInp.value = shortcut.category || 'AI Dev Tool';
      targetInp.value = shortcut.target || '';
      argsInp.value = shortcut.arguments || '';

      // Match preset if target matches
      if (presetSel) {
        presetSel.value = '';
        for (let i = 0; i < presetSel.options.length; i++) {
          const opt = presetSel.options[i];
          if (opt.dataset && opt.dataset.target && opt.dataset.target.toLowerCase() === (shortcut.target || '').toLowerCase()) {
            presetSel.selectedIndex = i;
            break;
          }
        }
      }
    } else {
      title.textContent = 'Add New Shortcut';
      idInp.value = '';
      nameInp.value = '';
      catInp.value = 'AI Dev Tool';
      targetInp.value = '';
      argsInp.value = '';
      if (presetSel) presetSel.selectedIndex = 0;
    }

    modal.classList.add('open');
  }

  function setupShortcutEditor() {
    document.getElementById('btn-add-dashboard-shortcut')?.addEventListener('click', () => {
      openShortcutEditorModal(null);
    });

    document.getElementById('btn-toggle-mask-shortcuts')?.addEventListener('click', async () => {
      state.isShortcutsMasked = !state.isShortcutsMasked;
      localStorage.setItem('agents_hub_mask_shortcuts', state.isShortcutsMasked);
      renderDashboardShortcuts();
      const maskCheck = document.getElementById('setting-mask-paths-checkbox');
      if (maskCheck) maskCheck.checked = state.isShortcutsMasked;
      await callBridge('save_general_settings', {
        settings: { maskShortcutPaths: state.isShortcutsMasked }
      });
    });

    document.getElementById('btn-cancel-shortcut-modal')?.addEventListener('click', () => {
      document.getElementById('modal-shortcut-editor')?.classList.remove('open');
    });

    // Preset dropdown change event
    const presetSel = document.getElementById('modal-shortcut-preset');
    presetSel?.addEventListener('change', () => {
      const selected = presetSel.options[presetSel.selectedIndex];
      if (selected && selected.dataset && selected.value) {
        const nameInp = document.getElementById('modal-shortcut-name');
        const catInp = document.getElementById('modal-shortcut-cat');
        const targetInp = document.getElementById('modal-shortcut-target');
        const argsInp = document.getElementById('modal-shortcut-args');

        if (selected.dataset.name && nameInp) nameInp.value = selected.dataset.name;
        if (selected.dataset.cat && catInp) catInp.value = selected.dataset.cat;
        if (selected.dataset.target && targetInp) targetInp.value = selected.dataset.target;
        if (selected.dataset.args && argsInp) argsInp.value = selected.dataset.args;
      }
    });

    document.getElementById('btn-browse-modal-shortcut')?.addEventListener('click', async () => {
      try {
        const res = await callBridge('browse_shortcut_target');
        if (res && res.path) {
          const inp = document.getElementById('modal-shortcut-target');
          if (inp) inp.value = res.path;
          const nameInp = document.getElementById('modal-shortcut-name');
          if (nameInp && !nameInp.value) {
            const fileName = res.path.split(/[/\\]/).pop().replace(/\.[^/.]+$/, '');
            nameInp.value = fileName;
          }
        }
      } catch { }
    });

    document.getElementById('btn-save-shortcut-modal')?.addEventListener('click', async () => {
      const id = document.getElementById('modal-shortcut-id').value;
      const name = document.getElementById('modal-shortcut-name').value.trim();
      const category = document.getElementById('modal-shortcut-cat').value;
      const target = document.getElementById('modal-shortcut-target').value.trim();
      const args = document.getElementById('modal-shortcut-args').value.trim();

      if (!name || !target) {
        alert('Please specify both a shortcut name and target executable/path.');
        return;
      }

      state.aiConfig.shortcuts = state.aiConfig.shortcuts || [];
      if (id) {
        const item = state.aiConfig.shortcuts.find(s => s.id === id);
        if (item) {
          item.name = name;
          item.category = category;
          item.target = target;
          item.arguments = args;
        } else {
          state.aiConfig.shortcuts.push({ id, name, category, target, arguments: args });
        }
      } else {
        state.aiConfig.shortcuts.push({
          id: 'sc_' + Math.random().toString(36).substring(2, 9),
          name,
          category,
          target,
          arguments: args
        });
      }

      await callBridge('save_shortcuts', { shortcuts: state.aiConfig.shortcuts });
      document.getElementById('modal-shortcut-editor')?.classList.remove('open');
      renderDashboardShortcuts();
    });
  }

  // Public IP & OpenCode Quota Reset Engine
  function renderPublicIpDisplay() {
    const ipText = document.getElementById('dash-public-ip-text');
    const eyeIcon = document.getElementById('icon-ip-eye');
    if (!ipText) return;

    if (state.isIpMasked) {
      ipText.textContent = 'IP: ***';
      if (eyeIcon) {
        eyeIcon.innerHTML = '<path d="m2 2 20 20"/><path d="M9.88 9.88a3 3 0 1 0 4.24 4.24"/><path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68"/><path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61"/>';
      }
    } else {
      ipText.textContent = 'IP: ' + (state.publicIp || 'Fetching...');
      if (eyeIcon) {
        eyeIcon.innerHTML = '<path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>';
      }
    }
  }

  function setupQuotaAndIpWidget() {
    document.getElementById('btn-toggle-mask-ip')?.addEventListener('click', async () => {
      state.isIpMasked = !state.isIpMasked;
      renderPublicIpDisplay();
      await callBridge('save_general_settings', {
        settings: { maskPublicIp: state.isIpMasked }
      });
    });

    document.getElementById('btn-refresh-public-ip')?.addEventListener('click', async () => {
      const ipText = document.getElementById('dash-public-ip-text');
      if (ipText && !state.isIpMasked) ipText.textContent = 'IP: Fetching...';
      try {
        const res = await callBridge('get_public_ip');
        if (res && res.ip) {
          state.publicIp = res.ip;
          renderPublicIpDisplay();
        }
      } catch { }
    });

    document.getElementById('btn-reset-opencode-quota')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-reset-opencode-quota');
      const lbl = document.getElementById('lbl-reset-quota-btn');
      const ipText = document.getElementById('dash-public-ip-text');
      if (btn) btn.disabled = true;
      if (lbl) lbl.textContent = 'Rotating Cloudflare WARP IP...';
      if (ipText && !state.isIpMasked) ipText.textContent = 'IP: Rotating...';

      try {
        const res = await callBridge('rotate_warp_ip');
        if (res) {
          if (res.ip) state.publicIp = res.ip;
          renderPublicIpDisplay();
          appendLog('[QuotaReset] ' + (res.message || 'WARP IP rotated successfully.'));
          if (lbl) lbl.textContent = 'Reset Succeeded!';
          setTimeout(() => { if (lbl) lbl.textContent = 'Reset OpenCode Quota (WARP IP)'; }, 3000);
        }
      } catch (err) {
        appendLog('[QuotaReset] Error: ' + err.message);
        if (lbl) lbl.textContent = 'Reset Failed';
        setTimeout(() => { if (lbl) lbl.textContent = 'Reset OpenCode Quota (WARP IP)'; }, 3000);
      } finally {
        if (btn) btn.disabled = false;
      }
    });
  }

  // AI Providers & Models Setup View
  function renderProvidersList() {
    const listEl = document.getElementById('providers-sidebar-list');
    if (!listEl) return;

    listEl.innerHTML = '';
    const providers = state.aiConfig.providers || [];

    providers.forEach(p => {
      const item = document.createElement('div');
      item.className = 'nav-item ' + (state.selectedProvider && state.selectedProvider.id === p.id ? 'active' : '');
      item.style.padding = '8px 10px';
      item.innerHTML = `
        <span style="color: ${p.isEnabled ? 'var(--accent-green)' : 'var(--text-dim)'}; font-size: 14px;">${p.isEnabled ? '●' : '○'}</span>
        <span style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${p.name}</span>
      `;
      item.onclick = () => selectProvider(p);
      listEl.appendChild(item);
    });

    if (!state.selectedProvider && providers.length > 0) {
      selectProvider(providers[0]);
    }
  }

  function selectProvider(prov) {
    state.selectedProvider = prov;
    renderProvidersList();

    const nameInput = document.getElementById('prov-name-input');
    const enabledCheck = document.getElementById('prov-enabled-checkbox');
    const urlInput = document.getElementById('prov-url-input');
    const keyInput = document.getElementById('prov-key-input');
    const feedback = document.getElementById('prov-save-feedback');

    if (nameInput) nameInput.value = prov.name;
    if (enabledCheck) enabledCheck.checked = prov.isEnabled;
    if (urlInput) urlInput.value = prov.baseUrl;
    if (keyInput) keyInput.value = prov.apiKey;
    if (feedback) feedback.textContent = '';

    renderProviderModels(prov);
  }

  function renderProviderModels(prov) {
    const list = document.getElementById('prov-models-list');
    if (!list) return;

    list.innerHTML = '';
    const models = prov.models || [];

    models.forEach(m => {
      const tag = document.createElement('div');
      tag.style = 'display: flex; justify-content: space-between; align-items: center; padding: 4px 8px; background: rgba(255,255,255,0.03); border-radius: 4px; margin-bottom: 4px; font-size: 12px; font-family: var(--font-mono);';
      tag.innerHTML = `
        <span>${m}</span>
        <button class="btn-icon btn-del-model" data-model="${m}" style="padding: 1px 6px; color: var(--accent-red);">×</button>
      `;
      list.appendChild(tag);
    });

    list.querySelectorAll('.btn-del-model').forEach(b => {
      b.onclick = () => {
        prov.models = prov.models.filter(x => x !== b.dataset.model);
        renderProviderModels(prov);
      };
    });
  }

  function setupProvidersView() {
    document.getElementById('btn-add-model')?.addEventListener('click', () => {
      if (!state.selectedProvider) return;
      const inp = document.getElementById('prov-new-model-input');
      const val = inp.value.trim();
      if (val && !state.selectedProvider.models.includes(val)) {
        state.selectedProvider.models.push(val);
        renderProviderModels(state.selectedProvider);
        inp.value = '';
      }
    });

    document.getElementById('btn-add-provider')?.addEventListener('click', () => {
      const newProv = {
        id: 'prov_' + Math.random().toString(36).substring(2, 9),
        name: 'New Provider',
        baseUrl: 'https://api.openai.com/v1',
        apiKey: '',
        isEnabled: true,
        models: ['gpt-4o-mini']
      };
      state.aiConfig.providers.push(newProv);
      selectProvider(newProv);
    });

    document.getElementById('btn-fetch-models')?.addEventListener('click', async () => {
      if (!state.selectedProvider) return;
      const btn = document.getElementById('btn-fetch-models');
      const feedback = document.getElementById('prov-save-feedback');
      btn.disabled = true;
      feedback.textContent = 'Fetching models from API...';
      feedback.style.color = 'var(--text-muted)';

      try {
        const res = await callBridge('fetch_models', { providerId: state.selectedProvider.id });
        state.selectedProvider.models = res.models || state.selectedProvider.models;
        renderProviderModels(state.selectedProvider);
        feedback.textContent = `Fetched ${res.total} models (+${res.added} new added)!`;
        feedback.style.color = 'var(--accent-green)';
      } catch (err) {
        feedback.textContent = 'Failed: ' + err.message;
        feedback.style.color = 'var(--accent-red)';
      } finally {
        btn.disabled = false;
      }
    });

    document.querySelectorAll('.prov-preset-btn').forEach(b => {
      b.addEventListener('click', () => {
        if (!state.selectedProvider) return;
        const p = b.dataset.preset;
        const presets = {
          'OpenRouter': { url: 'https://openrouter.ai/api/v1', models: ['anthropic/claude-3.5-sonnet', 'deepseek/deepseek-chat', 'openai/gpt-4o-mini'] },
          'OpenAI': { url: 'https://api.openai.com/v1', models: ['gpt-4o', 'gpt-4o-mini', 'o3-mini'] },
          'Groq': { url: 'https://api.groq.com/openai/v1', models: ['llama-3.3-70b-versatile', 'deepseek-r1-distill-llama-70b'] },
          'DeepSeek': { url: 'https://api.deepseek.com/v1', models: ['deepseek-chat', 'deepseek-reasoner'] },
          'Ollama': { url: 'http://localhost:11434/v1', models: ['llama3.2', 'qwen2.5-coder'] }
        };
        if (presets[p]) {
          document.getElementById('prov-name-input').value = p;
          document.getElementById('prov-url-input').value = presets[p].url;
          state.selectedProvider.name = p;
          state.selectedProvider.baseUrl = presets[p].url;
          presets[p].models.forEach(m => {
            if (!state.selectedProvider.models.includes(m)) state.selectedProvider.models.push(m);
          });
          renderProviderModels(state.selectedProvider);
        }
      });
    });

    document.getElementById('btn-save-current-provider')?.addEventListener('click', async () => {
      if (!state.selectedProvider) return;
      const feedback = document.getElementById('prov-save-feedback');

      state.selectedProvider.name = document.getElementById('prov-name-input').value.trim();
      state.selectedProvider.isEnabled = document.getElementById('prov-enabled-checkbox').checked;
      state.selectedProvider.baseUrl = document.getElementById('prov-url-input').value.trim();
      state.selectedProvider.apiKey = document.getElementById('prov-key-input').value.trim();

      await callBridge('save_ai_config', { config: state.aiConfig });
      renderProvidersList();
      renderPromptModelsDropdown();

      feedback.textContent = 'Provider saved successfully!';
      feedback.style.color = 'var(--accent-green)';
      setTimeout(() => feedback.textContent = '', 2500);
    });

    document.getElementById('btn-toggle-key-view')?.addEventListener('click', () => {
      const inp = document.getElementById('prov-key-input');
      inp.type = inp.type === 'password' ? 'text' : 'password';
    });
  }

  // Logging Helper
  function appendLog(text) {
    const logsEl = document.getElementById('logs-container');
    if (!logsEl) return;
    const time = new Date().toLocaleTimeString();
    const line = document.createElement('div');
    line.textContent = `[${time}] ${text}`;
    logsEl.appendChild(line);
    logsEl.scrollTop = logsEl.scrollHeight;
  }

  // Global Actions (Buttons, Modals)
  function setupGlobalActions() {
    document.getElementById('btn-ctrl-hermes')?.addEventListener('click', () => callBridge('toggle_cli', { cli: 'Hermes' }));
    document.getElementById('btn-restart-hermes')?.addEventListener('click', () => callBridge('restart_cli', { cli: 'Hermes' }));

    document.getElementById('btn-ctrl-claude')?.addEventListener('click', () => callBridge('toggle_cli', { cli: 'ClaudeCode' }));
    document.getElementById('btn-restart-claude')?.addEventListener('click', () => callBridge('restart_cli', { cli: 'ClaudeCode' }));

    document.getElementById('btn-ctrl-antigravity')?.addEventListener('click', () => callBridge('toggle_cli', { cli: 'AntigravityCli' }));
    document.getElementById('btn-restart-antigravity')?.addEventListener('click', () => callBridge('restart_cli', { cli: 'AntigravityCli' }));

    document.getElementById('btn-toggle-all-clis')?.addEventListener('click', async () => {
      const anyRunning = Object.values(state.clis).some(c => c.state === 'Running');
      await callBridge('toggle_cli', { cli: 'Hermes' });
      await callBridge('toggle_cli', { cli: 'ClaudeCode' });
      await callBridge('toggle_cli', { cli: 'AntigravityCli' });
    });

    document.getElementById('btn-minimize-tray')?.addEventListener('click', () => callBridge('minimize_to_tray'));
    document.getElementById('btn-refresh-state')?.addEventListener('click', loadInitialState);

    document.getElementById('btn-copy-lan-url')?.addEventListener('click', () => {
      navigator.clipboard.writeText(state.lanUrl);
      appendLog('Copied LAN URL: ' + state.lanUrl);
    });
    document.getElementById('btn-open-lan-url')?.addEventListener('click', () => {
      callBridge('open_url_external', { url: state.lanUrl });
    });
    document.getElementById('btn-copy-lan-tab-url')?.addEventListener('click', () => {
      navigator.clipboard.writeText(state.lanUrl);
    });

    // Modal: Add profile
    const modal = document.getElementById('modal-add-profile');
    const nameInp = document.getElementById('modal-profile-name-input');
    document.getElementById('btn-show-add-profile-modal')?.addEventListener('click', () => {
      nameInp.value = `Account_${(state.antigravity.profiles || []).length + 1}`;
      modal.classList.add('open');
      nameInp.focus();
    });
    document.getElementById('btn-cancel-add-profile')?.addEventListener('click', () => modal.classList.remove('open'));
    document.getElementById('btn-confirm-add-profile')?.addEventListener('click', async () => {
      const name = nameInp.value.trim();
      if (name) {
        await callBridge('create_antigravity_profile', { profileName: name });
        modal.classList.remove('open');
      }
    });

    document.getElementById('btn-clear-logs')?.addEventListener('click', () => {
      const logsEl = document.getElementById('logs-container');
      if (logsEl) logsEl.innerHTML = '<div>[System] Logs cleared.</div>';
    });
  }

  // Load Initial State from C# Backend
  async function loadInitialState() {
    try {
      const res = await callBridge('get_initial_state');
      if (res) {
        state.version = res.version || 'v1.0.3';
        state.lanIp = res.lanIp || '127.0.0.1';
        state.lanUrl = res.lanUrl || 'http://127.0.0.1:9095/';
        state.qrBase64 = res.qrBase64 || '';

        if (res.clis) {
          res.clis.forEach(c => {
            state.clis[c.type] = { state: c.state, port: c.port, pid: c.pid };
          });
        }
        if (res.antigravity) {
          state.antigravity.activeProfile = res.antigravity.activeProfile || '';
          state.antigravity.profiles = res.antigravity.profiles || [];
        }
        if (res.aiConfig) {
          state.aiConfig = res.aiConfig;
        }
        if (res.settings) {
          state.settings = res.settings;
          const autoCheck = document.getElementById('setting-autostart-checkbox');
          const minCheck = document.getElementById('setting-minimized-checkbox');
          const trayCheck = document.getElementById('setting-tray-minimize-checkbox');
          const notifCheck = document.getElementById('setting-notifications-checkbox');
          if (autoCheck) autoCheck.checked = !!res.settings.autostart;
          if (minCheck) minCheck.checked = !!res.settings.startMinimized;
          if (trayCheck) trayCheck.checked = res.settings.minimizeToTray !== false;
          if (notifCheck) notifCheck.checked = res.settings.notifications !== false;
          state.isIpMasked = !!res.settings.maskPublicIp;
          state.isShortcutsMasked = res.settings.maskShortcutPaths !== undefined
            ? !!res.settings.maskShortcutPaths
            : localStorage.getItem('agents_hub_mask_shortcuts') === 'true';
          const maskPathsCheck = document.getElementById('setting-mask-paths-checkbox');
          if (maskPathsCheck) maskPathsCheck.checked = state.isShortcutsMasked;

          // Apply initial theme & accent color
          const activeTheme = localStorage.getItem('agents_hub_theme') || res.settings.theme || 'dark';
          const activeColor = localStorage.getItem('agents_hub_accent_color') || res.settings.accentColor || '#4049B9';
          applyAdaptiveTheme(activeTheme, activeColor);
        }

        // Fetch public IP initially
        callBridge('get_public_ip').then(r => {
          if (r && r.ip) {
            state.publicIp = r.ip;
            renderPublicIpDisplay();
          }
        }).catch(() => {});

        // Update URLs in UI
        const urlEl = document.getElementById('dash-lan-url');
        const lanTabUrl = document.getElementById('lan-full-url');
        if (urlEl) urlEl.textContent = state.lanUrl;
        if (lanTabUrl) lanTabUrl.textContent = state.lanUrl;

        // Render QR Code (Fix: Uses Base64 Image Generated by QrCodeHelper!)
        if (state.qrBase64) {
          const qrImg = document.getElementById('lan-qr-img');
          const qrFallback = document.getElementById('lan-qr-fallback');
          if (qrImg) {
            qrImg.src = state.qrBase64;
            qrImg.style.display = 'block';
          }
          if (qrFallback) qrFallback.style.display = 'none';
        }

        updateHeaderStatus();
        updateDashboardCliRows();
        renderDashboardShortcuts();
        renderPublicIpDisplay();
        renderQuickProfileSelect();
        renderProfiles();
        renderPromptModelsDropdown();
        renderProvidersList();
        renderDevToolsList();
      }
    } catch (err) {
      console.error('Failed to load initial state:', err);
    }
  }

  // Initialize App on DOM Ready
  window.addEventListener('DOMContentLoaded', () => {
    // Load local storage theme immediately to avoid flash
    const savedTheme = localStorage.getItem('agents_hub_theme') || 'dark';
    const savedColor = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
    applyAdaptiveTheme(savedTheme, savedColor);

    setupNavigation();
    initTerminal();
    setupPromptEnhancer();
    setupProvidersView();
    setupSettingsView();
    setupShortcutEditor();
    setupQuotaAndIpWidget();
    setupGlobalActions();
    loadInitialState();
  });

})();
