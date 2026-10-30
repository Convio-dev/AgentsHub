import { create } from 'zustand';

/**
 * The user guide is a modal owned by the app, not by Settings: F1 has to reach it from any view,
 * and it opens by itself the first time the program starts.
 */
const SEEN_KEY = 'agents_hub_guide_seen';

interface GuideState {
  isOpen: boolean;
  step: number;
  open: () => void;
  close: () => void;
  setStep: (step: number) => void;
  /** Opens once on a fresh install, then stays out of the way. */
  openOnFirstRun: () => void;
}

export const useGuideStore = create<GuideState>((set, get) => ({
  isOpen: false,
  step: 0,

  open: () => set({ isOpen: true, step: 0 }),

  // Closing marks it seen, so a first-run guide is never a modal that greets you every launch.
  close: () => {
    try {
      localStorage.setItem(SEEN_KEY, '1');
    } catch {}
    set({ isOpen: false });
  },

  setStep: step => set({ step }),

  openOnFirstRun: () => {
    if (get().isOpen) return;
    try {
      if (localStorage.getItem(SEEN_KEY) === '1') return;
    } catch {}
    set({ isOpen: true, step: 0 });
  },
}));
