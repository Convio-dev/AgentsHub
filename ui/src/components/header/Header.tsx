import React from 'react';
import {
  FolderOpen,
  GitBranch,
  Cpu,
  CheckCircle2,
  PanelLeft,
  PanelLeftOpen,
} from 'lucide-react';
import { useSidecarStore } from '../../store/useSidecarStore';
import { useWorkspaceStore } from '../../store/useWorkspaceStore';

interface HeaderProps {
  /** Kept for callers that still pass it; the settings entry point lives in the rail. */
  onOpenSettings?: () => void;
  onOpenWorkspaceModal?: () => void;
}

export const Header: React.FC<HeaderProps> = ({ onOpenWorkspaceModal }) => {
  const { health, toggleRail, isRailCollapsed } = useSidecarStore();

  const { activeWorkspace, branch } = useWorkspaceStore();

  return (
    <header
      id="top-header"
      className="h-10 bg-surface-deep border-b border-surface-border flex items-center justify-between px-3 text-xs text-content-muted z-30 select-none flex-shrink-0"
    >
      {/* 1. Left Group: Hamburger Toggle, Logo, Workspace/Branch Badge */}
      <div className="flex items-center space-x-3">
        <button
          id="btn-toggle-sidebar"
          onClick={toggleRail}
          className="w-7 h-7 flex items-center justify-center text-content-muted hover:text-content-main rounded-lg hover:bg-surface-card transition border border-transparent hover:border-surface-border"
          title={isRailCollapsed ? 'Show the navigation rail' : 'Hide the navigation rail'}
          aria-label={isRailCollapsed ? 'Show the navigation rail' : 'Hide the navigation rail'}
        >
          {isRailCollapsed ? <PanelLeftOpen className="w-4 h-4" /> : <PanelLeft className="w-4 h-4" />}
        </button>

        <div className="flex items-center space-x-2 font-semibold text-content-main">
          <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse" />
          <span className="tracking-wide text-xs">AGENTS HUB</span>
        </div>

        <span className="opacity-30">|</span>

        {/* Global Project Workspace & Git Branch Context */}
        <button
          onClick={onOpenWorkspaceModal}
          className="flex items-center space-x-1.5 px-2.5 py-1 rounded-lg bg-surface-card border border-surface-border text-xs shadow-sm hover:border-sky-500 transition group text-left"
          title="Click to Configure Workspace"
        >
          <FolderOpen className="w-3.5 h-3.5 text-state-warning" />
          <span id="header-ws-name" className="font-medium text-content-main">
            {activeWorkspace?.name || 'Default Workspace'}
          </span>
          <span className="opacity-30">/</span>
          <span id="header-branch-name" className="font-mono text-[10px] text-accent flex items-center gap-1">
            <GitBranch className="w-2.5 h-2.5 text-accent" />
            <span id="header-branch-text">{branch || 'main'}</span>
          </span>
        </button>
      </div>

      {/* 2. Right: sidecar health dot and the terminal HUD. Everything else that used to live here
          (ConPTY status, RAM/NET, notifications, refresh, settings) was noise on a 40px strip.
          Settings is in the rail, and the telemetry that mattered is in the sidecar popover. */}
      <div className="flex items-center space-x-2">
        {/* Bun Sidecar Pure Icon Indicator with Modern Hover Tooltip Popover */}
        <div id="sidecar-status-container" className="relative group/sidecar flex items-center">
          <button
            type="button"
            className="flex cursor-pointer items-center justify-center bg-transparent p-1 focus:outline-none"
            aria-label="Sidecar Engine Status"
          >
            <div className="relative flex items-center justify-center">
              {health.isOnline && (
                <span className="absolute inline-flex h-3 w-3 rounded-full bg-emerald-400 opacity-60 animate-ping" />
              )}
              <span
                className={`relative inline-flex h-2 w-2 rounded-full ${
                  health.isOnline ? 'bg-emerald-500' : 'bg-rose-500'
                }`}
              />
            </div>
          </button>

          {/* Interactive Hover Tooltip Popover */}
          <div
            id="sidecar-tooltip"
            className="absolute top-full mt-2 right-0 w-64 p-3 rounded-xl bg-surface-card/95 backdrop-blur-md border border-surface-border shadow-2xl opacity-0 pointer-events-none group-hover/sidecar:opacity-100 group-hover/sidecar:pointer-events-auto transition-all duration-200 z-50 transform -translate-y-1 group-hover/sidecar:translate-y-0 text-xs"
          >
            <div className="flex items-center justify-between pb-2 mb-2 border-b border-surface-border/60">
              <div className="flex items-center space-x-1.5">
                <Cpu className="w-3.5 h-3.5 text-accent" />
                <span className="font-semibold text-content-main">Sidecar Runtime</span>
              </div>
              <span className="px-1.5 py-0.5 text-[9px] font-mono rounded bg-emerald-500/15 text-state-success border border-emerald-500/30">
                {health.status} ({health.version})
              </span>
            </div>
            <div className="space-y-1.5 text-[11px] text-content-muted">
              <div className="flex items-center justify-between">
                <span>Status:</span>
                <span className="font-medium text-state-success flex items-center gap-1">
                  <CheckCircle2 className="w-3 h-3 text-state-success" />
                  <span>{health.isOnline ? 'Connected / Healthy' : 'Disconnected'}</span>
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span>Endpoint:</span>
                <span className="font-mono text-accent">127.0.0.1:{health.port || 8089}</span>
              </div>
              <div className="flex items-center justify-between">
                <span>Health:</span>
                <span className="font-mono text-content-main">
                  PID {health.pid || '--'} • {health.latencyMs}ms
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span>Active Sessions:</span>
                <span className="font-mono text-state-warning font-semibold">{health.activeSessions}</span>
              </div>
            </div>
          </div>
        </div>

      </div>
    </header>
  );
};
