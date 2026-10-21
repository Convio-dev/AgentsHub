  function getWebPortsList() {
    if (state.runningPorts && state.runningPorts.length > 0) {
      return state.runningPorts.map(p => {
        if (!p || typeof p !== 'object') return p;
        if (p.id === 'hermes') {
          const h = state.clis['Hermes'] || {};
          return { ...p, state: h.state || 'Offline', isRunning: h.state === 'Running' };
        }
        if (p.id === 'claude_code') {
          const c = state.clis['ClaudeCode'] || {};
          return { ...p, state: c.state || 'Running', isRunning: true };
        }
        if (p.id === 'antigravity_cli' || (typeof p.id === 'string' && p.id.startsWith('antigravity_cli:'))) {
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
        renderLanPage();
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
  window.lanLoading = false;
  var lanLoading = false;
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
        renderLanPage();
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
        renderLanPage();
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

  window.loadLanState = loadLanState;
  window.renderRunningPorts = renderRunningPorts;
  var renderRunningPorts = window.renderRunningPorts;

window.renderLanClients = renderLanClients;
var renderLanClients = window.renderLanClients;
window.copyToClipboard = copyToClipboard;
var copyToClipboard = window.copyToClipboard;
window.renderLanPage = renderLanPage;
var renderLanPage = window.renderLanPage;
window.requestFirewallRule = requestFirewallRule;
var requestFirewallRule = window.requestFirewallRule;
window.removeFirewallRule = removeFirewallRule;
var removeFirewallRule = window.removeFirewallRule;
