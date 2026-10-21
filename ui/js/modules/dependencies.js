  function dependencyBadge(item, busy) {
    if (busy === 'running') return { cls: 'dep-badge-busy', text: 'Installing' };
    if (busy === 'done') return { cls: 'dep-badge-ok', text: 'Done' };
    if (busy === 'failed') return { cls: 'dep-badge-failed', text: 'Failed' };
    if (item.isInstalled) return { cls: 'dep-badge-ok', text: 'Installed' };
    return { cls: 'dep-badge-missing', text: 'Not Installed' };
  }

  function dependencyDetailText(item, busy) {
    if (busy === 'running') return 'Installing in the background - output streams below.';
    if (busy === 'failed') return 'Last install attempt failed. Check the output below.';

    if (item.isInstalled) {
      const parts = [];
      if (item.version) parts.push(item.version);
      if (item.path) parts.push(item.path);
      return parts.length ? parts.join('  -  ') : 'Detected on PATH';
    }

    return `Command: ${item.commandPreview || 'not available'}`;
  }

  function dependencyActionLabel(item, busy) {
    if (busy === 'running') return 'Installing...';

    if (item.installKind === 'browser') return item.isInstalled ? 'Verify' : 'Open Download Page';
    if (item.isInstalled) return item.installKind === 'winget' ? 'Update via Winget' : 'Reinstall';
    return item.installKind === 'winget' ? 'Install via Winget' : 'Run PowerShell Installer';
  }

  function buildDependencyRow(item) {
    const busy = dependencyState.busy[item.id];
    const badge = dependencyBadge(item, busy);

    const row = document.createElement('div');
    row.className = 'dependency-row';

    const icon = document.createElement('div');
    icon.className = 'dependency-icon';
    icon.innerHTML = `<svg class="icon-svg" style="width: 17px; height: 17px;" viewBox="0 0 24 24">${DEPENDENCY_ICONS[item.id] || DEPENDENCY_ICONS.hermes}</svg>`;

    const info = document.createElement('div');
    info.className = 'dependency-info';

    const titleLine = document.createElement('div');
    titleLine.className = 'dependency-title-line';

    const name = document.createElement('span');
    name.className = 'dependency-name';
    name.textContent = item.name;

    const badgeEl = document.createElement('span');
    badgeEl.className = 'dep-badge ' + badge.cls;
    badgeEl.textContent = badge.text;

    titleLine.appendChild(name);
    titleLine.appendChild(badgeEl);

    const detail = document.createElement('div');
    detail.className = 'dependency-detail';
    detail.textContent = dependencyDetailText(item, busy);

    info.appendChild(titleLine);
    info.appendChild(detail);

    const logLines = dependencyState.logs[item.id];
    if (logLines && logLines.length > 0) {
      const logEl = document.createElement('div');
      logEl.className = 'dependency-log';
      logEl.textContent = logLines.join('\n');
      info.appendChild(logEl);
    }

    const action = document.createElement('div');
    action.className = 'dependency-action';

    const button = document.createElement('button');
    button.className = 'btn ' + (item.isInstalled ? 'btn-outline' : 'btn-success');
    button.style.padding = '5px 12px';
    button.style.fontSize = '11.5px';
    button.style.whiteSpace = 'nowrap';
    button.textContent = dependencyActionLabel(item, busy);
    button.disabled = busy === 'running' || dependencyState.loading;
    button.addEventListener('click', () => handleDependencyAction(item));

    action.appendChild(button);

    row.appendChild(icon);
    row.appendChild(info);
    row.appendChild(action);
    return row;
  }

  // Install-all state. Kept outside dependencyState.busy so a batch in flight disables the
  // batch button even though nothing is busy on any single row yet.
  let dependencyInstallAllRunning = false;
  let dependencyInstallAllProgress = '';

  function updateInstallAllButton() {
    const btn = document.getElementById('btn-install-all-dependencies');
    if (!btn) return;

    const label = btn.querySelector('span');
    const missing = dependencyState.items.filter(i => !i.isInstalled);

    if (dependencyInstallAllRunning) {
      btn.disabled = true;
      if (label) label.textContent = dependencyInstallAllProgress || 'Installing...';
      return;
    }

    btn.disabled = missing.length === 0;
    if (label) {
      label.textContent = missing.length === 0
        ? 'Nothing missing'
        : 'Install all missing (' + missing.length + ')';
    }
  }

  function renderDependencies() {
    const runtimeContainer = document.getElementById('runtimes-list-container');
    const agentContainer = document.getElementById('agents-list-container');
    if (!runtimeContainer || !agentContainer) return;

    updateInstallAllButton();

    runtimeContainer.innerHTML = '';
    agentContainer.innerHTML = '';

    if (dependencyState.items.length === 0) {
      const placeholder = document.createElement('div');
      placeholder.className = 'dependency-empty';
      placeholder.textContent = dependencyState.loading
        ? 'Scanning for installed tools...'
        : 'No dependency data yet. Click Recheck All.';
      runtimeContainer.appendChild(placeholder);
      return;
    }

    // Runtimes go in section 1; agents and network utilities share section 2.
    dependencyState.items.forEach(item => {
      const row = buildDependencyRow(item);
      if (item.category === 'runtime') runtimeContainer.appendChild(row);
      else agentContainer.appendChild(row);
    });
  }

  async function loadDependencies(announceFailure) {
    if (dependencyState.loading) return;

    dependencyState.loading = true;
    renderDependencies();

    try {
      const res = await callBridge('get_dependencies_status');
      dependencyState.items = (res && res.dependencies) || [];
      dependencyState.loaded = true;
    } catch (err) {
      if (announceFailure) {
        showToast('Dependency scan failed: ' + err.message, 'error', 4500);
      } else {
        console.warn('Dependency scan failed:', err);
      }
    } finally {
      dependencyState.loading = false;
      renderDependencies();
    }
  }

  async function installAllDependencies() {
    if (dependencyInstallAllRunning) return;

    const missing = dependencyState.items.filter(i => !i.isInstalled);
    if (missing.length === 0) {
      showToast('Every detected tool is already installed.', 'info', 3500);
      return;
    }

    dependencyInstallAllRunning = true;
    dependencyInstallAllProgress = '';
    updateInstallAllButton();

    try {
      const res = await callBridge('install_all_dependencies');
      if (res && res.started === false) {
        showToast('Every detected tool is already installed.', 'info', 3500);
        dependencyInstallAllRunning = false;
        updateInstallAllButton();
        return;
      }

      const total = (res && res.total) || missing.length;
      showToast('Installing ' + total + ' item(s). This takes a few minutes and streams its output below.', 'info', 6000);
    } catch (err) {
      showToast('The batch install could not start: ' + (err.message || err), 'error', 6000);
      dependencyInstallAllRunning = false;
      updateInstallAllButton();
    }
  }

  async function handleDependencyAction(item) {
    if (dependencyState.busy[item.id] === 'running') return;

    // An already-installed browser-only tool has nothing to install; re-verify instead.
    if (item.installKind === 'browser' && item.isInstalled) {
      await loadDependencies(true);
      return;
    }

    dependencyState.busy[item.id] = 'running';
    dependencyState.logs[item.id] = [];
    renderDependencies();

    try {
      await callBridge('run_dependency_install', { id: item.id });
      showToast(item.installKind === 'browser'
        ? `Opening the download page for ${item.name}...`
        : `Starting ${item.name} installer...`, 'info', 3000);
    } catch (err) {
      dependencyState.busy[item.id] = 'failed';
      renderDependencies();
      showToast(`Could not start installer: ${err.message}`, 'error', 4500);
    }
  }

  function setupDependencies() {
    document.getElementById('btn-recheck-dependencies')?.addEventListener('click', () => loadDependencies(true));
    document.getElementById('btn-install-all-dependencies')?.addEventListener('click', () => installAllDependencies());
  }

  // Keep the About card's version in step with the version the backend reports, so it is not
  // a second hardcoded copy of the number.
  // Every version shown in the interface comes from the backend, which reads it off the
  // assembly. Nothing here is allowed to carry its own copy.
window.setupDependencies = setupDependencies;
window.loadDependencies = loadDependencies;
window.installAllDependencies = installAllDependencies;

window.updateInstallAllButton = updateInstallAllButton;
var updateInstallAllButton = window.updateInstallAllButton;
window.renderDependencies = renderDependencies;
var renderDependencies = window.renderDependencies;
