import React, { useState } from 'react';
import {
  Terminal,
  Layers,
  History,
  ShieldAlert,
  Mic,
  Video,
  ExternalLink,
  Trash2,
  Play,
  RotateCcw,
} from 'lucide-react';
import { XTermView } from '../components/terminal/XTermView';

export const TerminalsView: React.FC = () => {
  const [activeTab, setActiveTab] = useState<'hermes' | 'claude_code' | 'antigravity_cli' | 'powershell'>('antigravity_cli');
  const [activePreset, setActivePreset] = useState('AI Duo (Claude + agy)');
  const [snapshots, setSnapshots] = useState<string[]>([
    'Checkpoint #1 - Initial workspace state',
    'Checkpoint #2 - Antislop filters injected',
  ]);
  const [selectedSnapshot, setSelectedSnapshot] = useState(snapshots[0] || '');

  const handleCreateSnapshot = () => {
    const name = `Checkpoint #${snapshots.length + 1} - ${new Date().toLocaleTimeString()}`;
    setSnapshots(prev => [name, ...prev]);
    setSelectedSnapshot(name);
  };

  return (
    <div className="flex-1 flex flex-col h-full bg-surface-deep text-content-main select-none overflow-hidden font-sans">
      {/* 1. Presets Toolbar */}
      <div className="px-4 py-2 border-b border-surface-border bg-surface-card/60 flex items-center justify-between flex-wrap gap-2 text-xs">
        <div className="flex items-center space-x-2">
          <Layers className="w-3.5 h-3.5 text-content-dim" />
          <span className="font-semibold text-content-muted">Workspace Presets:</span>
          <div className="flex items-center space-x-1.5">
            {['Clean Slate', 'AI Duo (Claude + agy)', 'Full Suite'].map(preset => (
              <button
                key={preset}
                onClick={() => setActivePreset(preset)}
                className={`px-2.5 py-1 rounded-lg text-xs font-medium transition ${
                  activePreset === preset
                    ? 'bg-sky-500/15 text-sky-400 border border-sky-500/30'
                    : 'bg-surface-deep hover:bg-slate-800 text-content-muted border border-surface-border'
                }`}
              >
                {preset}
              </button>
            ))}
          </div>
        </div>

        <span className="text-[11px] font-mono text-content-dim">
          Active: <strong className="text-content-muted">{activePreset}</strong>
        </span>
      </div>

      {/* 2. Checkpoints & Time-Machine Toolbar */}
      <div className="px-4 py-1.5 border-b border-surface-border bg-surface-deep/80 flex items-center justify-between flex-wrap gap-2 text-xs">
        <div className="flex items-center space-x-2">
          <History className="w-3.5 h-3.5 text-sky-400" />
          <span className="font-semibold text-content-muted">Checkpoints:</span>
          <select
            value={selectedSnapshot}
            onChange={e => setSelectedSnapshot(e.target.value)}
            className="bg-surface-card border border-surface-border text-content-main rounded-md px-2 py-0.5 text-xs font-mono focus:outline-none focus:border-sky-500"
          >
            {snapshots.map(s => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
          <button
            onClick={handleCreateSnapshot}
            className="px-2 py-0.5 rounded bg-surface-card border border-surface-border hover:border-sky-500 text-content-muted hover:text-content-main transition flex items-center gap-1"
          >
            <span>+ Snapshot</span>
          </button>
          <button
            className="px-2 py-0.5 rounded bg-surface-card border border-surface-border hover:border-amber-500 text-amber-400 transition flex items-center gap-1"
            title="Rollback workspace to snapshot"
          >
            <RotateCcw className="w-3 h-3" />
            <span>Rollback</span>
          </button>
        </div>

        <div className="flex items-center space-x-1.5">
          <button
            className="flex items-center gap-1.5 px-2.5 py-0.5 rounded bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 font-mono text-[11px]"
            title="Pre-Commit Security Audit (Secrets & Guardrails)"
          >
            <ShieldAlert className="w-3.5 h-3.5" />
            <span>Security Audit: 100/100</span>
          </button>
        </div>
      </div>

      {/* 3. Terminal Tabs Bar */}
      <div className="px-4 py-1.5 border-b border-surface-border bg-surface-card flex items-center justify-between text-xs">
        <div className="flex items-center space-x-1">
          {[
            { id: 'antigravity_cli', label: 'Antigravity CLI' },
            { id: 'claude_code', label: 'Claude Code' },
            { id: 'hermes', label: 'Hermes Web' },
            { id: 'powershell', label: 'PowerShell' },
          ].map(tab => {
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id as any)}
                className={`flex items-center space-x-1.5 px-3 py-1 rounded-t-lg transition font-mono ${
                  isActive
                    ? 'bg-surface-deep text-sky-400 border-t border-x border-surface-border font-semibold shadow-sm'
                    : 'text-content-muted hover:text-content-main hover:bg-surface-deep/40'
                }`}
              >
                <Terminal className="w-3.5 h-3.5" />
                <span>{tab.label}</span>
              </button>
            );
          })}
        </div>

        <div className="flex items-center space-x-1.5">
          <button
            className="px-2 py-1 rounded bg-surface-deep border border-surface-border hover:border-sky-500 text-content-muted hover:text-content-main flex items-center gap-1 transition"
            title="Voice Commander"
          >
            <Mic className="w-3 h-3" />
            <span>PTT Mic</span>
          </button>
          <button
            className="px-2 py-1 rounded bg-surface-deep border border-surface-border hover:border-sky-500 text-content-muted hover:text-content-main flex items-center gap-1 transition"
            title="Timelapse Record"
          >
            <Video className="w-3 h-3 text-rose-400" />
            <span>Record</span>
          </button>
        </div>
      </div>

      {/* 4. Terminal Host / XTerm Surface */}
      <div className="flex-1 p-2 bg-surface-deep flex flex-col overflow-hidden">
        <XTermView />
      </div>
    </div>
  );
};
