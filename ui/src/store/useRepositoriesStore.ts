/**
 * Visual Git & Repositories Manager Store
 * Coordinates repositories scanning, git state, commit staging, branches, and project launchers.
 */

import { create } from 'zustand';
import { webviewBridge } from '../services/webviewBridge';

export interface RepositoryItem {
  id: string;
  name: string;
  path: string;
  branch: string;
  isPinned: boolean;
  status: 'clean' | 'modified' | 'ahead' | 'behind' | 'diverged' | 'conflict' | 'untracked';
  hasIssues: boolean;
  aheadCount: number;
  behindCount: number;
  remoteUrl?: string;
  lastCommitMessage?: string;
  lastCommitHash?: string;
  lastCommitAuthor?: string;
  lastCommitTime?: string;
  tags?: string[];
  notes?: string;
}

export interface ChangedFileItem {
  path: string;
  status: 'modified' | 'added' | 'deleted' | 'renamed' | 'untracked';
  isStaged: boolean;
  additions?: number;
  deletions?: number;
}

export interface GitCommitItem {
  hash: string;
  shortHash: string;
  subject: string;
  message: string;
  author: string;
  date: string;
  stats?: string;
}

export interface LauncherItem {
  id: string;
  name: string;
  command: string;
  type: string;
  icon?: string;
}

export interface HealthFinding {
  id: string;
  title: string;
  status: 'Pass' | 'Warning' | 'Fail' | 'Needs Attention';
  details: string;
  severity?: 'info' | 'warning' | 'error';
  message?: string;
}

interface RepositoriesStoreState {
  repositories: RepositoryItem[];
  selectedRepoId: string | null;
  workingProjectId: string | null;
  isScanning: boolean;
  searchFilter: string;
  activeFilter: 'all' | 'working' | 'pinned' | 'modified' | 'clean' | 'problems';
  activeSubTab: 'overview' | 'changes' | 'history' | 'launchers' | 'run' | 'health' | 'notes';
  changedFiles: ChangedFileItem[];
  selectedDiffFile: string | null;
  diffContent: string;
  diffStats: string;
  isStagingAll: boolean;
  commitMessage: string;
  commitAction: 'push' | 'commit' | 'local';
  branches: string[];
  launchers: LauncherItem[];
  healthFindings: HealthFinding[];
  commits: GitCommitItem[];
  repoNotes: string;
  notesSaved: boolean;
  selectedBatchRepoIds: Set<string>;

  // Filter and tab actions
  setSearchFilter: (query: string) => void;
  setActiveFilter: (filter: 'all' | 'working' | 'pinned' | 'modified' | 'clean' | 'problems') => void;
  setActiveSubTab: (tab: 'overview' | 'changes' | 'history' | 'launchers' | 'run' | 'health' | 'notes') => void;
  setCommitMessage: (msg: string) => void;
  setCommitAction: (action: 'push' | 'commit' | 'local') => void;
  setIsStagingAll: (stageAll: boolean) => void;
  toggleBatchSelectRepo: (id: string) => void;
  selectAllBatchRepos: () => void;
  clearBatchSelection: () => void;

  // Actions
  loadRepoManagerState: () => Promise<void>;
  startRepoScan: () => Promise<void>;
  cancelRepoScan: () => Promise<void>;
  selectRepo: (id: string) => Promise<void>;
  setWorkingProject: (id: string) => Promise<void>;
  loadFileDiff: (repoId: string, filePath: string) => Promise<void>;
  stageFile: (repoId: string, filePath: string) => Promise<void>;
  unstageFile: (repoId: string, filePath: string) => Promise<void>;
  stageAllFiles: (repoId: string) => Promise<void>;
  createCommit: (repoId: string, message?: string, push?: boolean) => Promise<void>;
  gitNetworkAction: (repoId: string, action: 'fetch' | 'pull' | 'push') => Promise<void>;
  batchNetworkAction: (action: 'fetch' | 'pull') => Promise<void>;
  switchBranch: (repoId: string, branch: string) => Promise<void>;
  executeLauncher: (repoId: string, launcherId: string) => Promise<void>;
  saveNotes: (repoId: string, notes: string) => Promise<void>;
  aiGenerateCommitMessage: (repoId: string) => Promise<void>;
}

