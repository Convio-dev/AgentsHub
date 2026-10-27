import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Gauge } from 'lucide-react';
import { RunnerProfile } from '../../types/agent';
import { formatResetAt, quotaColor, quotaPillClass } from '../../lib/quota';

interface QuotaPopoverProps {
  profile?: RunnerProfile;
  /** Side of the trigger the panel opens on. The composer sits at the bottom, so it opens upward. */
  placement?: 'top' | 'bottom';
  isChecking?: boolean;
  onRefresh?: () => void;
  children: React.ReactNode;
}

const PANEL_WIDTH = 300;
const VIEWPORT_MARGIN = 8;
const CLOSE_DELAY_MS = 140;

function formatAge(timestamp?: number): string {
  if (!timestamp) return 'never checked';
  const seconds = Math.max(0, Math.round((Date.now() - timestamp) / 1000));
  if (seconds < 60) return `checked ${seconds}s ago`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `checked ${minutes}m ago`;
  return `checked ${Math.round(minutes / 60)}h ago`;
}

/**
 * Hover card listing every quota window the CLI reported.
 *
 * The rows are rendered from the engine's own labels, grouped by the engine's own group names, so a
 * renamed or newly added bucket ("Weekly Limit Remaining", "Five Hour Limit Remaining", whatever
 * comes next) appears without touching this component.
 *
 * Portalled: the composer card clips its children, and a panel inside it would be cut off.
 */
