import React from 'react';
import { Cpu, Wifi, Activity } from 'lucide-react';
import { SidecarHealth } from '../../types/agent';
import { Tooltip } from '../common/Tooltip';

interface SidecarHealthPillProps {
  health: SidecarHealth;
  onRefresh?: () => void;
}

export const SidecarHealthPill: React.FC<SidecarHealthPillProps> = ({ health, onRefresh }) => {
  const isOnline = health.isOnline || health.status === 'Online';
  const isStarting = health.status === 'Starting' || health.status === 'Connecting';

  const dotColor = isOnline
    ? 'bg-emerald-400'
    : isStarting
    ? 'bg-amber-400 animate-pulse'
    : 'bg-rose-400';

  const tooltipContent = (
    <div className="space-y-1.5 p-1 font-mono text-[11px] leading-relaxed select-none">
      <div className="flex items-center justify-between gap-4 pb-1 border-b border-slate-700/80">
        <span className="font-semibold text-slate-200">Bun Sidecar Engine</span>
        <span
          className={`font-bold ${
            isOnline ? 'text-emerald-400' : isStarting ? 'text-amber-400' : 'text-rose-400'
          }`}
        >
          {health.status}
        </span>
      </div>
      <div className="flex items-center justify-between gap-4 text-slate-400">
        <span>Port:</span>
        <span className="text-slate-200">{health.port > 0 ? health.port : 'Auto (OS)'}</span>
      </div>
      <div className="flex items-center justify-between gap-4 text-slate-400">
        <span>Process ID (PID):</span>
        <span className="text-slate-200">{health.pid > 0 ? health.pid : 'N/A'}</span>
      </div>
      <div className="flex items-center justify-between gap-4 text-slate-400">
        <span>Latency:</span>
        <span className="text-emerald-400 font-semibold">{health.latencyMs}ms</span>
      </div>
      <div className="flex items-center justify-between gap-4 text-slate-400">
        <span>Active Sessions:</span>
        <span className="text-sky-400 font-semibold">{health.activeSessions}</span>
      </div>
      <div className="flex items-center justify-between gap-4 text-slate-400 pt-1 border-t border-slate-800">
        <span>Protocol:</span>
        <span className="text-slate-400">{health.version || 'v2.0.0'}</span>
      </div>
    </div>
  );

  return (
    <Tooltip content={tooltipContent} position="bottom">
      <button
        type="button"
        onClick={onRefresh}
        className="flex items-center justify-center w-7 h-7 rounded-lg bg-slate-900 border border-slate-800 hover:border-slate-700 hover:bg-slate-800 transition shadow-sm relative group"
        title="Sidecar Process Health"
      >
        <Cpu className="w-3.5 h-3.5 text-slate-400 group-hover:text-slate-200 transition" />
        <span
          className={`absolute top-1 right-1 w-1.5 h-1.5 rounded-full ${dotColor} shadow-sm`}
        />
      </button>
    </Tooltip>
  );
};
