import { create } from 'zustand';
import { webviewBridge } from '../services/webviewBridge';

export interface LanForwarder {
  port: number;
  label: string;
  address?: string;
  bound?: boolean;
  detail?: string;
}

interface LanState {
  /** True once the host actually bound the gateway socket, not just when the switch is on. */
  running: boolean;
  lanIp: string;
  gatewayPort: number;
  bindError: string;
  /** Sockets that came up, as reported by the host. */
  forwarders: LanForwarder[];
  /** Ports that can be exposed, whether or not they are selected. */
  available: LanForwarder[];
  selected: number[];
  hasLoaded: boolean;
  isBusy: boolean;
  error: string | null;

  load: () => Promise<void>;
  setEnabled: (enabled: boolean) => Promise<void>;
  setPorts: (ports: number[]) => Promise<void>;
  attachListeners: () => () => void;
}

function normaliseForwarders(raw: unknown): LanForwarder[] {
  if (!Array.isArray(raw)) return [];

  return raw
    .filter((entry): entry is Record<string, unknown> => Boolean(entry) && typeof entry === 'object')
    .map(entry => ({
      port: typeof entry.port === 'number' ? entry.port : Number(entry.port) || 0,
      label: String(entry.label ?? ''),
      address: typeof entry.address === 'string' ? entry.address : undefined,
      bound: entry.bound === true,
      detail: typeof entry.detail === 'string' ? entry.detail : undefined,
    }))
    .filter(entry => entry.port > 0);
}

/**
 * LAN exposure state. Off by default: the host binds nothing until the user turns it on, so this
 * store reports what is actually listening rather than what was asked for.
 */
export const useLanStore = create<LanState>((set, get) => ({
  running: false,
  lanIp: '',
  gatewayPort: 9191,
  bindError: '',
  forwarders: [],
  available: [{ port: 9119, label: 'Hub' }, { port: 9229, label: 'Claude Code' }, { port: 9339, label: 'Antigravity' }],
  selected: [9119, 9339],
  hasLoaded: false,
  isBusy: false,
  error: null,

  load: async () => {
    try {
      const payload: any = await webviewBridge.invoke('get_lan_state');
      const lan = payload?.lan ?? {};

      set({
        running: lan.running === true,
        lanIp: String(lan.lanIp ?? ''),
        gatewayPort: typeof lan.gatewayPort === 'number' ? lan.gatewayPort : 9191,
        bindError: String(lan.bindError ?? ''),
        forwarders: normaliseForwarders(lan.forwarders),
        available: normaliseForwarders(lan.availableForwarders),
        selected: Array.isArray(lan.selectedPorts) ? lan.selectedPorts.filter((p: unknown) => typeof p === 'number') : [],
        hasLoaded: true,
        error: null,
      });
    } catch (err) {
      set({ hasLoaded: true, error: err instanceof Error ? err.message : 'Could not read LAN state.' });
    }
  },

  setEnabled: async enabled => {
    if (get().isBusy) return;
    set({ isBusy: true, error: null });

    try {
      await webviewBridge.invoke('toggle_lan_proxy', { enabled });
      // A bind can fail, so the truth comes back from the host rather than the switch.
      await get().load();
    } catch (err) {
      set({ error: err instanceof Error ? err.message : 'Could not change LAN access.' });
    } finally {
      set({ isBusy: false });
    }
  },

  setPorts: async ports => {
    if (get().isBusy) return;
    set({ isBusy: true, error: null, selected: ports });

    try {
      const payload: any = await webviewBridge.invoke('set_lan_forwarders', { ports });
      if (payload?.lan) {
        const lan = payload.lan;
        set({
          running: lan.running === true,
          bindError: String(lan.bindError ?? ''),
          forwarders: normaliseForwarders(lan.forwarders),
          available: normaliseForwarders(lan.availableForwarders),
          selected: Array.isArray(lan.selectedPorts) ? lan.selectedPorts : ports,
        });
      } else {
        await get().load();
      }
    } catch (err) {
      set({ error: err instanceof Error ? err.message : 'Could not change the exposed ports.' });
      await get().load();
    } finally {
      set({ isBusy: false });
    }
  },

  attachListeners: () => {
    const refresh = () => void get().load();
    const unsubs = [
      webviewBridge.on('lan_state_changed', refresh),
      webviewBridge.on('lan_proxy_state_changed', refresh),
    ];
    return () => unsubs.forEach(unsub => unsub());
  },
}));
