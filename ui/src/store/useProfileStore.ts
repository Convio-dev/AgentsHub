/**
 * Runner Profile and Multi-Account Store
 * Manages Start/Stop execution per profile, account switching, and runner states.
 */

import { create } from 'zustand';
import { RunnerProfile } from '../types/agent';
import { webviewBridge } from '../services/webviewBridge';
import { isUnsetProfile } from '../lib/profileName';

interface ProfileStoreState {
  profiles: RunnerProfile[];
  activeProfileName: string;
  defaultModel: string;
  isLoading: boolean;
  /** Profiles with an in-flight quota read, keyed by lower-cased name. */
  quotaPending: Record<string, boolean>;

  // Actions
  loadProfiles: () => Promise<void>;
  setProfilesData: (profiles: RunnerProfile[], activeProfile?: string, model?: string) => void;
  switchActiveProfile: (name: string) => Promise<void>;
  startProfile: (name: string) => Promise<void>;
  stopProfile: (name: string) => Promise<void>;
  setProfileRunning: (name: string, isRunning: boolean) => void;
  updateProfileQuota: (name: string, quota: number) => void;
  /** Asks the host to read one profile's quota. Resolves when the request is dispatched. */
  checkQuota: (name: string) => Promise<void>;
  /** Clears the pending flag, called when the check reports back (success or failure). */
  settleQuotaCheck: (name?: string | null) => void;
  getActiveProfile: () => RunnerProfile | undefined;
}

/**
 * No seeded profile on purpose. The runner list is whatever the host reports; before that arrives
 * the UI shows "Default Profile" rather than a placeholder account that looks real.
 */
const defaultProfiles: RunnerProfile[] = [];

export const useProfileStore = create<ProfileStoreState>((set, get) => ({
  profiles: defaultProfiles,
  activeProfileName: '',
  defaultModel: '',
  isLoading: false,
  quotaPending: {},

  getActiveProfile: () => {
    const { profiles, activeProfileName } = get();
    return profiles.find(p => p.name.toLowerCase() === activeProfileName.toLowerCase()) || profiles[0];
  },

  setProfilesData: (profiles: RunnerProfile[], activeProfile?: string, model?: string) => {
    const sanitized = profiles.filter(p => !isUnsetProfile(p.name));
    const fallbackName = sanitized[0]?.name || '';
    const active = !isUnsetProfile(activeProfile)
      ? (activeProfile as string)
      : (!isUnsetProfile(get().activeProfileName) ? get().activeProfileName : fallbackName);

    set(state => ({
      profiles: sanitized,
      activeProfileName: active,
      defaultModel: model || state.defaultModel,
    }));
  },

  loadProfiles: async () => {
    try {
      set({ isLoading: true });
      const res = await webviewBridge.invoke('get_initial_state');
      if (res && res.antigravity) {
        const ag = res.antigravity;
        const mapped: RunnerProfile[] = Array.isArray(ag.profiles)
          ? ag.profiles
              .filter((p: any) => {
                const n = typeof p === 'string' ? p : p.name || '';
                return !isUnsetProfile(n);
              })
              .map((p: any) => ({
                name: typeof p === 'string' ? p : p.name || 'Profile',
                email: typeof p === 'object' ? p.email : undefined,
                active: typeof p === 'object' ? !!p.active : p === ag.activeProfile,
                isRunning: typeof p === 'object' ? !!p.isRunning : false,
                quotaPercent: typeof p === 'object' ? p.quotaPercent : undefined,
                provider: (typeof p === 'object' && p.provider) ? p.provider : 'Google Antigravity',
              }))
          : [];

        if (mapped.length > 0) {
          get().setProfilesData(mapped, ag.activeProfile, ag.model);
          set({ isLoading: false });
          return;
        }
      }
    } catch (err) {
      console.warn('[ProfileStore] Could not load profiles via get_initial_state:', err);
    }
    set({ isLoading: false });
  },

  switchActiveProfile: async (name: string) => {
    set(state => ({
      activeProfileName: name,
      profiles: state.profiles.map(p => ({
        ...p,
        active: p.name.toLowerCase() === name.toLowerCase(),
      })),
    }));

    try {
      await webviewBridge.invoke('switch_account', { profile: name });
    } catch (err) {
      console.warn(`[ProfileStore] Switch account to ${name} failed:`, err);
    }
  },

  startProfile: async (name: string) => {
    get().setProfileRunning(name, true);
    try {
      await webviewBridge.invoke('start_antigravity_instance', { profile: name });
    } catch (err) {
      console.error(`[ProfileStore] Start profile ${name} failed:`, err);
      get().setProfileRunning(name, false);
    }
  },

  stopProfile: async (name: string) => {
    get().setProfileRunning(name, false);
    try {
      await webviewBridge.invoke('stop_antigravity_instance', { profile: name });
    } catch (err) {
      console.error(`[ProfileStore] Stop profile ${name} failed:`, err);
    }
  },

  setProfileRunning: (name: string, isRunning: boolean) => {
    set(state => ({
      profiles: state.profiles.map(p =>
        p.name.toLowerCase() === name.toLowerCase() ? { ...p, isRunning } : p
      ),
    }));
  },

  updateProfileQuota: (name: string, quota: number) => {
    set(state => ({
      profiles: state.profiles.map(p =>
        p.name.toLowerCase() === name.toLowerCase()
          ? { ...p, quotaPercent: quota, lastQuotaChecked: Date.now() }
          : p
      ),
      quotaPending: { ...state.quotaPending, [name.toLowerCase()]: false },
    }));
  },

  checkQuota: async (name: string) => {
    const key = name.toLowerCase();
    if (!name || get().quotaPending[key]) return;

    set(state => ({ quotaPending: { ...state.quotaPending, [key]: true } }));

    try {
      await webviewBridge.invoke('check_profile_quota', { profile: name });
    } catch (err) {
      console.warn(`[ProfileStore] Quota check for ${name} failed:`, err);
      set(state => ({ quotaPending: { ...state.quotaPending, [key]: false } }));
      return;
    }

    // The result arrives as an event. If the sidecar never answers, drop the spinner rather than
    // leaving it spinning forever.
    window.setTimeout(() => get().settleQuotaCheck(name), 30_000);
  },

  settleQuotaCheck: (name?: string | null) => {
    set(state => {
      if (name) {
        return { quotaPending: { ...state.quotaPending, [name.toLowerCase()]: false } };
      }

      const cleared: Record<string, boolean> = {};
      for (const key of Object.keys(state.quotaPending)) cleared[key] = false;
      return { quotaPending: cleared };
    });
  },
}));
