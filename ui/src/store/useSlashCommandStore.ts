/**
 * Slash command cache.
 *
 * Discovery is a filesystem scan plus an engine probe on the host, so it is fetched once per engine
 * and workspace, then reused. `force` exists for the manual refresh in the palette footer.
 */

import { create } from 'zustand';
import { SlashCommand } from '../types/agent';
import { fetchSlashCommands, SlashCommandQuery } from '../services/slashCommandService';

const CACHE_TTL_MS = 60_000;

interface SlashCommandStoreState {
  commands: SlashCommand[];
  isLoading: boolean;
  hasLoaded: boolean;
  error?: string;
  /** Key of the last successful query, so switching engine or workspace refetches. */
  loadedKey?: string;
  lastLoadedAt: number;

  load: (query: SlashCommandQuery, options?: { force?: boolean }) => Promise<void>;
}

function keyFor(query: SlashCommandQuery): string {
  return `${query.cli || ''}::${query.cwd || ''}`;
}

export const useSlashCommandStore = create<SlashCommandStoreState>((set, get) => ({
  commands: [],
  isLoading: false,
  hasLoaded: false,
  lastLoadedAt: 0,

  load: async (query, options = {}) => {
    const state = get();
    const key = keyFor(query);

    if (state.isLoading) return;

    const isFresh =
      state.hasLoaded &&
      state.loadedKey === key &&
      Date.now() - state.lastLoadedAt < CACHE_TTL_MS;

    if (isFresh && !options.force) return;

    set({ isLoading: true, error: undefined });

    try {
      const commands = await fetchSlashCommands(query);
      set({
        commands,
        isLoading: false,
        hasLoaded: true,
        loadedKey: key,
        lastLoadedAt: Date.now(),
      });
    } catch (err) {
      set({
        isLoading: false,
        hasLoaded: true,
        error: err instanceof Error ? err.message : 'Command discovery failed',
      });
    }
  },
}));