export const QuotaPopover: React.FC<QuotaPopoverProps> = ({
  profile,
  placement = 'top',
  isChecking = false,
  onRefresh,
  children,
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const [anchor, setAnchor] = useState<{ left: number; top?: number; bottom?: number } | null>(null);

  const triggerRef = useRef<HTMLSpanElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const closeTimer = useRef<number | null>(null);
  // Placement is a hint: if the panel would leave the viewport it flips to the other side once.
  const hasFlipped = useRef(false);

  const cancelClose = () => {
    if (closeTimer.current !== null) {
      window.clearTimeout(closeTimer.current);
      closeTimer.current = null;
    }
  };

  const scheduleClose = () => {
    cancelClose();
    closeTimer.current = window.setTimeout(() => setIsOpen(false), CLOSE_DELAY_MS);
  };

  const measure = useCallback(() => {
    const rect = triggerRef.current?.getBoundingClientRect();
    if (!rect) return;

    const left = Math.max(
      VIEWPORT_MARGIN,
      Math.min(rect.left, window.innerWidth - PANEL_WIDTH - VIEWPORT_MARGIN)
    );

    if (placement === 'top') {
      setAnchor({ left, bottom: Math.max(VIEWPORT_MARGIN, window.innerHeight - rect.top + VIEWPORT_MARGIN) });
    } else {
      setAnchor({ left, top: rect.bottom + VIEWPORT_MARGIN });
    }
  }, [placement]);

  const open = () => {
    cancelClose();
    hasFlipped.current = false;
    measure();
    setIsOpen(true);
  };

  useLayoutEffect(() => {
    if (!isOpen || hasFlipped.current) return;

    const panel = panelRef.current?.getBoundingClientRect();
    const trigger = triggerRef.current?.getBoundingClientRect();
    if (!panel || !trigger) return;

    const overflowsTop = panel.top < VIEWPORT_MARGIN;
    const overflowsBottom = panel.bottom > window.innerHeight - VIEWPORT_MARGIN;
    if (!overflowsTop && !overflowsBottom) return;

    hasFlipped.current = true;

    if (overflowsTop && !overflowsBottom) {
      setAnchor({ left: panel.left, top: trigger.bottom + VIEWPORT_MARGIN });
    } else if (overflowsBottom && !overflowsTop) {
      setAnchor({ left: panel.left, bottom: window.innerHeight - trigger.top + VIEWPORT_MARGIN });
    }
  }, [isOpen, anchor]);

  useEffect(() => {
    if (!isOpen) return;

    const handleReposition = () => {
      const rect = triggerRef.current?.getBoundingClientRect();
      if (!rect || rect.bottom < 0 || rect.top > window.innerHeight) {
        setIsOpen(false);
        return;
      }
      measure();
    };

    window.addEventListener('scroll', handleReposition, true);
    window.addEventListener('resize', handleReposition);

    return () => {
      window.removeEventListener('scroll', handleReposition, true);
      window.removeEventListener('resize', handleReposition);
    };
  }, [isOpen, measure]);

  useEffect(() => () => cancelClose(), []);

  const windows = profile?.quotaWindows ?? [];
  const hasWindows = windows.length > 0;

  // Fall back to the two scalars when the window list is missing (older records).
  const fallbackRows =
    !hasWindows && typeof profile?.quotaPercent === 'number'
      ? [
          {
            group: profile.quotaGroup || 'Quota',
            label: 'Five Hour Limit Remaining',
            percent: profile.quotaPercent,
            resetAt: profile.quotaResetAt,
          },
          ...(typeof profile?.quotaWeeklyPercent === 'number'
            ? [
                {
                  group: profile.quotaGroup || 'Quota',
                  label: 'Weekly Limit Remaining',
                  percent: profile.quotaWeeklyPercent,
                  resetAt: profile.quotaWeeklyResetAt,
                },
              ]
            : []),
        ]
      : [];

  const rows = hasWindows ? windows : fallbackRows;

  // Group by the engine's group name, preserving its order.
  const groups: Array<{ name: string; rows: typeof rows }> = [];
  for (const row of rows) {
    const existing = groups.find(group => group.name === row.group);
    if (existing) existing.rows.push(row);
    else groups.push({ name: row.group, rows: [row] });
  }

  const basePercent = profile?.quotaPercent;

  const panel =
    isOpen && anchor ? (
      <div
        ref={panelRef}
        role="tooltip"
        onMouseEnter={cancelClose}
        onMouseLeave={scheduleClose}
        style={{ left: anchor.left, top: anchor.top, bottom: anchor.bottom, width: PANEL_WIDTH }}
        className="fixed z-[90] overflow-hidden rounded-xl border border-surface-border bg-surface-card shadow-2xl"
      >
        <div className="flex items-center justify-between border-b border-surface-border/70 px-3 py-2">
          <span className="flex items-center gap-1.5 text-[11px] font-semibold text-content-main">
            <Gauge className="h-3.5 w-3.5 text-accent" />
            <span>Quota</span>
            {profile?.name ? (
              <span className="font-mono text-[9.5px] font-normal text-content-dim">{profile.name}</span>
            ) : null}
          </span>

          {typeof basePercent === 'number' && basePercent >= 0 ? (
            <span
              className={`rounded border px-1.5 py-0.5 font-mono text-[9.5px] font-bold tabular-nums ${quotaPillClass(basePercent)}`}
            >
              {Math.round(basePercent)}%
            </span>
          ) : null}
        </div>

        {groups.length === 0 ? (
          <p className="px-3 py-4 text-center font-mono text-[10.5px] leading-relaxed text-content-dim">
            No quota reading yet — click to check.
          </p>
        ) : (
          <div className="max-h-[240px] overflow-y-auto">
            {groups.map(group => (
              <div key={group.name} className="border-b border-surface-border/40 last:border-b-0">
                <div className="px-3 pt-2 pb-1 font-mono text-[9.5px] uppercase tracking-[0.06em] text-content-dim">
                  {group.name}
                </div>

                {group.rows.map(row => (
                  <div
                    key={`${row.group}-${row.label}`}
                    className="flex items-center justify-between gap-3 px-3 py-1"
                  >
                    <span className="min-w-0 flex-1 truncate text-[11px] text-content-muted">{row.label}</span>
                    <span className="flex flex-shrink-0 items-baseline gap-2">
                      <span
                        className="font-mono text-[11.5px] font-semibold tabular-nums"
                        style={{ color: quotaColor(row.percent) }}
                      >
                        {Math.round(row.percent)}%
                      </span>
                      <span className="w-[74px] text-right font-mono text-[9px] tabular-nums text-content-dim">
                        {formatResetAt(row.resetAt) || '—'}
                      </span>
                    </span>
                  </div>
                ))}
              </div>
            ))}
          </div>
        )}

        <div className="flex items-center justify-between border-t border-surface-border/70 px-3 py-1.5 font-mono text-[9.5px] text-content-dim">
          <span>{formatAge(profile?.lastQuotaChecked)}</span>
          {onRefresh ? (
            <button
              type="button"
              onClick={onRefresh}
              className="text-accent transition-colors hover:underline disabled:cursor-wait disabled:opacity-60"
              disabled={isChecking}
            >
              {isChecking ? 'checking…' : 'refresh'}
            </button>
          ) : null}
        </div>
      </div>
    ) : null;

  return (
    <>
      <span
        ref={triggerRef}
        onMouseEnter={open}
        onMouseLeave={scheduleClose}
        onFocus={open}
        onBlur={scheduleClose}
        className="inline-flex"
      >
        {children}
      </span>

      {panel ? createPortal(panel, document.body) : null}
    </>
  );
};
