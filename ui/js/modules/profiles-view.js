  async function launchAntigravityAppFromDash() {
    const btn = document.getElementById('btn-open-antigravity-app-dash');
    if (btn) btn.disabled = true;

    try {
      const devTools = (state.aiConfig && state.aiConfig.devTools) || [];
      const tool = devTools.find(t => t.id === 'antigravity_app' || (t.name && t.name.toLowerCase() === 'antigravity app'));
      const target = (tool && tool.target) || (state.aiConfig && state.aiConfig.toolTargets && (state.aiConfig.toolTargets['Antigravity App'] || state.aiConfig.toolTargets['antigravity_app'])) || '';

      const res = await callBridge('launch_tool', {
        toolId: 'antigravity_app',
        toolName: 'Antigravity App',
        target: target
      });

      if (res && res.message) {
        showToast(res.message, 'success', 3500);
      } else {
        showToast('Antigravity App started.', 'success', 3500);
      }
    } catch (err) {
      const reason = (err && err.message) ? err.message : 'Could not launch Antigravity App. Please configure its path in AI Dev Tools.';
      showToast(reason, 'error', 5000);
      appendLog('Antigravity App launch failed: ' + reason);
    } finally {
      if (btn) btn.disabled = false;
    }
  }

  let isProfileEmailVisible = false;

  function getActiveProfileEmail() {
    const active = (state.antigravity && state.antigravity.activeProfile) || 'default';
    const profiles = (state.antigravity && state.antigravity.profiles) || [];
    const profObj = profiles.find(p => (typeof p === 'object' ? p.name : p).toLowerCase() === active.toLowerCase());
    if (profObj && typeof profObj === 'object') {
      return profObj.email || profObj.Email || profObj.accountEmail || profObj.AccountEmail || '';
    }
    return '';
  }

  async function updateProfileEmailUi() {
    const emailText = document.getElementById('dash-active-profile-email-text');
    const eyeOff = document.querySelector('#btn-toggle-profile-email-dash .icon-eye-off');
    const eyeOn = document.querySelector('#btn-toggle-profile-email-dash .icon-eye-on');
    const btn = document.getElementById('btn-toggle-profile-email-dash');
    if (!emailText) return;

    let email = getActiveProfileEmail();
    if (!email) {
      try {
        const res = await callBridge('get_active_profile_email');
        if (res && res.email) {
          email = res.email;
          const active = (state.antigravity && state.antigravity.activeProfile) || 'default';
          const profiles = (state.antigravity && state.antigravity.profiles) || [];
          const profObj = profiles.find(p => (typeof p === 'object' ? p.name : p).toLowerCase() === active.toLowerCase());
          if (profObj && typeof profObj === 'object') {
            profObj.email = email;
          }
        }
      } catch {}
    }

    if (!email) {
      emailText.textContent = 'No account email';
      emailText.style.color = 'var(--text-dim)';
      if (btn) btn.style.display = 'none';
      return;
    }

    if (btn) {
      btn.style.display = 'inline-flex';
      btn.title = isProfileEmailVisible ? 'Hide account email' : 'Show account email';
    }
    emailText.style.color = 'var(--text-main)';
    if (isProfileEmailVisible) {
      emailText.textContent = email;
      if (eyeOff) eyeOff.style.display = 'none';
      if (eyeOn) eyeOn.style.display = '';
    } else {
      emailText.textContent = '\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022';
      if (eyeOff) eyeOff.style.display = '';
      if (eyeOn) eyeOn.style.display = 'none';
    }
  }

  function updateAntigravityAppCardUi() {
    const dot = document.getElementById('dash-dot-antigravity-app');
    const note = document.getElementById('dash-antigravity-app-note');
    const btn = document.getElementById('btn-open-antigravity-app-dash');
    if (dot || btn) {
      const devTools = (state.aiConfig && state.aiConfig.devTools) || [];
      const tool = devTools.find(t => t.id === 'antigravity_app' || (t.name && t.name.toLowerCase() === 'antigravity app'));
      const target = (tool && tool.target) || (state.aiConfig && state.aiConfig.toolTargets && (state.aiConfig.toolTargets['Antigravity App'] || state.aiConfig.toolTargets['antigravity_app'])) || '';
      const hasPath = !!(target && target.trim());

      if (dot) {
        dot.className = 'dot ' + (hasPath ? 'online' : '');
      }
      if (note) {
        note.textContent = hasPath ? `(profile: ${state.antigravity.activeProfile || 'default'})` : '(no path set in AI Dev Tools)';
      }
      if (btn) {
        btn.title = hasPath
          ? `Open Antigravity App (uses profile: ${state.antigravity.activeProfile || 'default'})`
          : 'Open AI Dev Tools to set the executable path for Antigravity App';
      }
    }

    updateProfileEmailUi();
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

    updateAntigravityAppCardUi();
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
          const res = await callBridge('start_antigravity_instance', { profile: name });
          if (res && res.profiles) {
            state.antigravity.profiles = res.profiles;
            renderProfiles();
            renderQuickProfileSelect();
            updateHeaderStatus();
          }
        } catch (e) {
          console.error('Start instance failed:', e);
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
          const res = await callBridge('stop_antigravity_instance', { profile: name });
          if (res && res.profiles) {
            state.antigravity.profiles = res.profiles;
            renderProfiles();
            renderQuickProfileSelect();
            updateHeaderStatus();
          }
        } catch (e) {
          console.error('Stop instance failed:', e);
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
          const res = await callBridge('switch_antigravity_profile', { profile: name });
          if (res) {
            if (res.activeProfile !== undefined) state.antigravity.activeProfile = res.activeProfile;
            if (res.profiles) state.antigravity.profiles = res.profiles;
            renderProfiles();
            renderQuickProfileSelect();
            updateProfileEmailUi();
            updateHeaderStatus();
            updateAntigravityAppCardUi();
          }
        } catch (e) {
          console.error('Switch profile failed:', e);
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
        const res = await callBridge('switch_antigravity_profile', { profile: chosen });
        state.antigravity.activeProfile = chosen;
        if (res && res.profiles) {
          state.antigravity.profiles = res.profiles;
        }
        if (summary) summary.textContent = `Antigravity Profile: ${chosen}`;
        if (sel && sel !== dropdown) sel.value = chosen;
        if (cardSel && cardSel !== dropdown) cardSel.value = chosen;
        renderProfiles();
        renderQuickProfileSelect();
        updateAntigravityAppCardUi();
        updateProfileEmailUi();
        updateHeaderStatus();
      };
    });

    updateAntigravityAppCardUi();
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

window.updateHeaderStatus = updateHeaderStatus;
window.updateDashboardCliRows = updateDashboardCliRows;
window.updateAntigravityAppCardUi = updateAntigravityAppCardUi;
window.updateProfileEmailUi = updateProfileEmailUi;

window.renderProfiles = renderProfiles;
var renderProfiles = window.renderProfiles;
window.renderQuickProfileSelect = renderQuickProfileSelect;
var renderQuickProfileSelect = window.renderQuickProfileSelect;
window.currentThemeParam = currentThemeParam;
var currentThemeParam = window.currentThemeParam;
