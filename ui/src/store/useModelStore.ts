/**
 * Model catalogue cache.
 *
 * The list comes from the engine (`agy models` through the host bridge), not from a table in the
 * bundle: a model that ships after this build still shows up, and one that is retired disappears.
 * Selection itself lives on the session record, so two tabs can run different models.
 */

import { create } from 'zustand';
import { ModelCatalog, ModelInfo, ReasoningEffort } from '../types/agent';
import { webviewBridge } from '../services/webviewBridge';

const CACHE_TTL_MS = 5 * 60 * 1000;

/** Fallback effort scale, used only until the engine reports its own list. */
export const DEFAULT_EFFORTS: ReasoningEffort[] = ['low', 'medium', 'high', 'xhigh', 'max'];

const EFFORT_LABELS: Record<string, string> = {
  low: 'Low',
  medium: 'Medium',
  high: 'High',
  xhigh: 'Extra high',
  max: 'Max',
};

export function effortLabel(effort?: string | null): string {
  if (!effort) return '';
  return EFFORT_LABELS[effort.toLowerCase()] || effort;
}

interface ModelStoreState {
  models: ModelInfo[];
  efforts: string[];
  source: string;
  error?: string;
  isLoading: boolean;
  hasLoaded: boolean;
  loadedKey?: string;
  lastLoadedAt: number;

  load: (cli?: string, options?: { force?: boolean }) => Promise<void>;
}

function normaliseCatalog(raw: unknown): ModelCatalog | null {
  if (!raw || typeof raw !== 'object') return null;
  const record = raw as Record<string, unknown>;

  const models: ModelInfo[] = Array.isArray(record.models)
    ? record.models
        .map((entry): ModelInfo | null => {
          if (!entry || typeof entry !== 'object') return null;
          const item = entry as Record<string, unknown>;
          const id = typeof item.id === 'string' ? item.id.trim() : '';
          if (!id) return null;
          return {
            id,
            label: typeof item.label === 'string' && item.label.trim() ? item.label.trim() : id,
            provider: typeof item.provider === 'string' ? item.provider : '',
            tier: typeof item.tier === 'string' ? item.tier : null,
          };
        })
        .filter((model): model is ModelInfo => model !== null)
    : [];

  const efforts = Array.isArray(record.efforts)
    ? record.efforts.filter((value): value is string => typeof value === 'string')
    : [];

  return {
    models,
    efforts: efforts.length > 0 ? efforts : DEFAULT_EFFORTS,
    source: typeof record.source === 'string' ? record.source : 'unknown',
    error: typeof record.error === 'string' ? record.error : undefined,
  };
}

export const useModelStore = create<ModelStoreState>((set, get) => ({
  models: [],
  efforts: DEFAULT_EFFORTS,
  source: 'unknown',
  isLoading: false,
  hasLoaded: false,
  lastLoadedAt: 0,

  load: async (cli, options = {}) => {
    const state = get();
    if (state.isLoading) return;

    const key = cli || '';
    const isFresh =
      state.hasLoaded && state.loadedKey === key && Date.now() - state.lastLoadedAt < CACHE_TTL_MS;
    if (isFresh && !options.force) return;

    set({ isLoading: true, error: undefined });

    try {
      const response = await webviewBridge.invoke<unknown>('get_model_catalog', { cli }, 30000);
      const catalog = normaliseCatalog(response);

      if (!catalog) {
        set({ isLoading: false, hasLoaded: true, error: 'The host returned no model catalogue.' });
        return;
      }

      set({
        models: catalog.models,
        efforts: catalog.efforts,
        source: catalog.source,
        error: catalog.error ?? undefined,
        isLoading: false,
        hasLoaded: true,
        loadedKey: key,
        lastLoadedAt: Date.now(),
      });
    } catch (err) {
      set({
        isLoading: false,
        hasLoaded: true,
        error: err instanceof Error ? err.message : 'Model discovery failed',
      });
    }
  },
}));
