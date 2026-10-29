import React, { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { ChevronUp, Cpu, Gauge } from 'lucide-react';
import { webviewBridge } from '../../services/webviewBridge';

/**
 * The two chips the Qwen composer needs, shaped like the app's model selector: a pill with an
 * icon, a label and a chevron.
 *
 * The menu is portalled and measured from the trigger because the composer clips its own children.
 * An absolutely positioned panel inside it gets cut off, which is the same trap the image lightbox
 * fell into.
 */
interface MenuPosition {
  top: number;
  left: number;
}

function useAnchoredMenu() {
  const triggerRef = useRef<HTMLButtonElement | null>(null);
  const [position, setPosition] = useState<MenuPosition | null>(null);

  const open = () => {
    const rect = triggerRef.current?.getBoundingClientRect();
    if (!rect) return;

    setPosition({
      top: Math.max(8, rect.top - 8),
      left: Math.max(8, Math.min(rect.left, window.innerWidth - 180)),
    });
  };

  return { triggerRef, position, open, close: () => setPosition(null) };
}

const menuClass =
  'fixed z-[121] w-56 -translate-y-full overflow-hidden rounded-lg border border-surface-border bg-surface-card py-1 shadow-xl';

const chipClass =
  'flex items-center gap-1.5 rounded-lg border border-surface-border bg-surface-card px-2 py-1 font-mono text-[11px] text-content-muted transition-colors hover:border-violet-500/50 hover:text-content-main';

/**
 * The model the page is using, with the list it actually serves.
 *
 * The names are read from Qwen's own menu rather than hardcoded: a copy here would be wrong the day
 * they add a model, and a wrong name in a picker is worse than no picker.
 */
export const QwenModelChip: React.FC<{ model?: string }> = ({ model = 'Qwen3.7-Plus' }) => {
  const [current, setCurrent] = useState(model);
  const [models, setModels] = useState<string[] | null>(null);
  const { triggerRef, position, open, close } = useAnchoredMenu();

  useEffect(() => {
    if (!position) return;

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') close();
    };

    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [position]);

  const load = async () => {
    open();
    setModels(null);

    try {
      const res: any = await webviewBridge.invoke('qwen_models');
      setModels(Array.isArray(res?.models) ? res.models : []);
    } catch {
      setModels([]);
    }
  };

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        title="Change the model Qwen answers with"
        onClick={() => (position ? close() : void load())}
        className={chipClass}
      >
        <Cpu className="h-3 w-3 flex-shrink-0 text-violet-400" />
        <span className="max-w-[140px] truncate">{current}</span>
        <ChevronUp className={`h-3 w-3 opacity-60 transition-transform ${position ? 'rotate-180' : ''}`} />
      </button>

      {position
        ? createPortal(
            <>
              <div className="fixed inset-0 z-[120]" onClick={close} />
              <div style={{ top: position.top, left: position.left }} className={menuClass} role="menu">
                {models === null ? (
                  <p className="px-3 py-2 font-mono text-[10.5px] text-content-dim">Reading the model list...</p>
                ) : models.length === 0 ? (
                  <button
                    type="button"
                    onClick={() => {
                      close();
                      void webviewBridge.invoke('qwen_open', {}).catch(() => {});
                    }}
                    className="w-full px-3 py-2 text-left font-mono text-[10.5px] text-content-muted transition-colors hover:bg-surface-hover hover:text-content-main"
                  >
                    No list available. Open the Qwen window
                  </button>
                ) : (
                  models.map(name => (
                    <button
                      key={name}
                      type="button"
                      role="menuitem"
                      onClick={() => {
                        setCurrent(name);
                        close();
                        void webviewBridge.invoke('qwen_model', { model: name }).catch(() => {});
                      }}
                      className={`flex w-full items-center justify-between px-3 py-1.5 text-left font-mono text-[11px] transition-colors hover:bg-surface-hover ${
                        name === current ? 'text-violet-300' : 'text-content-muted'
                      }`}
                    >
                      <span className="truncate">{name}</span>
                      {name === current ? <span className="text-[9px]">current</span> : null}
                    </button>
                  ))
                )}
              </div>
            </>,
            document.body
          )
        : null}
    </>
  );
};

const MODES = ['auto', 'thinking', 'fast'] as const;
type QwenMode = (typeof MODES)[number];

const MODE_LABEL: Record<QwenMode, string> = {
  auto: 'Auto',
  thinking: 'Thinking',
  fast: 'Fast',
};

/** Auto / Thinking / Fast, the same three the page offers. */
export const QwenModeChip: React.FC = () => {
  const [mode, setMode] = useState<QwenMode>('auto');
  const { triggerRef, position, open, close } = useAnchoredMenu();

  useEffect(() => {
    if (!position) return;

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') close();
    };

    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [position]);

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        title="Answer mode on the Qwen page"
        onClick={() => (position ? close() : open())}
        className={chipClass}
      >
        <Gauge className="h-3 w-3 flex-shrink-0 text-violet-400" />
        {MODE_LABEL[mode]}
        <ChevronUp className={`h-3 w-3 opacity-60 transition-transform ${position ? 'rotate-180' : ''}`} />
      </button>

      {position
        ? createPortal(
            <>
              <div className="fixed inset-0 z-[120]" onClick={close} />
              <div
                style={{ top: position.top, left: position.left }}
                className="fixed z-[121] w-40 -translate-y-full overflow-hidden rounded-lg border border-surface-border bg-surface-card py-1 shadow-xl"
                role="menu"
              >
                {MODES.map(option => (
                  <button
                    key={option}
                    type="button"
                    role="menuitem"
                    onClick={() => {
                      setMode(option);
                      close();
                      void webviewBridge.invoke('qwen_mode', { mode: option }).catch(() => {});
                    }}
                    className={`flex w-full items-center justify-between px-3 py-1.5 text-left font-mono text-[11px] transition-colors hover:bg-surface-hover ${
                      option === mode ? 'text-violet-300' : 'text-content-muted'
                    }`}
                  >
                    {MODE_LABEL[option]}
                    {option === mode ? <span className="text-[9px]">current</span> : null}
                  </button>
                ))}
              </div>
            </>,
            document.body
          )
        : null}
    </>
  );
};
