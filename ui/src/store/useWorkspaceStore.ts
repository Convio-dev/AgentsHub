/**
 * Workspace and Repository File Tree Store
 * Coordinates active project workspaces, branches, and session history from C# host.
 */

import { create } from 'zustand';
import { ProjectWorkspace, WorkspaceFileItem, BackendSessionItem } from '../types/agent';
import { webviewBridge } from '../services/webviewBridge';

interface WorkspaceStoreState {
  workspaces: ProjectWorkspace[];
  activeWorkspaceId: string;
  activeWorkspace: ProjectWorkspace | null;
  branch: string;
  files: WorkspaceFileItem[];
  backendSessions: BackendSessionItem[];
  isLoadingFiles: boolean;
  isLoadingSessions: boolean;

  // Actions
  loadWorkspaces: () => Promise<void>;
  switchWorkspace: (workspaceId: string) => Promise<void>;
  /** Works with no folder at all, which is what the Unfiled group holds. */
  clearWorkspace: () => void;
  saveWorkspace: (ws: ProjectWorkspace) => Promise<boolean>;
  deleteWorkspace: (id: string) => Promise<boolean>;
  loadWorkspaceFiles: (pathOverride?: string) => Promise<void>;
  loadBackendSessions: () => Promise<void>;
  deleteBackendSession: (session: BackendSessionItem) => Promise<void>;
  setWorkspacesData: (workspaces: ProjectWorkspace[], activeId?: string) => void;
}

export const useWorkspaceStore = create<WorkspaceStoreState>((set, get) => ({
  workspaces: [],
  activeWorkspaceId: '',
  activeWorkspace: null,
  branch: 'main',
  files: [],
  backendSessions: [],
  isLoadingFiles: false,
  isLoadingSessions: false,

  setWorkspacesData: (workspaces: ProjectWorkspace[], activeId?: string) => {
    const currentActiveId = activeId || get().activeWorkspaceId || workspaces[0]?.id || '';
    const active = workspaces.find(w => w.id === currentActiveId) || workspaces[0] || null;
    set({
      workspaces,
      activeWorkspaceId: active ? active.id : currentActiveId,
      activeWorkspace: active,
    });
  },

  loadWorkspaces: async () => {
    try {
      const res = await webviewBridge.invoke('get_workspaces');
      if (res && Array.isArray(res.workspaces)) {
        get().setWorkspacesData(res.workspaces, res.activeWorkspaceId);
      }
    } catch (err) {
      console.warn('[WorkspaceStore] Could not load workspaces:', err);
    }
  },

  clearWorkspace: () => {
    set({ activeWorkspaceId: '', activeWorkspace: null, files: [], branch: 'main' });
  },

  switchWorkspace: async (workspaceId: string) => {
    const ws = get().workspaces.find(w => w.id === workspaceId);
    if (!ws) return;

    set({
      activeWorkspaceId: workspaceId,
      activeWorkspace: ws,
    });

    try {
      const res = await webviewBridge.invoke('activate_workspace', { id: workspaceId, workspaceId });
      if (res && Array.isArray(res.workspaces)) {
        get().setWorkspacesData(res.workspaces, workspaceId);
      }
      // Reload files & sessions for newly activated workspace
      await get().loadWorkspaceFiles(ws.workingDirectory);
      await get().loadBackendSessions();
    } catch (err) {
      console.warn(`[WorkspaceStore] Switch to workspace ${workspaceId} failed:`, err);
    }
  },

  saveWorkspace: async (ws: ProjectWorkspace): Promise<boolean> => {
    try {
      const res = await webviewBridge.invoke('save_workspace', { workspace: ws });
      if (res && res.success) {
        if (Array.isArray(res.workspaces)) {
          get().setWorkspacesData(res.workspaces, res.activeWorkspaceId);
        } else {
          // Update locally
          const updated = get().workspaces.filter(w => w.id !== ws.id).concat(ws);
          get().setWorkspacesData(updated, ws.id);
        }
        return true;
      }
    } catch (err) {
      console.error('[WorkspaceStore] Save workspace error:', err);
    }
    return false;
  },

  deleteWorkspace: async (id: string): Promise<boolean> => {
    try {
      const res = await webviewBridge.invoke('delete_workspace', { id });
      if (res && res.success) {
        if (Array.isArray(res.workspaces)) {
          get().setWorkspacesData(res.workspaces, res.activeWorkspaceId);
        } else {
          const updated = get().workspaces.filter(w => w.id !== id);
          get().setWorkspacesData(updated);
        }
        return true;
      }
    } catch (err) {
      console.error('[WorkspaceStore] Delete workspace error:', err);
    }
    return false;
  },

  loadWorkspaceFiles: async (pathOverride?: string) => {
    try {
      set({ isLoadingFiles: true });
      const targetDir = pathOverride || get().activeWorkspace?.workingDirectory;
      const res = await webviewBridge.invoke('get_workspace_files', targetDir ? { path: targetDir } : {});
      if (res && Array.isArray(res.files)) {
        set({
          files: res.files,
          branch: res.branch || 'main',
          isLoadingFiles: false,
        });
        return;
      }
    } catch (err) {
      console.warn('[WorkspaceStore] Load workspace files error:', err);
    }
    set({ isLoadingFiles: false });
  },

  loadBackendSessions: async () => {
    try {
      set({ isLoadingSessions: true });
      const activeWs = get().activeWorkspace;
      const res = await webviewBridge.invoke('get_agent_sessions', activeWs?.workingDirectory ? { workspaceDir: activeWs.workingDirectory } : {});
      if (res && Array.isArray(res.sessions)) {
        set({ backendSessions: res.sessions, isLoadingSessions: false });
        return;
      }
    } catch (err) {
      console.warn('[WorkspaceStore] Load backend sessions error:', err);
    }
    set({ isLoadingSessions: false });
  },

  deleteBackendSession: async (session: BackendSessionItem) => {
    try {
      if (session.transcriptPath) {
        await webviewBridge.invoke('delete_agent_session', { transcriptPath: session.transcriptPath });
      }
      set(state => ({
        backendSessions: state.backendSessions.filter(s => s.sessionId !== session.sessionId),
      }));
    } catch (err) {
      console.error('[WorkspaceStore] Delete session failed:', err);
    }
  },
}));
