/**
 * Tailwind theme extension.
 *
 * Colours are intentionally routed through CSS custom properties (`src/index.css`) instead of raw
 * palette values. One class (`text-content-main`, `bg-surface-card`) resolves to the right value in
 * dark and light mode, so components never need `dark:` duplicates and cannot drift out of sync with
 * the theme store. Adding a literal `text-slate-100` anywhere breaks light mode by definition.
 *
 * @type {import('tailwindcss').Config}
 */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        brand: {
          50: '#F0F9FF',
          100: '#E0F2FE',
          200: '#BAE6FD',
          300: '#7DD3FC',
          400: '#38BDF8',
          500: '#0EA5E9',
          600: '#0284C7',
          700: '#0369A1',
          DEFAULT: '#38BDF8',
        },
        surface: {
          deep: 'var(--bg-app)',
          sidebar: 'var(--bg-sidebar)',
          header: 'var(--bg-header)',
          card: 'var(--bg-card)',
          cardHover: 'var(--bg-card-hover)',
          border: 'var(--border-subtle)',
          borderLight: 'var(--border-light)',
          input: 'var(--bg-input)',
          terminal: 'var(--bg-terminal)',
          /** Fenced code blocks and tool drawers. */
          code: 'var(--bg-code)',
          codeHeader: 'var(--bg-code-header)',
          /** Translucent interaction layers: hover rows, pressed states, sunken panels. */
          hover: 'var(--overlay-hover)',
          strong: 'var(--overlay-strong)',
          sunken: 'var(--overlay-sunken)',
        },
        content: {
          main: 'var(--text-main)',
          muted: 'var(--text-muted)',
          dim: 'var(--text-dim)',
          /** Text drawn on top of an accent fill. */
          inverse: 'var(--text-inverse)',
        },
        /** Accent text/borders that stay legible on both surfaces. */
        accent: {
          DEFAULT: 'var(--accent-ink)',
          soft: 'var(--accent-sky-bg)',
        },
        /** Semantic status text, darkened in light mode to hold WCAG AA. */
        state: {
          success: 'var(--state-success)',
          danger: 'var(--state-danger)',
          warning: 'var(--state-warning)',
        },
        edge: {
          code: 'var(--border-code)',
        },
      },
      fontFamily: {
        sans: ['Inter', 'system-ui', '-apple-system', 'Segoe UI', 'Roboto', 'sans-serif'],
        mono: ['"JetBrains Mono"', '"Fira Code"', 'Consolas', 'monospace'],
      },
      keyframes: {
        actionRing: {
          to: { transform: 'rotate(360deg)' },
        },
      },
      animation: {
        'action-ring': 'actionRing 900ms linear infinite',
      },
    },
  },
  plugins: [],
};
