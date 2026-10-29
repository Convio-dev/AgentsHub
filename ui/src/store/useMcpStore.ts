import { create } from 'zustand';
import { webviewBridge } from '../services/webviewBridge';

export type McpTransport = 'stdio' | 'sse';
export type McpAuthStatus = 'Authenticated' | 'NeedsAuth' | 'TokenExpired' | 'NotRequired';

export interface McpServer {
  id: string;
  name: string;
  command: string;
  transport: McpTransport;
  serverUrl?: string | null;
  scope: string;
  isEnabled: boolean;
  description: string;
  toolCount: number;
  /** Tool names cached by the CLI, when it has run this server. */
  tools: string[];
  requiresAuth: boolean;
  authStatus: McpAuthStatus;
  authDetails: string;
  authKeyName?: string | null;
}

export interface NewMcpServer {
  name: string;
  transport: McpTransport;
  command: string;
  args: string[];
  serverUrl: string;
  description: string;
}

export interface AgentSkill {
  id: string;
  name: string;
  description: string;
  path: string;
  directoryPath: string;
  scope: 'Workspace' | 'Global' | string;
  /** Which workspace owns a Workspace-scoped skill. Empty for global ones. */
  workspaceName?: string | null;
  isEnabled: boolean;
  tokenFootprintApprox: number;
}

interface McpState {
  servers: McpServer[];
  skills: AgentSkill[];
  isLoading: boolean;
  /** True only for the very first scan, so the view can show a skeleton instead of an empty list. */
  hasLoaded: boolean;
  error: string | null;
  /** Skill directory currently being toggled, for a per-row spinner. */
  pendingSkill: string | null;
  /** Server name with an auth request in flight. */
  pendingAuth: string | null;
  /** Epoch ms of the last completed scan, so the header can show that a sync actually ran. */
  lastSyncedAt: number | null;

  load: (force?: boolean) => Promise<void>;
  saveServer: (draft: NewMcpServer) => Promise<boolean>;
  deleteServer: (id: string) => Promise<boolean>;
  revealSkill: (path: string) => Promise<void>;
  toggleSkill: (directoryPath: string, enabled: boolean) => Promise<boolean>;
  saveAuth: (serverName: string, keyName: string, keyValue: string) => Promise<boolean>;
  revokeAuth: (server: McpServer) => Promise<boolean>;
}

function normaliseServers(raw: unknown): McpServer[] {
  if (!Array.isArray(raw)) return [];

  return raw
    .filter((entry): entry is Record<string, unknown> => Boolean(entry) && typeof entry === 'object')
    .map(entry => ({
      id: String(entry.id ?? entry.name ?? ''),
      name: String(entry.name ?? ''),
      command: String(entry.command ?? ''),
      transport: (entry.transport === 'sse' ? 'sse' : 'stdio') as McpTransport,
      serverUrl: typeof entry.serverUrl === 'string' ? entry.serverUrl : null,
      scope: String(entry.scope ?? 'Global'),
      isEnabled: entry.isEnabled !== false,
      description: String(entry.description ?? ''),
      toolCount: typeof entry.toolCount === 'number' ? entry.toolCount : 0,
      tools: Array.isArray(entry.tools) ? entry.tools.filter((t): t is string => typeof t === 'string') : [],
      requiresAuth: entry.requiresAuth === true,
      authStatus: (entry.authStatus as McpAuthStatus) ?? 'NotRequired',
      authDetails: String(entry.authDetails ?? ''),
      authKeyName: typeof entry.authKeyName === 'string' ? entry.authKeyName : null,
    }))
    .filter(server => server.name.length > 0);
}

function normaliseSkills(raw: unknown): AgentSkill[] {
  if (!Array.isArray(raw)) return [];

  return raw
    .filter((entry): entry is Record<string, unknown> => Boolean(entry) && typeof entry === 'object')
    .map(entry => ({
      id: String(entry.id ?? entry.name ?? ''),
      name: String(entry.name ?? ''),
      description: String(entry.description ?? ''),
      path: String(entry.path ?? ''),
      directoryPath: String(entry.directoryPath ?? ''),
      scope: String(entry.scope ?? 'Workspace'),
      workspaceName: typeof entry.workspaceName === 'string' ? entry.workspaceName : null,
      isEnabled: entry.isEnabled !== false,
      tokenFootprintApprox: typeof entry.tokenFootprintApprox === 'number' ? entry.tokenFootprintApprox : 0,
    }))
    .filter(skill => skill.name.length > 0);
}

