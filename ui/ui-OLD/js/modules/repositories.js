// Repositories & Git Subsystem Module
(function() {
  const repoState = {
    repositories: [],
    selectedRepoId: null,
    selectedRepoIds: new Set(),
    workingProjectId: null,
    activeFilter: 'all',
    searchQuery: '',
    scanSettings: { roots: [], depth: 4, skipDirs: [], defaultAgent: 'claude', preferredEditor: 'ask', ignoredHealthRules: [] },
    isScanning: false,
    scanProgress: null,
    currentChanges: [],
    selectedChangeFile: null,
    checkedChangeFiles: new Set(),
    activeDetailTab: 'overview',
    notesDebounceTimer: null,
    commitGraph: [],
    selectedCommitHash: null,
    worktrees: [],
    stashes: [],
    selectedStashIndex: null,
    pullRequests: [],
    prFilterState: 'open',
    isLoadingPrs: false,
    commitAction: localStorage.getItem('repo_commit_action') || 'local',
    gitHubAuth: null,
    memories: [],
    memoryQuery: '',
    memoryDebounceTimer: null,
    repoBranches: [],
    branchFilter: '',
    isBranchPopoverOpen: false
  };

  async function initRepositoriesModule() {
    setupRepoEventListeners();
    setupRepoBridgeEvents();
    setCommitActionPreference(repoState.commitAction);
    loadGitHubAuthStatus();
    await loadRepoManagerState();
  }

  async function loadRepoManagerState() {
    try {
      const res = await callBridge('get_repo_manager_state');
      if (res) {
        repoState.repositories = res.repositories || [];
        repoState.scanSettings = res.settings || repoState.scanSettings;
        repoState.workingProjectId = res.workingProjectId;
        repoState.isScanning = !!res.isScanning;
        repoState.scanProgress = res.lastProgress;

        renderRepoMetrics();
        renderWorkingProjectBanner();
        renderRepositoriesList();

        if (!repoState.selectedRepoId && repoState.repositories.length > 0) {
          selectRepository(repoState.workingProjectId || repoState.repositories[0].id);
        } else if (repoState.selectedRepoId) {
          selectRepository(repoState.selectedRepoId);
        }

        updateScanProgressIndicator();
      }
    } catch (err) {
      console.warn('[Repositories] Failed to load state:', err);
    }
  }

  function setupRepoBridgeEvents() {
    window.handleRepoScanProgress = (p) => {
      if (!p) return;
      repoState.scanProgress = p;
      repoState.isScanning = !p.isCompleted;
      updateScanProgressIndicator();
    };

    window.handleReposUpdated = (data) => {
      if (data && data.repositories) {
        repoState.repositories = data.repositories;
        renderRepoMetrics();
        renderWorkingProjectBanner();
        renderRepositoriesList();
        if (repoState.selectedRepoId) {
          selectRepository(repoState.selectedRepoId);
        }
      }
    };

    window.addEventListener('repo_scan_progress', (e) => {
      window.handleRepoScanProgress(e.detail);
    });

    window.addEventListener('repos_updated', (e) => {
      window.handleReposUpdated(e.detail);
    });

    window.addEventListener('session_memory_updated', () => {
      const repo = getSelectedRepo();
      if (repo && repoState.activeDetailTab === 'memory') {
        loadWorkspaceMemories(repo.path);
      }
    });
  }

  function updateScanProgressIndicator() {
    const chip = document.getElementById('scan-progress-chip');
    const text = document.getElementById('scan-progress-text');
    const btnScan = document.getElementById('btn-scan-repos');

    if (!chip || !btnScan) return;

    if (repoState.isScanning) {
      chip.style.display = 'inline-flex';
      const p = repoState.scanProgress;
      text.textContent = p ? `${p.phase} (${p.reposFound} repos)` : 'Scanning...';
      btnScan.disabled = true;
    } else {
      chip.style.display = 'none';
      btnScan.disabled = false;
    }
  }

  function setupRepoEventListeners() {
    // Toolbar buttons
    document.getElementById('btn-scan-repos')?.addEventListener('click', triggerRepoScan);
    document.getElementById('btn-cancel-scan')?.addEventListener('click', cancelRepoScan);
    document.getElementById('btn-repo-settings')?.addEventListener('click', openRepoSettingsModal);
    document.getElementById('btn-repo-issues')?.addEventListener('click', openRepoIssuesModal);
    document.getElementById('btn-export-inventory')?.addEventListener('click', exportRepoInventory);

    // Filter input
    const searchInput = document.getElementById('repo-search-input');
    searchInput?.addEventListener('input', (e) => {
      repoState.searchQuery = e.target.value.trim().toLowerCase();
      renderRepositoriesList();
    });

    // Filter chips
    document.querySelectorAll('.repo-filter-chip').forEach(chip => {
      chip.addEventListener('click', () => {
        document.querySelectorAll('.repo-filter-chip').forEach(c => c.classList.remove('active'));
        chip.classList.add('active');
        repoState.activeFilter = chip.dataset.filter;
        renderRepositoriesList();
      });
    });

    // Detail Tabs
    const tabsBar = document.querySelector('.detail-tabs-bar');
    if (tabsBar) {
      tabsBar.addEventListener('wheel', (e) => {
        if (e.deltaY !== 0) {
          e.preventDefault();
          tabsBar.scrollLeft += e.deltaY;
        }
      }, { passive: false });
    }

    document.querySelectorAll('.detail-tab-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        switchDetailTab(btn.dataset.tab);
      });
    });

    // Detail Action buttons
    document.getElementById('btn-set-focus-project')?.addEventListener('click', toggleWorkingProjectFocus);
    document.getElementById('btn-copy-repo-path')?.addEventListener('click', copySelectedRepoPath);
    document.getElementById('detail-status-select')?.addEventListener('change', updateSelectedRepoStatus);
    document.getElementById('btn-quick-run')?.addEventListener('click', executeQuickRun);

    // Overview buttons
    document.getElementById('btn-pane-open-vscode')?.addEventListener('click', () => openInExternal('editor'));
    document.getElementById('btn-pane-editor-picker')?.addEventListener('click', () => {
      const repo = getSelectedRepo();
      if (repo) openEditorPickerModal(repo);
    });
    document.getElementById('btn-pane-open-explorer')?.addEventListener('click', () => openInExternal('explorer'));
    document.getElementById('btn-pane-open-terminal')?.addEventListener('click', () => openInExternal('terminal'));
    document.getElementById('btn-pane-launch-agent')?.addEventListener('click', launchAgentInSelectedRepo);

    // Working On Now banner buttons
    document.getElementById('btn-working-changes')?.addEventListener('click', () => {
      if (repoState.workingProjectId) {
        selectRepository(repoState.workingProjectId);
        switchDetailTab('changes');
      }
    });
    document.getElementById('btn-working-vscode')?.addEventListener('click', () => {
      const repo = getSelectedOrWorkingRepo();
      if (repo) openInExternal('editor');
    });
    document.getElementById('btn-working-editor-picker')?.addEventListener('click', () => {
      const repo = getSelectedOrWorkingRepo();
      if (repo) openEditorPickerModal(repo);
    });
    document.getElementById('btn-working-terminal')?.addEventListener('click', () => {
      const repo = getSelectedOrWorkingRepo();
      if (repo) callBridge('open_repo_in_editor', { repoPath: repo.path, target: 'terminal' });
    });
    document.getElementById('btn-working-agent')?.addEventListener('click', () => {
      const repo = getSelectedOrWorkingRepo();
      if (repo) callBridge('launch_agent_in_repo', { repoPath: repo.path, cliType: repoState.scanSettings.defaultAgent || 'claude' });
    });

    // Git Changes action buttons
    document.getElementById('btn-refresh-changes')?.addEventListener('click', () => {
      const repo = getSelectedRepo();
      if (repo) loadRepoChanges(repo.path);
    });
    document.getElementById('btn-toggle-wide-changes')?.addEventListener('click', () => toggleWideChanges());
    document.getElementById('btn-exit-wide-mode-header')?.addEventListener('click', () => toggleWideChanges(false));
    document.getElementById('btn-open-git-changes-modal')?.addEventListener('click', openGitChangesModal);
    document.getElementById('btn-stage-selected')?.addEventListener('click', () => stageCheckedFiles(false));
    document.getElementById('btn-unstage-selected')?.addEventListener('click', () => stageCheckedFiles(true));
    document.getElementById('btn-stage-all')?.addEventListener('click', stageAllFiles);
    document.getElementById('btn-create-commit')?.addEventListener('click', () => createLocalCommit());
    document.getElementById('btn-ai-generate-commit')?.addEventListener('click', generateAiCommitMessage);

    // Dual Split Commit Dropdown Handlers
    const btnCommitChevron = document.getElementById('btn-commit-split-menu');
    const menuCommitAction = document.getElementById('menu-commit-action');
    const btnModalCommitChevron = document.getElementById('btn-modal-commit-split-menu');
    const menuModalCommitAction = document.getElementById('menu-modal-commit-action');

    btnCommitChevron?.addEventListener('click', (e) => {
      e.stopPropagation();
      if (menuCommitAction) {
        menuCommitAction.style.display = menuCommitAction.style.display === 'none' ? 'flex' : 'none';
      }
    });

    btnModalCommitChevron?.addEventListener('click', (e) => {
      e.stopPropagation();
      if (menuModalCommitAction) {
        menuModalCommitAction.style.display = menuModalCommitAction.style.display === 'none' ? 'flex' : 'none';
      }
    });

    document.querySelectorAll('.commit-dropdown-item').forEach(item => {
      item.addEventListener('click', (e) => {
        e.stopPropagation();
        const action = item.dataset.action;
        setCommitActionPreference(action);
      });
    });

    document.addEventListener('click', () => {
      if (menuCommitAction) menuCommitAction.style.display = 'none';
      if (menuModalCommitAction) menuModalCommitAction.style.display = 'none';
    });

    // Git Network action buttons
    document.getElementById('btn-git-fetch')?.addEventListener('click', () => openGitNetworkModal('fetch'));
    document.getElementById('btn-git-pull')?.addEventListener('click', () => openGitNetworkModal('pull'));
    document.getElementById('btn-git-push')?.addEventListener('click', () => openGitNetworkModal('push'));

    // Batch Actions Bar
    document.getElementById('chk-select-all-repos')?.addEventListener('change', (e) => toggleSelectAllRepos(e.target.checked));
    document.getElementById('btn-batch-fetch')?.addEventListener('click', () => runBatchAction('fetch'));
    document.getElementById('btn-batch-pull')?.addEventListener('click', () => runBatchAction('pull_clean'));
    document.getElementById('btn-batch-clear')?.addEventListener('click', clearBatchSelection);

    // Commit Graph action buttons
    document.getElementById('btn-refresh-commit-graph')?.addEventListener('click', () => {
      const repo = getSelectedRepo();
      if (repo) loadCommitGraph(repo.path);
    });
    document.getElementById('btn-close-commit-diff')?.addEventListener('click', () => {
      const diffViewer = document.getElementById('commit-diff-viewer');
      if (diffViewer) diffViewer.style.display = 'none';
      document.querySelectorAll('.commit-row.selected').forEach(r => r.classList.remove('selected'));
      repoState.selectedCommitHash = null;
    });
    document.getElementById('btn-diff-revert')?.addEventListener('click', () => {
      if (repoState.activeDiffCommit) {
        openRevertCommitModal(repoState.activeDiffCommit.hash, repoState.activeDiffCommit.meta);
      } else if (repoState.selectedCommitHash) {
        openRevertCommitModal(repoState.selectedCommitHash, { subject: `Commit ${repoState.selectedCommitHash.substring(0, 7)}` });
      } else {
        showToast('Please select a commit to revert', 'info');
      }
    });

    // Revert Modal events
    document.getElementById('btn-close-revert-modal')?.addEventListener('click', closeRevertCommitModal);
    document.getElementById('btn-cancel-revert-modal')?.addEventListener('click', closeRevertCommitModal);
    document.getElementById('btn-submit-revert-modal')?.addEventListener('click', submitRevertCommit);
    document.querySelectorAll('input[name="revert-strategy"]').forEach(radio => {
      radio.addEventListener('change', (e) => updateRevertStrategyUI(e.target.value));
    });

    // GitHub Pull Requests events
    document.getElementById('btn-refresh-prs')?.addEventListener('click', () => {
      const repo = getSelectedRepo();
      if (repo) loadGitHubPullRequests(repo.path);
    });
    document.querySelectorAll('.btn-pr-filter').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.btn-pr-filter').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        repoState.prFilterState = btn.dataset.prState || 'open';
        const repo = getSelectedRepo();
        if (repo) loadGitHubPullRequests(repo.path);
      });
    });
    document.getElementById('btn-close-pr-merge-modal')?.addEventListener('click', closePrMergeModal);
    document.getElementById('btn-cancel-pr-merge-modal')?.addEventListener('click', closePrMergeModal);
    document.getElementById('btn-submit-pr-merge-modal')?.addEventListener('click', submitPrMergeModal);

    // Worktrees action buttons
    document.getElementById('btn-open-add-worktree')?.addEventListener('click', openAddWorktreeModal);

    // Stashes action buttons
    document.getElementById('btn-open-create-stash')?.addEventListener('click', openCreateStashModal);
    document.getElementById('btn-close-stash-diff')?.addEventListener('click', () => {
      const viewer = document.getElementById('stash-diff-viewer');
      if (viewer) viewer.style.display = 'none';
    });

    // Launchers action buttons
    document.getElementById('btn-add-custom-launcher')?.addEventListener('click', openCustomLauncherModal);

    // Notes auto-save listener
    const notesBox = document.getElementById('repo-notes-box');
    notesBox?.addEventListener('input', () => {
      clearTimeout(repoState.notesDebounceTimer);
      repoState.notesDebounceTimer = setTimeout(saveCurrentRepoNotes, 600);
    });

    // Escape key listener to exit Wide View
    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && isWideChangesActive) {
        const anyModalActive = document.querySelector('.modal-overlay.active');
        if (!anyModalActive) {
          toggleWideChanges(false);
        }
      }
    });

    // Branch switcher popover
    document.getElementById('detail-branch-pill')?.addEventListener('click', (e) => {
      e.stopPropagation();
      toggleBranchSwitcherPopover();
    });
    document.getElementById('input-branch-search')?.addEventListener('input', (e) => {
      filterBranchList(e.target.value);
    });
    document.addEventListener('click', (e) => {
      const popover = document.getElementById('branch-switcher-popover');
      if (popover && !popover.contains(e.target) && e.target.id !== 'detail-branch-pill' && !document.getElementById('detail-branch-pill')?.contains(e.target)) {
        popover.style.display = 'none';
        repoState.isBranchPopoverOpen = false;
      }
    });

    // Merge Preview modal events
    document.getElementById('btn-meta-merge')?.addEventListener('click', openGitMergeModal);
    document.getElementById('btn-history-open-merge')?.addEventListener('click', openGitMergeModal);
    document.getElementById('btn-close-merge-modal')?.addEventListener('click', closeGitMergeModal);
    document.getElementById('btn-cancel-merge-modal')?.addEventListener('click', closeGitMergeModal);
    document.getElementById('btn-execute-merge-modal')?.addEventListener('click', executeGitMerge);
    document.getElementById('select-merge-source-branch')?.addEventListener('change', (e) => {
      updateMergePreview(e.target.value);
    });
    document.getElementById('chk-merge-ff-only')?.addEventListener('change', () => {
      const select = document.getElementById('select-merge-source-branch');
      if (select) updateMergePreview(select.value);
    });

    // Knowledge & Memory events
    document.getElementById('btn-add-memory-note')?.addEventListener('click', openAddMemoryModal);
    document.getElementById('btn-close-add-memory-modal')?.addEventListener('click', closeAddMemoryModal);
    document.getElementById('btn-cancel-add-memory-modal')?.addEventListener('click', closeAddMemoryModal);
    document.getElementById('btn-submit-add-memory-modal')?.addEventListener('click', submitAddMemoryModal);
    document.getElementById('btn-refresh-memories')?.addEventListener('click', () => {
      const repo = getSelectedRepo();
      if (repo) loadWorkspaceMemories(repo.path);
    });
    document.getElementById('btn-clear-memories')?.addEventListener('click', clearAllWorkspaceMemories);
    document.getElementById('btn-inject-to-cli')?.addEventListener('click', copyContextPromptSnippet);
    document.getElementById('memory-search-input')?.addEventListener('input', (e) => {
      clearTimeout(repoState.memoryDebounceTimer);
      repoState.memoryDebounceTimer = setTimeout(() => {
        searchWorkspaceMemories(e.target.value);
      }, 300);
    });

    // Modals setup
    setupRepoModals();
  }

  function renderRepoMetrics() {
    const repos = repoState.repositories;
    const total = repos.length;
    const clean = repos.filter(r => r.isClean).length;
    const dirty = repos.filter(r => !r.isClean).length;
    const ahead = repos.reduce((acc, r) => acc + (r.ahead || 0), 0);

    const elTotal = document.getElementById('metric-repo-total');
    const elClean = document.getElementById('metric-repo-clean');
    const elDirty = document.getElementById('metric-repo-dirty');
    const elAhead = document.getElementById('metric-repo-ahead');

    if (elTotal) elTotal.textContent = String(total);
    if (elClean) elClean.textContent = String(clean);
    if (elDirty) elDirty.textContent = String(dirty);
    if (elAhead) elAhead.textContent = String(ahead);

    const issuesCount = repos.filter(r => r.broken || !r.path).length;
    const elIssues = document.getElementById('lbl-issues-count');
    if (elIssues) elIssues.textContent = `Issues (${issuesCount})`;
  }

  function renderWorkingProjectBanner() {
    const bannerActions = document.getElementById('working-project-actions');
    const titleEl = document.getElementById('working-project-name');
    const pathEl = document.getElementById('working-project-path');

    const working = repoState.repositories.find(r => r.id === repoState.workingProjectId);

    if (working) {
      if (titleEl) titleEl.textContent = working.name;
      if (pathEl) pathEl.textContent = `${working.path} (${working.branch || 'detached'})`;
      if (bannerActions) bannerActions.style.display = 'flex';
    } else {
      if (titleEl) titleEl.textContent = 'No project selected';
      if (pathEl) pathEl.textContent = "Select a repository from your collection and click 'Work on this'.";
      if (bannerActions) bannerActions.style.display = 'none';
    }
  }

  function getFilteredRepositories() {
    let filtered = [...repoState.repositories];

    // Filter by type
    if (repoState.activeFilter === 'working') {
      filtered = filtered.filter(r => r.id === repoState.workingProjectId);
    } else if (repoState.activeFilter === 'pinned') {
      filtered = filtered.filter(r => r.pinned);
    } else if (repoState.activeFilter === 'modified') {
      filtered = filtered.filter(r => !r.isClean);
    } else if (repoState.activeFilter === 'clean') {
      filtered = filtered.filter(r => r.isClean);
    } else if (repoState.activeFilter === 'problems') {
      filtered = filtered.filter(r => r.broken || !r.path);
    }

    // Search query
    if (repoState.searchQuery) {
      filtered = filtered.filter(r =>
        r.name.toLowerCase().includes(repoState.searchQuery) ||
        r.path.toLowerCase().includes(repoState.searchQuery) ||
        (r.branch && r.branch.toLowerCase().includes(repoState.searchQuery)) ||
        (r.focus && r.focus.toLowerCase().includes(repoState.searchQuery))
      );
    }

    // Sorting: Pinned first, then Working on, then Name
    filtered.sort((a, b) => {
      if (a.pinned !== b.pinned) return a.pinned ? -1 : 1;
      if (a.id === repoState.workingProjectId) return -1;
      if (b.id === repoState.workingProjectId) return 1;
      return a.name.localeCompare(b.name);
    });

    return filtered;
  }

  function renderRepositoriesList() {
    const container = document.getElementById('repo-items-container');
    const countHeader = document.getElementById('repo-count-header');
    if (!container) return;

    const filtered = getFilteredRepositories();

    if (countHeader) countHeader.textContent = `Repositories (${filtered.length})`;

    if (filtered.length === 0) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 40px 20px; text-align: center; color: var(--text-dim); font-size: 12px;">
          No matching repositories found.
        </div>`;
      updateBatchActionBar();
      return;
    }

    container.innerHTML = '';
    filtered.forEach(repo => {
      const item = document.createElement('div');
      item.className = `repo-list-item ${repo.id === repoState.selectedRepoId ? 'selected' : ''}`;
      item.dataset.id = repo.id;

      const isClean = repo.isClean;
      const dirtyText = isClean
        ? `<span class="dirty-counter-badge clean">Clean</span>`
        : `<span class="dirty-counter-badge dirty">+${repo.stagedCount || 0} ~${repo.unstagedCount || 0} ?${repo.untrackedCount || 0}</span>`;

      const syncText = (repo.ahead > 0 || repo.behind > 0)
        ? `<span class="sync-badge">↑${repo.ahead || 0} ↓${repo.behind || 0}</span>`
        : '';

      const isPinned = repo.pinned;
      const isWorking = repo.id === repoState.workingProjectId;
      const isBatchSelected = repoState.selectedRepoIds.has(repo.id);

      item.innerHTML = `
        <input type="checkbox" class="repo-card-checkbox" data-id="${repo.id}" ${isBatchSelected ? 'checked' : ''} title="Select for batch actions" />
        <button class="repo-pin-btn ${isPinned ? 'pinned' : ''}" title="${isPinned ? 'Unpin' : 'Pin to top'}">
          <svg class="icon-svg" style="width: 14px; height: 14px;" viewBox="0 0 24 24"><polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/></svg>
        </button>
        <div class="repo-item-main">
          <div class="repo-item-top">
            <span class="repo-name-text" title="${escapeHtml(repo.name)}">${escapeHtml(repo.name)}</span>
            ${isWorking ? `<span style="font-size: 9px; font-weight: 700; color: var(--accent-cyan); text-transform: uppercase;">• Working</span>` : ''}
            <span class="branch-pill">${escapeHtml(repo.branch || 'detached')}</span>
            ${dirtyText}
            ${syncText}
          </div>
          <span class="repo-path-text" title="${escapeHtml(repo.path)}">${escapeHtml(repo.path)}</span>
        </div>
      `;

      item.addEventListener('click', (e) => {
        if (e.target.closest('.repo-card-checkbox')) {
          e.stopPropagation();
          const chk = e.target.closest('.repo-card-checkbox');
          toggleRepoBatchSelection(repo.id, chk.checked);
          return;
        }
        if (e.target.closest('.repo-pin-btn')) {
          e.stopPropagation();
          toggleRepoPinned(repo.id, !repo.pinned);
          return;
        }
        selectRepository(repo.id);
      });

      container.appendChild(item);
    });

    updateBatchActionBar();
  }

  function toggleRepoBatchSelection(repoId, isSelected) {
    if (isSelected) {
      repoState.selectedRepoIds.add(repoId);
    } else {
      repoState.selectedRepoIds.delete(repoId);
    }
    updateBatchActionBar();
  }

  function toggleSelectAllRepos(isChecked) {
    const filtered = getFilteredRepositories();
    if (isChecked) {
      filtered.forEach(r => repoState.selectedRepoIds.add(r.id));
    } else {
      repoState.selectedRepoIds.clear();
    }
    renderRepositoriesList();
  }

  function clearBatchSelection() {
    repoState.selectedRepoIds.clear();
    renderRepositoriesList();
  }

  function updateBatchActionBar() {
    const bar = document.getElementById('batch-action-bar');
    const countEl = document.getElementById('batch-selected-count');
    const chkAll = document.getElementById('chk-select-all-repos');
    const sz = repoState.selectedRepoIds.size;

    if (countEl) countEl.textContent = sz;
    if (bar) bar.style.display = sz > 0 ? 'flex' : 'none';

    if (chkAll) {
      const filtered = getFilteredRepositories();
      const allSelected = filtered.length > 0 && filtered.every(r => repoState.selectedRepoIds.has(r.id));
      const someSelected = sz > 0 && !allSelected;
      chkAll.checked = allSelected;
      chkAll.indeterminate = someSelected;
    }
  }

  async function runBatchAction(actionName) {
    const selectedIds = Array.from(repoState.selectedRepoIds);
    if (selectedIds.length === 0) {
      showToast('No repositories selected', 'warn');
      return;
    }

    const actionLabels = {
      'fetch': 'Fetching remotes for',
      'pull_clean': 'Safely pulling clean repos for',
      'status': 'Checking status for'
    };
    showToast(`${actionLabels[actionName] || 'Processing'} ${selectedIds.length} repositories...`, 'info');

    try {
      const res = await callBridge('batch_repo_action', { batchAction: actionName, repoIds: selectedIds });
      if (res && res.results) {
        const results = res.results;
        const successCount = results.filter(r => r.success).length;
        const skipped = results.filter(r => r.skippedReason);
        const failed = results.filter(r => !r.success && !r.skippedReason);

        let msg = `Batch ${actionName}: ${successCount}/${results.length} succeeded.`;
        if (skipped.length > 0) {
          msg += ` (${skipped.length} skipped - dirty tree)`;
        }
        if (failed.length > 0) {
          msg += ` (${failed.length} failed)`;
        }
        showToast(msg, failed.length > 0 ? 'warn' : 'success', 6000);
        await loadRepoManagerState();
      }
    } catch (err) {
      showToast(`Batch ${actionName} error: ` + (err.message || err), 'error');
    }
  }

  function selectRepository(repoId) {
    repoState.selectedRepoId = repoId;

    // Update active row class
    document.querySelectorAll('.repo-list-item').forEach(el => {
      el.classList.toggle('selected', el.dataset.id === repoId);
    });

    const repo = getSelectedRepo();
    if (!repo) return;

    // Update Detail Header
    const titleEl = document.getElementById('detail-repo-title');
    const branchTextEl = document.getElementById('detail-branch-text');
    const dirtyBadgeEl = document.getElementById('detail-dirty-badge');
    const syncBadgeEl = document.getElementById('detail-sync-badge');
    const statusSelectEl = document.getElementById('detail-status-select');
    const pathEl = document.getElementById('detail-repo-path');
    const btnFocus = document.getElementById('btn-set-focus-project');

    if (titleEl) titleEl.textContent = repo.name;
    if (branchTextEl) branchTextEl.textContent = repo.branch || 'detached';
    if (pathEl) pathEl.textContent = repo.path;

    if (dirtyBadgeEl) {
      if (repo.isClean) {
        dirtyBadgeEl.className = 'dirty-counter-badge clean';
        dirtyBadgeEl.textContent = 'Clean';
      } else {
        dirtyBadgeEl.className = 'dirty-counter-badge dirty';
        dirtyBadgeEl.textContent = `+${repo.stagedCount || 0} ~${repo.unstagedCount || 0} ?${repo.untrackedCount || 0}`;
      }
    }

    if (syncBadgeEl) {
      if (repo.ahead > 0 || repo.behind > 0) {
        syncBadgeEl.style.display = 'inline-flex';
        syncBadgeEl.textContent = `Ahead ${repo.ahead} / Behind ${repo.behind}`;
      } else {
        syncBadgeEl.style.display = 'none';
      }
    }

    if (statusSelectEl) {
      statusSelectEl.value = repo.status || 'active';
    }

    const headerExitBtn = document.getElementById('btn-exit-wide-mode-header');
    if (headerExitBtn) {
      headerExitBtn.style.display = isWideChangesActive ? 'inline-flex' : 'none';
    }

    if (btnFocus) {
      const isWorking = repo.id === repoState.workingProjectId;
      btnFocus.classList.toggle('btn-primary', isWorking);
      btnFocus.classList.toggle('btn-outline', !isWorking);
      btnFocus.innerHTML = `
        <svg class="icon-svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><circle cx="12" cy="12" r="4"/></svg>
        <span>${isWorking ? 'Active Context' : 'Work on this'}</span>
      `;
    }

    // GitHub Actions in Header
    const btnOpenGh = document.getElementById('btn-open-github');
    const btnPublishGh = document.getElementById('btn-publish-github');
    if (btnOpenGh && btnPublishGh) {
      if (repo.gitHubUrl) {
        btnOpenGh.style.display = 'inline-flex';
        btnPublishGh.style.display = 'none';
        btnOpenGh.onclick = () => {
          const targetUrl = repo.branch && repo.branch !== 'HEAD' && !repo.branch.startsWith('(')
            ? `${repo.gitHubUrl.replace(/\.git$/, '')}/tree/${encodeURIComponent(repo.branch)}`
            : repo.gitHubUrl.replace(/\.git$/, '');
          callBridge('open_url_external', { url: targetUrl });
        };
      } else if (!repo.remotes || repo.remotes.length === 0) {
        btnOpenGh.style.display = 'none';
        btnPublishGh.style.display = 'inline-flex';
        btnPublishGh.onclick = () => openGitHubPublishModal();
      } else {
        btnOpenGh.style.display = 'none';
        btnPublishGh.style.display = 'none';
      }
    }

    // Load active tab content
    renderActiveTabContent(repo);
  }

  function switchDetailTab(tabName) {
    repoState.activeDetailTab = tabName;

    document.querySelectorAll('.detail-tab-btn').forEach(b => {
      b.classList.toggle('active', b.dataset.tab === tabName);
    });

    document.querySelectorAll('.detail-pane').forEach(p => {
      p.classList.toggle('active', p.id === `pane-${tabName}`);
    });

    const repo = getSelectedRepo();
    if (repo) {
      renderActiveTabContent(repo);
    }
  }

  function renderActiveTabContent(repo) {
    switch (repoState.activeDetailTab) {
      case 'overview':
        renderOverviewTab(repo);
        break;
      case 'changes':
        loadRepoChanges(repo.path);
        break;
      case 'history':
        loadCommitGraph(repo.path);
        break;
      case 'prs':
        loadGitHubPullRequests(repo.path);
        break;
      case 'worktrees':
        loadWorktrees(repo.path);
        break;
      case 'stashes':
        loadStashes(repo.path);
        break;
      case 'run':
        loadRepoLaunchers(repo.path);
        break;
      case 'health':
        loadRepoHealth(repo.id);
        break;
      case 'notes':
        loadRepoNotes(repo.id);
        break;
      case 'memory':
        loadWorkspaceMemories(repo.path);
        break;
    }
  }

  function renderOverviewTab(repo) {
    const commitMsgEl = document.getElementById('detail-commit-msg');
    const commitMetaEl = document.getElementById('detail-commit-meta');
    const remoteUrlEl = document.getElementById('detail-remote-url');
    const treeSummaryEl = document.getElementById('detail-tree-summary');
    const branchWorktreeEl = document.getElementById('detail-branch-worktree-val');

    if (commitMsgEl) commitMsgEl.textContent = repo.lastCommitMsg || 'No commits recorded';
    if (commitMetaEl) {
      commitMetaEl.textContent = repo.lastCommitAuthor
        ? `${repo.lastCommitAuthor} · ${repo.lastCommitHash || ''} · ${formatDate(repo.lastCommitDate)}`
        : 'Initial repository';
    }

    if (remoteUrlEl) {
      const primaryRemote = repo.remotes && repo.remotes.length > 0 ? repo.remotes[0] : null;
      remoteUrlEl.textContent = primaryRemote ? `${primaryRemote.name}: ${primaryRemote.fetchUrl}` : 'Local only (No remote configured)';
    }

    if (treeSummaryEl) {
      treeSummaryEl.textContent = repo.isClean
        ? 'Clean (No modified or untracked files)'
        : `${repo.stagedCount} staged, ${repo.unstagedCount} unstaged, ${repo.untrackedCount} untracked`;
    }

    if (branchWorktreeEl) {
      branchWorktreeEl.textContent = `Branch: ${repo.branch || 'detached'} · ${repo.worktreeCount || 1} Worktree(s)`;
    }
  }

  async function loadRepoChanges(repoPath) {
    const listEl = document.getElementById('changes-file-list');
    const countEl = document.getElementById('changes-file-count');
    const diffBox = document.getElementById('diff-content-box');
    const diffTitle = document.getElementById('diff-selected-file');
    const diffStats = document.getElementById('diff-stats-pill');

    if (!listEl) return;

    listEl.innerHTML = `<div class="empty-state" style="padding: 20px; font-size: 11px; text-align: center;">Inspecting working tree...</div>`;
    repoState.currentChanges = [];
    repoState.checkedChangeFiles.clear();

    try {
      const res = await callBridge('get_repo_changes', { repoPath });
      const changes = (res && res.changes) ? res.changes : [];
      repoState.currentChanges = changes;

      if (countEl) countEl.textContent = String(changes.length);

      if (changes.length === 0) {
        listEl.innerHTML = `<div class="empty-state" style="padding: 20px; font-size: 11px; text-align: center; color: var(--text-dim);">Working tree is clean.</div>`;
        if (diffBox) diffBox.textContent = 'No differences in working tree.';
        if (diffTitle) diffTitle.textContent = 'Clean working tree';
        if (diffStats) diffStats.textContent = '';
        return;
      }

      listEl.innerHTML = '';
      changes.forEach((ch, idx) => {
        const item = document.createElement('div');
        item.className = 'changes-file-item';
        item.dataset.index = idx;

        const isStaged = ch.isStaged;
        const statusLetter = isStaged ? ch.indexStatus : ch.worktreeStatus;
        const badgeColor = statusLetter === '?' ? '#06b6d4' : statusLetter === 'M' ? '#f59e0b' : statusLetter === 'A' ? '#10b981' : '#ef4444';

        item.innerHTML = `
          <div style="display: flex; align-items: center; gap: 7px; overflow: hidden;">
            <input type="checkbox" class="chk-file" data-path="${escapeHtml(ch.path)}" />
            <span style="font-size: 10px; font-weight: 700; color: ${badgeColor}; width: 12px; text-align: center;">${escapeHtml(statusLetter)}</span>
            <span style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis;" title="${escapeHtml(ch.path)}">${escapeHtml(ch.path)}</span>
          </div>
          <span style="font-size: 9.5px; color: var(--text-dim); text-transform: uppercase;">${isStaged ? 'Staged' : 'Unstaged'}</span>
        `;

        item.addEventListener('click', (e) => {
          if (e.target.classList.contains('chk-file')) return;
          document.querySelectorAll('.changes-file-item').forEach(el => el.classList.remove('selected'));
          item.classList.add('selected');
          fetchAndShowDiff(repoPath, ch.path, isStaged);
        });

        const chk = item.querySelector('.chk-file');
        chk.addEventListener('change', (e) => {
          if (e.target.checked) repoState.checkedChangeFiles.add(ch.path);
          else repoState.checkedChangeFiles.delete(ch.path);
        });

        listEl.appendChild(item);
      });

      // Auto-preview first file
      if (changes.length > 0) {
        listEl.children[0].classList.add('selected');
        fetchAndShowDiff(repoPath, changes[0].path, changes[0].isStaged);
      }
    } catch (err) {
      listEl.innerHTML = `<div class="empty-state" style="padding: 20px; font-size: 11px; text-align: center; color: #ef4444;">Failed to read working tree.</div>`;
    }
  }

  async function fetchAndShowDiff(repoPath, relativePath, staged) {
    const diffBox = document.getElementById('diff-content-box');
    const diffTitle = document.getElementById('diff-selected-file');
    const diffStats = document.getElementById('diff-stats-pill');

    if (!diffBox) return;

    if (diffTitle) diffTitle.textContent = relativePath;
    if (diffStats) diffStats.textContent = 'Loading diff...';
    diffBox.textContent = 'Loading unified diff...';

    try {
      const res = await callBridge('get_repo_diff', { repoPath, relativePath, staged });
      if (res) {
        if (diffStats) diffStats.textContent = `+${res.additions || 0} -${res.deletions || 0}`;
        renderDiffText(diffBox, res.diffContent || 'No changes in this view.');
      }
    } catch (err) {
      diffBox.textContent = 'Error loading diff: ' + err.message;
    }
  }

  function renderDiffText(container, text) {
    container.innerHTML = '';
    const lines = text.split('\n');
    const frag = document.createDocumentFragment();

    lines.forEach(line => {
      const div = document.createElement('div');
      div.className = 'diff-line';

      if (line.startsWith('+') && !line.startsWith('+++')) {
        div.className += ' diff-add';
      } else if (line.startsWith('-') && !line.startsWith('---')) {
        div.className += ' diff-del';
      } else if (line.startsWith('@@')) {
        div.className += ' diff-hunk';
      }

      div.textContent = line;
      frag.appendChild(div);
    });

    container.appendChild(frag);
  }

  async function stageCheckedFiles(unstage) {
    const repo = getSelectedRepo();
    if (!repo) return;

    const files = Array.from(repoState.checkedChangeFiles);
    if (files.length === 0) {
      showToast('Select files via checkboxes first', 'warn');
      return;
    }

    try {
      const res = await callBridge('stage_repo_changes', {
        repoPath: repo.path,
        files,
        unstage
      });

      if (res && res.success) {
        showToast(unstage ? 'Files unstaged' : 'Files staged', 'success');
        await loadRepoChanges(repo.path);
        refreshSelectedRepoMetadata();
      } else {
        showToast('Staging failed: ' + (res?.output || 'Unknown error'), 'error');
      }
    } catch (e) {
      showToast('Error staging changes: ' + e.message, 'error');
    }
  }

  async function stageAllFiles() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const allPaths = repoState.currentChanges.map(c => c.path);
    if (allPaths.length === 0) return;

    try {
      const res = await callBridge('stage_repo_changes', {
        repoPath: repo.path,
        files: allPaths,
        unstage: false
      });

      if (res && res.success) {
        showToast('All files staged', 'success');
        await loadRepoChanges(repo.path);
        refreshSelectedRepoMetadata();
      }
    } catch (e) {
      showToast('Error staging all: ' + e.message, 'error');
    }
  }

  async function createLocalCommit() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const msgInput = document.getElementById('repo-commit-message');
    const stageAllChk = document.getElementById('chk-stage-all-commit');
    const message = msgInput?.value.trim();

    if (!message) {
      showToast('Please enter a commit message', 'warn');
      msgInput?.focus();
      return;
    }

    const pushToRemote = repoState.commitAction === 'push';
    if (pushToRemote) {
      showToast('Creating commit and pushing to live GitHub...', 'info');
    }

    try {
      const res = await callBridge('create_repo_commit', {
        repoPath: repo.path,
        message,
        stageAll: !!stageAllChk?.checked,
        pushToRemote
      });

      if (res && res.success) {
        if (pushToRemote) {
          if (res.pushed) {
            showToast('Committed & pushed live to origin successfully!', 'success', 5000);
          } else {
            showToast(`Commit saved locally, but live push failed: ${res.pushOutput || res.output}`, 'warn', 7000);
          }
        } else {
          showToast('Local commit created successfully', 'success');
        }

        if (msgInput) msgInput.value = '';
        if (stageAllChk) stageAllChk.checked = false;
        await loadRepoChanges(repo.path);
        refreshSelectedRepoMetadata();
      } else {
        showToast('Commit failed: ' + (res?.output || 'Unknown error'), 'error');
      }
    } catch (e) {
      showToast('Error creating commit: ' + e.message, 'error');
    }
  }

  async function generateAiCommitMessage() {
    const repo = getSelectedRepo();
    if (!repo) {
      showToast('Select a repository first', 'warn');
      return;
    }

    const btn = document.getElementById('btn-ai-generate-commit');
    const msgInput = document.getElementById('repo-commit-message');
    if (!msgInput) return;

    const originalHtml = btn ? btn.innerHTML : '';
    if (btn) {
      btn.disabled = true;
      btn.innerHTML = `<span class="spinner" style="width: 11px; height: 11px; border-width: 2px;"></span> Generating...`;
    }

    try {
      showToast('Analyzing diff & generating commit message...', 'info', 2000);
      const res = await callBridge('generate_ai_commit_message', { repoPath: repo.path });
      if (res && res.success && res.fullMessage) {
        msgInput.value = res.fullMessage;
        showToast('Generated commit message from diff!', 'success');
        msgInput.focus();
      } else {
        showToast(res?.message || 'Could not generate message: working tree clean or no changes found.', 'warn');
      }
    } catch (e) {
      showToast('Error generating message: ' + e.message, 'error');
    } finally {
      if (btn) {
        btn.disabled = false;
        btn.innerHTML = originalHtml;
      }
    }
  }

  async function loadRepoLaunchers(repoPath) {
    const container = document.getElementById('launchers-list-container');
    const quickRunBtn = document.getElementById('btn-quick-run');
    const quickRunLbl = document.getElementById('lbl-quick-run');

    if (!container) return;

    container.innerHTML = `<div class="empty-state" style="padding: 24px; font-size: 11px; text-align: center;">Scanning for launchers...</div>`;

    try {
      const res = await callBridge('detect_repo_launchers', { repoPath });
      const launchers = res && res.launchers ? res.launchers : [];

      if (launchers.length > 0 && quickRunBtn) {
        quickRunBtn.style.display = 'inline-flex';
        if (quickRunLbl) quickRunLbl.textContent = `Run (${launchers[0].label})`;
        quickRunBtn.onclick = () => executeLauncherItem(repoPath, launchers[0]);
      } else if (quickRunBtn) {
        quickRunBtn.style.display = 'none';
      }

      if (launchers.length === 0) {
        container.innerHTML = `<div class="empty-state" style="padding: 24px; font-size: 11px; text-align: center; color: var(--text-dim);">No runnable project scripts detected. Click 'Add Launcher' to configure one.</div>`;
        return;
      }

      container.innerHTML = '';
      launchers.forEach(l => {
        const card = document.createElement('div');
        card.className = 'launcher-item-card';

        card.innerHTML = `
          <div class="launcher-info">
            <span class="launcher-title">${escapeHtml(l.label)}</span>
            <span class="launcher-sub">${escapeHtml(l.command)} ${(l.args || []).join(' ')}</span>
          </div>
          <button class="btn btn-outline btn-sm btn-run-single" style="gap: 4px;">
            <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"/></svg>
            <span>Run</span>
          </button>
        `;

        card.querySelector('.btn-run-single').addEventListener('click', () => {
          executeLauncherItem(repoPath, l);
        });

        container.appendChild(card);
      });
    } catch (err) {
      container.innerHTML = `<div class="empty-state" style="padding: 24px; font-size: 11px; text-align: center; color: #ef4444;">Failed to detect launchers.</div>`;
    }
  }

  async function executeLauncherItem(repoPath, launcher) {
    try {
      const res = await callBridge('run_repo_launcher', { repoPath, launcher });
      if (res && res.success) {
        showToast(`Launched ${launcher.label}`, 'success');
      } else {
        showToast(`Failed to launch ${launcher.label}`, 'error');
      }
    } catch (e) {
      showToast('Error: ' + e.message, 'error');
    }
  }

  function executeQuickRun() {
    const repo = getSelectedRepo();
    if (repo) loadRepoLaunchers(repo.path);
  }

  async function loadRepoHealth(repoId) {
    const container = document.getElementById('health-findings-container');
    const overallPill = document.getElementById('health-overall-pill');
    if (!container) return;

    container.innerHTML = `<div class="empty-state" style="padding: 24px; font-size: 11px; text-align: center;">Evaluating repository health...</div>`;

    try {
      const res = await callBridge('evaluate_repo_health', { idOrPath: repoId });
      if (res && res.findings) {
        if (overallPill) {
          const st = res.overallStatus || 'PASS';
          overallPill.textContent = st;
          overallPill.style.background = st === 'PASS' ? 'rgba(16, 185, 129, 0.12)' : st === 'WARN' ? 'rgba(245, 158, 11, 0.15)' : 'rgba(239, 68, 68, 0.15)';
          overallPill.style.color = st === 'PASS' ? '#10b981' : st === 'WARN' ? '#f59e0b' : '#ef4444';
        }

        container.innerHTML = '';
        res.findings.forEach(f => {
          const card = document.createElement('div');
          card.className = `health-finding-card ${f.status.toLowerCase()}`;

          card.innerHTML = `
            <div class="health-info">
              <span class="health-title">${escapeHtml(f.title)}</span>
              <span class="health-desc">${escapeHtml(f.explanation)}</span>
              ${f.remediation ? `<span class="health-remedy">Tip: ${escapeHtml(f.remediation)}</span>` : ''}
            </div>
            <span class="badge-agent-state" style="font-size: 10px; background: ${f.status === 'PASS' ? 'rgba(16,185,129,0.1)' : 'rgba(245,158,11,0.1)'}; color: ${f.status === 'PASS' ? '#10b981' : '#f59e0b'};">
              ${escapeHtml(f.status)}
            </span>
          `;

          container.appendChild(card);
        });
      }
    } catch (e) {
      container.innerHTML = `<div class="empty-state" style="padding: 24px; font-size: 11px; text-align: center; color: #ef4444;">Health evaluation failed.</div>`;
    }
  }

  async function loadRepoNotes(repoId) {
    const notesBox = document.getElementById('repo-notes-box');
    if (!notesBox) return;

    notesBox.value = 'Loading notes...';
    try {
      const res = await callBridge('get_repo_notes', { repoId });
      notesBox.value = res ? (res.notes || '') : '';
    } catch (e) {
      notesBox.value = '';
    }
  }

  async function saveCurrentRepoNotes() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const notesBox = document.getElementById('repo-notes-box');
    const statusEl = document.getElementById('notes-save-status');
    if (!notesBox) return;

    const notes = notesBox.value;
    try {
      await callBridge('save_repo_notes', { repoId: repo.id, notes });
      if (statusEl) {
        statusEl.style.display = 'inline';
        setTimeout(() => { statusEl.style.display = 'none'; }, 2000);
      }
    } catch (e) { }
  }

  // =========================================================================
  // 1. Commit Graph & Timeline
  // =========================================================================
  async function loadCommitGraph(repoPath) {
    const container = document.getElementById('commit-graph-container');
    const diffViewer = document.getElementById('commit-diff-viewer');
    if (!container) return;

    container.innerHTML = `
      <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center;">
        <span class="spinner" style="width: 14px; height: 14px; margin-bottom: 8px;"></span>
        <div>Loading commit history...</div>
      </div>
    `;
    if (diffViewer) diffViewer.style.display = 'none';

    try {
      const res = await callBridge('get_commit_graph', { repoPath, maxCount: 150 });
      const commits = (res && res.commits) ? res.commits : [];
      repoState.commitGraph = commits;
      renderCommitGraph(commits, repoPath);
    } catch (e) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: #ef4444;">
          Failed to load commit graph: ${escapeHtml(e.message || String(e))}
        </div>
      `;
    }
  }

  function renderCommitGraph(commits, repoPath) {
    const container = document.getElementById('commit-graph-container');
    const countEl = document.getElementById('lbl-commit-graph-count');
    if (countEl) countEl.textContent = `${commits.length} commits`;
    if (!container) return;

    if (commits.length === 0) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: var(--text-dim);">
          No commits recorded in this repository yet.
        </div>
      `;
      return;
    }

    container.innerHTML = '';

    // Assign lanes topological tracking
    const colors = ['#06b6d4', '#10b981', '#a855f7', '#f59e0b', '#ec4899', '#3b82f6'];
    const activeLanes = [];

    const rowsData = commits.map(commit => {
      let laneIndex = activeLanes.indexOf(commit.hash);
      if (laneIndex === -1) {
        laneIndex = activeLanes.indexOf(null);
        if (laneIndex === -1) {
          laneIndex = activeLanes.length;
          activeLanes.push(commit.hash);
        } else {
          activeLanes[laneIndex] = commit.hash;
        }
      }

      const parents = commit.parentHashes || [];
      if (parents.length === 0) {
        activeLanes[laneIndex] = null;
      } else {
        activeLanes[laneIndex] = parents[0];
        for (let p = 1; p < parents.length; p++) {
          const parentHash = parents[p];
          if (!activeLanes.includes(parentHash)) {
            const nextSlot = activeLanes.indexOf(null);
            if (nextSlot === -1) activeLanes.push(parentHash);
            else activeLanes[nextSlot] = parentHash;
          }
        }
      }

      while (activeLanes.length > 0 && activeLanes[activeLanes.length - 1] === null) {
        activeLanes.pop();
      }

      return {
        commit,
        lane: laneIndex
      };
    });

    const maxLanes = Math.min(8, Math.max(1, ...rowsData.map(r => r.lane + 1)));
    const svgWidth = maxLanes * 16 + 14;

    const commitRowMap = new Map();
    rowsData.forEach(({ commit, lane }, idx) => {
      commitRowMap.set(commit.hash, { index: idx, lane, color: colors[lane % colors.length] });
    });

    rowsData.forEach(({ commit, lane }, rowIndex) => {
      const row = document.createElement('div');
      row.className = `commit-row ${commit.hash === repoState.selectedCommitHash ? 'selected' : ''}`;
      row.dataset.hash = commit.hash;

      const nodeColor = colors[lane % colors.length];
      const cx = lane * 16 + 12;
      const cy = 18;

      let badgesHtml = '';
      if (commit.isHead) {
        badgesHtml += `<span class="commit-ref-pill head">HEAD</span>`;
      }
      if (commit.branchRefs && commit.branchRefs.length > 0) {
        commit.branchRefs.forEach(b => {
          const isRemote = b.includes('/');
          badgesHtml += `<span class="commit-ref-pill ${isRemote ? 'remote' : 'branch'}" title="Branch: ${escapeHtml(b)}">${escapeHtml(b)}</span>`;
        });
      }
      if (commit.tagRefs && commit.tagRefs.length > 0) {
        commit.tagRefs.forEach(t => {
          badgesHtml += `<span class="commit-ref-pill tag" title="Tag: ${escapeHtml(t)}">tag: ${escapeHtml(t)}</span>`;
        });
      }

      // Generate DAG SVG connectors
      let svgPaths = '';

      // 1. Line from top into this commit node
      if (rowIndex > 0) {
        svgPaths += `<line x1="${cx}" y1="0" x2="${cx}" y2="${cy}" stroke="${nodeColor}" stroke-width="2" opacity="0.6"/>`;
      }

      // 2. Connector curves/lines to parents
      const parents = commit.parentHashes || [];
      if (parents.length === 0) {
        // Initial commit, no downward line
      } else {
        parents.forEach(pHash => {
          const parentInfo = commitRowMap.get(pHash);
          if (parentInfo) {
            const pCx = parentInfo.lane * 16 + 12;
            if (parentInfo.lane === lane) {
              svgPaths += `<line x1="${cx}" y1="${cy}" x2="${cx}" y2="36" stroke="${nodeColor}" stroke-width="2" opacity="0.6"/>`;
            } else {
              // Cubic Bezier curve linking branches
              svgPaths += `<path d="M ${cx} ${cy} C ${cx} 27, ${pCx} 27, ${pCx} 36" stroke="${nodeColor}" stroke-width="2" fill="none" opacity="0.65"/>`;
            }
          } else {
            // Older parent beyond max count
            svgPaths += `<line x1="${cx}" y1="${cy}" x2="${cx}" y2="36" stroke="${nodeColor}" stroke-width="2" stroke-dasharray="2,2" opacity="0.4"/>`;
          }
        });
      }

      // 3. Node Circle
      svgPaths += `<circle cx="${cx}" cy="${cy}" r="${commit.isHead ? '5.5' : '4.5'}" fill="${nodeColor}" stroke="var(--bg-card)" stroke-width="2"/>`;
      if (commit.isHead) {
        svgPaths += `<circle cx="${cx}" cy="${cy}" r="7.5" fill="none" stroke="${nodeColor}" stroke-width="1.2" opacity="0.75"/>`;
      }

      row.innerHTML = `
        <div class="commit-graph-canvas-cell">
          <svg width="${svgWidth}" height="36" viewBox="0 0 ${svgWidth} 36" style="display: block;">
            ${svgPaths}
          </svg>
        </div>
        <div class="commit-info">
          <div class="commit-title-line">
            ${badgesHtml}
            <span class="commit-subject" title="${escapeHtml(commit.subject)}">${escapeHtml(commit.subject)}</span>
          </div>
          <div class="commit-meta-line">
            <span class="commit-hash">${escapeHtml(commit.shortHash || commit.hash.substring(0, 7))}</span>
            <span class="commit-author">${escapeHtml(commit.authorName)}</span>
            <span class="commit-date">${formatDate(commit.date)}</span>
          </div>
        </div>
        <div class="commit-row-actions">
          <button class="btn-commit-row-revert" type="button" title="Revert commit or branch from it">
            <svg class="icon-svg" style="width: 10px; height: 10px;" viewBox="0 0 24 24"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/></svg>
            <span>Revert...</span>
          </button>
        </div>
      `;

      row.querySelector('.btn-commit-row-revert')?.addEventListener('click', (e) => {
        e.stopPropagation();
        openRevertCommitModal(commit.hash, commit);
      });

      row.addEventListener('click', () => {
        container.querySelectorAll('.commit-row').forEach(r => r.classList.remove('selected'));
        row.classList.add('selected');
        repoState.selectedCommitHash = commit.hash;
        loadCommitDiff(repoPath, commit.hash, commit);
      });

      container.appendChild(row);
    });
  }

  async function loadCommitDiff(repoPath, commitHash, commitMeta) {
    const viewer = document.getElementById('commit-diff-viewer');
    const hashEl = document.getElementById('diff-commit-hash');
    const subjectEl = document.getElementById('diff-commit-subject');
    const authorEl = document.getElementById('diff-commit-author');
    const dateEl = document.getElementById('diff-commit-date');
    const statEl = document.getElementById('diff-commit-stat');
    const patchEl = document.getElementById('diff-commit-patch');

    if (!viewer || !patchEl) return;

    repoState.activeDiffCommit = { hash: commitHash, meta: commitMeta };

    viewer.style.display = 'flex';
    if (hashEl) hashEl.textContent = commitHash.substring(0, 8);
    if (subjectEl) subjectEl.textContent = commitMeta.subject;
    if (authorEl) authorEl.textContent = commitMeta.authorName || '';
    if (dateEl) dateEl.textContent = formatDate(commitMeta.date);
    if (statEl) statEl.textContent = 'Inspecting patch...';
    patchEl.textContent = 'Loading commit diff...';

    try {
      const res = await callBridge('get_commit_diff', { repoPath, commitHash });
      if (res) {
        if (statEl) statEl.textContent = `+${res.insertions || 0} -${res.deletions || 0} (${res.filesChanged || 0} files)`;
        patchEl.innerHTML = formatDiffPatchHtml(res.diffPatch || 'Empty diff or merge commit');
      }
    } catch (e) {
      patchEl.textContent = 'Failed to load diff: ' + (e.message || e);
    }
  }

  function formatDiffPatchHtml(rawPatch) {
    if (!rawPatch) return 'No diff patch available';
    const lines = rawPatch.split('\n');
    return lines.map(line => {
      const esc = escapeHtml(line);
      if (line.startsWith('+') && !line.startsWith('+++')) {
        return `<span style="color: #4ade80; background: rgba(74, 222, 128, 0.08); display: block;">${esc}</span>`;
      } else if (line.startsWith('-') && !line.startsWith('---')) {
        return `<span style="color: #f87171; background: rgba(248, 113, 113, 0.08); display: block;">${esc}</span>`;
      } else if (line.startsWith('@@')) {
        return `<span style="color: #38bdf8; font-weight: 600; display: block;">${esc}</span>`;
      } else if (line.startsWith('diff --git')) {
        return `<span style="color: #e2e8f0; font-weight: 700; border-top: 1px solid rgba(255,255,255,0.08); margin-top: 6px; padding-top: 4px; display: block;">${esc}</span>`;
      }
      return `<span>${esc}</span>`;
    }).join('');
  }

  // =========================================================================
  // Revert Commit & Branching Modal
  // =========================================================================
  function openRevertCommitModal(commitHash, commitMeta) {
    const modal = document.getElementById('modal-revert-commit');
    const repo = getSelectedRepo();
    if (!modal || !repo) {
      showToast('Select a repository and commit first', 'warn');
      return;
    }

    const shortHash = (commitHash || '').substring(0, 7);
    const subject = commitMeta?.subject || `Commit ${shortHash}`;

    repoState.revertTarget = {
      hash: commitHash,
      shortHash,
      subject,
      author: commitMeta?.authorName || '',
      date: commitMeta?.date || ''
    };

    // Update Modal Commit Card
    const hashEl = document.getElementById('revert-modal-hash');
    const subjectEl = document.getElementById('revert-modal-subject');
    const authorEl = document.getElementById('revert-modal-author');
    const dateEl = document.getElementById('revert-modal-date');
    const branchLabelEl = document.getElementById('revert-modal-current-branch-label');

    if (hashEl) hashEl.textContent = shortHash;
    if (subjectEl) subjectEl.textContent = subject;
    if (authorEl) authorEl.textContent = commitMeta?.authorName ? `Authored by ${commitMeta.authorName}` : '';
    if (dateEl) dateEl.textContent = commitMeta?.date ? formatDate(commitMeta.date) : '';
    if (branchLabelEl) branchLabelEl.textContent = repo.branch || 'main';

    // Working Tree Status Check
    const statusBox = document.getElementById('revert-modal-status-box');
    const statusText = document.getElementById('revert-modal-status-text');
    const isDirty = repo.isDirty || (repo.stagedCount > 0) || (repo.unstagedCount > 0);

    if (statusBox && statusText) {
      if (isDirty) {
        statusBox.style.background = 'rgba(245, 158, 11, 0.08)';
        statusBox.style.borderColor = 'rgba(245, 158, 11, 0.25)';
        statusBox.style.color = '#f59e0b';
        statusText.textContent = 'Uncommitted changes detected. Stash or commit changes before reverting.';
      } else {
        statusBox.style.background = 'rgba(16, 185, 129, 0.08)';
        statusBox.style.borderColor = 'rgba(16, 185, 129, 0.2)';
        statusBox.style.color = '#34d399';
        statusText.textContent = 'Working tree is clean. Safe to revert.';
      }
    }

    // Prefill branch suggestions
    const safeSubjectSlug = (commitMeta?.subject || '')
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .substring(0, 20);
    const defaultBranchName = safeSubjectSlug ? `revert-${shortHash}-${safeSubjectSlug}` : `revert-${shortHash}`;

    const branchInput = document.getElementById('input-revert-branch-name');
    if (branchInput) branchInput.value = defaultBranchName;

    const branchOnlyInput = document.getElementById('input-branch-only-name');
    if (branchOnlyInput) branchOnlyInput.value = `branch-${shortHash}`;

    // Reset to default option: new_branch
    const radioNewBranch = document.getElementById('radio-revert-new-branch');
    if (radioNewBranch) radioNewBranch.checked = true;
    updateRevertStrategyUI('new_branch');

    modal.classList.add('active');
  }

  function updateRevertStrategyUI(selectedMode) {
    const cardNewBranch = document.getElementById('label-strat-new-branch');
    const cardCurrent = document.getElementById('label-strat-current');
    const cardBranchOnly = document.getElementById('label-strat-branch-only');
    const wrapNewBranchInput = document.getElementById('wrap-revert-new-branch-input');
    const wrapBranchOnlyInput = document.getElementById('wrap-branch-only-input');
    const lblSubmit = document.getElementById('lbl-submit-revert');

    if (cardNewBranch) cardNewBranch.classList.toggle('active', selectedMode === 'new_branch');
    if (cardCurrent) cardCurrent.classList.toggle('active', selectedMode === 'current');
    if (cardBranchOnly) cardBranchOnly.classList.toggle('active', selectedMode === 'branch_only');

    if (wrapNewBranchInput) wrapNewBranchInput.style.display = selectedMode === 'new_branch' ? 'block' : 'none';
    if (wrapBranchOnlyInput) wrapBranchOnlyInput.style.display = selectedMode === 'branch_only' ? 'block' : 'none';

    if (lblSubmit) {
      if (selectedMode === 'new_branch') lblSubmit.textContent = 'Revert on New Branch';
      else if (selectedMode === 'current') lblSubmit.textContent = 'Revert on Current Branch';
      else if (selectedMode === 'branch_only') lblSubmit.textContent = 'Create & Switch Branch';
    }
  }

  function closeRevertCommitModal() {
    document.getElementById('modal-revert-commit')?.classList.remove('active');
  }

  async function submitRevertCommit() {
    const repo = getSelectedRepo();
    if (!repo || !repoState.revertTarget) {
      showToast('No commit selected for revert', 'warn');
      return;
    }

    const selectedRadio = document.querySelector('input[name="revert-strategy"]:checked');
    const mode = selectedRadio ? selectedRadio.value : 'new_branch';
    const commitHash = repoState.revertTarget.hash;

    const btnSubmit = document.getElementById('btn-submit-revert-modal');
    const originalText = btnSubmit?.innerHTML;
    if (btnSubmit) {
      btnSubmit.disabled = true;
      btnSubmit.innerHTML = '<span class="spinner" style="width: 12px; height: 12px; margin-right: 6px;"></span>Processing...';
    }

    try {
      if (mode === 'branch_only') {
        const branchName = document.getElementById('input-branch-only-name')?.value?.trim();
        if (!branchName) {
          showToast('Please enter a branch name', 'warn');
          if (btnSubmit) { btnSubmit.disabled = false; btnSubmit.innerHTML = originalText; }
          return;
        }

        const res = await callBridge('create_branch_at_commit', {
          repoPath: repo.path,
          commitHash,
          branchName,
          checkout: true
        });

        if (res && res.success) {
          showToast(res.message || `Branch '${branchName}' created successfully.`, 'success');
          closeRevertCommitModal();
          await loadRepoManagerState();
          await loadCommitGraph(repo.path);
        } else {
          showToast(res?.message || 'Failed to create branch.', 'error', 6000);
        }
      } else {
        // Revert mode: new_branch or current
        let newBranchName = null;
        if (mode === 'new_branch') {
          newBranchName = document.getElementById('input-revert-branch-name')?.value?.trim();
          if (!newBranchName) {
            showToast('Please enter a branch name for the revert', 'warn');
            if (btnSubmit) { btnSubmit.disabled = false; btnSubmit.innerHTML = originalText; }
            return;
          }
        }

        const res = await callBridge('revert_commit', {
          repoPath: repo.path,
          commitHash,
          mode,
          newBranchName
        });

        if (res && res.success) {
          showToast(res.message || 'Revert completed successfully.', 'success', 6000);
          closeRevertCommitModal();
          await loadRepoManagerState();
          await loadCommitGraph(repo.path);
        } else {
          showToast(res?.message || 'Revert failed.', 'error', 8000);
        }
      }
    } catch (err) {
      showToast('Error during operation: ' + (err.message || err), 'error', 8000);
    } finally {
      if (btnSubmit) {
        btnSubmit.disabled = false;
        btnSubmit.innerHTML = originalText;
      }
    }
  }

  // =========================================================================
  // 2. Git Worktrees Manager
  // =========================================================================
  async function loadWorktrees(repoPath) {
    const container = document.getElementById('worktrees-list-container');
    if (!container) return;

    container.innerHTML = `
      <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center;">
        <span class="spinner" style="width: 14px; height: 14px; margin-bottom: 8px;"></span>
        <div>Loading linked worktrees...</div>
      </div>
    `;

    try {
      const res = await callBridge('get_worktrees', { repoPath });
      const wts = (res && res.worktrees) ? res.worktrees : [];
      repoState.worktrees = wts;
      renderWorktrees(wts, repoPath);
    } catch (e) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: #ef4444;">
          Failed to load worktrees: ${escapeHtml(e.message || String(e))}
        </div>
      `;
    }
  }

  function renderWorktrees(worktrees, repoPath) {
    const container = document.getElementById('worktrees-list-container');
    if (!container) return;

    if (worktrees.length === 0) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: var(--text-dim);">
          No worktrees linked to this repository yet.
        </div>
      `;
      return;
    }

    container.innerHTML = '';
    worktrees.forEach(wt => {
      const card = document.createElement('div');
      card.className = `worktree-card ${wt.isMainWorktree ? 'is-main' : ''}`;

      card.innerHTML = `
        <div class="worktree-info">
          <div class="worktree-title-row">
            <span class="worktree-branch-badge">
              <svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24"><circle cx="18" cy="18" r="3"/><circle cx="6" cy="6" r="3"/><path d="M13 6h3a2 2 0 0 1 2 2v7"/><line x1="6" y1="9" x2="6" y2="21"/></svg>
              <span>${escapeHtml(wt.branch || wt.headCommit || 'detached')}</span>
            </span>
            ${wt.isMainWorktree ? `<span class="worktree-pill main">Main Tree</span>` : ''}
            ${wt.isBare ? `<span class="worktree-pill">Bare</span>` : ''}
            ${wt.isLocked ? `<span class="worktree-pill locked" title="${escapeHtml(wt.lockReason || 'Locked')}">Locked</span>` : ''}
          </div>
          <span class="worktree-path" title="${escapeHtml(wt.path)}">${escapeHtml(wt.path)}</span>
        </div>
        <div class="worktree-actions">
          <button class="btn btn-primary btn-xs btn-wt-agent" title="Launch AI Agent in this worktree">
            <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"/></svg>
            <span>Start Agent</span>
          </button>
          <button class="btn btn-outline btn-xs btn-wt-editor" title="Open worktree in External Code Editor">
            <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><path d="M16.5 9.4 7.55 2.81a1.5 1.5 0 0 0-1.85.1L2.3 5.4a1.5 1.5 0 0 0-.1 2.22L6 11.2l-3.8 3.58a1.5 1.5 0 0 0 .1 2.22l3.4 2.49a1.5 1.5 0 0 0 1.85.1L16.5 13"/></svg>
            <span>Editor</span>
          </button>
          <button class="btn btn-outline btn-xs btn-wt-terminal" title="Open terminal in worktree directory">
            <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/></svg>
            <span>Terminal</span>
          </button>
          ${!wt.isMainWorktree ? `
          <button class="btn btn-outline btn-xs btn-wt-remove" style="color: #ef4444;" title="Remove this worktree">
            <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
            <span>Remove</span>
          </button>` : ''}
        </div>
      `;

      card.querySelector('.btn-wt-agent')?.addEventListener('click', () => {
        const cli = repoState.scanSettings.defaultAgent || 'claude';
        callBridge('launch_agent_in_repo', { repoPath: wt.path, cliType: cli });
        showToast(`Launching ${cli} in worktree: ${wt.branch || 'detached'}...`, 'info');
      });

      card.querySelector('.btn-wt-editor')?.addEventListener('click', () => {
        callBridge('open_repo_in_editor', { repoPath: wt.path, target: 'editor' });
      });

      card.querySelector('.btn-wt-terminal')?.addEventListener('click', () => {
        callBridge('open_repo_in_editor', { repoPath: wt.path, target: 'terminal' });
      });

      card.querySelector('.btn-wt-remove')?.addEventListener('click', async () => {
        if (!confirm(`Are you sure you want to remove worktree at:\n${wt.path}?`)) return;
        try {
          const res = await callBridge('remove_worktree', { repoPath, worktreePath: wt.path, force: false });
          if (res && res.success) {
            showToast('Worktree removed successfully', 'success');
            loadWorktrees(repoPath);
          } else {
            showToast(`Failed to remove worktree: ${res?.message || 'Unknown error'}`, 'error');
          }
        } catch (e) {
          showToast(`Error removing worktree: ${e.message}`, 'error');
        }
      });

      container.appendChild(card);
    });
  }

  function openAddWorktreeModal() {
    const modal = document.getElementById('modal-git-worktree');
    const pathInput = document.getElementById('input-worktree-path');
    const branchSelect = document.getElementById('select-worktree-branch');
    const chkCreate = document.getElementById('chk-worktree-create-branch');
    const wrapNewBranch = document.getElementById('wrap-new-branch-name');
    const newBranchInput = document.getElementById('input-worktree-new-branch');
    const repo = getSelectedRepo();

    if (!modal || !repo) return;

    if (pathInput) pathInput.value = '';
    if (chkCreate) chkCreate.checked = false;
    if (wrapNewBranch) wrapNewBranch.style.display = 'none';
    if (newBranchInput) newBranchInput.value = '';

    if (branchSelect) {
      branchSelect.innerHTML = '';
      const branches = (repo.branches && repo.branches.length > 0) ? repo.branches : [repo.branch || 'main'];
      branches.forEach(b => {
        const opt = document.createElement('option');
        opt.value = b;
        opt.textContent = b;
        branchSelect.appendChild(opt);
      });
      if (repo.branch) branchSelect.value = repo.branch;
    }

    modal.classList.add('active');
  }

  function closeAddWorktreeModal() {
    document.getElementById('modal-git-worktree')?.classList.remove('active');
  }

  async function submitAddWorktreeModal() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const pathInput = document.getElementById('input-worktree-path');
    const branchSelect = document.getElementById('select-worktree-branch');
    const chkCreate = document.getElementById('chk-worktree-create-branch');
    const newBranchInput = document.getElementById('input-worktree-new-branch');

    const worktreePath = pathInput?.value.trim();
    const branch = branchSelect?.value.trim();
    const isNewBranch = chkCreate ? chkCreate.checked : false;
    const targetBranch = isNewBranch ? (newBranchInput?.value.trim() || '') : branch;

    if (!worktreePath) {
      showToast('Worktree directory path is required', 'warn');
      return;
    }
    if (!targetBranch) {
      showToast('Branch name is required', 'warn');
      return;
    }

    try {
      showToast('Creating git worktree...', 'info');
      const res = await callBridge('add_worktree', {
        repoPath: repo.path,
        worktreePath,
        branch: targetBranch,
        createBranch: isNewBranch
      });

      if (res && res.success) {
        showToast('Worktree created successfully', 'success');
        closeAddWorktreeModal();
        loadWorktrees(repo.path);
      } else {
        showToast(`Failed to create worktree: ${res?.message || 'Error'}`, 'error');
      }
    } catch (e) {
      showToast('Failed to create worktree: ' + e.message, 'error');
    }
  }

  // =========================================================================
  // 3. Git Stash Drawer
  // =========================================================================
  async function loadStashes(repoPath) {
    const container = document.getElementById('stashes-list-container');
    const diffViewer = document.getElementById('stash-diff-viewer');
    if (!container) return;

    container.innerHTML = `
      <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center;">
        <span class="spinner" style="width: 14px; height: 14px; margin-bottom: 8px;"></span>
        <div>Loading stashes...</div>
      </div>
    `;
    if (diffViewer) diffViewer.style.display = 'none';

    try {
      const res = await callBridge('get_stashes', { repoPath });
      const stashes = (res && res.stashes) ? res.stashes : [];
      repoState.stashes = stashes;
      renderStashes(stashes, repoPath);
    } catch (e) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: #ef4444;">
          Failed to load stashes: ${escapeHtml(e.message || String(e))}
        </div>
      `;
    }
  }

  function renderStashes(stashes, repoPath) {
    const container = document.getElementById('stashes-list-container');
    if (!container) return;

    if (stashes.length === 0) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: var(--text-dim);">
          No saved stashes in this repository.
        </div>
      `;
      return;
    }

    container.innerHTML = '';
    stashes.forEach(s => {
      const card = document.createElement('div');
      card.className = 'stash-card';

      card.innerHTML = `
        <div class="stash-info">
          <div style="display: flex; align-items: center; gap: 8px;">
            <span class="stash-index">${escapeHtml(s.name)}</span>
            <span class="stash-message" title="${escapeHtml(s.message)}">${escapeHtml(s.message)}</span>
          </div>
          <span class="stash-date">${formatDate(s.date)}</span>
        </div>
        <div class="stash-actions">
          <button class="btn btn-outline btn-xs btn-stash-diff" title="Inspect diff of this stash">
            <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
            <span>Diff</span>
          </button>
          <button class="btn btn-success btn-xs btn-stash-pop" title="Apply changes and remove stash (pop)">
            <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polyline points="9 11 12 14 22 4"/><path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11"/></svg>
            <span>Pop</span>
          </button>
          <button class="btn btn-outline btn-xs btn-stash-apply" title="Apply stash without removing it">
            <span>Apply</span>
          </button>
          <button class="btn btn-outline btn-xs btn-stash-drop" style="color: #ef4444;" title="Discard this stash">
            &times;
          </button>
        </div>
      `;

      card.querySelector('.btn-stash-diff')?.addEventListener('click', () => {
        inspectStashDiff(repoPath, s.index, s.name);
      });

      card.querySelector('.btn-stash-pop')?.addEventListener('click', async () => {
        await executeStashAction(repoPath, s.index, true, 'pop');
      });

      card.querySelector('.btn-stash-apply')?.addEventListener('click', async () => {
        await executeStashAction(repoPath, s.index, false, 'apply');
      });

      card.querySelector('.btn-stash-drop')?.addEventListener('click', async () => {
        if (!confirm(`Discard stash ${s.name}?`)) return;
        try {
          const res = await callBridge('drop_stash', { repoPath, stashIndex: s.index });
          if (res && res.success) {
            showToast(`Dropped ${s.name}`, 'info');
            loadStashes(repoPath);
          } else {
            showToast(`Failed to drop stash: ${res?.message || 'Error'}`, 'error');
          }
        } catch (e) {
          showToast('Failed to drop stash: ' + e.message, 'error');
        }
      });

      container.appendChild(card);
    });
  }

  async function inspectStashDiff(repoPath, stashIndex, stashName) {
    const viewer = document.getElementById('stash-diff-viewer');
    const titleEl = document.getElementById('diff-stash-title');
    const patchEl = document.getElementById('diff-stash-patch');
    if (!viewer || !patchEl) return;

    viewer.style.display = 'flex';
    if (titleEl) titleEl.textContent = `${stashName} Diff`;
    patchEl.textContent = 'Loading stash patch...';

    try {
      const res = await callBridge('get_stash_diff', { repoPath, stashIndex });
      if (res && res.success) {
        patchEl.innerHTML = formatDiffPatchHtml(res.diffContent || 'Clean diff');
      } else {
        patchEl.textContent = 'No diff available or empty stash';
      }
    } catch (e) {
      patchEl.textContent = 'Failed to load stash diff: ' + e.message;
    }
  }

  async function executeStashAction(repoPath, stashIndex, pop, actionLabel) {
    try {
      showToast(`Executing stash ${actionLabel}...`, 'info');
      const res = await callBridge('apply_stash', { repoPath, stashIndex, pop });
      if (res && res.success) {
        showToast(`Stash ${actionLabel} applied successfully`, 'success');
        loadStashes(repoPath);
        loadRepoChanges(repoPath);
      } else {
        showToast(`Failed to ${actionLabel} stash: ${res?.message || 'Conflict occurred'}`, 'error');
      }
    } catch (e) {
      showToast(`Stash ${actionLabel} error: ${e.message}`, 'error');
    }
  }

  function openCreateStashModal() {
    const modal = document.getElementById('modal-git-stash');
    const msgInput = document.getElementById('input-stash-msg');
    const chkUntracked = document.getElementById('chk-stash-untracked');
    if (!modal) return;

    if (msgInput) msgInput.value = '';
    if (chkUntracked) chkUntracked.checked = true;
    modal.classList.add('active');
  }

  function closeCreateStashModal() {
    document.getElementById('modal-git-stash')?.classList.remove('active');
  }

  async function submitCreateStashModal() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const msgInput = document.getElementById('input-stash-msg');
    const chkUntracked = document.getElementById('chk-stash-untracked');
    const message = msgInput?.value.trim() || '';
    const includeUntracked = chkUntracked ? chkUntracked.checked : true;

    try {
      showToast('Creating stash...', 'info');
      const res = await callBridge('create_stash', {
        repoPath: repo.path,
        message,
        includeUntracked
      });

      if (res && res.success) {
        showToast('Changes stashed successfully', 'success');
        closeCreateStashModal();
        loadStashes(repo.path);
        loadRepoChanges(repo.path);
      } else {
        showToast(`Failed to stash changes: ${res?.message || 'Error'}`, 'error');
      }
    } catch (e) {
      showToast('Failed to create stash: ' + e.message, 'error');
    }
  }

  // --- GitHub Pull Requests Deck ---
  async function loadGitHubPullRequests(repoPath) {
    const container = document.getElementById('prs-list-container');
    const badge = document.getElementById('lbl-repo-prs-badge');
    if (!container) return;

    if (!repoPath) {
      container.innerHTML = `<div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center;">No repository selected.</div>`;
      return;
    }

    repoState.isLoadingPrs = true;
    container.innerHTML = `<div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center;"><span class="spinner" style="width: 14px; height: 14px; border-width: 2px;"></span> Fetching pull requests from GitHub...</div>`;

    try {
      const stateFilter = repoState.prFilterState || 'open';
      const res = await callBridge('get_github_pull_requests', { repoPath, state: stateFilter });

      if (res && res.success) {
        repoState.pullRequests = res.pullRequests || [];
        if (badge) {
          const openCount = repoState.pullRequests.filter(p => p.state === 'open').length;
          badge.textContent = openCount;
          badge.style.display = openCount > 0 ? 'inline-block' : 'none';
        }
        renderPullRequests(repoState.pullRequests, repoPath, res.repoUrl);
      } else {
        container.innerHTML = `
          <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: var(--text-dim);">
            <div style="margin-bottom: 6px; color: var(--text-muted); font-weight: 600;">Unable to fetch pull requests</div>
            <div>${escapeHtml(res?.message || 'Repository might not have a GitHub remote or token is missing.')}</div>
          </div>
        `;
        if (badge) badge.style.display = 'none';
      }
    } catch (e) {
      container.innerHTML = `<div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: #ef4444;">Error loading pull requests: ${escapeHtml(e.message)}</div>`;
    } finally {
      repoState.isLoadingPrs = false;
    }
  }

  function renderPullRequests(prs, repoPath, repoUrl) {
    const container = document.getElementById('prs-list-container');
    if (!container) return;

    if (!prs || prs.length === 0) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: var(--text-dim);">
          No ${escapeHtml(repoState.prFilterState)} pull requests found for this repository.
        </div>
      `;
      return;
    }

    container.innerHTML = '';
    prs.forEach(pr => {
      const card = document.createElement('div');
      card.className = 'pr-card';

      const stateClass = pr.isDraft ? 'draft' : (pr.state === 'open' ? 'open' : 'closed');
      const stateLabel = pr.isDraft ? 'Draft' : (pr.state === 'open' ? 'Open' : 'Closed');

      let labelsHtml = '';
      if (pr.labels && pr.labels.length > 0) {
        labelsHtml = pr.labels.map(l => `<span class="pr-label-tag">${escapeHtml(l)}</span>`).join(' ');
      }

      const avatarHtml = pr.userAvatar
        ? `<img src="${escapeHtml(pr.userAvatar)}" alt="${escapeHtml(pr.userLogin)}" style="width: 16px; height: 16px; border-radius: 50%; vertical-align: middle; margin-right: 4px;" />`
        : '';

      const prDate = pr.updatedAt || pr.createdAt;
      const formattedDate = prDate ? formatDate(prDate) : '';

      card.innerHTML = `
        <div class="pr-card-main">
          <div class="pr-card-title-row">
            <span class="pr-badge ${stateClass}">
              <svg class="icon-svg" style="width: 10px; height: 10px;" viewBox="0 0 24 24"><circle cx="18" cy="18" r="3"/><circle cx="6" cy="6" r="3"/><path d="M13 6h3a2 2 0 0 1 2 2v7"/><line x1="6" y1="9" x2="6" y2="21"/></svg>
              <span>${stateLabel}</span>
            </span>
            <span class="pr-number">#${pr.number}</span>
            <a href="${escapeHtml(pr.htmlUrl || '#')}" target="_blank" class="pr-title" title="${escapeHtml(pr.title)}">${escapeHtml(pr.title)}</a>
            ${labelsHtml}
          </div>
          <div class="pr-meta-row">
            <span style="display: inline-flex; align-items: center;">${avatarHtml}<strong>${escapeHtml(pr.userLogin || 'unknown')}</strong></span>
            <span>Updated ${formattedDate}</span>
            <span class="pr-branch-flow">
              <span>${escapeHtml(pr.headRef || 'head')}</span>
              <svg class="icon-svg" style="width: 10px; height: 10px;" viewBox="0 0 24 24"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>
              <span>${escapeHtml(pr.baseRef || 'main')}</span>
            </span>
          </div>
        </div>
        <div class="pr-actions">
          <button class="btn btn-outline btn-xs btn-pr-checkout" title="Fetch and checkout branch for PR #${pr.number}">
            <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><path d="M6 9v9"/><path d="M18 9a9 9 0 0 0-9 9"/></svg>
            <span>Checkout</span>
          </button>
          ${pr.state === 'open' ? `
            <button class="btn btn-success btn-xs btn-pr-merge" title="Merge this pull request">
              <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>
              <span>Merge</span>
            </button>
          ` : ''}
          <a href="${escapeHtml(pr.htmlUrl || '#')}" target="_blank" class="btn btn-outline btn-xs" title="Open on GitHub">
            <svg class="icon-svg" style="width: 11px; height: 11px;" viewBox="0 0 24 24"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>
          </a>
        </div>
      `;

      card.querySelector('.btn-pr-checkout')?.addEventListener('click', () => {
        checkoutPullRequest(repoPath, pr.number);
      });

      card.querySelector('.btn-pr-merge')?.addEventListener('click', () => {
        openPrMergeModal(repoPath, pr);
      });

      container.appendChild(card);
    });
  }

  async function checkoutPullRequest(repoPath, prNumber) {
    try {
      showToast(`Fetching and checking out PR #${prNumber}...`, 'info');
      const res = await callBridge('checkout_github_pull_request', { repoPath, prNumber });
      if (res && res.success) {
        showToast(res.message || `Checked out PR #${prNumber}`, 'success');
        refreshSelectedRepoMetadata();
        switchDetailTab('changes');
      } else {
        showToast(`Checkout failed: ${res?.message || 'Error'}`, 'error', 6000);
      }
    } catch (e) {
      showToast('Error checking out PR: ' + e.message, 'error');
    }
  }

  function openPrMergeModal(repoPath, pr) {
    const modal = document.getElementById('modal-pr-merge');
    const titleEl = document.getElementById('pr-merge-modal-title');
    const descEl = document.getElementById('pr-merge-modal-desc');
    const numInput = document.getElementById('pr-merge-pr-number');
    const titleInput = document.getElementById('input-pr-merge-title');
    const msgInput = document.getElementById('input-pr-merge-message');
    if (!modal) return;

    if (numInput) numInput.value = pr.number;
    if (titleEl) titleEl.textContent = `Merge PR #${pr.number}: ${pr.title}`;
    if (descEl) descEl.textContent = `Merge branch '${pr.headRef}' into '${pr.baseRef}'.`;
    if (titleInput) titleInput.value = `Merge pull request #${pr.number} from ${pr.headRef}`;
    if (msgInput) msgInput.value = pr.title || '';

    modal.classList.add('active');
  }

  function closePrMergeModal() {
    document.getElementById('modal-pr-merge')?.classList.remove('active');
  }

  async function submitPrMergeModal() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const numInput = document.getElementById('pr-merge-pr-number');
    const prNumber = parseInt(numInput?.value, 10);
    const methodSelect = document.getElementById('select-pr-merge-method');
    const mergeMethod = methodSelect?.value || 'merge';
    const titleInput = document.getElementById('input-pr-merge-title');
    const msgInput = document.getElementById('input-pr-merge-message');
    const commitTitle = titleInput?.value.trim() || undefined;
    const commitMessage = msgInput?.value.trim() || undefined;

    if (!prNumber) {
      showToast('Invalid PR number', 'error');
      return;
    }

    try {
      showToast(`Merging PR #${prNumber}...`, 'info');
      const res = await callBridge('merge_github_pull_request', {
        repoPath: repo.path,
        prNumber,
        mergeMethod,
        commitTitle,
        commitMessage
      });

      if (res && res.success) {
        showToast(`PR #${prNumber} merged successfully!`, 'success');
        closePrMergeModal();
        loadGitHubPullRequests(repo.path);
        refreshSelectedRepoMetadata();
      } else {
        showToast(`Merge failed: ${res?.message || 'Error occurred'}`, 'error', 6000);
      }
    } catch (e) {
      showToast('Error merging PR: ' + e.message, 'error');
    }
  }

  async function triggerRepoScan() {
    try {
      showToast('Starting repository discovery scan...', 'info');
      await callBridge('scan_repositories');
      repoState.isScanning = true;
      updateScanProgressIndicator();
    } catch (e) {
      showToast('Scan failed: ' + e.message, 'error');
    }
  }

  async function cancelRepoScan() {
    try {
      showToast('Cancelling scan...', 'warn');
      await callBridge('cancel_repo_scan');
    } catch (e) { }
    finally {
      setTimeout(() => {
        repoState.isScanning = false;
        updateScanProgressIndicator();
      }, 500);
    }
  }

  async function toggleRepoPinned(repoId, pinned) {
    try {
      await callBridge('update_repo_metadata', { id: repoId, pinned });
      const r = repoState.repositories.find(item => item.id === repoId);
      if (r) r.pinned = pinned;
      renderRepositoriesList();
    } catch (e) { }
  }

  async function toggleWorkingProjectFocus() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const newTarget = repoState.workingProjectId === repo.id ? null : repo.id;
    try {
      await callBridge('set_working_project', { projectId: newTarget });
      repoState.workingProjectId = newTarget;
      renderWorkingProjectBanner();
      selectRepository(repo.id);
      showToast(newTarget ? `Working on: ${repo.name}` : 'Cleared working project focus', 'info');
    } catch (e) { }
  }

  async function updateSelectedRepoStatus(e) {
    const repo = getSelectedRepo();
    if (!repo) return;

    const newStatus = e.target.value;
    try {
      await callBridge('update_repo_metadata', { id: repo.id, status: newStatus });
      repo.status = newStatus;
      showToast(`Status updated to ${newStatus}`, 'success');
    } catch (e) { }
  }

  function copySelectedRepoPath() {
    const repo = getSelectedRepo();
    if (repo && navigator.clipboard) {
      navigator.clipboard.writeText(repo.path);
      showToast('Path copied to clipboard', 'info');
    }
  }

  async function openInExternal(target, explicitRepo) {
    const repo = explicitRepo || getSelectedRepo();
    if (!repo) return;

    const editorPref = repoState.scanSettings?.preferredEditor || 'ask';
    if ((!target || target === 'editor') && editorPref === 'ask') {
      openEditorPickerModal(repo);
      return;
    }

    try {
      const res = await callBridge('open_repo_in_editor', { repoPath: repo.path, target: target || 'editor' });
      if (res && res.message) {
        showToast(res.message, res.success !== false ? 'success' : 'error');
      }
    } catch (e) {
      showToast('Failed to open: ' + e.message, 'error');
    }
  }

  // --- External Editor Picker Modal ---
  let activeEditorPickerRepo = null;

  async function openEditorPickerModal(repo) {
    activeEditorPickerRepo = repo || getSelectedRepo();
    if (!activeEditorPickerRepo) return;

    const modal = document.getElementById('modal-repo-editor');
    const repoNameEl = document.getElementById('editor-modal-repo-name');
    const container = document.getElementById('editor-options-container');
    const chkRemember = document.getElementById('chk-remember-editor-choice');

    if (!modal || !container) return;

    if (repoNameEl) repoNameEl.textContent = activeEditorPickerRepo.name;
    if (chkRemember) chkRemember.checked = false;

    container.innerHTML = `<div class="empty-state" style="padding: 24px; text-align: center; font-size: 11px;">Scanning for installed code editors...</div>`;
    modal.classList.add('active');

    try {
      const res = await callBridge('get_installed_editors');
      const editors = (res && res.editors) ? res.editors : [];
      const currentPreferred = res?.preferred || repoState.scanSettings?.preferredEditor || 'ask';

      container.innerHTML = '';
      if (editors.length === 0) {
        container.innerHTML = `<div class="empty-state" style="padding: 24px; text-align: center; color: var(--text-dim); font-size: 11px;">No editor definitions found.</div>`;
        return;
      }

      editors.forEach(ed => {
        const tile = document.createElement('div');
        tile.className = 'editor-option-tile';
        if (ed.id === currentPreferred && currentPreferred !== 'ask') {
          tile.classList.add('selected');
        }

        const iconSvg = getEditorSvg(ed.id);
        const badgeClass = ed.isDetected ? 'detected' : '';
        const badgeText = ed.isDetected ? 'Detected' : 'Not Detected';
        const sourceBadge = ed.source === 'devtool'
          ? `<span class="editor-source-tag" style="display: inline-block; font-size: 9px; padding: 1px 5px; border-radius: 4px; background: rgba(56, 189, 248, 0.12); color: #38bdf8; font-weight: 600; margin-left: 6px; vertical-align: middle;">AI Dev Tool</span>`
          : '';

        tile.innerHTML = `
          <div class="editor-tile-left">
            <div class="editor-icon-wrap">${iconSvg}</div>
            <div class="editor-info">
              <span class="editor-title">${escapeHtml(ed.name)}${sourceBadge}</span>
              <span class="editor-desc">${escapeHtml(ed.description)}</span>
            </div>
          </div>
          <span class="editor-badge ${badgeClass}">${badgeText}</span>
        `;

        tile.addEventListener('click', () => {
          launchSelectedEditor(activeEditorPickerRepo, ed.id, !!chkRemember?.checked);
        });

        container.appendChild(tile);
      });
    } catch (err) {
      container.innerHTML = `<div class="empty-state" style="padding: 24px; text-align: center; color: #ef4444; font-size: 11px;">Failed to scan editors: ${escapeHtml(err.message)}</div>`;
    }
  }

  function closeEditorPickerModal() {
    document.getElementById('modal-repo-editor')?.classList.remove('active');
    activeEditorPickerRepo = null;
  }

  async function launchSelectedEditor(repo, editorId, rememberChoice) {
    closeEditorPickerModal();
    if (!repo) return;

    if (rememberChoice) {
      if (!repoState.scanSettings) repoState.scanSettings = {};
      repoState.scanSettings.preferredEditor = editorId;
      try {
        await callBridge('save_repo_settings', { settings: repoState.scanSettings });
        showToast(`Default editor set to ${editorId}`, 'info');
      } catch (e) {
        console.warn('Failed to remember editor choice:', e);
      }
    }

    try {
      showToast(`Opening ${repo.name} in ${editorId}...`, 'info');
      const res = await callBridge('open_repo_in_editor', { repoPath: repo.path, target: editorId });
      if (res && res.message) {
        showToast(res.message, res.success !== false ? 'success' : 'error');
      }
    } catch (e) {
      showToast('Failed to open: ' + e.message, 'error');
    }
  }

  function getEditorSvg(id) {
    const key = (id || '').toLowerCase();
    if (key.includes('cursor')) {
      return `<svg class="icon-svg" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"/></svg>`;
    }
    if (key.includes('antigravity')) {
      return `<svg class="icon-svg" viewBox="0 0 24 24"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>`;
    }
    if (key.includes('code') || key.includes('codium') || key.includes('vsc')) {
      return `<svg class="icon-svg" viewBox="0 0 24 24"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>`;
    }
    if (key.includes('devin') || key.includes('workbuddy')) {
      return `<svg class="icon-svg" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><path d="m10 15 5-3-5-3v6z"/></svg>`;
    }
    if (key.includes('rider') || key.includes('idea') || key.includes('pycharm') || key.includes('webstorm') || key.includes('jetbrains')) {
      return `<svg class="icon-svg" viewBox="0 0 24 24"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><path d="M8 16V8l4 8V8"/><line x1="14" y1="16" x2="18" y2="16"/></svg>`;
    }
    if (key.includes('devenv') || key.includes('visual studio')) {
      return `<svg class="icon-svg" viewBox="0 0 24 24"><path d="m18 3 4 3v12l-4 3L8 14l-6 5V5l6 5 10-7z"/></svg>`;
    }
    if (key.includes('sublime') || key.includes('notepad')) {
      return `<svg class="icon-svg" viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/></svg>`;
    }
    if (key.includes('explorer')) {
      return `<svg class="icon-svg" viewBox="0 0 24 24"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>`;
    }
    return `<svg class="icon-svg" viewBox="0 0 24 24"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="m9 9 3 3-3 3"/><line x1="13" y1="15" x2="15" y2="15"/></svg>`;
  }

  // --- Wide Changes View & Standalone Changes Modal ---
  let isWideChangesActive = false;

  function toggleWideChanges(forceState) {
    const container = document.querySelector('.repo-split-container');
    const lbl = document.getElementById('lbl-wide-changes');
    const btn = document.getElementById('btn-toggle-wide-changes');
    const headerExitBtn = document.getElementById('btn-exit-wide-mode-header');
    if (!container) return;

    if (typeof forceState === 'boolean') {
      isWideChangesActive = forceState;
    } else {
      isWideChangesActive = !isWideChangesActive;
    }

    container.classList.toggle('wide-mode', isWideChangesActive);

    if (lbl) {
      lbl.textContent = isWideChangesActive ? 'Exit Wide View' : 'Wide View';
    }
    if (btn) {
      btn.classList.toggle('btn-primary', isWideChangesActive);
      btn.classList.toggle('btn-outline', !isWideChangesActive);
    }
    if (headerExitBtn) {
      headerExitBtn.style.display = isWideChangesActive ? 'inline-flex' : 'none';
    }
  }

  const modalRepoState = {
    repoPath: null,
    changes: [],
    checkedFiles: new Set(),
    selectedFile: null
  };

  function openGitChangesModal() {
    const repo = getSelectedRepo();
    if (!repo) {
      showToast('Select a repository first', 'warn');
      return;
    }

    modalRepoState.repoPath = repo.path;
    const modal = document.getElementById('modal-git-changes');
    const nameEl = document.getElementById('modal-changes-repo-name');
    const branchEl = document.getElementById('modal-changes-branch');

    if (!modal) return;
    if (nameEl) nameEl.textContent = repo.name;
    if (branchEl) branchEl.textContent = repo.branch || 'detached';

    modal.classList.add('active');
    loadModalRepoChanges(repo.path);
  }

  function closeGitChangesModal() {
    document.getElementById('modal-git-changes')?.classList.remove('active');
  }

  function toggleModalChangesMaximized() {
    document.getElementById('modal-changes-dialog')?.classList.toggle('is-maximized');
  }

  async function loadModalRepoChanges(repoPath) {
    const listEl = document.getElementById('modal-changes-file-list');
    const countEl = document.getElementById('modal-changes-file-count');
    const diffBox = document.getElementById('modal-diff-content-box');
    const diffTitle = document.getElementById('modal-diff-selected-file');
    const diffStats = document.getElementById('modal-diff-stats-pill');

    if (!listEl) return;

    listEl.innerHTML = `<div class="empty-state" style="padding: 20px; font-size: 11px; text-align: center;">Inspecting working tree...</div>`;
    modalRepoState.changes = [];
    modalRepoState.checkedFiles.clear();

    try {
      const res = await callBridge('get_repo_changes', { repoPath });
      const changes = (res && res.changes) ? res.changes : [];
      modalRepoState.changes = changes;

      if (countEl) countEl.textContent = String(changes.length);

      if (changes.length === 0) {
        listEl.innerHTML = `<div class="empty-state" style="padding: 24px; font-size: 11px; text-align: center; color: var(--text-dim);">Working tree is clean.</div>`;
        if (diffBox) diffBox.textContent = 'No differences in working tree.';
        if (diffTitle) diffTitle.textContent = 'Clean working tree';
        if (diffStats) diffStats.textContent = '';
        return;
      }

      listEl.innerHTML = '';
      changes.forEach((ch, idx) => {
        const item = document.createElement('div');
        item.className = 'changes-file-item';
        item.dataset.index = idx;

        const isStaged = ch.isStaged;
        const statusLetter = isStaged ? ch.indexStatus : ch.worktreeStatus;
        const badgeColor = statusLetter === '?' ? '#06b6d4' : statusLetter === 'M' ? '#f59e0b' : statusLetter === 'A' ? '#10b981' : '#ef4444';

        item.innerHTML = `
          <div style="display: flex; align-items: center; gap: 7px; overflow: hidden;">
            <input type="checkbox" class="chk-modal-file" data-path="${escapeHtml(ch.path)}" />
            <span style="font-size: 10px; font-weight: 700; color: ${badgeColor}; width: 12px; text-align: center;">${escapeHtml(statusLetter)}</span>
            <span style="white-space: nowrap; overflow: hidden; text-overflow: ellipsis;" title="${escapeHtml(ch.path)}">${escapeHtml(ch.path)}</span>
          </div>
          <span style="font-size: 9.5px; color: var(--text-dim); text-transform: uppercase;">${isStaged ? 'Staged' : 'Unstaged'}</span>
        `;

        item.addEventListener('click', (e) => {
          if (e.target.classList.contains('chk-modal-file')) return;
          listEl.querySelectorAll('.changes-file-item').forEach(el => el.classList.remove('selected'));
          item.classList.add('selected');
          fetchAndShowModalDiff(repoPath, ch.path, isStaged);
        });

        const chk = item.querySelector('.chk-modal-file');
        chk.addEventListener('change', (e) => {
          if (e.target.checked) modalRepoState.checkedFiles.add(ch.path);
          else modalRepoState.checkedFiles.delete(ch.path);
        });

        listEl.appendChild(item);
      });

      if (changes.length > 0) {
        listEl.children[0].classList.add('selected');
        fetchAndShowModalDiff(repoPath, changes[0].path, changes[0].isStaged);
      }
    } catch (err) {
      listEl.innerHTML = `<div class="empty-state" style="padding: 24px; font-size: 11px; text-align: center; color: #ef4444;">Failed to read working tree.</div>`;
    }
  }

  async function fetchAndShowModalDiff(repoPath, relativePath, staged) {
    const diffBox = document.getElementById('modal-diff-content-box');
    const diffTitle = document.getElementById('modal-diff-selected-file');
    const diffStats = document.getElementById('modal-diff-stats-pill');

    if (!diffBox) return;
    if (diffTitle) diffTitle.textContent = relativePath;
    if (diffStats) diffStats.textContent = 'Loading diff...';
    diffBox.textContent = 'Loading unified diff...';

    try {
      const res = await callBridge('get_repo_diff', { repoPath, relativePath, staged });
      if (res) {
        if (diffStats) diffStats.textContent = `+${res.additions || 0} -${res.deletions || 0}`;
        renderDiffText(diffBox, res.diffContent || 'No changes in this view.');
      }
    } catch (err) {
      diffBox.textContent = 'Error loading diff: ' + err.message;
    }
  }

  async function stageCheckedModalFiles(unstage) {
    if (!modalRepoState.repoPath) return;
    const files = Array.from(modalRepoState.checkedFiles);
    if (files.length === 0) {
      showToast('Select files via checkboxes first', 'warn');
      return;
    }

    try {
      const res = await callBridge('stage_repo_changes', {
        repoPath: modalRepoState.repoPath,
        files,
        unstage
      });

      if (res && res.success) {
        showToast(unstage ? 'Files unstaged' : 'Files staged', 'success');
        await loadModalRepoChanges(modalRepoState.repoPath);
        loadRepoChanges(modalRepoState.repoPath);
        refreshSelectedRepoMetadata();
      } else {
        showToast('Staging failed: ' + (res?.output || 'Unknown error'), 'error');
      }
    } catch (e) {
      showToast('Error staging changes: ' + e.message, 'error');
    }
  }

  async function stageAllModalFiles() {
    if (!modalRepoState.repoPath) return;
    const allPaths = modalRepoState.changes.map(c => c.path);
    if (allPaths.length === 0) return;

    try {
      const res = await callBridge('stage_repo_changes', {
        repoPath: modalRepoState.repoPath,
        files: allPaths,
        unstage: false
      });

      if (res && res.success) {
        showToast('All files staged', 'success');
        await loadModalRepoChanges(modalRepoState.repoPath);
        loadRepoChanges(modalRepoState.repoPath);
        refreshSelectedRepoMetadata();
      } else {
        showToast('Error staging all: ' + (res?.output || 'Unknown error'), 'error');
      }
    } catch (e) {
      showToast('Error staging all: ' + e.message, 'error');
    }
  }

  async function createModalCommit() {
    if (!modalRepoState.repoPath) return;
    const msgInput = document.getElementById('modal-repo-commit-message');
    const stageAllChk = document.getElementById('modal-chk-stage-all-commit');
    const message = msgInput?.value.trim();

    if (!message) {
      showToast('Please enter a commit message', 'warn');
      msgInput?.focus();
      return;
    }

    const pushToRemote = repoState.commitAction === 'push';
    if (pushToRemote) {
      showToast('Creating commit and pushing to live GitHub...', 'info');
    }

    try {
      const res = await callBridge('create_repo_commit', {
        repoPath: modalRepoState.repoPath,
        message,
        stageAll: !!stageAllChk?.checked,
        pushToRemote
      });

      if (res && res.success) {
        if (pushToRemote) {
          if (res.pushed) {
            showToast('Committed & pushed live to origin successfully!', 'success', 5000);
          } else {
            showToast(`Commit saved locally, but live push failed: ${res.pushOutput || res.output}`, 'warn', 7000);
          }
        } else {
          showToast('Local commit created successfully', 'success');
        }

        if (msgInput) msgInput.value = '';
        if (stageAllChk) stageAllChk.checked = false;
        await loadModalRepoChanges(modalRepoState.repoPath);
        loadRepoChanges(modalRepoState.repoPath);
        refreshSelectedRepoMetadata();
      } else {
        showToast('Commit failed: ' + (res?.output || 'Unknown error'), 'error');
      }
    } catch (e) {
      showToast('Error creating commit: ' + e.message, 'error');
    }
  }

  function launchAgentInSelectedRepo() {
    const repo = getSelectedRepo();
    if (repo) {
      const cli = repoState.scanSettings.defaultAgent || 'claude';
      callBridge('launch_agent_in_repo', { repoPath: repo.path, cliType: cli });
      showToast(`Launching ${cli} in ${repo.name}...`, 'info');
    }
  }

  function refreshSelectedRepoMetadata() {
    const repo = getSelectedRepo();
    if (repo) {
      callBridge('get_repo_manager_state').then(res => {
        if (res && res.repositories) {
          repoState.repositories = res.repositories;
          renderRepoMetrics();
          renderRepositoriesList();
          selectRepository(repo.id);
        }
      });
    }
  }

  // --- Modals Setup ---
  function setupRepoModals() {
    // Scan Folders Settings Modal
    document.getElementById('btn-close-repo-settings')?.addEventListener('click', closeRepoSettingsModal);
    document.getElementById('btn-cancel-repo-settings')?.addEventListener('click', closeRepoSettingsModal);
    document.getElementById('btn-save-repo-settings')?.addEventListener('click', saveRepoSettings);
    document.getElementById('btn-add-scan-root')?.addEventListener('click', addScanRootFromInput);
    document.getElementById('select-preferred-editor')?.addEventListener('change', (e) => {
      const wrap = document.getElementById('wrap-custom-editor');
      if (wrap) wrap.style.display = e.target.value === 'custom' ? 'flex' : 'none';
    });
    document.getElementById('btn-browse-custom-editor')?.addEventListener('click', async () => {
      try {
        const res = await callBridge('browse_editor_path');
        if (res && res.path) {
          const inp = document.getElementById('input-custom-editor-path');
          if (inp) inp.value = res.path;
        }
      } catch { }
    });

    // Network Sync Modal
    document.getElementById('btn-cancel-net-modal')?.addEventListener('click', closeGitNetworkModal);
    document.getElementById('btn-confirm-net-modal')?.addEventListener('click', executeGitNetworkAction);

    // Inventory Issues Modal
    document.getElementById('btn-close-repo-inventory')?.addEventListener('click', closeRepoIssuesModal);
    document.getElementById('btn-close-inventory-issues')?.addEventListener('click', closeRepoIssuesModal);

    // Custom Launcher Modal
    document.getElementById('btn-close-launcher-modal')?.addEventListener('click', closeCustomLauncherModal);
    document.getElementById('btn-cancel-launcher-modal')?.addEventListener('click', closeCustomLauncherModal);
    document.getElementById('btn-save-launcher-modal')?.addEventListener('click', saveCustomLauncher);

    // External Editor Picker Modal
    document.getElementById('btn-close-editor-modal')?.addEventListener('click', closeEditorPickerModal);
    document.getElementById('btn-cancel-editor-modal')?.addEventListener('click', closeEditorPickerModal);

    // Git Changes Large Modal
    document.getElementById('btn-close-changes-modal')?.addEventListener('click', closeGitChangesModal);
    document.getElementById('btn-cancel-changes-modal')?.addEventListener('click', closeGitChangesModal);
    document.getElementById('btn-toggle-modal-changes-max')?.addEventListener('click', toggleModalChangesMaximized);
    document.getElementById('btn-modal-refresh-changes')?.addEventListener('click', () => {
      if (modalRepoState.repoPath) loadModalRepoChanges(modalRepoState.repoPath);
    });
    document.getElementById('btn-modal-stage-selected')?.addEventListener('click', () => stageCheckedModalFiles(false));
    document.getElementById('btn-modal-unstage-selected')?.addEventListener('click', () => stageCheckedModalFiles(true));
    document.getElementById('btn-modal-stage-all')?.addEventListener('click', stageAllModalFiles);
    document.getElementById('btn-modal-create-commit')?.addEventListener('click', createModalCommit);

    // Git Worktree Modal
    document.getElementById('btn-close-worktree-modal')?.addEventListener('click', closeAddWorktreeModal);
    document.getElementById('btn-cancel-worktree-modal')?.addEventListener('click', closeAddWorktreeModal);
    document.getElementById('btn-submit-worktree-modal')?.addEventListener('click', submitAddWorktreeModal);
    document.getElementById('chk-worktree-create-branch')?.addEventListener('change', (e) => {
      const wrap = document.getElementById('wrap-new-branch-name');
      if (wrap) wrap.style.display = e.target.checked ? 'block' : 'none';
    });
    document.getElementById('btn-browse-worktree-path')?.addEventListener('click', async () => {
      try {
        const res = await callBridge('browse_editor_path');
        if (res && res.path) {
          const inp = document.getElementById('input-worktree-path');
          if (inp) inp.value = res.path;
        }
      } catch { }
    });

    // Git Stash Modal
    document.getElementById('btn-close-stash-modal')?.addEventListener('click', closeCreateStashModal);
    document.getElementById('btn-cancel-stash-modal')?.addEventListener('click', closeCreateStashModal);
    document.getElementById('btn-submit-stash-modal')?.addEventListener('click', submitCreateStashModal);

    // GitHub Authentication in Settings
    document.getElementById('btn-auto-connect-gh')?.addEventListener('click', autoConnectGitHubFromGit);
    document.getElementById('btn-open-gh-token-gen')?.addEventListener('click', () => {
      callBridge('open_url_external', {
        url: 'https://github.com/settings/tokens/new?description=AgentsHub&scopes=repo,read:user,workflow'
      });
    });
    document.getElementById('btn-toggle-gh-guide')?.addEventListener('click', () => {
      const guide = document.getElementById('wrap-gh-guide');
      if (guide) {
        guide.style.display = guide.style.display === 'none' ? 'block' : 'none';
      }
    });
    document.getElementById('btn-save-gh-pat')?.addEventListener('click', saveGitHubPatFromInput);
    document.getElementById('btn-gh-disconnect')?.addEventListener('click', disconnectGitHubAccount);

    // GitHub Publish Modal
    document.getElementById('btn-close-gh-publish')?.addEventListener('click', closeGitHubPublishModal);
    document.getElementById('btn-cancel-gh-publish')?.addEventListener('click', closeGitHubPublishModal);
    document.getElementById('btn-submit-gh-publish')?.addEventListener('click', submitGitHubPublish);
  }

  function openRepoSettingsModal() {
    const modal = document.getElementById('modal-repo-settings');
    const rootsList = document.getElementById('repo-roots-list');
    const depthSelect = document.getElementById('select-scan-depth');
    const agentSelect = document.getElementById('select-default-agent');
    const skipInput = document.getElementById('input-skip-dirs');
    const editorSelect = document.getElementById('select-preferred-editor');
    const customEditorWrap = document.getElementById('wrap-custom-editor');
    const customEditorInput = document.getElementById('input-custom-editor-path');

    if (!modal) return;

    loadGitHubAuthStatus();

    if (depthSelect) depthSelect.value = String(repoState.scanSettings.depth || 4);
    if (agentSelect) agentSelect.value = repoState.scanSettings.defaultAgent || 'claude';
    if (skipInput) skipInput.value = (repoState.scanSettings.skipDirs || []).join(', ');

    if (editorSelect) {
      editorSelect.value = repoState.scanSettings.preferredEditor || 'ask';
      if (customEditorWrap) {
        customEditorWrap.style.display = editorSelect.value === 'custom' ? 'flex' : 'none';
      }
    }
    if (customEditorInput) {
      customEditorInput.value = repoState.scanSettings.customEditorPath || '';
    }

    renderRootsList();
    modal.classList.add('active');
  }

  function closeRepoSettingsModal() {
    document.getElementById('modal-repo-settings')?.classList.remove('active');
  }

  function renderRootsList() {
    const container = document.getElementById('repo-roots-list');
    if (!container) return;

    container.innerHTML = '';
    const roots = repoState.scanSettings.roots || [];

    if (roots.length === 0) {
      container.innerHTML = `<div style="font-size: 11px; color: var(--text-dim); padding: 8px;">No folders added yet.</div>`;
      return;
    }

    roots.forEach((root, idx) => {
      const row = document.createElement('div');
      row.style.cssText = 'display: flex; justify-content: space-between; align-items: center; padding: 4px 6px; font-size: 11.5px; font-family: var(--font-mono); border-bottom: 1px solid var(--border-subtle);';
      row.innerHTML = `
        <span style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap;" title="${escapeHtml(root)}">${escapeHtml(root)}</span>
        <button class="btn-icon btn-sm" style="color: #ef4444;" title="Remove folder">&times;</button>
      `;
      row.querySelector('button').addEventListener('click', () => {
        repoState.scanSettings.roots.splice(idx, 1);
        renderRootsList();
      });
      container.appendChild(row);
    });
  }

  function addScanRootFromInput() {
    const input = document.getElementById('input-new-scan-root');
    const val = input?.value.trim();
    if (!val) return;

    if (!repoState.scanSettings.roots) repoState.scanSettings.roots = [];
    if (!repoState.scanSettings.roots.includes(val)) {
      repoState.scanSettings.roots.push(val);
      renderRootsList();
      input.value = '';
    }
  }

  async function saveRepoSettings() {
    const depthSelect = document.getElementById('select-scan-depth');
    const agentSelect = document.getElementById('select-default-agent');
    const skipInput = document.getElementById('input-skip-dirs');
    const editorSelect = document.getElementById('select-preferred-editor');
    const customEditorInput = document.getElementById('input-custom-editor-path');

    const depth = parseInt(depthSelect?.value || '4', 10);
    const defaultAgent = agentSelect?.value || 'claude';
    const preferredEditor = editorSelect?.value || 'ask';
    const customEditorPath = customEditorInput?.value?.trim() || '';
    const skipDirs = (skipInput?.value || '')
      .split(',')
      .map(s => s.trim())
      .filter(s => s.length > 0);

    const newSettings = {
      roots: repoState.scanSettings.roots || [],
      depth,
      defaultAgent,
      preferredEditor,
      customEditorPath,
      skipDirs,
      ignoredHealthRules: repoState.scanSettings.ignoredHealthRules || []
    };

    try {
      await callBridge('save_repo_settings', { settings: newSettings });
      repoState.scanSettings = newSettings;
      closeRepoSettingsModal();
      showToast('Settings saved. Rescanning repositories...', 'success');
      triggerRepoScan();
    } catch (e) {
      showToast('Failed to save settings: ' + e.message, 'error');
    }
  }

  let currentNetworkOperation = 'fetch';

  async function openGitNetworkModal(operation) {
    const repo = getSelectedRepo();
    if (!repo) return;

    currentNetworkOperation = operation;
    const modal = document.getElementById('modal-git-network');
    const titleEl = document.getElementById('modal-net-title');
    const nameEl = document.getElementById('modal-net-repo-name');
    const descEl = document.getElementById('modal-net-desc');
    const remoteSelect = document.getElementById('modal-net-remote-select');
    const branchInput = document.getElementById('modal-net-branch-input');
    const upstreamGroup = document.getElementById('group-net-upstream');
    const branchGroup = document.getElementById('group-net-branch');
    const confirmBtnLbl = document.getElementById('lbl-confirm-net-btn');

    if (!modal) return;

    if (titleEl) titleEl.textContent = `Git ${operation.toUpperCase()} Confirmation`;
    if (nameEl) nameEl.textContent = repo.name;
    if (confirmBtnLbl) confirmBtnLbl.textContent = `Execute ${operation.toUpperCase()}`;

    if (descEl) {
      descEl.textContent = operation === 'push'
        ? `Push local commits from '${repo.branch || 'HEAD'}' to remote.`
        : operation === 'pull'
        ? `Fast-forward pull latest changes into '${repo.branch || 'HEAD'}'.`
        : `Fetch updated refs from remote without touching working files.`;
    }

    if (branchGroup) branchGroup.style.display = operation === 'fetch' ? 'none' : 'block';
    if (upstreamGroup) upstreamGroup.style.display = operation === 'push' ? 'block' : 'none';

    if (branchInput) branchInput.value = repo.branch || 'main';

    if (remoteSelect) {
      remoteSelect.innerHTML = '';
      (repo.remotes || [{ name: 'origin' }]).forEach(rem => {
        const opt = document.createElement('option');
        opt.value = rem.name;
        opt.textContent = `${rem.name} (${rem.fetchUrl || 'default'})`;
        remoteSelect.appendChild(opt);
      });
    }

    modal.classList.add('active');
  }

  function closeGitNetworkModal() {
    document.getElementById('modal-git-network')?.classList.remove('active');
  }

  async function executeGitNetworkAction() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const remoteSelect = document.getElementById('modal-net-remote-select');
    const branchInput = document.getElementById('modal-net-branch-input');
    const chkUpstream = document.getElementById('chk-net-set-upstream');

    const remote = remoteSelect?.value || 'origin';
    const branch = branchInput?.value.trim() || null;
    const setUpstream = !!chkUpstream?.checked;

    closeGitNetworkModal();
    showToast(`Executing git ${currentNetworkOperation}...`, 'info');

    try {
      const res = await callBridge('repo_network_action', {
        repoPath: repo.path,
        operation: currentNetworkOperation,
        remote,
        branch,
        setUpstream
      });

      if (res && res.success) {
        showToast(`Git ${currentNetworkOperation} completed successfully`, 'success');
        refreshSelectedRepoMetadata();
      } else {
        showToast(`Git ${currentNetworkOperation} failed: ${res?.output || 'Unknown error'}`, 'error');
      }
    } catch (e) {
      showToast('Error: ' + e.message, 'error');
    }
  }

  function openRepoIssuesModal() {
    const modal = document.getElementById('modal-repo-inventory');
    const listEl = document.getElementById('repo-issues-list');
    if (!modal || !listEl) return;

    const broken = repoState.repositories.filter(r => r.broken || !r.path);
    if (broken.length === 0) {
      listEl.innerHTML = `<div class="empty-state" style="padding: 30px; text-align: center; color: var(--accent-green);">No inventory issues detected. All repositories are healthy and accessible.</div>`;
    } else {
      listEl.innerHTML = '';
      broken.forEach(b => {
        const item = document.createElement('div');
        item.style.cssText = 'background: var(--bg-input); border: 1px solid rgba(239, 68, 68, 0.25); border-left: 3px solid #ef4444; border-radius: 6px; padding: 10px 12px; font-size: 11.5px;';
        item.innerHTML = `
          <div style="font-weight: 700; color: var(--text-main);">${escapeHtml(b.name)}</div>
          <div style="font-family: var(--font-mono); color: var(--text-dim); margin-top: 2px;">${escapeHtml(b.path)}</div>
          <div style="color: #ef4444; font-size: 11px; margin-top: 4px;">Repository marker corrupt or folder inaccessible.</div>
        `;
        listEl.appendChild(item);
      });
    }

    modal.classList.add('active');
  }

  function closeRepoIssuesModal() {
    document.getElementById('modal-repo-inventory')?.classList.remove('active');
  }

  function openCustomLauncherModal() {
    document.getElementById('modal-repo-launcher')?.classList.add('active');
  }

  function closeCustomLauncherModal() {
    document.getElementById('modal-repo-launcher')?.classList.remove('active');
  }

  function saveCustomLauncher() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const labelInput = document.getElementById('input-launcher-label');
    const cmdInput = document.getElementById('input-launcher-cmd');
    const argsInput = document.getElementById('input-launcher-args');

    const label = labelInput?.value.trim();
    const command = cmdInput?.value.trim();
    const args = (argsInput?.value.trim() || '').split(' ').filter(a => a.length > 0);

    if (!label || !command) {
      showToast('Label and command are required', 'warn');
      return;
    }

    const launcher = {
      label,
      type: 'custom',
      command,
      args,
      cwd: repo.path,
      source: 'custom',
      priority: 50
    };

    executeLauncherItem(repo.path, launcher);
    closeCustomLauncherModal();
  }

  async function exportRepoInventory() {
    try {
      const res = await callBridge('export_repo_inventory', { format: 'markdown' });
      if (res && res.content && navigator.clipboard) {
        await navigator.clipboard.writeText(res.content);
        showToast('Repository inventory copied to clipboard (Markdown format)', 'success');
      }
    } catch (e) {
      showToast('Failed to export inventory: ' + e.message, 'error');
    }
  }

  // =========================================================================
  // GitHub Integration & Split Commit Preferences
  // =========================================================================
  function setCommitActionPreference(action) {
    repoState.commitAction = action || 'local';
    try { localStorage.setItem('repo_commit_action', repoState.commitAction); } catch { }

    const isPush = repoState.commitAction === 'push';
    const label = isPush ? 'Commit & Push Live' : 'Create Local Commit';

    const lblMain = document.getElementById('lbl-commit-action');
    const lblModal = document.getElementById('lbl-modal-commit-action');
    if (lblMain) lblMain.textContent = label;
    if (lblModal) lblModal.textContent = label;

    document.querySelectorAll('.commit-dropdown-item').forEach(item => {
      item.classList.toggle('active', item.dataset.action === repoState.commitAction);
    });

    const menuMain = document.getElementById('menu-commit-action');
    const menuModal = document.getElementById('menu-modal-commit-action');
    if (menuMain) menuMain.style.display = 'none';
    if (menuModal) menuModal.style.display = 'none';
  }

  async function loadGitHubAuthStatus() {
    try {
      const res = await callBridge('get_github_auth_status');
      if (res) {
        repoState.gitHubAuth = res;
        renderGitHubSettingsStatus(res);
        renderGitHubPublishModalAccount(res);
      }
    } catch (e) {
      console.warn('[Repositories] Failed to get GitHub auth status:', e);
    }
  }

  function renderGitHubSettingsStatus(auth) {
    const badge = document.getElementById('gh-settings-status-badge');
    const userCard = document.getElementById('gh-settings-user-card');
    const unconnectedWrap = document.getElementById('gh-settings-unconnected-wrap');
    const avatar = document.getElementById('gh-settings-avatar');
    const usernameEl = document.getElementById('gh-settings-username');
    const methodEl = document.getElementById('gh-settings-method');

    if (!badge) return;

    if (auth && auth.isAuthenticated) {
      const methodLabel = auth.authMethod === 'gcm'
        ? 'Git Credential Manager'
        : (auth.authMethod === 'cli' ? 'GitHub CLI' : 'Personal Access Token');

      badge.textContent = `Connected (${methodLabel})`;
      badge.style.background = 'rgba(16, 185, 129, 0.15)';
      badge.style.color = '#34d399';

      if (userCard) userCard.style.display = 'flex';
      if (unconnectedWrap) unconnectedWrap.style.display = 'none';

      if (usernameEl) usernameEl.textContent = `@${auth.username || 'User'}` + (auth.name ? ` (${auth.name})` : '');
      if (methodEl) methodEl.textContent = `Authenticated via ${methodLabel}`;
      if (avatar && auth.avatarUrl) {
        avatar.src = auth.avatarUrl;
        avatar.style.display = 'block';
      }
    } else {
      badge.textContent = 'Not connected';
      badge.style.background = 'rgba(255, 255, 255, 0.05)';
      badge.style.color = 'var(--text-dim)';

      if (userCard) userCard.style.display = 'none';
      if (unconnectedWrap) unconnectedWrap.style.display = 'flex';
    }
  }

  function renderGitHubPublishModalAccount(auth) {
    const badge = document.getElementById('gh-auth-account-badge');
    if (!badge) return;

    if (auth && auth.isAuthenticated) {
      const methodLabel = auth.authMethod === 'gcm'
        ? 'Git Credential Manager'
        : (auth.authMethod === 'cli' ? 'GitHub CLI' : 'PAT');

      badge.innerHTML = `
        <svg class="icon-svg" style="width: 14px; height: 14px; color: #34d399;" viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"/></svg>
        <span>Publishing under <strong>@${escapeHtml(auth.username || 'User')}</strong> (${methodLabel})</span>
      `;
    } else {
      badge.innerHTML = `
        <svg class="icon-svg" style="width: 14px; height: 14px; color: #f59e0b;" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
        <span>No GitHub account linked. Configure in <strong>Folders &amp; Scope &gt; GitHub</strong> first.</span>
      `;
    }
  }

  async function autoConnectGitHubFromGit() {
    try {
      showToast('Searching for GitHub credentials in Windows Git...', 'info');
      const res = await callBridge('auto_detect_github_git_credentials');
      if (res && res.success) {
        showToast(res.message || 'Auto-connected to GitHub successfully!', 'success', 5000);
        await loadGitHubAuthStatus();
        await loadRepoManagerState();
      } else {
        showToast(res?.message || 'Could not auto-connect from Git. Try generating a token.', 'warn', 6000);
      }
    } catch (e) {
      showToast('Auto-connect error: ' + e.message, 'error');
    }
  }

  function openGitHubPublishModal() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const modal = document.getElementById('modal-github-publish');
    const nameInput = document.getElementById('input-gh-repo-name');
    const descInput = document.getElementById('input-gh-repo-desc');
    const radPublic = document.getElementById('rad-gh-public');

    if (!modal) return;

    if (nameInput) nameInput.value = repo.name || '';
    if (descInput) descInput.value = '';
    if (radPublic) radPublic.checked = true;

    loadGitHubAuthStatus();
    modal.classList.add('active');
  }

  function closeGitHubPublishModal() {
    document.getElementById('modal-github-publish')?.classList.remove('active');
  }

  async function submitGitHubPublish() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const nameInput = document.getElementById('input-gh-repo-name');
    const descInput = document.getElementById('input-gh-repo-desc');
    const radPrivate = document.getElementById('rad-gh-private');

    const repoName = nameInput?.value.trim();
    const description = descInput?.value.trim() || '';
    const isPrivate = !!radPrivate?.checked;

    if (!repoName) {
      showToast('Repository name is required', 'warn');
      nameInput?.focus();
      return;
    }

    try {
      showToast('Creating GitHub repository and pushing code...', 'info');
      const res = await callBridge('publish_repo_to_github', {
        repoPath: repo.path,
        repoName,
        description,
        isPrivate
      });

      if (res && res.success) {
        showToast('Repository published to GitHub successfully!', 'success', 6000);
        closeGitHubPublishModal();
        await loadRepoManagerState();
      } else {
        showToast(`Publish failed: ${res?.message || 'Unknown error'}`, 'error', 7000);
      }
    } catch (e) {
      showToast('Failed to publish: ' + e.message, 'error');
    }
  }

  async function saveGitHubPatFromInput() {
    const inp = document.getElementById('input-gh-settings-pat');
    const token = inp?.value.trim();
    if (!token) {
      showToast('Please enter a GitHub token', 'warn');
      return;
    }

    try {
      showToast('Validating token with GitHub...', 'info');
      const res = await callBridge('save_github_pat', { token });
      if (res && res.success) {
        showToast(res.message || 'GitHub account linked successfully', 'success');
        if (inp) inp.value = '';
        await loadGitHubAuthStatus();
      } else {
        showToast(`GitHub validation failed: ${res?.message || 'Invalid token'}`, 'error');
      }
    } catch (e) {
      showToast('Error saving token: ' + e.message, 'error');
    }
  }

  async function disconnectGitHubAccount() {
    if (!confirm('Disconnect linked GitHub account?')) return;
    try {
      await callBridge('disconnect_github');
      showToast('Disconnected GitHub account', 'info');
      await loadGitHubAuthStatus();
    } catch (e) {
      showToast('Error disconnecting: ' + e.message, 'error');
    }
  }

  // =========================================================================
  // Branch Switcher Popover (Option C)
  // =========================================================================
  async function toggleBranchSwitcherPopover() {
    const popover = document.getElementById('branch-switcher-popover');
    if (!popover) return;

    if (popover.style.display === 'block') {
      popover.style.display = 'none';
      repoState.isBranchPopoverOpen = false;
      return;
    }

    const repo = getSelectedRepo();
    if (!repo) {
      showToast('Select a repository first', 'warn');
      return;
    }

    popover.style.display = 'block';
    repoState.isBranchPopoverOpen = true;
    const searchInput = document.getElementById('input-branch-search');
    if (searchInput) {
      searchInput.value = '';
      setTimeout(() => searchInput.focus(), 50);
    }

    const listEl = document.getElementById('branch-popover-list');
    if (listEl) {
      listEl.innerHTML = '<div style="padding: 10px; font-size: 11px; text-align: center; color: var(--text-dim);"><span class="spinner" style="width: 12px; height: 12px;"></span> Loading branches...</div>';
    }

    try {
      const res = await callBridge('list_repo_branches', { repoPath: repo.path });
      repoState.repoBranches = (res && res.branches) ? res.branches : [];
      renderBranchPopoverList(repoState.repoBranches, repo.branch);
    } catch (e) {
      if (listEl) {
        listEl.innerHTML = `<div style="padding: 10px; font-size: 11px; text-align: center; color: #ef4444;">Failed to load branches: ${escapeHtml(e.message || String(e))}</div>`;
      }
    }
  }

  function renderBranchPopoverList(branches, currentBranch) {
    const listEl = document.getElementById('branch-popover-list');
    if (!listEl) return;

    if (!branches || branches.length === 0) {
      listEl.innerHTML = '<div style="padding: 10px; font-size: 11px; text-align: center; color: var(--text-dim);">No branches found.</div>';
      return;
    }

    listEl.innerHTML = '';
    branches.forEach(b => {
      const isCurrent = stringEquals(b, currentBranch);
      const isRemote = b.includes('/') || b.startsWith('origin/');
      const item = document.createElement('div');
      item.className = `branch-popover-item ${isCurrent ? 'current' : ''}`;
      item.innerHTML = `
        <svg class="icon-svg" style="width: 11px; height: 11px; color: ${isCurrent ? 'var(--accent-cyan)' : 'var(--text-dim)'};" viewBox="0 0 24 24"><circle cx="18" cy="18" r="3"/><circle cx="6" cy="6" r="3"/><path d="M13 6h3a2 2 0 0 1 2 2v7"/><line x1="6" y1="9" x2="6" y2="21"/></svg>
        <span class="branch-name" style="flex: 1; font-family: var(--font-mono); font-size: 11px; overflow: hidden; text-overflow: ellipsis;">${escapeHtml(b)}</span>
        ${isCurrent ? '<span class="branch-tag current" style="font-size: 9px; padding: 1px 4px; border-radius: 3px; background: rgba(6,182,212,0.15); color: var(--accent-cyan); font-weight: 700;">CURRENT</span>' : ''}
        ${isRemote ? '<span class="branch-tag" style="font-size: 9px; padding: 1px 4px; border-radius: 3px; background: rgba(255,255,255,0.06); color: var(--text-dim);">REMOTE</span>' : ''}
      `;

      if (!isCurrent) {
        item.style.cursor = 'pointer';
        item.addEventListener('click', () => switchBranchTo(b));
      }
      listEl.appendChild(item);
    });
  }

  function filterBranchList(query) {
    const q = (query || '').toLowerCase().trim();
    const currentBranch = getSelectedRepo()?.branch;
    const filtered = repoState.repoBranches.filter(b => b.toLowerCase().includes(q));
    renderBranchPopoverList(filtered, currentBranch);
  }

  async function switchBranchTo(branchName) {
    const repo = getSelectedRepo();
    if (!repo) return;

    const popover = document.getElementById('branch-switcher-popover');
    if (popover) popover.style.display = 'none';
    repoState.isBranchPopoverOpen = false;

    let target = branchName;
    if (target.startsWith('origin/')) target = target.substring(7);

    try {
      showToast(`Switching to branch '${target}'...`, 'info');
      const res = await callBridge('switch_repo_branch', { repoPath: repo.path, branch: target });
      if (res && res.success) {
        showToast(`Switched to branch '${target}' successfully`, 'success');
        await loadRepoManagerState();
        if (repoState.activeDetailTab === 'history') loadCommitGraph(repo.path);
        if (repoState.activeDetailTab === 'changes') loadRepoChanges(repo.path);
      } else {
        showToast(`Failed to switch branch: ${res?.output || 'Working directory may have uncommitted changes.'}`, 'error', 7000);
      }
    } catch (e) {
      showToast(`Error switching branch: ${e.message}`, 'error');
    }
  }

  function stringEquals(a, b) {
    return (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase();
  }

  // =========================================================================
  // Git Branch Merge & Conflict Preview Modal (Option C)
  // =========================================================================
  async function openGitMergeModal() {
    const repo = getSelectedRepo();
    if (!repo) {
      showToast('Select a repository first', 'warn');
      return;
    }

    const modal = document.getElementById('modal-git-merge');
    const repoNameEl = document.getElementById('lbl-merge-repo-name');
    const targetBranchEl = document.getElementById('lbl-merge-target-branch');
    const sourceSelect = document.getElementById('select-merge-source-branch');
    const customMsgInput = document.getElementById('input-merge-custom-msg');
    const executeBtn = document.getElementById('btn-execute-merge-modal');

    if (!modal) return;

    if (repoNameEl) repoNameEl.textContent = repo.name;
    if (targetBranchEl) targetBranchEl.textContent = repo.branch || 'main';
    if (customMsgInput) customMsgInput.value = '';
    if (executeBtn) executeBtn.disabled = true;

    if (sourceSelect) {
      sourceSelect.innerHTML = '<option value="">Loading branches...</option>';
      try {
        const res = await callBridge('list_repo_branches', { repoPath: repo.path });
        const branches = (res && res.branches) ? res.branches : [];
        repoState.repoBranches = branches;
        const otherBranches = branches.filter(b => !stringEquals(b, repo.branch));

        sourceSelect.innerHTML = '';
        if (otherBranches.length === 0) {
          sourceSelect.innerHTML = '<option value="">No other branches found to merge</option>';
        } else {
          otherBranches.forEach(b => {
            const opt = document.createElement('option');
            opt.value = b;
            opt.textContent = b;
            sourceSelect.appendChild(opt);
          });
          updateMergePreview(otherBranches[0]);
        }
      } catch (e) {
        sourceSelect.innerHTML = '<option value="">Failed to load branches</option>';
      }
    }

    modal.classList.add('active');
  }

  function closeGitMergeModal() {
    document.getElementById('modal-git-merge')?.classList.remove('active');
  }

  async function updateMergePreview(sourceBranch) {
    const repo = getSelectedRepo();
    if (!repo || !sourceBranch) return;

    const statusBadge = document.getElementById('badge-merge-status');
    const analysisMsg = document.getElementById('merge-analysis-msg');
    const commitsCountEl = document.getElementById('lbl-merge-commits-count');
    const commitsListEl = document.getElementById('merge-commits-list');
    const wrapConflicts = document.getElementById('wrap-merge-conflicts');
    const conflictsList = document.getElementById('merge-conflicts-list');
    const executeBtn = document.getElementById('btn-execute-merge-modal');
    const ffOnlyChk = document.getElementById('chk-merge-ff-only');

    if (statusBadge) {
      statusBadge.textContent = 'Evaluating...';
      statusBadge.style.background = 'rgba(255,255,255,0.08)';
      statusBadge.style.color = 'var(--text-dim)';
    }
    if (analysisMsg) analysisMsg.textContent = `Analyzing merge from '${sourceBranch}' into '${repo.branch || 'current'}'...`;
    if (executeBtn) executeBtn.disabled = true;

    try {
      const res = await callBridge('preview_git_merge', {
        repoPath: repo.path,
        sourceBranch,
        targetBranch: repo.branch
      });

      if (res && res.success) {
        if (commitsCountEl) commitsCountEl.textContent = res.incomingCommitCount || 0;

        if (commitsListEl) {
          if (res.incomingCommits && res.incomingCommits.length > 0) {
            commitsListEl.innerHTML = res.incomingCommits.map(c => `<div>${escapeHtml(c)}</div>`).join('');
          } else {
            commitsListEl.innerHTML = '<div style="color: var(--text-dim);">No incoming commits to merge (branches already synchronized).</div>';
          }
        }

        const isFf = !!res.isFastForward;
        const hasConflicts = !!res.hasConflicts;
        const canMerge = !!res.canMerge && (!ffOnlyChk?.checked || isFf);

        if (hasConflicts) {
          if (statusBadge) {
            statusBadge.textContent = 'Conflicts Detected';
            statusBadge.style.background = 'rgba(239, 68, 68, 0.15)';
            statusBadge.style.color = '#ef4444';
          }
          if (analysisMsg) analysisMsg.textContent = 'Cannot merge cleanly. Conflicting changes found in files below.';
          if (wrapConflicts) wrapConflicts.style.display = 'block';
          if (conflictsList) {
            conflictsList.innerHTML = (res.conflictingFiles && res.conflictingFiles.length > 0)
              ? res.conflictingFiles.map(f => `<div>&bull; ${escapeHtml(f)}</div>`).join('')
              : '<div>Conflicting files detected in merge tree.</div>';
          }
        } else if (isFf) {
          if (statusBadge) {
            statusBadge.textContent = 'Fast-Forward Clean';
            statusBadge.style.background = 'rgba(16, 185, 129, 0.15)';
            statusBadge.style.color = '#10b981';
          }
          if (analysisMsg) analysisMsg.textContent = 'Fast-forward merge possible without extra merge commit.';
          if (wrapConflicts) wrapConflicts.style.display = 'none';
        } else {
          if (statusBadge) {
            statusBadge.textContent = '3-Way Merge Clean';
            statusBadge.style.background = 'rgba(6, 182, 212, 0.15)';
            statusBadge.style.color = 'var(--accent-cyan)';
          }
          if (analysisMsg) analysisMsg.textContent = 'Automatic 3-way merge cleanly possible without conflicts.';
          if (wrapConflicts) wrapConflicts.style.display = 'none';
        }

        if (executeBtn) executeBtn.disabled = !canMerge;
      } else {
        if (statusBadge) {
          statusBadge.textContent = 'Error';
          statusBadge.style.background = 'rgba(239, 68, 68, 0.15)';
          statusBadge.style.color = '#ef4444';
        }
        if (analysisMsg) analysisMsg.textContent = res?.message || 'Failed to calculate merge preview.';
      }
    } catch (e) {
      if (analysisMsg) analysisMsg.textContent = 'Preview error: ' + (e.message || String(e));
    }
  }

  async function executeGitMerge() {
    const repo = getSelectedRepo();
    const sourceSelect = document.getElementById('select-merge-source-branch');
    const customMsgInput = document.getElementById('input-merge-custom-msg');
    const ffOnlyChk = document.getElementById('chk-merge-ff-only');
    const sourceBranch = sourceSelect?.value;

    if (!repo || !sourceBranch) return;

    if (!confirm(`Are you sure you want to merge '${sourceBranch}' into '${repo.branch || 'current'}'?`)) return;

    try {
      showToast(`Executing merge: ${sourceBranch} -> ${repo.branch}...`, 'info');
      const res = await callBridge('execute_git_merge', {
        repoPath: repo.path,
        sourceBranch,
        fastForwardOnly: !!ffOnlyChk?.checked,
        message: customMsgInput?.value.trim() || null
      });

      if (res && res.success) {
        showToast('Merge completed successfully!', 'success', 5000);
        closeGitMergeModal();
        await loadRepoManagerState();
        if (repoState.activeDetailTab === 'history') loadCommitGraph(repo.path);
        if (repoState.activeDetailTab === 'changes') loadRepoChanges(repo.path);
      } else {
        showToast(`Merge failed: ${res?.message || res?.output || 'Conflicts occurred'}`, 'error', 7000);
        updateMergePreview(sourceBranch);
      }
    } catch (e) {
      showToast(`Merge execution error: ${e.message}`, 'error');
    }
  }

  // =========================================================================
  // Knowledge & Memory Subtab Logic (Option D)
  // =========================================================================
  async function loadWorkspaceMemories(repoPath) {
    const container = document.getElementById('memory-cards-container');
    const badge = document.getElementById('lbl-repo-memory-badge');
    if (!container) return;

    container.innerHTML = `
      <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center;">
        <span class="spinner" style="width: 14px; height: 14px; margin-bottom: 8px;"></span>
        <div>Loading workspace memories and solutions...</div>
      </div>
    `;

    try {
      const res = await callBridge('get_workspace_memories', { repoPath });
      const memories = (res && res.memories) ? res.memories : [];
      repoState.memories = memories;
      if (badge) {
        badge.textContent = memories.length;
        badge.style.display = memories.length > 0 ? 'inline-block' : 'none';
      }
      renderMemoryCards(memories, repoPath);
    } catch (e) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 30px; font-size: 11px; text-align: center; color: #ef4444;">
          Failed to load memories: ${escapeHtml(e.message || String(e))}
        </div>
      `;
    }
  }

  function renderMemoryCards(memories, repoPath) {
    const container = document.getElementById('memory-cards-container');
    if (!container) return;

    if (!memories || memories.length === 0) {
      container.innerHTML = `
        <div class="empty-state" style="padding: 40px 20px; font-size: 12px; text-align: center; color: var(--text-dim); line-height: 1.6;">
          <svg class="icon-svg" style="width: 32px; height: 32px; opacity: 0.3; margin-bottom: 8px;" viewBox="0 0 24 24"><path d="M4 19.5v-15A2.5 2.5 0 0 1 6.5 2H20v20H6.5a2.5 2.5 0 0 1-2.5-2.5Z"/><path d="M6 6h10M6 10h10M6 14h6"/></svg>
          <div style="font-weight: 600; color: var(--text-main); margin-bottom: 4px;">No Knowledge Records Yet</div>
          <div>When CLI agents complete tasks in this workspace, solutions and architectural lessons will be automatically recorded in <code style="font-family: var(--font-mono); color: var(--accent-cyan);">.agents/memory/</code>.</div>
          <div style="margin-top: 8px;">Click <strong>Add Note</strong> above to manually record an architectural decision or fix.</div>
        </div>
      `;
      return;
    }

    container.innerHTML = '';
    memories.forEach(m => {
      const card = document.createElement('div');
      card.className = 'memory-entry-card';

      let tagsHtml = '';
      if (m.tags && m.tags.length > 0) {
        tagsHtml = m.tags.map(t => `<span class="memory-tag-chip">${escapeHtml(t)}</span>`).join('');
      }

      let filesHtml = '';
      if (m.affectedFiles && m.affectedFiles.length > 0) {
        filesHtml = `
          <div class="memory-files-strip">
            <span style="font-size: 10px; color: var(--text-dim); font-weight: 600;">Files:</span>
            ${m.affectedFiles.slice(0, 4).map(f => `<span class="memory-file-pill">${escapeHtml(f)}</span>`).join('')}
            ${m.affectedFiles.length > 4 ? `<span style="font-size: 10px; color: var(--text-dim);">+${m.affectedFiles.length - 4} more</span>` : ''}
          </div>
        `;
      }

      const scoreHtml = m.relevanceScore > 0
        ? `<span class="memory-score-badge" title="Search match relevance score">Score: ${m.relevanceScore}</span>`
        : '';

      card.innerHTML = `
        <div class="memory-card-header">
          <div class="memory-card-title-group">
            <span class="memory-source-badge ${m.isManual ? 'manual' : 'agent'}">${escapeHtml(m.sourceEngine || (m.isManual ? 'Note' : 'Agent'))}</span>
            <span class="memory-card-title" title="${escapeHtml(m.title)}">${escapeHtml(m.title)}</span>
            ${scoreHtml}
          </div>
          <div class="memory-card-actions">
            <button class="btn-icon btn-sm btn-copy-single-memory" title="Copy memory snippet to clipboard">
              <svg class="icon-svg" style="width: 12px; height: 12px;" viewBox="0 0 24 24"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>
            </button>
            <button class="btn-icon btn-sm btn-delete-single-memory" style="color: #ef4444;" title="Delete this memory record">
              <svg class="icon-svg" style="width: 12px; height: 12px;" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
            </button>
          </div>
        </div>

        <div class="memory-card-summary">${escapeHtml(m.summary || 'No summary text available.')}</div>

        ${filesHtml}

        <div class="memory-card-footer">
          <div class="memory-tags-strip">${tagsHtml}</div>
          <span class="memory-card-date">${formatDate(m.timestamp)}</span>
        </div>
      `;

      card.querySelector('.btn-copy-single-memory')?.addEventListener('click', () => {
        const text = `[AGENTS_HUB_MEMORY]\n# ${m.title}\n${m.summary}\nFiles: ${(m.affectedFiles || []).join(', ')}\n[/AGENTS_HUB_MEMORY]`;
        navigator.clipboard.writeText(text);
        showToast('Memory snippet copied to clipboard', 'success');
      });

      card.querySelector('.btn-delete-single-memory')?.addEventListener('click', () => {
        deleteWorkspaceMemory(repoPath, m.id, m.title);
      });

      container.appendChild(card);
    });
  }

  async function deleteWorkspaceMemory(repoPath, memoryId, title) {
    if (!confirm(`Are you sure you want to delete this memory record?\n\n"${title || memoryId}"\n\nThis will permanently delete the Markdown file from .agents/memory/.`)) {
      return;
    }

    try {
      showToast('Deleting memory record...', 'info');
      const res = await callBridge('delete_workspace_memory', { repoPath, memoryId });
      if (res && res.success) {
        showToast(res.message || 'Memory deleted successfully', 'success');
        await loadWorkspaceMemories(repoPath);
      } else {
        showToast(`Failed to delete memory: ${res?.message || 'Unknown error'}`, 'error');
      }
    } catch (e) {
      showToast(`Error deleting memory: ${e.message}`, 'error');
    }
  }

  async function clearAllWorkspaceMemories() {
    const repo = getSelectedRepo();
    if (!repo) return;

    if (!confirm(`CAUTION: Are you sure you want to delete ALL session memories and knowledge files in this workspace?\n\nDirectory: ${repo.path}/.agents/memory/`)) {
      return;
    }

    try {
      showToast('Clearing workspace memories...', 'info');
      const res = await callBridge('clear_workspace_memories', { repoPath: repo.path });
      if (res && res.success) {
        showToast(res.message || 'All memories cleared', 'success');
        await loadWorkspaceMemories(repo.path);
      } else {
        showToast(`Failed to clear memories: ${res?.message || 'Unknown error'}`, 'error');
      }
    } catch (e) {
      showToast(`Error clearing memories: ${e.message}`, 'error');
    }
  }

  async function searchWorkspaceMemories(query) {
    const repo = getSelectedRepo();
    if (!repo) return;

    repoState.memoryQuery = (query || '').trim();
    if (!repoState.memoryQuery) {
      renderMemoryCards(repoState.memories, repo.path);
      return;
    }

    try {
      const res = await callBridge('search_workspace_memories', {
        repoPath: repo.path,
        query: repoState.memoryQuery,
        topK: 20
      });
      const matches = (res && res.memories) ? res.memories : [];
      renderMemoryCards(matches, repo.path);
    } catch (e) {
      console.warn('Memory search failed:', e);
    }
  }

  async function copyContextPromptSnippet() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const query = document.getElementById('memory-search-input')?.value.trim() || '';
    try {
      const res = await callBridge('inject_session_context', {
        repoPath: repo.path,
        prompt: query
      });

      if (res && res.snippet) {
        await navigator.clipboard.writeText(res.snippet);
        showToast('Context injection snippet copied to clipboard! Paste into your agent prompt.', 'success', 5000);
      } else {
        showToast('No relevant memories found to generate snippet', 'info');
      }
    } catch (e) {
      showToast(`Error generating prompt snippet: ${e.message}`, 'error');
    }
  }

  function openAddMemoryModal() {
    const modal = document.getElementById('modal-add-memory');
    const titleInput = document.getElementById('input-memory-title');
    const tagsInput = document.getElementById('input-memory-tags');
    const contentInput = document.getElementById('input-memory-content');

    if (!modal) return;
    if (titleInput) titleInput.value = '';
    if (tagsInput) tagsInput.value = '';
    if (contentInput) contentInput.value = '';

    modal.classList.add('active');
    setTimeout(() => titleInput?.focus(), 50);
  }

  function closeAddMemoryModal() {
    document.getElementById('modal-add-memory')?.classList.remove('active');
  }

  async function submitAddMemoryModal() {
    const repo = getSelectedRepo();
    if (!repo) return;

    const titleInput = document.getElementById('input-memory-title');
    const tagsInput = document.getElementById('input-memory-tags');
    const contentInput = document.getElementById('input-memory-content');

    const title = titleInput?.value.trim();
    const summary = contentInput?.value.trim() || '';
    const rawTags = tagsInput?.value.split(',').map(t => t.trim()).filter(Boolean) || [];

    if (!title) {
      showToast('Title is required for the knowledge note', 'warn');
      titleInput?.focus();
      return;
    }

    try {
      showToast('Saving knowledge note...', 'info');
      const res = await callBridge('add_workspace_memory', {
        repoPath: repo.path,
        title,
        summary,
        tags: rawTags
      });

      if (res && res.success) {
        showToast('Knowledge note saved successfully', 'success');
        closeAddMemoryModal();
        await loadWorkspaceMemories(repo.path);
      } else {
        showToast(`Failed to save note: ${res?.message || 'Unknown error'}`, 'error');
      }
    } catch (e) {
      showToast(`Error saving note: ${e.message}`, 'error');
    }
  }

  // Helpers
  function getSelectedRepo() {
    return repoState.repositories.find(r => r.id === repoState.selectedRepoId);
  }

  function getSelectedOrWorkingRepo() {
    return repoState.repositories.find(r => r.id === repoState.workingProjectId) || getSelectedRepo();
  }

  function formatDate(iso) {
    if (!iso) return '';
    try {
      const d = new Date(iso);
      return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    } catch {
      return iso;
    }
  }

  // Export module to global scope
  window.initRepositoriesModule = initRepositoriesModule;
  window.refreshRepositoriesView = loadRepoManagerState;
})();
