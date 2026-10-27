import React, { useState } from 'react';
import {
  MessageSquare,
  ChevronDown,
  Users,
  Terminal,
  Plus,
  X,
  Loader2,
} from 'lucide-react';
import { useSessionStore } from '../store/useSessionStore';
import { useProfileStore } from '../store/useProfileStore';
import { useSidecarStore } from '../store/useSidecarStore';
import { useWorkspaceStore } from '../store/useWorkspaceStore';
import { sendPrompt, stopGeneration } from '../services/chatController';
import { ChatMessageList } from '../components/chat/ChatMessageList';
import { ChatInput } from '../components/chat/ChatInput';
import { ChatAttachment } from '../types/agent';
import { profileLabel } from '../lib/profileName';

interface AgentChatViewProps {
  onOpenGitModal: () => void;
}

export const AgentChatView: React.FC<AgentChatViewProps> = ({ onOpenGitModal }) => {
  const {
    sessions,
    activeSessionId,
    getActiveSession,
    createSession,
    switchSession,
    deleteSession,
  } = useSessionStore();

  const {
    profiles,
    activeProfileName,
    defaultModel,
    switchActiveProfile,
    startProfile,
    stopProfile,
  } = useProfileStore();

  const { setActiveView } = useSidecarStore();
  const { activeWorkspace } = useWorkspaceStore();

  const [isProfilePopoverOpen, setIsProfilePopoverOpen] = useState(false);

  const activeSession = getActiveSession();
  const activeProfile = profiles.find(
    p => p.name.toLowerCase() === activeProfileName.toLowerCase()
  ) || profiles[0];

  const isExecuting =
    activeSession?.status === 'running' ||
    activeSession?.status === 'thinking' ||
    activeSession?.status === 'tool_executing';

  const handleSendMessage = async (prompt: string, attachments?: ChatAttachment[]) => {
    if (!activeSession || isExecuting) return;
    await sendPrompt(activeSession.id, prompt, attachments);
  };

  const handleStopGenerating = () => {
    if (!activeSession) return;
    void stopGeneration(activeSession.id);
  };

  const handleNewTab = () => {
    const newId = createSession(activeProfileName, defaultModel);
    switchSession(newId);
  };

  const openTabs = Object.values(sessions);

  return (
    <div className="flex-1 flex flex-col h-full overflow-hidden bg-surface-deep text-content-main select-none relative">
      {/* 1. Top Context Bar */}
      <div className="h-12 border-b border-surface-border px-4 flex items-center justify-between bg-surface-deep z-20 flex-shrink-0 relative">
        <div className="flex items-center space-x-3">
          {/* Active Session Label */}
          <div className="flex items-center space-x-2">
            <MessageSquare className="w-4 h-4 text-accent" />
            <span className="text-xs font-semibold text-content-main">
              {activeSession?.title || 'Active Agent Session'}
            </span>
          </div>

          <span className="opacity-30">|</span>

          {/* Runner Dropdown with Multi-Profile Popover */}
          <div className="relative">
            <button
              onClick={() => setIsProfilePopoverOpen(!isProfilePopoverOpen)}
              className="text-xs px-2.5 py-1 rounded-lg bg-surface-card border border-surface-border hover:border-sky-500 text-content-main font-mono flex items-center space-x-2 transition shadow-sm"
              title="Manage Antigravity Profiles & Multi-Agent Execution"
            >
              <span
                className={`w-2 h-2 rounded-full ${
                  activeProfile?.isRunning ? 'bg-emerald-400 animate-pulse' : 'bg-slate-500'
                }`}
              />
              <span>Runner: {profileLabel(activeProfileName)}</span>
              <ChevronDown className="w-3 h-3 text-content-dim ml-1" />
            </button>

            {/* Multi-Profile Popover Panel */}
            {isProfilePopoverOpen && (
              <div className="absolute left-0 top-10 w-80 bg-surface-card border border-surface-border rounded-xl shadow-2xl p-3.5 z-50 space-y-3 font-sans">
                <div className="flex items-center justify-between pb-2 border-b border-surface-border">
                  <span className="text-xs font-semibold text-content-main flex items-center gap-1.5">
                    <Users className="w-3.5 h-3.5 text-accent" />
                    <span>Multi-Profile Agents</span>
                  </span>
                  <span className="text-[10px] font-mono text-content-dim">
                    {profiles.filter(p => p.active).length} Selected
                  </span>
                </div>

                <p className="text-[11px] text-content-muted leading-relaxed">
                  Select one or multiple profiles to run sessions in parallel. Start or stop instances directly.
                </p>

                {/* Profiles List */}
                <div className="space-y-1.5 max-h-56 overflow-y-auto pr-1">
                  {profiles.map(p => {
                    const isSelected = p.name.toLowerCase() === activeProfileName.toLowerCase();
                    return (
                      <div
                        key={p.name}
                        className={`flex items-center justify-between p-2 rounded-lg border text-xs transition ${
                          isSelected
                            ? 'bg-surface-deep/80 border-sky-500/40'
                            : 'bg-surface-deep/40 border-surface-border/60 hover:border-surface-border'
                        }`}
                      >
                        <div
                          className="flex items-center space-x-2 cursor-pointer flex-1"
                          onClick={() => switchActiveProfile(p.name)}
                        >
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => switchActiveProfile(p.name)}
                            className="rounded accent-sky-500 cursor-pointer"
                          />
                          <span
                            className={`w-2 h-2 rounded-full ${
                              p.isRunning ? 'bg-emerald-400 animate-pulse' : 'bg-slate-500'
                            }`}
                          />
                          <span className="font-mono text-xs text-content-main">{p.name}</span>
                        </div>

                        <div className="flex items-center space-x-2">
                          <span
                            className={`px-1.5 py-0.5 text-[9px] font-mono rounded border ${
                              p.isRunning
                                ? 'bg-emerald-500/15 text-state-success border-emerald-500/30'
                                : 'bg-slate-800 text-content-muted border-surface-border'
                            }`}
                          >
                            {p.isRunning ? 'Running' : 'Offline'}
                          </span>

                          <button
                            type="button"
                            onClick={() => (p.isRunning ? stopProfile(p.name) : startProfile(p.name))}
                            className={`px-2 py-0.5 rounded text-[10px] font-mono font-medium transition ${
                              p.isRunning
                                ? 'bg-rose-500/15 text-state-danger border border-rose-500/30 hover:bg-rose-500/25'
                                : 'bg-emerald-500/15 text-state-success border border-emerald-500/30 hover:bg-emerald-500/25'
                            }`}
                          >
                            {p.isRunning ? 'Stop' : 'Start'}
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>

                <div className="pt-2 border-t border-surface-border flex items-center justify-between text-xs">
                  <button
                    onClick={() => {
                      setIsProfilePopoverOpen(false);
                      setActiveView('profiles');
                    }}
                    className="text-[11px] text-accent hover:underline"
                  >
                    Full Profiles Manager &rarr;
                  </button>
                  <button
                    onClick={() => setIsProfilePopoverOpen(false)}
                    className="text-[11px] px-2.5 py-1 rounded-md bg-surface-deep border border-surface-border text-content-main hover:border-sky-500 transition"
                  >
                    Done
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>

        {/* Right Controls: Review Diff, Terminals. Model, effort, quota and tokens live in the
            composer footer, where they belong to the prompt rather than to the window chrome. */}
        <div className="flex items-center space-x-2">
          {/* Terminal View Trigger */}
          <button
            onClick={() => setActiveView('terminals')}
            title="Open Interactive Terminals"
            className="px-2.5 py-1 rounded-lg bg-surface-card border border-surface-border hover:border-sky-500 text-xs text-content-muted hover:text-content-main flex items-center space-x-1.5 transition shadow-sm"
          >
            <Terminal className="w-3 h-3 text-accent" />
            <span>Terminals</span>
          </button>
        </div>
      </div>

      {/* 2. Multi-Session Agent Tabs Bar */}
      <div className="h-9 border-b border-surface-border bg-surface-deep/95 px-4 flex items-center justify-between text-xs overflow-x-auto select-none z-10 flex-shrink-0">
        <div className="flex items-center space-x-1 overflow-x-auto scrollbar-none flex-1 py-1">
          {openTabs.map(tab => {
            const isActive = tab.id === activeSessionId;
            const isTabRunning =
              tab.status === 'running' ||
              tab.status === 'thinking' ||
              tab.status === 'tool_executing';

            return (
              <div
                key={tab.id}
                onClick={() => switchSession(tab.id)}
                className={`group flex items-center space-x-1.5 px-3 py-1 rounded-t-lg text-xs transition cursor-pointer select-none max-w-[200px] border-t border-x ${
                  isActive
                    ? 'bg-surface-card border-surface-border text-accent font-semibold shadow-sm'
                    : 'bg-surface-deep/60 border-transparent text-content-muted hover:text-content-main hover:bg-surface-card/40'
                }`}
              >
                {isTabRunning ? (
                  <Loader2 className="w-3 h-3 animate-spin text-accent flex-shrink-0" />
                ) : (
                  <span
                    className={`w-1.5 h-1.5 rounded-full flex-shrink-0 ${
                      isActive ? 'bg-sky-400' : 'bg-content-dim'
                    }`}
                  />
                )}
                <span className="truncate text-[11px] font-mono">{tab.title}</span>
                {openTabs.length > 1 && (
                  <button
                    type="button"
                    onClick={e => {
                      e.stopPropagation();
                      deleteSession(tab.id);
                    }}
                    className="opacity-0 group-hover:opacity-100 hover:text-state-danger transition ml-1 p-0.5 text-[10px]"
                    title="Close tab"
                  >
                    <X className="w-3 h-3" />
                  </button>
                )}
              </div>
            );
          })}
        </div>

        <button
          type="button"
          onClick={handleNewTab}
          title="Open New Agent Tab"
          className="ml-2 px-2.5 py-1 rounded-md bg-surface-card border border-surface-border hover:border-sky-500 text-content-muted hover:text-accent transition text-[11px] flex items-center gap-1 flex-shrink-0 shadow-sm"
        >
          <Plus className="w-3 h-3" />
          <span>New Tab</span>
        </button>
      </div>

      {/* 3. Messages List with Claude-Code Connected Stepper */}
      <div className="flex-1 flex flex-col overflow-hidden relative">
        {activeSession ? (
          <ChatMessageList
            messages={activeSession.messages}
            activeExecutionCardId={activeSession.activeExecutionCardId}
            isGenerating={isExecuting}
          />
        ) : (
          <div className="flex-1 flex items-center justify-center text-content-dim font-mono text-xs">
            No active session selected.
          </div>
        )}

        {/* 4. Bottom Floating Input Dock */}
        <ChatInput
          sessionId={activeSession?.id || ''}
          profileName={activeSession?.profile || activeProfileName}
          onSendMessage={handleSendMessage}
          onStop={handleStopGenerating}
          isExecuting={isExecuting}
          activeModel={activeSession?.model || defaultModel || undefined}
          reasoningEffort={activeSession?.reasoningEffort}
          lastTurnTokens={activeSession?.lastTurnTokens}
          sessionTokens={activeSession?.tokens}
          onOpenGitModal={onOpenGitModal}
          onOpenTerminal={() => setActiveView('terminals')}
          activeCli={activeWorkspace?.primaryCli}
          workingDirectory={activeWorkspace?.workingDirectory}
        />
      </div>
    </div>
  );
};
