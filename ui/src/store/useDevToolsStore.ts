/**
 * AI Dev Tools Store
 * Manages desktop development tools, paths, IDE detection, executable launching, and which of them
 * appear as quick shortcuts on the dashboard.
 */

import { create } from 'zustand';
import { webviewBridge } from '../services/webviewBridge';

export interface DevToolItem {
  id: string;
  name: string;
  target: string;
  subtitle?: string;
  description?: string;
  state: 'ready' | 'needsfixing' | 'missing' | 'empty' | 'unsupported';
  isIde?: boolean;
  enabled?: boolean;
  arguments?: string;
  workingDirectory?: string;
  /** Whether this tool is one of the dashboard's quick shortcuts. */
  showOnDashboard?: boolean;
  /** Inline image for the shortcut card, as a data URL. Empty falls back to a glyph. */
  iconDataUrl?: string;
  health?: {
    state?: string;
    text?: string;
    detail?: string;
    resolvedTarget?: string;
    kind?: string;
    launchable?: boolean;
  };
}

interface DevToolsStoreState {
  tools: DevToolItem[];
  /** Whether the dashboard shows its shortcuts card at all. Shown by default. */
  shortcutsVisible: boolean;
  isLoading: boolean;
  expandedToolId: string | null;
  feedbackMessage: string | null;

  // Actions
  loadTools: () => Promise<void>;
  launchTool: (id: string) => Promise<void>;
  toggleTool: (id: string, enabled: boolean) => Promise<void>;
  setShortcutsVisible: (visible: boolean) => Promise<void>;
  updateTool: (
    id: string,
    patch: Partial<Pick<DevToolItem, 'name' | 'iconDataUrl' | 'showOnDashboard'>>
  ) => Promise<void>;
  recheckPaths: () => Promise<void>;
  findInstalledTools: () => Promise<void>;
  browseToolTarget: (id: string, name: string) => Promise<string | null>;
  updateToolTarget: (id: string, target: string) => Promise<void>;
  addTool: (tool: { name: string; target: string; isIde?: boolean }) => Promise<void>;
  removeTool: (id: string) => Promise<void>;
  setExpandedToolId: (id: string | null) => void;
  setFeedbackMessage: (msg: string | null) => void;
}

function parseSubtitle(tool: any): string {
  const desc = (tool.description || '').trim();
  const name = (tool.name || '').trim();
  const target = (tool.target || '').trim();

  const restatesName =
    !desc ||
    desc.toLowerCase() === name.toLowerCase() ||
    (name.length > 0 && desc.toLowerCase().includes(name.toLowerCase()));

  if (!restatesName) return desc;
  if (!target) return 'No path set yet';
  return target;
}

function mapTool(raw: any): DevToolItem {
  const health = raw.health || {};
  let state: DevToolItem['state'] = 'ready';
  if (health.state === 'needsfixing' || health.state === 'missing') {
    state = 'needsfixing';
  } else if (!raw.target) {
    state = 'empty';
  }

  return {
    id: raw.id || `tool_${Date.now()}_${Math.random().toString(36).substring(2, 6)}`,
    name: raw.name || 'Unnamed Tool',
    target: raw.target || '',
    subtitle: parseSubtitle(raw),
    description: raw.description || '',
    state,
    isIde: Boolean(raw.isIde),
    enabled: raw.isEnabled !== false,
    arguments: raw.arguments || '',
    workingDirectory: raw.workingDirectory || '',
    showOnDashboard: raw.showOnDashboard === true,
    iconDataUrl: typeof raw.iconDataUrl === 'string' ? raw.iconDataUrl : '',
    health,
  };
}

/**
 * The shape the host's DevToolItem expects.
 *
 * Writing the UI model straight back lost fields: the switch is called `enabled` here and
 * `isEnabled` on the host, so toggling one row quietly re-enabled a disabled tool, and the
 * dashboard flags would be dropped the same way. Everything the host stores is mapped here.
 */
function toPayload(tool: DevToolItem) {
  return {
    id: tool.id,
    name: tool.name,
    description: tool.description || '',
    target: tool.target,
    arguments: tool.arguments || '',
    workingDirectory: tool.workingDirectory || '',
    isIde: tool.isIde ?? null,
    isEnabled: tool.enabled !== false,
    showOnDashboard: Boolean(tool.showOnDashboard),
    iconDataUrl: tool.iconDataUrl || '',
  };
}

