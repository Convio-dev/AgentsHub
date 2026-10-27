import React, { useState, useRef, useEffect } from 'react';
import { UserCheck, ChevronDown, Play, Square, Check, Plus } from 'lucide-react';
import { useProfileStore } from '../../store/useProfileStore';
import { Button } from '../common/Button';

interface RunnerProfileDropdownProps {
  onAddProfile?: () => void;
}

export const RunnerProfileDropdown: React.FC<RunnerProfileDropdownProps> = ({ onAddProfile }) => {
  const [isOpen, setIsOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const { profiles, activeProfileName, switchActiveProfile, startProfile, stopProfile } =
    useProfileStore();

  const activeProfile =
    profiles.find(p => p.name.toLowerCase() === activeProfileName.toLowerCase()) || profiles[0];

  const isRunning = activeProfile?.isRunning || false;

  // Close dropdown on click outside
  useEffect(() => {
    const handleOutsideClick = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };
    if (isOpen) {
      document.addEventListener('mousedown', handleOutsideClick);
    }
    return () => document.removeEventListener('mousedown', handleOutsideClick);
  }, [isOpen]);

  const handleToggleRun = (e: React.MouseEvent, profileName: string, running: boolean) => {
    e.stopPropagation();
    if (running) {
      stopProfile(profileName);
    } else {
      startProfile(profileName);
    }
  };

  return (
    <div ref={dropdownRef} className="relative inline-flex items-center select-none text-xs">
      <div className="flex items-center rounded-lg bg-surface-card border border-surface-border shadow-sm overflow-hidden p-0.5">
        {/* Profile Selector Trigger */}
        <button
          type="button"
          onClick={() => setIsOpen(!isOpen)}
          className="flex items-center space-x-2 px-2.5 py-1 hover:bg-surface-strong transition-colors font-mono"
        >
          <span
            className={`w-2 h-2 rounded-full ${
              isRunning ? 'bg-emerald-400 animate-pulse' : 'bg-content-dim'
            }`}
          />
          <span className="font-semibold text-content-main truncate max-w-[110px]">
            {activeProfile?.name || 'Default'}
          </span>
          <span className="text-[10.5px] text-content-muted hidden sm:inline">
            {isRunning ? '(Running)' : '(Offline)'}
          </span>
          <ChevronDown className="w-3 h-3 text-content-muted" />
        </button>

        {/* Start / Stop Quick Toggle Button */}
        <button
          type="button"
          onClick={e => handleToggleRun(e, activeProfile?.name || 'Default', isRunning)}
          className={`flex items-center gap-1 px-2.5 py-1 text-[11px] font-mono font-semibold transition-colors rounded-md ${
            isRunning
              ? 'bg-rose-500/15 text-state-danger hover:bg-rose-500/25 border border-rose-500/30'
              : 'bg-emerald-500/15 text-state-success hover:bg-emerald-500/25 border border-emerald-500/30'
          }`}
          title={isRunning ? 'Stop active agent profile' : 'Start agent profile'}
        >
          {isRunning ? (
            <>
              <Square className="w-2.5 h-2.5 fill-current" />
              <span>Stop</span>
            </>
          ) : (
            <>
              <Play className="w-2.5 h-2.5 fill-current" />
              <span>Start</span>
            </>
          )}
        </button>
      </div>

      {/* Dropdown Menu */}
      {isOpen && (
        <div className="absolute top-full left-0 mt-1.5 w-64 rounded-xl bg-surface-card border border-surface-border/80 shadow-2xl z-50 overflow-hidden font-sans animate-in fade-in zoom-in-95">
          <div className="p-2 border-b border-surface-border flex items-center justify-between text-content-muted text-[11px] font-mono uppercase">
            <span>Runner Profiles</span>
            <span className="text-[10px] text-content-dim">{profiles.length} Available</span>
          </div>

          <div className="max-h-56 overflow-y-auto p-1 space-y-0.5">
            {profiles.map(p => {
              const isSelected = p.name.toLowerCase() === activeProfileName.toLowerCase();
              return (
                <div
                  key={p.name}
                  onClick={() => {
                    switchActiveProfile(p.name);
                    setIsOpen(false);
                  }}
                  className={`flex items-center justify-between p-2 rounded-lg cursor-pointer transition-colors ${
                    isSelected ? 'bg-surface-strong text-content-main' : 'text-content-muted hover:bg-surface-hover'
                  }`}
                >
                  <div className="flex items-center space-x-2 min-w-0">
                    <span
                      className={`w-2 h-2 rounded-full flex-shrink-0 ${
                        p.isRunning ? 'bg-emerald-400 animate-pulse' : 'bg-content-dim'
                      }`}
                    />
                    <div className="flex flex-col min-w-0">
                      <span className="font-semibold text-xs truncate">{p.name}</span>
                      {p.email && <span className="text-[10.5px] text-content-muted truncate">{p.email}</span>}
                    </div>
                  </div>

                  <div className="flex items-center space-x-1.5 flex-shrink-0">
                    <button
                      type="button"
                      onClick={e => handleToggleRun(e, p.name, p.isRunning)}
                      className={`p-1 rounded text-[10px] ${
                        p.isRunning
                          ? 'text-state-danger hover:bg-rose-500/20'
                          : 'text-state-success hover:bg-emerald-500/20'
                      }`}
                      title={p.isRunning ? 'Stop runner' : 'Start runner'}
                    >
                      {p.isRunning ? (
                        <Square className="w-3 h-3 fill-current" />
                      ) : (
                        <Play className="w-3 h-3 fill-current" />
                      )}
                    </button>
                    {isSelected && <Check className="w-3.5 h-3.5 text-accent" />}
                  </div>
                </div>
              );
            })}
          </div>

          {onAddProfile && (
            <div className="p-1.5 border-t border-surface-border bg-surface-code/40">
              <button
                type="button"
                onClick={() => {
                  setIsOpen(false);
                  onAddProfile();
                }}
                className="w-full flex items-center justify-center gap-1.5 p-1.5 rounded-lg text-xs font-mono text-accent hover:bg-sky-500/10 transition"
              >
                <Plus className="w-3.5 h-3.5" />
                <span>Add Runner Account</span>
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
};
