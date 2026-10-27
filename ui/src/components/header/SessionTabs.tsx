import React, { useState } from 'react';
import { Plus, X, Pin, MessageSquare, Edit2, Check } from 'lucide-react';
import { useSessionStore } from '../../store/useSessionStore';

export const SessionTabs: React.FC = () => {
  const {
    sessions,
    activeSessionId,
    switchSession,
    createSession,
    deleteSession,
    pinSession,
    renameSession,
  } = useSessionStore();

  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingTitle, setEditingTitle] = useState('');

  const sessionList = Object.values(sessions).sort((a, b) => {
    // Pinned sessions first, then newest
    if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1;
    return b.updatedAt - a.updatedAt;
  });

  const startRename = (id: string, currentTitle: string, e: React.MouseEvent) => {
    e.stopPropagation();
    setEditingId(id);
    setEditingTitle(currentTitle);
  };

  const saveRename = (id: string) => {
    if (editingTitle.trim()) {
      renameSession(id, editingTitle.trim());
    }
    setEditingId(null);
  };

  return (
    <div className="flex items-center space-x-1 overflow-x-auto no-scrollbar py-1 text-xs select-none max-w-full">
      {sessionList.map(session => {
        const isActive = session.id === activeSessionId;
        const isEditing = editingId === session.id;

        return (
          <div
            key={session.id}
            onClick={() => switchSession(session.id)}
            onDoubleClick={e => startRename(session.id, session.title, e)}
            className={`group relative flex items-center space-x-2 px-3 py-1.5 rounded-lg border cursor-pointer font-sans transition-all max-w-[200px] flex-shrink-0 ${
              isActive
                ? 'bg-surface-card border-surface-border text-content-main shadow-sm'
                : 'bg-surface-card border-transparent text-content-muted hover:text-content-main hover:bg-surface-hover'
            }`}
            title={session.title}
          >
            {/* Status dot or pin icon */}
            {session.isPinned ? (
              <Pin className="w-3 h-3 text-accent flex-shrink-0 fill-current rotate-45" />
            ) : (
              <span
                className={`w-1.5 h-1.5 rounded-full flex-shrink-0 ${
                  session.status === 'running' || session.status === 'thinking' || session.status === 'tool_executing'
                    ? 'bg-emerald-400 animate-pulse'
                    : isActive
                    ? 'bg-sky-400'
                    : 'bg-surface-border'
                }`}
              />
            )}

            {/* Title / Rename input */}
            {isEditing ? (
              <div className="flex items-center space-x-1" onClick={e => e.stopPropagation()}>
                <input
                  type="text"
                  autoFocus
                  value={editingTitle}
                  onChange={e => setEditingTitle(e.target.value)}
                  onKeyDown={e => {
                    if (e.key === 'Enter') saveRename(session.id);
                    if (e.key === 'Escape') setEditingId(null);
                  }}
                  onBlur={() => saveRename(session.id)}
                  className="w-24 px-1 py-0.5 bg-surface-code border border-sky-500 rounded text-xs text-content-main focus:outline-none font-sans"
                />
                <button onClick={() => saveRename(session.id)} className="text-state-success">
                  <Check className="w-3 h-3" />
                </button>
              </div>
            ) : (
              <span className="truncate text-xs font-medium">{session.title}</span>
            )}

            {/* Actions: Pin & Close */}
            <div className="flex items-center space-x-1 opacity-0 group-hover:opacity-100 transition-opacity">
              <button
                type="button"
                onClick={e => {
                  e.stopPropagation();
                  pinSession(session.id);
                }}
                className={`p-0.5 rounded hover:text-content-main ${
                  session.isPinned ? 'text-accent' : 'text-content-dim'
                }`}
                title={session.isPinned ? 'Unpin session' : 'Pin session'}
              >
                <Pin className="w-2.5 h-2.5" />
              </button>
              <button
                type="button"
                onClick={e => {
                  e.stopPropagation();
                  deleteSession(session.id);
                }}
                className="p-0.5 rounded text-content-dim hover:text-state-danger"
                title="Close session"
              >
                <X className="w-2.5 h-2.5" />
              </button>
            </div>
          </div>
        );
      })}

      {/* New Session Button */}
      <button
        type="button"
        onClick={() => createSession()}
        className="flex items-center justify-center w-7 h-7 rounded-lg bg-surface-card border border-surface-border hover:border-surface-border hover:bg-surface-strong text-content-muted hover:text-accent transition flex-shrink-0"
        title="Create New Session"
      >
        <Plus className="w-3.5 h-3.5" />
      </button>
    </div>
  );
};
