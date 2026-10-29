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
      ? 'text-state-success bg-emerald-500/10 border-emerald-500/25'
      : quota > 20
      ? 'text-state-warning bg-amber-500/10 border-amber-500/25'
      : 'text-state-danger bg-rose-500/10 border-rose-500/25';

  const totalTokens = activeSession.tokens?.totalTokens || 0;

  const systemTooltip = (
    <div className="space-y-1.5 p-1 font-mono text-[11px] select-none">
      <div className="font-semibold text-content-main pb-1 border-b border-surface-border/80">
        System Hardware Telemetry
      </div>
      <div className="flex justify-between gap-4 text-content-muted">
        <span>RAM Usage:</span>
        <span className="text-content-main">
          {telemetry.ramMb} MB / {telemetry.totalRamMb} MB ({telemetry.memoryUsedGb} GB)
        </span>
      </div>
      <div className="flex justify-between gap-4 text-content-muted">
        <span>Network:</span>
        <span className="text-state-success">{telemetry.netKbps} KB/s</span>
      </div>
    </div>
  );

  return (
    <div className="flex items-center space-x-2 font-mono text-xs select-none">
      {/* 1. Model & Reasoning Effort (Scoped to Active Session) */}
      <div className="hidden lg:flex items-center space-x-1.5 px-2.5 py-1 rounded-lg bg-surface-card border border-surface-border text-content-muted">
        <Zap className="w-3.5 h-3.5 text-accent" />
        <span className="font-medium text-content-main truncate max-w-[140px]">
          {activeSession.model || 'Gemini 2.5 Flash'}
        </span>
        {activeSession.reasoningEffort && (
          <span className="px-1.5 py-0.2 rounded text-[10px] bg-surface-strong text-accent border border-surface-border/60 font-semibold">
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
        <div className="hidden sm:flex items-center space-x-1 px-2.5 py-1 rounded-lg bg-surface-card border border-surface-border text-content-muted text-[11px]">
          <span className="text-content-muted">Tokens:</span>
          <span className="font-semibold text-accent">{totalTokens.toLocaleString()}</span>
        </div>
      )}

      {/* 4. Hardware System Pill with Tooltip */}
      <Tooltip content={systemTooltip} position="bottom">
        <div className="hidden xl:flex items-center space-x-1.5 px-2.5 py-1 rounded-lg bg-surface-card border border-surface-border text-content-muted hover:text-content-main transition cursor-default text-[11px]">
          <HardDrive className="w-3.5 h-3.5 text-content-dim" />
          <span>RAM: {telemetry.memoryUsedGb}G</span>
          <span className="text-content-dim">|</span>
          <span className="text-state-success">{telemetry.netKbps} KB/s</span>
        </div>
      </Tooltip>
    </div>
  );
};
