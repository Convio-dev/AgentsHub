// AgentsHub v2.0 AI Agent Chat IDE Subsystem
(function initAgentChatIdeSubsystem() {
  'use strict';

  let isHudOpen = false;
  let activeExecutionCards = new Map(); // profileName -> DOM element
  let selectedProfileTargets = new Set();
  let currentActiveSessionId = localStorage.getItem('agents_hub_active_session_id') || 'session_default';

  function escapeHtml(str) {
    return String(str || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  // Terminal ANSI escape sequence stripper for clean card display
  function stripAnsi(str) {
    if (!str) return '';
    return String(str)
      .replace(/[\u001b\u009b][[()#;?]*(?:[0-9]{1,4}(?:;[0-9]{0,4})*)?[0-9A-ORZcf-nqry=><]/g, '')
      .replace(/[\x00-\x08\x0B-\x1F\x7F]/g, '')
      .trim();
  }

  // --- 1. Quake HUD Console Dropdown Handler ---
  function toggleHud(forceState) {
    const hud = document.getElementById('quake-hud');
    if (!hud) return;
    isHudOpen = typeof forceState === 'boolean' ? forceState : !isHudOpen;
    if (isHudOpen) {
      hud.classList.add('active');
      hud.classList.remove('pointer-events-none');
    } else {
      hud.classList.remove('active');
      hud.classList.add('pointer-events-none');
    }
  }
  window.toggleHud = toggleHud;

  function clearHudTerminal() {
    const container = document.getElementById('hud-terminal-content');
    if (container) {
      container.innerHTML = '<p class="text-content-dim">// Terminal buffer cleared. ConPTY process listening...</p>';
    }
  }
  window.clearHudTerminal = clearHudTerminal;

  function appendHudLog(message, color = 'slate') {
    const hudContent = document.getElementById('hud-terminal-content');
    if (!hudContent) return;
    const colorClasses = {
      slate: 'text-content-muted',
      sky: 'text-sky-500 font-medium',
      emerald: 'text-emerald-500 font-medium',
      amber: 'text-amber-500 font-medium',
      rose: 'text-rose-500 font-medium'
    };
    const cls = colorClasses[color] || 'text-content-muted';
    const p = document.createElement('p');
    p.className = cls;
    p.textContent = message;
    hudContent.appendChild(p);

    if (hudContent.children.length > 500) {
      hudContent.removeChild(hudContent.firstChild);
    }
    hudContent.scrollTop = hudContent.scrollHeight;
  }
  window.appendHudLog = appendHudLog;

  // --- 2. Real Workspaces & Active Git Branch Sync ---
  function renderWorkspacesInChat(workspaces, activeId) {
    const sel = document.getElementById('select-project-workspace');
    const wsLabel = document.getElementById('chat-active-workspace-label');
    const headerWs = document.getElementById('header-ws-name');
    const list = Array.isArray(workspaces) && workspaces.length > 0 ? workspaces : (window.state?.workspaces || []);
    const effActiveId = activeId || window.state?.activeWorkspaceId;
    const activeWs = list.find(w => w.id === effActiveId) || list[0] || null;

    if (wsLabel) {
      wsLabel.textContent = activeWs ? activeWs.name : 'Default Workspace';
    }
    if (headerWs) {
      headerWs.textContent = activeWs ? activeWs.name : 'Default Workspace';
    }

    if (sel) {
      sel.innerHTML = '';
      if (list.length === 0) {
        const opt = document.createElement('option');
        opt.value = 'default';
        opt.textContent = 'Default (Isolated VirtualHome)';
        sel.appendChild(opt);
      } else {
        list.forEach(ws => {
          const opt = document.createElement('option');
          opt.value = ws.id;
          opt.textContent = `${ws.name}${ws.id === effActiveId ? ' (active)' : ''}`;
          if (ws.id === effActiveId) opt.selected = true;
          sel.appendChild(opt);
        });
      }
    }

    // Refresh file tree whenever active workspace changes
    loadWorkspaceFiles(activeWs?.workingDirectory);
  }
  window.renderWorkspacesInChat = renderWorkspacesInChat;

  async function onWorkspaceChange(wsId) {
    if (!wsId) return;
    if (typeof window.switchWorkspace === 'function') {
      await window.switchWorkspace(wsId);
    } else if (typeof window.callBridge === 'function') {
      try {
        await window.callBridge('activate_workspace', { id: wsId });
      } catch (e) {
        console.warn('Failed to switch workspace:', e);
      }
    }
    appendHudLog(`[Workspace] Switched to workspace ID: ${wsId}`, 'sky');
    loadWorkspaceFiles();
  }
  window.onWorkspaceChange = onWorkspaceChange;

  // --- 3. Live Real Workspace Files Tree ---
  async function loadWorkspaceFiles(pathOverride) {
    const treeContainer = document.getElementById('repo-file-tree');
    const branchBadge = document.getElementById('workspace-branch-text');
    const headerBranch = document.getElementById('header-branch-text');
    if (!treeContainer) return;

    if (typeof window.callBridge !== 'function') return;

    try {
      const res = await window.callBridge('get_workspace_files', pathOverride ? { path: pathOverride } : {});
      if (!res || !Array.isArray(res.files)) return;

      if (res.branch) {
        if (branchBadge) branchBadge.textContent = res.branch;
        if (headerBranch) headerBranch.textContent = res.branch;
      }

      treeContainer.innerHTML = '';
      if (res.files.length === 0) {
        treeContainer.innerHTML = '<div class="py-2 text-[11px] text-content-dim italic">Workspace empty</div>';
        return;
      }

      res.files.forEach(f => {
        const item = document.createElement('div');
        item.className = 'py-0.5 hover:text-content-main cursor-pointer flex items-center space-x-1.5 transition text-[11px] truncate group';
        item.title = `Insert '${f.path}' into prompt`;

        let iconClass = 'fa-regular fa-file text-content-dim';
        if (f.isDirectory) {
          iconClass = 'fa-regular fa-folder text-amber-500';
        } else if (['.js', '.ts', '.html', '.css', '.cs'].includes(f.extension)) {
          iconClass = 'fa-regular fa-file-code text-sky-500';
        } else if (['.md', '.txt'].includes(f.extension)) {
          iconClass = 'fa-regular fa-file-lines text-emerald-500';
        } else if (['.json', '.yaml', '.yml', '.config'].includes(f.extension)) {
          iconClass = 'fa-solid fa-gear text-amber-500 text-[10px]';
        }

        item.innerHTML = `<i class="${iconClass} flex-shrink-0"></i><span class="truncate ${f.isDirectory ? 'font-medium text-content-main' : ''}">${escapeHtml(f.name)}</span>`;
        item.onclick = () => {
          const input = document.getElementById('prompt-input');
          if (input) {
            const separator = input.value.endsWith(' ') || input.value.length === 0 ? '' : ' ';
            input.value = `${input.value}${separator}@${f.path} `;
            input.focus();
            if (typeof window.showToast === 'function') {
              window.showToast(`Referenced ${f.name} in prompt`, 'info', 1800);
            }
          }
        };
        treeContainer.appendChild(item);
      });
    } catch (err) {
      console.warn('Failed to load workspace files:', err);
      treeContainer.innerHTML = '<div class="py-2 text-[11px] text-rose-500 italic">Error reading files</div>';
    }
  }
  window.loadWorkspaceFiles = loadWorkspaceFiles;

  // --- 4. Multi-Profile Management & Concurrent Selection in Chat ---
  function toggleChatProfilePopover(forceState) {
    const popover = document.getElementById('chat-profile-popover');
    if (!popover) return;
    const shouldShow = typeof forceState === 'boolean' ? forceState : (popover.style.display === 'none');
    popover.style.display = shouldShow ? 'block' : 'none';
    if (shouldShow) renderChatProfileManager();
  }
  window.toggleChatProfilePopover = toggleChatProfilePopover;

  // Close popover when clicking outside
  document.addEventListener('click', (e) => {
    const container = document.getElementById('chat-profile-manager-container');
    const popover = document.getElementById('chat-profile-popover');
    if (popover && popover.style.display !== 'none' && container && !container.contains(e.target)) {
      popover.style.display = 'none';
    }
  });

  function getValidProfiles() {
    const rawList = Array.isArray(window.state?.antigravity?.profiles) ? window.state.antigravity.profiles : [];
    return rawList
      .map(p => (typeof p === 'object' ? p.name : p))
      .filter(name => Boolean(name) && String(name).trim().toLowerCase() !== 'default');
  }

  async function renderChatProfileManager() {
    const listContainer = document.getElementById('chat-popover-profile-list');
    const summaryLabel = document.getElementById('chat-profile-summary-label');
    const countBadge = document.getElementById('chat-popover-count');
    const liveDot = document.getElementById('chat-profile-live-dot');
    if (!listContainer) return;

    if (!window.state?.antigravity?.profiles && typeof window.callBridge === 'function') {
      try {
        const init = await window.callBridge('get_initial_state');
        if (init && init.antigravity) {
          if (!window.state) window.state = {};
          window.state.antigravity = init.antigravity;
        }
      } catch (e) {}
    }

    const rawList = Array.isArray(window.state?.antigravity?.profiles) ? window.state.antigravity.profiles : [];
    const validProfiles = getValidProfiles();

    // Clean up selected targets: eliminate "Default" or names not in validProfiles
    for (const t of Array.from(selectedProfileTargets)) {
      if (!validProfiles.some(p => p.toLowerCase() === t.toLowerCase())) {
        selectedProfileTargets.delete(t);
      }
    }

    // Auto-select 1st available profile if none selected
    if (selectedProfileTargets.size === 0 && validProfiles.length > 0) {
      const activeCandidate = window.state?.antigravity?.activeProfile;
      const matched = validProfiles.find(p => p.toLowerCase() === (activeCandidate || '').toLowerCase());
      selectedProfileTargets.add(matched || validProfiles[0]);
    }

    listContainer.innerHTML = '';
    let runningCount = 0;

    if (validProfiles.length === 0) {
      listContainer.innerHTML = `
        <div class="py-4 text-center text-xs text-content-dim italic space-y-2">
          <p>No profiles found.</p>
          <button onclick="if (typeof switchView === 'function') switchView('profiles');" class="text-sky-500 hover:underline text-[11px] font-medium">Create profile &rarr;</button>
        </div>
      `;
    } else {
      validProfiles.forEach(pName => {
        const pObj = rawList.find(x => (typeof x === 'object' ? x.name : x).toLowerCase() === pName.toLowerCase());
        const isRun = typeof pObj === 'object' && pObj.isRunning;
        const port = typeof pObj === 'object' && pObj.port ? pObj.port : null;
        if (isRun) runningCount++;

        const isChecked = selectedProfileTargets.has(pName);

        const row = document.createElement('div');
        row.className = 'flex items-center justify-between p-2 rounded-lg bg-surface-deep border border-surface-border text-xs transition hover:border-surface-borderLight';
        row.innerHTML = `
          <label class="flex items-center space-x-2.5 cursor-pointer flex-1 min-w-0 pr-2 select-none">
            <input type="checkbox" ${isChecked ? 'checked' : ''} class="rounded border-surface-border text-sky-600 focus:ring-0 cursor-pointer" data-profile="${escapeHtml(pName)}">
            <div class="flex items-center space-x-1.5 truncate">
              <span class="w-2 h-2 rounded-full ${isRun ? 'bg-emerald-500 animate-pulse' : 'bg-content-dim'} flex-shrink-0"></span>
              <span class="font-mono font-medium ${isChecked ? 'text-content-main' : 'text-content-muted'} truncate">${escapeHtml(pName)}</span>
            </div>
          </label>
          <div class="flex items-center space-x-2 flex-shrink-0">
            <span class="text-[10px] font-mono px-1.5 py-0.5 rounded ${isRun ? 'bg-emerald-500/15 text-emerald-600 dark:text-emerald-400' : 'bg-surface-card text-content-dim'}">
              ${isRun ? (port ? `Port ${port}` : 'Online') : 'Offline'}
            </span>
            <button class="btn-profile-action text-[11px] px-2 py-0.5 rounded font-mono font-medium transition ${isRun ? 'bg-rose-500/15 text-rose-500 hover:bg-rose-500 hover:text-white' : 'bg-emerald-500/15 text-emerald-600 hover:bg-emerald-600 hover:text-white'}" data-profile="${escapeHtml(pName)}" data-action="${isRun ? 'stop' : 'start'}">
              ${isRun ? 'Stop' : 'Start'}
            </button>
          </div>
        `;

        // Checkbox listener
        const cb = row.querySelector('input[type="checkbox"]');
        cb?.addEventListener('change', (e) => {
          if (e.target.checked) {
            selectedProfileTargets.add(pName);
          } else {
            selectedProfileTargets.delete(pName);
            if (selectedProfileTargets.size === 0 && validProfiles.length > 0) {
              selectedProfileTargets.add(pName);
              e.target.checked = true;
            }
          }
          updateProfileSelectionUi();
        });

        // Start / Stop button listener
        const actionBtn = row.querySelector('.btn-profile-action');
        actionBtn?.addEventListener('click', async (e) => {
          e.stopPropagation();
          const action = actionBtn.dataset.action;
          if (action === 'start') {
            await startProfileFromChat(pName);
          } else {
            await stopProfileFromChat(pName);
          }
        });

        listContainer.appendChild(row);
      });
    }

    // Populate sidebar agent-profile-selector
    const sideSelector = document.getElementById('agent-profile-selector');
    if (sideSelector) {
      sideSelector.innerHTML = '';
      if (validProfiles.length === 0) {
        const opt = document.createElement('option');
        opt.value = '';
        opt.textContent = 'No Profile';
        sideSelector.appendChild(opt);
      } else {
        const firstTarget = Array.from(selectedProfileTargets)[0] || validProfiles[0];
        validProfiles.forEach(pName => {
          const opt = document.createElement('option');
          opt.value = pName;
          opt.textContent = `agy: ${pName}`;
          if (pName.toLowerCase() === firstTarget.toLowerCase()) opt.selected = true;
          sideSelector.appendChild(opt);
        });
      }
    }

    updateProfileSelectionUi();

    if (liveDot) {
      liveDot.className = `w-2 h-2 rounded-full ${runningCount > 0 ? 'bg-emerald-500 animate-pulse' : 'bg-content-dim'}`;
    }
  }
  window.renderChatProfileManager = renderChatProfileManager;

  window.switchAgentProfile = async function(pName) {
    if (!pName || typeof window.callBridge !== 'function') return;
    try {
      await window.callBridge('switch_antigravity_profile', { profile: pName });
      await window.callBridge('switch_account', { profile: pName });
      if (window.state?.antigravity) window.state.antigravity.activeProfile = pName;
      selectedProfileTargets.clear();
      selectedProfileTargets.add(pName);
      renderChatProfileManager();
      if (typeof window.showToast === 'function') {
        window.showToast(`Switched active profile to ${pName}`, 'info', 2000);
      }
    } catch (err) {
      console.warn('Failed to switch profile:', err);
    }
  };

  function updateProfileSelectionUi() {
    const summaryLabel = document.getElementById('chat-profile-summary-label');
    const countBadge = document.getElementById('chat-popover-count');
    const liveDot = document.getElementById('chat-profile-live-dot');
    const validProfiles = getValidProfiles();
    const count = selectedProfileTargets.size;

    if (validProfiles.length === 0 || count === 0) {
      if (summaryLabel) summaryLabel.textContent = 'Runner: No Profile';
      if (countBadge) countBadge.textContent = 'No Profile';
      if (liveDot) liveDot.className = 'w-2 h-2 rounded-full bg-content-dim';
      return;
    }

    if (countBadge) {
      countBadge.textContent = `${count} Selected`;
    }

    if (summaryLabel) {
      if (count === 1) {
        const singleName = Array.from(selectedProfileTargets)[0];
        summaryLabel.textContent = `Runner: ${singleName}`;
      } else {
        summaryLabel.textContent = `Runners: ${count} Active Parallel`;
      }
    }
  }

  async function startProfileFromChat(pName) {
    if (!pName) return;
    try {
      appendHudLog(`[Sidecar] Registering and starting runner for profile: ${pName}...`, 'sky');
      if (typeof window.callBridge === 'function') {
        const res = await window.callBridge('start_antigravity_instance', { profile: pName });
        if (res && res.model) {
          updateChatModelDisplay(res.model);
        }
      }
      if (typeof window.showToast === 'function') {
        window.showToast(`Started profile: ${pName}`, 'success', 2500);
      }
      setTimeout(renderChatProfileManager, 300);
      if (window.sidecarStatus && typeof window.sidecarStatus.refresh === 'function') {
        window.sidecarStatus.refresh();
      }
    } catch (err) {
      appendHudLog(`[Profile Start Error] ${err?.message || err}`, 'rose');
    }
  }
  window.startProfileFromChat = startProfileFromChat;

  async function stopProfileFromChat(pName) {
    if (!pName) return;
    try {
      appendHudLog(`[Sidecar] Stopping runner for profile: ${pName}...`, 'amber');
      if (typeof window.callBridge === 'function') {
        await window.callBridge('stop_antigravity_instance', { profile: pName });
      }
      if (typeof window.showToast === 'function') {
        window.showToast(`Stopped profile: ${pName}`, 'info', 2500);
      }
      setTimeout(renderChatProfileManager, 300);
      if (window.sidecarStatus && typeof window.sidecarStatus.refresh === 'function') {
        window.sidecarStatus.refresh();
      }
    } catch (err) {
      appendHudLog(`[Profile Stop Error] ${err?.message || err}`, 'rose');
    }
  }
  window.stopProfileFromChat = stopProfileFromChat;

  // --- 5. Real Session History (Today / Yesterday / Older) with Accordion & Deletion ---
  const collapsedDayGroups = new Set(JSON.parse(localStorage.getItem('agents_hub_collapsed_days') || '[]'));

  function toggleDayGroup(dayKey) {
    const k = dayKey.toLowerCase();
    if (collapsedDayGroups.has(k)) {
      collapsedDayGroups.delete(k);
    } else {
      collapsedDayGroups.add(k);
    }
    try {
      localStorage.setItem('agents_hub_collapsed_days', JSON.stringify(Array.from(collapsedDayGroups)));
    } catch (e) {}
    loadSessionHistory();
  }
  window.toggleDayGroup = toggleDayGroup;

  function cleanSessionTitle(raw) {
    if (!raw) return 'Agent Session';
    let clean = String(raw)
      .replace(/<\/?USER_REQUEST>/gi, '')
      .replace(/```[\s\S]*?```/g, '')
      .replace(/\r?\n/g, ' ')
      .trim();
    return clean || 'Agent Session';
  }

  async function deleteSessionItem(session) {
    if (!session) return;

    if (session.transcriptPath && typeof window.callBridge === 'function') {
      try {
        await window.callBridge('delete_agent_session', { transcriptPath: session.transcriptPath });
      } catch (err) {
        console.warn('Failed to delete session on bridge:', err);
      }
    }

    try {
      let local = JSON.parse(localStorage.getItem('agents_hub_local_sessions') || '[]');
      local = local.filter(x => x.sessionId !== session.sessionId && (!session.transcriptPath || x.transcriptPath !== session.transcriptPath));
      localStorage.setItem('agents_hub_local_sessions', JSON.stringify(local));
    } catch (e) {}

    if (currentActiveSessionId === session.sessionId) {
      currentActiveSessionId = null;
      localStorage.removeItem('agents_hub_active_session_id');
      const msgContainer = document.getElementById('chat-messages-container');
      if (msgContainer) msgContainer.innerHTML = '';
      const titleEl = document.getElementById('chat-title-text');
      if (titleEl) titleEl.textContent = 'Active Agent Session';
    }

    if (typeof window.showToast === 'function') {
      window.showToast('Session deleted', 'info', 2000);
    }

    loadSessionHistory();
  }
  window.deleteSessionItem = deleteSessionItem;

  async function loadSessionHistory() {
    const listContainer = document.getElementById('session-history-list');
    if (!listContainer) return;

    let sessions = [];
    if (typeof window.callBridge === 'function') {
      try {
        const res = await window.callBridge('get_agent_sessions');
        if (res && Array.isArray(res.sessions)) {
          sessions = res.sessions;
        }
      } catch (e) {
        console.warn('Failed to load sessions from bridge:', e);
      }
    }

    // Merge with any local sessions
    try {
      const local = JSON.parse(localStorage.getItem('agents_hub_local_sessions') || '[]');
      if (Array.isArray(local)) {
        local.forEach(l => {
          if (!sessions.some(s => s.sessionId === l.sessionId)) {
            sessions.push(l);
          }
        });
      }
    } catch (e) {}

    listContainer.innerHTML = '';

    if (sessions.length === 0) {
      listContainer.innerHTML = `
        <div class="p-3 rounded-lg bg-surface-deep border border-surface-border text-center text-xs text-content-dim space-y-2">
          <p>No past sessions recorded.</p>
          <button onclick="createNewSession()" class="text-sky-500 hover:underline text-xs font-medium">Create first session &rarr;</button>
        </div>
      `;
      return;
    }

    const now = Date.now();
    const oneDay = 24 * 60 * 60 * 1000;
    const groups = { today: [], yesterday: [], earlier: [] };

    sessions.forEach(s => {
      const time = s.lastActiveUtc ? new Date(s.lastActiveUtc).getTime() : now;
      const diff = now - time;
      if (diff < oneDay) {
        groups.today.push(s);
      } else if (diff < 2 * oneDay) {
        groups.yesterday.push(s);
      } else {
        groups.earlier.push(s);
      }
    });

    const renderGroup = (title, items) => {
      if (items.length === 0) return;
      const dayKey = title.toLowerCase();
      const isCollapsed = collapsedDayGroups.has(dayKey);

      const groupEl = document.createElement('div');
      groupEl.className = 'space-y-1';

      // Collapsible Header Button with Chevron & Item Count
      const headerBtn = document.createElement('button');
      headerBtn.type = 'button';
      headerBtn.className = 'w-full flex items-center justify-between px-2 py-1.5 text-[10px] uppercase tracking-wider text-content-dim font-semibold hover:text-content-main rounded hover:bg-surface-card transition cursor-pointer select-none';
      headerBtn.innerHTML = `
        <span class="flex items-center space-x-1.5">
          <i class="fa-solid ${isCollapsed ? 'fa-chevron-right' : 'fa-chevron-down'} text-[9px] text-content-dim transition-transform"></i>
          <span>${escapeHtml(title)}</span>
        </span>
        <span class="font-mono text-[9px] px-1.5 py-0.2 rounded bg-surface-card border border-surface-border text-content-dim">${items.length}</span>
      `;
      headerBtn.onclick = () => toggleDayGroup(dayKey);
      groupEl.appendChild(headerBtn);

      const subList = document.createElement('div');
      subList.className = `space-y-1 ${isCollapsed ? 'hidden' : ''}`;

      items.forEach(s => {
        const isActive = s.sessionId === currentActiveSessionId;
        const row = document.createElement('div');
        row.className = `group w-full px-2.5 py-2 rounded-lg text-xs flex items-center space-x-2 transition cursor-pointer ${isActive ? 'bg-surface-card border border-surface-border text-content-main shadow-sm' : 'hover:bg-surface-card text-content-muted hover:text-content-main'}`;
        
        const displayTitle = cleanSessionTitle(s.goal || s.sessionId);

        row.innerHTML = `
          <i class="fa-solid fa-bolt text-sky-500 text-xs flex-shrink-0"></i>
          <span class="truncate flex-1 font-medium select-none" title="${escapeHtml(displayTitle)}">${escapeHtml(displayTitle)}</span>
          <span class="text-[10px] font-mono text-content-dim flex-shrink-0 mr-1">${isActive ? 'active' : (s.stepCount ? `${s.stepCount} steps` : '')}</span>
          <button type="button" class="btn-del-session flex items-center justify-center w-5 h-5 rounded text-content-dim hover:text-rose-500 hover:bg-rose-500/15 transition flex-shrink-0 cursor-pointer" title="Delete Session" style="opacity: 0.7;">
            <i class="fa-regular fa-trash-can text-[11px] pointer-events-none"></i>
          </button>
        `;

        row.onclick = () => loadSession(s);

        const delBtn = row.querySelector('.btn-del-session');
        delBtn?.addEventListener('click', async (e) => {
          e.stopPropagation();
          e.preventDefault();
          await deleteSessionItem(s);
        });

        subList.appendChild(row);
      });

      groupEl.appendChild(subList);
      listContainer.appendChild(groupEl);
    };

    renderGroup('Today', groups.today);
    renderGroup('Yesterday', groups.yesterday);
    renderGroup('Earlier', groups.earlier);
  }
  window.loadSessionHistory = loadSessionHistory;

  async function loadSession(session) {
    if (!session) return;
    currentActiveSessionId = session.sessionId;
    localStorage.setItem('agents_hub_active_session_id', session.sessionId);

    const titleEl = document.getElementById('chat-title-text');
    if (titleEl) {
      titleEl.textContent = session.goal ? `Session: ${session.goal}` : `Session: ${session.sessionId}`;
    }

    const msgContainer = document.getElementById('chat-messages-container');
    if (msgContainer) msgContainer.innerHTML = '';

    // Switch view to chat if called from elsewhere
    if (typeof window.switchView === 'function') {
      window.switchView('chat');
    }

    // Refresh history highlight
    loadSessionHistory();

    if (session.transcriptPath && typeof window.callBridge === 'function') {
      try {
        appendHudLog(`[Session] Loading transcript from ${session.transcriptPath}...`, 'sky');
        const res = await window.callBridge('get_session_steps', { transcriptPath: session.transcriptPath });
        if (res && Array.isArray(res.steps) && msgContainer) {
          res.steps.forEach(st => {
            if (st.type === 'USER_INPUT') {
              const uCard = document.createElement('div');
              uCard.className = 'flex items-start space-x-3 max-w-3xl';
              uCard.innerHTML = `
                <div class="w-8 h-8 rounded-lg bg-surface-card border border-surface-border flex items-center justify-center font-bold text-xs text-content-main flex-shrink-0 shadow-sm">
                  YOU
                </div>
                <div class="flex-1 bg-surface-card border border-surface-border rounded-xl p-4 text-sm text-content-main shadow-sm">
                  <p class="whitespace-pre-wrap">${escapeHtml(st.content)}</p>
                </div>
              `;
              msgContainer.appendChild(uCard);
            } else if (st.type === 'PLANNER_RESPONSE' || st.toolCalls?.length) {
              const aCard = document.createElement('div');
              aCard.className = 'flex items-start space-x-3 max-w-3xl';
              aCard.innerHTML = `
                <div class="w-8 h-8 rounded-lg bg-sky-600 flex items-center justify-center text-white text-xs flex-shrink-0 shadow-md shadow-sky-600/30">
                  <i class="fa-solid fa-microchip"></i>
                </div>
                <div class="flex-1 bg-surface-card border border-surface-border rounded-xl p-4 text-xs space-y-2 text-content-main shadow-sm">
                  <p class="whitespace-pre-wrap leading-relaxed">${escapeHtml(st.content || 'Action executed')}</p>
                </div>
              `;
              msgContainer.appendChild(aCard);
            }
          });
          const stream = document.getElementById('chat-stream');
          if (stream) stream.scrollTop = stream.scrollHeight;
        }
      } catch (err) {
        console.warn('Failed to load session steps:', err);
      }
    }
  }
  window.loadSession = loadSession;

  // --- 6. '+ New Agent Session' Action ---
  function createNewSession() {
    const newId = 'session_' + Date.now();
    currentActiveSessionId = newId;
    localStorage.setItem('agents_hub_active_session_id', newId);

    const titleEl = document.getElementById('chat-title-text');
    if (titleEl) titleEl.textContent = 'Active Agent Session';

    const msgContainer = document.getElementById('chat-messages-container');
    if (msgContainer) msgContainer.innerHTML = '';

    activeExecutionCards.clear();

    // Save initial session stub
    try {
      const local = JSON.parse(localStorage.getItem('agents_hub_local_sessions') || '[]');
      local.unshift({
        sessionId: newId,
        goal: 'New Agent Session',
        lastActiveUtc: new Date().toISOString(),
        stepCount: 0
      });
      localStorage.setItem('agents_hub_local_sessions', JSON.stringify(local.slice(0, 30)));
    } catch (e) {}

    loadSessionHistory();

    const input = document.getElementById('prompt-input');
    if (input) {
      input.value = '';
      input.focus();
    }

    if (typeof window.switchView === 'function') {
      window.switchView('chat');
    }

    if (typeof window.showToast === 'function') {
      window.showToast('Started new agent session', 'success', 2000);
    }
    appendHudLog('[Session] Started new agent session', 'emerald');
  }
  window.createNewSession = createNewSession;

  // --- 7. User Prompt Submission & ConPTY Dispatch (With \r\n Enter Fix & Multi-Profile) ---
  async function submitUserPrompt() {
    const input = document.getElementById('prompt-input');
    if (!input) return;
    const text = input.value.trim();
    if (!text) return;

    input.value = '';

    const stream = document.getElementById('chat-stream');
    const msgContainer = document.getElementById('chat-messages-container') || stream;
    if (!msgContainer) return;

    // 1. Render User Message Card
    const userCard = document.createElement('div');
    userCard.className = 'flex items-start space-x-3 max-w-3xl';
    userCard.innerHTML = `
      <div class="w-8 h-8 rounded-lg bg-surface-card border border-surface-border flex items-center justify-center font-bold text-xs text-content-main flex-shrink-0 shadow-sm">
        YOU
      </div>
      <div class="flex-1 bg-surface-card border border-surface-border rounded-xl p-4 text-sm text-content-main shadow-sm">
        <p class="whitespace-pre-wrap">${escapeHtml(text)}</p>
      </div>
    `;
    msgContainer.appendChild(userCard);
    stream.scrollTop = stream.scrollHeight;

    appendHudLog(`[User Prompt] ${text.slice(0, 100)}`, 'sky');

    // 2. Identify Target Profiles (Multi-Working Support)
    const targets = selectedProfileTargets.size > 0
      ? Array.from(selectedProfileTargets)
      : [window.state?.antigravity?.activeProfile || 'Default'];

    const activeWs = window.state?.workspaces?.find(w => w.id === window.state?.activeWorkspaceId);
    const cwd = activeWs?.workingDirectory || '';

    // 3. For each profile, spawn execution card and dispatch via Sidecar Engine
    for (const profileName of targets) {
      const sessionId = 'session_' + Date.now() + '_' + Math.random().toString(36).substring(2, 6);

      // Complete any previous active card for this profile
      const prevCard = activeExecutionCards.get(profileName.toLowerCase());
      if (prevCard) {
        const prevBadge = prevCard.querySelector('.card-status-badge');
        if (prevBadge) {
          prevBadge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 border border-emerald-500/30';
          prevBadge.textContent = 'Completed';
        }
        const ping = prevCard.querySelector('.animate-ping');
        if (ping) ping.classList.remove('animate-ping');
      }

      const executionCard = document.createElement('div');
      executionCard.className = 'flex items-start space-x-3 max-w-3xl';
      executionCard.setAttribute('data-session-id', sessionId);
      executionCard.setAttribute('data-profile', profileName);
      executionCard._rawMarkdown = '';
      executionCard._thinkingStartTime = Date.now();
      executionCard.innerHTML = `
        <div class="w-8 h-8 rounded-lg bg-sky-600 flex items-center justify-center text-white text-xs flex-shrink-0 shadow-md shadow-sky-600/30">
          <i class="fa-solid fa-microchip"></i>
        </div>
        <div class="flex-1 space-y-3 min-w-0">
          <div class="bg-surface-card border border-surface-border rounded-xl p-4 text-xs space-y-3 shadow-sm">
            <div class="flex items-center justify-between font-mono pb-2 border-b border-surface-border">
              <span class="text-content-main font-semibold flex items-center space-x-2">
                <span class="w-2 h-2 rounded-full bg-sky-500 animate-ping"></span>
                <span>Sidecar Engine: ${escapeHtml(profileName)}</span>
              </span>
              <span class="card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-sky-500/15 text-sky-500 border border-sky-500/30">Thinking...</span>
            </div>

            <!-- Sleek Thinking Component Container -->
            <div class="chat-thinking-container"></div>

            <!-- Interactive Tool Calls Cards Container -->
            <div class="chat-tools-container space-y-2"></div>

            <!-- Structured Chat Markdown Stream Content -->
            <div class="chat-assistant-markdown font-sans text-sm text-content-main leading-relaxed space-y-1"></div>

            <!-- Clean Error Alert Container -->
            <div class="chat-error-container hidden"></div>

            <div class="flex items-center justify-between pt-2 border-t border-surface-border text-[11px]">
              <button onclick="if (typeof toggleHud === 'function') toggleHud(true);" class="text-sky-500 hover:text-sky-400 font-mono flex items-center space-x-1.5 transition">
                <i class="fa-solid fa-terminal text-[10px]"></i>
                <span>Raw CLI HUD (F12) &rarr;</span>
              </button>
              <button onclick="if (typeof switchView === 'function') switchView('repositories');" class="text-content-muted hover:text-content-main font-mono transition">
                Inspect Workspace Diff &rarr;
              </button>
            </div>
          </div>
        </div>
      `;
      msgContainer.appendChild(executionCard);
      stream.scrollTop = stream.scrollHeight;
      activeExecutionCards.set(profileName.toLowerCase(), executionCard);

      // Dispatch to native Bun Sidecar via Bridge
      if (typeof window.callBridge === 'function') {
        try {
          await window.callBridge('send_chat', {
            sessionId: sessionId,
            prompt: text,
            profile: profileName,
            workingDirectory: cwd
          });
        } catch (err) {
          appendHudLog(`[Sidecar Send Error (${profileName})] ${err?.message || err}`, 'rose');
          const badge = executionCard.querySelector('.card-status-badge');
          if (badge) {
            badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-rose-500/15 text-rose-500 border border-rose-500/30';
            badge.textContent = 'Failed';
          }
        }
      }
    }
  }
  window.submitUserPrompt = submitUserPrompt;

  // --- Dynamic Model Display Helper ---
  function updateChatModelDisplay(modelName) {
    const el = document.getElementById('chat-model-text');
    if (el && modelName) {
      el.textContent = modelName;
    }
  }
  window.updateChatModelDisplay = updateChatModelDisplay;

  // --- Helper: Select tool icon based on tool name ---
  function getToolIcon(toolName) {
    const t = (toolName || '').toLowerCase();
    if (t.includes('bash') || t.includes('exec') || t.includes('shell') || t.includes('command')) return 'fa-terminal';
    if (t.includes('edit') || t.includes('write') || t.includes('replace') || t.includes('patch')) return 'fa-pen-to-square';
    if (t.includes('read') || t.includes('view') || t.includes('cat')) return 'fa-book-open';
    if (t.includes('grep') || t.includes('search')) return 'fa-magnifying-glass';
    if (t.includes('dir') || t.includes('list') || t.includes('ls')) return 'fa-folder-tree';
    if (t.includes('git')) return 'fa-code-branch';
    return 'fa-wrench';
  }

  // --- Helper: Format readable parameter summary for tool header ---
  function formatToolPreview(toolName, params) {
    if (!params || typeof params !== 'object') return '';
    if (params.command) return params.command;
    if (params.CommandLine) return params.CommandLine;
    if (params.TargetFile) return params.TargetFile;
    if (params.AbsolutePath) return params.AbsolutePath;
    if (params.SearchPath) return `${params.Query || ''} in ${params.SearchPath}`;
    if (params.DirectoryPath) return params.DirectoryPath;
    if (params.path) return params.path;
    return Object.keys(params).slice(0, 3).map(k => `${k}: ${params[k]}`).join(', ');
  }

  // --- Modern Claude / Cursor Markdown Renderer with Fenced Code Blocks & Copy Buttons ---
  function renderMarkdown(rawText) {
    if (!rawText) return '';

    // 1. Extract and preserve code blocks
    const codeBlocks = [];
    let text = rawText.replace(/```([a-zA-Z0-9_\-\+]*)\n([\s\S]*?)```/g, (match, lang, code) => {
      const placeholder = `___AGENTS_CODEBLOCK_${codeBlocks.length}___`;
      codeBlocks.push({ lang: (lang || 'code').trim(), code });
      return placeholder;
    });

    // 2. Extract and preserve inline code
    const inlineCodes = [];
    text = text.replace(/`([^`\n]+)`/g, (match, code) => {
      const placeholder = `___AGENTS_INLINE_${inlineCodes.length}___`;
      inlineCodes.push(code);
      return placeholder;
    });

    // 3. Escape HTML
    text = escapeHtml(text);

    // 4. Headers
    text = text.replace(/^### (.*$)/gim, '<h3 class="text-xs font-semibold text-sky-400 mt-3 mb-1 font-mono uppercase tracking-wider">$1</h3>');
    text = text.replace(/^## (.*$)/gim, '<h2 class="text-sm font-semibold text-content-main mt-3 mb-1 pb-1 border-b border-surface-border">$1</h2>');
    text = text.replace(/^# (.*$)/gim, '<h1 class="text-base font-bold text-content-main mt-4 mb-2 pb-1.5 border-b border-surface-border">$1</h1>');

    // 5. Blockquotes
    text = text.replace(/^\> (.*$)/gim, '<blockquote class="border-l-2 border-sky-500/60 pl-3 py-1 my-2 text-content-muted italic bg-surface-deep/40 rounded-r">$1</blockquote>');

    // 6. Horizontal Rules
    text = text.replace(/^(\-\-\-|\*\*\*|___)$/gim, '<hr class="my-3 border-surface-border" />');

    // 7. Bold, Italic, Strikethrough
    text = text.replace(/\*\*(.*?)\*\*/g, '<strong class="font-semibold text-content-main">$1</strong>');
    text = text.replace(/__(.*?)__/g, '<strong class="font-semibold text-content-main">$1</strong>');
    text = text.replace(/\*(.*?)\*/g, '<em class="italic text-content-main/90">$1</em>');
    text = text.replace(/_(.*?)_/g, '<em class="italic text-content-main/90">$1</em>');
    text = text.replace(/~~(.*?)~~/g, '<del class="line-through text-content-muted">$1</del>');

    // 8. Links [title](url)
    text = text.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" target="_blank" rel="noopener noreferrer" class="text-sky-400 hover:text-sky-300 underline font-medium">$1</a>');

    // 9. Lists
    text = text.replace(/^\s*[\-\*]\s+(.*$)/gim, '<li class="flex items-start space-x-2 my-0.5"><span class="text-sky-400 font-bold select-none text-[11px] mt-0.5">•</span><span class="flex-1">$1</span></li>');
    text = text.replace(/^\s*(\d+)\.\s+(.*$)/gim, '<li class="flex items-start space-x-2 my-0.5"><span class="font-mono text-sky-400 select-none text-[11px] mt-0.5">$1.</span><span class="flex-1">$2</span></li>');
    text = text.replace(/(<li class="flex items-start[\s\S]*?<\/li>)+/g, '<ul class="space-y-1 my-2 pl-1">$0</ul>');

    // 10. Restore inline code
    inlineCodes.forEach((code, i) => {
      text = text.replace(`___AGENTS_INLINE_${i}___`, `<code class="px-1.5 py-0.5 rounded bg-surface-deep border border-surface-border text-sky-400 font-mono text-[11.5px]">${escapeHtml(code)}</code>`);
    });

    // 11. Restore code blocks with interactive copy button
    codeBlocks.forEach((block, i) => {
      const langBadge = escapeHtml(block.lang || 'code');
      const escapedCode = escapeHtml(block.code.replace(/^\n+|\n+$/g, ''));
      const htmlBlock = `
        <div class="codeblock-container my-3 rounded-lg overflow-hidden border border-surface-border bg-black/40 shadow-sm">
          <div class="codeblock-header flex items-center justify-between px-3 py-1.5 bg-surface-deep text-[11px] font-mono text-content-muted border-b border-surface-border select-none">
            <span class="font-semibold text-sky-400 lowercase flex items-center gap-1.5">
              <i class="fa-solid fa-code text-[10px]"></i>
              <span>${langBadge}</span>
            </span>
            <button type="button" class="copy-code-btn hover:text-content-main flex items-center gap-1 transition px-1.5 py-0.5 rounded hover:bg-surface-card" onclick="copyCodeBlock(this)">
              <i class="fa-regular fa-copy text-[10px]"></i>
              <span>Copy</span>
            </button>
          </div>
          <pre class="p-3 text-xs font-mono text-sky-200 overflow-x-auto leading-relaxed whitespace-pre scrollbar-thin"><code>${escapedCode}</code></pre>
        </div>
      `;
      text = text.replace(`___AGENTS_CODEBLOCK_${i}___`, htmlBlock);
    });

    // 12. Paragraph split
    const paragraphs = text.split(/\n{2,}/);
    text = paragraphs.map(p => {
      const trimmed = p.trim();
      if (!trimmed) return '';
      if (trimmed.startsWith('<h') || trimmed.startsWith('<ul') || trimmed.startsWith('<div') || trimmed.startsWith('<blockquote') || trimmed.startsWith('<hr')) {
        return trimmed;
      }
      return `<p class="leading-relaxed my-1.5">${trimmed.replace(/\n/g, '<br />')}</p>`;
    }).join('\n');

    return text;
  }

  // Global copy code block button handler
  window.copyCodeBlock = function (btn) {
    const container = btn.closest('.codeblock-container');
    if (!container) return;
    const codeEl = container.querySelector('pre code');
    if (!codeEl) return;
    const codeText = codeEl.innerText || codeEl.textContent;

    navigator.clipboard.writeText(codeText).then(() => {
      const originalHtml = btn.innerHTML;
      btn.innerHTML = `<i class="fa-solid fa-check text-emerald-400 text-[10px]"></i><span class="text-emerald-400 font-semibold">Copied!</span>`;
      setTimeout(() => {
        btn.innerHTML = originalHtml;
      }, 2000);
    }).catch(err => {
      console.warn('Copy failed:', err);
    });
  };

  // Helper to finalize thinking block once streaming or tools begin
  function finalizeThinkingBlock(targetCard) {
    if (!targetCard) return;
    const thinkingBlock = targetCard.querySelector('.chat-thinking-block');
    if (thinkingBlock && !thinkingBlock._finalized) {
      thinkingBlock._finalized = true;
      const pulse = thinkingBlock.querySelector('.animate-pulse');
      if (pulse) pulse.classList.remove('animate-pulse');
      const label = thinkingBlock.querySelector('.thinking-label');
      const timer = thinkingBlock.querySelector('.thinking-timer');
      if (label) {
        label.textContent = `Thought for ${timer ? timer.textContent : 'a moment'}`;
      }
      const body = thinkingBlock.querySelector('.thinking-body');
      if (body) body.classList.add('hidden');
      const chevron = thinkingBlock.querySelector('.thinking-chevron');
      if (chevron) chevron.className = 'fa-solid fa-chevron-right text-[9px] text-content-muted transition-transform thinking-chevron';
    }
  }

  // --- 8. Real-Time Sidecar Token & Status Streaming ---
  window.addEventListener('thinking', (e) => {
    if (!e.detail) return;
    const { sessionId, text, durationSeconds } = e.detail;

    let targetCard = null;
    if (sessionId) {
      targetCard = document.querySelector(`[data-session-id="${sessionId}"]`);
    }
    if (!targetCard && activeExecutionCards.size > 0) {
      targetCard = Array.from(activeExecutionCards.values())[0];
    }
    if (!targetCard) return;

    const thinkingContainer = targetCard.querySelector('.chat-thinking-container');
    if (!thinkingContainer) return;

    let block = thinkingContainer.querySelector('.chat-thinking-block');
    if (!block) {
      block = document.createElement('div');
      block.className = 'chat-thinking-block my-1.5 rounded-lg border border-sky-500/20 bg-sky-500/5 overflow-hidden transition-all duration-200';
      block.innerHTML = `
        <div class="thinking-header flex items-center justify-between px-3 py-1.5 cursor-pointer select-none text-[11px] font-mono hover:bg-sky-500/10 transition">
          <div class="flex items-center space-x-2 text-sky-400">
            <i class="fa-solid fa-brain text-[11px] animate-pulse"></i>
            <span class="thinking-label font-medium">Thinking...</span>
          </div>
          <div class="flex items-center space-x-2">
            <span class="thinking-timer text-[10px] text-content-muted">0.0s</span>
            <i class="fa-solid fa-chevron-right text-[9px] text-content-muted transition-transform thinking-chevron"></i>
          </div>
        </div>
        <div class="thinking-body hidden px-3 py-2 text-xs font-mono text-content-muted border-t border-sky-500/15 max-h-48 overflow-y-auto whitespace-pre-wrap leading-relaxed"></div>
      `;

      const header = block.querySelector('.thinking-header');
      const body = block.querySelector('.thinking-body');
      const chevron = block.querySelector('.thinking-chevron');
      header.addEventListener('click', () => {
        const isHidden = body.classList.toggle('hidden');
        chevron.className = isHidden
          ? 'fa-solid fa-chevron-right text-[9px] text-content-muted transition-transform thinking-chevron'
          : 'fa-solid fa-chevron-down text-[9px] text-sky-400 transition-transform thinking-chevron';
      });

      thinkingContainer.appendChild(block);
      targetCard._thinkingStartTime = Date.now();
    }

    if (text) {
      const body = block.querySelector('.thinking-body');
      if (body) {
        body.textContent += (body.textContent ? '\n' : '') + text;
      }
    }

    const timer = block.querySelector('.thinking-timer');
    if (timer) {
      if (typeof durationSeconds === 'number' && durationSeconds > 0) {
        timer.textContent = `${durationSeconds.toFixed(1)}s`;
      } else {
        const elapsed = ((Date.now() - (targetCard._thinkingStartTime || Date.now())) / 1000).toFixed(1);
        timer.textContent = `${elapsed}s`;
      }
    }

    const stream = document.getElementById('chat-stream');
    if (stream) stream.scrollTop = stream.scrollHeight;
  });

  window.addEventListener('tool_start', (e) => {
    if (!e.detail) return;
    const { sessionId, callId, tool, params } = e.detail;

    let targetCard = null;
    if (sessionId) {
      targetCard = document.querySelector(`[data-session-id="${sessionId}"]`);
    }
    if (!targetCard && activeExecutionCards.size > 0) {
      targetCard = Array.from(activeExecutionCards.values())[0];
    }
    if (!targetCard) return;

    // Finalize thinking state if still active
    finalizeThinkingBlock(targetCard);

    // Update overall badge
    const badge = targetCard.querySelector('.card-status-badge');
    if (badge) {
      badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-amber-500/15 text-amber-500 border border-amber-500/30';
      badge.textContent = `Executing: ${tool || 'Tool'}`;
    }

    const toolsContainer = targetCard.querySelector('.chat-tools-container');
    if (!toolsContainer) return;

    const toolCard = document.createElement('div');
    toolCard.className = 'tool-card rounded-lg border border-surface-border bg-surface-card overflow-hidden transition shadow-sm';
    toolCard.setAttribute('data-call-id', callId || `call_${Date.now()}`);
    const preview = formatToolPreview(tool, params);

    toolCard.innerHTML = `
      <div class="tool-card-header flex items-center justify-between px-3 py-2 bg-surface-deep cursor-pointer select-none font-mono text-xs border-b border-surface-border hover:bg-surface-card transition">
        <div class="flex items-center space-x-2 min-w-0">
          <i class="fa-solid ${getToolIcon(tool)} text-sky-400"></i>
          <span class="font-semibold text-content-main">${escapeHtml(tool || 'tool')}</span>
          ${preview ? `<span class="text-content-muted text-[11px] truncate max-w-sm font-sans">${escapeHtml(preview)}</span>` : ''}
        </div>
        <div class="flex items-center space-x-2 flex-shrink-0">
          <span class="tool-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-amber-500/15 text-amber-500 border border-amber-500/30 flex items-center gap-1.5">
            <span class="w-1.5 h-1.5 rounded-full bg-amber-400 animate-ping"></span>
            <span>Running</span>
          </span>
          <i class="fa-solid fa-chevron-right text-[10px] text-content-muted tool-chevron transition-transform"></i>
        </div>
      </div>
      <div class="tool-card-body hidden p-3 font-mono text-xs bg-black/30 overflow-x-auto max-h-56 leading-relaxed">
        <div class="text-content-dim text-[10px] uppercase font-bold tracking-wider mb-1">Invocation Parameters</div>
        <pre class="text-sky-300 whitespace-pre-wrap">${escapeHtml(typeof params === 'object' ? JSON.stringify(params, null, 2) : String(params || ''))}</pre>
        <div class="tool-output-section mt-2 pt-2 border-t border-white/10 hidden">
          <div class="text-content-dim text-[10px] uppercase font-bold tracking-wider mb-1">Output Result</div>
          <pre class="tool-output-text text-emerald-300 whitespace-pre-wrap"></pre>
        </div>
      </div>
    `;

    const header = toolCard.querySelector('.tool-card-header');
    const body = toolCard.querySelector('.tool-card-body');
    const chevron = toolCard.querySelector('.tool-chevron');
    header.addEventListener('click', () => {
      const isHidden = body.classList.toggle('hidden');
      chevron.className = isHidden
        ? 'fa-solid fa-chevron-right text-[10px] text-content-muted tool-chevron transition-transform'
        : 'fa-solid fa-chevron-down text-[10px] text-sky-400 tool-chevron transition-transform';
    });

    toolsContainer.appendChild(toolCard);
    const stream = document.getElementById('chat-stream');
    if (stream) stream.scrollTop = stream.scrollHeight;
  });

  window.addEventListener('tool_output', (e) => {
    if (!e.detail) return;
    const { sessionId, callId, success, result, error, durationMs } = e.detail;

    let targetCard = null;
    if (sessionId) {
      targetCard = document.querySelector(`[data-session-id="${sessionId}"]`);
    }
    if (!targetCard && activeExecutionCards.size > 0) {
      targetCard = Array.from(activeExecutionCards.values())[0];
    }
    if (!targetCard) return;

    let toolCard = null;
    if (callId) {
      toolCard = targetCard.querySelector(`.tool-card[data-call-id="${callId}"]`);
    }
    if (!toolCard) {
      const toolCards = targetCard.querySelectorAll('.tool-card');
      if (toolCards.length > 0) toolCard = toolCards[toolCards.length - 1];
    }

    if (toolCard) {
      const badge = toolCard.querySelector('.tool-status-badge');
      if (badge) {
        if (success !== false) {
          badge.className = 'tool-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/15 text-emerald-400 border border-emerald-500/30 flex items-center gap-1.5';
          badge.innerHTML = `<i class="fa-solid fa-check text-[9px]"></i><span>Completed (${durationMs || 0}ms)</span>`;
        } else {
          badge.className = 'tool-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-rose-500/15 text-rose-400 border border-rose-500/30 flex items-center gap-1.5';
          badge.innerHTML = `<i class="fa-solid fa-xmark text-[9px]"></i><span>Failed</span>`;
        }
      }

      const outputSec = toolCard.querySelector('.tool-output-section');
      const outputText = toolCard.querySelector('.tool-output-text');
      if (outputSec && outputText) {
        const val = result !== undefined ? result : error;
        outputText.textContent = typeof val === 'object' ? JSON.stringify(val, null, 2) : String(val || '');
        if (success === false) {
          outputText.className = 'tool-output-text text-rose-300 whitespace-pre-wrap';
        }
        outputSec.classList.remove('hidden');
      }
    }
  });

  window.addEventListener('chat_token', (e) => {
    if (!e.detail || typeof e.detail.text !== 'string') return;
    const { sessionId, text } = e.detail;

    let targetCard = null;
    if (sessionId) {
      targetCard = document.querySelector(`[data-session-id="${sessionId}"]`);
    }
    if (!targetCard && activeExecutionCards.size > 0) {
      targetCard = Array.from(activeExecutionCards.values())[0];
    }
    if (!targetCard) return;

    finalizeThinkingBlock(targetCard);

    const badge = targetCard.querySelector('.card-status-badge');
    if (badge && !badge.textContent.includes('Executing')) {
      badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-sky-500/15 text-sky-500 border border-sky-500/30';
      badge.textContent = 'Responding';
    }

    targetCard._rawMarkdown = (targetCard._rawMarkdown || '') + text;
    const markdownEl = targetCard.querySelector('.chat-assistant-markdown');
    if (markdownEl) {
      markdownEl.innerHTML = renderMarkdown(targetCard._rawMarkdown);
      const stream = document.getElementById('chat-stream');
      if (stream) stream.scrollTop = stream.scrollHeight;
    }
  });

  window.addEventListener('session_status', (e) => {
    if (!e.detail) return;
    const { sessionId, state, error } = e.detail;

    let targetCard = null;
    if (sessionId) {
      targetCard = document.querySelector(`[data-session-id="${sessionId}"]`);
    }
    if (!targetCard && activeExecutionCards.size > 0) {
      targetCard = Array.from(activeExecutionCards.values())[0];
    }

    if (targetCard) {
      const badge = targetCard.querySelector('.card-status-badge');
      const ping = targetCard.querySelector('.animate-ping');

      if (state === 'running') {
        if (badge) {
          badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-sky-500/15 text-sky-500 border border-sky-500/30';
          badge.textContent = 'Streaming';
        }
      } else if (state === 'thinking') {
        if (badge) {
          badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-sky-500/15 text-sky-500 border border-sky-500/30';
          badge.textContent = 'Thinking...';
        }
      } else if (state === 'tool_executing') {
        if (badge) {
          badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-amber-500/15 text-amber-500 border border-amber-500/30';
          badge.textContent = 'Executing Tool';
        }
      } else if (state === 'error') {
        finalizeThinkingBlock(targetCard);
        if (ping) ping.classList.remove('animate-ping');
        if (badge) {
          badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-rose-500/15 text-rose-500 border border-rose-500/30';
          badge.textContent = 'Failed';
        }

        const errContainer = targetCard.querySelector('.chat-error-container');
        if (errContainer) {
          errContainer.className = 'chat-error-container flex items-center space-x-2.5 px-3 py-2 rounded-lg bg-rose-500/10 border border-rose-500/20 text-rose-400 text-xs font-mono my-2';
          errContainer.innerHTML = `<i class="fa-solid fa-circle-exclamation text-rose-500 flex-shrink-0"></i><span class="truncate">${escapeHtml(error || 'Execution encountered an error')}</span>`;
          errContainer.classList.remove('hidden');
        }

        if (typeof window.showToast === 'function') {
          window.showToast(error || 'Execution failed', 'error', 4500);
        }
      } else if (state === 'idle' || state === 'completed') {
        finalizeThinkingBlock(targetCard);
        if (ping) ping.classList.remove('animate-ping');
        if (badge) {
          badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 border border-emerald-500/30';
          badge.textContent = 'Completed';
        }
      }
    }
  });

  window.addEventListener('account_state', (e) => {
    if (!e.detail) return;
    const { activeAccount, profiles } = e.detail;

    if (activeAccount && window.state?.antigravity) {
      window.state.antigravity.activeProfile = activeAccount;
    }
    if (Array.isArray(profiles) && window.state?.antigravity) {
      window.state.antigravity.profiles = profiles;
      const matched = profiles.find(p => p.name.toLowerCase() === (activeAccount || '').toLowerCase());
      if (matched && matched.model) {
        updateChatModelDisplay(matched.model);
      }
    }

    renderChatProfileManager();
    appendHudLog(`[Sidecar] Active profile switched to ${activeAccount}`, 'sky');
  });

  window.addEventListener('profile_changed', (e) => {
    if (!e.detail) return;
    const { activeProfile, profiles, model } = e.detail;
    if (model) {
      updateChatModelDisplay(model);
    } else {
      updateChatModelDisplay('Gemini 3.8 Flash (High)');
    }
    if (activeProfile && window.state?.antigravity) {
      window.state.antigravity.activeProfile = activeProfile;
    }
    if (Array.isArray(profiles) && window.state?.antigravity) {
      window.state.antigravity.profiles = profiles;
    }
    renderChatProfileManager();
  });

  // --- 9. Real-Time Terminal Data for ConPTY Raw CLI HUD ---
  window.addEventListener('terminal_data', (e) => {
    if (!e.detail || !e.detail.data) return;
    const rawData = String(e.detail.data);

    // Quake HUD Console Stream (Preserved for Raw CLI Tab & F12)
    const hudContent = document.getElementById('hud-terminal-content');
    if (hudContent) {
      const span = document.createElement('span');
      span.className = 'text-content-muted font-mono whitespace-pre-wrap';
      span.textContent = rawData;
      hudContent.appendChild(span);
      if (hudContent.children.length > 500) hudContent.removeChild(hudContent.firstChild);
      hudContent.scrollTop = hudContent.scrollHeight;
    }
  });

  window.addEventListener('agent_task_completed', (e) => {
    const payload = e.detail;
    if (!payload) return;
    appendHudLog(`[Agent Task Completed] ${payload.displayName || 'Agent'}: ${payload.isError ? 'Error' : 'Success'}`, payload.isError ? 'rose' : 'emerald');

    activeExecutionCards.forEach(card => {
      const badge = card.querySelector('.card-status-badge');
      if (badge) {
        if (payload.isError) {
          badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-rose-500/15 text-rose-500 border border-rose-500/30';
          badge.textContent = 'Error';
        } else {
          badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/15 text-emerald-600 dark:text-emerald-400 border border-emerald-500/30';
          badge.textContent = 'Completed';
        }
      }
    });
    activeExecutionCards.clear();
  });

  // State synchronization listeners
  window.addEventListener('workspaces_updated', (e) => {
    if (e.detail?.workspaces) {
      renderWorkspacesInChat(e.detail.workspaces, e.detail.activeWorkspaceId);
    }
  });

  window.addEventListener('workspace_activated', (e) => {
    if (e.detail?.activeWorkspaceId) {
      renderWorkspacesInChat(window.state?.workspaces, e.detail.activeWorkspaceId);
    }
  });

  window.addEventListener('profile_changed', () => {
    renderChatProfileManager();
  });

  window.addEventListener('antigravity_instance_status_changed', () => {
    renderChatProfileManager();
  });

  window.addEventListener('state_updated', () => {
    renderChatProfileManager();
  });

  window.addEventListener('state_initialized', () => {
    renderChatProfileManager();
  });

  // --- 9. Primary Subsystem Setup ---
  function setupAgentChatIde() {
    const promptInput = document.getElementById('prompt-input');
    const btnSend = document.getElementById('btn-send-prompt');
    const btnToggleHud = document.getElementById('btn-toggle-hud');
    const selWs = document.getElementById('select-project-workspace');
    const btnNewSession = document.getElementById('btn-new-session');

    if (promptInput) {
      const newInput = promptInput.cloneNode(true);
      promptInput.parentNode.replaceChild(newInput, promptInput);
      newInput.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
          e.preventDefault();
          submitUserPrompt();
        }
      });
    }

    if (btnSend) {
      const newBtn = btnSend.cloneNode(true);
      btnSend.parentNode.replaceChild(newBtn, btnSend);
      newBtn.addEventListener('click', submitUserPrompt);
    }

    if (btnToggleHud) {
      btnToggleHud.onclick = () => toggleHud();
    }

    if (btnNewSession) {
      btnNewSession.onclick = () => createNewSession();
    }

    if (selWs) {
      selWs.onchange = (e) => onWorkspaceChange(e.target.value);
    }

    // Initialize data
    renderWorkspacesInChat(window.state?.workspaces, window.state?.activeWorkspaceId);
    renderChatProfileManager();
    loadWorkspaceFiles();
    loadSessionHistory();

    // Initialize dynamic model display
    if (window.state?.antigravity?.model) {
      updateChatModelDisplay(window.state.antigravity.model);
    }
  }
  window.setupAgentChatIde = setupAgentChatIde;

  // Global Quake HUD key handler (F12 or ~)
  window.addEventListener('keydown', (e) => {
    if (e.key === 'F12' || (e.key === '`' && !['INPUT', 'TEXTAREA'].includes(e.target.tagName))) {
      e.preventDefault();
      toggleHud();
    }
  });

  // Auto-init on script load
  if (document.readyState === 'complete' || document.readyState === 'interactive') {
    setTimeout(setupAgentChatIde, 50);
  } else {
    document.addEventListener('DOMContentLoaded', setupAgentChatIde);
  }
})();
