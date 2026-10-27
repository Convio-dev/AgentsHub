import React, { useEffect, useRef, useState } from 'react';
import { Loader2, Square, Terminal as TerminalIcon } from 'lucide-react';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import '@xterm/xterm/css/xterm.css';
import { webviewBridge } from '../../services/webviewBridge';

interface NpxInstallPanelProps {
  command: string;
  title: string;
  /** `global` runs the installer against the user profile, `workspace` against the active project. */
  scope?: 'global' | 'workspace';
  onClose: () => void;
}

/**
 * Interactive install console backed by a real terminal emulator.
 *
 * A plain `<pre>` cannot do this job: installers draw checkboxes with cursor addressing, colour
 * their output, and read arrow keys. xterm handles all of it — `onData` emits the exact sequences a
 * terminal would send, and `write` interprets the escapes instead of printing them.
 */
export const NpxInstallPanel: React.FC<NpxInstallPanelProps> = ({
  command,
  title,
  scope = 'workspace',
  onClose,
}) => {
  const hostRef = useRef<HTMLDivElement>(null);
  const startedRef = useRef(false);

  const [isRunning, setIsRunning] = useState(true);
  const [exitCode, setExitCode] = useState<number | null>(null);

  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;

    const term = new Terminal({
      convertEol: true,
      cursorBlink: true,
      fontSize: 12,
      fontFamily: 'Cascadia Mono, Consolas, ui-monospace, monospace',
      scrollback: 4000,
      theme: {
        background: '#0b1220',
        foreground: '#e2e8f0',
        cursor: '#7dd3fc',
        selectionBackground: '#1e3a5f',
        black: '#0b1220',
        red: '#f87171',
        green: '#4ade80',
        yellow: '#fbbf24',
        blue: '#60a5fa',
        magenta: '#c084fc',
        cyan: '#22d3ee',
        white: '#e2e8f0',
        brightBlack: '#64748b',
      },
    });

    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(host);

    const refit = () => {
      try {
        fit.fit();
      } catch {
        // The panel can be measured before layout settles; a later resize fixes it.
      }
    };

    requestAnimationFrame(refit);

    // xterm emits the correct bytes for every key, including arrows and modifiers.
    const dataSub = term.onData(data => {
      void webviewBridge.invoke('send_npx_input', { input: data }).catch(() => {});
    });

    const offData = webviewBridge.on('npx_install_data', (payload: any) => {
      const text = typeof payload?.text === 'string' ? payload.text : '';
      if (text) term.write(text);
    });

    const offExit = webviewBridge.on('npx_install_exited', (payload: any) => {
      setIsRunning(false);
      setExitCode(typeof payload?.exitCode === 'number' ? payload.exitCode : 0);
    });

    if (!startedRef.current) {
      startedRef.current = true;
      void webviewBridge
        .invoke('run_npx_command', { command, scope })
        .catch((err: Error) => term.write(`\r\n\x1b[31m[host] ${err.message}\x1b[0m\r\n`));
    }

    const onResize = () => refit();
    window.addEventListener('resize', onResize);

    return () => {
      window.removeEventListener('resize', onResize);
      dataSub.dispose();
      offData();
      offExit();
      term.dispose();
    };
  }, [command, scope]);

  const stop = () => {
    void webviewBridge.invoke('stop_npx_command').catch(() => {});
    setIsRunning(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-surface-sunken/80 p-4 backdrop-blur-sm">
      <div className="flex h-[72vh] w-full max-w-4xl flex-col overflow-hidden rounded-2xl border border-surface-border bg-surface-card shadow-2xl">
        <div className="flex flex-shrink-0 items-center justify-between border-b border-surface-border px-4 py-2.5">
          <div className="flex min-w-0 items-center gap-2">
            <TerminalIcon className="h-4 w-4 flex-shrink-0 text-accent" />
            <div className="min-w-0">
              <div className="truncate text-xs font-bold text-content-main">{title}</div>
              <div className="truncate font-mono text-[10px] text-content-dim">
                {command} · {scope}
              </div>
            </div>
          </div>

          <div className="flex flex-shrink-0 items-center gap-2">
            {isRunning ? (
              <span className="flex items-center gap-1 font-mono text-[10px] text-content-dim">
                <Loader2 className="h-3 w-3 animate-spin" />
                running
              </span>
            ) : (
              <span
                className={`font-mono text-[10px] ${exitCode === 0 ? 'text-state-success' : 'text-state-danger'}`}
              >
                exit {exitCode}
              </span>
            )}

            {isRunning ? (
              <button
                type="button"
                onClick={stop}
                className="flex items-center gap-1 rounded-md border border-rose-500/30 bg-rose-500/10 px-2 py-0.5 text-[10px] text-state-danger hover:bg-rose-500/20"
              >
                <Square className="h-3 w-3 fill-current" />
                Stop
              </button>
            ) : (
              <button
                type="button"
                onClick={onClose}
                className="rounded-md border border-surface-border bg-surface-deep px-2 py-0.5 text-[10px] text-content-main hover:border-sky-500"
              >
                Close
              </button>
            )}
          </div>
        </div>

        <div className="min-h-0 flex-1 bg-[#0b1220] p-2">
          <div ref={hostRef} className="h-full w-full" />
        </div>

        <div className="flex flex-shrink-0 items-center justify-between border-t border-surface-border px-3 py-1.5 font-mono text-[9.5px] text-content-dim">
          <span>Click the console, then use ↑/↓ to move · space to select · enter to confirm</span>
          <span>{isRunning ? 'interactive' : 'finished'}</span>
        </div>
      </div>
    </div>
  );
};
