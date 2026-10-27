// Inbound C# Bridge Events & Dispatcher
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
          appendLog(`Bridge action '${msg.action || 'request'}' failed: ${errText}`, 'System', null, 'error', msg.data);
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
        try {
          window.dispatchEvent(new CustomEvent(msg.event, { detail: msg.payload }));
        } catch (e) {
          console.warn('Event dispatch failed:', e);
        }
      }
    });
  }

  function handleBackendEvent(eventType, payload) {
    // Suppress high-frequency stream events from log spam
    const suppressedEvents = [
      'hardware_metrics', 
      'terminal_data', 
      'live_log_entry', 
      'opencode_status_changed', 
      'opencode_sessions_updated',
      'opencode_interactive_prompt',
      'update_download_progress',
      'repo_scan_progress',
      'discord_rpc_status_changed'
    ];
    if (!suppressedEvents.includes(eventType)) {
      let friendlySummary = `Event: ${eventType}`;
      let inferredLevel = 'info';
      let inferredTag = 'System';

      if (eventType === 'ai_config_changed') {
        const presetsCount = payload?.promptPresets?.length || payload?.builtInPromptPresets?.length || 0;
        const provsCount = payload?.providers?.length || 0;
        friendlySummary = `AI Configuration updated (${presetsCount} prompt presets, ${provsCount} providers configured)`;
        inferredTag = 'Config';
      } else if (eventType === 'cli_status_changed') {
        friendlySummary = `CLI ${payload?.cli || 'workspace'} status changed to ${payload?.state || 'unknown'} (PID: ${payload?.pid || 'N/A'}, Port: ${payload?.port || 'N/A'})`;
        inferredTag = 'CLI';
        if ((payload?.state || '').toLowerCase() === 'error') inferredLevel = 'error';
      } else if (eventType === 'lan_proxy_state_changed') {
        friendlySummary = `LAN Proxy ${payload?.lanProxyEnabled ? 'enabled' : 'disabled'} on port ${payload?.lanProxyPort || state.lanProxyPort}`;
        inferredTag = 'Network';
      } else if (eventType === 'lan_clients_changed') {
        const cnt = Array.isArray(payload?.clients) ? payload.clients.length : 0;
        friendlySummary = `LAN active clients updated (${cnt} device(s) connected)`;
        inferredTag = 'Network';
      } else if (eventType === 'lan_state_changed') {
        if (payload?.bindError) {
          friendlySummary = `LAN bind warning: ${payload.bindError}`;
          inferredLevel = 'warn';
        } else {
          friendlySummary = `LAN network state updated (running: ${!!payload?.running})`;
        }
        inferredTag = 'Network';
      } else if (eventType === 'running_ports_updated') {
        let ports = 'none';
        if (Array.isArray(payload?.runningPorts)) {
          ports = payload.runningPorts.length > 0
            ? payload.runningPorts.map(p => {
                if (typeof p === 'object' && p) {
                  const name = p.name || p.id || 'CLI';
                  const port = p.port || p.lanPort;
                  return port ? `${name}: ${port}` : name;
                }
                return String(p);
              }).join(', ')
            : 'none';
        } else if (payload?.runningPorts && typeof payload.runningPorts === 'object') {
          ports = Object.entries(payload.runningPorts).map(([k, v]) => {
            if (typeof v === 'object' && v) {
              const name = v.name || v.id || k;
              const port = v.port || v.lanPort;
              return port ? `${name}: ${port}` : name;
            }
            return `${k}:${v}`;
          }).join(', ');
        }
        friendlySummary = `Running ports updated: ${ports || 'none'}`;
        inferredTag = 'System';
      } else if (eventType === 'profile_changed' || eventType === 'profile_saved') {
        friendlySummary = `Antigravity profile switched: ${payload?.activeProfile || 'default'}`;
        inferredTag = 'CLI';
      } else if (eventType === 'antigravity_instance_status_changed') {
        friendlySummary = `Antigravity profile '${payload?.profile}' ${payload?.isRunning ? 'started on port ' + payload?.port : 'stopped'}`;
        inferredTag = 'CLI';
      } else if (eventType === 'dependency_install_all_complete') {
        const failedCount = (payload?.failed || []).length;
        if (failedCount > 0) {
          friendlySummary = `Batch dependency install finished with failures: ${payload.failed.join(', ')}`;
          inferredLevel = 'error';
        } else {
          friendlySummary = 'Batch dependency install finished successfully';
          inferredLevel = 'success';
        }
        inferredTag = 'System';
      } else if (typeof payload === 'string') {
        friendlySummary = `[${eventType}] ${payload}`;
      }

      appendLog(friendlySummary, inferredTag, null, inferredLevel, payload);
    }

    switch (eventType) {
      case 'update_available':
      case 'update_download_progress':
      case 'update_download_failed':
        if (typeof window.handleExternalUpdateEvent === 'function') {
          window.handleExternalUpdateEvent(eventType, payload);
        }
        break;

      case 'workspace_file_changed':
        if (typeof window.handleWorkspaceFileEvent === 'function') {
          window.handleWorkspaceFileEvent(payload);
        }
        break;

      case 'custom_clis_changed':
        if (typeof window.handleCustomClisChanged === 'function') {
          window.handleCustomClisChanged(payload);
        }
        break;

      case 'workspaces_updated':
        if (payload?.workspaces) {
          state.workspaces = payload.workspaces;
          if (payload.activeWorkspaceId) state.activeWorkspaceId = payload.activeWorkspaceId;
          renderWorkspacesDropdown(state.workspaces, state.activeWorkspaceId);
        }
        break;

      case 'workspace_activated':
        if (payload?.activeWorkspaceId) {
          state.activeWorkspaceId = payload.activeWorkspaceId;
          renderWorkspacesDropdown(state.workspaces, state.activeWorkspaceId);
          showToast(`Switched to workspace: ${payload.activeWorkspace?.name || payload.activeWorkspaceId}`, 'success');
          window.refreshAgentRules?.();
        }
        break;

      case 'token_telemetry_updated':
        if (payload) {
          state.tokenTelemetry = payload;
          renderTokenTelemetry(payload);
        }
        break;

      case 'agent_task_completed':
        if (payload) {
          const now = Date.now();
          const dedupKey = `${payload.sessionId || ''}_${payload.brainId || ''}_${payload.isError}_${payload.isApproval}`;
          if (window.__lastAgentCompletionKey === dedupKey && (now - (window.__lastAgentCompletionTime || 0)) < 10000) {
            break;
          }
          window.__lastAgentCompletionKey = dedupKey;
          window.__lastAgentCompletionTime = now;

          const statusText = payload.isError ? 'Error' : (payload.isApproval ? 'Approval Required' : 'Completed');
          const toastType = payload.isError ? 'error' : (payload.isApproval ? 'warning' : 'success');
          const shortBrain = payload.brainId ? ` [${payload.brainId.substring(0, 8)}...]` : '';
          showToast(`Agent ${payload.displayName || payload.sessionId}${shortBrain}: ${statusText}`, toastType, 4500);
          if (typeof window.loadAgentSessions === 'function') {
            window.loadAgentSessions();
          }
        }
        break;

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
          appendLog(payload.text || payload.message, payload.tag, payload.timestamp, payload.level, payload.data || payload.details);
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

          // A start or stop changes whether the open tab can attach, so it re-reads its session.
          if (state.activeView === 'terminals' && normalizeCliKey(payload.cli) === normalizeCliKey(state.activeTerminalCli)) {
            activateTerminalTab(state.activeTerminalCli, false);
          }
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
          // Output is routed by session, not by the visible tab, so a background CLI keeps its
          // scrollback instead of losing everything printed while another tab was open.
          const key = normalizeCliKey(payload.cli);
          if (key !== 'hermes') {
            const session = getTerminalSession(key);
            if (session) session.term.write(payload.data);
          }
        }
        break;

      case 'terminal_tab_switched':
        if (payload && payload.cli) {
          // Pushed by the standalone workspace; echoing it back would loop.
          activateTerminalTab(payload.cli, false);
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
          updateAntigravityAppCardUi();
          updateProfileEmailUi();
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

      case 'lan_firewall_updated':
        if (payload) {
          lanSnapshot = Object.assign({}, lanSnapshot || {}, { firewall: payload });
          if (typeof window.renderLanPage === 'function') window.renderLanPage();
        }
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

      case 'repo_scan_progress':
        if (typeof window.handleRepoScanProgress === 'function') {
          window.handleRepoScanProgress(payload);
        }
        break;

      case 'repos_updated':
        if (typeof window.handleReposUpdated === 'function') {
          window.handleReposUpdated(payload);
        }
        break;
    }
  }

  window.handleBackendEvent = handleBackendEvent;
  var handleBackendEvent = window.handleBackendEvent;