/**
 * MCP servers and agent skills as the host actually discovers them.
 *
 * Nothing here is seeded: an empty list means discovery found nothing, and the view says so rather
 * than showing placeholder cards.
 */
export const useMcpStore = create<McpState>((set, get) => ({
  servers: [],
  skills: [],
  isLoading: false,
  hasLoaded: false,
  error: null,
  pendingSkill: null,
  pendingAuth: null,
  lastSyncedAt: null,

  load: async (force = false) => {
    // A click on Sync must always re-scan: the guard only prevents two overlapping scans.
    if (get().isLoading && !force) return;
    set({ isLoading: true, error: null });

    try {
      const payload: any = await webviewBridge.invoke('get_mcp_and_skills');
      set({
        servers: normaliseServers(payload?.servers),
        skills: normaliseSkills(payload?.skills),
        isLoading: false,
        hasLoaded: true,
        error: null,
        lastSyncedAt: Date.now(),
      });
    } catch (err) {
      set({
        isLoading: false,
        hasLoaded: true,
        error: err instanceof Error ? err.message : 'Discovery failed',
      });
    }
  },

  saveServer: async draft => {
    if (!draft.name.trim()) return false;

    try {
      await webviewBridge.invoke('save_mcp_server', {
        server: {
          name: draft.name.trim(),
          command: draft.transport === 'stdio' ? draft.command.trim() || 'npx' : '',
          args: draft.transport === 'stdio' ? draft.args : [],
          transport: draft.transport,
          serverUrl: draft.transport === 'sse' ? draft.serverUrl.trim() : null,
          scope: 'Global',
          isEnabled: true,
          description: draft.description.trim(),
        },
      });

      // The host rewrites its config file, so re-read it rather than trusting the draft.
      await get().load(true);
      return true;
    } catch {
      return false;
    }
  },

  deleteServer: async id => {
    if (!id) return false;
    try {
      await webviewBridge.invoke('delete_mcp_server', { id });
      await get().load(true);
      return true;
    } catch (err) {
      set({ error: err instanceof Error ? err.message : 'Could not remove this server.' });
      return false;
    }
  },

  revealSkill: async path => {
    if (!path) return;
    try {
      await webviewBridge.invoke('reveal_skill_in_explorer', { path });
    } catch {
      // Opening a folder is a convenience; a failure needs no user-facing error.
    }
  },

  toggleSkill: async (directoryPath, enabled) => {
    if (!directoryPath || get().pendingSkill) return false;
    set({ pendingSkill: directoryPath });

    try {
      const payload: any = await webviewBridge.invoke('toggle_skill', { directoryPath, enabled });
      if (!payload?.success) {
        // Silence here was the problem: a refused toggle looked like a dead switch.
        set({ error: `Could not ${enabled ? 'enable' : 'disable'} this skill. The host refused the rename.` });
        return false;
      }
      set({ error: null });

      // The host returns the refreshed list, so the row flips without a second round trip.
      if (Array.isArray(payload.skills)) {
        set({ skills: normaliseSkills(payload.skills) });
      } else {
        await get().load();
      }
      return true;
    } catch (err) {
      set({ error: err instanceof Error ? err.message : 'Could not change this skill.' });
      return false;
    } finally {
      set({ pendingSkill: null });
    }
  },

  saveAuth: async (serverName, keyName, keyValue) => {
    if (!serverName || !keyName || get().pendingAuth) return false;
    set({ pendingAuth: serverName });

    try {
      const payload: any = await webviewBridge.invoke('save_mcp_auth', {
        serverName,
        keyName,
        keyValue,
      });
      if (!payload?.success) return false;

      // Re-run discovery so the auth badge reflects the key that was just written.
      if (Array.isArray(payload.servers)) {
        set({ servers: normaliseServers(payload.servers) });
      } else {
        await get().load();
      }
      return true;
    } catch {
      return false;
    } finally {
      set({ pendingAuth: null });
    }
  },

  revokeAuth: async server => {
    if (!server.authKeyName) return false;
    return get().saveAuth(server.name, server.authKeyName, '');
  },
}));
