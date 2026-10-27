import React, { useState } from 'react';
import {
  Plus,
  ChevronDown,
  ChevronRight,
  GitBranch,
  Trash2,
  Folder,
  FileCode,
  FileText,
  File,
  Settings,
  Zap,
  Loader2,
} from 'lucide-react';
import { useWorkspaceStore } from '../../store/useWorkspaceStore';
import { useSessionStore } from '../../store/useSessionStore';
import { useProfileStore } from '../../store/useProfileStore';
import { useSidecarStore } from '../../store/useSidecarStore';
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
  } = useSessionStore();

  const { activeProfileName, profiles, switchActiveProfile } = useProfileStore();
  const { setActiveView } = useSidecarStore();

  const [collapsedGroups, setCollapsedGroups] = useState<Record<string, boolean>>({});

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
    isLocal: true,
  }));

  const allSessions: Array<BackendSessionItem & { isLocal?: boolean }> = [...localSessionList];
  backendSessions.forEach(bs => {
    if (!allSessions.some(s => s.sessionId === bs.sessionId)) {
      allSessions.push(bs);
    }
  });

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
              return (
                <div
                  key={s.sessionId}
                  onClick={() => {
                    switchSession(s.sessionId);
                    setActiveView('chat');
                  }}
                  className={`group w-full px-2.5 py-2 rounded-lg text-xs flex items-center space-x-2 transition cursor-pointer select-none ${
                    isActive
                      ? 'bg-surface-card border border-surface-border text-content-main shadow-sm'
                      : 'hover:bg-surface-card text-content-muted hover:text-content-main'
                  }`}
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

        <div className="relative">
          <select
            value={activeWorkspaceId}
            onChange={e => switchWorkspace(e.target.value)}
            className="w-full bg-surface-card border border-surface-border text-xs text-content-main rounded-lg px-2.5 py-1.5 focus:outline-none focus:border-sky-500 cursor-pointer appearance-none pr-8 font-mono shadow-sm"
          >
            {workspaces.map(ws => (
              <option key={ws.id} value={ws.id}>
                {ws.name} {ws.id === activeWorkspaceId ? '(active)' : ''}
              </option>
            ))}
            {workspaces.length === 0 && <option value="">Loading workspaces...</option>}
          </select>
          <ChevronDown className="w-3.5 h-3.5 text-content-dim absolute right-2.5 top-2.5 pointer-events-none" />
        </div>

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
        {renderSessionGroup('Today', groups.today, 'today')}
        {renderSessionGroup('Yesterday', groups.yesterday, 'yesterday')}
        {renderSessionGroup('Earlier', groups.earlier, 'earlier')}

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
          <span className="text-[10px] uppercase tracking-wider text-content-dim font-semibold">
            Files
          </span>
          <span className="text-[10px] text-sky-400 font-mono flex items-center gap-1">
            <GitBranch className="w-2.5 h-2.5" />
            <span>{branch}</span>
          </span>
        </div>

        <div className="space-y-0.5 text-xs text-content-muted font-mono overflow-y-auto flex-1">
          {isLoadingFiles ? (
            <div className="py-2 px-1 text-[11px] text-content-dim italic flex items-center gap-1.5">
              <Loader2 className="w-3 h-3 animate-spin text-sky-400" />
              <span>Scanning files...</span>
            </div>
          ) : files.length === 0 ? (
            <div className="py-2 text-[11px] text-content-dim italic">Workspace empty</div>
          ) : (
            files.slice(0, 30).map(file => (
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
