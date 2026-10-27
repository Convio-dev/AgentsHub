import React, { useState } from 'react';
import {
  Menu,
  FolderOpen,
  GitBranch,
  Terminal,
  Bell,
  RefreshCw,
  Settings,
  Cpu,
  CheckCircle2,
} from 'lucide-react';
import { useSidecarStore } from '../../store/useSidecarStore';
import { useWorkspaceStore } from '../../store/useWorkspaceStore';

interface HeaderProps {
  onOpenSettings?: () => void;
  onOpenWorkspaceModal?: () => void;
}

export const Header: React.FC<HeaderProps> = ({ onOpenSettings, onOpenWorkspaceModal }) => {
  const {
    health,
    telemetry,
    toggleSidebar,
    refreshSidecarStatus,
    hydrateInitialState,
    setActiveView,
  } = useSidecarStore();

  const { activeWorkspace, branch } = useWorkspaceStore();
  const [isRefreshing, setIsRefreshing] = useState(false);

  const handleRefresh = async () => {
    setIsRefreshing(true);
    await refreshSidecarStatus();
    await hydrateInitialState();
    setTimeout(() => setIsRefreshing(false), 500);
  };

  return (
    <header
      id="top-header"
      className="h-10 bg-surface-deep border-b border-surface-border flex items-center justify-between px-3 text-xs text-content-muted z-30 select-none flex-shrink-0"
    >
      {/* 1. Left Group: Hamburger Toggle, Logo, Workspace/Branch Badge */}
      <div className="flex items-center space-x-3">
        <button
          id="btn-toggle-sidebar"
          onClick={toggleSidebar}
          className="w-7 h-7 flex items-center justify-center text-content-muted hover:text-content-main rounded-lg hover:bg-surface-card transition border border-transparent hover:border-surface-border"
          title="Toggle Secondary Sidebar"
        >
          <Menu className="w-4 h-4" />
        </button>

        <div className="flex items-center space-x-2 font-semibold text-content-main">
          <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 animate-pulse" />
          <span className="tracking-wide text-xs">AGENTS HUB</span>
          <span
            id="brand-version"
            className="px-1.5 py-0.5 text-[10px] bg-sky-500/10 text-accent border border-sky-500/30 rounded font-mono"
          >
            {health.version || '--'}
          </span>
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

      {/* 2. Center: ConPTY status. The PTY runs inside the sidecar, so this reports the sidecar's
          real connection state rather than a fixed "Active" label. */}
      <div className="hidden md:flex items-center space-x-2 text-xs font-mono text-content-muted">
        <span
          className={`w-2 h-2 rounded-full ${health.isOnline ? 'bg-emerald-500' : 'bg-rose-500'}`}
          aria-hidden="true"
        />
        <span>
          ConPTY:{' '}
          <strong className={health.isOnline ? 'text-state-success font-semibold' : 'text-state-danger font-semibold'}>
            {health.isOnline ? 'Active' : 'Offline'}
          </strong>
        </span>
      </div>

      {/* 3. Right: Sidecar Health Popover, Telemetry Pill, HUD Button, Bell, Refresh, Settings */}
      <div className="flex items-center space-x-2">
        {/* Bun Sidecar Pure Icon Indicator with Modern Hover Tooltip Popover */}
        <div id="sidecar-status-container" className="relative group/sidecar flex items-center">
          <button
            type="button"
            className="w-7 h-7 rounded-lg bg-surface-card border border-surface-border flex items-center justify-center transition-all duration-200 hover:border-sky-500/50 hover:bg-surface-card/80 focus:outline-none shadow-sm cursor-pointer"
            aria-label="Sidecar Engine Status"
          >
            <div className="relative flex items-center justify-center">
              {health.isOnline && (
                <span className="absolute inline-flex h-3 w-3 rounded-full bg-emerald-400 opacity-60 animate-ping" />
              )}
              <span
                className={`relative inline-flex rounded-full h-2 w-2 shadow-sm ${
                  health.isOnline ? 'bg-emerald-500 shadow-emerald-500/50' : 'bg-rose-500 shadow-rose-500/50'
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

        {/* Combined Compact Telemetry Pill */}
        <div className="flex items-center space-x-2 text-[11px] font-mono px-2.5 py-1 rounded-lg bg-surface-card border border-surface-border text-content-muted shadow-sm">
          <span className="text-accent font-semibold">
            RAM {telemetry.ramMb > 0 ? `${telemetry.ramMb} MB` : '--'}
          </span>
          <span className="opacity-30">|</span>
          <span className="text-state-success">NET {telemetry.netKbps > 0 ? `${telemetry.netKbps} KB/s` : '--'}</span>
        </div>

        {/* Quake HUD Toggle (F12) */}
        <button
          onClick={() => setActiveView('terminals')}
          className="flex items-center space-x-1.5 px-2.5 py-1 rounded-lg bg-surface-card border border-surface-border hover:border-sky-500 text-content-muted hover:text-content-main transition text-xs shadow-sm"
          title="Toggle HUD Terminal (F12)"
        >
          <Terminal className="w-3 h-3 text-accent" />
          <span>HUD</span>
          <kbd className="px-1 py-0.2 bg-surface-sunken rounded text-[9px] border border-surface-border font-mono text-content-dim">
            F12
          </kbd>
        </button>

        {/* Notifications Button */}
        <button
          className="relative w-7 h-7 flex items-center justify-center rounded-lg bg-surface-card border border-surface-border hover:border-sky-500 text-content-muted hover:text-content-main transition shadow-sm"
          title="Notifications"
        >
          <Bell className="w-3.5 h-3.5" />
        </button>

        {/* Refresh State Button */}
        <button
          onClick={handleRefresh}
          className="w-7 h-7 flex items-center justify-center rounded-lg bg-surface-card border border-surface-border hover:border-sky-500 text-content-muted hover:text-content-main transition shadow-sm"
          title="Refresh Backend Telemetry & State"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? 'animate-spin text-accent' : ''}`} />
        </button>

        {/* Workspace Settings Button */}
        {onOpenSettings && (
          <button
            onClick={onOpenSettings}
            className="w-7 h-7 flex items-center justify-center rounded-lg bg-surface-card border border-surface-border hover:border-sky-500 text-content-muted hover:text-content-main transition shadow-sm"
            title="Application Settings"
          >
            <Settings className="w-3.5 h-3.5" />
          </button>
        )}
      </div>
    </header>
  );
};
