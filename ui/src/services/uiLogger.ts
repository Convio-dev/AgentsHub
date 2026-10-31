import { useSettingsStore, type LogEntry } from '../store/useSettingsStore';

type Level = LogEntry['level'];

/** Same `[HH:MM:SS]` shape the host stamps on its own lines, so the console reads uniformly. */
function stamp(): string {
  const now = new Date();
  const pad = (value: number) => String(value).padStart(2, '0');
  return `[${pad(now.getHours())}:${pad(now.getMinutes())}:${pad(now.getSeconds())}]`;
}

function describe(value: unknown): string {
  if (value instanceof Error) return value.stack || `${value.name}: ${value.message}`;
  if (typeof value === 'string') return value;
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

let lastKey = '';
let lastAt = 0;

/** Writes one line into the Live Logs console. */
export function logUi(level: Level, source: string, message: string): void {
  const text = (message ?? '').trim();
  if (!text) return;

  // A component that throws during render repeats hundreds of times a second; one line per second
  // keeps the console readable without hiding the fact that it is looping.
  const key = `${source}|${level}|${text}`;
  const now = Date.now();
  if (key === lastKey && now - lastAt < 1000) return;
  lastKey = key;
  lastAt = now;

  useSettingsStore.getState().addLogEntry({
    timestamp: stamp(),
    level,
    source,
    message: text.length > 600 ? `${text.slice(0, 600)}...` : text,
  });
}

/**
 * Captures what the C# host cannot see: failures that only exist inside the React app. Installed
 * once from App; the returned function restores the console and removes the listeners.
 */
export function installUiLogging(): () => void {
  const originalError = console.error;
  const originalWarn = console.warn;

  let echoing = false;
  const wrap = (level: Level, original: (...args: unknown[]) => void) => (...args: unknown[]) => {
    original(...args);
    if (echoing) return;
    echoing = true;
    try {
      logUi(level, 'UI', args.map(describe).join(' '));
    } finally {
      echoing = false;
    }
  };

  console.error = wrap('error', originalError) as typeof console.error;
  console.warn = wrap('warn', originalWarn) as typeof console.warn;

  const onError = (event: ErrorEvent) => {
    if (event.error) logUi('error', 'UI', describe(event.error));
  };

  const onRejection = (event: PromiseRejectionEvent) => {
    logUi('error', 'UI', `Unhandled rejection: ${describe(event.reason)}`);
  };

  // A missing script, stylesheet or image reports as an error event with a target and no Error.
  const onResourceError = (event: Event) => {
    const target = event.target as HTMLElement | null;
    if (!target || target === (window as unknown as HTMLElement)) return;
    const src =
      (target as HTMLImageElement).src ||
      (target as HTMLScriptElement).src ||
      (target as HTMLLinkElement).href;
    if (src) logUi('warn', 'UI', `Failed to load ${target.tagName.toLowerCase()}: ${src}`);
  };

  window.addEventListener('error', onError, true);
  window.addEventListener('error', onResourceError, true);
  window.addEventListener('unhandledrejection', onRejection);

  return () => {
    console.error = originalError;
    console.warn = originalWarn;
    window.removeEventListener('error', onError, true);
    window.removeEventListener('error', onResourceError, true);
    window.removeEventListener('unhandledrejection', onRejection);
  };
}
