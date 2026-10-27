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

  let draggedShortcutId = null;

  function updateShortcutsLockUi() {
    const btnLock = document.getElementById('btn-toggle-lock-shortcuts');
    const iconLock = document.getElementById('icon-lock-shortcuts');
    if (!btnLock || !iconLock) return;

    if (state.isShortcutsLocked) {
      btnLock.classList.remove('unlocked');
      btnLock.title = 'Unlock to rearrange shortcuts (Currently Locked)';
      iconLock.innerHTML = '<rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>';
    } else {
      btnLock.classList.add('unlocked');
      btnLock.title = 'Lock shortcuts in place (Currently Unlocked)';
      iconLock.innerHTML = '<rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 9.9-1"/>';
    }
  }

  function renderDashboardShortcuts() {
    const grid = document.getElementById('dashboard-shortcuts-grid');
    if (!grid) return;
    grid.innerHTML = '';

    updateShortcutsLockUi();

    const rawShortcuts = state.aiConfig?.shortcuts || [];
    if (rawShortcuts.length === 0) {
      grid.innerHTML = '<div style="color: var(--text-dim); font-size: 12px; grid-column: 1 / -1; padding: 10px 0;">No shortcuts configured yet. Click "+ Add Shortcut" above to add tools, CLIs, or applications.</div>';
      return;
    }

    // Normalize and save back into state
    state.aiConfig.shortcuts = rawShortcuts.map((s, idx) => normalizeShortcut(s, idx));
    const shortcuts = state.aiConfig.shortcuts;

    shortcuts.forEach((sc, index) => {
      const card = document.createElement('div');
      card.className = 'shortcut-card' + (state.isShortcutsLocked ? '' : ' reorder-unlocked');
      card.setAttribute('data-id', sc.id);
      card.setAttribute('data-index', index);
      card.draggable = !state.isShortcutsLocked;

      const targetDisplay = state.isShortcutsMasked ? '**' : (sc.target || 'No target configured');
      const targetTooltip = state.isShortcutsMasked ? 'Location hidden (**)' : (sc.target || 'No target configured');

      const dragHandleHtml = !state.isShortcutsLocked
        ? '<span class="shortcut-drag-handle" title="Drag to reorder" style="margin-right: 6px; cursor: grab; font-size: 14px; user-select: none;">&#8942;&#8942;</span>'
        : '';

      card.innerHTML = `
        <div class="shortcut-top">
          <div class="shortcut-info" style="display: flex; align-items: flex-start; gap: 4px;">
            ${dragHandleHtml}
            <div>
              <div class="shortcut-name">${escapeHtml(sc.name)}</div>
              <span class="shortcut-cat">${escapeHtml(sc.category || 'AI Dev Tool')}</span>
            </div>
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

      if (!state.isShortcutsLocked) {
        card.addEventListener('dragstart', (e) => {
          if (e.target.closest('button') || e.target.closest('input')) {
            e.preventDefault();
            return;
          }
          draggedShortcutId = sc.id;
          e.dataTransfer.effectAllowed = 'move';
          e.dataTransfer.setData('text/plain', sc.id);
          setTimeout(() => {
            if (draggedShortcutId === sc.id) {
              card.classList.add('dragging');
            }
          }, 0);
        });

        card.addEventListener('dragend', () => {
          draggedShortcutId = null;
          grid.querySelectorAll('.shortcut-card').forEach(c => {
            c.classList.remove('dragging');
            c.classList.remove('drag-over');
          });
        });

        card.addEventListener('dragover', (e) => {
          if (!draggedShortcutId || draggedShortcutId === sc.id) return;
          e.preventDefault();
          e.dataTransfer.dropEffect = 'move';
          card.classList.add('drag-over');
        });

        card.addEventListener('dragleave', (e) => {
          if (!card.contains(e.relatedTarget)) {
            card.classList.remove('drag-over');
          }
        });

        card.addEventListener('drop', async (e) => {
          e.preventDefault();
          card.classList.remove('drag-over');
          if (!draggedShortcutId || draggedShortcutId === sc.id) return;

          const fromId = draggedShortcutId;
          const toId = sc.id;
          draggedShortcutId = null;

          const fromIdx = state.aiConfig.shortcuts.findIndex(s => s.id === fromId);
          const toIdx = state.aiConfig.shortcuts.findIndex(s => s.id === toId);

          if (fromIdx !== -1 && toIdx !== -1 && fromIdx !== toIdx) {
            const [movedItem] = state.aiConfig.shortcuts.splice(fromIdx, 1);
            state.aiConfig.shortcuts.splice(toIdx, 0, movedItem);
            renderDashboardShortcuts();
            await callBridge('save_shortcuts', { shortcuts: state.aiConfig.shortcuts });
          }
        });
      }

      grid.appendChild(card);
    });

    grid.ondragover = (e) => {
      if (draggedShortcutId) {
        e.preventDefault();
      }
    };

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

    document.getElementById('btn-toggle-lock-shortcuts')?.addEventListener('click', () => {
      state.isShortcutsLocked = !state.isShortcutsLocked;
      localStorage.setItem('agents_hub_shortcuts_locked', state.isShortcutsLocked);
      updateShortcutsLockUi();
      renderDashboardShortcuts();
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

window.setupShortcutEditor = setupShortcutEditor;
window.renderDashboardShortcuts = renderDashboardShortcuts;

window.populatePresetDropdown = populatePresetDropdown;
var populatePresetDropdown = window.populatePresetDropdown;
