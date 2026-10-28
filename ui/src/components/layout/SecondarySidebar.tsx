import React, { useState } from 'react';
import { ChevronDown, ChevronRight, File, FileCode, FileText, Filter, Folder, FolderOpen, FolderPlus, GitBranch, Loader2, Plus, Settings, Trash2, Zap } from 'lucide-react';
import { useWorkspaceStore } from '../../store/useWorkspaceStore';
import { useSessionStore } from '../../store/useSessionStore';
import { useProfileStore } from '../../store/useProfileStore';
import { createPortal } from 'react-dom';
import { useSidecarStore } from '../../store/useSidecarStore';
import { webviewBridge } from '../../services/webviewBridge';
import { BackendSessionItem, WorkspaceFileItem } from '../../types/agent';

interface SecondarySidebarProps {
  onOpenWorkspaceModal: () => void;
}

export const SecondarySidebar: React.FC<SecondarySidebarProps> = ({ onOpenWorkspaceModal }) => {
  const {
    workspaces,
    activeWorkspaceId,
    activeWorkspace,
    branch,
    files,
    backendSessions,
    isLoadingFiles,
    switchWorkspace,
    deleteBackendSession,
  } = useWorkspaceStore();

  const {
    sessions,
    activeSessionId,
    createSession,
    switchSession,
    deleteSession,
    renameSession,
  } = useSessionStore();

  const { activeProfileName, profiles, switchActiveProfile } = useProfileStore();
  const { saveWorkspace, loadWorkspaces } = useWorkspaceStore();

  // Creating a workspace belongs where you pick one, not only in the settings dialog.
  const [newWorkspace, setNewWorkspace] = useState<{ name: string; dir: string } | null>(null);
  const [newWorkspaceError, setNewWorkspaceError] = useState<string | null>(null);
  const [isNewWorkspaceMenuOpen, setIsNewWorkspaceMenuOpen] = useState(false);
  const [sessionFilter, setSessionFilter] = useState('');
  const [isFilterVisible, setIsFilterVisible] = useState(false);

  // Files: collapsible, and noise hidden by default so `__pycache__` does not push real sources out.
  const [isFilesOpen, setIsFilesOpen] = useState(
    () => localStorage.getItem('agentshub_sidebar_files_open') !== 'false'
  );
  const [showNoisyFiles, setShowNoisyFiles] = useState(
    () => localStorage.getItem('agentshub_sidebar_files_all') === 'true'
  );

  const toggleFiles = () => {
    setIsFilesOpen(open => {
      localStorage.setItem('agentshub_sidebar_files_open', String(!open));
      return !open;
    });
  };

  const toggleNoisyFiles = () => {
    setShowNoisyFiles(show => {
      localStorage.setItem('agentshub_sidebar_files_all', String(!show));
      return !show;
    });
  };

  const NOISY = /(^|[\\/])(__pycache__|node_modules|\.git|\.venv|venv|dist|build|bin|obj|target)([\\/]|$)|(\.pyc|\.pyo|\.class|\.o|\.obj|\.log)$/i;
  const visibleFiles = showNoisyFiles ? files : files.filter(file => !NOISY.test(file.path));
  const { setActiveView } = useSidecarStore();

  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>({});
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState('');

  const beginRename = (sessionId: string, currentTitle: string) => {
    setRenamingId(sessionId);
    setRenameDraft(currentTitle);
  };

  const commitRename = (sessionId: string) => {
    const title = renameDraft.trim();
    if (title) renameSession(sessionId, title);
    setRenamingId(null);
    setRenameDraft('');
  };

  const toggleGroup = (groupKey: string) => {
    setCollapsedGroups(prev => ({ ...prev, [groupKey]: !prev[groupKey] }));
  };

  const handleNewSession = () => {
    const newId = createSession(activeProfileName);
    switchSession(newId);
    setActiveView('chat');
  };

  // Merge active local sessions with backend sessions
  const localSessionList = Object.values(sessions).map(s => ({
    sessionId: s.id,
    goal: s.title,
    profile: s.profile,
    stepCount: s.messages.length,
    lastActiveUtc: new Date(s.updatedAt).toISOString(),
    workspaceId: s.workspaceId,
    isLocal: true,
  }));

  const allSessions: Array<BackendSessionItem & { isLocal?: boolean }> = [...localSessionList];
  backendSessions.forEach(bs => {
    if (!allSessions.some(s => s.sessionId === bs.sessionId)) {
      allSessions.push(bs);
    }
  });

  // Workspace tree: each project with the sessions that ran in it. Sessions created before
  // workspaces were tracked have no id and land in "Unfiled" rather than being guessed into a project.
  const workspaceGroups = [
    ...workspaces.map(ws => ({
      id: ws.id,
      name: ws.name,
      items: allSessions.filter(s => (s as any).workspaceId === ws.id),
    })),
    {
      id: '__unfiled__',
      name: 'Unfiled',
      items: allSessions.filter(s => !(s as any).workspaceId),
    },
  ]
    .map(group => ({
      ...group,
      items: sessionFilter.trim()
        ? group.items.filter(s =>
            String((s as any).goal ?? '').toLowerCase().includes(sessionFilter.trim().toLowerCase())
          )
        : group.items,
    }))
    .filter(group => group.id !== '__unfiled__' || group.items.length > 0);

  // Group sessions into Today, Yesterday, Earlier
  const now = Date.now();
  const oneDay = 86400000;
  const groups: { today: typeof allSessions; yesterday: typeof allSessions; earlier: typeof allSessions } = {
    today: [],
    yesterday: [],
    earlier: [],
  };

  allSessions.forEach(s => {
    const timestamp = s.lastActiveUtc ? new Date(s.lastActiveUtc).getTime() : now;
    const diff = now - timestamp;
    if (diff < oneDay) {
      groups.today.push(s);
    } else if (diff < 2 * oneDay) {
      groups.yesterday.push(s);
    } else {
      groups.earlier.push(s);
    }
  });

  const getFileIcon = (file: WorkspaceFileItem) => {
    if (file.isDirectory) return <Folder className="w-3.5 h-3.5 text-amber-500 flex-shrink-0" />;
    if (['.js', '.ts', '.tsx', '.jsx', '.cs', '.py', '.html', '.css'].includes(file.extension)) {
      return <FileCode className="w-3.5 h-3.5 text-sky-400 flex-shrink-0" />;
    }
    if (['.md', '.txt', '.log'].includes(file.extension)) {
      return <FileText className="w-3.5 h-3.5 text-emerald-400 flex-shrink-0" />;
    }
    return <File className="w-3.5 h-3.5 text-content-dim flex-shrink-0" />;
  };

  const renderSessionGroup = (title: string, items: typeof allSessions, groupKey: string) => {
    if (items.length === 0) return null;
    const isCollapsed = !!collapsedGroups[groupKey];

    return (
      <div key={groupKey} className="space-y-1">
        <button
          type="button"
          onClick={() => toggleGroup(groupKey)}
          className="w-full flex items-center justify-between px-2 py-1.5 text-[10px] uppercase tracking-wider text-content-dim font-semibold hover:text-content-main rounded hover:bg-surface-card transition select-none"
        >
          <span className="flex items-center space-x-1.5">
            {isCollapsed ? <ChevronRight className="w-3 h-3 text-content-dim" /> : <ChevronDown className="w-3 h-3 text-content-dim" />}
            <span>{title}</span>
          </span>
          <span className="font-mono text-[9px] px-1.5 py-0.5 rounded bg-surface-card border border-surface-border text-content-dim">
            {items.length}
          </span>
        </button>

        {!isCollapsed && (
          <div className="space-y-1">
            {items.map(s => {
              const isActive = s.sessionId === activeSessionId;
              const isRenaming = renamingId === s.sessionId;

              if (isRenaming) {
                return (
                  <div key={s.sessionId} className="flex items-center gap-1.5 rounded-lg bg-surface-card px-2.5 py-1.5">
                    <Zap className="h-3.5 w-3.5 flex-shrink-0 fill-sky-400 text-sky-400" />
                    <input
                      autoFocus
                      value={renameDraft}
                      onChange={event => setRenameDraft(event.target.value)}
                      onBlur={() => commitRename(s.sessionId)}
                      onKeyDown={event => {
                        if (event.key === 'Enter') {
                          event.preventDefault();
                          commitRename(s.sessionId);
                        }
                        if (event.key === 'Escape') {
                          event.preventDefault();
                          setRenamingId(null);
                        }
                      }}
                      className="w-full min-w-0 bg-transparent text-xs text-content-main focus:outline-none"
                    />
                  </div>
                );
              }

              return (
                <div
                  key={s.sessionId}
                  onClick={() => {
                    switchSession(s.sessionId);
                    setActiveView('chat');
                  }}
                  onDoubleClick={() => {
                    // Backend-only sessions live outside the store, so only local ones can be renamed.
                    if (s.isLocal) beginRename(s.sessionId, s.goal || '');
                  }}
                  className={`group w-full px-2.5 py-2 rounded-lg text-xs flex items-center space-x-2 transition cursor-pointer select-none ${
                    isActive
                      ? 'bg-surface-card border border-surface-border text-content-main shadow-sm'
                      : 'hover:bg-surface-card text-content-muted hover:text-content-main'
                  }`}
                  title={s.isLocal ? `${s.goal || s.sessionId} — double-click to rename` : s.goal || s.sessionId}
                >
                  <Zap className={`w-3.5 h-3.5 flex-shrink-0 ${isActive ? 'text-sky-400 fill-sky-400' : 'text-sky-500'}`} />
                  <span className="truncate flex-1 font-medium text-xs text-left" title={s.goal || s.sessionId}>
                    {s.goal || s.sessionId}
                  </span>
                  <span className="text-[10px] font-mono text-content-dim flex-shrink-0">
                    {isActive ? 'active' : s.stepCount ? `${s.stepCount} steps` : ''}
                  </span>
                  <button
                    type="button"
                    onClick={async e => {
                      e.stopPropagation();
                      if (s.isLocal) {
                        deleteSession(s.sessionId);
                      } else {
                        await deleteBackendSession(s);
                      }
                    }}
                    className="opacity-0 group-hover:opacity-100 p-1 rounded text-content-dim hover:text-rose-400 hover:bg-rose-500/10 transition flex-shrink-0"
                    title="Delete session"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                  </button>
                </div>
              );
            })}
          </div>
        )}
      </div>
    );
  };

  return (
    <aside
      id="conversations-sidebar"
      className="w-64 bg-surface-sidebar border-r border-surface-border flex flex-col flex-shrink-0 transition-all duration-200 select-none z-10"
    >
      {/* 1. Project Workspace Directory Selector */}
      <div className="p-3 border-b border-surface-border space-y-2">
        <div className="flex items-center justify-between">
          <label className="text-[10px] uppercase tracking-wider text-content-dim font-semibold block">
            Project Workspace
          </label>
          <button
            onClick={onOpenWorkspaceModal}
            className="text-content-dim hover:text-sky-400 p-0.5 rounded transition"
            title="Configure Workspaces"
          >
            <Settings className="w-3 h-3" />
          </button>
        </div>

        <div className="flex items-center justify-between px-1 pb-1">
          <span className="text-[10px] font-semibold uppercase tracking-wider text-content-dim">
            Workspaces ({workspaces.length})
          </span>

          <span className="relative flex items-center gap-1">
            <button
              type="button"
              onClick={() => setIsFilterVisible(visible => !visible)}
              title="Filter sessions"
              className={`rounded p-0.5 transition-colors hover:text-content-main ${
                isFilterVisible || sessionFilter ? 'text-accent' : 'text-content-dim'
              }`}
            >
              <Filter className="h-3 w-3" />
            </button>

            <button
              type="button"
              onClick={() => setIsNewWorkspaceMenuOpen(open => !open)}
              title="Create a new workspace"
              className="rounded p-0.5 text-content-dim transition-colors hover:text-content-main"
            >
              <FolderPlus className="h-3.5 w-3.5" />
            </button>

            {isNewWorkspaceMenuOpen ? (
              <div className="absolute right-0 top-5 z-40 w-40 overflow-hidden rounded-lg border border-surface-border bg-surface-card shadow-xl">
                <button
                  type="button"
                  onClick={() => {
                    setIsNewWorkspaceMenuOpen(false);
                    setNewWorkspace({ name: '', dir: '' });
                    setNewWorkspaceError(null);
                  }}
                  className="flex w-full items-center gap-2 px-2.5 py-1.5 text-left text-[11px] text-content-main hover:bg-surface-hover"
                >
                  <FolderPlus className="h-3 w-3" />
                  New Project
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setIsNewWorkspaceMenuOpen(false);
                    // Quick Start is a conversation, not a project: create a session with no workspace
                    // and go straight to it.
                    const id = createSession(activeProfileName, '', null);
                    switchSession(id);
                    setActiveView('chat');
                  }}
                  className="flex w-full items-center gap-2 px-2.5 py-1.5 text-left text-[11px] text-content-muted hover:bg-surface-hover hover:text-content-main"
                >
                  <Zap className="h-3 w-3" />
                  Quick Start
                </button>
              </div>
            ) : null}
          </span>
        </div>

        {isFilterVisible ? (
          <input
            value={sessionFilter}
            onChange={e => setSessionFilter(e.target.value)}
            placeholder="Filter sessions…"
            autoFocus
            className="mb-1 w-full rounded-lg border border-surface-border bg-surface-deep px-2 py-1 font-mono text-[11px] text-content-main outline-none focus:border-sky-500"
          />
        ) : null}

        <div className="relative">
          <select
            value={activeWorkspaceId}
            onChange={e => {
              if (e.target.value === '__new__') {
                setNewWorkspace({ name: '', dir: '' });
                setNewWorkspaceError(null);
                return;
              }
              switchWorkspace(e.target.value);
            }}
            className="w-full bg-surface-card border border-surface-border text-xs text-content-main rounded-lg px-2.5 py-1.5 focus:outline-none focus:border-sky-500 cursor-pointer appearance-none pr-8 font-mono shadow-sm"
          >
            {workspaces.map(ws => (
              <option key={ws.id} value={ws.id}>
                {ws.name} {ws.id === activeWorkspaceId ? '(active)' : ''}
              </option>
            ))}
            {workspaces.length === 0 && <option value="">Loading workspaces...</option>}
            <option value="__new__">+ New workspace…</option>
          </select>
          <ChevronDown className="w-3.5 h-3.5 text-content-dim absolute right-2.5 top-2.5 pointer-events-none" />
        </div>

        {newWorkspace
          ? createPortal(
              <div className="fixed inset-0 z-[60] flex items-center justify-center bg-black/50 p-4 backdrop-blur-sm">
                <div className="w-full max-w-md space-y-3 rounded-2xl border border-surface-border bg-surface-card p-5 shadow-2xl">
                  <h3 className="text-sm font-bold text-content-main">New workspace</h3>

                  <label className="block space-y-1">
                    <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Name</span>
                    <input
                      value={newWorkspace.name}
                      onChange={e => setNewWorkspace({ ...newWorkspace, name: e.target.value })}
                      placeholder="ChessSenseV2"
                      autoFocus
                      className="w-full rounded-lg border border-surface-border bg-surface-deep px-2.5 py-1.5 font-mono text-xs text-content-main outline-none focus:border-sky-500"
                    />
                  </label>

                  <div className="space-y-1">
                    <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Folder</span>
                    <div className="flex gap-1.5">
                      <input
                        value={newWorkspace.dir}
                        onChange={e => setNewWorkspace({ ...newWorkspace, dir: e.target.value })}
                        placeholder="C:/path/to/project"
                        className="min-w-0 flex-1 rounded-lg border border-surface-border bg-surface-deep px-2.5 py-1.5 font-mono text-xs text-content-main outline-none focus:border-sky-500"
                      />
                      <button
                        type="button"
                        onClick={async () => {
                          try {
                            const res: any = await webviewBridge.invoke('browse_directory', {
                              title: 'Open workspace',
                            });
                            const picked = res?.path as string | undefined;
                            if (!picked) return;

                            // Name follows the folder unless the user already typed one.
                            const folderName = picked.split(/[\\/]/).filter(Boolean).pop() || '';
                            setNewWorkspace(current => ({
                              dir: picked,
                              name: current && current.name.trim() ? current.name : folderName,
                            }));
                          } catch {
                            // Cancelling the dialog is not an error.
                          }
                        }}
                        title="Select a folder"
                        className="flex flex-shrink-0 items-center gap-1 rounded-lg border border-surface-border bg-surface-deep px-2 py-1.5 font-mono text-[10.5px] text-content-muted transition-colors hover:border-sky-500/50 hover:text-content-main"
                      >
                        <FolderOpen className="h-3.5 w-3.5" />
                        Browse
                      </button>
                    </div>
                    <span className="block text-[10px] text-content-dim">
                      The agent runs in this folder; skills live in its <span className="font-mono">.agents/skills</span>.
                    </span>
                  </div>

                  {newWorkspaceError ? (
                    <p className="text-[11px] text-state-danger">{newWorkspaceError}</p>
                  ) : null}

                  <div className="flex justify-end gap-2 pt-1">
                    <button
                      type="button"
                      onClick={() => setNewWorkspace(null)}
                      className="rounded-md border border-surface-border bg-surface-deep px-2.5 py-1 text-[11px] text-content-main"
                    >
                      Cancel
                    </button>
                    <button
                      type="button"
                      onClick={async () => {
                        const name = newWorkspace.name.trim();
                        const dir = newWorkspace.dir.trim();
                        if (!name) return setNewWorkspaceError('A name is required.');
                        if (!dir) return setNewWorkspaceError('Pick a folder for this workspace.');

                        const newId = `ws_${Date.now().toString(36)}`;
                        const ok = await saveWorkspace({ id: newId, name, workingDirectory: dir } as any);
                        if (!ok) return setNewWorkspaceError('The host could not create this workspace.');

                        await loadWorkspaces();
                        switchWorkspace(newId);
                        setNewWorkspace(null);
                      }}
                      className="rounded-md border border-sky-500/50 bg-sky-500 px-2.5 py-1 text-[11px] font-semibold text-slate-950"
                    >
                      Create
                    </button>
                  </div>
                </div>
              </div>,
              document.body
            )
          : null}

        {/* '+ New Agent Session' Button */}
        <button
          onClick={handleNewSession}
          className="w-full bg-sky-600 hover:bg-sky-500 text-white font-medium text-xs py-1.5 px-3 rounded-lg flex items-center justify-center space-x-1.5 transition shadow-sm active:scale-98"
        >
          <Plus className="w-3.5 h-3.5" />
          <span>New Agent Session</span>
        </button>
      </div>

      {/* 2. Active Runner / Profile Selector Pill */}
      <div className="px-3 py-2 bg-surface-deep border-b border-surface-border flex items-center justify-between text-xs">
        <span className="text-content-dim text-[11px]">Runner:</span>
        <div className="flex items-center space-x-1.5">
          <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
          <select
            value={activeProfileName}
            onChange={e => switchActiveProfile(e.target.value)}
            className="bg-transparent text-content-main font-mono text-[11px] focus:outline-none cursor-pointer"
          >
            {profiles.map(p => (
              <option key={p.name} value={p.name} className="bg-slate-900 text-content-main">
                {p.name}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* 3. Conversations & Work History List */}
      <div className="flex-1 overflow-y-auto px-2 py-3 space-y-3" id="sessions-scroll-container">
        {workspaceGroups.map(group => renderSessionGroup(group.name, group.items, group.id))}

        {allSessions.length === 0 && (
          <div className="py-6 text-center text-xs text-content-dim italic flex flex-col items-center gap-1.5">
            <p>No past sessions recorded.</p>
            <button onClick={handleNewSession} className="text-sky-400 hover:underline text-xs font-medium">
              Create first session &rarr;
            </button>
          </div>
        )}
      </div>

      {/* 4. Active Repo Files Tree (Live Real Filesystem) */}
      <div className="p-3 border-t border-surface-border bg-surface-deep/40 max-h-48 flex flex-col">
        <div className="flex items-center justify-between pb-1.5">
          <button
            type="button"
            onClick={toggleFiles}
            className="flex items-center gap-1 text-[10px] uppercase tracking-wider text-content-dim font-semibold transition-colors hover:text-content-main"
            title={isFilesOpen ? 'Hide the file list' : 'Show the file list'}
          >
            <ChevronDown className={`w-3 h-3 transition-transform ${isFilesOpen ? '' : '-rotate-90'}`} />
            Files
          </button>

          <span className="flex items-center gap-2">
            <button
              type="button"
              onClick={toggleNoisyFiles}
              className={`rounded border px-1 py-0.5 text-[9px] font-mono transition-colors ${
                showNoisyFiles
                  ? 'border-sky-500/40 bg-sky-500/10 text-accent'
                  : 'border-surface-border text-content-dim hover:text-content-main'
              }`}
              title={showNoisyFiles ? 'Hide build/cache files' : 'Show build and cache files'}
            >
              {showNoisyFiles ? 'all' : 'src'}
            </button>
            <span className="text-[10px] text-sky-400 font-mono flex items-center gap-1">
              <GitBranch className="w-2.5 h-2.5" />
              <span>{branch}</span>
            </span>
          </span>
        </div>

        <div className={`space-y-0.5 text-xs text-content-muted font-mono overflow-y-auto flex-1 ${isFilesOpen ? '' : 'hidden'}`}>
          {isLoadingFiles ? (
            <div className="py-2 px-1 text-[11px] text-content-dim italic flex items-center gap-1.5">
              <Loader2 className="w-3 h-3 animate-spin text-sky-400" />
              <span>Scanning files...</span>
            </div>
          ) : visibleFiles.length === 0 ? (
            <div className="py-2 text-[11px] text-content-dim italic">
              {files.length === 0 ? 'Workspace empty' : 'Only build/cache files here — press "all"'}
            </div>
          ) : (
            visibleFiles.slice(0, 30).map(file => (
              <div
                key={file.path}
                className="py-0.5 hover:text-content-main cursor-pointer flex items-center space-x-1.5 transition text-[11px] truncate group"
                title={`File: ${file.path}`}
                onClick={() => {
                  // Dispatch custom event so input dock can append reference
                  window.dispatchEvent(new CustomEvent('insert-prompt-file', { detail: { path: file.path } }));
                }}
              >
                {getFileIcon(file)}
                <span className={`truncate ${file.isDirectory ? 'font-medium text-content-main' : ''}`}>
                  {file.name}
                </span>
              </div>
            ))
          )}
        </div>
      </div>
    </aside>
  );
};
