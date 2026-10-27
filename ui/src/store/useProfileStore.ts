/**
 * Runner Profile and Multi-Account Store
 * Manages Start/Stop execution per profile, account switching, and runner states.
 */

import { create } from 'zustand';
import { RunnerProfile } from '../types/agent';
import { webviewBridge } from '../services/webviewBridge';

interface ProfileStoreState {
  profiles: RunnerProfile[];
  activeProfileName: string;
  defaultModel: string;
  isLoading: boolean;

  // Actions
  loadProfiles: () => Promise<void>;
  setProfilesData: (profiles: RunnerProfile[], activeProfile?: string, model?: string) => void;
  switchActiveProfile: (name: string) => Promise<void>;
  startProfile: (name: string) => Promise<void>;
  stopProfile: (name: string) => Promise<void>;
  setProfileRunning: (name: string, isRunning: boolean) => void;
  updateProfileQuota: (name: string, quota: number) => void;
  getActiveProfile: () => RunnerProfile | undefined;
}

const defaultProfiles: RunnerProfile[] = [
  { name: 'ZXR@1', active: true, isRunning: false, quotaPercent: 100, provider: 'Google Antigravity' },
];

export const useProfileStore = create<ProfileStoreState>((set, get) => ({
  profiles: defaultProfiles,
  activeProfileName: 'ZXR@1',
  defaultModel: 'Gemini 3.8 Flash (High)',
  isLoading: false,

  getActiveProfile: () => {
    const { profiles, activeProfileName } = get();
    return profiles.find(p => p.name.toLowerCase() === activeProfileName.toLowerCase()) || profiles[0];
  },

  setProfilesData: (profiles: RunnerProfile[], activeProfile?: string, model?: string) => {
    const sanitized = profiles.filter(p => p.name.toLowerCase() !== 'default');
    const fallbackName = sanitized[0]?.name || 'ZXR@1';
    const active = (activeProfile && activeProfile.toLowerCase() !== 'default')
      ? activeProfile
      : (get().activeProfileName.toLowerCase() !== 'default' ? get().activeProfileName : fallbackName);

    set(state => ({
      profiles: sanitized.length > 0 ? sanitized : state.profiles,
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
                return n.toLowerCase() !== 'default';
              })
              .map((p: any) => ({
                name: typeof p === 'string' ? p : p.name || 'Profile',
                email: typeof p === 'object' ? p.email : undefined,
                active: typeof p === 'object' ? !!p.active : p === ag.activeProfile,
                isRunning: typeof p === 'object' ? !!p.isRunning : false,
                quotaPercent: typeof p === 'object' && p.quotaPercent !== undefined ? p.quotaPercent : 96,
                provider: (typeof p === 'object' && p.provider) ? p.provider : 'Google Antigravity',
              }))
          : [];

        if (mapped.length > 0) {
          get().setProfilesData(mapped, ag.activeProfile, ag.model || 'Gemini 3.8 Flash (High)');
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
        p.name.toLowerCase() === name.toLowerCase() ? { ...p, quotaPercent: quota } : p
      ),
    }));
  },
}));
