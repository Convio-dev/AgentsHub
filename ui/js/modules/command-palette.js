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
    add('Scan Git repositories', 'Discover and update git repositories across scan roots', () => {
      switchView('repositories');
      document.getElementById('btn-scan-repos')?.click();
    });
    add('Review Git changes & commit', 'Inspect working tree modifications and stage/commit', () => {
      switchView('repositories');
      document.getElementById('btn-open-git-changes-modal')?.click();
    });
    add('Export repository inventory', 'Copy repository status markdown to clipboard', () => {
      switchView('repositories');
      document.getElementById('btn-export-inventory')?.click();
    });
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
      const pName = typeof p === 'object' && p ? (p.name || '') : String(p || '');
      if (!pName) return;
      const email = typeof p === 'object' && p && p.email ? p.email : '';
      out.push({
        group: 'Switch profile',
        label: 'Switch to Antigravity profile ' + pName,
        hint: email ? `Account: ${email}` : 'The active account and its tab',
        run: async () => {
          try {
            await callBridge('switch_antigravity_profile', { profile: pName, profileName: pName });
            loadInitialState();
          } catch (err) {
            showToast('Could not switch to ' + pName + '.', 'error', 5000);
          }
        },
        search: ('profile ' + pName + ' ' + email + ' antigravity account switch').toLowerCase()
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

window.setupCommandPalette = setupCommandPalette;
