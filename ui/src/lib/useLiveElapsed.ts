import { useEffect, useRef, useState } from 'react';

/**
 * Live elapsed milliseconds since `startedAt`, ticking only while `isRunning`.
 *
 * Used for the `Thinking 2.4s` / `Worked for 33m` labels. The timer stops as soon as the work
 * finishes, so nothing keeps animating after a turn completes.
 */
export function useLiveElapsed(startedAt: number, isRunning: boolean, tickMs = 100): number {
  const [now, setNow] = useState(() => Date.now());
  const startRef = useRef(startedAt);

  useEffect(() => {
    startRef.current = startedAt;
  }, [startedAt]);

  useEffect(() => {
    if (!isRunning) return;

    setNow(Date.now());
    const interval = window.setInterval(() => setNow(Date.now()), tickMs);
    return () => window.clearInterval(interval);
  }, [isRunning, tickMs]);

  if (!isRunning) return 0;
  return Math.max(0, now - startRef.current);
}