export const useRepositoriesStore = create<RepositoriesStoreState>((set, get) => ({
  repositories: [],
  selectedRepoId: null,
  workingProjectId: null,
  isScanning: false,
  searchFilter: '',
  activeFilter: 'all',
  activeSubTab: 'overview',
  changedFiles: [],
  selectedDiffFile: null,
  diffContent: '',
  diffStats: '',
  isStagingAll: false,
  commitMessage: '',
  commitAction: 'push',
  branches: ['main'],
  launchers: [],
  healthFindings: [],
  commits: [],
  repoNotes: '',
  notesSaved: false,
  selectedBatchRepoIds: new Set(),

  setSearchFilter: searchFilter => set({ searchFilter }),
  setActiveFilter: activeFilter => set({ activeFilter }),
  setActiveSubTab: activeSubTab => set({ activeSubTab }),
  setCommitMessage: commitMessage => set({ commitMessage }),
  setCommitAction: commitAction => set({ commitAction }),
  setIsStagingAll: isStagingAll => set({ isStagingAll }),

  toggleBatchSelectRepo: (id: string) => {
    const next = new Set(get().selectedBatchRepoIds);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    set({ selectedBatchRepoIds: next });
  },

  selectAllBatchRepos: () => {
    const all = new Set(get().repositories.map(r => r.id));
    set({ selectedBatchRepoIds: all });
  },

  clearBatchSelection: () => {
    set({ selectedBatchRepoIds: new Set() });
  },

  loadRepoManagerState: async () => {
    try {
      const res = await webviewBridge.invoke('get_repo_manager_state');
      if (res && Array.isArray(res.repositories)) {
        const repos: RepositoryItem[] = res.repositories.map((r: any) => ({
          id: r.id || r.path,
          name: r.name || 'Repo',
          path: r.path || '',
          branch: r.branch || 'main',
          isPinned: Boolean(r.isPinned),
          status: r.status ? r.status.toLowerCase() : (r.isDirty ? 'modified' : 'clean'),
          hasIssues: Boolean(r.hasIssues),
          aheadCount: r.aheadCount || 0,
          behindCount: r.behindCount || 0,
          remoteUrl: r.remoteUrl,
          lastCommitMessage: r.lastCommitMessage,
          lastCommitHash: r.lastCommitHash,
          lastCommitAuthor: r.lastCommitAuthor,
          lastCommitTime: r.lastCommitTime,
          tags: Array.isArray(r.tags) ? r.tags : [],
          notes: r.notes || '',
        }));

        const selectedId = get().selectedRepoId || res.workingProjectId || (repos.length > 0 ? repos[0].id : null);
        set({
          repositories: repos,
          selectedRepoId: selectedId,
          workingProjectId: res.workingProjectId || selectedId,
          isScanning: Boolean(res.isScanning),
        });

        if (selectedId) {
          await get().selectRepo(selectedId);
        }
      }
    } catch (err) {
      console.warn('[RepositoriesStore] get_repo_manager_state failed:', err);
    }
  },

  startRepoScan: async () => {
    set({ isScanning: true });
    try {
      await webviewBridge.invoke('scan_repositories');
      await get().loadRepoManagerState();
    } catch {}
    set({ isScanning: false });
  },

  cancelRepoScan: async () => {
    try {
      await webviewBridge.invoke('cancel_repo_scan');
    } catch {}
    set({ isScanning: false });
  },

  selectRepo: async (id: string) => {
    set({ selectedRepoId: id, selectedDiffFile: null, diffContent: '', diffStats: '' });
    const repo = get().repositories.find(r => r.id === id);
    if (!repo) return;

    try {
      const [changesRes, branchesRes, launchersRes, notesRes, healthRes] = await Promise.allSettled([
        webviewBridge.invoke('get_repo_changes', { repoPath: repo.path }),
        webviewBridge.invoke('list_repo_branches', { repoPath: repo.path }),
        webviewBridge.invoke('detect_repo_launchers', { repoPath: repo.path }),
        webviewBridge.invoke('get_repo_notes', { repoId: id }),
        webviewBridge.invoke('evaluate_repo_health', { idOrPath: id }),
      ]);

      const rawChanges: any[] = changesRes.status === 'fulfilled' && Array.isArray(changesRes.value?.changes)
        ? changesRes.value.changes
        : [];
      const changedFiles: ChangedFileItem[] = rawChanges.map((c: any) => ({
        path: typeof c === 'string' ? c : (c.path || c.file || ''),
        status: (c.status || 'modified').toLowerCase(),
        isStaged: Boolean(c.isStaged || c.staged),
        additions: c.additions || 0,
        deletions: c.deletions || 0,
      }));

      const rawBranches: string[] = branchesRes.status === 'fulfilled' && Array.isArray(branchesRes.value?.branches)
        ? branchesRes.value.branches
        : [repo.branch];

      const rawLaunchers: any[] = launchersRes.status === 'fulfilled' && Array.isArray(launchersRes.value?.launchers)
        ? launchersRes.value.launchers
        : [];
      const launchers: LauncherItem[] = rawLaunchers.map((l: any) => ({
        id: l.id || `launch_${l.name || Date.now()}`,
        name: l.name || 'Script',
        command: l.command || '',
        type: l.type || 'npm script',
        icon: l.icon,
      }));

      const rawNotes: string = notesRes.status === 'fulfilled' && typeof notesRes.value?.notes === 'string'
        ? notesRes.value.notes
        : (repo.notes || '');

      let healthFindings: HealthFinding[] = [];
      if (healthRes.status === 'fulfilled' && healthRes.value) {
        const rawFindings = Array.isArray(healthRes.value.findings) ? healthRes.value.findings : [];
        healthFindings = rawFindings.map((h: any) => ({
          id: h.id || `health_${Math.random()}`,
          title: h.title || 'Check',
          status: h.status || (h.pass ? 'Pass' : 'Needs Attention'),
          details: h.details || h.message || '',
          severity: h.severity,
          message: h.message,
        }));
      }

      set({
        changedFiles,
        branches: rawBranches.length > 0 ? rawBranches : [repo.branch],
        launchers,
        repoNotes: rawNotes,
        healthFindings,
      });
    } catch (err) {
      console.warn('[RepositoriesStore] Error fetching repo details:', err);
    }
  },

  setWorkingProject: async (id: string) => {
    set({ workingProjectId: id });
    try {
      await webviewBridge.invoke('set_working_project', { projectId: id });
    } catch {}
  },

  loadFileDiff: async (repoId: string, filePath: string) => {
    set({ selectedDiffFile: filePath });
    const repo = get().repositories.find(r => r.id === repoId);
    if (!repo) return;

    const file = get().changedFiles.find(f => f.path === filePath);
    const isStaged = file?.isStaged || false;

    try {
      const res = await webviewBridge.invoke('get_repo_diff', {
        repoPath: repo.path,
        relativePath: filePath,
        staged: isStaged,
      });
      if (res) {
        const statsStr = res.stats || (res.additions !== undefined || res.deletions !== undefined
          ? `+${res.additions || 0} -${res.deletions || 0}`
          : '');
        set({
          diffContent: res.diff || '',
          diffStats: statsStr,
        });
      }
    } catch {}
  },

  stageFile: async (repoId: string, filePath: string) => {
    const repo = get().repositories.find(r => r.id === repoId);
    if (!repo) return;
    try {
      await webviewBridge.invoke('stage_repo_changes', {
        repoPath: repo.path,
        files: [filePath],
        unstage: false,
      });
      await get().selectRepo(repoId);
    } catch {}
  },

  unstageFile: async (repoId: string, filePath: string) => {
    const repo = get().repositories.find(r => r.id === repoId);
    if (!repo) return;
    try {
      await webviewBridge.invoke('stage_repo_changes', {
        repoPath: repo.path,
        files: [filePath],
        unstage: true,
      });
      await get().selectRepo(repoId);
    } catch {}
  },

  stageAllFiles: async (repoId: string) => {
    const repo = get().repositories.find(r => r.id === repoId);
    if (!repo) return;
    const allPaths = get().changedFiles.map(f => f.path);
    try {
      await webviewBridge.invoke('stage_repo_changes', {
        repoPath: repo.path,
        files: allPaths,
        unstage: false,
      });
      await get().selectRepo(repoId);
    } catch {}
  },

  createCommit: async (repoId: string, message?: string, push?: boolean) => {
    const repo = get().repositories.find(r => r.id === repoId);
    if (!repo) return;
    const msg = message ?? get().commitMessage;
    const shouldPush = push ?? (get().commitAction === 'push');
    const stageAll = get().isStagingAll;
    try {
      await webviewBridge.invoke('create_repo_commit', {
        repoPath: repo.path,
        message: msg,
        stageAll,
        pushToRemote: shouldPush,
      });
      set({ commitMessage: '' });
      await get().selectRepo(repoId);
    } catch {}
  },

  gitNetworkAction: async (repoId: string, action: 'fetch' | 'pull' | 'push') => {
    const repo = get().repositories.find(r => r.id === repoId);
    if (!repo) return;
    try {
      await webviewBridge.invoke('repo_network_action', {
        repoPath: repo.path,
        operation: action,
        remote: 'origin',
        branch: repo.branch,
      });
      await get().selectRepo(repoId);
    } catch {}
  },

  batchNetworkAction: async (action: 'fetch' | 'pull') => {
    const ids = Array.from(get().selectedBatchRepoIds);
    for (const id of ids) {
      await get().gitNetworkAction(id, action);
    }
    await get().loadRepoManagerState();
  },

  switchBranch: async (repoId: string, branch: string) => {
    const repo = get().repositories.find(r => r.id === repoId);
    if (!repo) return;
    try {
      await webviewBridge.invoke('switch_repo_branch', {
        repoPath: repo.path,
        branch,
      });
      await get().selectRepo(repoId);
    } catch {}
  },

  executeLauncher: async (repoId: string, launcherId: string) => {
    const repo = get().repositories.find(r => r.id === repoId);
    if (!repo) return;
    const launcher = get().launchers.find(l => l.id === launcherId);
    if (!launcher) return;
    try {
      await webviewBridge.invoke('run_repo_launcher', {
        repoPath: repo.path,
        launcher,
      });
    } catch {}
  },

  saveNotes: async (repoId: string, notes: string) => {
    set({ repoNotes: notes, notesSaved: true });
    try {
      await webviewBridge.invoke('save_repo_notes', { repoId, notes });
    } catch {}
    setTimeout(() => set({ notesSaved: false }), 2500);
  },

  aiGenerateCommitMessage: async (repoId: string) => {
    const repo = get().repositories.find(r => r.id === repoId);
    if (!repo) return;
    try {
      const changed = get().changedFiles.map(f => `${f.status}: ${f.path}`).join('\n');
      const prompt = `feat(${repo.name}): update ${get().changedFiles.length} file(s)\n\n${changed}`;
      set({ commitMessage: prompt });
    } catch {}
  },
}));
