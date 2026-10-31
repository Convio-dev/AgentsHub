import React, { useEffect, useState } from 'react';
import { Activity, RefreshCw, HardDrive, Wifi, Cpu } from 'lucide-react';
import { webviewBridge } from '../../services/webviewBridge';

interface Snapshot {
  memoryUsedGb: number;
  memoryTotalGb: number;
  hubMemMb: number;
  netKbps: number;
  sampledAt: number;
}

/**
 * Hardware readings, on request only.
 *
 * There is no polling loop behind this: opening the panel takes one sample, a second one a couple of
 * seconds later so the network delta is real, and closing it tells the host to stop its sampler.
 * The dashboard used to show the same numbers from a 2.5 s timer that ran for the whole session.
 */
export const TelemetryPanel: React.FC = () => {
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const sample = async () => {
    setIsBusy(true);
    try {
      const res: any = await webviewBridge.invoke('get_system_telemetry');
      setSnapshot({
        memoryUsedGb: Number(res?.memoryUsedGb ?? 0),
        memoryTotalGb: Number(res?.memoryTotalGb ?? 0),
        hubMemMb: Number(res?.hubMemMb ?? 0),
        netKbps: Number(res?.netKbps ?? 0),
        sampledAt: Date.now(),
      });
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The host did not answer.');
    } finally {
      setIsBusy(false);
    }
  };

  useEffect(() => {
    void sample();

    // Throughput is a delta between two readings, so one more sample while the panel is open makes
    // the number meaningful. The timer dies with the panel.
    const timer = setTimeout(() => void sample(), 2200);

    return () => {
      clearTimeout(timer);
      void webviewBridge.invoke('release_system_telemetry').catch(() => {});
    };
  }, []);

  const usedPct =
    snapshot && snapshot.memoryTotalGb > 0
      ? Math.min(100, Math.round((snapshot.memoryUsedGb / snapshot.memoryTotalGb) * 100))
      : 0;

  const seenAt = snapshot ? new Date(snapshot.sampledAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : null;

  return (
    <div className="rounded-xl border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-card,#0F172A)] px-3.5 py-2.5">
      <div className="flex items-center justify-between gap-3">
        <h3 className="flex items-center gap-1.5 text-[11.5px] font-bold text-[var(--text-main,#F1F5F9)]">
          <Activity className="h-3.5 w-3.5 text-accent" />
          Hardware snapshot
          <span className="ml-1 font-normal text-[10px] text-[var(--text-dim,#64748B)]">
            on demand, nothing runs in the background
          </span>
        </h3>

        <button
          type="button"
          onClick={() => void sample()}
          disabled={isBusy}
          title="Take a fresh reading"
          className="flex flex-shrink-0 items-center gap-1 rounded border border-[var(--border-subtle,#1E293B)] px-1.5 py-0.5 font-mono text-[10px] text-[var(--text-dim,#64748B)] transition-colors hover:text-[var(--text-main,#F1F5F9)] disabled:opacity-60"
        >
          <RefreshCw className={`h-2.5 w-2.5 ${isBusy ? 'animate-spin' : ''}`} />
          {isBusy ? 'reading' : 'refresh'}
        </button>
      </div>

      {error ? <p className="mt-1 font-mono text-[10.5px] text-state-danger">{error}</p> : null}

      {snapshot ? (
        <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1.5 font-mono text-[10.5px]">
          <span className="flex min-w-[200px] flex-1 items-center gap-2">
            <HardDrive className="h-3 w-3 flex-shrink-0 text-[var(--text-dim,#64748B)]" />
            <span className="h-1 w-full max-w-[160px] flex-shrink-0 overflow-hidden rounded-full bg-[var(--bg-input,#0D1322)]">
              <span className="block h-full rounded-full bg-sky-500" style={{ width: `${usedPct}%` }} />
            </span>
            <span className="whitespace-nowrap text-[var(--text-muted,#94A3B8)]">
              {snapshot.memoryUsedGb}/{snapshot.memoryTotalGb} GB
              <span className="ml-1 text-[var(--text-dim,#64748B)]">{usedPct}%</span>
            </span>
          </span>

          <span className="flex items-center gap-1.5 whitespace-nowrap">
            <Cpu className="h-3 w-3 text-[var(--text-dim,#64748B)]" />
            <span className="text-[var(--text-muted,#94A3B8)]">app</span>
            <span className="text-[var(--text-main,#F1F5F9)]">{snapshot.hubMemMb} MB</span>
          </span>

          <span className="flex items-center gap-1.5 whitespace-nowrap">
            <Wifi className="h-3 w-3 text-[var(--text-dim,#64748B)]" />
            <span className="text-[var(--text-muted,#94A3B8)]">net</span>
            <span className="text-[var(--text-main,#F1F5F9)]">{snapshot.netKbps} KB/s</span>
          </span>

          <span className="whitespace-nowrap text-[var(--text-dim,#64748B)]">{seenAt}</span>
        </div>
      ) : (
        <p className="mt-1 font-mono text-[10.5px] text-[var(--text-dim,#64748B)]">
          {isBusy ? 'Reading the machine…' : 'No reading yet.'}
        </p>
      )}
    </div>
  );
};
