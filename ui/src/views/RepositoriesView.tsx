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
              <span className="text-xl font-bold font-mono text-sky-400 mt-0.5 block">{totalRepos}</span>
            </div>
            <div className="w-9 h-9 rounded-lg bg-sky-500/10 border border-sky-500/20 flex items-center justify-center text-sky-400">
              <Folder className="w-4 h-4" />
            </div>
          </div>

          <div className="p-3.5 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between">
            <div>
              <span className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider block">Clean Trees</span>
              <span className="text-xl font-bold font-mono text-emerald-400 mt-0.5 block">{cleanRepos}</span>
            </div>
            <div className="w-9 h-9 rounded-lg bg-emerald-500/10 border border-emerald-500/20 flex items-center justify-center text-emerald-400">
              <CheckCircle2 className="w-4 h-4" />
            </div>
          </div>

          <div className="p-3.5 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between">
            <div>
              <span className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider block">Modified Trees</span>
              <span className="text-xl font-bold font-mono text-amber-400 mt-0.5 block">{dirtyRepos}</span>
            </div>
            <div className="w-9 h-9 rounded-lg bg-amber-500/10 border border-amber-500/20 flex items-center justify-center text-amber-400">
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
              <div className="w-8 h-8 rounded-lg bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-sky-400">
                <Activity className="w-4 h-4" />
              </div>
              <div>
                <div className="text-[10px] font-bold text-sky-400 uppercase tracking-wider">Working On Now</div>
                <div className="text-xs font-bold text-[var(--text-main,#F1F5F9)] flex items-center gap-2">
                  <span>{workingRepo.name}</span>
                  <span className="text-[10px] px-1.5 py-0.2 rounded bg-sky-500/10 text-sky-400 border border-sky-500/20 font-mono">
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
                    ? 'bg-sky-500/15 text-sky-400 font-semibold border border-sky-500/30'
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
            <span className="flex items-center gap-1.5 text-xs text-sky-400 font-mono">
              <RefreshCw className="w-3 h-3 animate-spin" />
              <span>Scanning...</span>
              <button onClick={cancelRepoScan} className="text-rose-400 hover:text-rose-300 ml-1">
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
              <span className="font-semibold text-sky-400">{selectedBatchRepoIds.size} selected</span>
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
                        <span className="px-1.5 py-0.2 rounded text-[10px] font-bold bg-sky-500/20 text-sky-400 border border-sky-500/30">
                          WORKING
                        </span>
                      )}
                      <span
                        className={`px-1.5 py-0.2 rounded text-[10px] font-mono ${
                          repo.status === 'clean'
                            ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                            : 'bg-amber-500/10 text-amber-400 border border-amber-500/20'
                        }`}
                      >
                        {repo.status === 'clean' ? 'Clean' : 'Modified'}
                      </span>
                    </div>
                  </div>

                  <div className="flex items-center gap-2 mt-1 text-[11px] text-[var(--text-dim,#64748B)] font-mono">
                    <span className="flex items-center gap-1">
                      <GitBranch className="w-3 h-3 text-sky-400" />
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
                  <div className="w-7 h-7 rounded-lg bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-sky-400">
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
                    leftIcon={selectedRepo.id === workingProjectId ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : undefined}
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
                    {copyFeedback ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
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
                    <GitBranch className="w-3 h-3 text-sky-400" />
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
                              b === selectedRepo.branch ? 'bg-sky-500/20 text-sky-400 font-bold' : 'hover:bg-[var(--bg-input,#0D1322)] text-slate-300'
                            }`}
                          >
                            <span>{b}</span>
                            {b === selectedRepo.branch && <Check className="w-3 h-3 text-sky-400" />}
                          </button>
                        ))}
                      </div>
                    </div>
                  )}
                </div>

                <span
                  className={`px-2 py-0.5 rounded text-[11px] font-mono ${
                    selectedRepo.status === 'clean'
                      ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                      : 'bg-amber-500/10 text-amber-400 border border-amber-500/20'
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
                      ? 'border-sky-500 text-sky-400 font-bold'
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
                      <div className="text-xs font-medium text-[var(--text-main,#F1F5F9)]">{selectedRepo.lastCommitMessage || 'No commits found'}</div>
                      <div className="text-[11px] font-mono text-[var(--text-dim,#64748B)]">
                        {selectedRepo.lastCommitHash} • {selectedRepo.lastCommitAuthor} • {selectedRepo.lastCommitTime}
                      </div>
                    </div>

                    <div className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] space-y-1">
                      <div className="text-[11px] font-semibold text-[var(--text-dim,#64748B)] uppercase tracking-wider">Remote Origin</div>
                      <div className="text-xs font-mono text-sky-400 truncate">{selectedRepo.remoteUrl || 'No remote configured'}</div>
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
                                ? 'bg-sky-500/20 text-sky-400 font-semibold border border-sky-500/30'
                                : 'hover:bg-[var(--bg-input,#0D1322)] text-slate-300 border border-transparent'
                            }`}
                          >
                            <span className="truncate flex-1">{file.path}</span>
                            <span className="text-[10px] text-emerald-400 ml-2">
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
                        <span className="text-sky-400 truncate">{selectedDiffFile || 'Select a file to inspect diff'}</span>
                        <span className="text-[var(--text-dim,#64748B)]">{diffStats}</span>
                      </div>
                      <pre className="flex-1 p-4 font-mono text-xs overflow-auto bg-[var(--bg-input,#0D1322)] text-slate-300 leading-relaxed whitespace-pre">
                        {diffContent || 'No diff available for this file.'}
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
                          className="flex items-center gap-1.5 px-2.5 py-1 rounded bg-sky-500/10 text-sky-400 border border-sky-500/20 text-xs hover:bg-sky-500/20"
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
                                commitAction === 'local' ? 'bg-sky-500/20 text-sky-400 font-bold' : 'hover:bg-[var(--bg-input,#0D1322)] text-slate-300'
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
                                commitAction === 'push' ? 'bg-sky-500/20 text-sky-400 font-bold' : 'hover:bg-[var(--bg-input,#0D1322)] text-slate-300'
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
                          <span className="px-2 py-0.5 rounded text-[10px] font-mono bg-sky-500/10 text-sky-400 border border-sky-500/20">
                            {c.shortHash}
                          </span>
                          <div>
                            <div className="text-xs font-semibold text-[var(--text-main,#F1F5F9)]">{c.subject}</div>
                            <div className="text-[11px] text-[var(--text-dim,#64748B)] font-mono">
                              {c.author} • {c.date}
                            </div>
                          </div>
                        </div>

                        {c.stats && (
                          <span className="text-xs font-mono text-[var(--text-dim,#64748B)]">{c.stats}</span>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              )}

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
                          <div className="text-[11px] font-mono text-sky-400 mt-0.5">{l.command}</div>
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
                                ? 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
                                : 'bg-amber-500/10 text-amber-400 border border-amber-500/20'
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
                    {notesSaved && <span className="text-xs text-emerald-400 flex items-center gap-1 font-mono"><Check className="w-3.5 h-3.5" /> Saved</span>}
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
