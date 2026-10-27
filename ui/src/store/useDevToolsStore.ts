/**
 * AI Dev Tools Store
 * Manages desktop development tools, paths, IDE detection, and executable launcher triggers.
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
  isLoading: boolean;
  expandedToolId: string | null;
  feedbackMessage: string | null;

  // Actions
  loadTools: () => Promise<void>;
  launchTool: (id: string) => Promise<void>;
  toggleTool: (id: string, enabled: boolean) => Promise<void>;
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
    health,
  };
}

export const useDevToolsStore = create<DevToolsStoreState>((set, get) => ({
  tools: [],
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
        set({ tools: res.devTools.map(mapTool), isLoading: false });
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
    try {
      set({ isLoading: true });
      const res = await webviewBridge.invoke('find_installed_dev_tools', {});
      if (res && Array.isArray(res.devTools)) {
        set({ tools: res.devTools.map(mapTool), isLoading: false, feedbackMessage: 'Found installed developer tools.' });
      } else {
        await get().loadTools();
      }
    } catch {
      await get().loadTools();
    }
    setTimeout(() => set({ feedbackMessage: null }), 3500);
  },

  launchTool: async (id: string) => {
    try {
      await webviewBridge.invoke('launch_dev_tool', { id });
      set({ feedbackMessage: 'Tool launch initiated.' });
    } catch (err: any) {
      set({ feedbackMessage: `Launch failed: ${err.message || err}` });
    }
    setTimeout(() => set({ feedbackMessage: null }), 3000);
  },

  toggleTool: async (id: string, enabled: boolean) => {
    const next = get().tools.map(t => (t.id === id ? { ...t, enabled } : t));
    set({ tools: next });
    try {
      await webviewBridge.invoke('save_ai_config', { config: { devTools: next } });
    } catch {}
  },

  browseToolTarget: async (id: string, name: string) => {
    try {
      const res = await webviewBridge.invoke('browse_for_file', {
        title: `Select Executable for ${name}`,
        filter: 'Executables (*.exe)|*.exe|All Files (*.*)|*.*',
      });
      if (res && res.filePath) {
        return res.filePath as string;
      }
    } catch {}
    return null;
  },

  updateToolTarget: async (id: string, target: string) => {
    const next = get().tools.map(t => (t.id === id ? { ...t, target, subtitle: target, state: 'ready' as const } : t));
    set({ tools: next, feedbackMessage: 'Target updated.' });
    try {
      await webviewBridge.invoke('save_ai_config', { config: { devTools: next } });
    } catch {}
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
    };
    const next = [...get().tools, newTool];
    set({ tools: next, feedbackMessage: `${name} added.` });
    try {
      await webviewBridge.invoke('save_ai_config', { config: { devTools: next } });
    } catch {}
    setTimeout(() => set({ feedbackMessage: null }), 3000);
  },

  removeTool: async (id: string) => {
    const next = get().tools.filter(t => t.id !== id);
    set({ tools: next, feedbackMessage: 'Tool removed.' });
    try {
      await webviewBridge.invoke('save_ai_config', { config: { devTools: next } });
    } catch {}
    setTimeout(() => set({ feedbackMessage: null }), 3000);
  },
}));
