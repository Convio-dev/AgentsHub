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
window.handleOpenCodeUpdate = handleOpenCodeUpdate;
window.handleOpenCodePrompt = handleOpenCodePrompt;
window.setupPromptModalListeners = setupPromptModalListeners;

window.renderOpenCodeCard = renderOpenCodeCard;
var renderOpenCodeCard = window.renderOpenCodeCard;
