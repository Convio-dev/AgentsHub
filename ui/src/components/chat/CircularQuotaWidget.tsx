import React, { useCallback, useEffect, useMemo } from 'react';
import { useProfileStore } from '../../store/useProfileStore';
import { quotaColor } from '../../lib/quota';
import { QuotaPopover } from '../common/QuotaPopover';

interface CircularQuotaWidgetProps {
  /** Profile whose quota this ring shows. Empty renders an inactive ring. */
  profileName?: string;
  /** Outer diameter in px. */
  size?: number;
  strokeWidth?: number;
}

/** Background refresh cadence. The sidecar polls too; this only fires when the data is stale. */
const REFRESH_INTERVAL_MS = 3 * 60 * 1000;
const STALE_AFTER_MS = 3 * 60 * 1000;

/**
 * Circular remaining-quota indicator for the composer footer.
 *
 * Quota belongs to one authenticated profile, so the ring reads only that profile's value and never
 * falls back to another account's number. It refreshes on a timer when the cached reading is stale,
 * and a click forces an immediate check.
 */
export const CircularQuotaWidget: React.FC<CircularQuotaWidgetProps> = ({
  profileName,
  size = 30,
  strokeWidth = 3,
}) => {
  const { profiles, quotaPending, checkQuota } = useProfileStore();

  const profile = useMemo(
    () => profiles.find(entry => entry.name.toLowerCase() === (profileName || '').toLowerCase()),
    [profiles, profileName]
  );

  const key = (profileName || '').toLowerCase();
  const isChecking = Boolean(quotaPending[key]);
  const percent = profile?.quotaPercent;
  const hasReading = typeof percent === 'number' && percent >= 0;

  const refresh = useCallback(() => {
    if (!profileName || isChecking) return;
    void checkQuota(profileName);
  }, [checkQuota, isChecking, profileName]);

  // Refresh only when the cached reading has gone stale, so this never stacks extra `agy` calls on
  // top of the sidecar's own background poll. The profile is read from the store inside the tick
  // rather than closed over: a captured snapshot would keep seeing the pre-check timestamp.
  useEffect(() => {
    if (!profileName) return;

    const maybeRefresh = () => {
      const store = useProfileStore.getState();
      const current = store.profiles.find(
        entry => entry.name.toLowerCase() === profileName.toLowerCase()
      );

      // The profile list may not have hydrated yet; checking an unknown name would spawn a CLI run
      // against a profile that does not exist.
      if (!current) return;

      const last = current.lastQuotaChecked;
      if (!last || Date.now() - last > STALE_AFTER_MS) {
        void store.checkQuota(profileName);
      }
    };

    maybeRefresh();
    const timer = window.setInterval(maybeRefresh, REFRESH_INTERVAL_MS);
    return () => window.clearInterval(timer);
  }, [profileName]);

  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const clamped = hasReading ? Math.min(100, Math.max(0, percent)) : 0;
  const dashOffset = circumference * (1 - clamped / 100);

  const ringColor = hasReading ? quotaColor(clamped) : 'var(--border-light)';

  // Every window (5-hour, weekly, and anything the CLI adds) is listed in the popover.
  const label = !profileName
    ? 'No profile selected'
    : hasReading
      ? `Quota: ${clamped}% left. Hover for every window, click to refresh.`
      : `Quota not read yet for ${profileName}. Click to check.`;

  return (
    <QuotaPopover profile={profile} placement="top" isChecking={isChecking} onRefresh={refresh}>
      <button
        type="button"
        onClick={refresh}
        disabled={!profileName || isChecking}
        aria-label={label}
        className="group/quota relative flex flex-shrink-0 items-center justify-center rounded-full transition-opacity disabled:cursor-default"
      style={{ width: size, height: size }}
    >
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        className={isChecking ? 'animate-spin' : 'transition-transform duration-300'}
        aria-hidden="true"
      >
        {/* Track */}
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke="var(--overlay-strong)"
          strokeWidth={strokeWidth}
        />

        {/* Progress arc, starting at twelve o'clock */}
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke={ringColor}
          strokeWidth={strokeWidth}
          strokeLinecap="round"
          strokeDasharray={circumference}
          strokeDashoffset={dashOffset}
          transform={`rotate(-90 ${size / 2} ${size / 2})`}
          style={{ transition: 'stroke-dashoffset 400ms ease, stroke 300ms ease' }}
        />
      </svg>

      {/*
        The number stays visible while a refresh runs: hiding it during every check made the
        reading look like it had been lost. The spinning ring is the only "busy" signal.
      */}
      {hasReading ? (
        <span
          className={`pointer-events-none absolute inset-0 flex items-center justify-center font-mono text-[8.5px] font-semibold tabular-nums transition-opacity ${
            isChecking ? 'opacity-60' : 'opacity-100'
          }`}
          style={{ color: ringColor }}
        >
          {Math.round(clamped)}
        </span>
      ) : null}
      </button>
    </QuotaPopover>
  );
};
