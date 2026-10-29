import React, { useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { Check, Copy, MessageSquarePlus } from 'lucide-react';
import { copyToClipboard } from '../../lib/clipboard';

/**
 * Copy / quote toolbar that follows a text selection in the transcript.
 *
 * The chat shell sets `select-none` so the surrounding chrome does not drag-select, which means the
 * prose has to opt back in (`select-text`) and this toolbar only appears for selections that start
 * inside such a block.
 */
export const SelectionToolbar: React.FC = () => {
  const [selection, setSelection] = useState<{ text: string; top: number; left: number } | null>(null);
  const [copied, setCopied] = useState(false);

  const capture = useCallback(() => {
    const sel = window.getSelection();
    const text = sel?.toString().trim() ?? '';
    if (!sel || !text || sel.rangeCount === 0) {
      setSelection(null);
      return;
    }

    const node = sel.anchorNode;
    const element = node instanceof Element ? node : node?.parentElement;
    if (!element?.closest('.select-text')) {
      setSelection(null);
      return;
    }

    const rect = sel.getRangeAt(0).getBoundingClientRect();
    setSelection({
      text,
      top: Math.max(8, rect.top - 44),
      left: Math.min(window.innerWidth - 220, Math.max(8, rect.left + rect.width / 2 - 110)),
    });
    setCopied(false);
  }, []);

  useEffect(() => {
    const onUp = () => window.setTimeout(capture, 0);
    document.addEventListener('mouseup', onUp);
    document.addEventListener('keyup', onUp);
    window.addEventListener('scroll', () => setSelection(null), true);

    return () => {
      document.removeEventListener('mouseup', onUp);
      document.removeEventListener('keyup', onUp);
    };
  }, [capture]);

  if (!selection) return null;

  const copy = async () => {
    await copyToClipboard(selection.text);
    setCopied(true);
    window.setTimeout(() => setSelection(null), 600);
  };

  const quote = () => {
    const quoted = selection.text
      .split('\n')
      .map(line => `> ${line}`)
      .join('\n');
    window.dispatchEvent(new CustomEvent('agentshub:quote', { detail: quoted }));
    setSelection(null);
    window.getSelection()?.removeAllRanges();
  };

  return createPortal(
    <div
      style={{ top: selection.top, left: selection.left }}
      className="fixed z-[130] flex items-center gap-0.5 rounded-lg border border-surface-border bg-surface-card px-1 py-1 shadow-xl"
      onMouseDown={event => event.preventDefault()}
    >
      <button
        type="button"
        onClick={() => void copy()}
        className="flex items-center gap-1.5 rounded px-2 py-1 text-[11px] text-content-main transition-colors hover:bg-surface-hover"
      >
        {copied ? <Check className="h-3.5 w-3.5 text-state-success" /> : <Copy className="h-3.5 w-3.5" />}
        Copy
      </button>
      <button
        type="button"
        onClick={quote}
        className="flex items-center gap-1.5 rounded px-2 py-1 text-[11px] text-content-main transition-colors hover:bg-surface-hover"
      >
        <MessageSquarePlus className="h-3.5 w-3.5" />
        Add to conversation
      </button>
    </div>,
    document.body
  );
};