/** Persists the whole list, since the host patches the array as one value. */
async function persistTools(tools: DevToolItem[]): Promise<void> {
  try {
    await webviewBridge.invoke('save_ai_config', { config: { devTools: tools.map(toPayload) } });
  } catch {
    // The row keeps the state it was given; the next load corrects it if the write failed.
  }
}

export const useDevToolsStore = create<DevToolsStoreState>((set, get) => ({
  tools: [],
  shortcutsVisible: true,
  isLoading: false,
  expandedToolId: null,
  feedbackMessage: null,

  setExpandedToolId: id => set({ expandedToolId: id }),
  setFeedbackMessage: msg => set({ feedbackMessage: msg }),

  loadTools: async () => {
    try {
      set({ isLoading: true });
      const res = await webviewBridge.invoke('get_dev_tools', {});
      if (res && Array.isArray(res.devTools)) {
        set({
          tools: res.devTools.map(mapTool),
          shortcutsVisible: res.shortcutsVisible !== false,
          isLoading: false,
        });
      } else {
        set({ isLoading: false });
      }
    } catch {
      set({ isLoading: false });
    }
  },

  recheckPaths: async () => {
    await get().loadTools();
    set({ feedbackMessage: 'Paths refreshed successfully.' });
    setTimeout(() => set({ feedbackMessage: null }), 3000);
  },

  findInstalledTools: async () => {
    // The host scans, then reports what it found that is not configured yet. The list itself is
    // re-read afterwards, so the panel shows the same state the config holds.
    try {
      set({ isLoading: true });
      const res: any = await webviewBridge.invoke('detect_ai_clients', {});
      const found = Array.isArray(res?.found) ? res.found.length : 0;
      await get().loadTools();
      set({
        isLoading: false,
        feedbackMessage:
          found > 0
            ? `Found ${found} installed tool${found === 1 ? '' : 's'}.`
            : 'No new installed tools were found.',
      });
    } catch {
      await get().loadTools();
      set({ isLoading: false });
    }
    setTimeout(() => set({ feedbackMessage: null }), 3500);
  },

  launchTool: async (id: string) => {
    try {
      await webviewBridge.invoke('launch_tool', { toolId: id });
      set({ feedbackMessage: 'Tool launch initiated.' });
    } catch (err: any) {
      set({ feedbackMessage: `Launch failed: ${err.message || err}` });
    }
    setTimeout(() => set({ feedbackMessage: null }), 3000);
  },

  toggleTool: async (id: string, enabled: boolean) => {
    const next = get().tools.map(t => (t.id === id ? { ...t, enabled } : t));
    set({ tools: next });
    await persistTools(next);
  },

  setShortcutsVisible: async (visible: boolean) => {
    set({ shortcutsVisible: visible });
    try {
      await webviewBridge.invoke('save_ai_config', { config: { shortcutsVisible: visible } });
    } catch {
      // Reverting would hide the toggle's own state; the next load reports the truth.
    }
  },

  updateTool: async (id, patch) => {
    const next = get().tools.map(t => (t.id === id ? { ...t, ...patch } : t));
    set({ tools: next });
    await persistTools(next);
  },

  browseToolTarget: async (id: string, name: string) => {
    try {
      const res: any = await webviewBridge.invoke('browse_tool_target', {
        toolId: id,
        toolName: name,
      });
      if (res && typeof res.path === 'string' && res.path.length > 0) {
        return res.path as string;
      }
    } catch {}
    return null;
  },

  updateToolTarget: async (id: string, target: string) => {
    const next = get().tools.map(t =>
      t.id === id ? { ...t, target, subtitle: target, state: 'ready' as const } : t
    );
    set({ tools: next, feedbackMessage: 'Target updated.' });
    await persistTools(next);
    setTimeout(() => set({ feedbackMessage: null }), 3000);
  },

  addTool: async ({ name, target, isIde }) => {
    const newTool: DevToolItem = {
      id: `devtool_${Date.now()}`,
      name,
      target,
      subtitle: target,
      state: 'ready',
      isIde: Boolean(isIde),
      enabled: true,
      showOnDashboard: false,
      iconDataUrl: '',
    };
    const next = [...get().tools, newTool];
    set({ tools: next, feedbackMessage: `${name} added.` });
    await persistTools(next);
    setTimeout(() => set({ feedbackMessage: null }), 3000);
  },

  removeTool: async (id: string) => {
    const next = get().tools.filter(t => t.id !== id);
    set({ tools: next, feedbackMessage: 'Tool removed.' });
    await persistTools(next);
    setTimeout(() => set({ feedbackMessage: null }), 3000);
  },
}));
