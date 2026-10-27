import React, { useEffect, useRef } from 'react';
import { ChatMessage } from '../../types/agent';
import { MarkdownRenderer } from './MarkdownRenderer';
import { AgentTimeline } from './timeline/AgentTimeline';
import { Bot, User, AlertCircle, Copy, Check } from 'lucide-react';
import appMascot from '../../assets/app.png';

interface ChatMessageListProps {
  messages: ChatMessage[];
  activeExecutionCardId?: string;
}

export const ChatMessageList: React.FC<ChatMessageListProps> = ({ messages, activeExecutionCardId }) => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [copiedId, setCopiedId] = React.useState<string | null>(null);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages]);

  const copyContent = (id: string, text?: string) => {
    if (text) {
      navigator.clipboard.writeText(text);
      setCopiedId(id);
      setTimeout(() => setCopiedId(null), 2000);
    }
  };

  if (!messages || messages.length === 0) {
    return (
      <div className="flex-1 flex flex-col items-center justify-center p-8 text-center select-none">
        <div className="w-16 h-16 rounded-2xl bg-slate-900 border border-slate-800 flex items-center justify-center p-2 mb-4 shadow-xl">
          <img src={appMascot} alt="Agents Hub Mascot" className="w-full h-full object-contain" />
        </div>
        <h3 className="text-base font-semibold text-slate-100 mb-1">Agents Hub Workspace</h3>
        <p className="text-xs text-slate-400 max-w-sm mb-6 leading-relaxed">
          Send a prompt below to launch tasks, execute CLI operations, edit code, and inspect files with real-time streaming timelines.
        </p>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 max-w-md w-full text-left font-mono text-[11.5px]">
          <div className="p-2.5 rounded-lg border border-slate-800/80 bg-slate-900/40 text-slate-300 hover:border-slate-700 transition">
            <span className="text-sky-400 font-semibold block mb-0.5">Explore codebase</span>
            <span className="text-slate-500 text-[10.5px]">Analyze dependencies and project structure</span>
          </div>
          <div className="p-2.5 rounded-lg border border-slate-800/80 bg-slate-900/40 text-slate-300 hover:border-slate-700 transition">
            <span className="text-sky-400 font-semibold block mb-0.5">Run CLI build</span>
            <span className="text-slate-500 text-[10.5px]">Compile solution and test distribution</span>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div ref={scrollRef} className="flex-1 overflow-y-auto px-4 py-4 space-y-5">
      {messages.map(msg => {
        const isUser = msg.sender === 'user';
        const isRunning = msg.status === 'running' || msg.status === 'thinking' || msg.status === 'tool_executing';

        if (isUser) {
          return (
            <div key={msg.id} className="flex justify-end">
              <div className="max-w-[85%] rounded-2xl rounded-tr-sm bg-slate-800 border border-slate-700/60 p-3.5 text-slate-100 shadow-sm text-sm space-y-2.5">
                <div className="flex items-center space-x-2 text-[11px] font-mono text-slate-400 select-none">
                  <User className="w-3.5 h-3.5 text-sky-400" />
                  <span className="font-semibold text-slate-300">You</span>
                </div>

                {/* Attached files chips */}
                {msg.attachments && msg.attachments.length > 0 && (
                  <div className="flex flex-wrap gap-2 pt-0.5">
                    {msg.attachments.map(att => (
                      <div
                        key={att.id}
                        className="flex items-center gap-2 p-1.5 pr-2.5 rounded-lg bg-slate-900/80 border border-slate-700/80 text-xs"
                      >
                        {att.isImage && att.dataUrl ? (
                          <a
                            href={att.dataUrl}
                            target="_blank"
                            rel="noreferrer"
                            className="block flex-shrink-0"
                            title="Click to view full image"
                          >
                            <img
                              src={att.dataUrl}
                              alt={att.name}
                              className="w-8 h-8 rounded object-cover border border-slate-600/50 hover:opacity-90 transition"
                            />
                          </a>
                        ) : (
                          <div className="w-8 h-8 rounded bg-sky-500/15 border border-sky-500/30 flex items-center justify-center flex-shrink-0 text-sky-400">
                            <span className="text-[10px] font-mono font-bold uppercase">
                              {att.name.split('.').pop()?.slice(0, 3) || 'FILE'}
                            </span>
                          </div>
                        )}
                        <div className="min-w-0 max-w-[160px]">
                          <div className="text-[11.5px] font-medium text-slate-200 truncate" title={att.name}>
                            {att.name}
                          </div>
                          <div className="text-[9.5px] font-mono text-slate-400">
                            {att.size < 1024 ? `${att.size} B` : `${(att.size / 1024).toFixed(1)} KB`}
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}

                <div className="whitespace-pre-wrap leading-relaxed select-text font-sans text-[13.5px]">
                  {msg.prompt}
                </div>
              </div>
            </div>
          );
        }

        // Assistant card
        return (
          <div key={msg.id} className="flex justify-start">
            <div className="w-full max-w-full rounded-2xl rounded-tl-sm bg-slate-900/70 border border-slate-800 p-4 text-slate-200 shadow-md">
              {/* Header */}
              <div className="flex items-center justify-between pb-2.5 mb-3 border-b border-slate-800/80 select-none text-xs">
                <div className="flex items-center space-x-2">
                  <div className="w-5 h-5 rounded-md overflow-hidden bg-slate-800 border border-slate-700 flex items-center justify-center p-0.5">
                    <img src={appMascot} alt="Mascot" className="w-full h-full object-contain" />
                  </div>
                  <span className="font-semibold text-slate-100">Agent</span>
                  {msg.profile && (
                    <span className="px-1.5 py-0.5 rounded text-[10px] font-mono bg-slate-800 text-sky-400 border border-slate-700/60">
                      {msg.profile}
                    </span>
                  )}
                </div>

                <div className="flex items-center space-x-2">
                  {/* Status Badge */}
                  {isRunning ? (
                    <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-[10px] font-semibold bg-sky-500/10 text-sky-400 border border-sky-500/25">
                      <span className="w-1.5 h-1.5 rounded-full bg-sky-400 animate-ping" />
                      <span>
                        {msg.status === 'thinking'
                          ? 'Thinking...'
                          : msg.status === 'tool_executing'
                          ? 'Executing Tool'
                          : msg.markdown && msg.markdown.length > 0
                          ? 'Streaming'
                          : 'Processing...'}
                      </span>
                    </span>
                  ) : msg.status === 'error' ? (
                    <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-semibold bg-rose-500/10 text-rose-400 border border-rose-500/25">
                      <AlertCircle className="w-3 h-3" />
                      <span>Failed</span>
                    </span>
                  ) : (
                    <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/25">
                      Completed
                    </span>
                  )}

                  {msg.markdown && (
                    <button
                      onClick={() => copyContent(msg.id, msg.markdown)}
                      className="p-1 rounded text-slate-400 hover:text-slate-200 hover:bg-slate-800 transition"
                      title="Copy response"
                    >
                      {copiedId === msg.id ? (
                        <Check className="w-3.5 h-3.5 text-emerald-400" />
                      ) : (
                        <Copy className="w-3.5 h-3.5" />
                      )}
                    </button>
                  )}
                </div>
              </div>

              {/* Connected Execution Stepper Timeline */}
              {msg.timeline && msg.timeline.length > 0 && (
                <div className="mb-3">
                  <AgentTimeline nodes={msg.timeline} isLive={isRunning} />
                </div>
              )}

              {/* Streaming Markdown prose with clear visual styling */}
              {msg.markdown ? (
                <div className={`select-text ${msg.timeline && msg.timeline.length > 0 ? 'mt-3 pt-3 border-t border-slate-800/70' : 'mt-1'}`}>
                  <MarkdownRenderer content={msg.markdown} />
                </div>
              ) : isRunning && (!msg.timeline || msg.timeline.length === 0) ? (
                <div className="flex items-center space-x-2 text-xs text-slate-400 italic py-2">
                  <span className="w-1.5 h-1.5 rounded-full bg-sky-400 animate-pulse" />
                  <span>Preparing agent execution pipeline...</span>
                </div>
              ) : null}

              {/* Error display if present */}
              {msg.error && (
                <div className="mt-3 p-2.5 rounded-lg border border-rose-500/30 bg-rose-500/10 text-rose-300 text-xs font-mono">
                  {msg.error}
                </div>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
};
