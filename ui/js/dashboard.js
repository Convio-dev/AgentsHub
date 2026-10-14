// Agents Hub - Modern WebView2 Dashboard Front-End Engine
(function () {
  'use strict';

  // Global Client State
  const state = {
    version: '',
    lanIp: '127.0.0.1',
    lanUrl: 'http://127.0.0.1:9095/',
    lanProxyPort: 9191,
    lanProxyEnabled: true,
    dashUrlMode: 'local',
    lanUrlMode: 'lan',
    isIpMasked: false,
    isShortcutsMasked: localStorage.getItem('agents_hub_mask_shortcuts') === 'true',
    publicIp: '',
    ipv4: '',
    ipv6: '',
    previousIpv4: '',
    previousIpv6: '',
    countryCode: '',
    country: '',
    countryEmoji: '',
    city: '',
    vpnProvider: (localStorage.getItem('agents_hub_enable_proton') === 'true' && localStorage.getItem('agents_hub_vpn_provider') === 'proton') ? 'proton' : 'warp',
    isDirectIsp: false,
    qrBase64: '',
    clis: {},
    antigravity: { activeProfile: '', profiles: [] },
    opencode: { isOnline: false, port: 0, activeSessions: [] },
    aiConfig: { providers: [], toolTargets: {}, devTools: [], shortcuts: [], selectedProviderId: null, selectedModel: null, promptPresets: [], builtInPromptPresets: [], activePromptPresetId: 'preset_general', promptEnhancerTemperature: 0.6 },
    settings: { autostart: false, startMinimized: false, minimizeToTray: true, notifications: true, maskPublicIp: false, maskShortcutPaths: false, enableProtonVpnProvider: localStorage.getItem('agents_hub_enable_proton') === 'true', enableHardwareMonitor: localStorage.getItem('agents_hub_enable_hardware_monitor') === 'true', theme: 'dark', accentColor: '#4049B9' },
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
        if (msg.success) {
          resolve(msg.data);
        } else {
          let errText = 'Action failed';
          if (typeof msg.data === 'string') {
            errText = msg.data;
          } else if (msg.data && typeof msg.data === 'object') {
            errText = msg.data.message || msg.data.error || JSON.stringify(msg.data);
          }
          const err = new Error(errText);
          err.data = msg.data;
          reject(err);
        }
        return;
      }

      // Handle OpenCode live push events
      if (msg.type === 'OPENCODE_UPDATE' && msg.data) {
        handleOpenCodeUpdate(msg.data);
      }
      if (msg.type === 'OPENCODE_INTERACTIVE_PROMPT') {
        handleOpenCodePrompt(msg.payload || msg.prompt || msg.data);
      }

      // Handle Push Events
      if (msg.event) {
        handleBackendEvent(msg.event, msg.payload);
      }
    });
  }

  function handleBackendEvent(eventType, payload) {
    // FIX: Do NOT flood logs with hardware metrics, stream chunks, or internal updates!
    const suppressedEvents = [
      'hardware_metrics', 
      'terminal_data', 
      'live_log_entry', 
      'opencode_status_changed', 
      'opencode_sessions_updated',
      'opencode_interactive_prompt'
    ];
    if (!suppressedEvents.includes(eventType)) {
      appendLog(`[${eventType}] ${JSON.stringify(payload)}`);
    }

    switch (eventType) {
      case 'opencode_status_changed':
      case 'opencode_sessions_updated':
        if (payload) {
          handleOpenCodeUpdate(payload);
        }
        break;

      case 'opencode_interactive_prompt':
        if (payload) {
          handleOpenCodePrompt(payload);
        }
        break;

      case 'live_log_entry':
        if (payload) {
          appendLog(payload.text || payload.message, payload.tag, payload.timestamp);
        }
        break;

      case 'quota_reset_progress':
        if (payload && payload.line) {
          appendLog(payload.line, 'WARP');
        }
        break;

      case 'dependency_install_progress':
        if (payload && payload.id && payload.line) {
          const lines = dependencyState.logs[payload.id] || (dependencyState.logs[payload.id] = []);
          lines.push(payload.line);
          if (lines.length > 200) lines.shift();
          renderDependencies();
        }
        break;

      case 'dependency_install_all_progress':
        if (payload && payload.name) {
          dependencyInstallAllProgress = 'Installing ' + ((payload.index || 0) + 1) + '/' + (payload.total || '?');
          dependencyInstallAllProgress = dependencyInstallAllProgress + ': ' + payload.name;
          updateInstallAllButton();
        }
        break;

      case 'dependency_install_all_complete':
        {
          dependencyInstallAllRunning = false;
          dependencyInstallAllProgress = '';
          updateInstallAllButton();

          const ok = (payload && payload.succeeded) || [];
          const failed = (payload && payload.failed) || [];
          const parts = [];
          if (ok.length > 0) parts.push('installed ' + ok.join(', '));
          if (failed.length > 0) parts.push('failed: ' + failed.join(', '));
          showToast(parts.length > 0 ? 'Batch install finished. ' + parts.join('; ') + '.'
            : 'The batch install finished with nothing to report.', failed.length > 0 ? 'warning' : 'success', 7000);
          appendLog('Batch install finished: ' + (parts.join('; ') || 'nothing to do'));
          loadDependencies(true);
        }
        break;

      case 'dependency_install_complete':
        if (payload && payload.id) {
          if (payload.dependency) {
            const index = dependencyState.items.findIndex(d => d.id === payload.id);
            if (index >= 0) dependencyState.items[index] = payload.dependency;
            else dependencyState.items.push(payload.dependency);
          }

          dependencyState.busy[payload.id] = payload.success ? 'done' : 'failed';
          renderDependencies();

          showToast(payload.message || (payload.success ? 'Install finished.' : 'Install failed.'),
            payload.success ? 'success' : 'error', 6000);

          // Let the transient Done/Failed badge decay back to the detected state.
          setTimeout(() => {
            delete dependencyState.busy[payload.id];
            renderDependencies();
          }, 4000);
        }
        break;

      case 'cli_status_changed':
        if (payload && payload.cli) {
          state.clis[payload.cli] = { state: payload.state, port: payload.port, pid: payload.pid };
          updateHeaderStatus();
          updateDashboardCliRows();
          renderRunningPorts();
        }
        break;

      case 'lan_proxy_state_changed':
        if (payload) {
          state.lanProxyEnabled = !!payload.lanProxyEnabled;
          if (payload.runningPorts) state.runningPorts = payload.runningPorts;
          if (!state.lanProxyEnabled) {
            state.dashUrlMode = 'local';
            state.lanUrlMode = 'local';
          }
          renderRunningPorts();
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

      case 'antigravity_instance_status_changed':
        if (payload) {
          if (state.antigravity && state.antigravity.profiles) {
            const p = state.antigravity.profiles.find(x => (typeof x === 'object' ? x.name : x).toLowerCase() === (payload.profile || '').toLowerCase());
            if (p && typeof p === 'object') {
              p.isRunning = !!payload.isRunning;
              p.port = payload.port || null;
            }
          }
          renderProfiles();
          renderQuickProfileSelect();
          renderRunningPorts();
          updateHeaderStatus();
        }
        break;

      case 'running_ports_updated':
        if (payload && payload.runningPorts) {
          state.runningPorts = payload.runningPorts;
          renderRunningPorts();
        }
        break;

      case 'lan_clients_changed':
        {
          const clients = payload && payload.clients ? payload.clients : [];
          lanSnapshot = Object.assign({}, lanSnapshot || {}, { clients });
          renderLanClients(clients);
        }
        break;

      case 'lan_state_changed':
        if (payload && payload.bindError) {
          appendLog('LAN access could not bind: ' + payload.bindError);
        }
        loadLanState(true);
        break;

      case 'hardware_metrics':
        if (payload) {
          handleHardwareMetrics(payload);
        }
        break;

      case 'ai_config_changed':
        if (payload) {
          state.aiConfig = payload;
          renderPromptModelsDropdown();
          renderPromptPresets();
          renderProvidersList();
          renderDevToolsList();
        }
        break;
    }
  }

  // ==========================================
  // Real-Time Hardware & System Telemetry Engine
  // ==========================================
  const TELEMETRY_HISTORY_LENGTH = 20;
  const telemetryState = {
    ramHistory: [],
    pollTimer: null,
    isPaused: false
  };

  function renderSmoothSparkline(lineEl, areaEl, dotEl, dataPoints, width = 240, height = 56, maxVal = 100) {
    if (!lineEl || !areaEl || !dataPoints || dataPoints.length === 0) return;

    const len = dataPoints.length;
    const paddingY = 4;
    const usableHeight = height - paddingY * 2;

    if (len === 1) {
      const val = Math.max(0, Math.min(maxVal, dataPoints[0]));
      const y = (height - paddingY) - (val / maxVal) * usableHeight;
      lineEl.setAttribute('d', `M 0,${y.toFixed(1)} L ${width},${y.toFixed(1)}`);
      areaEl.setAttribute('d', `M 0,${y.toFixed(1)} L ${width},${y.toFixed(1)} L ${width},${height} L 0,${height} Z`);
      if (dotEl) {
        dotEl.setAttribute('cx', width);
        dotEl.setAttribute('cy', y.toFixed(1));
        dotEl.style.display = 'block';
      }
      return;
    }

    const stepX = width / (len - 1);
    const pts = dataPoints.map((val, idx) => {
      const clamped = Math.max(0, Math.min(maxVal, val));
      const y = (height - paddingY) - (clamped / maxVal) * usableHeight;
      return { x: idx * stepX, y };
    });

    let lineD = `M ${pts[0].x.toFixed(1)},${pts[0].y.toFixed(1)}`;
    for (let i = 0; i < pts.length - 1; i++) {
      const p0 = pts[Math.max(0, i - 1)];
      const p1 = pts[i];
      const p2 = pts[i + 1];
      const p3 = pts[Math.min(pts.length - 1, i + 2)];

      const cp1x = p1.x + (p2.x - p0.x) / 6;
      const cp1y = p1.y + (p2.y - p0.y) / 6;
      const cp2x = p2.x - (p3.x - p1.x) / 6;
      const cp2y = p2.y - (p3.y - p1.y) / 6;

      lineD += ` C ${cp1x.toFixed(1)},${cp1y.toFixed(1)} ${cp2x.toFixed(1)},${cp2y.toFixed(1)} ${p2.x.toFixed(1)},${p2.y.toFixed(1)}`;
    }

    const lastPt = pts[pts.length - 1];
    const areaD = `${lineD} L ${width},${height} L 0,${height} Z`;

    lineEl.setAttribute('d', lineD);
    areaEl.setAttribute('d', areaD);

    if (dotEl) {
      dotEl.setAttribute('cx', lastPt.x.toFixed(1));
      dotEl.setAttribute('cy', lastPt.y.toFixed(1));
      dotEl.style.display = 'block';
    }
  }

  function formatNetworkRate(kbps) {
    const value = Number.isFinite(kbps) && kbps > 0 ? kbps : 0;
    if (value >= 1024) return `${(value / 1024).toFixed(1)} MB/s`;
    return `${Math.round(value)} KB/s`;
  }

  function handleHardwareMetrics(payload) {
    if (!payload) return;

    const netKbps = typeof payload.netKbps === 'number' && Number.isFinite(payload.netKbps)
      ? Math.max(0, payload.netKbps)
      : 0;

    const ramMb = typeof payload.ramMb === 'number' ? payload.ramMb : 0;
    const totalRamMb = typeof payload.totalRamMb === 'number' ? payload.totalRamMb : 16384;
    const hermesMemMb = typeof payload.hermesMemMb === 'number' ? payload.hermesMemMb : 0;

    // Header bar badges
    const netEl = document.getElementById('metric-net');
    const ramEl = document.getElementById('metric-ram');
    if (netEl) netEl.textContent = `NET ${formatNetworkRate(netKbps)}`;
    if (ramEl) ramEl.textContent = `RAM ${ramMb} MB`;

    // Ring buffer (last 20 points)
    const ramPercent = totalRamMb > 0 ? (ramMb / totalRamMb) * 100 : 0;
    telemetryState.ramHistory.push(ramPercent);
    if (telemetryState.ramHistory.length > TELEMETRY_HISTORY_LENGTH) {
      telemetryState.ramHistory.shift();
    }

    // Update telemetry card numeric displays
    const netLoadVal = document.getElementById('net-load-val');
    if (netLoadVal) netLoadVal.textContent = formatNetworkRate(netKbps);

    const ramUsageVal = document.getElementById('ram-usage-val');
    const usedGb = (ramMb / 1024).toFixed(1);
    const totalGb = (totalRamMb / 1024).toFixed(1);
    if (ramUsageVal) ramUsageVal.textContent = `${usedGb} GB (${Math.round(ramPercent)}%)`;

    const ramStatsSub = document.getElementById('ram-stats-sub');
    if (ramStatsSub) ramStatsSub.textContent = `Total: ${totalGb} GB`;

    const availMb = Math.max(0, totalRamMb - ramMb);
    const availGb = (availMb / 1024).toFixed(1);
    const ramAvailSub = document.getElementById('ram-avail-sub');
    if (ramAvailSub) ramAvailSub.textContent = `Avail: ${availGb} GB`;

    const hermesMemVal = document.getElementById('hermes-mem-val');
    if (hermesMemVal) hermesMemVal.textContent = `${hermesMemMb} MB`;

    const sysStatusVal = document.getElementById('sys-status-val');
    if (sysStatusVal) {
      if (ramPercent > 90) {
        sysStatusVal.textContent = 'High Load';
        sysStatusVal.style.color = 'var(--accent-red)';
      } else {
        sysStatusVal.textContent = 'Active';
        sysStatusVal.style.color = 'var(--accent-green)';
      }
    }

    // Smooth Bézier sparkline
    const ramLine = document.getElementById('ram-sparkline-line');
    const ramArea = document.getElementById('ram-sparkline-area');
    const ramDot = document.getElementById('ram-sparkline-dot');
    renderSmoothSparkline(ramLine, ramArea, ramDot, telemetryState.ramHistory, 240, 56, 100);
  }

  function updateHardwareMonitorVisibility() {
    const isEnabled = state.settings.enableHardwareMonitor === true;
    const card = document.getElementById('hardware-monitor-card');
    if (card) {
      card.style.display = isEnabled ? 'flex' : 'none';
    }
    if (!isEnabled) {
      pauseHardwareTelemetry();
    } else if (state.activeView === 'dashboard' && !document.hidden) {
      resumeHardwareTelemetry();
    }
  }

  function pollHardwareTelemetry() {
    if (!state.settings.enableHardwareMonitor || telemetryState.isPaused || state.activeView !== 'dashboard') return;
    callBridge('get_system_telemetry').then(data => {
      if (data) handleHardwareMetrics(data);
    }).catch(() => {});
  }

  function pauseHardwareTelemetry() {
    telemetryState.isPaused = true;
    const badge = document.getElementById('sys-telemetry-refresh-badge');
    if (badge) badge.classList.add('is-paused');
  }

  function resumeHardwareTelemetry() {
    if (!state.settings.enableHardwareMonitor) return;
    telemetryState.isPaused = false;
    const badge = document.getElementById('sys-telemetry-refresh-badge');
    if (badge) badge.classList.remove('is-paused');
    pollHardwareTelemetry();
  }

  function setupHardwareTelemetry() {
    updateHardwareMonitorVisibility();

    if (telemetryState.ramHistory.length === 0) {
      for (let i = 0; i < 6; i++) {
        telemetryState.ramHistory.push(0);
      }
    }

    if (state.settings.enableHardwareMonitor) {
      callBridge('get_system_telemetry').then(data => {
        if (data) handleHardwareMetrics(data);
      }).catch(() => {});
    }

    if (telemetryState.pollTimer) clearInterval(telemetryState.pollTimer);
    telemetryState.pollTimer = setInterval(pollHardwareTelemetry, 2000);

    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        pauseHardwareTelemetry();
      } else if (state.activeView === 'dashboard' && state.settings.enableHardwareMonitor) {
        resumeHardwareTelemetry();
      }
    });
  }

  // Navigation Logic & Collapsible Sidebar
  // ---- Command palette (Ctrl+K) ----

  const cpState = { open: false, query: '', items: [], selected: 0 };

  // Views are read from the sidebar rather than a second list, so the palette can never
  // disagree with the nav about what exists.
  function cpViewCommands() {
    const out = [];
    document.querySelectorAll('.nav-item').forEach(nav => {
      const view = nav.dataset.view;
      const label = (nav.querySelector('.nav-text') || {}).textContent || view;
      if (view) out.push({ group: 'Go to', label: 'Open ' + label, hint: view, run: () => switchView(view), search: (label + ' ' + view).toLowerCase() });
    });
    return out;
  }

  function cpActionCommands() {
    const out = [];
    const add = (label, hint, run) => out.push({ group: 'Action', label, hint, run, search: (label + ' ' + (hint || '')).toLowerCase() });

    add('Toggle LAN access', 'Start or stop sharing these services', () => {
      document.getElementById('btn-toggle-lan-network')?.click();
    });
    add('Copy the LAN link', 'Put the current link on the clipboard', () => {
      document.getElementById('btn-copy-lan-tab-url')?.click();
    });
    add('Open the LAN link in a browser', 'Test the link on this machine', () => {
      document.getElementById('btn-open-lan-tab-url')?.click();
    });
    add('Recheck the firewall and ports', 'Re-read the LAN state', () => loadLanState(true));
    add('Refresh everything', 'Re-read the state from the backend', () => loadInitialState());
    add('Recheck dependencies', 'Scan for installed runtimes', () => loadDependencies(true));
    add('Install all missing dependencies', 'Run every missing installer', () => installAllDependencies());
    add('Find installed AI clients', 'Scan the usual install folders', () => {
      switchView('devtools');
      findInstalledClients();
    });
    add('Add an AI dev tool', 'Register an external executable', () => {
      switchView('devtools');
      openDevToolModal(null);
    });
    add('Open standalone terminals', 'Detach the CLI workspace', () => {
      document.getElementById('btn-open-standalone-hdr')?.click();
    });
    add('Switch theme', 'Light or dark', () => {
      const next = (document.documentElement.getAttribute('data-theme') || 'dark') === 'dark' ? 'light' : 'dark';
      applyAdaptiveTheme(next, localStorage.getItem('agents_hub_accent_color') || '#10b981');
      callBridge('save_general_settings', { settings: { theme: next } }).catch(() => {});
    });
    add('Show notification history', 'Read a notification you missed', () => toggleToastHistory(true));
    add('Clear notification history', 'Empty the list', () => clearToastHistory());
    return out;
  }

  function cpToolCommands() {
    const out = [];
    (state.aiConfig && Array.isArray(state.aiConfig.devTools) ? state.aiConfig.devTools : []).forEach(t => {
      out.push({
        group: 'Launch tool',
        label: 'Launch ' + t.name,
        hint: t.target,
        run: async () => {
          switchView('devtools');
          try {
            const res = await callBridge('launch_tool', { toolId: t.id, toolName: t.name, target: t.target });
            showToast((res && res.message) || (t.name + ' started.'), 'success', 3500);
          } catch (err) {
            showToast('Could not start ' + t.name + '.', 'error', 5000);
          }
        },
        search: (t.name + ' ' + (t.target || '') + ' launch').toLowerCase()
      });
    });

    // Antigravity profiles are what people switch between most, so they get a row too.
    (state.antigravity && Array.isArray(state.antigravity.profiles) ? state.antigravity.profiles : []).forEach(p => {
      out.push({
        group: 'Switch profile',
        label: 'Switch to Antigravity profile ' + p,
        hint: 'The active account and its tab',
        run: async () => {
          try {
            await callBridge('switch_antigravity_profile', { profileName: p });
            loadInitialState();
          } catch (err) {
            showToast('Could not switch to ' + p + '.', 'error', 5000);
          }
        },
        search: ('profile ' + p + ' antigravity account switch').toLowerCase()
      });
    });

    return out;
  }

  function cpAllCommands() {
    return [].concat(cpViewCommands(), cpActionCommands(), cpToolCommands());
  }

  function cpRender() {
    const list = document.getElementById('cp-list');
    if (!list) return;

    const q = cpState.query.trim().toLowerCase();
    const all = cpAllCommands();
    cpState.items = q ? all.filter(c => c.search.indexOf(q) >= 0) : all;

    if (cpState.selected >= cpState.items.length) cpState.selected = cpState.items.length - 1;
    if (cpState.selected < 0) cpState.selected = 0;

    list.innerHTML = '';

    if (cpState.items.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'cp-empty';
      empty.textContent = 'Nothing matches "' + cpState.query + '".';
      list.appendChild(empty);
      return;
    }

    let lastGroup = null;
    cpState.items.forEach((item, index) => {
      if (item.group !== lastGroup) {
        lastGroup = item.group;
        const header = document.createElement('div');
        header.className = 'cp-group';
        header.textContent = item.group;
        list.appendChild(header);
      }

      const row = document.createElement('div');
      row.className = 'cp-item' + (index === cpState.selected ? ' is-selected' : '');
      row.setAttribute('role', 'option');
      row.setAttribute('aria-selected', index === cpState.selected ? 'true' : 'false');

      const label = document.createElement('span');
      label.className = 'cp-item-label';
      label.textContent = item.label;

      const hint = document.createElement('span');
      hint.className = 'cp-item-hint';
      hint.textContent = item.hint || '';

      row.appendChild(label);
      row.appendChild(hint);
      row.addEventListener('click', () => {
        cpState.selected = index;
        cpRunSelected();
      });
      row.addEventListener('mousemove', () => {
        if (cpState.selected !== index) {
          cpState.selected = index;
          cpRender();
        }
      });
      list.appendChild(row);
    });
  }

  function cpOpen() {
    cpState.open = true;
    cpState.query = '';
    cpState.selected = 0;

    const overlay = document.getElementById('cp-overlay');
    const input = document.getElementById('cp-input');
    if (overlay) overlay.style.display = '';
    if (input) {
      input.value = '';
      setTimeout(() => input.focus(), 30);
    }
    cpRender();
  }

  function cpClose() {
    cpState.open = false;
    const overlay = document.getElementById('cp-overlay');
    if (overlay) overlay.style.display = 'none';
  }

  async function cpRunSelected() {
    const item = cpState.items[cpState.selected];
    if (!item) return;
    cpClose();
    try {
      await item.run();
    } catch (err) {
      showToast('That command failed: ' + (err && err.message ? err.message : err), 'error', 5000);
    }
  }

  function setupCommandPalette() {
    const input = document.getElementById('cp-input');
    const overlay = document.getElementById('cp-overlay');

    input?.addEventListener('input', () => {
      cpState.query = input.value;
      cpState.selected = 0;
      cpRender();
    });

    input?.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        cpState.selected = Math.min(cpState.selected + 1, cpState.items.length - 1);
        cpRender();
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        cpState.selected = Math.max(cpState.selected - 1, 0);
        cpRender();
      } else if (e.key === 'Enter') {
        e.preventDefault();
        cpRunSelected();
      } else if (e.key === 'Escape') {
        e.preventDefault();
        cpClose();
      }

      const selected = document.querySelector('.cp-item.is-selected');
      if (selected) selected.scrollIntoView({ block: 'nearest' });
    });

    overlay?.addEventListener('click', (e) => {
      if (e.target === overlay) cpClose();
    });

    window.addEventListener('keydown', (e) => {
      if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
        e.preventDefault();
        if (cpState.open) cpClose();
        else cpOpen();
      } else if (e.key === 'Escape' && cpState.open) {
        cpClose();
      }
    });
  }

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

    if (viewName === 'dashboard') {
      resumeHardwareTelemetry();
    } else {
      pauseHardwareTelemetry();
    }

    if (viewName === 'devtools') {
      // The health check is local reads only, so it is re-run whenever the view opens.
      loadDevTools();
    }

    if (viewName === 'terminals') {
      setTimeout(() => {
        if (fitAddonInstance) fitAddonInstance.fit();
      }, 50);
    }

    // Dependency detection spawns version probes, so only scan the first time Settings opens.
    if (viewName === 'settings' && !dependencyState.loaded && !dependencyState.loading) {
      loadDependencies(false);
    }

    // The firewall probe spawns PowerShell, so the LAN page reads its state on first open
    // rather than at startup. Recheck afterwards.
    if (viewName === 'lan' && !lanLoading) {
      loadLanState(false);
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

  // CLI Dependencies & Runtimes (Settings view)
  const dependencyState = {
    items: [],
    busy: {},
    logs: {},
    loaded: false,
    loading: false
  };

  const DEPENDENCY_ICONS = {
    node: '<path d="M12 2 3 7v10l9 5 9-5V7Z"/><path d="M12 8v8"/>',
    git: '<circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M18 9a9 9 0 0 1-9 9"/><circle cx="18" cy="6" r="3"/>',
    python: '<polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/>',
    claude: '<polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/>',
    antigravity: '<path d="m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3Z"/>',
    hermes: '<path d="m12 2 9 5v10l-9 5-9-5V7Z"/><path d="m3 7 9 5 9-5"/><path d="M12 22V12"/>',
    warp: '<path d="M17.5 19a4.5 4.5 0 0 0 .5-8.97A6 6 0 0 0 6.2 9.4 4.5 4.5 0 0 0 7 19Z"/>',
    proton: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z"/>'
  };

  function dependencyBadge(item, busy) {
    if (busy === 'running') return { cls: 'dep-badge-busy', text: 'Installing' };
    if (busy === 'done') return { cls: 'dep-badge-ok', text: 'Done' };
    if (busy === 'failed') return { cls: 'dep-badge-failed', text: 'Failed' };
    if (item.isInstalled) return { cls: 'dep-badge-ok', text: 'Installed' };
    return { cls: 'dep-badge-missing', text: 'Not Installed' };
  }

  function dependencyDetailText(item, busy) {
    if (busy === 'running') return 'Installing in the background - output streams below.';
    if (busy === 'failed') return 'Last install attempt failed. Check the output below.';

    if (item.isInstalled) {
      const parts = [];
      if (item.version) parts.push(item.version);
      if (item.path) parts.push(item.path);
      return parts.length ? parts.join('  -  ') : 'Detected on PATH';
    }

    return `Command: ${item.commandPreview || 'not available'}`;
  }

  function dependencyActionLabel(item, busy) {
    if (busy === 'running') return 'Installing...';

    if (item.installKind === 'browser') return item.isInstalled ? 'Verify' : 'Open Download Page';
    if (item.isInstalled) return item.installKind === 'winget' ? 'Update via Winget' : 'Reinstall';
    return item.installKind === 'winget' ? 'Install via Winget' : 'Run PowerShell Installer';
  }

  function buildDependencyRow(item) {
    const busy = dependencyState.busy[item.id];
    const badge = dependencyBadge(item, busy);

    const row = document.createElement('div');
    row.className = 'dependency-row';

    const icon = document.createElement('div');
    icon.className = 'dependency-icon';
    icon.innerHTML = `<svg class="icon-svg" style="width: 17px; height: 17px;" viewBox="0 0 24 24">${DEPENDENCY_ICONS[item.id] || DEPENDENCY_ICONS.hermes}</svg>`;

    const info = document.createElement('div');
    info.className = 'dependency-info';

    const titleLine = document.createElement('div');
    titleLine.className = 'dependency-title-line';

    const name = document.createElement('span');
    name.className = 'dependency-name';
    name.textContent = item.name;

    const badgeEl = document.createElement('span');
    badgeEl.className = 'dep-badge ' + badge.cls;
    badgeEl.textContent = badge.text;

    titleLine.appendChild(name);
    titleLine.appendChild(badgeEl);

    const detail = document.createElement('div');
    detail.className = 'dependency-detail';
    detail.textContent = dependencyDetailText(item, busy);

    info.appendChild(titleLine);
    info.appendChild(detail);

    const logLines = dependencyState.logs[item.id];
    if (logLines && logLines.length > 0) {
      const logEl = document.createElement('div');
      logEl.className = 'dependency-log';
      logEl.textContent = logLines.join('\n');
      info.appendChild(logEl);
    }

    const action = document.createElement('div');
    action.className = 'dependency-action';

    const button = document.createElement('button');
    button.className = 'btn ' + (item.isInstalled ? 'btn-outline' : 'btn-success');
    button.style.padding = '5px 12px';
    button.style.fontSize = '11.5px';
    button.style.whiteSpace = 'nowrap';
    button.textContent = dependencyActionLabel(item, busy);
    button.disabled = busy === 'running' || dependencyState.loading;
    button.addEventListener('click', () => handleDependencyAction(item));

    action.appendChild(button);

    row.appendChild(icon);
    row.appendChild(info);
    row.appendChild(action);
    return row;
  }

  // Install-all state. Kept outside dependencyState.busy so a batch in flight disables the
  // batch button even though nothing is busy on any single row yet.
  let dependencyInstallAllRunning = false;
  let dependencyInstallAllProgress = '';

  function updateInstallAllButton() {
    const btn = document.getElementById('btn-install-all-dependencies');
    if (!btn) return;

    const label = btn.querySelector('span');
    const missing = dependencyState.items.filter(i => !i.isInstalled);

    if (dependencyInstallAllRunning) {
      btn.disabled = true;
      if (label) label.textContent = dependencyInstallAllProgress || 'Installing...';
      return;
    }

    btn.disabled = missing.length === 0;
    if (label) {
      label.textContent = missing.length === 0
        ? 'Nothing missing'
        : 'Install all missing (' + missing.length + ')';
    }
  }

  function renderDependencies() {
    const runtimeContainer = document.getElementById('runtimes-list-container');
    const agentContainer = document.getElementById('agents-list-container');
    if (!runtimeContainer || !agentContainer) return;

    updateInstallAllButton();

    runtimeContainer.innerHTML = '';
    agentContainer.innerHTML = '';

    if (dependencyState.items.length === 0) {
      const placeholder = document.createElement('div');
      placeholder.className = 'dependency-empty';
      placeholder.textContent = dependencyState.loading
        ? 'Scanning for installed tools...'
        : 'No dependency data yet. Click Recheck All.';
      runtimeContainer.appendChild(placeholder);
      return;
    }

    // Runtimes go in section 1; agents and network utilities share section 2.
    dependencyState.items.forEach(item => {
      const row = buildDependencyRow(item);
      if (item.category === 'runtime') runtimeContainer.appendChild(row);
      else agentContainer.appendChild(row);
    });
  }

  async function loadDependencies(announceFailure) {
    if (dependencyState.loading) return;

    dependencyState.loading = true;
    renderDependencies();

    try {
      const res = await callBridge('get_dependencies_status');
      dependencyState.items = (res && res.dependencies) || [];
      dependencyState.loaded = true;
    } catch (err) {
      if (announceFailure) {
        showToast('Dependency scan failed: ' + err.message, 'error', 4500);
      } else {
        console.warn('Dependency scan failed:', err);
      }
    } finally {
      dependencyState.loading = false;
      renderDependencies();
    }
  }

  async function installAllDependencies() {
    if (dependencyInstallAllRunning) return;

    const missing = dependencyState.items.filter(i => !i.isInstalled);
    if (missing.length === 0) {
      showToast('Every detected tool is already installed.', 'info', 3500);
      return;
    }

    dependencyInstallAllRunning = true;
    dependencyInstallAllProgress = '';
    updateInstallAllButton();

    try {
      const res = await callBridge('install_all_dependencies');
      if (res && res.started === false) {
        showToast('Every detected tool is already installed.', 'info', 3500);
        dependencyInstallAllRunning = false;
        updateInstallAllButton();
        return;
      }

      const total = (res && res.total) || missing.length;
      showToast('Installing ' + total + ' item(s). This takes a few minutes and streams its output below.', 'info', 6000);
    } catch (err) {
      showToast('The batch install could not start: ' + (err.message || err), 'error', 6000);
      dependencyInstallAllRunning = false;
      updateInstallAllButton();
    }
  }

  async function handleDependencyAction(item) {
    if (dependencyState.busy[item.id] === 'running') return;

    // An already-installed browser-only tool has nothing to install; re-verify instead.
    if (item.installKind === 'browser' && item.isInstalled) {
      await loadDependencies(true);
      return;
    }

    dependencyState.busy[item.id] = 'running';
    dependencyState.logs[item.id] = [];
    renderDependencies();

    try {
      await callBridge('run_dependency_install', { id: item.id });
      showToast(item.installKind === 'browser'
        ? `Opening the download page for ${item.name}...`
        : `Starting ${item.name} installer...`, 'info', 3000);
    } catch (err) {
      dependencyState.busy[item.id] = 'failed';
      renderDependencies();
      showToast(`Could not start installer: ${err.message}`, 'error', 4500);
    }
  }

  function setupDependencies() {
    document.getElementById('btn-recheck-dependencies')?.addEventListener('click', () => loadDependencies(true));
    document.getElementById('btn-install-all-dependencies')?.addEventListener('click', () => installAllDependencies());
  }

  // Keep the About card's version in step with the version the backend reports, so it is not
  // a second hardcoded copy of the number.
  // Every version shown in the interface comes from the backend, which reads it off the
  // assembly. Nothing here is allowed to carry its own copy.
  function renderAboutVersion() {
    const version = state.version || '';

    const about = document.getElementById('about-version');
    if (about) about.textContent = version;

    const brand = document.getElementById('brand-version');
    if (brand) brand.textContent = version.toUpperCase();

    const bootLine = document.getElementById('boot-log-line');
    if (bootLine) {
      bootLine.textContent = version ? 'Agents Hub ' + version + ' active.' : 'Agents Hub starting.';
    }
  }

  function setupSettingsView() {
    // About card: open links in the system browser instead of navigating the WebView.
    document.querySelectorAll('.about-link').forEach(btn => {
      btn.addEventListener('click', () => {
        const url = btn.dataset.url;
        if (url) callBridge('open_url_external', { url });
      });
    });

    renderAboutVersion();
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
      const globalHotkey = document.getElementById('setting-global-hotkey-checkbox')?.checked !== false;
      const maskShortcutPaths = document.getElementById('setting-mask-paths-checkbox')?.checked === true;
      const embedShells = document.getElementById('setting-embed-shells-checkbox')?.checked === true;
      const hideMainOnCliOpen = document.getElementById('setting-hide-main-on-cli-checkbox')?.checked === true;
      const enableProton = (document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox'))?.checked === true;
      const enableHardwareMonitor = (document.getElementById('pref-enable-hardware-monitor') || document.getElementById('setting-enable-hardware-monitor-checkbox'))?.checked === true;
      const theme = document.documentElement.getAttribute('data-theme') || 'dark';
      const accentColor = localStorage.getItem('agents_hub_accent_color') || '#10b981';
      const feedback = document.getElementById('settings-save-feedback');

      state.settings.enableProtonVpnProvider = enableProton;
      localStorage.setItem('agents_hub_enable_proton', enableProton);
      if (!enableProton && state.vpnProvider === 'proton') {
        state.vpnProvider = 'warp';
        localStorage.setItem('agents_hub_vpn_provider', 'warp');
      }
      updateVpnProviderUi();

      state.settings.enableHardwareMonitor = enableHardwareMonitor;
      localStorage.setItem('agents_hub_enable_hardware_monitor', enableHardwareMonitor);
      updateHardwareMonitorVisibility();

      state.isShortcutsMasked = maskShortcutPaths;
      localStorage.setItem('agents_hub_mask_shortcuts', maskShortcutPaths);
      renderDashboardShortcuts();

      await callBridge('save_general_settings', {
        settings: { autostart, startMinimized, minimizeToTray, notifications, globalHotkey, maskShortcutPaths, embedShellsInTerminalWorkspace: embedShells, hideMainOnCliOpen, enableProtonVpnProvider: enableProton, enableHardwareMonitor, theme, accentColor }
      });

      feedback.textContent = 'Preferences saved successfully!';
      setTimeout(() => feedback.textContent = '', 2500);
    });

    // Reactive checkbox toggle for Proton VPN Provider
    const protonToggle = document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox');
    protonToggle?.addEventListener('change', async (e) => {
      const isEnabled = !!e.target.checked;
      state.settings.enableProtonVpnProvider = isEnabled;
      localStorage.setItem('agents_hub_enable_proton', isEnabled);
      if (!isEnabled && state.vpnProvider === 'proton') {
        state.vpnProvider = 'warp';
        localStorage.setItem('agents_hub_vpn_provider', 'warp');
      }
      updateVpnProviderUi();
      await callBridge('save_general_settings', {
        settings: { enableProtonVpnProvider: isEnabled }
      });
      window.dispatchEvent(new CustomEvent('preference-changed', {
        detail: { key: 'enableProtonVpnProvider', value: isEnabled }
      }));
    });

    // Reactive checkbox toggle for System & Hardware Telemetry Monitor
    const hwToggle = document.getElementById('pref-enable-hardware-monitor') || document.getElementById('setting-enable-hardware-monitor-checkbox');
    hwToggle?.addEventListener('change', async (e) => {
      const isEnabled = !!e.target.checked;
      state.settings.enableHardwareMonitor = isEnabled;
      localStorage.setItem('agents_hub_enable_hardware_monitor', isEnabled);
      updateHardwareMonitorVisibility();
      await callBridge('save_general_settings', {
        settings: { enableHardwareMonitor: isEnabled }
      });
      window.dispatchEvent(new CustomEvent('preference-changed', {
        detail: { key: 'enableHardwareMonitor', value: isEnabled }
      }));
    });

    document.getElementById('setting-embed-shells-checkbox')?.addEventListener('change', async (e) => {
      await callBridge('save_general_settings', {
        settings: { embedShellsInTerminalWorkspace: e.target.checked }
      });
    });

    document.getElementById('setting-hide-main-on-cli-checkbox')?.addEventListener('change', async (e) => {
      await callBridge('save_general_settings', {
        settings: { hideMainOnCliOpen: e.target.checked }
      });
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
      const themeParam = currentThemeParam();
      callBridge('open_url_external', { url: `http://127.0.0.1:${port}/${themeParam}` });
    });

    // Standalone Terminals Window Buttons
    const openStandalone = () => callBridge('open_standalone_terminals');
    document.getElementById('btn-popout-terminals')?.addEventListener('click', openStandalone);
    document.getElementById('btn-popout-terminals-dash')?.addEventListener('click', openStandalone);
    document.getElementById('btn-open-standalone-hdr')?.addEventListener('click', openStandalone);

    // System Shell Launchers (Standard & Administrator)
    document.getElementById('btn-shell-cmd')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'cmd', asAdmin: false });
    });
    document.getElementById('btn-shell-cmd-admin')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'cmd', asAdmin: true });
    });
    document.getElementById('btn-shell-ps')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'powershell', asAdmin: false });
    });
    document.getElementById('btn-shell-ps-admin')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'powershell', asAdmin: true });
    });
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
      summaryEl.textContent = `Antigravity Profile: ${state.antigravity.activeProfile || 'None'}`;
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

  // Interactive Terminals - Workspace Preset Selector
  const TERMINAL_PRESET_LABELS = {
    Preset_Clean: 'Clean Slate',
    Preset_Duo: 'AI Duo (Claude + agy)',
    Preset_All: 'Full Suite'
  };

  function renderTerminalPresetState(activePreset) {
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
  }

  async function loadTerminalPreset() {
    try {
      const res = await callBridge('get_terminal_preset');
      renderTerminalPresetState(res && res.preset);
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
          renderTerminalPresetState(applied);
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
  function getPromptPresets() {
    if (!state.aiConfig) state.aiConfig = {};
    if (!Array.isArray(state.aiConfig.promptPresets)) state.aiConfig.promptPresets = [];
    return state.aiConfig.promptPresets;
  }

  function getActivePromptPreset() {
    const presets = getPromptPresets();
    const activeId = state.aiConfig.activePromptPresetId || 'preset_general';
    return presets.find(p => p.id === activeId) || presets[0] || null;
  }

  function getBuiltInPromptPreset(id) {
    const builtIns = Array.isArray(state.aiConfig.builtInPromptPresets) ? state.aiConfig.builtInPromptPresets : [];
    return builtIns.find(p => p.id === id) || null;
  }

  function loadPresetIntoDrawer(presetId) {
    const preset = getPromptPresets().find(p => p.id === presetId);
    const textarea = document.getElementById('prompt-system-instructions-input');
    const meta = document.getElementById('prompt-preset-meta');
    const deleteBtn = document.getElementById('btn-delete-preset-prompt');
    const slider = document.getElementById('prompt-temp-slider');
    const sliderVal = document.getElementById('prompt-temp-val');

    if (textarea) textarea.value = preset ? (preset.systemPrompt || '') : '';

    if (meta) {
      meta.textContent = preset
        ? (preset.isBuiltIn ? 'Built-in preset - editable, restorable via Reset' : 'Custom preset')
        : 'No preset selected';
    }

    if (deleteBtn) deleteBtn.classList.toggle('hidden', !preset || !!preset.isBuiltIn);

    const temp = Number.isFinite(state.aiConfig.promptEnhancerTemperature)
      ? state.aiConfig.promptEnhancerTemperature
      : 0.6;
    if (slider) slider.value = temp;
    if (sliderVal) sliderVal.textContent = temp.toFixed(1);
  }

  function renderPromptPresets() {
    const select = document.getElementById('enhance-style-select');
    if (!select) return;

    const presets = getPromptPresets();
    if (presets.length === 0) return;

    const activeId = state.aiConfig.activePromptPresetId || 'preset_general';
    select.innerHTML = '';
    presets.forEach(preset => {
      const opt = document.createElement('option');
      opt.value = preset.id;
      opt.textContent = preset.isBuiltIn ? preset.name : `${preset.name} (custom)`;
      if (preset.id === activeId) opt.selected = true;
      select.appendChild(opt);
    });

    loadPresetIntoDrawer(activeId);
  }

  function setupPromptEnhancer() {
    const input = document.getElementById('prompt-input');
    const output = document.getElementById('prompt-output');
    const inCount = document.getElementById('prompt-input-count');
    const outCount = document.getElementById('prompt-output-count');
    const btnExec = document.getElementById('btn-enhance-exec');
    const statusLbl = document.getElementById('prompt-enhance-status');

    const styleSelect = document.getElementById('enhance-style-select');
    const drawer = document.getElementById('prompt-instructions-drawer');
    const drawerToggle = document.getElementById('btn-toggle-prompt-instructions');
    const instructionsInput = document.getElementById('prompt-system-instructions-input');
    const tempSlider = document.getElementById('prompt-temp-slider');
    const tempVal = document.getElementById('prompt-temp-val');
    const saveStatus = document.getElementById('prompt-preset-save-status');

    const updateCounts = (text, targetEl) => {
      if (!targetEl) return;
      const chars = text.length;
      const words = text.trim() ? text.trim().split(/\s+/).length : 0;
      targetEl.textContent = `${chars} chars | ${words} words`;
    };

    const flashSaveStatus = (message, color) => {
      if (!saveStatus) return;
      saveStatus.textContent = message;
      saveStatus.style.color = color;
      setTimeout(() => { if (saveStatus.textContent === message) saveStatus.textContent = ''; }, 2500);
    };

    const persistConfig = async () => {
      await callBridge('save_ai_config', { config: state.aiConfig });
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

    // Iterative refinement: push the enhanced result back into the raw input.
    document.getElementById('btn-refine-prompt')?.addEventListener('click', () => {
      if (!output.value) {
        statusLbl.textContent = 'Nothing to refine yet.';
        statusLbl.style.color = 'var(--accent-amber)';
        return;
      }
      input.value = output.value;
      updateCounts(input.value, inCount);
      input.focus();
      statusLbl.textContent = 'Sent back to the raw prompt - refine and enhance again.';
      statusLbl.style.color = 'var(--accent-green)';
    });

    // Collapsible instructions drawer
    drawerToggle?.addEventListener('click', () => {
      if (!drawer) return;
      const wasHidden = drawer.classList.contains('hidden');
      drawer.classList.toggle('hidden', !wasHidden);
      drawerToggle.classList.toggle('btn-success', wasHidden);
      drawerToggle.classList.toggle('btn-outline', !wasHidden);
      if (wasHidden) {
        loadPresetIntoDrawer(styleSelect ? styleSelect.value : state.aiConfig.activePromptPresetId);
      }
    });

    // Preset selection loads its instructions
    styleSelect?.addEventListener('change', () => {
      state.aiConfig.activePromptPresetId = styleSelect.value;
      loadPresetIntoDrawer(styleSelect.value);
    });

    tempSlider?.addEventListener('input', () => {
      const val = parseFloat(tempSlider.value);
      if (tempVal) tempVal.textContent = val.toFixed(1);
    });

    // Persist edits to the active preset
    document.getElementById('btn-save-preset-prompt')?.addEventListener('click', async () => {
      const preset = getActivePromptPreset();
      if (!preset) return;

      preset.systemPrompt = instructionsInput ? instructionsInput.value : preset.systemPrompt;
      state.aiConfig.promptEnhancerTemperature = parseFloat(tempSlider ? tempSlider.value : '0.6');
      state.aiConfig.activePromptPresetId = preset.id;

      try {
        await persistConfig();
        flashSaveStatus('Saved.', 'var(--accent-green)');
      } catch (err) {
        flashSaveStatus('Save failed: ' + err.message, 'var(--accent-red)');
      }
    });

    // Restore a built-in preset's shipped instructions
    document.getElementById('btn-reset-preset-prompt')?.addEventListener('click', async () => {
      const preset = getActivePromptPreset();
      if (!preset) return;

      const builtIn = getBuiltInPromptPreset(preset.id);
      if (!builtIn) {
        flashSaveStatus('No shipped default for this preset.', 'var(--accent-amber)');
        return;
      }

      preset.systemPrompt = builtIn.systemPrompt;
      if (instructionsInput) instructionsInput.value = builtIn.systemPrompt;

      try {
        await persistConfig();
        flashSaveStatus('Restored default instructions.', 'var(--accent-green)');
      } catch (err) {
        flashSaveStatus('Reset failed: ' + err.message, 'var(--accent-red)');
      }
    });

    // Create a custom preset from the current instructions
    document.getElementById('btn-new-preset-prompt')?.addEventListener('click', async () => {
      const name = window.prompt('Name for the new preset:', 'My Preset');
      if (!name || !name.trim()) return;

      const preset = {
        id: 'preset_custom_' + Date.now().toString(36),
        name: name.trim(),
        systemPrompt: instructionsInput ? instructionsInput.value : '',
        isBuiltIn: false
      };

      getPromptPresets().push(preset);
      state.aiConfig.activePromptPresetId = preset.id;

      try {
        await persistConfig();
        renderPromptPresets();
        flashSaveStatus(`Created "${preset.name}".`, 'var(--accent-green)');
      } catch (err) {
        flashSaveStatus('Create failed: ' + err.message, 'var(--accent-red)');
      }
    });

    // Delete a custom preset (built-ins are protected)
    document.getElementById('btn-delete-preset-prompt')?.addEventListener('click', async () => {
      const preset = getActivePromptPreset();
      if (!preset || preset.isBuiltIn) return;
      if (!window.confirm(`Delete preset "${preset.name}"?`)) return;

      state.aiConfig.promptPresets = getPromptPresets().filter(p => p.id !== preset.id);
      state.aiConfig.activePromptPresetId = 'preset_general';

      try {
        await persistConfig();
        renderPromptPresets();
        flashSaveStatus('Preset deleted.', 'var(--accent-green)');
      } catch (err) {
        flashSaveStatus('Delete failed: ' + err.message, 'var(--accent-red)');
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
      const style = styleSelect ? styleSelect.value : 'preset_general';

      // Send the instructions the user currently sees, so unsaved edits still apply.
      const systemPrompt = instructionsInput ? instructionsInput.value.trim() : '';
      const temperature = tempSlider ? parseFloat(tempSlider.value) : 0.6;

      btnExec.disabled = true;
      statusLbl.textContent = 'Enhancing prompt...';
      statusLbl.style.color = 'var(--accent-green)';
      output.value = 'Enhancing in progress, please wait...';

      try {
        const res = await callBridge('enhance_prompt', {
          providerId,
          model,
          prompt: raw,
          style,
          systemPrompt,
          temperature
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
      const name = typeof p === 'object' ? (p.name || '') : p;
      const email = typeof p === 'object' ? (p.email || '') : '';
      const isRunning = typeof p === 'object' ? !!p.isRunning : false;
      const port = typeof p === 'object' && p.port ? p.port : null;
      const isActive = name.toLowerCase() === active.toLowerCase();
      const cardId = 'prof-card-' + name.replace(/[^a-z0-9]/gi, '_');
      const card = document.createElement('div');
      card.className = 'dashboard-card';
      card.id = cardId;
      card.style.borderColor = isRunning ? 'var(--accent-green)' : (isActive ? 'var(--accent-green)' : 'var(--border-subtle)');

      const emailHtml = email ? `
        <div class="profile-email-row" data-visible="false">
          <span class="profile-email-text">&#x2022;&#x2022;&#x2022;&#x2022;&#x2022;&#x2022;&#x2022;&#x2022;&#x2022;&#x2022;&#x2022;&#x2022;</span>
          <button class="btn-eye-toggle" title="Show / hide email" aria-label="Toggle email visibility">
            <svg viewBox="0 0 24 24" class="icon-eye icon-eye-off"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>
            <svg viewBox="0 0 24 24" class="icon-eye icon-eye-on" style="display:none"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
          </button>
        </div>` : '';

      card.innerHTML = `
        <div class="card-header">
          <div class="card-title-group">
            <div class="card-icon" style="color: ${isRunning || isActive ? 'var(--accent-green)' : 'var(--text-muted)'}">
              <svg class="icon-svg" viewBox="0 0 24 24"><path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
            </div>
            <div>
              <div class="card-title">${name}</div>
              <div class="card-subtitle">${isRunning ? `Running (Port: ${port || '9339'})` : (isActive ? 'Active Session (Stopped)' : 'Stopped')}</div>
              ${emailHtml}
            </div>
          </div>
          <div class="profile-card-actions" style="display: flex; align-items: center; gap: 6px;">
            ${isActive ? '<span class="brand-badge">ACTIVE</span>' : ''}
            ${isRunning
              ? `<span class="brand-badge" style="background: rgba(16, 185, 129, 0.15); color: var(--accent-green); border: 1px solid var(--accent-green);">RUNNING${port ? ' :' + port : ''}</span>`
              : '<span class="brand-badge" style="background: rgba(113, 113, 122, 0.15); color: var(--text-muted); border: 1px solid var(--border-subtle);">STOPPED</span>'}
            <button class="btn-open-folder" data-name="${name}" title="Open profile folder" aria-label="Open profile folder in Explorer">
              <svg viewBox="0 0 24 24" class="icon-folder"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>
            </button>
          </div>
        </div>
        <div style="display: flex; gap: 8px; margin-top: 14px; flex-wrap: wrap;">
          ${isRunning
            ? `<button class="btn btn-danger btn-stop-prof-instance" data-name="${name}" style="flex: 1;">Stop Instance</button>`
            : `<button class="btn btn-success btn-start-prof-instance" data-name="${name}" style="flex: 1;">Start Instance</button>`}
          ${!isActive ? `<button class="btn btn-outline btn-switch-prof" data-name="${name}">Set Active</button>` : ''}
          <button class="btn btn-outline btn-rename-prof" data-name="${name}">Rename</button>
          ${!isActive && !isRunning ? `<button class="btn btn-danger btn-delete-prof" data-name="${name}">Delete</button>` : ''}
        </div>
      `;

      // Eye toggle handler
      const emailRow = card.querySelector('.profile-email-row');
      if (emailRow) {
        const emailText = emailRow.querySelector('.profile-email-text');
        const eyeOff = emailRow.querySelector('.icon-eye-off');
        const eyeOn  = emailRow.querySelector('.icon-eye-on');
        emailRow.querySelector('.btn-eye-toggle').addEventListener('click', () => {
          const visible = emailRow.dataset.visible === 'true';
          if (visible) {
            emailText.textContent = '\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022';
            eyeOff.style.display = '';
            eyeOn.style.display  = 'none';
            emailRow.dataset.visible = 'false';
          } else {
            emailText.textContent = email;
            eyeOff.style.display = 'none';
            eyeOn.style.display  = '';
            emailRow.dataset.visible = 'true';
          }
        });
      }

      // Folder open handler
      card.querySelector('.btn-open-folder')?.addEventListener('click', async () => {
        await callBridge('open_profile_folder', { profileName: name });
      });

      container.appendChild(card);
    });

    container.querySelectorAll('.btn-start-prof-instance').forEach(b => {
      b.addEventListener('click', async () => {
        const name = b.dataset.name;
        b.disabled = true;
        try {
          await callBridge('start_antigravity_instance', { profile: name });
        } finally {
          b.disabled = false;
        }
      });
    });

    container.querySelectorAll('.btn-stop-prof-instance').forEach(b => {
      b.addEventListener('click', async () => {
        const name = b.dataset.name;
        b.disabled = true;
        try {
          await callBridge('stop_antigravity_instance', { profile: name });
        } finally {
          b.disabled = false;
        }
      });
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
          try {
            const res = await callBridge('rename_antigravity_profile', { oldName, newName: newName.trim() });
            if (res) {
              if (res.activeProfile !== undefined) state.antigravity.activeProfile = res.activeProfile;
              if (res.profiles) state.antigravity.profiles = res.profiles;
              renderProfiles();
              renderQuickProfileSelect();
              updateHeaderStatus();
            }
          } catch (e) {
            console.error('Rename profile failed:', e);
          }
        }
      });
    });

    container.querySelectorAll('.btn-delete-prof').forEach(b => {
      b.addEventListener('click', async () => {
        const name = b.dataset.name;
        if (confirm(`Are you sure you want to delete profile '${name}'?`)) {
          try {
            const res = await callBridge('delete_antigravity_profile', { profileName: name });
            if (res) {
              if (res.activeProfile !== undefined) state.antigravity.activeProfile = res.activeProfile;
              if (res.profiles) state.antigravity.profiles = res.profiles;
              renderProfiles();
              renderQuickProfileSelect();
              updateHeaderStatus();
            }
          } catch (e) {
            console.error('Delete profile failed:', e);
          }
        }
      });
    });
  }

  function renderQuickProfileSelect() {
    const sel = document.getElementById('quick-profile-select');
    const cardSel = document.getElementById('dash-card-profile-select');
    const summary = document.getElementById('dash-active-profile-summary');
    const rawProfiles = (state.antigravity.profiles && state.antigravity.profiles.length > 0)
      ? state.antigravity.profiles
      : (state.antigravity.activeProfile ? [state.antigravity.activeProfile] : ['default']);
    // Normalize to name strings for the dropdown
    const profiles = rawProfiles.map(p => (typeof p === 'object' ? p.name : p));
    const active = state.antigravity.activeProfile || (profiles[0] || 'default');

    if (summary) {
      summary.textContent = `Antigravity Profile: ${active}`;
    }

    [sel, cardSel].forEach(dropdown => {
      if (!dropdown) return;
      dropdown.innerHTML = '';
      profiles.forEach(p => {
        const opt = document.createElement('option');
        const pObj = rawProfiles.find(x => (typeof x === 'object' ? x.name : x).toLowerCase() === p.toLowerCase());
        const isRun = typeof pObj === 'object' && pObj.isRunning;
        const pPort = typeof pObj === 'object' && pObj.port ? pObj.port : null;

        opt.value = p;
        opt.textContent = isRun ? `${p} [Running : ${pPort || 'Active'}]` : `${p} [Stopped]`;
        if (p.toLowerCase() === active.toLowerCase()) opt.selected = true;
        dropdown.appendChild(opt);
      });

      dropdown.onchange = async () => {
        const chosen = dropdown.value;
        await callBridge('switch_antigravity_profile', { profile: chosen });
        state.antigravity.activeProfile = chosen;
        if (summary) summary.textContent = `Antigravity Profile: ${chosen}`;
        if (sel && sel !== dropdown) sel.value = chosen;
        if (cardSel && cardSel !== dropdown) cardSel.value = chosen;
      };
    });
  }

  let selectedPortId = 'hermes';

  // state.theme and state.accentColor are never populated, so reading them stamped
  // "theme=light" onto every link the dashboard built in JavaScript, even on a dark app.
  // The live values are the ones applyAdaptiveTheme persisted.
  function currentThemeParam() {
    const theme = localStorage.getItem('agents_hub_theme')
      || document.documentElement.getAttribute('data-theme')
      || 'light';
    const accent = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
    return `?theme=${encodeURIComponent(theme)}&accent=${encodeURIComponent(accent)}`;
  }

  function getWebPortsList() {
    if (state.runningPorts && state.runningPorts.length > 0) {
      return state.runningPorts.map(p => {
        if (p.id === 'hermes') {
          const h = state.clis['Hermes'] || {};
          return { ...p, state: h.state || 'Offline', isRunning: h.state === 'Running' };
        }
        if (p.id === 'claude_code') {
          const c = state.clis['ClaudeCode'] || {};
          return { ...p, state: c.state || 'Running', isRunning: true };
        }
        if (p.id === 'antigravity_cli' || p.id.startsWith('antigravity_cli:')) {
          return { ...p, state: p.isRunning ? 'Running' : 'Offline', isRunning: !!p.isRunning };
        }
        if (p.id === 'lan_proxy') {
          return { ...p, state: state.lanProxyEnabled ? 'Running' : 'Offline', isRunning: !!state.lanProxyEnabled };
        }
        return p;
      });
    }

    const lanIp = state.lanIp || '127.0.0.1';
    const hermesState = (state.clis['Hermes'] || {}).state || 'Offline';
    const proxyPort = state.lanProxyPort || 9191;
    const themeParam = currentThemeParam();

    return [
      {
        id: 'hermes',
        name: 'Hermes Dashboard',
        port: 9119,
        lanPort: proxyPort,
        localUrl: `http://127.0.0.1:9119/${themeParam}`,
        lanUrl: `http://${lanIp}:${proxyPort}/${themeParam}`,
        state: hermesState,
        isRunning: hermesState === 'Running'
      },
      {
        id: 'claude_code',
        name: 'Claude Code Web',
        port: 9229,
        lanPort: 9229,
        localUrl: `http://127.0.0.1:9229/${themeParam}`,
        lanUrl: `http://${lanIp}:9229/${themeParam}`,
        state: 'Running',
        isRunning: true
      },
      {
        id: 'antigravity_cli',
        name: 'Antigravity CLI Web',
        port: 9339,
        lanPort: 9339,
        localUrl: `http://127.0.0.1:9339/${themeParam}`,
        lanUrl: `http://${lanIp}:9339/${themeParam}`,
        state: 'Running',
        isRunning: true
      },
      {
        id: 'lan_proxy',
        name: 'LAN Remote Gateway',
        port: proxyPort,
        lanPort: proxyPort,
        localUrl: `http://127.0.0.1:${proxyPort}/${themeParam}`,
        lanUrl: `http://${lanIp}:${proxyPort}/${themeParam}`,
        state: state.lanProxyEnabled ? 'Running' : 'Offline',
        isRunning: !!state.lanProxyEnabled
      }
    ];
  }

  function renderRunningPorts() {
    const ports = getWebPortsList();
    if (!ports || ports.length === 0) return;

    if (!ports.some(p => p.id === selectedPortId)) {
      selectedPortId = ports[0].id;
    }

    const activePort = ports.find(p => p.id === selectedPortId) || ports[0];
    const lanIsActive = !!state.lanProxyEnabled;

    // When LAN is off, force local 127 in dashboard and everywhere
    const effectiveDashMode = lanIsActive ? state.dashUrlMode : 'local';
    const effectiveLanMode = lanIsActive ? state.lanUrlMode : 'local';

    const dashUrlStr = effectiveDashMode === 'lan' ? activePort.lanUrl : activePort.localUrl;
    const lanUrlStr = effectiveLanMode === 'lan' ? activePort.lanUrl : activePort.localUrl;

    // 1. Dashboard Card: WEB APP HOSTS & RUNNING PORTS
    const dashSelect = document.getElementById('dash-web-port-select');
    if (dashSelect) {
      dashSelect.innerHTML = '';
      ports.forEach(p => {
        const opt = document.createElement('option');
        opt.value = p.id;
        opt.textContent = effectiveDashMode === 'lan' ? `${p.name} (LAN :${p.lanPort})` : `${p.name} (Port ${p.port})`;
        if (p.id === selectedPortId) opt.selected = true;
        dashSelect.appendChild(opt);
      });
      dashSelect.onchange = () => {
        selectedPortId = dashSelect.value;
        renderRunningPorts();
      };
    }

    const dashUrl = document.getElementById('dash-lan-url');
    if (dashUrl) {
      dashUrl.textContent = dashUrlStr;
    }

    // Toggle button for Dashboard URL mode
    const btnToggleDashUrlMode = document.getElementById('btn-toggle-dash-url-mode');
    if (btnToggleDashUrlMode) {
      if (!lanIsActive) {
        btnToggleDashUrlMode.title = 'LAN network connection is turned off. Showing Local (127.0.0.1).';
        btnToggleDashUrlMode.classList.remove('active');
        btnToggleDashUrlMode.style.color = '';
      } else if (effectiveDashMode === 'lan') {
        btnToggleDashUrlMode.title = 'Currently showing Network (LAN) URL. Click to switch to Local (127.0.0.1).';
        btnToggleDashUrlMode.classList.add('active');
        btnToggleDashUrlMode.style.color = 'var(--accent-green)';
      } else {
        btnToggleDashUrlMode.title = 'Currently showing Local (127.0.0.1) URL. Click to switch to Network (LAN).';
        btnToggleDashUrlMode.classList.remove('active');
        btnToggleDashUrlMode.style.color = '';
      }
    }

    const dashChips = document.getElementById('dash-running-ports-chips');
    if (dashChips) {
      dashChips.innerHTML = '';
      ports.forEach(p => {
        const chip = document.createElement('button');
        const isSel = p.id === selectedPortId;
        chip.className = `btn btn-outline ${isSel ? 'active' : ''}`;
        chip.style.cssText = `padding: 3px 8px; font-size: 11px; display: inline-flex; align-items: center; gap: 5px; cursor: pointer; border-radius: var(--radius-sm); ${isSel ? 'border-color: var(--accent-green); color: var(--accent-green); font-weight: 600;' : ''}`;
        const portNum = effectiveDashMode === 'lan' ? p.lanPort : p.port;
        chip.innerHTML = `<span class="dot ${p.isRunning ? 'online' : ''}" style="width: 7px; height: 7px;"></span><span>${p.name}: ${portNum}</span>`;
        chip.onclick = () => {
          selectedPortId = p.id;
          renderRunningPorts();
        };
        dashChips.appendChild(chip);
      });
    }

    // The LAN page renders itself from its own state; this call keeps it in sync with
    // whichever service the dashboard card has selected.
    renderLanPage();
  }

  // ---- LAN Remote page ----
  // The page renders from one snapshot: what the proxy bound, what the firewall says, and who
  // has connected. Reachability is reported per service because binding can fail per port.

  let lanSnapshot = null;
  let lanLoading = false;
  let lanQrUrl = '';

  // The clipboard API needs a secure context, which a mapped virtual host does not always
  // provide. Fall back to the legacy path so the copy buttons always do something.
  function copyToClipboard(text) {
    const legacy = () => {
      try {
        const area = document.createElement('textarea');
        area.value = text;
        area.setAttribute('readonly', '');
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.select();
        document.execCommand('copy');
        document.body.removeChild(area);
        return true;
      } catch (e) {
        appendLog('Could not copy to the clipboard.');
        return false;
      }
    };

    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text).catch(() => legacy());
    }
    return Promise.resolve(legacy());
  }

  function lanRelativeTime(iso) {
    if (!iso) return '';
    const then = new Date(iso);
    if (isNaN(then.getTime())) return '';
    const secs = Math.max(0, Math.round((Date.now() - then.getTime()) / 1000));
    if (secs < 60) return secs + 's ago';
    if (secs < 3600) return Math.round(secs / 60) + 'm ago';
    if (secs < 86400) return Math.round(secs / 3600) + 'h ago';
    return Math.round(secs / 86400) + 'd ago';
  }

  // Three outcomes, because "reachable" is not a yes or no question here: we can bind a port,
  // fail to bind it because another process already owns it, or never attempt it at all.
  // "Checking" is separate from "LAN is off" so the table never states something untrue
  // while the first state read is still in flight.
  function lanReachability(port, lan, loaded) {
    if (!port.isRunning) {
      return { label: 'Not running', cls: 'is-off' };
    }
    if (!loaded) {
      return { label: 'Checking', cls: 'is-off' };
    }
    if (!lan || !lan.running) {
      return { label: 'LAN is off', cls: 'is-off' };
    }
    const target = port.lanPort || port.port;
    const entry = (lan.forwarders || []).find(f => f.port === target);
    if (!entry) {
      return { label: 'This device only', cls: 'is-off' };
    }
    return entry.bound
      ? { label: 'On the network', cls: 'is-open' }
      : { label: 'Port already in use', cls: 'is-warn' };
  }

  function renderLanPage() {
    const ports = getWebPortsList();
    const loaded = !!lanSnapshot;
    const lan = lanSnapshot && lanSnapshot.lan ? lanSnapshot.lan : null;

    renderLanFocal(ports, lan, loaded);
    renderLanServices(ports, lan, loaded);
    renderLanFirewall(lanSnapshot ? lanSnapshot.firewall : null, lan, loaded);
    renderLanClients(lanSnapshot ? lanSnapshot.clients : null);
  }

  function renderLanFocal(ports, lan, loaded) {
    const lanIsActive = !!state.lanProxyEnabled;
    const effectiveMode = lanIsActive ? state.lanUrlMode : 'local';
    const active = ports.find(p => p.id === selectedPortId) || ports[0] || null;
    const url = active ? (effectiveMode === 'lan' ? active.lanUrl : active.localUrl) : '';

    const urlEl = document.getElementById('lan-full-url');
    if (urlEl) {
      if (url) {
        urlEl.textContent = url;
        urlEl.classList.remove('is-empty');
      } else {
        urlEl.textContent = lanIsActive ? 'No service is running yet' : 'Turn LAN access on to get a link';
        urlEl.classList.add('is-empty');
      }
    }

    const sub = document.getElementById('lan-connect-sub');
    if (sub) {
      sub.textContent = lanIsActive
        ? 'Open a running service from a phone or laptop on the same network'
        : 'LAN access is off, so these links point at this machine only';
    }

    const meta = document.getElementById('lan-url-meta');
    if (meta) {
      meta.textContent = effectiveMode === 'lan'
        ? 'The link carries your theme and accent so the remote page matches this one.'
        : 'Showing the local link on 127.0.0.1. Switch to the network link to share it with another device.';
    }

    const ipEl = document.getElementById('lan-ip');
    if (ipEl) ipEl.textContent = (lan && lan.lanIp) || state.lanIp || 'Not detected';

    const netEl = document.getElementById('lan-network');
    if (netEl) {
      const fw = lanSnapshot && lanSnapshot.firewall;
      if (!loaded) {
        netEl.textContent = 'Checking';
      } else if (fw && fw.networkName) {
        netEl.textContent = fw.networkCategory ? fw.networkName + ' (' + fw.networkCategory + ')' : fw.networkName;
      } else if (fw && fw.networkCategory) {
        netEl.textContent = fw.networkCategory;
      } else {
        netEl.textContent = 'Unknown';
      }
    }

    const gwEl = document.getElementById('lan-gateway');
    if (gwEl) gwEl.textContent = (lan && lan.gatewayPort) || state.lanProxyPort || 9191;

    const btn = document.getElementById('btn-toggle-lan-network');
    const dot = document.getElementById('lan-status-dot');
    const text = document.getElementById('lan-status-text');
    if (btn && dot && text) {
      if (lanIsActive) {
        dot.className = 'dot online';
        text.textContent = 'LAN on';
        btn.classList.add('is-on');
        btn.setAttribute('aria-pressed', 'true');
        btn.title = 'LAN access is on. Click to stop sharing these services with the network.';
      } else {
        dot.className = 'dot';
        text.textContent = 'LAN off';
        btn.classList.remove('is-on');
        btn.setAttribute('aria-pressed', 'false');
        btn.title = 'LAN access is off. Click to share these services with the network.';
      }
    }

    updateLanQr(url, lanIsActive);
  }

  function setLanQrState(kind, text) {
    const img = document.getElementById('lan-qr-img');
    const fallback = document.getElementById('lan-qr-fallback');
    const retry = document.getElementById('lan-qr-retry');

    if (img) img.style.display = kind === 'ready' ? 'block' : 'none';
    if (fallback) {
      fallback.style.display = kind === 'ready' ? 'none' : '';
      fallback.textContent = text || '';
      fallback.classList.toggle('is-error', kind === 'error');
    }
    if (retry) retry.style.display = kind === 'error' ? '' : 'none';
  }

  function updateLanQr(url, lanIsActive) {
    if (!lanIsActive || !url) {
      lanQrUrl = '';
      setLanQrState('idle', lanIsActive
        ? 'The code appears once a service is running.'
        : 'The code appears once LAN access is on.');
      return;
    }

    if (url === lanQrUrl) return;
    lanQrUrl = url;
    setLanQrState('loading', 'Generating the code for this link.');

    callBridge('generate_qr_code', { url })
      .then(res => {
        if (lanQrUrl !== url) return;
        if (res && res.qrBase64) {
          const img = document.getElementById('lan-qr-img');
          if (img) img.src = res.qrBase64;
          setLanQrState('ready', '');
        } else {
          lanQrUrl = '';
          setLanQrState('error', 'The code could not be generated for this link.');
        }
      })
      .catch(() => {
        lanQrUrl = '';
        setLanQrState('error', 'The code could not be generated for this link.');
      });
  }

  function renderLanServices(ports, lan, loaded) {
    const container = document.getElementById('lan-services');
    const note = document.getElementById('lan-services-note');
    if (!container) return;

    container.innerHTML = '';

    if (ports.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'lan-row';
      empty.style.gridTemplateColumns = '1fr';
      empty.style.cursor = 'default';
      empty.textContent = 'No service is running yet. Start an engine from the Dashboard and it appears here.';
      container.appendChild(empty);
      if (note) note.textContent = '';
      return;
    }

    const head = document.createElement('div');
    head.className = 'lan-row lan-row-head';
    ['Service', 'Port', 'Reachable from a device', ''].forEach(label => {
      const cell = document.createElement('span');
      cell.textContent = label;
      head.appendChild(cell);
    });
    container.appendChild(head);

    const localOnly = [];

    ports.forEach(p => {
      const reach = lanReachability(p, lan, loaded);
      const isSelected = p.id === selectedPortId;

      const row = document.createElement('div');
      row.className = 'lan-row' + (isSelected ? ' is-selected' : '');

      const nameBtn = document.createElement('button');
      nameBtn.type = 'button';
      nameBtn.className = 'lan-row-name';
      nameBtn.title = 'Use this service for the link above';
      const dot = document.createElement('span');
      dot.className = 'dot' + (p.isRunning ? ' online' : '');
      const label = document.createElement('span');
      label.textContent = p.name;
      nameBtn.appendChild(dot);
      nameBtn.appendChild(label);
      nameBtn.addEventListener('click', () => {
        selectedPortId = p.id;
        renderRunningPorts();
      });

      const portCell = document.createElement('span');
      portCell.className = 'lan-row-port';
      portCell.textContent = String(p.lanPort || p.port);

      const reachCell = document.createElement('span');
      reachCell.className = 'lan-row-reach ' + reach.cls;
      reachCell.textContent = reach.label;

      const actions = document.createElement('span');
      actions.className = 'lan-row-actions';

      const rowUrl = () => (state.lanProxyEnabled ? p.lanUrl : p.localUrl);

      const copyBtn = document.createElement('button');
      copyBtn.type = 'button';
      copyBtn.className = 'btn-icon';
      copyBtn.title = 'Copy this service link';
      copyBtn.innerHTML = '<svg class="icon-svg" viewBox="0 0 24 24"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>';
      copyBtn.addEventListener('click', () => {
        copyToClipboard(rowUrl());
        appendLog('Copied link for ' + p.name + ': ' + rowUrl());
      });

      const openBtn = document.createElement('button');
      openBtn.type = 'button';
      openBtn.className = 'btn-icon';
      openBtn.title = 'Open in this machine\'s browser';
      openBtn.innerHTML = '<svg class="icon-svg" viewBox="0 0 24 24"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>';
      openBtn.addEventListener('click', () => callBridge('open_url_external', { url: rowUrl() }));

      actions.appendChild(copyBtn);
      actions.appendChild(openBtn);

      row.appendChild(nameBtn);
      row.appendChild(portCell);
      row.appendChild(reachCell);
      row.appendChild(actions);
      container.appendChild(row);

      if (reach.label === 'This device only') localOnly.push(p.name);
    });

    if (note) {
      const parts = [];
      if (!loaded) {
        parts.push('Checking which ports are bound and who can reach them.');
      } else if (!lan || !lan.running) {
        parts.push('LAN access is off, so every link points at this machine.');
      }
      if (localOnly.length > 0) {
        parts.push(localOnly.length === 1
          ? 'One service stays on this machine and is not shared with the network.'
          : localOnly.length + ' services stay on this machine and are not shared with the network.');
      }
      if (lan && lan.bindError) {
        parts.push('The gateway could not bind: ' + lan.bindError);
      }
      note.textContent = parts.join(' ');
    }
  }

  function renderLanFirewall(fw, lan, loaded) {
    const badge = document.getElementById('lan-fw-badge');
    const detail = document.getElementById('lan-fw-detail');
    const allow = document.getElementById('btn-lan-firewall-allow');
    const remove = document.getElementById('btn-lan-firewall-remove');
    if (!badge || !detail) return;

    const hasPorts = !!(lan && lan.requiredPorts && lan.requiredPorts.length > 0);

    const setBadge = (text, cls) => {
      badge.textContent = text;
      badge.className = 'status-badge' + (cls ? ' ' + cls : '');
    };

    if (!loaded) {
      setBadge('Checking', '');
      detail.textContent = 'Reading the Windows Firewall rule and your network profile.';
      if (allow) { allow.disabled = true; allow.textContent = 'Allow through firewall'; }
      if (remove) remove.style.display = 'none';
      return;
    }

    if (!fw || !fw.querySucceeded) {
      setBadge('Unknown', '');
      detail.textContent = (fw && fw.detail) || 'The firewall could not be read on this machine.';
      if (allow) { allow.disabled = true; allow.textContent = 'Allow through firewall'; }
      if (remove) remove.style.display = 'none';
      return;
    }

    if (remove) remove.style.display = fw.ruleExists ? '' : 'none';

    if (!fw.firewallOnForActiveProfile) {
      setBadge('Firewall off', 'is-off');
      detail.textContent = 'Windows Firewall is off for the ' + fw.activeProfile + ' profile, so nothing is filtering incoming connections.';
      if (allow) { allow.disabled = true; allow.textContent = 'Not needed'; }
      return;
    }

    if (fw.state === 'ready') {
      setBadge('Allowed', 'is-ok');
      detail.textContent = 'Windows Firewall allows incoming connections on port ' + (fw.rulePorts || []).join(', ') + '.';
      if (allow) { allow.disabled = true; allow.textContent = 'Already allowed'; }
      return;
    }

    if (fw.state === 'partial') {
      setBadge('Partly allowed', 'is-warn');
      detail.textContent = 'The rule exists but does not cover port ' + (fw.missingPorts || []).join(', ') + '.';
      if (allow) { allow.disabled = !hasPorts; allow.textContent = 'Allow the missing ports'; }
      return;
    }

    setBadge('Not allowed', 'is-warn');
    const parts = [];
    if (fw.networkCategory) {
      parts.push('This network is set to ' + fw.networkCategory + ', and Windows Firewall is on for that profile.');
    } else {
      parts.push('Windows Firewall is on for the ' + fw.activeProfile + ' profile.');
    }
    parts.push('No rule for Agents Hub covers port ' + (fw.requiredPorts || []).join(', ') + '.');
    if (!hasPorts) {
      parts.push('Turn LAN access on first, then allow it here.');
    }
    detail.textContent = parts.join(' ');
    if (allow) { allow.disabled = !hasPorts; allow.textContent = 'Allow through firewall'; }
  }

  function renderLanClients(clients) {
    const container = document.getElementById('lan-clients');
    const countEl = document.getElementById('lan-clients-count');
    if (!container) return;

    const list = Array.isArray(clients) ? clients : [];
    if (countEl) countEl.textContent = String(list.length);

    container.innerHTML = '';

    if (list.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'lan-note';
      empty.style.margin = '0';
      empty.textContent = 'No device has connected yet. Open the link above on a phone and it shows up here.';
      container.appendChild(empty);
      return;
    }

    list.forEach(c => {
      const row = document.createElement('div');
      row.className = 'lan-client';

      const left = document.createElement('div');
      left.style.minWidth = '0';

      const name = document.createElement('div');
      name.className = 'lan-client-name';
      name.textContent = c.hostName || c.deviceName || c.ip;

      const meta = document.createElement('div');
      meta.className = 'lan-client-meta';
      const bits = [c.ip];
      if (c.deviceType) bits.push(c.deviceType);
      if (c.activeConnections) {
        bits.push(c.activeConnections + (c.activeConnections === 1 ? ' connection' : ' connections'));
      }
      meta.textContent = bits.join('  ');

      left.appendChild(name);
      left.appendChild(meta);

      const seen = document.createElement('div');
      seen.className = 'lan-client-meta';
      seen.textContent = lanRelativeTime(c.lastActivity);

      row.appendChild(left);
      row.appendChild(seen);
      container.appendChild(row);
    });
  }

  async function loadLanState(force) {
    if (lanLoading) return;
    lanLoading = true;

    const refreshBtn = document.getElementById('btn-lan-refresh');
    if (refreshBtn) {
      refreshBtn.disabled = true;
      refreshBtn.textContent = 'Checking';
    }

    try {
      const res = await callBridge('get_lan_state', { refresh: !!force });
      if (res) {
        lanSnapshot = res;
        if (res.lan) {
          state.lanIp = res.lan.lanIp || state.lanIp;
          state.lanProxyPort = res.lan.gatewayPort || state.lanProxyPort;
          state.lanProxyEnabled = !!res.lan.running;
          if (!state.lanProxyEnabled) {
            state.lanUrlMode = 'local';
          }
        }
        renderRunningPorts();
      }
    } catch (err) {
      appendLog('Could not read the LAN state: ' + (err.message || err));
      setLanQrState('error', 'The LAN state could not be read.');
    } finally {
      lanLoading = false;
      if (refreshBtn) {
        refreshBtn.disabled = false;
        refreshBtn.textContent = 'Recheck';
      }
    }
  }

  async function requestFirewallRule() {
    const allow = document.getElementById('btn-lan-firewall-allow');
    if (allow) {
      allow.disabled = true;
      allow.textContent = 'Waiting for Windows';
    }

    try {
      const res = await callBridge('request_firewall_rule', {});
      if (res) {
        appendLog('Firewall: ' + (res.message || ''));
        if (res.userCancelled) {
          showToast('The Windows elevation prompt was dismissed, so no rule was created.', 'info', 5000);
        } else if (res.succeeded) {
          showToast('Windows Firewall now allows Agents Hub on this network.', 'success', 4000);
        } else {
          showToast(res.message || 'The firewall rule could not be created.', 'error', 6000);
        }
        if (res.lan) lanSnapshot = Object.assign({}, lanSnapshot || {}, { lan: res.lan });
        if (res.firewall) lanSnapshot = Object.assign({}, lanSnapshot || {}, { firewall: res.firewall });
      }
    } catch (err) {
      appendLog('Firewall request failed: ' + (err.message || err));
      showToast('The firewall rule could not be created.', 'error', 6000);
    } finally {
      renderLanPage();
    }
  }

  async function removeFirewallRule() {
    const remove = document.getElementById('btn-lan-firewall-remove');
    if (remove) {
      remove.disabled = true;
      remove.textContent = 'Waiting for Windows';
    }

    try {
      const res = await callBridge('remove_firewall_rule', {});
      if (res) {
        appendLog('Firewall: ' + (res.message || ''));
        showToast(res.userCancelled
          ? 'The Windows elevation prompt was dismissed, so the rule is unchanged.'
          : (res.message || 'The rule was removed.'), res.userCancelled ? 'info' : 'success', 5000);
        if (res.lan) lanSnapshot = Object.assign({}, lanSnapshot || {}, { lan: res.lan });
        if (res.firewall) lanSnapshot = Object.assign({}, lanSnapshot || {}, { firewall: res.firewall });
      }
    } catch (err) {
      appendLog('Firewall removal failed: ' + (err.message || err));
    } finally {
      if (remove) {
        remove.disabled = false;
        remove.textContent = 'Remove rule';
      }
      renderLanPage();
    }
  }

  // AI Dev Tools View (Dynamic Tools Manager)
  // ---- AI Dev Tools ----

  let devtoolExpandedId = null;

  const DEVTOOL_HEALTH = {
    ready: { text: 'Ready', cls: 'is-ok' },
    needsfixing: { text: 'Path needs fixing', cls: 'is-warn' },
    missing: { text: 'Path missing', cls: 'is-warn' },
    empty: { text: 'No path set', cls: 'is-off' },
    unsupported: { text: 'Cannot start this', cls: 'is-off' }
  };

  function devtoolHealth(tool) {
    const h = (tool && tool.health) || {};
    const known = DEVTOOL_HEALTH[h.state] || DEVTOOL_HEALTH.empty;
    return {
      state: DEVTOOL_HEALTH[h.state] ? h.state : 'empty',
      text: known.text,
      cls: known.cls,
      detail: h.detail || '',
      resolvedTarget: h.resolvedTarget || '',
      kind: h.kind || '',
      launchable: h.launchable === true
    };
  }

  // The seeded descriptions restate the name ("WorkBuddy AI" -> "WorkBuddy AI", "Main executable
  // for FreeBuff Desktop"), so they read as a fact without being one. When a description says
  // nothing the name does not already say, the line shows where the tool actually lives, which
  // is what helps you spot a path that moved.
  function devtoolSubtitle(tool, health) {
    const desc = (tool.description || '').trim();
    const name = (tool.name || '').trim();
    const target = (tool.target || '').trim();

    const restatesName = !desc
      || desc.toLowerCase() === name.toLowerCase()
      || (name.length > 0 && desc.toLowerCase().includes(name.toLowerCase()));

    if (!restatesName) return desc;
    if (!target) return 'No path set yet';
    if (health.kind === 'App alias') return 'Windows app alias';

    const resolved = health.resolvedTarget || target;
    const parts = resolved.split('\\').filter(Boolean);
    return parts.length > 1 ? '...\\' + parts.slice(-2).join('\\') : resolved;
  }

  function renderDevToolsList() {
    const container = document.getElementById('tools-list-container');
    if (!container) return;

    const devTools = (state.aiConfig && Array.isArray(state.aiConfig.devTools))
      ? state.aiConfig.devTools
      : [];

    container.innerHTML = '';

    const countEl = document.getElementById('devtools-health-count');
    if (countEl) {
      const ready = devTools.filter(t => devtoolHealth(t).state === 'ready').length;
      if (devTools.length === 0) {
        countEl.textContent = 'No tools';
        countEl.className = 'status-badge';
      } else {
        countEl.textContent = ready + ' of ' + devTools.length + ' ready';
        countEl.className = 'status-badge' + (ready === devTools.length ? ' is-ok' : ' is-warn');
      }
    }

    if (devTools.length === 0) {
      const emptyBox = document.createElement('div');
      emptyBox.className = 'empty-state';
      emptyBox.style.cssText = 'padding: 32px 16px; text-align: center; border: 1px dashed var(--border-subtle); border-radius: var(--radius-md); color: var(--text-muted);';
      emptyBox.innerHTML = `
        <svg class="icon-svg" style="width: 32px; height: 32px; margin: 0 auto 10px; color: var(--text-dim);" viewBox="0 0 24 24"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
        <div style="font-size: 13.5px; font-weight: 600; color: var(--text-main); margin-bottom: 4px;">No AI Dev Tools configured</div>
        <div style="font-size: 12px; margin-bottom: 14px;">Add a desktop AI client and its executable path to launch it from here.</div>
        <button class="btn btn-primary" id="btn-empty-add-devtool" type="button" style="padding: 6px 14px; font-size: 12px;">+ Add First Tool</button>
      `;
      container.appendChild(emptyBox);
      emptyBox.querySelector('#btn-empty-add-devtool')?.addEventListener('click', () => openDevToolModal(null));
      return;
    }

    devTools.forEach(t => {
      const health = devtoolHealth(t);
      const targetPath = t.target || '';
      const isEnabled = t.isEnabled !== false;
      const isExpanded = devtoolExpandedId === t.id;
      const canLaunch = health.launchable;

      const row = document.createElement('div');
      row.className = 'devtool-card' + (isEnabled ? '' : ' disabled') + (isExpanded ? ' expanded' : '');
      row.dataset.id = t.id;

      const launchTitle = canLaunch
        ? 'Launch ' + t.name
        : (health.detail || 'This tool has no usable executable path.');

      row.innerHTML = `
        <div class="devtool-row">
          <button type="button" class="devtool-expand" data-id="${t.id}" aria-expanded="${isExpanded}" title="${isExpanded ? 'Hide path and actions' : 'Show path and actions'}">
            <svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24"><polyline points="9 18 15 12 9 6"/></svg>
          </button>
          <span class="dot ${health.state === 'ready' ? 'online' : ''}"></span>
          <div class="devtool-identity">
            <div class="devtool-name">${escapeHtml(t.name)}</div>
            <div class="devtool-sub">${escapeHtml(devtoolSubtitle(t, health))}</div>
          </div>
          <span class="devtool-health ${health.cls}">${escapeHtml(health.text)}</span>
          <div class="devtool-row-actions">
            <label class="toggle-switch" title="${isEnabled ? 'Enabled. Click to disable.' : 'Disabled. Click to enable.'}">
              <input type="checkbox" class="devtool-enable-toggle" data-id="${t.id}" ${isEnabled ? 'checked' : ''}>
              <span class="toggle-slider"></span>
            </label>
            <button class="btn btn-success btn-launch-tool" data-id="${t.id}" data-name="${escapeHtml(t.name)}" title="${escapeHtml(launchTitle)}" ${canLaunch ? '' : 'disabled'}>
              <svg class="icon-svg" style="width: 12px; height: 12px;" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"/></svg>
              <span>Launch</span>
            </button>
            <button class="btn btn-outline btn-edit-devtool" data-id="${t.id}" title="Edit the name and description">
              <svg class="icon-svg" style="width: 12px; height: 12px;" viewBox="0 0 24 24"><path d="M12 20h9"/><path d="M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>
              <span>Edit</span>
            </button>
          </div>
        </div>
        <div class="devtool-detail" style="display: ${isExpanded ? 'flex' : 'none'};">
          <div class="devtool-path-row">
            <div class="devtool-path-wrap">
              <input type="${state.isShortcutsMasked ? 'password' : 'text'}" class="input-text tool-path-input" data-id="${t.id}" value="${escapeHtml(targetPath)}" placeholder="C:\path\to\tool.exe or shell:AppsFolder\...">
              <button type="button" class="devtool-mask btn-toggle-mask-tool" data-id="${t.id}" title="Show or hide the path">
                <svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24">
                  ${state.isShortcutsMasked
                    ? '<path d="m2 2 20 20"/><path d="M9.88 9.88a3 3 0 1 0 4.24 4.24"/><path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68"/><path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61"/>'
                    : '<path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>'
                  }
                </svg>
              </button>
            </div>
            <button class="btn btn-outline btn-browse-tool" data-id="${t.id}" data-name="${escapeHtml(t.name)}" title="Select the executable with the file browser">
              <svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>
              <span>Browse...</span>
            </button>
          </div>
          ${health.detail ? `<p class="devtool-note ${health.cls === 'is-warn' ? 'is-warn' : ''}">${escapeHtml(health.detail)}</p>` : ''}
          <div class="devtool-detail-actions">
            ${health.state === 'needsfixing' ? '<button class="btn btn-primary btn-fix-tool" data-id="' + t.id + '" type="button">Fix path</button>' : ''}
            <button class="btn btn-outline btn-reset-tool" data-id="${t.id}" data-name="${escapeHtml(t.name)}" type="button" title="Restore the path this tool ships with">Reset to default</button>
            <button class="btn btn-outline devtool-danger btn-delete-devtool" data-id="${t.id}" data-name="${escapeHtml(t.name)}" type="button" title="Remove this tool">
              <svg class="icon-svg" style="width: 12px; height: 12px;" viewBox="0 0 24 24"><path d="M3 6h18"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/><line x1="10" y1="11" x2="10" y2="17"/><line x1="14" y1="11" x2="14" y2="17"/></svg>
              <span>Delete</span>
            </button>
          </div>
        </div>
      `;
      container.appendChild(row);
    });

    // Expand / collapse
    container.querySelectorAll('.devtool-expand').forEach(b => {
      b.addEventListener('click', () => {
        const id = b.dataset.id;
        devtoolExpandedId = devtoolExpandedId === id ? null : id;
        renderDevToolsList();
      });
    });

    // Launch
    container.querySelectorAll('.btn-launch-tool').forEach(b => {
      b.addEventListener('click', async () => {
        const id = b.dataset.id;
        const tool = (state.aiConfig.devTools || []).find(t => t.id === id);
        const name = (tool && tool.name) || b.dataset.name || 'Tool';
        const target = (tool && tool.target) || '';

        b.disabled = true;
        try {
          const res = await callBridge('launch_tool', { toolId: id, toolName: name, target });
          if (res && res.message) {
            showToast(res.message, 'success', 3500);
          } else {
            showToast(name + ' started.', 'success', 3500);
          }
        } catch (err) {
          const reason = (err && err.message) ? err.message : ('Could not start ' + name + '.');
          showToast(reason, 'error', 6000);
          appendLog('Launch failed for ' + name + ': ' + reason);
        } finally {
          b.disabled = false;
        }
      });
    });

    // Browse
    container.querySelectorAll('.btn-browse-tool').forEach(b => {
      b.addEventListener('click', async () => {
        const id = b.dataset.id;
        const tool = (state.aiConfig.devTools || []).find(t => t.id === id);
        const toolName = tool ? tool.name : b.dataset.name;
        try {
          const res = await callBridge('browse_tool_target', { toolId: id, toolName });
          if (res && res.path) {
            if (tool) tool.target = res.path;
            if (!state.aiConfig.toolTargets) state.aiConfig.toolTargets = {};
            if (toolName) state.aiConfig.toolTargets[toolName] = res.path;
            await callBridge('save_ai_config', { config: state.aiConfig });
            await loadDevTools(false);
          }
        } catch { }
      });
    });

    // Manual path edit
    container.querySelectorAll('.tool-path-input').forEach(inp => {
      inp.addEventListener('change', async () => {
        const id = inp.dataset.id;
        const tool = (state.aiConfig.devTools || []).find(t => t.id === id);
        if (!tool) return;

        const entered = inp.value.trim();
        tool.target = entered;
        if (!state.aiConfig.toolTargets) state.aiConfig.toolTargets = {};
        state.aiConfig.toolTargets[tool.name] = entered;

        await callBridge('save_ai_config', { config: state.aiConfig });
        await loadDevTools(false);
      });
    });

    // Show / hide the path
    container.querySelectorAll('.btn-toggle-mask-tool').forEach(btn => {
      btn.addEventListener('click', () => {
        const id = btn.dataset.id;
        const input = container.querySelector(`.tool-path-input[data-id="${id}"]`);
        if (!input) return;
        const wasPassword = input.type === 'password';
        input.type = wasPassword ? 'text' : 'password';
        btn.innerHTML = `<svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24">${
          wasPassword
            ? '<path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>'
            : '<path d="m2 2 20 20"/><path d="M9.88 9.88a3 3 0 1 0 4.24 4.24"/><path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68"/><path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61"/>'
        }</svg>`;
      });
    });

    // Fix a stored path that only needs cleaning up
    container.querySelectorAll('.btn-fix-tool').forEach(b => {
      b.addEventListener('click', async () => {
        const id = b.dataset.id;
        const tool = (state.aiConfig.devTools || []).find(t => t.id === id);
        if (!tool) return;

        const health = devtoolHealth(tool);
        const fixed = health.resolvedTarget || (tool.target || '').replace(/^"+|"+$/g, '');
        tool.target = fixed;
        if (!state.aiConfig.toolTargets) state.aiConfig.toolTargets = {};
        state.aiConfig.toolTargets[tool.name] = fixed;

        await callBridge('save_ai_config', { config: state.aiConfig });
        showToast('Path cleaned up for ' + tool.name + '.', 'success', 3500);
        await loadDevTools(false);
      });
    });

    // Restore the shipped default path
    container.querySelectorAll('.btn-reset-tool').forEach(b => {
      b.addEventListener('click', async () => {
        const id = b.dataset.id;
        const toolName = b.dataset.name || 'Tool';
        try {
          const res = await callBridge('reset_tool_target', { toolId: id, toolName });
          if (res && res.path) {
            showToast('Restored the default path for ' + toolName + '.', 'success', 3500);
          } else {
            showToast('No default path is known for ' + toolName + '.', 'warning', 5000);
          }
          await loadDevTools(false);
        } catch (err) {
          showToast('Could not reset the path for ' + toolName + '.', 'error', 5000);
        }
      });
    });

    // Edit name and description
    container.querySelectorAll('.btn-edit-devtool').forEach(b => {
      b.addEventListener('click', () => {
        const tool = (state.aiConfig.devTools || []).find(t => t.id === b.dataset.id);
        if (tool) openDevToolModal(tool);
      });
    });

    // Delete
    container.querySelectorAll('.btn-delete-devtool').forEach(b => {
      b.addEventListener('click', async () => {
        const id = b.dataset.id;
        const toolName = b.dataset.name || 'Tool';
        if (!confirm(`Remove "${toolName}" from AI Dev Tools?`)) return;

        state.aiConfig.devTools = (state.aiConfig.devTools || []).filter(t => t.id !== id);
        if (state.aiConfig.toolTargets) {
          delete state.aiConfig.toolTargets[toolName];
          delete state.aiConfig.toolTargets[id];
        }
        if (devtoolExpandedId === id) devtoolExpandedId = null;

        await callBridge('save_ai_config', { config: state.aiConfig });
        await loadDevTools(false);
        populatePresetDropdown();
      });
    });

    // Enable / disable
    container.querySelectorAll('.devtool-enable-toggle').forEach(chk => {
      chk.addEventListener('change', async () => {
        const tool = (state.aiConfig.devTools || []).find(t => t.id === chk.dataset.id);
        if (!tool) return;
        tool.isEnabled = chk.checked;
        await callBridge('save_ai_config', { config: state.aiConfig });
        renderDevToolsList();
      });
    });
  }

  // ---- finding installed clients ----

  async function findInstalledClients() {
    const btn = document.getElementById('btn-find-clients');
    const original = btn ? btn.textContent : '';
    if (btn) {
      btn.disabled = true;
      btn.textContent = 'Searching';
    }

    try {
      const res = await callBridge('detect_ai_clients', {});
      renderDevToolOffers(res);
    } catch (err) {
      appendLog('Could not scan for installed tools: ' + (err.message || err));
      showToast('The scan for installed tools failed.', 'error', 5000);
    } finally {
      if (btn) {
        btn.disabled = false;
        btn.textContent = original;
      }
    }
  }

  function renderDevToolOffers(res) {
    const container = document.getElementById('devtool-offers');
    if (!container) return;

    const found = (res && Array.isArray(res.found)) ? res.found : [];
    const alreadyConfigured = (res && res.alreadyConfigured) || 0;

    container.innerHTML = '';
    if (found.length === 0) {
      container.style.display = 'none';
      if (alreadyConfigured > 0) {
        showToast('Every tool found on this machine is already configured.', 'info', 4000);
      } else {
        showToast('None of the recognised clients were found in the usual install folders.', 'info', 5000);
      }
      return;
    }

    container.style.display = '';

    const head = document.createElement('div');
    head.className = 'devtool-offers-head';
    const headText = document.createElement('span');
    headText.textContent = found.length + (found.length === 1 ? ' client found' : ' clients found');
    const dismiss = document.createElement('button');
    dismiss.type = 'button';
    dismiss.className = 'btn btn-outline';
    dismiss.style.cssText = 'padding: 3px 10px; font-size: 11px;';
    dismiss.textContent = 'Dismiss';
    dismiss.addEventListener('click', () => { container.style.display = 'none'; });
    head.appendChild(headText);
    head.appendChild(dismiss);
    container.appendChild(head);

    found.forEach(f => {
      const row = document.createElement('div');
      row.className = 'devtool-offer';

      const name = document.createElement('span');
      name.className = 'devtool-offer-name';
      name.textContent = f.name;

      const path = document.createElement('span');
      path.className = 'devtool-offer-path';
      path.textContent = f.target;
      path.title = f.target;

      const add = document.createElement('button');
      add.type = 'button';
      add.className = 'btn btn-primary';
      add.style.cssText = 'padding: 4px 12px; font-size: 11.5px;';
      add.textContent = 'Add';
      add.addEventListener('click', async () => {
        add.disabled = true;
        await addDiscoveredClient(f);
        add.disabled = false;
      });

      row.appendChild(name);
      row.appendChild(path);
      row.appendChild(add);
      container.appendChild(row);
    });
  }

  async function addDiscoveredClient(found) {
    if (!state.aiConfig) state.aiConfig = {};

    const id = found.name.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
    const already = (state.aiConfig.devTools || []).some(d =>
      d.id === id || (d.target || '') === found.target);

    state.aiConfig.devTools = state.aiConfig.devTools || [];
    if (!already) {
      state.aiConfig.devTools.push({
        id,
        name: found.name,
        description: '',
        target: found.target,
        arguments: '',
        workingDirectory: '',
        isEnabled: true
      });
      showToast(found.name + ' added to AI Dev Tools.', 'success', 3500);
    }

    try {
      await callBridge('save_ai_config', { config: state.aiConfig });
    } catch { }

    await loadDevTools();
  }

  async function loadDevTools() {
    const btn = document.getElementById('btn-recheck-devtools');
    if (btn) {
      btn.disabled = true;
      btn.textContent = 'Checking';
    }

    try {
      const res = await callBridge('get_dev_tools', {});
      if (res && Array.isArray(res.devTools)) {
        if (!state.aiConfig) state.aiConfig = {};
        state.aiConfig.devTools = res.devTools;
      }
    } catch (err) {
      appendLog('Could not read the dev tool list: ' + (err.message || err));
    } finally {
      if (btn) {
        btn.disabled = false;
        btn.textContent = 'Recheck paths';
      }
      renderDevToolsList();
    }
  }
  function openDevToolModal(tool = null) {
    const modal = document.getElementById('modal-devtool-editor');
    if (!modal) return;
    const title = document.getElementById('modal-devtool-title');
    const idInp = document.getElementById('modal-devtool-id');
    const nameInp = document.getElementById('modal-devtool-name');
    const descInp = document.getElementById('modal-devtool-desc');
    const targetInp = document.getElementById('modal-devtool-target');
    const argsInp = document.getElementById('modal-devtool-args');
    const workdirInp = document.getElementById('modal-devtool-workdir');
    const enabledInp = document.getElementById('modal-devtool-enabled');

    if (tool) {
      if (title) title.textContent = 'Edit AI Dev Tool';
      if (idInp) idInp.value = tool.id || '';
      if (nameInp) nameInp.value = tool.name || '';
      if (descInp) descInp.value = tool.description || '';
      if (targetInp) targetInp.value = tool.target || '';
      if (argsInp) argsInp.value = tool.arguments || '';
      if (workdirInp) workdirInp.value = tool.workingDirectory || '';
      if (enabledInp) enabledInp.checked = tool.isEnabled !== false;
    } else {
      if (title) title.textContent = 'Add AI Dev Tool';
      if (idInp) idInp.value = '';
      if (nameInp) nameInp.value = '';
      if (descInp) descInp.value = '';
      if (targetInp) targetInp.value = '';
      if (argsInp) argsInp.value = '';
      if (workdirInp) workdirInp.value = '';
      if (enabledInp) enabledInp.checked = true;
    }

    modal.classList.add('open');
    if (nameInp) setTimeout(() => nameInp.focus(), 60);
  }

  function setupDevToolsEditor() {
    document.getElementById('btn-add-dev-tool')?.addEventListener('click', () => {
      openDevToolModal(null);
    });

    document.getElementById('btn-recheck-devtools')?.addEventListener('click', () => loadDevTools());
    document.getElementById('btn-find-clients')?.addEventListener('click', () => findInstalledClients());

    const modal = document.getElementById('modal-devtool-editor');
    if (!modal) return;

    document.getElementById('btn-cancel-devtool-modal')?.addEventListener('click', () => {
      modal.classList.remove('open');
    });

    modal.addEventListener('click', (e) => {
      if (e.target === modal) modal.classList.remove('open');
    });

    window.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && modal.classList.contains('open')) {
        modal.classList.remove('open');
      }
    });

    document.getElementById('btn-browse-modal-devtool')?.addEventListener('click', async () => {
      const nameInp = document.getElementById('modal-devtool-name');
      const toolName = nameInp ? nameInp.value.trim() : '';
      try {
        const res = await callBridge('browse_tool_target', { toolName });
        if (res && res.path) {
          const targetInp = document.getElementById('modal-devtool-target');
          if (targetInp) targetInp.value = res.path;
        }
      } catch { }
    });

    document.getElementById('btn-browse-modal-devtool-workdir')?.addEventListener('click', async () => {
      try {
        const res = await callBridge('browse_directory', { title: 'Select the folder the tool starts in' });
        if (res && res.path) {
          const wd = document.getElementById('modal-devtool-workdir');
          if (wd) wd.value = res.path;
        }
      } catch { }
    });

    document.getElementById('btn-save-devtool-modal')?.addEventListener('click', async () => {
      const idInp = document.getElementById('modal-devtool-id');
      const nameInp = document.getElementById('modal-devtool-name');
      const descInp = document.getElementById('modal-devtool-desc');
      const targetInp = document.getElementById('modal-devtool-target');
      const enabledInp = document.getElementById('modal-devtool-enabled');
      const argsInp = document.getElementById('modal-devtool-args');
      const workdirInp = document.getElementById('modal-devtool-workdir');

      const name = nameInp ? nameInp.value.trim() : '';
      if (!name) {
        alert('Tool Name is required.');
        nameInp?.focus();
        return;
      }

      const id = idInp && idInp.value ? idInp.value : ('tool_' + Date.now().toString(36) + Math.random().toString(36).substr(2, 4));
      const desc = descInp ? descInp.value.trim() : '';
      const target = targetInp ? targetInp.value.trim() : '';
      const argumentsText = argsInp ? argsInp.value.trim() : '';
      const workingDirectory = workdirInp ? workdirInp.value.trim() : '';
      const isEnabled = enabledInp ? enabledInp.checked : true;

      if (!state.aiConfig) state.aiConfig = {};
      if (!Array.isArray(state.aiConfig.devTools)) state.aiConfig.devTools = [];
      if (!state.aiConfig.toolTargets) state.aiConfig.toolTargets = {};

      const existingIndex = state.aiConfig.devTools.findIndex(t => t.id === id);
      if (existingIndex >= 0) {
        state.aiConfig.devTools[existingIndex] = { id, name, description: desc, target, arguments: argumentsText, workingDirectory, isEnabled };
      } else {
        state.aiConfig.devTools.push({ id, name, description: desc, target, arguments: argumentsText, workingDirectory, isEnabled });
      }

      state.aiConfig.toolTargets[name] = target;
      state.aiConfig.toolTargets[id] = target;

      await callBridge('save_ai_config', { config: state.aiConfig });
      modal.classList.remove('open');
      // The modal rebuilds the entry locally, so the health attached by the backend has to be
      // read back rather than rendered from a record that no longer carries it.
      await loadDevTools();
      populatePresetDropdown();
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

    // If target is empty, attempt resolution from devTools / toolTargets
    if (!target && state.aiConfig) {
      if (state.aiConfig.devTools && Array.isArray(state.aiConfig.devTools)) {
        const found = state.aiConfig.devTools.find(dt => dt.name.toLowerCase() === name.toLowerCase() || dt.id.toLowerCase() === name.toLowerCase());
        if (found && found.target) target = found.target;
      }
      if (!target && state.aiConfig.toolTargets) {
        const targets = state.aiConfig.toolTargets;
        if (targets[name]) target = targets[name];
        else if (name.includes('Antigravity') && targets['Antigravity-ide']) target = targets['Antigravity-ide'];
        else if (name.includes('Claude') && targets['Claude']) target = targets['Claude'];
        else if (name.includes('ChatGPT') && targets['ChatGPT']) target = targets['ChatGPT'];
        else if (name.includes('OpenCode') && targets['OpenCode']) target = targets['OpenCode'];
        else if (name.includes('Cline') && targets['Cline']) target = targets['Cline'];
        else if (name.includes('FreeBuff') && targets['FreeBuff']) target = targets['FreeBuff'];
      }
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
    const devTools = Array.isArray(state.aiConfig?.devTools) ? state.aiConfig.devTools : [];
    const devToolsOptgroup = document.createElement('optgroup');
    devToolsOptgroup.label = 'AI Dev Tools (Configured Paths)';

    if (devTools.length > 0) {
      devTools.forEach(t => {
        const opt = document.createElement('option');
        opt.value = `tool:${t.id || t.name}`;
        const configuredPath = t.target || toolTargets[t.name] || '';
        opt.textContent = `${t.name} ${configuredPath ? `(${configuredPath})` : '(Not configured)'}`;
        opt.dataset.name = t.name;
        opt.dataset.cat = 'AI Dev Tool';
        opt.dataset.target = configuredPath;
        devToolsOptgroup.appendChild(opt);
      });
    } else {
      Object.keys(toolTargets).forEach(k => {
        const opt = document.createElement('option');
        opt.value = `tool:${k}`;
        const configuredPath = toolTargets[k] || '';
        opt.textContent = `${k} ${configuredPath ? `(${configuredPath})` : '(Not configured)'}`;
        opt.dataset.name = k;
        opt.dataset.cat = 'AI Dev Tool';
        opt.dataset.target = configuredPath;
        devToolsOptgroup.appendChild(opt);
      });
    }

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
  let quotaCooldownInterval = null;

  function updateVpnProviderUi() {
    const isProtonEnabled = state.settings.enableProtonVpnProvider === true || localStorage.getItem('agents_hub_enable_proton') === 'true';
    const providerTabs = document.getElementById('provider-tabs') || document.getElementById('vpn-provider-switcher');

    if (providerTabs) {
      providerTabs.style.display = isProtonEnabled ? 'flex' : 'none';
    }

    // Default Fallback & Safety: If Proton is disabled, strictly force active provider to WARP
    if (!isProtonEnabled && state.vpnProvider === 'proton') {
      state.vpnProvider = 'warp';
      localStorage.setItem('agents_hub_vpn_provider', 'warp');
    }

    const isWarp = state.vpnProvider === 'warp';
    const btnWarp = document.getElementById('btn-provider-warp');
    const btnProton = document.getElementById('btn-provider-proton');
    const lblReset = document.getElementById('lbl-reset-quota-btn');
    const providerBadge = document.getElementById('dash-provider-badge');
    const providerText = document.getElementById('dash-provider-text');

    if (btnWarp) {
      btnWarp.classList.toggle('active', isWarp);
      btnWarp.setAttribute('aria-selected', isWarp ? 'true' : 'false');
    }
    if (btnProton) {
      btnProton.classList.toggle('active', !isWarp);
      btnProton.setAttribute('aria-selected', !isWarp ? 'true' : 'false');
    }

    if (lblReset && !quotaCooldownInterval) {
      lblReset.textContent = isWarp ? 'Reset OpenCode Quota (WARP IP)' : 'Rotate Proton VPN Server & IP';
    }

    if (providerBadge && providerText) {
      providerBadge.className = 'ip-pill-badge ' + (state.isDirectIsp ? 'is-direct' : (isWarp ? 'is-warp' : 'is-proton'));
      providerText.textContent = state.isDirectIsp ? 'Active: Direct ISP' : (isWarp ? 'Active: Cloudflare WARP' : 'Active: Proton VPN');
    }
  }

  function startQuotaResetCooldown(durationSeconds = 12) {
    if (quotaCooldownInterval) {
      clearInterval(quotaCooldownInterval);
      quotaCooldownInterval = null;
    }

    const btn = document.getElementById('btn-reset-opencode-quota');
    const lbl = document.getElementById('lbl-reset-quota-btn');
    const icon = document.getElementById('icon-reset-quota');

    if (!btn) return;

    let remaining = durationSeconds;
    btn.disabled = true;
    btn.classList.add('btn-cooldown');
    if (icon) icon.classList.remove('spinning');
    if (lbl) lbl.textContent = `Cooldown (${remaining}s)...`;

    quotaCooldownInterval = setInterval(() => {
      remaining -= 1;
      if (remaining > 0) {
        if (lbl) lbl.textContent = `Cooldown (${remaining}s)...`;
      } else {
        clearInterval(quotaCooldownInterval);
        quotaCooldownInterval = null;
        btn.disabled = false;
        btn.classList.remove('btn-cooldown');
        updateVpnProviderUi();
      }
    }, 1000);
  }

  function escapeHtml(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  function renderPublicIpDisplay() {
    const v4Diff = document.getElementById('dash-ipv4-diff');
    const v6Diff = document.getElementById('dash-ipv6-diff');
    const legacyIpText = document.getElementById('dash-public-ip-text');
    const eyeIcon = document.getElementById('icon-ip-eye');
    const geoFlag = document.getElementById('dash-geo-flag');
    const geoText = document.getElementById('dash-geo-text');

    if (state.isIpMasked) {
      if (v4Diff) {
        v4Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">***.***.***</span>';
      }
      if (v6Diff) {
        v6Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">****:****:****</span>';
      }
      if (legacyIpText) {
        legacyIpText.textContent = 'IP: ***';
      }
      if (geoFlag) {
        geoFlag.style.display = 'none';
      }
      if (geoText) geoText.textContent = 'Hidden (Masked)';
      if (eyeIcon) {
        eyeIcon.innerHTML = '<path d="m2 2 20 20"/><path d="M9.88 9.88a3 3 0 1 0 4.24 4.24"/><path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68"/><path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61"/>';
      }
      updateVpnProviderUi();
      return;
    }

    if (eyeIcon) {
      eyeIcon.innerHTML = '<path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>';
    }

    const currentV4 = state.ipv4 || (state.publicIp && !state.publicIp.includes(':') ? state.publicIp : 'Fetching...');
    const currentV6 = state.ipv6 || (state.publicIp && state.publicIp.includes(':') ? state.publicIp : 'Not assigned');

    // IPv4 Row: Diff display if rotated
    if (v4Diff) {
      if (state.previousIpv4 && state.previousIpv4 !== 'unknown' && state.previousIpv4 !== currentV4 && currentV4 !== 'Fetching...') {
        v4Diff.innerHTML = `<span class="ip-old">${escapeHtml(state.previousIpv4)}</span> <span class="ip-arrow">➜</span> <span class="ip-val-curr font-mono">${escapeHtml(currentV4)}</span>`;
      } else {
        v4Diff.innerHTML = `<span class="ip-val-curr font-mono">${escapeHtml(currentV4)}</span>`;
      }
    }

    // IPv6 Row: Diff display if rotated
    if (v6Diff) {
      if (state.previousIpv6 && state.previousIpv6 !== 'Not assigned' && state.previousIpv6 !== currentV6 && currentV6 !== 'Not assigned') {
        v6Diff.innerHTML = `<span class="ip-old">${escapeHtml(state.previousIpv6)}</span> <span class="ip-arrow">➜</span> <span class="ip-val-curr font-mono">${escapeHtml(currentV6)}</span>`;
      } else {
        v6Diff.innerHTML = `<span class="ip-val-curr font-mono ${currentV6 === 'Not assigned' ? 'text-muted' : ''}">${escapeHtml(currentV6)}</span>`;
      }
    }

    // Geolocation Meta Row
    if (geoFlag) {
      if (state.countryCode) {
        geoFlag.textContent = state.countryCode.toUpperCase();
        geoFlag.style.display = 'inline-block';
      } else {
        geoFlag.style.display = 'none';
      }
    }
    if (geoText) {
      const locStr = state.city
        ? (state.country ? `${state.country} (${state.city})` : state.city)
        : (state.country || 'Detecting location...');
      geoText.textContent = locStr;
    }

    if (legacyIpText) {
      const flag = state.countryCode ? `[${state.countryCode.toUpperCase()}] ` : '';
      const locDetail = state.city ? ` (${state.city})` : (state.country ? ` (${state.country})` : '');
      legacyIpText.textContent = `IP: ${flag}${currentV4}${locDetail}`;
    }

    updateVpnProviderUi();
  }

  function formatRelativeTime(isoString) {
    if (!isoString) return 'Never';
    const timestamp = new Date(isoString).getTime();
    if (isNaN(timestamp)) return 'Never';
    const diffSec = Math.max(0, Math.floor((Date.now() - timestamp) / 1000));
    if (diffSec < 45) return 'Just now';
    if (diffSec < 3600) return `${Math.floor(diffSec / 60)}m ago`;
    if (diffSec < 86400) return `${Math.floor(diffSec / 3600)}h ago`;
    return `${Math.floor(diffSec / 86400)}d ago`;
  }

  function updateQuotaStatsDisplay() {
    try {
      const statsEl = document.getElementById('quota-reset-stats');
      if (!statsEl) return;

      const todayStr = new Date().toISOString().slice(0, 10);
      const savedDate = localStorage.getItem('agents_hub_rotations_date');
      let count = parseInt(localStorage.getItem('agents_hub_rotations_today') || '0', 10);

      if (savedDate !== todayStr) {
        count = 0;
        localStorage.setItem('agents_hub_rotations_today', '0');
        localStorage.setItem('agents_hub_rotations_date', todayStr);
      }

      const lastRotated = localStorage.getItem('agents_hub_last_rotated') || '';
      const relTime = formatRelativeTime(lastRotated);
      const lblStats = document.getElementById('lbl-quota-stats-text');
      if (lblStats) {
        lblStats.textContent = `Last rotated: ${relTime} • Rotations today: ${count}`;
      } else {
        statsEl.textContent = `Last rotated: ${relTime} • Rotations today: ${count}`;
      }
    } catch (err) {
      console.warn('updateQuotaStatsDisplay error:', err);
    }
  }

  function recordSuccessfulRotation() {
    const nowIso = new Date().toISOString();
    const todayStr = nowIso.slice(0, 10);
    const savedDate = localStorage.getItem('agents_hub_rotations_date');
    let count = parseInt(localStorage.getItem('agents_hub_rotations_today') || '0', 10);

    if (savedDate !== todayStr) {
      count = 1;
    } else {
      count += 1;
    }

    localStorage.setItem('agents_hub_last_rotated', nowIso);
    localStorage.setItem('agents_hub_rotations_today', String(count));
    localStorage.setItem('agents_hub_rotations_date', todayStr);
    updateQuotaStatsDisplay();
  }

  function setupQuotaAndIpWidget() {
    try {
      updateQuotaStatsDisplay();
      updateVpnProviderUi();
      renderPublicIpDisplay();
      setInterval(updateQuotaStatsDisplay, 15000);
    } catch (err) {
      console.warn('setupQuotaAndIpWidget error:', err);
    }

    // Trigger initial public IP fetch immediately
    if (!state.ipv4 && !state.publicIp) {
      callBridge('get_public_ip').then(r => {
        if (r) {
          if (r.ipv4) state.ipv4 = r.ipv4;
          if (r.ipv6) state.ipv6 = r.ipv6;
          if (r.ip) state.publicIp = r.ip;
          if (!state.ipv4 && r.ip && !r.ip.includes(':')) state.ipv4 = r.ip;
          if (!state.ipv6 && r.ip && r.ip.includes(':')) state.ipv6 = r.ip;
          state.countryCode = r.countryCode || '';
          state.country = r.country || '';
          state.countryEmoji = r.countryEmoji || '';
          state.city = r.city || '';
          renderPublicIpDisplay();
        }
      }).catch(() => {});
    }

    // Segmented VPN Provider Controls
    document.getElementById('btn-provider-warp')?.addEventListener('click', () => {
      state.vpnProvider = 'warp';
      state.isDirectIsp = false;
      localStorage.setItem('agents_hub_vpn_provider', 'warp');
      updateVpnProviderUi();
    });

    document.getElementById('btn-provider-proton')?.addEventListener('click', async () => {
      const isProtonEnabled = state.settings.enableProtonVpnProvider === true || localStorage.getItem('agents_hub_enable_proton') === 'true';
      if (!isProtonEnabled) return;

      state.vpnProvider = 'proton';
      state.isDirectIsp = false;
      localStorage.setItem('agents_hub_vpn_provider', 'proton');
      updateVpnProviderUi();

      try {
        const res = await callBridge('ensure_proton_vpn_running');
        if (res && res.launched) {
          showToast('Opening Proton VPN application...', 'info', 3500);
          appendLog('[ProtonVPN] Launching Proton VPN GUI');
        }
      } catch { }
    });

    document.getElementById('btn-toggle-mask-ip')?.addEventListener('click', async () => {
      state.isIpMasked = !state.isIpMasked;
      renderPublicIpDisplay();
      await callBridge('save_general_settings', {
        settings: { maskPublicIp: state.isIpMasked }
      });
    });

    document.getElementById('btn-refresh-public-ip')?.addEventListener('click', async () => {
      const v4Diff = document.getElementById('dash-ipv4-diff');
      const v6Diff = document.getElementById('dash-ipv6-diff');
      if (v4Diff && !state.isIpMasked) v4Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Fetching...</span>';
      if (v6Diff && !state.isIpMasked) v6Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Fetching...</span>';
      try {
        const res = await callBridge('get_public_ip');
        if (res) {
          if (res.ipv4) state.ipv4 = res.ipv4;
          if (res.ipv6) state.ipv6 = res.ipv6;
          if (res.ip) state.publicIp = res.ip;
          if (!state.ipv4 && res.ip && !res.ip.includes(':')) state.ipv4 = res.ip;
          if (!state.ipv6 && res.ip && res.ip.includes(':')) state.ipv6 = res.ip;
          state.countryCode = res.countryCode || '';
          state.country = res.country || '';
          state.countryEmoji = res.countryEmoji || '';
          state.city = res.city || '';
          renderPublicIpDisplay();
        }
      } catch { }
    });

    // Reset Quota / Rotate Tunnel Button
    document.getElementById('btn-reset-opencode-quota')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-reset-opencode-quota');
      const icon = document.getElementById('icon-reset-quota');
      const lbl = document.getElementById('lbl-reset-quota-btn');
      const v4Diff = document.getElementById('dash-ipv4-diff');
      const v6Diff = document.getElementById('dash-ipv6-diff');
      const ipContainer = document.getElementById('dash-ip-container');
      const isProton = state.vpnProvider === 'proton';

      if (btn) btn.disabled = true;
      if (icon) icon.classList.add('spinning');
      if (lbl) lbl.textContent = isProton ? 'Rotating Proton VPN Server...' : 'Rotating Tunnel & Clearing Sockets...';
      if (v4Diff && !state.isIpMasked) v4Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Rotating...</span>';
      if (v6Diff && !state.isIpMasked) v6Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Rotating...</span>';

      try {
        const res = await callBridge('reset_opencode_quota', { provider: state.vpnProvider });
        if (res && res.success) {
          state.isDirectIsp = false;

          // Record old IPs for before vs after visual diff
          if (res.old) {
            if (res.old.ipv4 && res.old.ipv4 !== 'unknown') state.previousIpv4 = res.old.ipv4;
            if (res.old.ipv6 && res.old.ipv6 !== 'Not assigned') state.previousIpv6 = res.old.ipv6;
          } else if (res.oldIp && res.oldIp !== 'unknown') {
            state.previousIpv4 = res.oldIp;
          }

          // Record new rotated IPs
          if (res.new) {
            if (res.new.ipv4) state.ipv4 = res.new.ipv4;
            if (res.new.ipv6) state.ipv6 = res.new.ipv6;
          }
          if (res.ipv4) state.ipv4 = res.ipv4;
          if (res.ipv6) state.ipv6 = res.ipv6;
          const freshIp = res.newIp || res.ip;
          if (freshIp && freshIp !== 'unknown') {
            state.publicIp = freshIp;
            if (!state.ipv4 && !freshIp.includes(':')) state.ipv4 = freshIp;
            if (!state.ipv6 && freshIp.includes(':')) state.ipv6 = freshIp;
          }

          state.countryCode = res.countryCode || '';
          state.country = res.country || '';
          state.countryEmoji = res.countryEmoji || '';
          state.city = res.city || '';

          renderPublicIpDisplay();
          recordSuccessfulRotation();

          // Play subtle success animation on IP container
          if (ipContainer) {
            ipContainer.classList.remove('pulse-success');
            void ipContainer.offsetWidth; // Force CSS reflow
            ipContainer.classList.add('pulse-success');
          }

          const provName = isProton ? 'Proton VPN' : 'Cloudflare WARP';
          const oldV4Str = (res.old && res.old.ipv4) || res.oldIp || 'Previous';
          const newV4Str = (res.new && res.new.ipv4) || res.newIp || freshIp || 'Updated';
          const flagStr = state.countryEmoji ? `${state.countryEmoji} ` : '';
          const locStr = state.city ? ` (${state.city})` : (state.country ? ` (${state.country})` : '');
          const toastMsg = res.sidecarSynced
            ? `[${provName}] Rotated: ${oldV4Str} ➜ ${flagStr}${newV4Str}${locStr} | Sidecar Re-synced`
            : `[${provName}] Rotated: ${oldV4Str} ➜ ${flagStr}${newV4Str}${locStr}`;

          showToast(toastMsg, 'success', 5000);
          appendLog(`[QuotaReset] ${provName}: ` + (res.message || toastMsg));

          // Initiate smart cooldown (12s for WARP, 60s for Proton)
          const cooldownTarget = res.cooldownSeconds || (isProton ? 60 : 12);
          startQuotaResetCooldown(cooldownTarget);
        } else {
          renderPublicIpDisplay();
          const errMsg = (res && res.message) ? res.message : 'Tunnel rotation timed out or failed';
          const isCooldown = res && (res.cooldownRemaining || (res.message && res.message.includes('Cooldown active')));
          if (isCooldown) {
            const waitSec = res.cooldownRemaining || (isProton ? 60 : 12);
            startQuotaResetCooldown(waitSec);
            showToast(errMsg, 'info', 4000);
          } else {
            showToast(`Quota Reset Failed: ${errMsg}`, 'error', 6500);
            appendLog(`[QuotaReset] Failed: ${errMsg}`);
            if (lbl) lbl.textContent = isProton ? 'Connect Proton VPN' : 'Reset Failed';
            setTimeout(() => {
              if (!quotaCooldownInterval) updateVpnProviderUi();
            }, 3500);
          }
        }
      } catch (err) {
        renderPublicIpDisplay();
        const rawMsg = err?.message && err.message !== '[object Object]'
          ? err.message
          : (err?.data?.message || (typeof err === 'object' ? JSON.stringify(err) : String(err)));
        const errMsg = (rawMsg || 'Unknown error occurred').replace(/^Error:\s*/, '');
        showToast(`Quota Reset Error: ${errMsg}`, 'error', 6500);
        appendLog('[QuotaReset] Error: ' + errMsg);
        if (lbl) lbl.textContent = isProton ? 'Connect Proton VPN' : 'Reset Failed';
        setTimeout(() => {
          if (!quotaCooldownInterval) updateVpnProviderUi();
        }, 3500);
      } finally {
        if (!quotaCooldownInterval) {
          if (icon) icon.classList.remove('spinning');
          if (btn) btn.disabled = false;
        }
      }
    });

    // Disconnect VPN Button (Revert to Direct ISP)
    document.getElementById('btn-disconnect-vpn')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-disconnect-vpn');
      const v4Diff = document.getElementById('dash-ipv4-diff');
      const v6Diff = document.getElementById('dash-ipv6-diff');
      const ipContainer = document.getElementById('dash-ip-container');

      if (btn) btn.disabled = true;
      if (v4Diff && !state.isIpMasked) v4Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Disconnecting...</span>';
      if (v6Diff && !state.isIpMasked) v6Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Restoring ISP...</span>';

      try {
        const res = await callBridge('disconnect_vpn');
        state.isDirectIsp = true;

        if (state.ipv4 && state.ipv4 !== 'Fetching...') {
          state.previousIpv4 = state.ipv4;
        }
        if (state.ipv6 && state.ipv6 !== 'Not assigned') {
          state.previousIpv6 = state.ipv6;
        }

        if (res) {
          const freshV4 = res.ipv4 || res.newIp;
          if (freshV4 && freshV4 !== 'unknown') {
            state.ipv4 = freshV4;
            state.publicIp = freshV4;
          }
          if (res.ipv6 && res.ipv6 !== 'unknown') {
            state.ipv6 = res.ipv6;
          }
          state.countryCode = res.countryCode || '';
          state.country = res.country || '';
          state.countryEmoji = res.countryEmoji || '🌐';
          state.city = res.city || '';
        }

        renderPublicIpDisplay();

        if (ipContainer) {
          ipContainer.classList.remove('pulse-success');
          void ipContainer.offsetWidth;
          ipContainer.classList.add('pulse-success');
        }

        const msg = (res && res.message) ? res.message : 'VPN disconnected. Direct ISP connection restored.';
        showToast(msg, 'info', 5000);
        appendLog('[VPN] ' + msg);
      } catch (err) {
        const rawMsg = err?.message && err.message !== '[object Object]'
          ? err.message
          : (err?.data?.message || (typeof err === 'object' ? JSON.stringify(err) : String(err)));
        const errMsg = (rawMsg || 'Disconnect failed').replace(/^Error:\s*/, '');
        showToast(`Disconnect VPN Error: ${errMsg}`, 'error', 5000);
        appendLog('[VPN] Disconnect Error: ' + errMsg);
      } finally {
        if (btn) btn.disabled = false;
      }
    });

    document.getElementById('btn-refresh-opencode-sidecar')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-refresh-opencode-sidecar');
      const svg = btn?.querySelector('svg');
      if (svg) svg.classList.add('spinning');
      try {
        const fresh = await callBridge('refresh_opencode_sidecar');
        if (fresh) {
          handleOpenCodeUpdate(fresh);
          showToast(`Sidecar ${fresh.isOnline ? 'Online (Port ' + fresh.port + ')' : 'Offline'}`, fresh.isOnline ? 'success' : 'info', 2500);
        }
      } catch (err) {
        showToast('Sidecar refresh failed: ' + err.message, 'error', 3000);
      } finally {
        if (svg) svg.classList.remove('spinning');
      }
    });

    document.getElementById('btn-purge-sessions')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-purge-sessions');
      const svg = btn?.querySelector('svg');
      if (!state.opencode?.isOnline) {
        showToast('OpenCode Sidecar is offline', 'info', 2500);
        return;
      }
      if (btn) btn.disabled = true;
      if (svg) svg.classList.add('spinning');

      try {
        const res = await callBridge('purge_idle_sessions');
        const count = res?.deletedCount ?? 0;
        if (count > 0) {
          showToast(`Cleaned ${count} idle session${count === 1 ? '' : 's'}`, 'success', 3500);
          appendLog(`[OpenCode] Cleaned ${count} idle sessions.`);
        } else {
          showToast('No idle sessions to clean', 'info', 2500);
        }
        const fresh = await callBridge('refresh_opencode_sidecar');
        if (fresh) handleOpenCodeUpdate(fresh);
      } catch (err) {
        showToast(`Purge failed: ${err.message}`, 'error', 3000);
      } finally {
        if (svg) svg.classList.remove('spinning');
        if (btn) btn.disabled = false;
      }
    });

    // Feature 2: Service Management Controls (Start, Stop, Recheck)
    document.getElementById('btn-opencode-start')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-opencode-start');
      const textSpan = btn?.querySelector('span');
      if (btn) btn.disabled = true;
      if (textSpan) textSpan.textContent = 'Starting...';
      try {
        const res = await callBridge('start_opencode_service');
        if (res && res.success) {
          showToast(res.message || 'OpenCode Desktop launched successfully', 'success', 3500);
          if (res.state) handleOpenCodeUpdate(res.state);
        } else {
          showToast(res?.message || 'Could not start OpenCode service', 'error', 4000);
        }
      } catch (err) {
        showToast('Start OpenCode failed: ' + err.message, 'error', 3500);
      } finally {
        if (btn) btn.disabled = false;
        if (textSpan) textSpan.textContent = 'Start';
      }
    });

    document.getElementById('btn-opencode-stop')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-opencode-stop');
      const textSpan = btn?.querySelector('span');
      if (btn) btn.disabled = true;
      if (textSpan) textSpan.textContent = 'Stopping...';
      try {
        const res = await callBridge('stop_opencode_service');
        if (res && res.success) {
          showToast(res.message || 'OpenCode Desktop terminated', 'info', 3000);
          if (res.state) handleOpenCodeUpdate(res.state);
        } else {
          showToast(res?.message || 'Could not stop OpenCode service', 'error', 3500);
        }
      } catch (err) {
        showToast('Stop OpenCode failed: ' + err.message, 'error', 3500);
      } finally {
        if (btn) btn.disabled = false;
        if (textSpan) textSpan.textContent = 'Stop';
      }
    });

    document.getElementById('btn-opencode-recheck')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-opencode-recheck');
      const svg = btn?.querySelector('svg');
      if (svg) svg.classList.add('spinning');
      try {
        const fresh = await callBridge('recheck_opencode_service');
        if (fresh) {
          handleOpenCodeUpdate(fresh);
          showToast(`OpenCode status: ${fresh.isOnline ? 'Online (Port ' + fresh.port + ')' : 'Offline'}`, fresh.isOnline ? 'success' : 'info', 2500);
        }
      } catch (err) {
        showToast('Recheck failed: ' + err.message, 'error', 3000);
      } finally {
        if (svg) svg.classList.remove('spinning');
      }
    });
  }

  function handleOpenCodeUpdate(data) {
    if (!data) return;
    state.opencode = {
      isOnline: !!data.isOnline,
      port: data.port || 0,
      activeSessions: Array.isArray(data.activeSessions) ? data.activeSessions : []
    };
    renderOpenCodeCard();
  }

  function renderOpenCodeCard() {
    const badge = document.getElementById('opencode-sidecar-badge');
    const dot = document.getElementById('opencode-sidecar-dot');
    const text = document.getElementById('opencode-sidecar-text');
    const countEl = document.getElementById('opencode-sessions-count');
    const badgeIcon = document.getElementById('opencode-sessions-badge-icon');
    const listEl = document.getElementById('opencode-sessions-list');

    const isOnline = !!state.opencode?.isOnline;
    const port = state.opencode?.port || 0;
    const sessions = state.opencode?.activeSessions || [];

    // 1. Sidecar Status Header Pill & Service Controls
    const startBtn = document.getElementById('btn-opencode-start');
    const stopBtn = document.getElementById('btn-opencode-stop');
    if (startBtn) startBtn.style.display = isOnline ? 'none' : 'inline-flex';
    if (stopBtn) stopBtn.style.display = isOnline ? 'inline-flex' : 'none';

    if (badge && dot && text) {
      if (isOnline) {
        badge.style.background = 'rgba(16, 185, 129, 0.12)';
        badge.style.color = '#059669';
        badge.style.borderColor = 'rgba(16, 185, 129, 0.3)';
        dot.style.background = '#10b981';
        text.textContent = `Sidecar: Connected (Port ${port})`;
      } else {
        badge.style.background = 'rgba(100, 116, 139, 0.12)';
        badge.style.color = 'var(--text-muted, #64748b)';
        badge.style.borderColor = 'rgba(148, 163, 184, 0.25)';
        dot.style.background = '#94a3b8';
        text.textContent = 'Sidecar: Offline';
      }
    }

    // 2. Active Sessions Tray
    if (countEl) countEl.textContent = String(sessions.length);
    if (badgeIcon) {
      badgeIcon.style.color = (isOnline && sessions.length > 0) ? '#10b981' : 'var(--text-dim, #94a3b8)';
    }

    if (listEl) {
      listEl.innerHTML = '';

      if (!isOnline) {
        const emptyDiv = document.createElement('div');
        emptyDiv.className = 'empty-state';
        emptyDiv.style.cssText = 'font-size: 11px; color: var(--text-dim, #94a3b8); padding: 4px 0; font-style: italic;';
        emptyDiv.textContent = 'Sidecar offline (Click Start OpenCode or launch Desktop build)';
        listEl.appendChild(emptyDiv);
      } else if (sessions.length === 0) {
        const emptyDiv = document.createElement('div');
        emptyDiv.className = 'empty-state';
        emptyDiv.style.cssText = 'font-size: 11px; color: var(--text-dim, #94a3b8); padding: 4px 0; font-style: italic;';
        emptyDiv.textContent = 'Idle (No active sessions)';
        listEl.appendChild(emptyDiv);
      } else {
        sessions.forEach(s => {
          const item = document.createElement('div');
          item.className = 'opencode-session-item';

          const title = escapeHtml(s.title || 'Untitled Session');
          const dir = escapeHtml(s.directory || 'No directory');
          const isBusy = !!(s.isBusy || (s.agentState && s.agentState.toLowerCase() === 'busy') || (s.status && (s.status.toLowerCase() === 'busy' || s.status.toLowerCase() === 'running' || s.status.toLowerCase() === 'generating' || s.status.toLowerCase() === 'thinking')));

          const stateBadge = isBusy
            ? `<span class="badge-agent-state badge-busy"><span class="pulse-dot-amber"></span> Running / Thinking...</span>`
            : `<span class="badge-agent-state badge-idle"><span class="dot-green"></span> Idle</span>`;

          item.innerHTML = `
            <div class="opencode-session-info">
              <div style="display: flex; align-items: center; gap: 8px; margin-bottom: 2px;">
                <span class="opencode-session-title" title="${title}">${title}</span>
                ${stateBadge}
              </div>
              <div class="opencode-session-path" title="${dir}">${dir}</div>
            </div>
            <button class="btn-session-focus" title="Bring OpenCode to foreground">Focus</button>
          `;

          const focusBtn = item.querySelector('.btn-session-focus');
          if (focusBtn) {
            focusBtn.addEventListener('click', async (ev) => {
              ev.stopPropagation();
              focusBtn.disabled = true;
              focusBtn.textContent = 'Opening...';
              try {
                await callBridge('focus_opencode_session', {
                  sessionId: s.id,
                  directory: s.directory
                });
                showToast(`Focused session: ${s.title}`, 'info', 2500);
              } catch (err) {
                showToast(`Focus failed: ${err.message}`, 'error', 3000);
              } finally {
                setTimeout(() => {
                  focusBtn.disabled = false;
                  focusBtn.textContent = 'Focus';
                }, 1000);
              }
            });
          }

          listEl.appendChild(item);
        });
      }
    }
  }

  // Modern Dynamic Toast System
  function showToast(message, type = 'info', durationMs = 4500) {
    let cleanMessage = message;
    if (cleanMessage && typeof cleanMessage === 'object') {
      cleanMessage = cleanMessage.message || cleanMessage.error || JSON.stringify(cleanMessage);
    } else if (cleanMessage !== undefined && cleanMessage !== null) {
      cleanMessage = String(cleanMessage);
    } else {
      cleanMessage = '';
    }

    let container = document.getElementById('toast-container');
    if (!container) {
      container = document.createElement('div');
      container.id = 'toast-container';
      document.body.appendChild(container);
    }

    const toast = document.createElement('div');
    toast.className = `toast-item toast-${type}`;

    let iconSvg = '';
    if (type === 'success') {
      iconSvg = `<svg viewBox="0 0 24 24" width="16" height="16" stroke="#10b981" stroke-width="2.5" fill="none"><path d="M20 6L9 17l-5-5"/></svg>`;
    } else if (type === 'error') {
      iconSvg = `<svg viewBox="0 0 24 24" width="16" height="16" stroke="#ef4444" stroke-width="2.5" fill="none"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>`;
    } else {
      iconSvg = `<svg viewBox="0 0 24 24" width="16" height="16" stroke="#3b82f6" stroke-width="2" fill="none"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>`;
    }

    toast.innerHTML = `
      <span style="display: flex; align-items: center; flex-shrink: 0;">${iconSvg}</span>
      <span style="flex: 1; word-break: break-word;">${escapeHtml(cleanMessage)}</span>
    `;

    container.appendChild(toast);

    // A toast that has already gone cannot be read, so every message goes into a capped
    // history behind the bell in the header.
    recordToast(cleanMessage, type);

    setTimeout(() => {
      toast.classList.add('toast-out');
      setTimeout(() => {
        if (toast.parentNode) {
          toast.parentNode.removeChild(toast);
        }
      }, 250);
    }, durationMs);
  }

  const toastHistoryState = { items: [] };

  function recordToast(message, type) {
    toastHistoryState.items.unshift({ message, type: type || 'info', at: new Date().toISOString() });
    if (toastHistoryState.items.length > 50) toastHistoryState.items.length = 50;
    updateToastBadge();
  }

  function updateToastBadge() {
    const badge = document.getElementById('toast-history-count');
    if (badge) {
      const n = toastHistoryState.items.length;
      badge.textContent = String(n);
      badge.style.display = n > 0 ? '' : 'none';
    }
  }

  function toggleToastHistory(forceOpen) {
    const panel = document.getElementById('toast-history-panel');
    const badge = document.getElementById('toast-history-count');
    if (!panel) return;

    const willOpen = typeof forceOpen === 'boolean' ? forceOpen : panel.classList.contains('open') === false;
    panel.classList.toggle('open', willOpen);
    if (willOpen) {
      renderToastHistory();
      if (badge) badge.style.display = 'none';
    }
  }

  function renderToastHistory() {
    const list = document.getElementById('toast-history-list');
    if (!list) return;

    list.innerHTML = '';
    if (toastHistoryState.items.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'toast-history-empty';
      empty.textContent = 'No notifications yet.';
      list.appendChild(empty);
      return;
    }

    toastHistoryState.items.forEach(item => {
      const row = document.createElement('div');
      row.className = 'toast-history-item is-' + item.type;

      const when = document.createElement('span');
      when.className = 'toast-history-time';
      const d = new Date(item.at);
      when.textContent = isNaN(d.getTime()) ? '' : d.toLocaleTimeString();

      const body = document.createElement('span');
      body.className = 'toast-history-message';
      body.textContent = item.message;

      row.appendChild(when);
      row.appendChild(body);
      list.appendChild(row);
    });
  }

  function clearToastHistory() {
    toastHistoryState.items = [];
    renderToastHistory();
    updateToastBadge();
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

  // Feature 8: Centralized Structured Logging System & Filter Tags
  state.logs = [];
  state.currentLogFilter = 'all';
  state.currentPrompt = null;

  function appendLog(text, tag = null, timestamp = null) {
    if (!text) return;
    let rawText = text;
    if (typeof rawText === 'object') {
      rawText = rawText.message || rawText.error || JSON.stringify(rawText);
    } else {
      rawText = String(rawText);
    }
    const time = timestamp || new Date().toLocaleTimeString();

    // Deduce tag if not explicitly provided
    let inferredTag = tag;
    if (!inferredTag) {
      if (rawText.startsWith('[System]') || rawText.startsWith('[Config]') || rawText.startsWith('[Metrics]')) {
        inferredTag = 'System';
      } else if (rawText.startsWith('[OpenCode]') || rawText.startsWith('[Sidecar]')) {
        inferredTag = 'OpenCode';
      } else if (rawText.startsWith('[WARP]') || rawText.startsWith('[QuotaReset]') || rawText.startsWith('[ProtonVPN]')) {
        inferredTag = 'WARP';
      } else {
        inferredTag = 'System';
      }
    }

    let cleanText = rawText;
    if (cleanText.startsWith(`[${inferredTag}]`)) {
      cleanText = cleanText.substring(inferredTag.length + 2).trim();
    }

    const logEntry = {
      timestamp: time,
      tag: inferredTag,
      text: cleanText
    };

    state.logs.push(logEntry);
    if (state.logs.length > 1000) {
      state.logs.shift();
    }

    if (shouldShowLogEntry(logEntry, state.currentLogFilter)) {
      renderLogEntryDom(logEntry);
    }
  }

  function shouldShowLogEntry(entry, filter) {
    if (!filter || filter.toLowerCase() === 'all') return true;
    const f = filter.toLowerCase();
    const t = (entry.tag || '').toLowerCase();
    if (f === 'system') return t === 'system';
    if (f === 'opencode') return t === 'opencode';
    if (f === 'warp') return t === 'warp' || t === 'quotareset';
    return t === f;
  }

  function renderLogEntryDom(entry) {
    const logsEl = document.getElementById('logs-container');
    if (!logsEl) return;

    const t = (entry.tag || '').toLowerCase();
    let tagClass = 'log-tag-default';
    let tagLabel = entry.tag || 'System';
    if (t === 'system') {
      tagClass = 'log-tag-system';
      tagLabel = 'System';
    } else if (t === 'opencode') {
      tagClass = 'log-tag-opencode';
      tagLabel = 'OpenCode';
    } else if (t === 'warp' || t === 'quotareset') {
      tagClass = 'log-tag-warp';
      tagLabel = 'WARP';
    }

    const line = document.createElement('div');
    line.className = 'log-line';
    line.innerHTML = `
      <span class="log-time">[${escapeHtml(entry.timestamp)}]</span>
      <span class="log-tag ${tagClass}">${escapeHtml(tagLabel)}</span>
      <span class="log-text">${escapeHtml(entry.text)}</span>
    `;

    logsEl.appendChild(line);
    logsEl.scrollTop = logsEl.scrollHeight;
  }

  function refreshLogsDisplay() {
    const logsEl = document.getElementById('logs-container');
    if (!logsEl) return;
    logsEl.innerHTML = '';
    const visibleEntries = state.logs.filter(entry => shouldShowLogEntry(entry, state.currentLogFilter));
    visibleEntries.forEach(entry => renderLogEntryDom(entry));
    logsEl.scrollTop = logsEl.scrollHeight;
  }

  function setupLogFilterListeners() {
    document.querySelectorAll('.log-filter-bar .log-filter-pill').forEach(pill => {
      pill.addEventListener('click', () => {
        document.querySelectorAll('.log-filter-bar .log-filter-pill').forEach(p => p.classList.remove('active'));
        pill.classList.add('active');
        state.currentLogFilter = pill.getAttribute('data-filter') || 'all';
        refreshLogsDisplay();
      });
    });

    document.getElementById('btn-clear-logs')?.addEventListener('click', () => {
      state.logs = [];
      const logsEl = document.getElementById('logs-container');
      if (logsEl) logsEl.innerHTML = '';
      showToast('Logs cleared', 'info', 1500);
    });
  }

  // Feature 1: Interactive Permissions & Questions
  function handleOpenCodePrompt(prompt) {
    if (!prompt) return;
    state.currentPrompt = prompt;

    const modal = document.getElementById('modal-opencode-prompt');
    const titleEl = document.getElementById('opencode-prompt-title');
    const typeEl = document.getElementById('opencode-prompt-type');
    const timeEl = document.getElementById('opencode-prompt-time');
    const bodyEl = document.getElementById('opencode-prompt-body');
    const cmdBox = document.getElementById('opencode-prompt-command-box');
    const cmdEl = document.getElementById('opencode-prompt-command');
    const inputGroup = document.getElementById('opencode-prompt-input-group');
    const inputEl = document.getElementById('opencode-prompt-input');
    const approveBtn = document.getElementById('btn-approve-opencode-prompt');

    if (!modal) return;

    const isQuestion = (prompt.type || '').toLowerCase().includes('question');
    if (titleEl) titleEl.textContent = prompt.title || (isQuestion ? 'OpenCode Question' : 'OpenCode Permission Requested');
    if (typeEl) typeEl.textContent = isQuestion ? 'User Input' : 'Tool Execution';
    if (timeEl) timeEl.textContent = new Date().toLocaleTimeString();
    if (bodyEl) bodyEl.textContent = prompt.prompt || (isQuestion ? 'The agent is asking for your input:' : 'The agent requests permission to execute the following:');

    if (prompt.command) {
      if (cmdBox) cmdBox.style.display = 'block';
      if (cmdEl) cmdEl.textContent = prompt.command;
    } else {
      if (cmdBox) cmdBox.style.display = 'none';
    }

    if (isQuestion) {
      if (inputGroup) inputGroup.style.display = 'block';
      if (inputEl) {
        inputEl.value = '';
        setTimeout(() => inputEl.focus(), 150);
      }
      if (approveBtn) approveBtn.textContent = 'Submit Answer';
    } else {
      if (inputGroup) inputGroup.style.display = 'none';
      if (approveBtn) approveBtn.textContent = '✓ Approve / Yes';
    }

    modal.classList.add('open');
    showToast(`OpenCode Prompt: ${prompt.title || 'Action Requested'}`, 'warn', 5000);
  }

  function setupPromptModalListeners() {
    const modal = document.getElementById('modal-opencode-prompt');
    const approveBtn = document.getElementById('btn-approve-opencode-prompt');
    const rejectBtn = document.getElementById('btn-reject-opencode-prompt');
    const inputEl = document.getElementById('opencode-prompt-input');

    approveBtn?.addEventListener('click', async () => {
      if (!state.currentPrompt) {
        modal?.classList.remove('open');
        return;
      }
      const p = state.currentPrompt;
      const answer = (p.type || '').toLowerCase().includes('question') ? (inputEl?.value || '') : null;
      approveBtn.disabled = true;
      try {
        await callBridge('reply_opencode_prompt', {
          id: p.id,
          type: p.type || 'permission',
          approved: true,
          answer: answer
        });
        showToast('Prompt approved / replied', 'success', 2500);
      } catch (err) {
        showToast('Reply failed: ' + err.message, 'error', 3000);
      } finally {
        approveBtn.disabled = false;
        state.currentPrompt = null;
        modal?.classList.remove('open');
      }
    });

    rejectBtn?.addEventListener('click', async () => {
      if (!state.currentPrompt) {
        modal?.classList.remove('open');
        return;
      }
      const p = state.currentPrompt;
      rejectBtn.disabled = true;
      try {
        await callBridge('reply_opencode_prompt', {
          id: p.id,
          type: p.type || 'permission',
          approved: false
        });
        showToast('Prompt rejected / denied', 'info', 2500);
      } catch (err) {
        showToast('Reject failed: ' + err.message, 'error', 3000);
      } finally {
        rejectBtn.disabled = false;
        state.currentPrompt = null;
        modal?.classList.remove('open');
      }
    });

    // Close modal on Escape
    window.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && modal?.classList.contains('open')) {
        rejectBtn?.click();
      }
    });
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

    document.getElementById('btn-refresh-state')?.addEventListener('click', loadInitialState);
    document.getElementById('btn-toast-history')?.addEventListener('click', (e) => {
      e.stopPropagation();
      toggleToastHistory();
      document.getElementById('btn-toast-history')?.setAttribute('aria-expanded', String(document.getElementById('toast-history-panel').classList.contains('open')));
    });
    document.getElementById('btn-clear-toast-history')?.addEventListener('click', (e) => {
      e.stopPropagation();
      clearToastHistory();
    });
    // Clicking anywhere else closes the panel, the same way the other header overlays work.
    document.addEventListener('click', (e) => {
      const panel = document.getElementById('toast-history-panel');
      if (!panel || !panel.classList.contains('open')) return;
      if (e.target.closest('.toast-history-wrap')) return;
      panel.classList.remove('open');
    });

    document.getElementById('btn-copy-lan-url')?.addEventListener('click', () => {
      const urlText = document.getElementById('dash-lan-url')?.textContent || '';
      navigator.clipboard.writeText(urlText);
      appendLog('Copied Web Host URL: ' + urlText);
    });
    document.getElementById('btn-open-lan-url')?.addEventListener('click', () => {
      const urlText = document.getElementById('dash-lan-url')?.textContent || '';
      callBridge('open_url_external', { url: urlText });
    });
    document.getElementById('btn-toggle-dash-url-mode')?.addEventListener('click', () => {
      if (!state.lanProxyEnabled) {
        appendLog('LAN connection is turned off. Turn on LAN from LAN Remote & QR to view network links.');
        return;
      }
      state.dashUrlMode = state.dashUrlMode === 'local' ? 'lan' : 'local';
      renderRunningPorts();
      appendLog('Switched Dashboard Web Host view to: ' + (state.dashUrlMode === 'lan' ? 'Network (LAN)' : 'Local (127.0.0.1)'));
    });

    document.getElementById('btn-copy-lan-tab-url')?.addEventListener('click', () => {
      const urlText = document.getElementById('lan-full-url')?.textContent || '';
      copyToClipboard(urlText);
      appendLog('Copied LAN Remote URL: ' + urlText);
    });
    document.getElementById('btn-open-lan-tab-url')?.addEventListener('click', () => {
      const urlText = document.getElementById('lan-full-url')?.textContent || '';
      callBridge('open_url_external', { url: urlText });
    });
    document.getElementById('btn-toggle-lan-url-mode')?.addEventListener('click', () => {
      if (!state.lanProxyEnabled) {
        appendLog('LAN connection is turned off. Turn on LAN to view network links.');
        return;
      }
      state.lanUrlMode = state.lanUrlMode === 'lan' ? 'local' : 'lan';
      renderRunningPorts();
      appendLog('Switched LAN Remote view to: ' + (state.lanUrlMode === 'lan' ? 'Network (LAN)' : 'Local (127.0.0.1)'));
    });

    document.getElementById('btn-toggle-lan-network')?.addEventListener('click', async () => {
      const targetState = !state.lanProxyEnabled;
      try {
        const res = await callBridge('toggle_lan_proxy', { enabled: targetState });
        if (res) {
          state.lanProxyEnabled = !!res.lanProxyEnabled;
          if (res.runningPorts) state.runningPorts = res.runningPorts;
          if (!state.lanProxyEnabled) {
            state.dashUrlMode = 'local';
            state.lanUrlMode = 'local';
            appendLog('LAN network connection turned OFF. All links switched to 127.0.0.1.');
          } else {
            state.lanUrlMode = 'lan';
            appendLog('LAN network connection turned ON (192.168 active).');
          }
          renderRunningPorts();
          // Which ports bound, and whether the firewall now matters, both changed.
          loadLanState(true);
        }
      } catch (err) {
        appendLog('Failed to toggle LAN network: ' + (err.message || err));
      }
    });

    document.getElementById('btn-lan-refresh')?.addEventListener('click', () => loadLanState(true));
    document.getElementById('btn-lan-firewall-allow')?.addEventListener('click', () => requestFirewallRule());
    document.getElementById('btn-lan-firewall-remove')?.addEventListener('click', () => removeFirewallRule());
    document.getElementById('lan-qr-retry')?.addEventListener('click', () => {
      lanQrUrl = '';
      renderLanPage();
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
        try {
          const res = await callBridge('create_antigravity_profile', { profileName: name });
          if (res) {
            if (res.activeProfile !== undefined) state.antigravity.activeProfile = res.activeProfile;
            if (res.profiles) state.antigravity.profiles = res.profiles;
            renderProfiles();
            renderQuickProfileSelect();
            updateHeaderStatus();
          }
        } catch (e) {
          console.error('Create profile failed:', e);
        }
        modal.classList.remove('open');
      }
    });

    document.getElementById('btn-clear-logs')?.addEventListener('click', () => {
      const logsEl = document.getElementById('logs-container');
      if (logsEl) logsEl.innerHTML = '<div>[System] Logs cleared.</div>';
    });

    // Standalone Terminals Window Popouts
    const openStandalone = () => callBridge('open_standalone_terminals');
    document.getElementById('btn-popout-terminals')?.addEventListener('click', openStandalone);
    document.getElementById('btn-popout-terminals-dash')?.addEventListener('click', openStandalone);
    document.getElementById('btn-open-standalone-hdr')?.addEventListener('click', openStandalone);

    // Native System Shell Launchers (Standard & Administrator)
    document.getElementById('btn-shell-cmd')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'cmd', asAdmin: false });
      appendLog('[Shell] Launched Command Prompt (cmd.exe)');
    });
    document.getElementById('btn-shell-cmd-admin')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'cmd', asAdmin: true });
      appendLog('[Shell] Launched Command Prompt (Admin)');
    });
    document.getElementById('btn-shell-ps')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'powershell', asAdmin: false });
      appendLog('[Shell] Launched Windows PowerShell');
    });
    document.getElementById('btn-shell-ps-admin')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'powershell', asAdmin: true });
      appendLog('[Shell] Launched Windows PowerShell (Admin)');
    });
  }

  // Load Initial State from C# Backend
  async function loadInitialState() {
    try {
      const res = await callBridge('get_initial_state');
      if (res) {
        state.version = res.version || '';
        renderAboutVersion();
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
        if (res.opencode) {
          state.opencode = res.opencode;
          renderOpenCodeCard();
        }
        if (res.settings) {
          state.settings = res.settings;
          const autoCheck = document.getElementById('setting-autostart-checkbox');
          const minCheck = document.getElementById('setting-minimized-checkbox');
          const trayCheck = document.getElementById('setting-tray-minimize-checkbox');
          const notifCheck = document.getElementById('setting-notifications-checkbox');
          const hotkeyCheck = document.getElementById('setting-global-hotkey-checkbox');
          if (autoCheck) autoCheck.checked = !!res.settings.autostart;
          if (minCheck) minCheck.checked = !!res.settings.startMinimized;
          if (trayCheck) trayCheck.checked = res.settings.minimizeToTray !== false;
          if (notifCheck) notifCheck.checked = res.settings.notifications !== false;
          if (hotkeyCheck) hotkeyCheck.checked = res.settings.globalHotkey !== false;
          state.isIpMasked = !!res.settings.maskPublicIp;
          state.isShortcutsMasked = res.settings.maskShortcutPaths !== undefined
            ? !!res.settings.maskShortcutPaths
            : localStorage.getItem('agents_hub_mask_shortcuts') === 'true';
          const maskPathsCheck = document.getElementById('setting-mask-paths-checkbox');
          if (maskPathsCheck) maskPathsCheck.checked = state.isShortcutsMasked;
          const embedShellsCheck = document.getElementById('setting-embed-shells-checkbox');
          if (embedShellsCheck) embedShellsCheck.checked = !!res.settings.embedShellsInTerminalWorkspace;
          const hideMainCheck = document.getElementById('setting-hide-main-on-cli-checkbox');
          if (hideMainCheck) hideMainCheck.checked = !!res.settings.hideMainOnCliOpen;

          // Proton VPN Provider preference
          const protonEnabled = res.settings.enableProtonVpnProvider === true;
          state.settings.enableProtonVpnProvider = protonEnabled;
          localStorage.setItem('agents_hub_enable_proton', protonEnabled);
          const prefProtonCheck = document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox');
          if (prefProtonCheck) prefProtonCheck.checked = protonEnabled;

          // Hardware Monitor preference (default false / disabled)
          const hwEnabled = res.settings.enableHardwareMonitor === true;
          state.settings.enableHardwareMonitor = hwEnabled;
          localStorage.setItem('agents_hub_enable_hardware_monitor', hwEnabled);
          const prefHwCheck = document.getElementById('pref-enable-hardware-monitor') || document.getElementById('setting-enable-hardware-monitor-checkbox');
          if (prefHwCheck) prefHwCheck.checked = hwEnabled;
          updateHardwareMonitorVisibility();

          // Apply initial theme & accent color
          const activeTheme = localStorage.getItem('agents_hub_theme') || res.settings.theme || 'dark';
          const activeColor = localStorage.getItem('agents_hub_accent_color') || res.settings.accentColor || '#4049B9';
          applyAdaptiveTheme(activeTheme, activeColor);

          // The dashboard owns the theme; the C# side keeps its own copy in the registry and
          // ThemeManager seeds from it at startup. If the two disagree, push ours so the CLI
          // windows match this view instead of staying on a stale dark value.
          const appliedTheme = localStorage.getItem('agents_hub_theme') || 'light';
          const backendTheme = res.settings.theme === 'light' ? 'light' : 'dark';
          if (appliedTheme !== backendTheme) {
            callBridge('save_general_settings', { settings: { theme: appliedTheme } });
          }

          // Update VPN Provider UI conditionally
          updateVpnProviderUi();
        }

        // Fetch public IP initially (dual-stack IPv4 & IPv6)
        callBridge('get_public_ip').then(r => {
          if (r) {
            if (r.ipv4) state.ipv4 = r.ipv4;
            if (r.ipv6) state.ipv6 = r.ipv6;
            if (r.ip) state.publicIp = r.ip;
            if (!state.ipv4 && r.ip && !r.ip.includes(':')) state.ipv4 = r.ip;
            if (!state.ipv6 && r.ip && r.ip.includes(':')) state.ipv6 = r.ip;
            state.countryCode = r.countryCode || '';
            state.country = r.country || '';
            state.countryEmoji = r.countryEmoji || '';
            state.city = r.city || '';
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

        if (res.runningPorts) {
          state.runningPorts = res.runningPorts;
        }
        state.lanProxyPort = res.lanProxyPort || 9191;
        state.lanProxyEnabled = res.lanProxyEnabled !== undefined ? !!res.lanProxyEnabled : true;
        if (!state.lanProxyEnabled) {
          state.dashUrlMode = 'local';
          state.lanUrlMode = 'local';
        }

        updateHeaderStatus();
        updateDashboardCliRows();
        renderDashboardShortcuts();
        renderPublicIpDisplay();
        renderQuickProfileSelect();
        renderProfiles();
        renderPromptModelsDropdown();
        renderPromptPresets();
        renderProvidersList();
        renderDevToolsList();
        renderRunningPorts();
      }
    } catch (err) {
      console.error('Failed to load initial state:', err);
    }
  }

  // Claude Code Gateway Engine Configuration Card
  function setupClaudeGatewayConfig() {
    const card = document.getElementById('claude-gateway-config-card');
    if (!card) return;

    const toggleHeader = document.getElementById('btn-toggle-claude-config');
    const collapseBtn = document.getElementById('btn-collapse-claude-config');
    const chevronText = document.getElementById('claude-config-chevron');
    const chevronIcon = document.getElementById('claude-config-chevron-icon');
    const body = document.getElementById('claude-config-body');
    const badge = document.getElementById('claude-config-status-badge');
    const injectionCheckbox = document.getElementById('claude-injection-enable-checkbox');

    const providerSelect = document.getElementById('claude-provider-select');
    const btnAddProvider = document.getElementById('btn-add-claude-provider');
    const btnDeleteProvider = document.getElementById('btn-delete-claude-provider');
    const baseUrlInput = document.getElementById('claude-baseurl-input');
    const apiKeyInput = document.getElementById('claude-apikey-input');
    const btnToggleEye = document.getElementById('btn-toggle-show-claude-key');
    const eyeIcon = document.getElementById('icon-claude-key-eye');
    const btnSaveProvider = document.getElementById('btn-save-claude-provider');

    const balanceText = document.getElementById('claude-key-balance-text');
    const modelsCountText = document.getElementById('claude-free-models-count-text');
    const btnFetchModels = document.getElementById('btn-fetch-models-live');
    const btnLogout = document.getElementById('btn-claude-logout');

    let providersMap = {};
    let isOpen = localStorage.getItem('agents_hub_claude_config_open') === 'true';

    function updateCollapseUi() {
      if (!body) return;
      body.style.display = isOpen ? 'block' : 'none';
      if (chevronText) chevronText.textContent = isOpen ? 'Close Config' : 'Open Config';
      if (chevronIcon) chevronIcon.style.transform = isOpen ? 'rotate(180deg)' : 'rotate(0deg)';
      localStorage.setItem('agents_hub_claude_config_open', isOpen ? 'true' : 'false');
    }

    const toggleCollapse = () => {
      isOpen = !isOpen;
      updateCollapseUi();
    };

    toggleHeader?.addEventListener('click', (e) => {
      if (e.target.closest('#btn-collapse-claude-config') || e.target.closest('#claude-injection-enable-checkbox')) return;
      toggleCollapse();
    });
    collapseBtn?.addEventListener('click', (e) => {
      e.stopPropagation();
      toggleCollapse();
    });

    function updateBadge(enabled, hasKey) {
      if (!badge) return;
      if (!enabled) {
        badge.textContent = 'Injection Disabled (Stock Anthropic Mode)';
        badge.style.background = 'rgba(100, 116, 139, 0.15)';
        badge.style.color = 'var(--text-dim)';
      } else {
        badge.textContent = hasKey ? 'Configured' : 'Key Needed';
        badge.style.background = hasKey ? 'rgba(34, 197, 94, 0.15)' : 'rgba(245, 158, 11, 0.15)';
        badge.style.color = hasKey ? 'var(--accent-green)' : 'var(--warning-amber)';
      }
    }

    // Eye toggle for key masking
    btnToggleEye?.addEventListener('click', (e) => {
      e.stopPropagation();
      if (!apiKeyInput) return;
      const isPassword = apiKeyInput.type === 'password';
      apiKeyInput.type = isPassword ? 'text' : 'password';
      if (eyeIcon) {
        eyeIcon.innerHTML = isPassword
          ? '<path d="m2 2 20 20"/><path d="M9.88 9.88a3 3 0 1 0 4.24 4.24"/><path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68"/><path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61"/>'
          : '<path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>';
      }
    });

    async function checkKeyHealth(key) {
      if (!balanceText) return;
      if (!key) {
        balanceText.textContent = 'Ready (No key set)';
        balanceText.style.color = 'var(--text-dim)';
        return;
      }
      balanceText.textContent = 'Checking key balance...';
      try {
        const res = await callBridge('check_openrouter_key', { apiKey: key });
        if (res && res.valid) {
          balanceText.textContent = res.message || 'Key valid';
          balanceText.style.color = 'var(--text-main)';
        } else {
          balanceText.textContent = res?.message || 'Invalid or unverified key';
          balanceText.style.color = 'var(--accent-red)';
        }
      } catch (err) {
        balanceText.textContent = 'Check failed: ' + (err.message || err);
        balanceText.style.color = 'var(--text-dim)';
      }
    }

    async function loadFreeModelsCount() {
      try {
        const res = await callBridge('get_openrouter_free_models', { force: false });
        if (res && res.models && modelsCountText) {
          modelsCountText.textContent = `${res.models.length} free models ready`;
        }
      } catch { }
    }

    function onProviderSelected(provId) {
      const p = providersMap[provId];
      if (!p) return;
      const baseUrl = p.baseUrl || p.BaseUrl || '';
      const apiKey = p.apiKey || p.ApiKey || '';
      const isPreset = !!(p.isPreset !== undefined ? p.isPreset : (p.IsPreset !== undefined ? p.IsPreset : provId.startsWith('preset_')));
      if (baseUrlInput) baseUrlInput.value = baseUrl;
      if (apiKeyInput) apiKeyInput.value = apiKey;
      if (btnDeleteProvider) {
        btnDeleteProvider.style.display = isPreset ? 'none' : 'inline-block';
      }
      const enabled = injectionCheckbox ? injectionCheckbox.checked : true;
      updateBadge(enabled, !!apiKey);
      if (apiKey) {
        checkKeyHealth(apiKey);
      } else if (balanceText) {
        balanceText.textContent = 'Ready (No key set)';
        balanceText.style.color = 'var(--text-dim)';
      }
    }

    function populateProviders(providers, selectedId) {
      providersMap = {};
      if (!providerSelect) return;
      providerSelect.innerHTML = '';
      (providers || []).forEach(p => {
        const id = p.id || p.Id;
        const name = p.name || p.Name || id;
        providersMap[id] = p;
        const opt = document.createElement('option');
        opt.value = id;
        opt.textContent = name;
        if (id === selectedId) opt.selected = true;
        providerSelect.appendChild(opt);
      });
      const activeId = selectedId || (providers && providers[0] ? (providers[0].id || providers[0].Id) : 'preset_local_proxy');
      providerSelect.value = activeId;
      onProviderSelected(activeId);
    }

    // Provider select change
    providerSelect?.addEventListener('change', async () => {
      const provId = providerSelect.value;
      onProviderSelected(provId);
      try {
        await callBridge('select_claude_gateway_provider', { providerId: provId });
      } catch (err) {
        console.warn('Select provider error:', err);
      }
    });

    // Master injection toggle
    injectionCheckbox?.addEventListener('change', async () => {
      const enabled = injectionCheckbox.checked;
      try {
        const res = await callBridge('toggle_claude_injection', { enabled });
        const curProv = providersMap[providerSelect ? providerSelect.value : ''];
        const curKey = curProv ? (curProv.apiKey || curProv.ApiKey || '') : '';
        updateBadge(enabled, !!curKey);
        showToast(enabled ? 'Claude environment injection enabled' : 'Stock Anthropic mode active (injection disabled)', 'info', 3000);
      } catch (err) {
        showToast('Failed to toggle injection: ' + err.message, 'error', 3500);
      }
    });

    // Add Custom Provider
    btnAddProvider?.addEventListener('click', async (e) => {
      e.stopPropagation();
      const name = prompt('Enter a name for the new Gateway Provider (e.g. Groq Gateway, TokenHarbor):');
      if (!name || !name.trim()) return;
      try {
        const res = await callBridge('add_claude_gateway_provider', {
          name: name.trim(),
          baseUrl: 'https://openrouter.ai/api/v1',
          apiKey: ''
        });
        if (res && res.provider) {
          const newProv = res.provider;
          const newId = newProv.id || newProv.Id;
          const newName = newProv.name || newProv.Name || newId;
          providersMap[newId] = newProv;
          const opt = document.createElement('option');
          opt.value = newId;
          opt.textContent = newName;
          opt.selected = true;
          providerSelect.appendChild(opt);
          providerSelect.value = newId;
          onProviderSelected(newId);
          showToast(`Provider "${newName}" added`, 'success', 3000);
        }
      } catch (err) {
        showToast('Add provider failed: ' + err.message, 'error', 3500);
      }
    });

    // Delete Custom Provider
    btnDeleteProvider?.addEventListener('click', async (e) => {
      e.stopPropagation();
      const provId = providerSelect.value;
      const p = providersMap[provId];
      const isPreset = p ? !!(p.isPreset !== undefined ? p.isPreset : p.IsPreset) : provId.startsWith('preset_');
      if (!p || isPreset) return;
      const provName = p.name || p.Name || provId;
      const confirmed = confirm(`Delete custom provider "${provName}"?`);
      if (!confirmed) return;
      try {
        const res = await callBridge('delete_claude_gateway_provider', { providerId: provId });
        if (res && res.success) {
          delete providersMap[provId];
          const opt = providerSelect.querySelector(`option[value="${provId}"]`);
          if (opt) opt.remove();
          const nextId = res.selectedGatewayId || 'preset_local_proxy';
          providerSelect.value = nextId;
          onProviderSelected(nextId);
          showToast(`Provider "${provName}" deleted`, 'info', 3000);
        }
      } catch (err) {
        showToast('Delete provider failed: ' + err.message, 'error', 3500);
      }
    });

    // Save Provider
    btnSaveProvider?.addEventListener('click', async (e) => {
      e.stopPropagation();
      const provId = providerSelect ? providerSelect.value : 'preset_local_proxy';
      const p = providersMap[provId];
      const provName = p ? (p.name || p.Name || provId) : provId;
      const baseUrl = baseUrlInput?.value?.trim() || '';
      const apiKey = apiKeyInput?.value?.trim() || '';

      btnSaveProvider.disabled = true;
      try {
        const res = await callBridge('save_claude_gateway_config', {
          providerId: provId,
          name: provName,
          baseUrl: baseUrl,
          apiKey: apiKey
        });
        if (res && res.success) {
          if (p) {
            p.baseUrl = baseUrl;
            p.apiKey = apiKey;
            p.BaseUrl = baseUrl;
            p.ApiKey = apiKey;
          }
          showToast('Gateway provider saved', 'success', 3000);
          const enabled = injectionCheckbox ? injectionCheckbox.checked : true;
          updateBadge(enabled, !!apiKey);
          if (apiKey && (baseUrl.includes('openrouter.ai') || baseUrl.includes('127.0.0.1:9230') || provId.startsWith('preset_'))) {
            await checkKeyHealth(apiKey);
          } else if (balanceText) {
            balanceText.textContent = apiKey ? 'Key configured' : 'Ready (No key set)';
            balanceText.style.color = 'var(--text-dim)';
          }
        }
      } catch (err) {
        showToast('Save failed: ' + err.message, 'error', 3500);
      } finally {
        btnSaveProvider.disabled = false;
      }
    });

    // Fetch free models live
    btnFetchModels?.addEventListener('click', async () => {
      const svg = btnFetchModels.querySelector('svg');
      if (svg) svg.classList.add('spinning');
      btnFetchModels.disabled = true;
      try {
        const res = await callBridge('refresh_openrouter_free_models');
        if (res && res.models) {
          if (modelsCountText) modelsCountText.textContent = `${res.models.length} free models available`;
          showToast(`Fetched ${res.models.length} free models from OpenRouter`, 'success', 3500);
        }
      } catch (err) {
        showToast('Fetch failed: ' + err.message, 'error', 3500);
      } finally {
        if (svg) svg.classList.remove('spinning');
        btnFetchModels.disabled = false;
      }
    });

    // Claude /logout
    btnLogout?.addEventListener('click', async () => {
      const confirmed = confirm('This will run "claude /logout" to clear stored Anthropic login tokens and prevent OAuth token precedence conflicts. Proceed?');
      if (!confirmed) return;

      btnLogout.disabled = true;
      try {
        const res = await callBridge('run_claude_logout');
        if (res && res.success) {
          showToast('Claude login credentials cleared.', 'success', 4000);
          appendLog('[Claude] Cleared official Anthropic credentials via /logout.');
        } else {
          showToast(res?.message || 'Logout failed', 'error', 3500);
        }
      } catch (err) {
        showToast('Logout error: ' + err.message, 'error', 3500);
      } finally {
        btnLogout.disabled = false;
      }
    });

    // Initial load
    callBridge('get_claude_gateway_config').then(res => {
      if (res) {
        if (injectionCheckbox) {
          injectionCheckbox.checked = res.injectionEnabled !== false;
        }
        populateProviders(res.providers || [], res.selectedGatewayId || 'preset_local_proxy');
        const activeProv = res.activeProvider || (res.providers && res.providers[0]);
        const activeKey = activeProv ? (activeProv.apiKey || activeProv.ApiKey || '') : '';
        updateBadge(res.injectionEnabled !== false, !!activeKey);
      }
    }).catch(err => {
      console.warn('Failed to load claude gateway config:', err);
    });

    updateCollapseUi();
    loadFreeModelsCount();
  }

  // Initialize App on DOM Ready
  window.addEventListener('DOMContentLoaded', () => {
    try {
      // Load local storage theme immediately to avoid flash
      const savedTheme = localStorage.getItem('agents_hub_theme') || 'dark';
      const savedColor = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
      applyAdaptiveTheme(savedTheme, savedColor);
    } catch (e) {
      console.warn('applyAdaptiveTheme error:', e);
    }

    const initSteps = [
      ['Navigation', setupNavigation],
      ['Terminal', initTerminal],
      ['TerminalPresets', setupTerminalPresets],
      ['Dependencies', setupDependencies],
      ['PromptEnhancer', setupPromptEnhancer],
      ['ProvidersView', setupProvidersView],
      ['ClaudeConfig', setupClaudeGatewayConfig],
      ['SettingsView', setupSettingsView],
      ['ShortcutEditor', setupShortcutEditor],
      ['DevToolsEditor', setupDevToolsEditor],
      ['CommandPalette', setupCommandPalette],
      ['QuotaAndIpWidget', setupQuotaAndIpWidget],
      ['RenderOpenCode', renderOpenCodeCard],
      ['PromptModal', setupPromptModalListeners],
      ['LogFilters', setupLogFilterListeners],
      ['GlobalActions', setupGlobalActions],
      ['InitialState', loadInitialState],
      ['HardwareTelemetry', setupHardwareTelemetry]
    ];

    initSteps.forEach(([name, fn]) => {
      try {
        fn();
      } catch (err) {
        console.error(`Failed during ${name} setup:`, err);
      }
    });
    window.AgentsHub = {
      state,
      callBridge,
      updateVpnProviderUi,
      updateHardwareMonitorVisibility,
      showToast
    };

    window.addEventListener('preference-changed', (e) => {
      if (e.detail && e.detail.key === 'enableProtonVpnProvider') {
        state.settings.enableProtonVpnProvider = !!e.detail.value;
        if (!state.settings.enableProtonVpnProvider && state.vpnProvider === 'proton') {
          state.vpnProvider = 'warp';
          localStorage.setItem('agents_hub_vpn_provider', 'warp');
        }
        updateVpnProviderUi();
      }
      if (e.detail && e.detail.key === 'enableHardwareMonitor') {
        state.settings.enableHardwareMonitor = !!e.detail.value;
        updateHardwareMonitorVisibility();
      }
    });
  });

})();
