/**
 * Quota thresholds, shared by the composer ring and the profile cards so both agree on when a
 * reading should look alarming.
 */

export type QuotaTone = 'healthy' | 'low' | 'critical';

/** Above 30% is healthy, 11–30% is low, 10% or less is critical. */
export function quotaTone(percent: number): QuotaTone {
  if (percent > 30) return 'healthy';
  if (percent > 10) return 'low';
  return 'critical';
}

/** CSS colour token for strokes and text, matching the current theme. */
export function quotaColor(percent: number): string {
  switch (quotaTone(percent)) {
    case 'low':
      return 'var(--state-warning)';
    case 'critical':
      return 'var(--state-danger)';
    default:
      return 'var(--accent-ink)';
  }
}

/** Tailwind classes for a quota pill. */
export function quotaPillClass(percent: number): string {
  switch (quotaTone(percent)) {
    case 'low':
      return 'bg-amber-500/10 text-state-warning border-amber-500/30';
    case 'critical':
      return 'bg-rose-500/10 text-state-danger border-rose-500/30';
    default:
      return 'bg-sky-500/10 text-accent border-sky-500/30';
  }
}

/** Reset time for a quota window, as local time (date shown only when it is not today). */
export function formatResetAt(iso?: string): string {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';

  const time = date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const isToday = date.toDateString() === new Date().toDateString();
  return isToday ? time : `${date.toLocaleDateString([], { month: 'short', day: 'numeric' })} ${time}`;
}
