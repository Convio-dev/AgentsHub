/**
 * Theme Manager & Syncing Store
 * Manages Dark/Light mode, Discord Adaptive Theme, color swatches, intensity, and host syncing.
 */

import { create } from 'zustand';
import { webviewBridge } from '../services/webviewBridge';

export type ThemeMode = 'dark' | 'light';

export interface ThemeStoreState {
  theme: ThemeMode;
  accentColor: string;
  themeIntensity: number;

  // Actions
  setTheme: (theme: ThemeMode) => void;
  setAccentColor: (hex: string) => void;
  setThemeIntensity: (intensity: number) => void;
  applyTheme: (theme?: ThemeMode, hex?: string, intensity?: number) => void;
  initTheme: () => void;
}

export const PRESET_SWATCHES = [
  { hex: '#4049B9', label: 'Discord Indigo' },
  { hex: '#5865F2', label: 'Discord Blurple' },
  { hex: '#10B981', label: 'Emerald Matrix' },
  { hex: '#06B6D4', label: 'Cyan Tech' },
  { hex: '#38BDF8', label: 'Skyline Blue' },
  { hex: '#F59E0B', label: 'Amber Cyber' },
  { hex: '#EC4899', label: 'Hot Pink' },
  { hex: '#8B5CF6', label: 'Deep Violet' },
];

function hexToHsl(hex: string): [number, number, number] {
  let c = hex.replace('#', '');
  if (c.length === 3) c = c.split('').map(x => x + x).join('');
  const num = parseInt(c, 16);
  const r = (num >> 16) / 255;
  const g = ((num >> 8) & 255) / 255;
  const b = (num & 255) / 255;

  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  let h = 0;
  let s = 0;
  const l = (max + min) / 2;

  if (max !== min) {
    const d = max - min;
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    switch (max) {
      case r: h = ((g - b) / d + (g < b ? 6 : 0)) / 6; break;
      case g: h = ((b - r) / d + 2) / 6; break;
      case b: h = ((r - g) / d + 4) / 6; break;
    }
  }

  return [Math.round(h * 360), Math.round(s * 100), Math.round(l * 100)];
}

export const useThemeStore = create<ThemeStoreState>((set, get) => ({
  theme: (function (): ThemeMode {
    try {
      const saved = localStorage.getItem('agents_hub_theme');
      return saved === 'light' ? 'light' : 'dark';
    } catch {
      return 'dark';
    }
  })(),
  accentColor: (function (): string {
    try {
      return localStorage.getItem('agents_hub_accent_color') || '#4049B9';
    } catch {
      return '#4049B9';
    }
  })(),
  themeIntensity: (function (): number {
    try {
      const val = parseInt(localStorage.getItem('agents_hub_theme_intensity') || '50', 10);
      return isNaN(val) ? 50 : val;
    } catch {
      return 50;
    }
  })(),

  applyTheme: (themeOverride, hexOverride, intensityOverride) => {
    const theme = themeOverride || get().theme;
    const hex = (hexOverride || get().accentColor).toUpperCase();
    const intensity = intensityOverride !== undefined ? intensityOverride : get().themeIntensity;

    const [h, s, l] = hexToHsl(hex);
    const root = document.documentElement;

    root.dataset.theme = theme;
    document.body.dataset.theme = theme;

    if (theme === 'dark') {
      root.classList.add('dark');
      root.classList.remove('light');

      const sTint = Math.round((s * (intensity / 100)) * 0.45);
      root.style.setProperty('--bg-app', `hsl(${h}, ${Math.max(sTint, 8)}%, 6%)`);
      root.style.setProperty('--bg-sidebar', `hsl(${h}, ${Math.max(Math.round(sTint * 1.2), 10)}%, 7.5%)`);
      root.style.setProperty('--bg-header', `hsl(${h}, ${Math.max(sTint, 8)}%, 5.5%)`);
      root.style.setProperty('--bg-card', `hsl(${h}, ${Math.max(Math.round(sTint * 1.15), 10)}%, 9%)`);
      root.style.setProperty('--bg-card-hover', `hsl(${h}, ${Math.max(Math.round(sTint * 1.3), 12)}%, 11.5%)`);
      root.style.setProperty('--bg-input', `hsl(${h}, ${Math.max(sTint, 8)}%, 7%)`);
      root.style.setProperty('--border-subtle', `hsl(${h}, ${Math.max(Math.round(sTint * 1.1), 10)}%, 14%)`);
      root.style.setProperty('--text-main', '#F1F5F9');
      root.style.setProperty('--text-muted', '#94A3B8');
      root.style.setProperty('--text-dim', '#64748B');
    } else {
      root.classList.remove('dark');
      root.classList.add('light');

      const sTintLight = Math.round((s * (intensity / 100)) * 0.35);
      root.style.setProperty('--bg-app', `hsl(${h}, ${sTintLight}%, 96%)`);
      root.style.setProperty('--bg-sidebar', `hsl(${h}, ${Math.round(sTintLight * 1.35)}%, 93%)`);
      root.style.setProperty('--bg-header', `hsl(${h}, ${Math.round(sTintLight * 1.5)}%, 89%)`);
      root.style.setProperty('--bg-card', '#FFFFFF');
      root.style.setProperty('--bg-card-hover', `hsl(${h}, ${sTintLight}%, 97%)`);
      root.style.setProperty('--bg-input', `hsl(${h}, ${sTintLight}%, 97%)`);
      root.style.setProperty('--border-subtle', `hsl(${h}, ${Math.round(sTintLight * 1.25)}%, 84%)`);
      root.style.setProperty('--text-main', '#0F172A');
      root.style.setProperty('--text-muted', '#475569');
      root.style.setProperty('--text-dim', '#64748B');
    }

    root.style.setProperty('--accent-sky', hex);
    root.style.setProperty('--accent-cyan', hex);
    root.style.setProperty('--accent-blue', hex);

    // The accent is also used as text. The same hue that reads well on a dark surface is too light
    // on a light one, so the ink variant is derived per theme instead of being reused verbatim.
    const inkLightness = theme === 'dark' ? 68 : 34;
    const inkSaturation = Math.max(s, theme === 'dark' ? 60 : 55);
    root.style.setProperty('--accent-ink', `hsl(${h}, ${inkSaturation}%, ${inkLightness}%)`);

    try {
      localStorage.setItem('agents_hub_theme', theme);
      localStorage.setItem('agents_hub_accent_color', hex);
      localStorage.setItem('agents_hub_theme_intensity', String(intensity));
    } catch {}

    set({ theme, accentColor: hex, themeIntensity: intensity });
  },

  setTheme: (newTheme: ThemeMode) => {
    get().applyTheme(newTheme, get().accentColor, get().themeIntensity);
    try {
      webviewBridge.invoke('save_settings', {
        settings: { theme: newTheme, accentColor: get().accentColor },
      });
    } catch {}
  },

  setAccentColor: (newHex: string) => {
    get().applyTheme(get().theme, newHex, get().themeIntensity);
    try {
      webviewBridge.invoke('save_settings', {
        settings: { theme: get().theme, accentColor: newHex },
      });
    } catch {}
  },

  setThemeIntensity: (newIntensity: number) => {
    get().applyTheme(get().theme, get().accentColor, newIntensity);
  },

  initTheme: () => {
    get().applyTheme(get().theme, get().accentColor, get().themeIntensity);
  },
}));
