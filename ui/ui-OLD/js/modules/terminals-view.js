  function normalizeCliKey(key) {
    if (!key) return 'hermes';
    const lk = String(key).toLowerCase();
    if (lk === 'hermes') return 'hermes';
    if (lk === 'claudecode' || lk === 'claude_code' || lk === 'claude') return 'claude_code';
    if (lk === 'antigravitycli' || lk === 'antigravity_cli' || lk === 'agy' || lk.startsWith('antigravity_cli:')) return 'antigravity_cli';
    return lk;
  }

  // reveal: the pane is shown before the terminal is opened. An xterm opened inside a
  // display:none container never measures its cell size, so fit() silently keeps the default
  // 80x24 grid and the shell wraps its output at the wrong width.
  function getTerminalSession(key, reveal = false) {
    if (terminalSessions[key]) return terminalSessions[key];

    const wrapper = document.getElementById('term-panes-wrapper');
    if (!wrapper || typeof Terminal === 'undefined') return null;

    const container = document.createElement('div');
    container.className = 'term-pane-instance' + (reveal ? '' : ' hidden');
    wrapper.appendChild(container);

    const term = new Terminal({
      cursorBlink: true,
      fontFamily: "'Cascadia Code', 'Fira Code', 'Consolas', monospace",
      fontSize: 13,
      scrollback: 5000,
      smoothScrollDuration: 0,
      fastScrollModifier: 'alt',
      fastScrollSensitivity: 5,
      theme: {
        background: '#09090b',
        foreground: '#f4f4f5',
        cursor: '#10b981',
        selectionBackground: 'rgba(16, 185, 129, 0.3)'
      }
    });

    let fitAddon = null;
    if (typeof FitAddon !== 'undefined' && FitAddon.FitAddon) {
      fitAddon = new FitAddon.FitAddon();
      term.loadAddon(fitAddon);
    }

    term.open(container);
    term.onData(input => callBridge('send_terminal_input', { cli: key, input }));

    const session = { key, term, fitAddon, container, initialized: false };
    terminalSessions[key] = session;
    return session;
  }

  // A pane measures zero while hidden, so it is fitted after it becomes visible.
  function fitTerminalSession(session) {
    if (!session || !session.fitAddon) return;
    try {
      session.fitAddon.fit();
      // A fit that lands on the same dimensions repaints nothing, so the pane is redrawn
      // explicitly. Without this a pane created while its view was hidden could stay blank.
      session.term.refresh(0, session.term.rows - 1);
      callBridge('resize_terminal', {
        cli: session.key,
        cols: session.term.cols,
        rows: session.term.rows
      }).catch(() => {});
    } catch { }
  }

  function showTerminalOverlay(show, title, status, badge) {
    const overlay = document.getElementById('terminal-offline-overlay');
    if (!overlay) return;
    overlay.classList.toggle('hidden', !show);
    if (!show) return;

    const titleEl = document.getElementById('terminal-offline-title');
    const statusEl = document.getElementById('terminal-offline-status');
    const badgeEl = document.getElementById('terminal-offline-badge');
    if (titleEl) titleEl.textContent = title;
    if (statusEl) statusEl.textContent = status;
    if (badgeEl) badgeEl.textContent = badge;
  }

  async function activateTerminalTab(cliKey, syncStandalone = true) {
    const key = normalizeCliKey(cliKey);
    state.activeTerminalCli = key;
    try {
      localStorage.setItem('agents_hub_active_cli_tab', key);
    } catch (e) {}

    document.querySelectorAll('#interactive-terminal-tabs .term-tab').forEach(tab => {
      tab.classList.toggle('active', normalizeCliKey(tab.dataset.term) === key);
    });

    // The standalone workspace shows the same sessions, so it follows the dashboard's tab.
    if (syncStandalone) callBridge('sync_terminal_tab', { cli: key }).catch(() => {});

    let info = null;
    try {
      info = await callBridge('get_terminal_session', { cli: key });
    } catch (err) {
      console.warn('Terminal session lookup failed:', err);
    }

    if (!info) {
      showTerminalOverlay(true, 'Session unavailable', 'The backend did not answer for this CLI.', 'Error');
      return;
    }

    const frame = document.getElementById('terminal-web-frame');
    const wrapper = document.getElementById('term-panes-wrapper');

    if (info.isWebUi) {
      Object.values(terminalSessions).forEach(s => s.container.classList.add('hidden'));
      if (wrapper) wrapper.classList.add('hidden');
      if (frame) {
        frame.classList.remove('hidden');
        const target = info.isRunning ? (info.webUrl || 'http://127.0.0.1:9119/') : 'hermes-offline.html';
        if (frame.getAttribute('src') !== target) frame.setAttribute('src', target);
      }
      showTerminalOverlay(!info.isRunning, 'Hermes is not running', info.status || 'Start it from the Dashboard to load this page.', 'Offline');
      return;
    }

    if (frame) {
      frame.classList.add('hidden');
      frame.removeAttribute('src');
    }
    if (wrapper) wrapper.classList.remove('hidden');

    const session = getTerminalSession(key, true);
    if (!session) return;

    Object.values(terminalSessions).forEach(s => s.container.classList.toggle('hidden', s.key !== key));

    if (!session.initialized) {
      session.initialized = true;
      if (info.history) session.term.write(info.history);
      else session.term.writeln(`\x1b[32m[${info.displayName || TERMINAL_TAB_LABELS[key]} session]\x1b[0m\r\n`);
    }

    const label = info.displayName || TERMINAL_TAB_LABELS[key];
    if (!info.isRunning) {
      showTerminalOverlay(true, `${label} is not running`, info.status || 'The process is currently stopped.', 'Offline');
      return;
    }

    showTerminalOverlay(false);
    // The cell size is measured on the first rendered frame after the pane becomes visible, so
    // the fit waits for it and repeats once more for the layout pass that follows.
    requestAnimationFrame(() => fitTerminalSession(session));
    setTimeout(() => {
      fitTerminalSession(session);
      session.term.focus();
    }, 50);
  }

  function initTerminal() {
    if (typeof Terminal === 'undefined') return;
    if (!document.getElementById('terminal-host')) return;

    document.querySelectorAll('#interactive-terminal-tabs .term-tab').forEach(tab => {
      tab.addEventListener('click', () => activateTerminalTab(tab.dataset.term, true));
    });

    window.addEventListener('resize', () => {
      if (state.activeView !== 'terminals') return;
      fitTerminalSession(terminalSessions[normalizeCliKey(state.activeTerminalCli)]);
    });

    document.getElementById('btn-clear-terminal')?.addEventListener('click', () => {
      const key = normalizeCliKey(state.activeTerminalCli);
      const session = terminalSessions[key];
      if (session) session.term.clear();
      callBridge('clear_terminal_history', { cli: key }).catch(() => {});
    });

    document.getElementById('btn-term-open-browser')?.addEventListener('click', async () => {
      const key = normalizeCliKey(state.activeTerminalCli);
      let url = '';
      try {
        const info = await callBridge('get_terminal_session', { cli: key });
        url = (info && (info.webUrl || (info.port ? `http://127.0.0.1:${info.port}/` : ''))) || '';
      } catch { }
      if (!url) url = key === 'claude_code' ? 'http://127.0.0.1:9229/' : 'http://127.0.0.1:9339/';
      const sep = url.includes('?') ? '&' : '?';
      callBridge('open_url_external', { url: `${url}${sep}${currentThemeParam().replace('?', '')}` });
    });

    document.getElementById('btn-terminal-start-cli')?.addEventListener('click', async (e) => {
      const btn = e.currentTarget;
      btn.disabled = true;
      try {
        await callBridge('toggle_cli', { cli: normalizeCliKey(state.activeTerminalCli) });
        setTimeout(() => activateTerminalTab(state.activeTerminalCli, false), 600);
      } catch (err) {
        showToast('Failed to start CLI: ' + err.message, 'error', 4500);
      } finally {
        btn.disabled = false;
      }
    });

    try {
      const savedCliTab = localStorage.getItem('agents_hub_active_cli_tab');
      if (savedCliTab) {
        state.activeTerminalCli = normalizeCliKey(savedCliTab);
      }
    } catch (e) {}

    if (state.activeTerminalCli) {
      activateTerminalTab(state.activeTerminalCli, false);
    }
  }

  function renderTerminalPresetState(activePreset, enabledClis) {
    // An unknown preset means the enabled tabs are a hand-toggled mix, so nothing is highlighted.
    const active = TERMINAL_PRESET_LABELS[activePreset] ? activePreset : null;

    document.querySelectorAll('#terminal-presets-group .preset-btn').forEach(btn => {
      const isActive = active !== null && btn.dataset.preset === active;
      btn.classList.toggle('active', isActive);
      btn.classList.toggle('btn-success', isActive);
      btn.classList.toggle('btn-outline', !isActive);
    });

    const label = document.getElementById('preset-active-label');
    if (label) label.textContent = `Active: ${active ? TERMINAL_PRESET_LABELS[active] : 'Custom'}`;

    if (Array.isArray(enabledClis)) {
      document.querySelectorAll('#interactive-terminal-tabs .term-tab').forEach(tab => {
        const termKey = normalizeCliKey(tab.dataset.term);
        const isEnabled = enabledClis.includes(termKey);
        tab.style.display = isEnabled ? '' : 'none';
      });

      const currentNorm = normalizeCliKey(state.activeTerminalCli);
      if (!enabledClis.includes(currentNorm)) {
        const nextCli = enabledClis.length > 0 ? enabledClis[0] : '';
        if (nextCli) {
          activateTerminalTab(nextCli, false);
        }
      }
    }
  }

  async function loadTerminalPreset() {
    try {
      const res = await callBridge('get_terminal_preset');
      renderTerminalPresetState(res && res.preset, res && res.enabledClis);
    } catch (err) {
      console.warn('Failed to load terminal workspace preset:', err);
    }
  }

  function setupTerminalPresets() {
    const group = document.getElementById('terminal-presets-group');
    if (!group) return;

    group.querySelectorAll('.preset-btn').forEach(btn => {
      btn.addEventListener('click', async () => {
        const preset = btn.dataset.preset;
        if (!preset) return;

        btn.disabled = true;
        try {
          const res = await callBridge('apply_terminal_preset', { preset });
          const applied = (res && res.preset) || preset;
          renderTerminalPresetState(applied, res && res.enabledClis);
          showToast(`Applied preset: ${TERMINAL_PRESET_LABELS[applied] || TERMINAL_PRESET_LABELS[preset]}`, 'success', 3000);
        } catch (err) {
          showToast('Failed to apply preset: ' + err.message, 'error', 4500);
        } finally {
          btn.disabled = false;
        }
      });
    });

    loadTerminalPreset();
  }

  // Prompt Enhancer - Preset & System Instruction Helpers
window.setupTerminalPresets = setupTerminalPresets;
window.activateTerminalTab = activateTerminalTab;
window.getTerminalSession = getTerminalSession;
window.fitTerminalSession = fitTerminalSession;

window.normalizeCliKey = normalizeCliKey;
var normalizeCliKey = window.normalizeCliKey;
window.renderTerminalPresetState = renderTerminalPresetState;
var renderTerminalPresetState = window.renderTerminalPresetState;
