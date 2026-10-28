import React, { useEffect, useRef, useState } from 'react';
import { Terminal } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import '@xterm/xterm/css/xterm.css';
import { Terminal as TerminalIcon, X, RotateCw } from 'lucide-react';
import { webviewBridge } from '../../services/webviewBridge';

interface TerminalPanelProps {
  cwd?: string;
  onClose: () => void;
}

/**
 * Embedded shell, the way an IDE docks one.
 *
 * It talks to the same console host the NPX installer uses (`NpxInstallHost`), so there is one place
 * that owns a pseudo-console and one set of events. Only one console can run at a time — starting the
 * shell stops an install and vice versa — which is a deliberate limitation, not an oversight.
 */
export const TerminalPanel: React.FC<TerminalPanelProps> = ({ cwd, onClose }) => {
  const hostRef = useRef<HTMLDivElement>(null);
  const [isRunning, setIsRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const termRef = useRef<Terminal | null>(null);
  const fitRef = useRef<FitAddon | null>(null);

  useEffect(() => {
    const host = hostRef.current;
    if (!host) return;

    const term = new Terminal({
      convertEol: true,
      cursorBlink: true,
      fontSize: 12,
      fontFamily: 'Cascadia Mono, Consolas, ui-monospace, monospace',
      scrollback: 5000,
      theme: {
        background: '#0b1220',
        foreground: '#e2e8f0',
        cursor: '#7dd3fc',
        selectionBackground: '#1e3a5f',
        red: '#f87171',
        green: '#4ade80',
        yellow: '#fbbf24',
        blue: '#60a5fa',
        magenta: '#c084fc',
        cyan: '#22d3ee',
        white: '#e2e8f0',
      },
    });

    const fit = new FitAddon();
    term.loadAddon(fit);
    term.open(host);
    termRef.current = term;
    fitRef.current = fit;

    const refit = () => {
      try {
        fit.fit();
      } catch {
        // Measured before layout settles; the next resize corrects it.
      }
    };
    requestAnimationFrame(refit);

    const dataSub = term.onData(data => {
      void webviewBridge.invoke('send_shell_input', { input: data }).catch(() => {});
    });

    const offData = webviewBridge.on('npx_install_data', (payload: any) => {
      const text = typeof payload?.text === 'string' ? payload.text : '';
      if (text) term.write(text);
    });

    const offExit = webviewBridge.on('npx_install_exited', (payload: any) => {
      setIsRunning(false);
      term.write(`\r\n\x1b[2m[process exited: ${payload?.exitCode ?? '?'}]\x1b[0m\r\n`);
    });

    void webviewBridge
      .invoke('start_shell', { cwd })
      .then(() => setIsRunning(true))
      .catch((err: Error) => {
        setError(err.message);
        term.write(`\r\n\x1b[31m${err.message}\x1b[0m\r\n`);
      });

    const onResize = () => refit();
    window.addEventListener('resize', onResize);

    return () => {
      window.removeEventListener('resize', onResize);
      dataSub.dispose();
      offData();
      offExit();
      // Closing the panel ends the shell; leaving it alive would orphan a process nobody can see.
      void webviewBridge.invoke('stop_shell').catch(() => {});
      term.dispose();
      termRef.current = null;
    };
  }, [cwd]);

  return (
    <div className="flex h-[280px] flex-shrink-0 flex-col border-t border-surface-border bg-surface-deep">
      <div className="flex flex-shrink-0 items-center justify-between border-b border-surface-border px-3 py-1.5">
        <div className="flex min-w-0 items-center gap-2">
          <TerminalIcon className="h-3.5 w-3.5 flex-shrink-0 text-accent" />
          <span className="font-mono text-[10.5px] font-semibold text-content-main">Terminal</span>
          <span className="truncate font-mono text-[9.5px] text-content-dim" title={cwd}>
            {cwd || 'workspace root'}
          </span>
          {error ? <span className="font-mono text-[9.5px] text-state-danger">{error}</span> : null}
        </div>

        <div className="flex flex-shrink-0 items-center gap-1">
          <button
            type="button"
            onClick={() => {
              void webviewBridge.invoke('stop_shell').catch(() => {});
              void webviewBridge.invoke('start_shell', { cwd }).then(() => setIsRunning(true)).catch(() => {});
            }}
            title="Restart the shell"
            className="rounded p-1 text-content-dim transition-colors hover:bg-surface-hover hover:text-content-main"
          >
            <RotateCw className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            onClick={onClose}
            title="Close the terminal"
            className="rounded p-1 text-content-dim transition-colors hover:bg-rose-500/10 hover:text-state-danger"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      <div className="min-h-0 flex-1 bg-[#0b1220] p-1.5">
        <div ref={hostRef} className="h-full w-full" />
      </div>

      <div className="flex flex-shrink-0 items-center justify-between border-t border-surface-border px-3 py-1 font-mono text-[9px] text-content-dim">
        <span>Click the console to type · commands run in the workspace folder</span>
        <span>{isRunning ? 'running' : 'stopped'}</span>
      </div>
    </div>
  );
};
