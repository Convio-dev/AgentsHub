import { createPortal } from 'react-dom';
import React, { useState, useEffect } from 'react';
import {
  GitBranch,
  GitCommit,
  GitPullRequest,
  CheckCircle2,
  AlertCircle,
  Folder,
  RefreshCw,
  Search,
  ExternalLink,
  Terminal,
  Play,
  FileCode,
  Shield,
  Layers,
  ArrowUp,
  Copy,
  ChevronDown,
  Sparkles,
  Check,
  X,
  FileText,
  Activity,
  Download,
  Upload,
} from 'lucide-react';
import { useRepositoriesStore, RepositoryItem } from '../store/useRepositoriesStore';
import { useSidecarStore } from '../store/useSidecarStore';
import { useWorkspaceStore } from '../store/useWorkspaceStore';
import { webviewBridge } from '../services/webviewBridge';
import { Button } from '../components/common/Button';

export const RepositoriesView: React.FC = () => {
  const {
    repositories,
    selectedRepoId,
    workingProjectId,
    isScanning,
    searchFilter,
    activeFilter,
    activeSubTab,
    changedFiles,
    selectedDiffFile,
    diffContent,
    diffStats,
    isStagingAll,
    commitMessage,
    commitAction,
    branches,
    launchers,
    healthFindings,
    commits,
    loadCommits,
    repoNotes,
    notesSaved,
    selectedBatchRepoIds,
    setSearchFilter,
    setActiveFilter,
    setActiveSubTab,
    setCommitMessage,
    setCommitAction,
    setIsStagingAll,
    toggleBatchSelectRepo,
    selectAllBatchRepos,
    clearBatchSelection,
    loadRepoManagerState,
    startRepoScan,
    cancelRepoScan,
    selectRepo,
    setWorkingProject,
    loadFileDiff,
    stageFile,
    unstageFile,
    stageAllFiles,
    createCommit,
    gitNetworkAction,
    batchNetworkAction,
    switchBranch,
    executeLauncher,
    saveNotes,
    aiGenerateCommitMessage,
  } = useRepositoriesStore();

  const { setActiveView } = useSidecarStore();
  const { switchWorkspace } = useWorkspaceStore();

  const [branchPopoverOpen, setBranchPopoverOpen] = useState(false);
  const [branchSearch, setBranchSearch] = useState('');
  const [copyFeedback, setCopyFeedback] = useState(false);
  const [commitSplitOpen, setCommitSplitOpen] = useState(false);

  useEffect(() => {
    loadRepoManagerState();
  }, [loadRepoManagerState]);

  /** Commit the user wants a branch from, if the confirm modal is open. */
  const [branchTarget, setBranchTarget] = useState<{ hash: string; subject: string } | null>(null);
  const [branchName, setBranchName] = useState('');
  const [branchSwitch, setBranchSwitch] = useState(false);
  const [branchBusy, setBranchBusy] = useState(false);
  const [branchError, setBranchError] = useState<string | null>(null);

  const openBranchModal = (hash: string, subject: string) => {
    setBranchTarget({ hash, subject });
    setBranchName(`restore/${hash.slice(0, 7)}`);
    setBranchSwitch(false);
    setBranchError(null);
  };

  const createBranch = async () => {
    if (!branchTarget) return;

    const repo = useRepositoriesStore.getState();
    const target = repo.repositories.find(r => r.id === repo.selectedRepoId);
    if (!target) return;

    setBranchBusy(true);
    setBranchError(null);

    try {
      const res: any = await webviewBridge.invoke('git_branch_at', {
        repoPath: target.path,
        name: branchName.trim(),
        hash: branchTarget.hash,
        switchTo: branchSwitch,
      });

      if (!res?.created) {
        setBranchError(res?.error || 'The branch could not be created.');
        return;
      }

      if (res.error) {
        // Created, but the checkout refused (usually a dirty tree). Say so instead of closing.
        setBranchError(`Branch created, but not checked out: ${res.error}`);
        return;
      }

      setBranchTarget(null);
      await repo.loadRepoManagerState();
    } catch (err) {
      setBranchError(err instanceof Error ? err.message : 'The branch could not be created.');
    } finally {
      setBranchBusy(false);
    }
  };

  // The graph is fetched when its tab opens, and again when the selection changes, because the
  // commit list belongs to one repository rather than to the whole manager state.
  useEffect(() => {
    if (activeSubTab !== 'history') return;

    // Read the selection from the store rather than the local binding: this hook runs before that
    // binding is declared, and the path is only needed when the tab opens.
    const state = useRepositoriesStore.getState();
    const repo = state.repositories.find(r => r.id === state.selectedRepoId);
    if (repo?.path) void loadCommits(repo.path);
  }, [activeSubTab, loadCommits]);

  const selectedRepo = repositories.find(r => r.id === selectedRepoId) || repositories[0];
  const workingRepo = repositories.find(r => r.id === workingProjectId) || selectedRepo;

  // Metrics
  const totalRepos = repositories.length;
  const cleanRepos = repositories.filter(r => r.status === 'clean').length;
  const dirtyRepos = repositories.filter(r => r.status === 'modified').length;
  const aheadRepos = repositories.filter(r => r.aheadCount > 0).length;

  // Filter repos
  const filteredRepos = repositories.filter(repo => {
    if (searchFilter) {
      const q = searchFilter.toLowerCase();
      const match = repo.name.toLowerCase().includes(q) || repo.path.toLowerCase().includes(q) || repo.branch.toLowerCase().includes(q);
      if (!match) return false;
    }
    if (activeFilter === 'working') return repo.id === workingProjectId;
    if (activeFilter === 'pinned') return repo.isPinned;
    if (activeFilter === 'modified') return repo.status === 'modified';
    if (activeFilter === 'clean') return repo.status === 'clean';
    if (activeFilter === 'problems') return repo.hasIssues;
    return true;
  });

  const handleCopyPath = (path: string) => {
    navigator.clipboard.writeText(path);
    setCopyFeedback(true);
    setTimeout(() => setCopyFeedback(false), 2000);
  };

  const handleStartAgentInRepo = (repo: RepositoryItem) => {
    switchWorkspace(repo.path);
    setActiveView('chat');
  };

  const openInEditor = (path: string) => {
    webviewBridge.invoke('open_external_editor', { path });
  };

  const openTerminal = (path: string) => {
    webviewBridge.invoke('open_terminal_at', { path });
  };

  const openExplorer = (path: string) => {
    webviewBridge.invoke('open_in_explorer', { path });
  };

  const filteredBranches = branches.filter(b => !branchSearch || b.toLowerCase().includes(branchSearch.toLowerCase()));

  return (
    <div className="flex-1 flex flex-col h-full overflow-hidden bg-[var(--bg-app,#090D16)] text-[var(--text-main,#F1F5F9)] select-none">
      {/* 1. TOP METRICS OVERVIEW */}
      <div className="px-6 pt-5 pb-3">
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
          <div className="p-3.5 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between">
            <div>
              <span className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider block">Repositories</span>
              <span className="text-xl font-bold font-mono text-accent mt-0.5 block">{totalRepos}</span>
            </div>
            <div className="w-9 h-9 rounded-lg bg-sky-500/10 border border-sky-500/20 flex items-center justify-center text-accent">
              <Folder className="w-4 h-4" />
            </div>
          </div>

          <div className="p-3.5 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between">
            <div>
              <span className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider block">Clean Trees</span>
              <span className="text-xl font-bold font-mono text-state-success mt-0.5 block">{cleanRepos}</span>
            </div>
            <div className="w-9 h-9 rounded-lg bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-state-success">
              <CheckCircle2 className="w-4 h-4" />
            </div>
          </div>

          <div className="p-3.5 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between">
            <div>
              <span className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider block">Modified Trees</span>
              <span className="text-xl font-bold font-mono text-state-warning mt-0.5 block">{dirtyRepos}</span>
            </div>
            <div className="w-9 h-9 rounded-lg bg-amber-500/10 border border-amber-500/20 flex items-center justify-center text-state-warning">
              <AlertCircle className="w-4 h-4" />
            </div>
          </div>

          <div className="p-3.5 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between">
            <div>
              <span className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider block">Ahead / Unpushed</span>
              <span className="text-xl font-bold font-mono text-purple-400 mt-0.5 block">{aheadRepos}</span>
            </div>
            <div className="w-9 h-9 rounded-lg bg-purple-500/10 border border-purple-500/20 flex items-center justify-center text-purple-400">
              <ArrowUp className="w-4 h-4" />
            </div>
          </div>
        </div>
      </div>

      {/* 2. WORKING ON NOW BANNER */}
      {workingRepo && (
        <div className="px-6 pb-3">
          <div className="p-3.5 rounded-xl bg-[var(--bg-card,#0F172A)] border border-sky-500/30 flex items-center justify-between flex-wrap gap-3">
            <div className="flex items-center gap-3">
              <div className="w-8 h-8 rounded-lg bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-accent">
                <Activity className="w-4 h-4" />
              </div>
              <div>
                <div className="text-[10px] font-bold text-accent uppercase tracking-wider">Working On Now</div>
                <div className="text-xs font-bold text-[var(--text-main,#F1F5F9)] flex items-center gap-2">
                  <span>{workingRepo.name}</span>
                  <span className="text-[10px] px-1.5 py-0.2 rounded bg-sky-500/10 text-accent border border-sky-500/20 font-mono">
                    {workingRepo.branch}
                  </span>
                </div>
                <div className="text-[11px] font-mono text-[var(--text-dim,#64748B)] truncate max-w-md">
                  {workingRepo.path}
                </div>
              </div>
            </div>

            <div className="flex items-center gap-2">
              <Button variant="secondary" size="xs" onClick={() => setActiveSubTab('changes')}>
                Changes ({changedFiles.length})
              </Button>
              <Button variant="secondary" size="xs" leftIcon={<FileCode className="w-3.5 h-3.5" />} onClick={() => openInEditor(workingRepo.path)}>
                External Editor
              </Button>
              <Button variant="secondary" size="xs" leftIcon={<Terminal className="w-3.5 h-3.5" />} onClick={() => openTerminal(workingRepo.path)}>
                Terminal
              </Button>
              <Button variant="primary" size="xs" leftIcon={<Play className="w-3.5 h-3.5" />} onClick={() => handleStartAgentInRepo(workingRepo)}>
                Start Agent
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* 3. TOOLBAR & FILTERS */}
      <div className="px-6 py-2 border-y border-[var(--border-subtle,#1E293B)] flex items-center justify-between bg-[var(--bg-card,#0F172A)]/60 gap-4 flex-wrap">
        <div className="flex items-center gap-3 flex-1 min-w-[260px]">
          <div className="relative flex-1 max-w-xs">
            <Search className="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--text-dim,#64748B)]" />
            <input
              type="text"
              value={searchFilter}
              onChange={e => setSearchFilter(e.target.value)}
              placeholder="Search repositories (name, path, branch)..."
              className="w-full pl-8 pr-3 py-1.5 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-xs text-[var(--text-main,#F1F5F9)] placeholder-[var(--text-dim,#64748B)] focus:outline-none focus:border-sky-500 font-sans"
            />
          </div>

          <div className="flex items-center gap-1.5 overflow-x-auto">
            {(['all', 'working', 'pinned', 'modified', 'clean', 'problems'] as const).map(f => (
              <button
                key={f}
                onClick={() => setActiveFilter(f)}
                className={`px-2.5 py-1 rounded-md text-[11px] font-medium capitalize transition-all ${
                  activeFilter === f
                    ? 'bg-sky-500/15 text-accent font-semibold border border-sky-500/30'
                    : 'text-[var(--text-muted,#94A3B8)] hover:bg-[var(--bg-input,#0D1322)] hover:text-[var(--text-main,#F1F5F9)]'
                }`}
              >
                {f}
              </button>
            ))}
          </div>
        </div>

        <div className="flex items-center gap-2">
          {isScanning && (
            <span className="flex items-center gap-1.5 text-xs text-accent font-mono">
              <RefreshCw className="w-3 h-3 animate-spin" />
              <span>Scanning...</span>
              <button onClick={cancelRepoScan} className="text-state-danger hover:text-state-danger ml-1">
                &times;
              </button>
            </span>
          )}
          <Button
            variant="secondary"
            size="xs"
            leftIcon={<RefreshCw className={`w-3.5 h-3.5 ${isScanning ? 'animate-spin' : ''}`} />}
            onClick={startRepoScan}
          >
            Scan (F5)
          </Button>
          <Button variant="secondary" size="xs" onClick={() => webviewBridge.invoke('open_repo_settings')}>
            Folders
          </Button>
          <Button variant="secondary" size="xs" onClick={() => webviewBridge.invoke('export_repo_inventory')}>
            Export
          </Button>
        </div>
      </div>

      {/* 4. MASTER-DETAIL SPLIT CONTAINER */}
      <div className="flex-1 flex overflow-hidden">
        {/* Left: Repositories List (~340px) */}
        <div className="w-80 border-r border-[var(--border-subtle,#1E293B)] bg-[var(--bg-sidebar,#0A0E1A)] flex flex-col">
          {/* Header */}
          <div className="p-3 border-b border-[var(--border-subtle,#1E293B)] flex items-center justify-between text-xs text-[var(--text-muted,#94A3B8)]">
            <div className="flex items-center gap-2">
              <input
                type="checkbox"
                checked={selectedBatchRepoIds.size === repositories.length && repositories.length > 0}
                onChange={() => (selectedBatchRepoIds.size === repositories.length ? clearBatchSelection() : selectAllBatchRepos())}
                className="w-3.5 h-3.5 rounded accent-sky-500"
              />
              <span className="font-semibold text-[var(--text-main,#F1F5F9)]">Repositories ({filteredRepos.length})</span>
            </div>
            <span className="text-[11px] text-[var(--text-dim,#64748B)]">Sorted by Name</span>
          </div>

          {/* Batch Actions Bar */}
          {selectedBatchRepoIds.size > 0 && (
            <div className="p-2.5 bg-sky-500/10 border-b border-sky-500/20 flex items-center justify-between text-xs">
              <span className="font-semibold text-accent">{selectedBatchRepoIds.size} selected</span>
              <div className="flex items-center gap-1.5">
                <Button variant="secondary" size="xs" onClick={() => batchNetworkAction('fetch')}>
                  Fetch All
                </Button>
                <Button variant="primary" size="xs" onClick={() => batchNetworkAction('pull')}>
                  Pull Clean
                </Button>
                <button onClick={clearBatchSelection} className="text-xs text-[var(--text-dim,#64748B)] hover:text-white px-1">
                  Clear
                </button>
              </div>
            </div>
          )}

          {/* Scrollable Repo List */}
          <div className="flex-1 overflow-y-auto p-2 space-y-1.5">
            {filteredRepos.map(repo => {
              const isSelected = repo.id === (selectedRepo?.id || '');
              const isWorking = repo.id === workingProjectId;
              return (
                <div
                  key={repo.id}
                  onClick={() => selectRepo(repo.id)}
                  className={`p-3 rounded-lg border cursor-pointer transition-all ${
                    isSelected
                      ? 'bg-sky-500/15 border-sky-500/50 shadow-sm'
                      : 'bg-[var(--bg-card,#0F172A)] border-[var(--border-subtle,#1E293B)] hover:border-slate-600'
                  }`}
                >
                  <div className="flex items-start justify-between gap-2">
                    <div className="flex items-center gap-2 min-w-0">
                      <input
                        type="checkbox"
                        checked={selectedBatchRepoIds.has(repo.id)}
                        onChange={e => {
                          e.stopPropagation();
                          toggleBatchSelectRepo(repo.id);
                        }}
                        className="w-3.5 h-3.5 rounded accent-sky-500"
                      />
                      <span className="text-xs font-bold text-[var(--text-main,#F1F5F9)] truncate">{repo.name}</span>
                    </div>

                    <div className="flex items-center gap-1">
                      {isWorking && (
                        <span className="px-1.5 py-0.2 rounded text-[10px] font-bold bg-sky-500/20 text-accent border border-sky-500/30">
                          WORKING
                        </span>
                      )}
                      <span
                        className={`px-1.5 py-0.2 rounded text-[10px] font-mono ${
                          repo.status === 'clean'
                            ? 'bg-emerald-500/10 text-state-success border border-emerald-500/20'
                            : 'bg-amber-500/10 text-state-warning border border-amber-500/20'
                        }`}
                      >
                        {/* `status` is a closed union, so the badge is only as good as the store's
                            mapping from the service payload. A clean repo showing "Modified" means
                            that mapping, not this line. */}
                        {repo.status === 'clean' ? 'Clean' : 'Modified'}
                      </span>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 mt-1 text-[11px] text-[var(--text-dim,#64748B)] font-mono">
                    <span className="flex items-center gap-1">
                      <GitBranch className="w-3 h-3 text-accent" />
                      {repo.branch}
                    </span>
                    <span>•</span>
                    <span>{repo.lastCommitTime || 'Recently'}</span>
                  </div>

                  {repo.lastCommitMessage && (
                    <div className="text-[11px] text-[var(--text-muted,#94A3B8)] mt-1 truncate">
                      {repo.lastCommitMessage}
                    </div>
                  )}
                </div>
              );
            })}

            {filteredRepos.length === 0 && (
              <div className="p-8 text-center text-xs text-[var(--text-dim,#64748B)]">
                No repositories found matching current filter.
              </div>
            )}
          </div>
        </div>

        {/* Right: Selected Repository Detail Deck */}
        {selectedRepo ? (
          <div className="flex-1 flex flex-col overflow-hidden bg-[var(--bg-app,#090D16)]">
            {/* Header Deck */}
            <div className="p-4 border-b border-[var(--border-subtle,#1E293B)] bg-[var(--bg-header,#0B0F19)] space-y-3">
              <div className="flex items-center justify-between flex-wrap gap-3">
                <div className="flex items-center gap-2.5">
                  <div className="w-7 h-7 rounded-lg bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-accent">
                    <Folder className="w-4 h-4" />
                  </div>
                  <div>
                    <h2 className="text-sm font-bold text-[var(--text-main,#F1F5F9)]">{selectedRepo.name}</h2>
                    <span className="text-[11px] font-mono text-[var(--text-dim,#64748B)]">{selectedRepo.path}</span>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <Button
                    variant="secondary"
                    size="xs"
                    leftIcon={selectedRepo.id === workingProjectId ? <Check className="w-3.5 h-3.5 text-state-success" /> : undefined}
                    onClick={() => setWorkingProject(selectedRepo.id)}
                  >
                    {selectedRepo.id === workingProjectId ? 'Working on this' : 'Work on this'}
                  </Button>
                  {selectedRepo.remoteUrl && (
                    <Button
                      variant="secondary"
                      size="xs"
                      leftIcon={<ExternalLink className="w-3.5 h-3.5" />}
                      onClick={() => window.open(selectedRepo.remoteUrl, '_blank')}
                    >
                      GitHub
                    </Button>
                  )}
                  {launchers.length > 0 && (
                    <Button
                      variant="primary"
                      size="xs"
                      leftIcon={<Play className="w-3.5 h-3.5" />}
                      onClick={() => executeLauncher(selectedRepo.id, launchers[0].id)}
                    >
                      Run ({launchers[0].name})
                    </Button>
                  )}
                  <button
                    onClick={() => handleCopyPath(selectedRepo.path)}
                    title="Copy repository path"
                    className="p-1.5 rounded-lg border border-[var(--border-subtle,#1E293B)] hover:bg-[var(--bg-card,#0F172A)] text-[var(--text-dim,#64748B)] hover:text-white"
                  >
                    {copyFeedback ? <Check className="w-3.5 h-3.5 text-state-success" /> : <Copy className="w-3.5 h-3.5" />}
                  </button>
                </div>
              </div>

              {/* Meta strip with branch switcher */}
              <div className="flex items-center gap-3 pt-1 border-t border-[var(--border-subtle,#1E293B)]/60 text-xs">
                {/* Branch switcher popover */}
                <div className="relative">
                  <button
                    onClick={() => setBranchPopoverOpen(!branchPopoverOpen)}
                    className="flex items-center gap-1.5 px-2.5 py-1 rounded-md bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] hover:border-sky-500 font-mono text-[11px] text-[var(--text-main,#F1F5F9)]"
                  >
                    <GitBranch className="w-3 h-3 text-accent" />
                    <span>{selectedRepo.branch}</span>
                    <ChevronDown className="w-3 h-3 text-[var(--text-dim,#64748B)] ml-1" />
                  </button>

                  {branchPopoverOpen && (
                    <div className="absolute left-0 top-full mt-1 w-64 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] shadow-2xl z-50 p-2 space-y-2">
                      <input
                        type="text"
                        value={branchSearch}
                        onChange={e => setBranchSearch(e.target.value)}
                        placeholder="Search branches..."
                        className="w-full px-2.5 py-1 text-xs rounded bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-white focus:outline-none"
                      />
                      <div className="max-h-48 overflow-y-auto space-y-0.5">
                        {filteredBranches.map(b => (
                          <button
                            key={b}
                            onClick={() => {
                              switchBranch(selectedRepo.id, b);
                              setBranchPopoverOpen(false);
                            }}
                            className={`w-full text-left px-2 py-1.5 rounded text-xs font-mono flex items-center justify-between ${
                              b === selectedRepo.branch ? 'bg-sky-500/20 text-accent font-bold' : 'hover:bg-[var(--bg-input,#0D1322)] text-content-muted'
                            }`}
                          >
                            <span>{b}</span>
                            {b === selectedRepo.branch && <Check className="w-3 h-3 text-accent" />}
                          </button>
                        ))}
                      </div>
                    </div>
                  )}
                </div>

                <span
                  className={`px-2 py-0.5 rounded text-[11px] font-mono ${
                    selectedRepo.status === 'clean'
                      ? 'bg-emerald-500/10 text-state-success border border-emerald-500/20'
                      : 'bg-amber-500/10 text-state-warning border border-amber-500/20'
                  }`}
                >
                  {selectedRepo.status === 'clean' ? 'Clean Working Tree' : `${changedFiles.length} Changed Files`}
                </span>

                {selectedRepo.remoteUrl && (
                  <span className="text-[11px] font-mono text-[var(--text-dim,#64748B)] truncate max-w-xs">
                    {selectedRepo.remoteUrl}
                  </span>
                )}
              </div>
            </div>

            {/* Sub-tabs bar */}
            <div className="px-4 border-b border-[var(--border-subtle,#1E293B)] flex items-center gap-1 bg-[var(--bg-card,#0F172A)]">
              {(
                [
                  { id: 'overview', label: 'Overview' },
                  { id: 'changes', label: `Git Changes (${changedFiles.length})` },
                  { id: 'history', label: 'Commit Graph' },
                  { id: 'run', label: `Run (${launchers.length})` },
                  { id: 'health', label: 'Health' },
                  { id: 'notes', label: 'Notes' },
                ] as const
              ).map(t => (
                <button
                  key={t.id}
                  onClick={() => setActiveSubTab(t.id)}
                  className={`px-3 py-2.5 text-xs font-medium border-b-2 transition-all ${
                    activeSubTab === t.id
                      ? 'border-sky-500 text-accent font-bold'
                      : 'border-transparent text-[var(--text-muted,#94A3B8)] hover:text-white'
                  }`}
                >
                  {t.label}
                </button>
              ))}
            </div>

            {/* Sub-tab content */}
            <div className="flex-1 overflow-y-auto p-5">
              {/* 1. OVERVIEW */}
              {activeSubTab === 'overview' && (
                <div className="max-w-4xl space-y-5">
                  <div className="flex items-center gap-2">
                    <Button variant="secondary" size="xs" leftIcon={<FileCode className="w-3.5 h-3.5" />} onClick={() => openInEditor(selectedRepo.path)}>
                      Open in External Code Editor
                    </Button>
                    <Button variant="secondary" size="xs" leftIcon={<Folder className="w-3.5 h-3.5" />} onClick={() => openExplorer(selectedRepo.path)}>
                      Explorer
                    </Button>
                    <Button variant="secondary" size="xs" leftIcon={<Terminal className="w-3.5 h-3.5" />} onClick={() => openTerminal(selectedRepo.path)}>
                      Terminal
                    </Button>
                    <Button variant="primary" size="xs" leftIcon={<Play className="w-3.5 h-3.5" />} onClick={() => handleStartAgentInRepo(selectedRepo)}>
                      Start Agent in Repo
                    </Button>
                  </div>

                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] space-y-1">
                      <div className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider">Latest Commit</div>
                      <div className="text-xs font-medium text-[var(--text-main,#F1F5F9)]">
                        {/* A repo with a hash but no subject is not "no commits"; the service simply
                            did not read the subject, and claiming otherwise contradicted the hash
                            printed directly below. */}
                        {selectedRepo.lastCommitMessage
                          || (selectedRepo.lastCommitHash ? '(no commit message)' : 'No commits found')}
                      </div>
                      <div className="text-[11px] font-mono text-[var(--text-dim,#64748B)]">
                        {selectedRepo.lastCommitHash} • {selectedRepo.lastCommitAuthor} • {selectedRepo.lastCommitTime}
                      </div>
                    </div>

                    <div className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] space-y-1">
                      <div className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider">Remote Origin</div>
                      <div className="text-xs font-mono text-accent truncate">{selectedRepo.remoteUrl || 'No remote configured'}</div>
                      <div className="text-[11px] text-[var(--text-dim,#64748B)]">Upstream tracking: origin/{selectedRepo.branch}</div>
                    </div>

                    <div className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] space-y-1">
                      <div className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider">Working Tree Status</div>
                      <div className="text-xs font-medium text-[var(--text-main,#F1F5F9)]">
                        {selectedRepo.status === 'clean' ? 'Tree is clean, working directory up to date' : `${changedFiles.length} unstaged modifications`}
                      </div>
                      <div className="text-[11px] text-[var(--text-dim,#64748B)]">
                        Ahead: {selectedRepo.aheadCount} • Behind: {selectedRepo.behindCount}
                      </div>
                    </div>

                    <div className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] space-y-1">
                      <div className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider">Active Branch & Worktrees</div>
                      <div className="text-xs font-mono text-[var(--text-main,#F1F5F9)]">{selectedRepo.branch} (primary HEAD)</div>
                      <div className="text-[11px] text-[var(--text-dim,#64748B)]">{branches.length} local branches tracked</div>
                    </div>
                  </div>
                </div>
              )}

              {/* 2. GIT CHANGES & DIFF */}
              {activeSubTab === 'changes' && (
                <div className="h-full flex flex-col space-y-3">
                  {/* Toolbar */}
                  <div className="flex items-center justify-between gap-2 flex-wrap">
                    <div className="flex items-center gap-1.5">
                      <Button variant="secondary" size="xs" onClick={() => stageAllFiles(selectedRepo.id)}>
                        Stage All
                      </Button>
                    </div>

                    <div className="flex items-center gap-1.5">
                      <Button variant="secondary" size="xs" leftIcon={<Download className="w-3 h-3" />} onClick={() => gitNetworkAction(selectedRepo.id, 'fetch')}>
                        Fetch
                      </Button>
                      <Button variant="secondary" size="xs" leftIcon={<Download className="w-3 h-3" />} onClick={() => gitNetworkAction(selectedRepo.id, 'pull')}>
                        Pull
                      </Button>
                      <Button variant="secondary" size="xs" leftIcon={<Upload className="w-3 h-3" />} onClick={() => gitNetworkAction(selectedRepo.id, 'push')}>
                        Push
                      </Button>
                    </div>
                  </div>

                  {/* 2-column layout: Files and Diff */}
                  <div className="flex-1 grid grid-cols-1 md:grid-cols-3 gap-3 min-h-[300px]">
                    {/* Changed files column */}
                    <div className="rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] p-3 flex flex-col overflow-hidden">
                      <div className="text-xs font-bold text-[var(--text-main,#F1F5F9)] pb-2 border-b border-[var(--border-subtle,#1E293B)] flex items-center justify-between">
                        <span>Changed Files ({changedFiles.length})</span>
                      </div>
                      <div className="flex-1 overflow-y-auto space-y-1 pt-2">
                        {changedFiles.map(file => (
                          <div
                            key={file.path}
                            onClick={() => loadFileDiff(selectedRepo.id, file.path)}
                            className={`p-2 rounded-lg text-xs font-mono cursor-pointer flex items-center justify-between transition-all ${
                              selectedDiffFile === file.path
                                ? 'bg-sky-500/20 text-accent font-semibold border border-sky-500/30'
                                : 'hover:bg-[var(--bg-input,#0D1322)] text-content-muted border border-transparent'
                            }`}
                          >
                            <span className="truncate flex-1">{file.path}</span>
                            <span className="text-[10px] text-state-success ml-2">
                              {file.additions ? `+${file.additions}` : ''} {file.deletions ? `-${file.deletions}` : ''}
                            </span>
                          </div>
                        ))}

                        {changedFiles.length === 0 && (
                          <div className="p-8 text-center text-xs text-[var(--text-dim,#64748B)]">
                            Working tree clean. No modified files detected.
                          </div>
                        )}
                      </div>
                    </div>

                    {/* Diff canvas column */}
                    <div className="md:col-span-2 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex flex-col overflow-hidden">
                      <div className="px-4 py-2 border-b border-[var(--border-subtle,#1E293B)] bg-[var(--bg-header,#0B0F19)] flex items-center justify-between text-xs font-mono">
                        <span className="text-accent truncate">{selectedDiffFile || 'Select a file to inspect diff'}</span>
                        <span className="text-[var(--text-dim,#64748B)]">{diffStats}</span>
                      </div>
                      <pre className="flex-1 p-4 font-mono text-xs overflow-auto bg-[var(--bg-input,#0D1322)] text-content-muted leading-relaxed whitespace-pre">
                        {/* An empty diff is not a failure: a new file has nothing to compare
                            against, and a binary has no text to show. "No diff available" made both
                            read like an error. `selectedDiffFile` is a path string, so the wording
                            covers both cases instead of branching on a status it does not carry. */}
                        {diffContent
                          || 'No text diff to show. A new file has nothing to compare against, and a binary file has no text.'}
                      </pre>
                    </div>
                  </div>

                  {/* Commit composer */}
                  <div className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] space-y-3">
                    <textarea
                      rows={2}
                      value={commitMessage}
                      onChange={e => setCommitMessage(e.target.value)}
                      placeholder="Commit message (describes what changed)..."
                      className="w-full px-3 py-2 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-xs text-[var(--text-main,#F1F5F9)] focus:outline-none focus:border-sky-500 font-sans resize-none"
                    />

                    <div className="flex items-center justify-between gap-3 flex-wrap">
                      <div className="flex items-center gap-3">
                        <label className="flex items-center gap-2 text-xs text-[var(--text-main,#F1F5F9)] cursor-pointer">
                          <input
                            type="checkbox"
                            checked={isStagingAll}
                            onChange={e => setIsStagingAll(e.target.checked)}
                            className="w-4 h-4 rounded accent-sky-500"
                          />
                          <span>Stage all changes before committing</span>
                        </label>

                        <button
                          onClick={() => aiGenerateCommitMessage(selectedRepo.id)}
                          className="flex items-center gap-1.5 px-2.5 py-1 rounded bg-sky-500/10 text-accent border border-sky-500/20 text-xs hover:bg-sky-500/20"
                        >
                          <Sparkles className="w-3.5 h-3.5" />
                          <span>AI Generate Message</span>
                        </button>
                      </div>

                      <div className="relative">
                        <div className="flex items-center">
                          <Button
                            variant="primary"
                            size="xs"
                            onClick={() => createCommit(selectedRepo.id)}
                            disabled={!commitMessage.trim()}
                          >
                            {commitAction === 'push' ? 'Commit & Push to GitHub' : 'Create Local Commit'}
                          </Button>
                          <button
                            onClick={() => setCommitSplitOpen(!commitSplitOpen)}
                            className="px-2 py-1.5 bg-sky-600 hover:bg-sky-500 text-white rounded-r border-l border-sky-700 text-xs"
                          >
                            <ChevronDown className="w-3 h-3" />
                          </button>
                        </div>

                        {commitSplitOpen && (
                          <div className="absolute right-0 bottom-full mb-1 w-56 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] shadow-2xl z-50 p-1 space-y-1">
                            <button
                              onClick={() => {
                                setCommitAction('local');
                                setCommitSplitOpen(false);
                              }}
                              className={`w-full text-left p-2 rounded text-xs ${
                                commitAction === 'local' ? 'bg-sky-500/20 text-accent font-bold' : 'hover:bg-[var(--bg-input,#0D1322)] text-content-muted'
                              }`}
                            >
                              <div className="font-semibold">Create Local Commit</div>
                              <div className="text-[10px] text-[var(--text-dim,#64748B)]">Record commit without pushing</div>
                            </button>
                            <button
                              onClick={() => {
                                setCommitAction('push');
                                setCommitSplitOpen(false);
                              }}
                              className={`w-full text-left p-2 rounded text-xs ${
                                commitAction === 'push' ? 'bg-sky-500/20 text-accent font-bold' : 'hover:bg-[var(--bg-input,#0D1322)] text-content-muted'
                              }`}
                            >
                              <div className="font-semibold">Commit & Push to Live GitHub</div>
                              <div className="text-[10px] text-[var(--text-dim,#64748B)]">Record and push to origin</div>
                            </button>
                          </div>
                        )}
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {/* 3. COMMIT GRAPH */}
              {activeSubTab === 'history' && (
                <div className="max-w-4xl space-y-3">
                  <div className="text-xs font-bold text-[var(--text-dim,#64748B)] uppercase tracking-wider">
                    Visual Commit Graph & Timeline ({commits.length})
                  </div>
                  <div className="space-y-2">
                    {commits.map(c => (
                      <div
                        key={c.hash}
                        className="p-3 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between"
                      >
                        <div className="flex items-center gap-3">
                          <span className="px-2 py-0.5 rounded text-[10px] font-mono bg-sky-500/10 text-accent border border-sky-500/20">
                            {c.shortHash}
                          </span>
                          <div>
                            <div className="text-xs font-semibold text-[var(--text-main,#F1F5F9)]">{c.subject}</div>
                            <div className="text-[11px] text-[var(--text-dim,#64748B)] font-mono">
                              {c.author} • {c.date}
                            </div>
                          </div>
                        </div>

                        <div className="flex items-center gap-2">
                          {c.stats && (
                            <span className="text-xs font-mono text-[var(--text-dim,#64748B)]">{c.stats}</span>
                          )}
                          <button
                            type="button"
                            onClick={() => openBranchModal(c.hash, c.subject)}
                            className="rounded-md border border-surface-border px-2 py-0.5 font-mono text-[10px] text-content-muted transition-colors hover:border-accent hover:text-accent"
                            title="Create a branch from this commit. The current branch is not changed."
                          >
                            Branch from here
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Branch from a commit. Nothing here moves the current branch. */}
              {branchTarget
                ? createPortal(
                    <div
                      className="fixed inset-0 z-[120] flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm"
                      onClick={() => setBranchTarget(null)}
                      role="dialog"
                      aria-modal="true"
                    >
                      <div
                        className="w-full max-w-md space-y-3 rounded-2xl border border-surface-border bg-surface-card p-5 shadow-2xl"
                        onClick={event => event.stopPropagation()}
                      >
                        <h3 className="text-sm font-bold text-content-main">Create a branch from this commit</h3>

                        <div className="rounded-lg border border-surface-border bg-surface-deep/60 p-2.5">
                          <div className="font-mono text-[10px] text-accent">{branchTarget.hash.slice(0, 7)}</div>
                          <div className="truncate text-[11px] text-content-main" title={branchTarget.subject}>
                            {branchTarget.subject}
                          </div>
                        </div>

                        <label className="block space-y-1">
                          <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Branch name</span>
                          <input
                            value={branchName}
                            onChange={event => setBranchName(event.target.value)}
                            className="w-full rounded-lg border border-surface-border bg-surface-deep px-3 py-1.5 font-mono text-[11px] text-content-main focus:border-accent focus:outline-none"
                          />
                        </label>

                        <label className="flex items-center gap-2 text-[11px] text-content-main">
                          <input
                            type="checkbox"
                            checked={branchSwitch}
                            onChange={event => setBranchSwitch(event.target.checked)}
                            className="h-3.5 w-3.5 accent-sky-500"
                          />
                          Switch to it after creating
                        </label>

                        <p className="text-[10.5px] leading-relaxed text-content-dim">
                          The current branch is not changed and no commit is discarded. Switching may be
                          refused if you have uncommitted changes, and that is reported rather than forced.
                        </p>

                        {branchError ? <p className="text-[11px] text-state-danger">{branchError}</p> : null}

                        <div className="flex justify-end gap-2 pt-1">
                          <button
                            type="button"
                            onClick={() => setBranchTarget(null)}
                            className="rounded-lg border border-surface-border px-3 py-1.5 text-[11px] text-content-muted transition-colors hover:text-content-main"
                          >
                            Cancel
                          </button>
                          <button
                            type="button"
                            disabled={branchBusy || branchName.trim().length === 0}
                            onClick={() => void createBranch()}
                            className="rounded-lg bg-sky-600 px-3 py-1.5 text-[11px] font-medium text-white transition-colors hover:bg-sky-500 disabled:opacity-50"
                          >
                            {branchBusy ? 'Creating...' : 'Create branch'}
                          </button>
                        </div>
                      </div>
                    </div>,
                    document.body
                  )
                : null}

              {/* 4. RUN & LAUNCHERS */}
              {activeSubTab === 'run' && (
                <div className="max-w-4xl space-y-4">
                  <div className="text-xs font-bold text-[var(--text-dim,#64748B)] uppercase tracking-wider">
                    Detected Project Launchers ({launchers.length})
                  </div>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    {launchers.map(l => (
                      <div
                        key={l.id}
                        className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between"
                      >
                        <div>
                          <div className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">{l.name}</div>
                          <div className="text-[11px] font-mono text-accent mt-0.5">{l.command}</div>
                          <div className="text-[10px] text-[var(--text-dim,#64748B)] uppercase mt-1">{l.type}</div>
                        </div>
                        <Button
                          variant="primary"
                          size="xs"
                          leftIcon={<Play className="w-3.5 h-3.5" />}
                          onClick={() => executeLauncher(selectedRepo.id, l.id)}
                        >
                          Run
                        </Button>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* 5. HEALTH */}
              {activeSubTab === 'health' && (
                <div className="max-w-4xl space-y-4">
                  <div className="text-xs font-bold text-[var(--text-dim,#64748B)] uppercase tracking-wider">
                    Repository Health & Diagnostics
                  </div>
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                    {healthFindings.map(h => (
                      <div
                        key={h.id}
                        className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] space-y-1.5"
                      >
                        <div className="flex items-center justify-between">
                          <span className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">{h.title}</span>
                          <span
                            className={`px-2 py-0.5 rounded text-[10px] font-bold ${
                              h.status === 'Pass'
                                ? 'bg-emerald-500/10 text-state-success border border-emerald-500/20'
                                : 'bg-amber-500/10 text-state-warning border border-amber-500/20'
                            }`}
                          >
                            {h.status}
                          </span>
                        </div>
                        <p className="text-[11px] text-[var(--text-dim,#64748B)] leading-relaxed">{h.details}</p>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* 6. NOTES */}
              {activeSubTab === 'notes' && (
                <div className="max-w-4xl space-y-3">
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-[var(--text-dim,#64748B)]">Project scratchpad and Markdown notes (auto-saved):</span>
                    {notesSaved && <span className="text-xs text-state-success flex items-center gap-1 font-mono"><Check className="w-3.5 h-3.5" /> Saved</span>}
                  </div>
                  <textarea
                    rows={12}
                    value={repoNotes}
                    onChange={e => saveNotes(selectedRepo.id, e.target.value)}
                    placeholder="# Project Notes&#10;&#10;Record objectives, architecture notes, active task list, or references here..."
                    className="w-full p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] text-xs text-[var(--text-main,#F1F5F9)] font-mono leading-relaxed focus:outline-none focus:border-sky-500"
                  />
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className="flex-1 flex items-center justify-center text-xs text-[var(--text-dim,#64748B)]">
            Select a repository on the left to inspect its details and changes.
          </div>
        )}
      </div>
    </div>
  );
};
