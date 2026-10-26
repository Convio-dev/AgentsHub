// AgentsHub v2.0 AI Agent Chat IDE Subsystem
(function initAgentChatIdeSubsystem() {
  'use strict';

  let isHudOpen = false;
  let activeExecutionCard = null;

  function escapeHtml(str) {
    return String(str || '')
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
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
      container.innerHTML = '<p class="text-slate-500">// Terminal buffer cleared. ConPTY process listening...</p>';
    }
  }
  window.clearHudTerminal = clearHudTerminal;

  function appendHudLog(message, color = 'slate') {
    const hudContent = document.getElementById('hud-terminal-content');
    if (!hudContent) return;
    const colorClasses = {
      slate: 'text-slate-400',
      sky: 'text-sky-400',
      emerald: 'text-emerald-400',
      amber: 'text-amber-400',
      rose: 'text-rose-400'
    };
    const cls = colorClasses[color] || 'text-slate-300';
    const p = document.createElement('p');
    p.className = cls;
    p.textContent = message;
    hudContent.appendChild(p);

    // Keep buffer bounded
    if (hudContent.children.length > 500) {
      hudContent.removeChild(hudContent.firstChild);
    }
    hudContent.scrollTop = hudContent.scrollHeight;
  }
  window.appendHudLog = appendHudLog;

  // --- 2. Real Workspaces Synchronization ---
  function renderWorkspacesInChat(workspaces, activeId) {
    const sel = document.getElementById('select-project-workspace');
    const wsLabel = document.getElementById('chat-active-workspace-label');
    const list = Array.isArray(workspaces) && workspaces.length > 0 ? workspaces : (window.state?.workspaces || []);
    const effActiveId = activeId || window.state?.activeWorkspaceId;
    const activeWs = list.find(w => w.id === effActiveId) || list[0] || null;

    if (wsLabel) {
      wsLabel.textContent = activeWs ? activeWs.name : 'Default Workspace';
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
  }
  window.onWorkspaceChange = onWorkspaceChange;

  // --- 3. Real Profiles Synchronization ---
  function renderProfilesInChat(profiles, activeProfile) {
    const sel = document.getElementById('agent-profile-selector');
    const badge = document.getElementById('header-profile-badge');
    const railBtn = document.getElementById('btn-rail-profile');

    const rawList = Array.isArray(profiles) && profiles.length > 0
      ? profiles
      : (window.state?.antigravity?.profiles || []);
    const normalized = rawList.map(p => (typeof p === 'object' ? p.name : p));
    const active = activeProfile || window.state?.antigravity?.activeProfile || normalized[0] || 'Default';

    if (badge) {
      badge.textContent = `Antigravity CLI (agy) • ${active}`;
    }

    if (railBtn) {
      const shortName = active.replace(/[^a-zA-Z0-9]/g, '').slice(0, 2).toUpperCase() || 'Z1';
      railBtn.querySelector('span:first-child')?.replaceWith(document.createTextNode(shortName));
      railBtn.title = `Current Active Profile: ${active}`;
    }

    if (sel) {
      sel.innerHTML = '';
      if (normalized.length === 0) {
        const opt = document.createElement('option');
        opt.value = active;
        opt.textContent = `agy: ${active}`;
        sel.appendChild(opt);
      } else {
        normalized.forEach(pName => {
          const opt = document.createElement('option');
          const pObj = rawList.find(x => (typeof x === 'object' ? x.name : x).toLowerCase() === pName.toLowerCase());
          const isRun = typeof pObj === 'object' && pObj.isRunning;
          const pPort = typeof pObj === 'object' && pObj.port ? pObj.port : null;
          opt.value = pName;
          opt.textContent = isRun ? `agy: ${pName} (Port ${pPort || 'Active'})` : `agy: ${pName}`;
          if (pName.toLowerCase() === active.toLowerCase()) opt.selected = true;
          sel.appendChild(opt);
        });
      }
    }
  }
  window.renderProfilesInChat = renderProfilesInChat;

  async function switchAgentProfile(profileName) {
    if (!profileName) return;
    try {
      if (typeof window.callBridge === 'function') {
        const res = await window.callBridge('switch_antigravity_profile', { profile: profileName });
        if (window.state?.antigravity) {
          window.state.antigravity.activeProfile = profileName;
          if (res?.profiles) window.state.antigravity.profiles = res.profiles;
        }
      }
      renderProfilesInChat(window.state?.antigravity?.profiles, profileName);
      appendHudLog(`[VirtualHome] Profile switched to ${profileName}. Supervised ConPTY attached.`, 'emerald');
      if (typeof window.showToast === 'function') {
        window.showToast(`Switched active profile: ${profileName}`, 'success', 2500);
      }
    } catch (err) {
      appendHudLog(`[Profile Switch Error] ${err?.message || err}`, 'rose');
    }
  }
  window.switchAgentProfile = switchAgentProfile;

  // --- 4. User Prompt Submission & Real ConPTY Dispatch ---
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

    // Log to Quake HUD
    appendHudLog(`[User Prompt] ${text.slice(0, 100)}`, 'sky');

    // 2. Render Live Agent Execution Card
    const activeProfile = document.getElementById('agent-profile-selector')?.value || 'Default';
    const executionCard = document.createElement('div');
    executionCard.className = 'flex items-start space-x-3 max-w-3xl';
    executionCard.innerHTML = `
      <div class="w-8 h-8 rounded-lg bg-sky-600 flex items-center justify-center text-white text-xs flex-shrink-0 shadow-md shadow-sky-600/30">
        <i class="fa-solid fa-microchip"></i>
      </div>
      <div class="flex-1 space-y-3">
        <div class="bg-surface-card border border-surface-border rounded-xl p-4 text-xs space-y-2.5 shadow-sm">
          <div class="flex items-center justify-between font-mono pb-2 border-b border-surface-border">
            <span class="text-content-main font-semibold flex items-center space-x-2">
              <span class="w-2 h-2 rounded-full bg-sky-400 animate-ping"></span>
              <span>Process: Antigravity (${escapeHtml(activeProfile)})</span>
            </span>
            <span class="card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-sky-500/15 text-sky-500 border border-sky-500/30">Executing</span>
          </div>

          <!-- Live Output Snip -->
          <div class="p-2.5 rounded bg-surface-deep border border-surface-border font-mono text-[11px] text-content-main space-y-1">
            <div class="text-content-dim">// Dispatched to background ConPTY supervisor</div>
            <div class="exec-live-line text-emerald-500 font-medium">Ready...</div>
          </div>

          <div class="flex items-center justify-between pt-1 text-[11px]">
            <button onclick="if (typeof toggleHud === 'function') toggleHud(true);" class="text-sky-500 hover:text-sky-400 font-mono flex items-center space-x-1.5 transition">
              <i class="fa-solid fa-terminal text-[10px]"></i>
              <span>View Full Terminal (F12) &rarr;</span>
            </button>
            <button onclick="if (typeof switchView === 'function') switchView('repositories');" class="text-content-muted hover:text-content-main font-mono transition">
              Inspect Diff &rarr;
            </button>
          </div>
        </div>
      </div>
    `;
    msgContainer.appendChild(executionCard);
    stream.scrollTop = stream.scrollHeight;
    activeExecutionCard = executionCard;

    // 3. Dispatch to ConPTY Supervisor
    if (typeof window.callBridge === 'function') {
      try {
        await window.callBridge('send_terminal_input', {
          cli: 'antigravity_cli',
          input: text + '\n'
        });
      } catch (err) {
        appendHudLog(`[CLI Send Error] ${err?.message || err}`, 'rose');
      }
    }

    // 4. Notify WebView2 Host
    if (window.chrome && window.chrome.webview) {
      try {
        window.chrome.webview.postMessage({
          event: 'agent_prompt_submitted',
          prompt: text,
          profile: activeProfile
        });
      } catch (e) {}
    }
  }
  window.submitUserPrompt = submitUserPrompt;

  // --- 5. Real-Time Terminal Data & Task Completion Listener ---
  window.addEventListener('terminal_data', (e) => {
    if (e.detail && e.detail.data) {
      const dataStr = String(e.detail.data);
      const hudContent = document.getElementById('hud-terminal-content');
      if (hudContent) {
        const span = document.createElement('span');
        span.className = 'text-slate-300 font-mono';
        span.textContent = dataStr;
        hudContent.appendChild(span);
        if (hudContent.children.length > 500) hudContent.removeChild(hudContent.firstChild);
        hudContent.scrollTop = hudContent.scrollHeight;
      }

      if (activeExecutionCard) {
        const liveLine = activeExecutionCard.querySelector('.exec-live-line');
        if (liveLine) {
          const lines = dataStr.split(/[\r\n]+/).filter(Boolean);
          if (lines.length > 0) {
            liveLine.textContent = lines[lines.length - 1].slice(0, 80);
          }
        }
      }
    }
  });

  window.addEventListener('agent_task_completed', (e) => {
    const payload = e.detail;
    if (!payload) return;
    appendHudLog(`[Agent Task Completed] ${payload.displayName || 'Agent'}: ${payload.isError ? 'Error' : 'Success'}`, payload.isError ? 'rose' : 'emerald');

    if (activeExecutionCard) {
      const badge = activeExecutionCard.querySelector('.card-status-badge');
      if (badge) {
        if (payload.isError) {
          badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-rose-950/70 text-rose-400 border border-rose-800/60';
          badge.textContent = 'Error';
        } else {
          badge.className = 'card-status-badge px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-950/70 text-emerald-400 border border-emerald-800/60';
          badge.textContent = 'Completed';
        }
      }
      activeExecutionCard = null;
    }
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

  window.addEventListener('profile_changed', (e) => {
    if (e.detail) {
      renderProfilesInChat(e.detail.profiles, e.detail.activeProfile);
    }
  });

  window.addEventListener('antigravity_instance_status_changed', (e) => {
    if (window.state?.antigravity) {
      renderProfilesInChat(window.state.antigravity.profiles, window.state.antigravity.activeProfile);
    }
  });

  // --- 6. Primary Subsystem Setup ---
  function setupAgentChatIde() {
    const promptInput = document.getElementById('prompt-input');
    const btnSend = document.getElementById('btn-send-prompt');
    const btnToggleHud = document.getElementById('btn-toggle-hud');
    const selWs = document.getElementById('select-project-workspace');
    const selProf = document.getElementById('agent-profile-selector');

    if (promptInput) {
      // Remove stale clone listeners
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

    if (selWs) {
      selWs.onchange = (e) => onWorkspaceChange(e.target.value);
    }

    if (selProf) {
      selProf.onchange = (e) => switchAgentProfile(e.target.value);
    }

    // Populate current initial state
    renderWorkspacesInChat(window.state?.workspaces, window.state?.activeWorkspaceId);
    renderProfilesInChat(window.state?.antigravity?.profiles, window.state?.antigravity?.activeProfile);
  }
  window.setupAgentChatIde = setupAgentChatIde;

  // Global Quake HUD key handler
  window.addEventListener('keydown', (e) => {
    if (e.key === 'F12' || (e.key === '`' && !['INPUT', 'TEXTAREA'].includes(e.target.tagName))) {
      e.preventDefault();
      toggleHud();
    }
  });

  // Auto-init on script load if DOM is ready
  if (document.readyState === 'complete' || document.readyState === 'interactive') {
    setTimeout(setupAgentChatIde, 50);
  } else {
    document.addEventListener('DOMContentLoaded', setupAgentChatIde);
  }
})();
