// ==========================================================================
// Agents Hub - Chat-First AI Workspace Module
// Clean, modern, distraction-free conversational agent client
// ==========================================================================

(function() {
  'use strict';

  let chatMessages = [];
  let activeWorker = 'claude_code';
  let isGenerating = false;
  let isDrawerOpen = false;
  let drawerTerminal = null;
  let activeActionPending = null;

  function setupChatView() {
    const chatContainer = document.getElementById('view-chat');
    if (!chatContainer) return;

    const messagesArea = document.getElementById('chat-messages-container');
    const welcomeState = document.getElementById('chat-welcome-state');
    const inputArea = document.getElementById('chat-input-textarea');
    const btnSend = document.getElementById('btn-chat-send');
    const btnClear = document.getElementById('btn-chat-clear');
    const btnNew = document.getElementById('btn-chat-new');
    const workerSelect = document.getElementById('chat-worker-select');
    const statusPill = document.getElementById('chat-status-pill');
    const statusDot = document.getElementById('chat-status-dot');
    const statusText = document.getElementById('chat-status-text');
    const workspacePill = document.getElementById('chat-workspace-name');
    const btnDrawer = document.getElementById('btn-toggle-terminal-drawer');
    const drawer = document.getElementById('chat-terminal-drawer');
    const btnDrawerClose = document.getElementById('btn-chat-drawer-close');
    const btnDrawerClear = document.getElementById('btn-chat-drawer-clear');
    const drawerCliName = document.getElementById('chat-drawer-cli-name');
    const drawerContent = document.getElementById('chat-drawer-terminal-content');

    // 1. Populate workers dropdown & account profiles
    function isWorkerRunning(workerId) {
      if (!workerId) return false;
      if (workerId.startsWith('antigravity_')) {
        const profName = workerId.substring('antigravity_'.length).toLowerCase();
        const profs = window.state?.antigravity?.profiles || [];
        const found = profs.find(p => (typeof p === 'object' ? (p.name || '') : p).toLowerCase() === profName);
        return found && typeof found === 'object' ? !!found.isRunning : false;
      }
      if (workerId === 'claude_code' || workerId === 'ClaudeCode') {
        return window.state?.claudeStatus === 'Online' || window.state?.clis?.ClaudeCode?.state === 'Running';
      }
      if (workerId === 'hermes' || workerId === 'Hermes') {
        return window.state?.hermesStatus === 'Online' || window.state?.clis?.Hermes?.state === 'Running';
      }
      return false;
    }

    function updateWorkerPowerButton() {
      const btnPower = document.getElementById('btn-chat-worker-toggle');
      const lblPower = document.getElementById('lbl-worker-power');
      const iconPower = document.getElementById('icon-worker-power');
      if (!btnPower || !lblPower) return;

      const running = isWorkerRunning(activeWorker);
      if (running) {
        btnPower.className = 'btn-worker-power running';
        lblPower.textContent = 'Stop CLI';
        btnPower.title = 'Stop this background CLI process';
        if (iconPower) {
          iconPower.innerHTML = '<rect width="12" height="12" x="6" y="6" rx="2" fill="currentColor"/>';
        }
        updateWorkerStatus('idle', 'Online (Ready)');
      } else {
        btnPower.className = 'btn-worker-power offline';
        lblPower.textContent = 'Start CLI';
        btnPower.title = 'Start this CLI process in the background';
        if (iconPower) {
          iconPower.innerHTML = '<polygon points="5 3 19 12 5 21 5 3" fill="currentColor"/>';
        }
        updateWorkerStatus('offline', 'Offline');
      }
    }

    function refreshWorkerList() {
      if (!workerSelect) return;
      const profilesGroup = document.getElementById('chat-optgroup-antigravity');
      if (profilesGroup) {
        profilesGroup.innerHTML = '';
        const rawProfs = window.state?.antigravity?.profiles || window.state?.profiles || [];
        const profs = Array.isArray(rawProfs) ? rawProfs : [];
        if (profs.length === 0) {
          const opt = document.createElement('option');
          opt.value = 'antigravity_Default';
          opt.textContent = 'Antigravity (Default)';
          profilesGroup.appendChild(opt);
        } else {
          profs.forEach((p) => {
            const name = typeof p === 'object' ? (p.name || '') : p;
            const email = typeof p === 'object' ? (p.email || '') : '';
            const isRunning = typeof p === 'object' ? !!p.isRunning : false;
            const opt = document.createElement('option');
            opt.value = `antigravity_${name}`;
            const label = email ? `${name} (${email})` : name;
            opt.textContent = `${label} ${isRunning ? '● [Running]' : '○ [Offline]'}`;
            profilesGroup.appendChild(opt);
          });
        }
      }

      // Sync custom CLIs if present
      const customGroup = document.getElementById('chat-optgroup-custom');
      if (customGroup && window.state?.customClis) {
        customGroup.innerHTML = '';
        const clis = Array.isArray(window.state.customClis) ? window.state.customClis : [];
        if (clis.length > 0) {
          customGroup.classList.remove('hidden');
          clis.forEach(c => {
            const opt = document.createElement('option');
            opt.value = `custom_${c.id || c.name}`;
            opt.textContent = c.name;
            customGroup.appendChild(opt);
          });
        } else {
          customGroup.classList.add('hidden');
        }
      }

      // Preserve activeWorker selection or set to first available
      if (workerSelect.querySelector(`option[value="${activeWorker}"]`)) {
        workerSelect.value = activeWorker;
      } else {
        const activeProf = window.state?.antigravity?.activeProfile;
        if (activeProf && workerSelect.querySelector(`option[value="antigravity_${activeProf}"]`)) {
          workerSelect.value = `antigravity_${activeProf}`;
          activeWorker = `antigravity_${activeProf}`;
        }
      }

      updateWorkerPowerButton();
    }

    // Direct Start / Stop button listener
    document.getElementById('btn-chat-worker-toggle')?.addEventListener('click', async () => {
      const btnPower = document.getElementById('btn-chat-worker-toggle');
      const lblPower = document.getElementById('lbl-worker-power');
      const currentlyRunning = isWorkerRunning(activeWorker);

      if (lblPower) lblPower.textContent = currentlyRunning ? 'Stopping...' : 'Starting...';
      if (btnPower) btnPower.style.opacity = '0.6';

      try {
        if (activeWorker.startsWith('antigravity_')) {
          const profName = activeWorker.substring('antigravity_'.length);
          if (currentlyRunning) {
            await window.callBridge('stop_antigravity_instance', { profile: profName });
            if (typeof window.showToast === 'function') window.showToast(`Stopped Antigravity profile '${profName}'`, 'info');
          } else {
            await window.callBridge('start_antigravity_instance', { profile: profName });
            if (typeof window.showToast === 'function') window.showToast(`Started Antigravity profile '${profName}' in background`, 'success');
          }
        } else if (activeWorker === 'claude_code') {
          if (currentlyRunning) {
            await window.callBridge('stop_cli', { cli: 'ClaudeCode' });
          } else {
            await window.callBridge('start_cli', { cli: 'ClaudeCode' });
          }
        } else if (activeWorker === 'hermes') {
          if (currentlyRunning) {
            await window.callBridge('stop_cli', { cli: 'Hermes' });
          } else {
            await window.callBridge('start_cli', { cli: 'Hermes' });
          }
        }
      } catch (err) {
        if (typeof window.showToast === 'function') window.showToast(`Failed: ${err?.message || err}`, 'error');
      } finally {
        if (btnPower) btnPower.style.opacity = '1';
        setTimeout(refreshWorkerList, 300);
      }
    });

    // 2. Sync workspace badge
    function updateWorkspaceBadge() {
      if (!workspacePill) return;
      const ws = window.state?.activeWorkspace || window.state?.workspaces?.active;
      if (ws && (ws.name || ws.path || ws.workingDirectory)) {
        const name = ws.name || (ws.path ? ws.path.split(/[\\/]/).pop() : 'Workspace');
        workspacePill.textContent = name;
        workspacePill.title = ws.path || ws.workingDirectory || name;
      } else {
        workspacePill.textContent = 'Global Workspace';
        workspacePill.title = 'Default application directory';
      }
    }

    // 3. Worker Selection
    workerSelect?.addEventListener('change', () => {
      activeWorker = workerSelect.value;
      if (drawerCliName) {
        drawerCliName.textContent = workerSelect.options[workerSelect.selectedIndex]?.text || activeWorker;
      }
      updateWorkerPowerButton();
    });

    function updateWorkerStatus(status, text) {
      if (!statusDot || !statusText) return;
      statusDot.className = `chat-status-dot ${status}`;
      statusText.textContent = text || status;
    }

    // 4. Auto-growing input textarea
    inputArea?.addEventListener('input', () => {
      inputArea.style.height = 'auto';
      const newHeight = Math.min(inputArea.scrollHeight, 180);
      inputArea.style.height = (newHeight > 24 ? newHeight : 24) + 'px';
    });

    inputArea?.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        handleSendMessage();
      }
    });

    btnSend?.addEventListener('click', () => {
      if (isGenerating) {
        handleStopGeneration();
      } else {
        handleSendMessage();
      }
    });

    async function handleStopGeneration() {
      if (!isGenerating) return;
      try {
        if (typeof window.callBridge === 'function') {
          await window.callBridge('stop_chat_generation', { workerId: activeWorker });
        }
      } catch (e) {
        console.warn('Error stopping generation:', e);
      }
      setGenerating(false);
      updateWorkerStatus('idle', 'Ready');
    }

    // Quick actions in welcome card
    document.querySelectorAll('.chat-quick-card').forEach(card => {
      card.addEventListener('click', () => {
        const prompt = card.dataset.prompt;
        if (prompt && inputArea) {
          inputArea.value = prompt;
          inputArea.focus();
          handleSendMessage();
        }
      });
    });

    // 5. Send message logic
    async function handleSendMessage() {
      const text = inputArea?.value?.trim();
      if (!text || isGenerating) return;

      if (welcomeState) welcomeState.style.display = 'none';

      // Append user message
      const userMsg = {
        id: 'msg_' + Date.now(),
        role: 'user',
        content: text,
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
      };
      chatMessages.push(userMsg);
      renderMessage(userMsg);

      inputArea.value = '';
      inputArea.style.height = 'auto';

      // Prepare assistant streaming placeholder
      const assistantMsg = {
        id: 'msg_' + (Date.now() + 1),
        role: 'assistant',
        content: '',
        thought: '',
        worker: activeWorker,
        timestamp: new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
        isStreaming: true
      };
      chatMessages.push(assistantMsg);
      const assistantEl = renderMessage(assistantMsg);

      setGenerating(true);
      updateWorkerStatus('busy', 'Thinking & Generating...');

      // Dispatch to C# Backend via Bridge
      try {
        if (typeof window.callBridge === 'function') {
          const resp = await window.callBridge('send_chat_message', {
            workerId: activeWorker,
            prompt: text,
            messageId: assistantMsg.id
          });
          if (resp && resp.immediateResponse) {
            appendChatToken(assistantMsg.id, resp.immediateResponse);
            finalizeAssistantMessage(assistantMsg.id);
          }
        } else {
          // Mock local response if bridge not yet wired
          simulateLocalStream(assistantMsg.id, `Processing request with backend worker **${activeWorker}**...\n\nAnalyzing workspace context and preparing execution plan.`);
        }
      } catch (err) {
        appendChatToken(assistantMsg.id, `\n\n[Error communicating with backend worker: ${err?.message || err}]`);
        finalizeAssistantMessage(assistantMsg.id);
      }
    }

    function setGenerating(generating) {
      isGenerating = generating;
      if (!btnSend) return;
      if (generating) {
        btnSend.innerHTML = '<svg class="icon-svg" style="width: 14px; height: 14px; color: #ef4444;" viewBox="0 0 24 24"><rect width="14" height="14" x="5" y="5" rx="2" fill="currentColor"/></svg>';
        btnSend.title = 'Stop generation';
      } else {
        btnSend.innerHTML = '<svg class="icon-svg" style="width: 15px; height: 15px;" viewBox="0 0 24 24"><line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/></svg>';
        btnSend.title = 'Send message (Enter)';
      }
    }

    function simulateLocalStream(msgId, fullText) {
      let idx = 0;
      const timer = setInterval(() => {
        if (idx < fullText.length) {
          const chunk = fullText.slice(idx, idx + 4);
          idx += 4;
          appendChatToken(msgId, chunk);
        } else {
          clearInterval(timer);
          finalizeAssistantMessage(msgId);
        }
      }, 30);
    }

    // 6. Message Rendering Engine
    function renderMessage(msg) {
      if (!messagesArea) return null;

      const row = document.createElement('div');
      row.className = `chat-message-row ${msg.role}`;
      row.id = `chat-row-${msg.id}`;

      if (msg.role === 'assistant') {
        const avatar = document.createElement('div');
        avatar.className = 'chat-avatar assistant-avatar';
        avatar.innerHTML = '<svg class="icon-svg" style="width: 16px; height: 16px;" viewBox="0 0 24 24"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>';
        row.appendChild(avatar);
      }

      const bubble = document.createElement('div');
      bubble.className = 'chat-bubble';
      bubble.id = `chat-bubble-${msg.id}`;

      if (msg.role === 'assistant') {
        // Thought accordion placeholder
        const thoughtWrap = document.createElement('div');
        thoughtWrap.className = 'chat-thought-block hidden';
        thoughtWrap.id = `thought-wrap-${msg.id}`;
        thoughtWrap.innerHTML = `
          <div class="chat-thought-header" onclick="this.parentElement.classList.toggle('collapsed')">
            <span>Thinking Process</span>
            <svg class="icon-svg" style="width: 12px; height: 12px;" viewBox="0 0 24 24"><polyline points="6 9 12 15 18 9"/></svg>
          </div>
          <div class="chat-thought-body" id="thought-body-${msg.id}"></div>
        `;
        bubble.appendChild(thoughtWrap);

        const contentDiv = document.createElement('div');
        contentDiv.className = 'chat-content-body';
        contentDiv.id = `chat-body-${msg.id}`;
        contentDiv.innerHTML = formatMarkdown(msg.content);
        bubble.appendChild(contentDiv);

        // Action card placeholder
        const actionWrap = document.createElement('div');
        actionWrap.id = `action-wrap-${msg.id}`;
        bubble.appendChild(actionWrap);
      } else {
        bubble.textContent = msg.content;
      }

      row.appendChild(bubble);

      if (msg.role === 'user') {
        const avatar = document.createElement('div');
        avatar.className = 'chat-avatar user-avatar';
        avatar.innerHTML = '<svg class="icon-svg" style="width: 15px; height: 15px;" viewBox="0 0 24 24"><path d="M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>';
        row.appendChild(avatar);
      }

      messagesArea.appendChild(row);
      messagesArea.scrollTop = messagesArea.scrollHeight;
      return row;
    }

    // 7. Token Stream Appender
    function appendChatToken(msgId, token) {
      const msg = chatMessages.find(m => m.id === msgId);
      if (!msg) return;

      msg.content += token;
      const bodyEl = document.getElementById(`chat-body-${msgId}`);
      if (bodyEl) {
        bodyEl.innerHTML = formatMarkdown(msg.content);
      }
      if (messagesArea) {
        messagesArea.scrollTop = messagesArea.scrollHeight;
      }
    }

    function appendChatThought(msgId, thoughtToken) {
      const msg = chatMessages.find(m => m.id === msgId);
      if (!msg) return;

      msg.thought = (msg.thought || '') + thoughtToken;
      const wrap = document.getElementById(`thought-wrap-${msgId}`);
      const body = document.getElementById(`thought-body-${msgId}`);
      if (wrap && body) {
        wrap.classList.remove('hidden');
        body.textContent = msg.thought;
      }
    }

    function finalizeAssistantMessage(msgId) {
      const msg = chatMessages.find(m => m.id === msgId);
      if (msg) {
        msg.isStreaming = false;
      }
      setGenerating(false);
      updateWorkerStatus('idle', 'Ready');
    }

    // 8. Action Card Rendering (Interactive CLI Confirmations)
    function renderActionCard(msgId, actionData) {
      const actionWrap = document.getElementById(`action-wrap-${msgId}`);
      if (!actionWrap || !actionData) return;

      activeActionPending = actionData;
      updateWorkerStatus('waiting', 'Action Required');

      const card = document.createElement('div');
      card.className = 'chat-action-card';
      card.innerHTML = `
        <div class="chat-action-card-header">
          <svg class="icon-svg" style="width: 14px; height: 14px;" viewBox="0 0 24 24"><path d="M12 9v4M12 17h.01M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/></svg>
          <span>${actionData.title || 'Permission Confirmation Required'}</span>
        </div>
        <div class="chat-action-card-desc">${actionData.description || 'The CLI is requesting confirmation to execute:'}</div>
        ${actionData.command ? `<div class="chat-action-card-command">${escapeHtml(actionData.command)}</div>` : ''}
        <div class="chat-action-buttons">
          <button type="button" class="btn-action-approve" id="btn-approve-${msgId}">Approve &amp; Continue</button>
          <button type="button" class="btn-action-deny" id="btn-deny-${msgId}">Deny Action</button>
        </div>
      `;

      actionWrap.innerHTML = '';
      actionWrap.appendChild(card);

      document.getElementById(`btn-approve-${msgId}`)?.addEventListener('click', () => {
        handleActionResponse(actionData.id || msgId, true, card);
      });
      document.getElementById(`btn-deny-${msgId}`)?.addEventListener('click', () => {
        handleActionResponse(actionData.id || msgId, false, card);
      });

      if (messagesArea) messagesArea.scrollTop = messagesArea.scrollHeight;
    }

    async function handleActionResponse(actionId, approved, cardElement) {
      if (cardElement) {
        cardElement.style.opacity = '0.6';
        cardElement.style.pointerEvents = 'none';
        const btns = cardElement.querySelector('.chat-action-buttons');
        if (btns) {
          btns.innerHTML = `<span style="font-size: 11px; font-weight: 600; color: ${approved ? 'var(--accent-green)' : 'var(--accent-red)'};">${approved ? 'Approved' : 'Denied'}</span>`;
        }
      }

      if (typeof window.callBridge === 'function') {
        await window.callBridge('respond_chat_action', {
          actionId: actionId,
          approved: approved,
          response: approved ? 'y' : 'n'
        });
      }
      activeActionPending = null;
      updateWorkerStatus('busy', 'Executing...');
    }

    // 9. Markdown & Code Block Formatter
    function formatMarkdown(text) {
      if (!text) return '';

      let html = escapeHtml(text);

      // Fenced Code blocks
      html = html.replace(/```([a-zA-Z0-9_-]*)\r?\n([\s\S]*?)```/g, (match, lang, code) => {
        const langLabel = lang || 'code';
        return `
          <div class="chat-code-block-wrapper">
            <div class="chat-code-header">
              <span>${langLabel}</span>
              <button type="button" class="chat-code-copy-btn" onclick="navigator.clipboard.writeText(this.closest('.chat-code-block-wrapper').querySelector('pre code').innerText); this.textContent='Copied!'; setTimeout(() => this.textContent='Copy', 1500)">Copy</button>
            </div>
            <pre><code>${code.trim()}</code></pre>
          </div>
        `;
      });

      // Inline code
      html = html.replace(/`([^`]+)`/g, '<code>$1</code>');

      // Bold & Italic
      html = html.replace(/\*\*([^*]+)\*\*/g, '<b>$1</b>');
      html = html.replace(/\*([^*]+)\*/g, '<i>$1</i>');

      // Line breaks outside pre
      html = html.replace(/\n/g, '<br>');

      return html;
    }

    function escapeHtml(str) {
      return (str || '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
    }

    // 10. Drawer Toggle (F12 or Button)
    function toggleDrawer(open) {
      isDrawerOpen = (open !== undefined) ? open : !isDrawerOpen;
      if (drawer) {
        drawer.classList.toggle('open', isDrawerOpen);
      }
      if (btnDrawer) {
        btnDrawer.classList.toggle('active', isDrawerOpen);
      }
    }

    btnDrawer?.addEventListener('click', () => toggleDrawer());
    btnDrawerClose?.addEventListener('click', () => toggleDrawer(false));
    btnDrawerClear?.addEventListener('click', () => {
      if (drawerContent) drawerContent.innerHTML = '';
    });

    window.addEventListener('keydown', (e) => {
      if (e.key === 'F12') {
        e.preventDefault();
        toggleDrawer();
      }
    });

    // Clear chat
    btnClear?.addEventListener('click', () => {
      chatMessages = [];
      if (messagesArea) {
        messagesArea.innerHTML = '';
        if (welcomeState) {
          messagesArea.appendChild(welcomeState);
          welcomeState.style.display = 'block';
        }
      }
    });

    btnNew?.addEventListener('click', () => {
      btnClear?.click();
      inputArea?.focus();
    });

    // Initial sync
    refreshWorkerList();
    updateWorkspaceBadge();

    // 11. Backend Push Event Listeners
    window.addEventListener('chat_token_stream', (e) => {
      if (e.detail?.messageId && e.detail?.token) {
        appendChatToken(e.detail.messageId, e.detail.token);
      }
    });

    window.addEventListener('chat_thought_stream', (e) => {
      if (e.detail?.messageId && e.detail?.thought) {
        appendChatThought(e.detail.messageId, e.detail.thought);
      }
    });

    window.addEventListener('chat_action_requested', (e) => {
      if (e.detail?.messageId && e.detail?.action) {
        renderActionCard(e.detail.messageId, e.detail.action);
      }
    });

    window.addEventListener('chat_message_completed', (e) => {
      if (e.detail?.messageId) {
        finalizeAssistantMessage(e.detail.messageId);
      } else {
        setGenerating(false);
        updateWorkerStatus('idle', 'Ready');
      }
    });

    window.addEventListener('chat_status_changed', (e) => {
      if (e.detail) {
        updateWorkerStatus(e.detail.status, e.detail.text);
      }
    });

    // Stream raw terminal data to the drawer peek view
    window.addEventListener('terminal_data', (e) => {
      if (drawerContent && e.detail?.data) {
        const span = document.createElement('span');
        span.textContent = e.detail.data.replace(/\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])/g, '');
        drawerContent.appendChild(span);
        if (drawerContent.childNodes.length > 300) {
          drawerContent.removeChild(drawerContent.firstChild);
        }
        drawerContent.scrollTop = drawerContent.scrollHeight;
      }
    });

    // Sync profile and workspace changes
    window.addEventListener('state-loaded', () => {
      refreshWorkerList();
      updateWorkspaceBadge();
    });

    window.addEventListener('antigravity_instance_status_changed', () => {
      refreshWorkerList();
    });

    window.addEventListener('cli_status_changed', () => {
      refreshWorkerList();
    });

    // Auto-focus chat input when switching to Agent Chat view
    window.addEventListener('view-switched', (e) => {
      if (e.detail?.activeView === 'chat') {
        setTimeout(() => inputArea?.focus(), 60);
      }
    });

    // 12. Expose Global Chat API for Bridge Events
    window.AgentsHubChat = {
      appendToken: appendChatToken,
      appendThought: appendChatThought,
      finalizeMessage: finalizeAssistantMessage,
      requestAction: renderActionCard,
      updateStatus: updateWorkerStatus,
      refreshWorkers: refreshWorkerList,
      updateWorkspace: updateWorkspaceBadge,
      toggleDrawer: toggleDrawer
    };
  }

  window.setupChatView = setupChatView;
})();
