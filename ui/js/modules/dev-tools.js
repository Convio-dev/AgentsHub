  // AI Dev Tools View (Dynamic Tools Manager)
  let devtoolExpandedId = null;

  function isKnownIde(name, target) {
    if (!name && !target) return false;
    const n = (name || '').toLowerCase();
    const t = (target || '').toLowerCase();
    const exe = t.split(/[\/\\]/).pop() || '';

    const ideExes = [
      'code.exe', 'cursor.exe', 'antigravity ide.exe', 'devin.exe', 'workbuddy.exe',
      'code - insiders.exe', 'vscodium.exe', 'codium.exe', 'zed.exe', 'trae.exe',
      'positron.exe', 'void.exe', 'pearai.exe', 'devenv.exe', 'rider64.exe', 'rider.exe',
      'idea64.exe', 'idea.exe', 'pycharm64.exe', 'pycharm.exe', 'webstorm64.exe', 'webstorm.exe',
      'rustrover64.exe', 'rustrover.exe', 'fleet.exe', 'clion64.exe', 'clion.exe',
      'goland64.exe', 'goland.exe', 'sublime_text.exe', 'notepad++.exe', 'eclipse.exe'
    ];
    if (ideExes.includes(exe)) return true;

    const ideKeywords = [
      'ide', 'code', 'cursor', 'devin', 'workbuddy', 'studio', 'rider', 'pycharm',
      'intellij', 'webstorm', 'sublime', 'editor', 'vscodium', 'rustrover', 'fleet', 'clion', 'goland'
    ];
    return ideKeywords.some(k => n.includes(k));
  }

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
      const isIde = (typeof t.isIde === 'boolean') ? t.isIde : isKnownIde(t.name, t.target);
      const ideBadge = isIde ? `<span class="devtool-ide-badge" title="Flagged as IDE / Code Editor (synced to repository Open in Editor)">IDE</span>` : '';

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
            <div class="devtool-name">${escapeHtml(t.name)}${ideBadge}</div>
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
          <div class="devtool-ide-toggle-wrap">
            <input type="checkbox" class="chk-devtool-is-ide" data-id="${t.id}" id="chk-ide-${t.id}" ${isIde ? 'checked' : ''}>
            <label for="chk-ide-${t.id}">Flag as IDE / Code Editor (show in External Code Editor)</label>
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

    // Toggle Flag as IDE
    container.querySelectorAll('.chk-devtool-is-ide').forEach(chk => {
      chk.addEventListener('change', async () => {
        const tool = (state.aiConfig.devTools || []).find(t => t.id === chk.dataset.id);
        if (!tool) return;
        tool.isIde = chk.checked;
        await callBridge('save_ai_config', { config: state.aiConfig });
        renderDevToolsList();
      });
    });

    updateAntigravityAppCardUi();
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
    headText.textContent = found.length + (found.length === 1 ? ' tool/client found' : ' tools/clients found');

    const actionsWrap = document.createElement('div');
    actionsWrap.style.cssText = 'display: flex; gap: 6px; align-items: center;';

    if (found.length > 1) {
      const addAll = document.createElement('button');
      addAll.type = 'button';
      addAll.className = 'btn btn-primary';
      addAll.style.cssText = 'padding: 3px 10px; font-size: 11px;';
      addAll.textContent = 'Add All (' + found.length + ')';
      addAll.addEventListener('click', async () => {
        addAll.disabled = true;
        addAll.textContent = 'Adding all...';
        for (const f of found) {
          await addDiscoveredClient(f, false);
        }
        try {
          await callBridge('save_ai_config', { config: state.aiConfig });
        } catch { }
        await loadDevTools();
        container.style.display = 'none';
        showToast('All ' + found.length + ' discovered tools added.', 'success', 3500);
      });
      actionsWrap.appendChild(addAll);
    }

    const dismiss = document.createElement('button');
    dismiss.type = 'button';
    dismiss.className = 'btn btn-outline';
    dismiss.style.cssText = 'padding: 3px 10px; font-size: 11px;';
    dismiss.textContent = 'Dismiss';
    dismiss.addEventListener('click', () => { container.style.display = 'none'; });
    actionsWrap.appendChild(dismiss);

    head.appendChild(headText);
    head.appendChild(actionsWrap);
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
        add.textContent = 'Adding...';
        await addDiscoveredClient(f, true);
        add.textContent = 'Added \u2713';
        add.classList.remove('btn-primary');
        add.classList.add('btn-outline');
      });

      row.appendChild(name);
      row.appendChild(path);
      row.appendChild(add);
      container.appendChild(row);
    });
  }

  async function addDiscoveredClient(found, shouldReload = true) {
    if (!state.aiConfig) state.aiConfig = {};

    const id = found.name.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
    state.aiConfig.devTools = state.aiConfig.devTools || [];

    const isIde = (typeof found.isIde === 'boolean') ? found.isIde : isKnownIde(found.name, found.target);

    const existing = state.aiConfig.devTools.find(d =>
      d.id === id ||
      (d.name && d.name.toLowerCase() === found.name.toLowerCase()) ||
      (d.target && d.target.toLowerCase() === found.target.toLowerCase())
    );

    if (existing) {
      existing.target = found.target;
      existing.name = existing.name || found.name;
      existing.isEnabled = true;
      if (typeof existing.isIde !== 'boolean') {
        existing.isIde = isIde;
      }
      if (shouldReload) showToast(found.name + ' path updated.', 'success', 3500);
    } else {
      state.aiConfig.devTools.push({
        id,
        name: found.name,
        description: '',
        target: found.target,
        arguments: '',
        workingDirectory: '',
        isEnabled: true,
        isIde
      });
      if (shouldReload) showToast(found.name + ' added to AI Dev Tools.', 'success', 3500);
    }

    if (shouldReload) {
      try {
        await callBridge('save_ai_config', { config: state.aiConfig });
      } catch { }

      await loadDevTools();
    }
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
    const isIdeInp = document.getElementById('modal-devtool-is-ide');

    if (isIdeInp) delete isIdeInp.dataset.userModified;

    if (tool) {
      if (title) title.textContent = 'Edit AI Dev Tool';
      if (idInp) idInp.value = tool.id || '';
      if (nameInp) nameInp.value = tool.name || '';
      if (descInp) descInp.value = tool.description || '';
      if (targetInp) targetInp.value = tool.target || '';
      if (argsInp) argsInp.value = tool.arguments || '';
      if (workdirInp) workdirInp.value = tool.workingDirectory || '';
      if (enabledInp) enabledInp.checked = tool.isEnabled !== false;
      if (isIdeInp) isIdeInp.checked = (typeof tool.isIde === 'boolean') ? tool.isIde : isKnownIde(tool.name, tool.target);
    } else {
      if (title) title.textContent = 'Add AI Dev Tool';
      if (idInp) idInp.value = '';
      if (nameInp) nameInp.value = '';
      if (descInp) descInp.value = '';
      if (targetInp) targetInp.value = '';
      if (argsInp) argsInp.value = '';
      if (workdirInp) workdirInp.value = '';
      if (enabledInp) enabledInp.checked = true;
      if (isIdeInp) isIdeInp.checked = false;
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

    const modalNameInp = document.getElementById('modal-devtool-name');
    const modalTargetInp = document.getElementById('modal-devtool-target');
    const modalIsIdeInp = document.getElementById('modal-devtool-is-ide');

    function checkModalIdeHint() {
      if (modalIsIdeInp && !modalIsIdeInp.dataset.userModified) {
        if (isKnownIde(modalNameInp?.value, modalTargetInp?.value)) {
          modalIsIdeInp.checked = true;
        }
      }
    }

    modalNameInp?.addEventListener('input', checkModalIdeHint);
    modalTargetInp?.addEventListener('input', checkModalIdeHint);
    modalIsIdeInp?.addEventListener('change', () => {
      modalIsIdeInp.dataset.userModified = 'true';
    });

    document.getElementById('btn-browse-modal-devtool')?.addEventListener('click', async () => {
      const nameInp = document.getElementById('modal-devtool-name');
      const toolName = nameInp ? nameInp.value.trim() : '';
      try {
        const res = await callBridge('browse_tool_target', { toolName });
        if (res && res.path) {
          const targetInp = document.getElementById('modal-devtool-target');
          if (targetInp) {
            targetInp.value = res.path;
            checkModalIdeHint();
          }
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
      const isIdeInp = document.getElementById('modal-devtool-is-ide');
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
      const isIde = isIdeInp ? isIdeInp.checked : false;

      if (!state.aiConfig) state.aiConfig = {};
      if (!Array.isArray(state.aiConfig.devTools)) state.aiConfig.devTools = [];
      if (!state.aiConfig.toolTargets) state.aiConfig.toolTargets = {};

      const existingIndex = state.aiConfig.devTools.findIndex(t => t.id === id);
      if (existingIndex >= 0) {
        state.aiConfig.devTools[existingIndex] = { id, name, description: desc, target, arguments: argumentsText, workingDirectory, isEnabled, isIde };
      } else {
        state.aiConfig.devTools.push({ id, name, description: desc, target, arguments: argumentsText, workingDirectory, isEnabled, isIde });
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
window.setupDevToolsEditor = setupDevToolsEditor;
window.loadDevTools = loadDevTools;
window.findInstalledClients = findInstalledClients;
window.renderDevToolsList = renderDevToolsList;
var renderDevToolsList = window.renderDevToolsList;

window.openDevToolModal = openDevToolModal;
var openDevToolModal = window.openDevToolModal;
