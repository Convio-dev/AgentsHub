import React from 'react';
import { Cpu, Zap, Activity, HardDrive } from 'lucide-react';
import { SessionRecord, HardwareTelemetry } from '../../types/agent';
import { Tooltip } from '../common/Tooltip';

interface TelemetryPillsProps {
  activeSession?: SessionRecord;
  telemetry: HardwareTelemetry;
}

export const TelemetryPills: React.FC<TelemetryPillsProps> = ({ activeSession, telemetry }) => {
  if (!activeSession) return null;

  const quota = activeSession.quotaPercent ?? 100;
  const quotaColor =
    quota > 50
      ? 'text-emerald-400 bg-emerald-500/10 border-emerald-500/25'
      : quota > 20
      ? 'text-amber-400 bg-amber-500/10 border-amber-500/25'
      : 'text-rose-400 bg-rose-500/10 border-rose-500/25';

  const totalTokens = activeSession.tokens?.totalTokens || 0;

  const systemTooltip = (
    <div className="space-y-1.5 p-1 font-mono text-[11px] select-none">
      <div className="font-semibold text-slate-200 pb-1 border-b border-slate-700/80">
        System Hardware Telemetry
      </div>
      <div className="flex justify-between gap-4 text-slate-400">
        <span>RAM Usage:</span>
        <span className="text-slate-200">
          {telemetry.ramMb} MB / {telemetry.totalRamMb} MB ({telemetry.memoryUsedGb} GB)
        </span>
      </div>
      <div className="flex justify-between gap-4 text-slate-400">
        <span>Network:</span>
        <span className="text-emerald-400">{telemetry.netKbps} KB/s</span>
      </div>
      {telemetry.hermesMemMb > 0 && (
        <div className="flex justify-between gap-4 text-slate-400">
          <span>AgentsHub Host RAM:</span>
          <span className="text-sky-400">{telemetry.hermesMemMb} MB</span>
        </div>
      )}
    </div>
  );

  return (
    <div className="flex items-center space-x-2 font-mono text-xs select-none">
      {/* 1. Model & Reasoning Effort (Scoped to Active Session) */}
      <div className="hidden lg:flex items-center space-x-1.5 px-2.5 py-1 rounded-lg bg-slate-900 border border-slate-800 text-slate-300">
        <Zap className="w-3.5 h-3.5 text-sky-400" />
        <span className="font-medium text-slate-200 truncate max-w-[140px]">
          {activeSession.model || 'Gemini 2.5 Flash'}
        </span>
        {activeSession.reasoningEffort && (
          <span className="px-1.5 py-0.2 rounded text-[10px] bg-slate-800 text-sky-400 border border-slate-700/60 font-semibold">
            {activeSession.reasoningEffort}
          </span>
        )}
      </div>

      {/* 2. Quota % Indicator (Scoped to Active Session) */}
      <div
        className={`flex items-center space-x-1.5 px-2.5 py-1 rounded-lg border font-semibold text-[11px] shadow-sm ${quotaColor}`}
        title={`Session Quota: ${quota}% remaining`}
      >
        <Activity className="w-3.5 h-3.5" />
        <span>Quota: {quota}%</span>
      </div>

      {/* 3. Session Tokens */}
      {totalTokens > 0 && (
        <div className="hidden sm:flex items-center space-x-1 px-2.5 py-1 rounded-lg bg-slate-900 border border-slate-800 text-slate-300 text-[11px]">
          <span className="text-slate-400">Tokens:</span>
          <span className="font-semibold text-sky-400">{totalTokens.toLocaleString()}</span>
        </div>
      )}

      {/* 4. Hardware System Pill with Tooltip */}
      <Tooltip content={systemTooltip} position="bottom">
        <div className="hidden xl:flex items-center space-x-1.5 px-2.5 py-1 rounded-lg bg-slate-900 border border-slate-800 text-slate-400 hover:text-slate-200 transition cursor-default text-[11px]">
          <HardDrive className="w-3.5 h-3.5 text-slate-500" />
          <span>RAM: {telemetry.memoryUsedGb}G</span>
          <span className="text-slate-600">|</span>
          <span className="text-emerald-400">{telemetry.netKbps} KB/s</span>
        </div>
      </Tooltip>
    </div>
  );
};
