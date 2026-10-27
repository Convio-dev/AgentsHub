/**
 * Sidecar Health, Hardware Telemetry, and Active View Store
 */

import { create } from 'zustand';
import { SidecarHealth, HardwareTelemetry, ActiveView } from '../types/agent';
import { webviewBridge } from '../services/webviewBridge';
import { useSessionStore } from './useSessionStore';
import { useProfileStore } from './useProfileStore';
import { useWorkspaceStore } from './useWorkspaceStore';
import { useRepositoriesStore } from './useRepositoriesStore';

interface SidecarStoreState {
  health: SidecarHealth;
  telemetry: HardwareTelemetry;
  activeView: ActiveView;
  isSidebarCollapsed: boolean;
  isBridgeConnected: boolean;

  // Actions
  setHealth: (health: Partial<SidecarHealth>) => void;
  setTelemetry: (telemetry: Partial<HardwareTelemetry>) => void;
  setActiveView: (view: ActiveView) => void;
  toggleSidebar: () => void;
  setSidebarCollapsed: (collapsed: boolean) => void;
  initBridgeListeners: () => () => void;
  refreshSidecarStatus: () => Promise<void>;
  hydrateInitialState: () => Promise<void>;
}

export const useSidecarStore = create<SidecarStoreState>((set, get) => ({
  health: {
    status: 'Offline',
    port: 0,
    pid: 0,
    isOnline: false,
    latencyMs: 0,
    activeSessions: 0,
    version: 'v2.0.0',
  },
  // Zeroed until the host reports real numbers: a plausible-looking placeholder reads as fact.
  telemetry: {
    netKbps: 0,
    netMbps: 0,
    ramMb: 0,
    totalRamMb: 0,
    memoryUsedGb: 0,
    memoryTotalGb: 0,
    hermesMemMb: 0,
  },
  activeView: (function (): ActiveView {
    try {
      const v = localStorage.getItem('agents_hub_active_view') as ActiveView;
      if (['chat', 'dashboard', 'terminals', 'repositories', 'profiles', 'mcpskills', 'devtools', 'lan', 'settings'].includes(v)) {
        return v;
      }
    } catch {}
    return 'chat';
  })(),
  isSidebarCollapsed: (function (): boolean {
    try {
      return localStorage.getItem('agents_hub_sidebar_collapsed') === 'true';
    } catch {
      return false;
    }
  })(),
  isBridgeConnected: false,

  setHealth: healthUpdate =>
    set(state => ({
      health: { ...state.health, ...healthUpdate },
      isBridgeConnected: healthUpdate.isOnline !== undefined ? healthUpdate.isOnline : state.isBridgeConnected,
    })),

  setTelemetry: telemetryUpdate =>
    set(state => ({
      telemetry: { ...state.telemetry, ...telemetryUpdate },
    })),

  setActiveView: view => {
    try {
      localStorage.setItem('agents_hub_active_view', view);
    } catch {}
    set({ activeView: view });
  },

  toggleSidebar: () => {
    const next = !get().isSidebarCollapsed;
    try {
      localStorage.setItem('agents_hub_sidebar_collapsed', String(next));
    } catch {}
    set({ isSidebarCollapsed: next });
  },

  setSidebarCollapsed: collapsed => {
    try {
      localStorage.setItem('agents_hub_sidebar_collapsed', String(collapsed));
    } catch {}
    set({ isSidebarCollapsed: collapsed });
  },

  refreshSidecarStatus: async () => {
    try {
      const res = await webviewBridge.invoke('get_sidecar_status');
      if (res) {
        get().setHealth({
          status: res.status || (res.isOnline ? 'Online' : 'Offline'),
          port: res.port || 0,
          pid: res.pid || 0,
          isOnline: !!res.isOnline,
          latencyMs: res.latencyMs || 1,
          activeSessions: res.activeSessions || 0,
          version: res.version || 'v2.0.0',
        });
      }
    } catch (err) {
      console.warn('[SidecarStore] Refresh sidecar status failed:', err);
    }
  },

  hydrateInitialState: async () => {
    try {
      const res = await webviewBridge.invoke('get_initial_state');
      if (res) {
        // Hydrate workspaces
        if (Array.isArray(res.workspaces)) {
          useWorkspaceStore.getState().setWorkspacesData(res.workspaces, res.activeWorkspaceId);
          useWorkspaceStore.getState().loadWorkspaceFiles();
          useWorkspaceStore.getState().loadBackendSessions();
        }

        // Hydrate profiles
        if (res.antigravity) {
          const ag = res.antigravity;
          const mapped = Array.isArray(ag.profiles)
            ? ag.profiles.map((p: any) => ({
                name: typeof p === 'string' ? p : p.name || 'Profile',
                email: typeof p === 'object' ? p.email : undefined,
                active: typeof p === 'object' ? !!p.active : p === ag.activeProfile,
                isRunning: typeof p === 'object' ? !!p.isRunning : false,
                // No fallback: a profile whose quota has never been read has no percentage, and
                // inventing one would show a healthy account that was never actually checked.
                quotaPercent: typeof p === 'object' ? p.quotaPercent : undefined,
                lastQuotaChecked: typeof p === 'object' ? p.lastQuotaChecked : undefined,
                provider: (typeof p === 'object' && p.provider) ? p.provider : 'Google Antigravity',
              }))
            : [];
          useProfileStore.getState().setProfilesData(mapped, ag.activeProfile, ag.model);
        }
      }

      // Also fetch system telemetry
      const tel = await webviewBridge.invoke('get_system_telemetry');
      if (tel) {
        get().setTelemetry({
          netKbps: tel.netKbps || 0,
          netMbps: tel.netMbps || 0,
          ramMb: tel.ramMb || 0,
          totalRamMb: tel.totalRamMb || 16384,
          memoryUsedGb: tel.memoryUsedGb || 0,
          memoryTotalGb: tel.memoryTotalGb || 16,
          hermesMemMb: tel.hermesMemMb || 0,
        });
      }
    } catch (err) {
      console.warn('[SidecarStore] Hydrate initial state error:', err);
    }
  },

  initBridgeListeners: () => {
    const unsubs: Array<() => void> = [];

    // 1. Sidecar Status
    unsubs.push(
      webviewBridge.on('sidecar_status_changed', (data: any) => {
        get().setHealth({
          status: data.status || (data.isOnline ? 'Online' : 'Offline'),
          port: data.port || 0,
          pid: data.pid || 0,
          isOnline: !!data.isOnline,
          latencyMs: data.latencyMs || 1,
          activeSessions: data.activeSessions || 0,
          version: data.version || 'v2.0.0',
          error: data.error,
        });
      })
    );

    // 2. Hardware Metrics
    const onMetrics = (data: any) => {
      get().setTelemetry({
        netKbps: data.netKbps || 0,
        netMbps: data.netMbps || 0,
        ramMb: data.ramMb || 0,
        totalRamMb: data.totalRamMb || 16384,
        memoryUsedGb: data.memoryUsedGb || 0,
        memoryTotalGb: data.memoryTotalGb || 16,
        hermesMemMb: data.hermesMemMb || 0,
      });
    };
    unsubs.push(webviewBridge.on('hardware_metrics', onMetrics));
    unsubs.push(webviewBridge.on('system_telemetry', onMetrics));

    // 3. Workspaces updates from C#
    unsubs.push(
      webviewBridge.on('workspaces_updated', (data: any) => {
        if (Array.isArray(data.workspaces)) {
          useWorkspaceStore.getState().setWorkspacesData(data.workspaces, data.activeWorkspaceId);
        }
      })
    );

    unsubs.push(
      webviewBridge.on('workspace_activated', (data: any) => {
        if (data.activeWorkspace) {
          const currentList = useWorkspaceStore.getState().workspaces;
          const allWorkspaces = Array.isArray(data.workspaces) && data.workspaces.length > 0
            ? data.workspaces
            : currentList.map(w => w.id === data.activeWorkspaceId ? data.activeWorkspace : w);
          useWorkspaceStore.getState().setWorkspacesData(allWorkspaces, data.activeWorkspaceId);
          useWorkspaceStore.getState().loadWorkspaceFiles(data.activeWorkspace.workingDirectory);
          useWorkspaceStore.getState().loadBackendSessions();
        }
      })
    );

    unsubs.push(
      webviewBridge.on('workspace_file_changed', () => {
        useWorkspaceStore.getState().loadWorkspaceFiles();
      })
    );

    // 4. Profiles updates from C#
    unsubs.push(
      webviewBridge.on('profile_changed', (data: any) => {
        if (Array.isArray(data.profiles)) {
          useProfileStore.getState().setProfilesData(data.profiles, data.activeProfile);
        }
      })
    );

    unsubs.push(
      webviewBridge.on('profile_saved', (data: any) => {
        if (Array.isArray(data.profiles)) {
          useProfileStore.getState().setProfilesData(data.profiles, data.activeProfile);
        }
      })
    );

    unsubs.push(
      webviewBridge.on('antigravity_instance_status_changed', (data: any) => {
        if (data.profile) {
          useProfileStore.getState().setProfileRunning(data.profile, !!data.isRunning);
        }
      })
    );

    unsubs.push(
      webviewBridge.on('repo_manager_state_changed', () => {
        useRepositoriesStore.getState().loadRepoManagerState();
      })
    );

    // 5. Streaming Chat Tokens -> Target Session
    unsubs.push(
      webviewBridge.on('chat_token', (data: any) => {
        const sessionStore = useSessionStore.getState();
        const targetSessionId = data.sessionId || sessionStore.activeSessionId;
        if (data.text) {
          sessionStore.appendChatToken(targetSessionId, data.text);
        }
      })
    );

    // 6. Live Thinking Stream -> Target Session
    unsubs.push(
      webviewBridge.on('thinking', (data: any) => {
        const sessionStore = useSessionStore.getState();
        const targetSessionId = data.sessionId || sessionStore.activeSessionId;
        sessionStore.appendThinkingChunk(
          targetSessionId,
          data.text,
          data.durationSeconds,
          data.thinkingTokens,
          data.state
        );
      })
    );

    // 7. Tool Invocation Start -> Stepper Node
    unsubs.push(
      webviewBridge.on('tool_start', (data: any) => {
        const sessionStore = useSessionStore.getState();
        const targetSessionId = data.sessionId || sessionStore.activeSessionId;
        sessionStore.startToolNode(
          targetSessionId,
          data.callId || `call_${Date.now()}`,
          data.tool || 'command',
          data.params || {}
        );
      })
    );

    // 8. Tool Invocation Finish -> Stepper Node
    unsubs.push(
      webviewBridge.on('tool_output', (data: any) => {
        const sessionStore = useSessionStore.getState();
        const targetSessionId = data.sessionId || sessionStore.activeSessionId;
        sessionStore.finishToolNode(
          targetSessionId,
          data.callId || '',
          data.tool || '',
          data.success !== false,
          data.result,
          data.error,
          data.durationMs
        );
      })
    );

    // 9. Session Status / Completion
    unsubs.push(
      webviewBridge.on('session_status', (data: any) => {
        const sessionStore = useSessionStore.getState();
        const targetSessionId = data.sessionId || sessionStore.activeSessionId;
        sessionStore.updateSessionStatus(
          targetSessionId,
          data.state || 'idle',
          data.error,
          data.pid
        );
      })
    );

    // 10. Dynamic Quota Sync — strictly per profile
    unsubs.push(
      webviewBridge.on('quota_update', (data: any) => {
        const pct = typeof data.quotaPercent === 'number' ? data.quotaPercent : parseInt(data.quotaPercent, 10);
        if (isNaN(pct)) return;

        const profile = typeof data.profile === 'string' && data.profile ? data.profile : undefined;
        if (profile) {
          useProfileStore.getState().updateProfileQuota(profile, pct);
        }

        const sessionStore = useSessionStore.getState();
        const targetSessionId = data.sessionId || sessionStore.activeSessionId;
        const sessionProfile = sessionStore.sessions[targetSessionId]?.profile;

        // A poll for profile B must never write its number onto profile A's session.
        if (profile && sessionProfile && sessionProfile.toLowerCase() !== profile.toLowerCase()) {
          return;
        }

        sessionStore.updateSessionTelemetry(targetSessionId, { quotaPercent: pct });
      })
    );

    // 10b. Quota check finished (success or failure) — always clears the per-profile spinner.
    unsubs.push(
      webviewBridge.on('quota_check_completed', (data: any) => {
        const profile = typeof data.profile === 'string' && data.profile ? data.profile : null;
        const profileStore = useProfileStore.getState();

        if (profile && typeof data.quotaPercent === 'number' && data.quotaPercent >= 0) {
          profileStore.updateProfileQuota(profile, data.quotaPercent);
        }

        profileStore.settleQuotaCheck(profile);
      })
    );

    // 11. Dynamic Model Info Sync
    unsubs.push(
      webviewBridge.on('model_info', (data: any) => {
        const sessionStore = useSessionStore.getState();
        const targetSessionId = data.sessionId || sessionStore.activeSessionId;
        sessionStore.updateSessionTelemetry(targetSessionId, {
          model: data.model,
          reasoningEffort: data.reasoningEffort,
        });
      })
    );

    // 12. Dynamic Token Counter Sync
    unsubs.push(
      webviewBridge.on('token_usage', (data: any) => {
        const sessionStore = useSessionStore.getState();
        const targetSessionId = data.sessionId || sessionStore.activeSessionId;
        const usage = data.usage || data;

        // One event per completed turn; accumulate it instead of replacing the session total.
        sessionStore.recordTurnUsage(targetSessionId, {
          inputTokens: usage.inputTokens || 0,
          outputTokens: usage.outputTokens || 0,
          thinkingTokens: usage.thinkingTokens || 0,
          totalTokens: usage.totalTokens || (usage.inputTokens || 0) + (usage.outputTokens || 0),
        });
      })
    );

    // Initial query
    get().refreshSidecarStatus();
    get().hydrateInitialState();

    return () => {
      unsubs.forEach(unsub => unsub());
    };
  },
}));
