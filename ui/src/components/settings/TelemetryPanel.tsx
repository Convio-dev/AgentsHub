import React, { useEffect, useState } from 'react';
import { Activity, RefreshCw, HardDrive, Wifi, Cpu } from 'lucide-react';
import { Button } from '../common/Button';
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
    <div className="space-y-3 rounded-xl border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-card,#0F172A)] p-5">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="flex items-center gap-1.5 text-sm font-bold text-[var(--text-main,#F1F5F9)]">
            <Activity className="h-3.5 w-3.5 text-accent" />
            Hardware snapshot
          </h3>
          <p className="mt-0.5 text-xs text-[var(--text-dim,#64748B)]">
            Read only while this page is open — nothing samples in the background.
          </p>
        </div>

        <Button
          variant="secondary"
          size="xs"
          onClick={() => void sample()}
          disabled={isBusy}
          leftIcon={<RefreshCw className={`h-3.5 w-3.5 ${isBusy ? 'animate-spin' : ''}`} />}
        >
          {isBusy ? 'Reading…' : 'Refresh'}
        </Button>
      </div>

      {error ? <p className="font-mono text-[11px] text-state-danger">{error}</p> : null}

      {snapshot ? (
        <>
          <div className="space-y-1.5">
            <div className="flex items-center justify-between font-mono text-[11px]">
              <span className="flex items-center gap-1.5 text-[var(--text-muted,#94A3B8)]">
                <HardDrive className="h-3 w-3" />
                Memory
              </span>
              <span className="text-[var(--text-main,#F1F5F9)]">
                {snapshot.memoryUsedGb} GB of {snapshot.memoryTotalGb} GB ({usedPct}%)
              </span>
            </div>
            <div className="h-1.5 overflow-hidden rounded-full bg-[var(--bg-input,#0D1322)]">
              <div className="h-full rounded-full bg-sky-500" style={{ width: `${usedPct}%` }} />
            </div>
          </div>

          <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
            <div className="rounded-lg border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-input,#0D1322)] px-2.5 py-2">
              <div className="flex items-center gap-1.5 font-mono text-[10px] text-[var(--text-dim,#64748B)]">
                <Cpu className="h-3 w-3" />
                Agents Hub
              </div>
              <div className="mt-0.5 font-mono text-xs text-[var(--text-main,#F1F5F9)]">{snapshot.hubMemMb} MB</div>
            </div>
            <div className="rounded-lg border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-input,#0D1322)] px-2.5 py-2">
              <div className="flex items-center gap-1.5 font-mono text-[10px] text-[var(--text-dim,#64748B)]">
                <Wifi className="h-3 w-3" />
                Network
              </div>
              <div className="mt-0.5 font-mono text-xs text-[var(--text-main,#F1F5F9)]">{snapshot.netKbps} KB/s</div>
            </div>
            <div className="rounded-lg border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-input,#0D1322)] px-2.5 py-2">
              <div className="font-mono text-[10px] text-[var(--text-dim,#64748B)]">Sampled</div>
              <div className="mt-0.5 font-mono text-xs text-[var(--text-main,#F1F5F9)]">{seenAt}</div>
            </div>
          </div>
        </>
      ) : (
        <p className="font-mono text-[11px] text-[var(--text-dim,#64748B)]">
          {isBusy ? 'Reading the machine…' : 'No reading yet.'}
        </p>
      )}
    </div>
  );
};
