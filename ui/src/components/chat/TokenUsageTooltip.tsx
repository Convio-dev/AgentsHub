import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { TokenUsage } from '../../types/agent';

interface TokenUsageTooltipProps {
  /** Usage reported by the most recent completed turn. */
  turn?: TokenUsage;
  /** Running total for this session. */
  session?: TokenUsage;
  /** Completed replies in this session, used to explain what the session total sums. */
  turns?: number;
}

const PANEL_WIDTH = 276;
const VIEWPORT_MARGIN = 8;
const OPEN_DELAY_MS = 140;

function format(value: number): string {
  if (value <= 0) return '0';
  if (value < 1000) return String(value);
  if (value < 1_000_000) return `${(value / 1000).toFixed(1)}k`;
  return `${(value / 1_000_000).toFixed(2)}M`;
}

const SEGMENTS = [
  { key: 'inputTokens' as const, label: 'Input', className: 'bg-accent' },
  { key: 'outputTokens' as const, label: 'Output', className: 'bg-state-success' },
  { key: 'thinkingTokens' as const, label: 'Thinking', className: 'bg-state-warning' },
];

/**
 * The composer's token readout, with the arithmetic spelled out.
 *
 * Two numbers sit next to each other and they measure different things: the last turn, and the sum
 * of every turn. Without that, "58.0k tok · 177.3k session" reads like a contradiction. The panel
 * also states the part that makes the session total look inflated — a resumed conversation re-sends
 * its whole history, so earlier context is counted again on every turn.
 */
export const TokenUsageTooltip: React.FC<TokenUsageTooltipProps> = ({ turn, session, turns }) => {
  const [isOpen, setIsOpen] = useState(false);
  const [anchor, setAnchor] = useState<{ bottom: number; left: number } | null>(null);

  const triggerRef = useRef<HTMLSpanElement>(null);
  const openTimer = useRef<number | null>(null);

  const turnTotal = turn?.totalTokens || 0;
  const sessionTotal = session?.totalTokens || 0;

  const measure = useCallback(() => {
    const rect = triggerRef.current?.getBoundingClientRect();
    if (!rect) return;
    setAnchor({
      bottom: Math.max(VIEWPORT_MARGIN, window.innerHeight - rect.top + 6),
      // Right-aligned to the readout: it sits at the right edge of the composer.
      left: Math.max(VIEWPORT_MARGIN, Math.min(rect.right - PANEL_WIDTH, window.innerWidth - PANEL_WIDTH - VIEWPORT_MARGIN)),
    });
  }, []);

  useLayoutEffect(() => {
    if (isOpen) measure();
  }, [isOpen, measure]);

  useEffect(() => {
    if (!isOpen) return;
    const handleReposition = () => {
      const rect = triggerRef.current?.getBoundingClientRect();
      if (!rect) return;
      if (rect.bottom < 0 || rect.top > window.innerHeight) {
        setIsOpen(false);
        return;
      }
      measure();
    };
    const handleEscape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setIsOpen(false);
    };

    window.addEventListener('resize', handleReposition);
    window.addEventListener('scroll', handleReposition, true);
    document.addEventListener('keydown', handleEscape);
    return () => {
      window.removeEventListener('resize', handleReposition);
      window.removeEventListener('scroll', handleReposition, true);
      document.removeEventListener('keydown', handleEscape);
    };
  }, [isOpen, measure]);

  useEffect(() => () => {
    if (openTimer.current !== null) window.clearTimeout(openTimer.current);
  }, []);

  const scheduleOpen = () => {
    if (openTimer.current !== null) window.clearTimeout(openTimer.current);
    // Short delay: the readout sits next to the send button, and a panel that flashes on every pass
    // is worse than one that waits.
    openTimer.current = window.setTimeout(() => setIsOpen(true), OPEN_DELAY_MS);
  };

  const cancelOpen = () => {
    if (openTimer.current !== null) {
      window.clearTimeout(openTimer.current);
      openTimer.current = null;
    }
    setIsOpen(false);
  };

  if (turnTotal <= 0) return null;

  const segments = SEGMENTS.map(segment => ({ ...segment, value: turn?.[segment.key] || 0 })).filter(
    segment => segment.value > 0
  );

  return (
    <>
      <span
        ref={triggerRef}
        tabIndex={0}
        role="button"
        aria-label="Token usage details"
        aria-expanded={isOpen}
        onMouseEnter={scheduleOpen}
        onMouseLeave={cancelOpen}
        onFocus={() => setIsOpen(true)}
        onBlur={cancelOpen}
        className="flex cursor-help items-baseline gap-1.5 rounded font-mono text-[10.5px] tabular-nums text-content-dim outline-none transition hover:text-content-muted focus-visible:ring-1 focus-visible:ring-accent/40"
      >
        <span className="text-content-muted">{format(turnTotal)} tok</span>
        {sessionTotal > turnTotal ? <span>· {format(sessionTotal)} session</span> : null}
      </span>

      {isOpen && anchor
        ? createPortal(
            <div
              role="tooltip"
              style={{ bottom: anchor.bottom, left: anchor.left, width: PANEL_WIDTH }}
              className="pointer-events-none fixed z-50 rounded-lg border border-surface-border bg-surface-card p-3 shadow-xl"
            >
              <div className="flex items-baseline justify-between gap-3">
                <span className="text-[11px] font-medium text-content-main">Last turn</span>
                <span className="font-mono text-[11px] tabular-nums text-content-main">{format(turnTotal)}</span>
              </div>

              <div className="mt-1.5 flex h-1 overflow-hidden rounded-full bg-surface-strong">
                {segments.map(segment => (
                  <span
                    key={segment.key}
                    className={segment.className}
                    style={{ width: `${(segment.value / turnTotal) * 100}%` }}
                  />
                ))}
              </div>

              <div className="mt-2 space-y-0.5">
                {segments.map(segment => (
                  <div key={segment.key} className="flex items-baseline justify-between gap-3">
                    <span className="flex items-center gap-1.5 text-[10.5px] text-content-muted">
                      <span className={`h-1.5 w-1.5 rounded-full ${segment.className}`} />
                      {segment.label}
                    </span>
                    <span className="font-mono text-[10.5px] tabular-nums text-content-dim">
                      {format(segment.value)}
                    </span>
                  </div>
                ))}
              </div>

              {sessionTotal > turnTotal ? (
                <>
                  <div className="my-2.5 border-t border-surface-border/70" />
                  <div className="flex items-baseline justify-between gap-3">
                    <span className="text-[11px] font-medium text-content-main">This session</span>
                    <span className="font-mono text-[11px] tabular-nums text-content-main">{format(sessionTotal)}</span>
                  </div>
                  <p className="mt-1 text-[10px] leading-relaxed text-content-dim">
                    {turns && turns > 1 ? `Added up over ${turns} replies. ` : 'Added up over every reply. '}
                    Each turn re-sends the conversation, so earlier context is counted again. This is
                    tokens processed, not unique text.
                  </p>
                </>
              ) : (
                <p className="mt-2 text-[10px] leading-relaxed text-content-dim">
                  Counted once per completed turn, after the turn ends.
                </p>
              )}
            </div>,
            document.body
          )
        : null}
    </>
  );
};
