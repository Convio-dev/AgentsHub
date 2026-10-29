import React, { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { ImageLightbox } from './ImageLightbox';
import { Paperclip } from 'lucide-react';
import { AlertCircle, MessageSquare, User } from 'lucide-react';
import { ChatMessage } from '../../types/agent';
import { MarkdownRenderer } from './MarkdownRenderer';
import { AgentTimeline } from './timeline/AgentTimeline';
import { AssistantMessageActions, UserMessageActions } from './MessageActions';
import { profileLabel } from '../../lib/profileName';
import { editAndResend, regenerateResponse, stopGeneration } from '../../services/chatController';
import appMascot from '../../assets/app.png';

interface ChatMessageListProps {
  messages: ChatMessage[];
  activeExecutionCardId?: string;
  /** True while the active session has a turn in flight. */
  isGenerating?: boolean;
}

const ACTIVE_STATES = new Set(['running', 'thinking', 'tool_executing']);

function statusLabel(message: ChatMessage): string {
  if (message.status === 'thinking') return 'Thinking';
  if (message.status === 'tool_executing') return 'Executing tools';
  return message.markdown ? 'Streaming' : 'Working';
}

/**
 * Renders the `[Attached: name (size)]` lines the prompt carries as chips.
 *
 * The text has to stay in the prompt: it is the only hint the CLI engine gets, since attachments are
 * not handed to it as files. Rendering it as a pill keeps that hint without the bracket noise.
 */
function renderAttachmentLine(prompt?: string): React.ReactNode {
  if (!prompt) return null;

  const pattern = /\[Attached: ([^\]]+)\]/g;
  if (!pattern.test(prompt)) return prompt;

  const parts: React.ReactNode[] = [];
  let cursor = 0;
  let match: RegExpExecArray | null;
  pattern.lastIndex = 0;

  while ((match = pattern.exec(prompt)) !== null) {
    if (match.index > cursor) parts.push(prompt.slice(cursor, match.index));

    const [name, size] = match[1].split(/\s+\(/);
    parts.push(
      <span
        key={`${match.index}-${match[1]}`}
        className="mx-0.5 inline-flex max-w-[220px] items-center gap-1 rounded-md border border-surface-border bg-surface-deep px-1.5 py-0.5 align-middle font-mono text-[10.5px] text-content-muted"
        title={match[1]}
      >
        <Paperclip className="h-3 w-3 flex-shrink-0 text-accent" />
        <span className="truncate">{name}</span>
        {size ? <span className="flex-shrink-0 text-content-dim">{size.replace('(', '').replace(')', '')}</span> : null}
      </span>
    );

    cursor = match.index + match[0].length;
  }

  if (cursor < prompt.length) parts.push(prompt.slice(cursor));
  return parts;
}

export const ChatMessageList: React.FC<ChatMessageListProps> = ({ messages, isGenerating = false }) => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const pinnedToBottom = useRef(true);

  const [editingId, setEditingId] = useState<string | null>(null);
  /** Data URL of the attachment being previewed, if any. */
  const [preview, setPreview] = useState<string | null>(null);
  const [draft, setDraft] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const editRef = useRef<HTMLTextAreaElement>(null);

  const handleScroll = () => {
    const node = scrollRef.current;
    if (!node) return;
    const distance = node.scrollHeight - node.scrollTop - node.clientHeight;
    pinnedToBottom.current = distance < 80;
  };

  // Follow the stream only while the reader is at the bottom; scrolling up to read a step should not
  // be yanked away by the next token.
  useEffect(() => {
    const node = scrollRef.current;
    if (!node || !pinnedToBottom.current) return;
    node.scrollTop = node.scrollHeight;
  }, [messages]);

  useEffect(() => {
    if (editingId && editRef.current) {
      editRef.current.focus();
      editRef.current.setSelectionRange(draft.length, draft.length);
      editRef.current.style.height = 'auto';
      editRef.current.style.height = `${Math.min(editRef.current.scrollHeight, 220)}px`;
    }
  }, [editingId, draft.length]);

  const beginEdit = (message: ChatMessage) => {
    setEditingId(message.id);
    setDraft(message.prompt || '');
  };

  const cancelEdit = () => {
    setEditingId(null);
    setDraft('');
  };

  const submitEdit = async (message: ChatMessage) => {
    const text = draft.trim();
    if (!text || isSaving) return;

    setIsSaving(true);
    try {
      await editAndResend(message.sessionId, message.id, text);
      setEditingId(null);
      setDraft('');
      pinnedToBottom.current = true;
    } finally {
      setIsSaving(false);
    }
  };

  const handleEditKeyDown = (event: React.KeyboardEvent<HTMLTextAreaElement>, message: ChatMessage) => {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      void submitEdit(message);
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      cancelEdit();
    }
  };

  if (!messages || messages.length === 0) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center p-8 text-center select-none">
        <div className="mb-4 flex h-16 w-16 items-center justify-center rounded-2xl border border-surface-border bg-surface-card p-2">
          <img src={appMascot} alt="" className="h-full w-full object-contain" />
        </div>
        <h3 className="mb-1 text-base font-semibold text-content-main">Agents Hub Workspace</h3>
        <p className="mb-6 max-w-sm text-xs leading-relaxed text-content-muted">
          Send a prompt to run the agent in this workspace. Steps stream into a timeline above the
          reply, and every command can be expanded to its full output.
        </p>
        <div className="grid w-full max-w-md grid-cols-1 gap-2 text-left font-mono text-[11px] sm:grid-cols-2">
          <div className="rounded-lg border border-surface-border/70 bg-surface-card/40 p-2.5 text-content-muted">
            <span className="mb-0.5 block font-semibold text-accent">/help</span>
            <span className="text-[10.5px] text-content-dim">List the active engine's commands</span>
          </div>
          <div className="rounded-lg border border-surface-border/70 bg-surface-card/40 p-2.5 text-content-muted">
            <span className="mb-0.5 block font-semibold text-accent">/exec</span>
            <span className="text-[10.5px] text-content-dim">Run a shell command as a tool step</span>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div ref={scrollRef} onScroll={handleScroll} className="flex-1 space-y-5 overflow-y-auto px-4 py-4">
      {messages.map(message => {
        const isUser = message.sender === 'user';
        const isActive = ACTIVE_STATES.has(message.status);
        const isEditing = editingId === message.id;

        if (isUser) {
          return (
            <div key={message.id} className="group flex justify-end">
              {/* Filled, not outlined: an overlay tint reads as a bubble in both themes, where a
                  white card on the near-white canvas would have no edge at all. */}
              <div className="max-w-[85%] space-y-2.5 rounded-2xl rounded-tr-sm bg-surface-strong px-3.5 py-2.5 text-sm text-content-main">
                <div className="flex items-center gap-2 font-mono text-[11px] text-content-dim">
                  <User className="h-3.5 w-3.5 text-accent" />
                  <span className="font-semibold text-content-muted">You</span>
                  {message.forkedFrom ? (
                    <span className="rounded bg-surface-strong px-1.5 py-0.5 text-[9.5px] text-content-dim">
                      edited
                    </span>
                  ) : null}
                  <span className="ml-auto opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                    {!isEditing && !isGenerating ? (
                      <UserMessageActions onEdit={() => beginEdit(message)} />
                    ) : null}
                  </span>
                </div>

                {message.attachments && message.attachments.length > 0 ? (
                  <div className="flex flex-wrap gap-2 pt-0.5">
                    {message.attachments.map(att => (
                      <div
                        key={att.id}
                        className="flex items-center gap-2 rounded-lg border border-surface-border/70 bg-surface-deep/80 p-1.5 pr-2.5 text-xs"
                      >
                        {att.isImage && att.dataUrl ? (
                          <button
                            type="button"
                            onClick={() => setPreview(att.dataUrl || null)}
                            className="block flex-shrink-0 cursor-zoom-in"
                            title="Open the image"
                          >
                            <img
                              src={att.dataUrl}
                              alt={att.name}
                              className="h-8 w-8 rounded border border-surface-border object-cover"
                            />
                          </button>
                        ) : (
                          <div className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded border border-sky-500/25 bg-sky-500/10 font-mono text-[10px] font-bold uppercase text-accent">
                            {att.name.split('.').pop()?.slice(0, 3) || 'file'}
                          </div>
                        )}
                        <div className="max-w-[160px] min-w-0">
                          <div className="truncate text-[11.5px] font-medium text-content-main" title={att.name}>
                            {att.name}
                          </div>
                          <div className="font-mono text-[9.5px] text-content-dim">
                            {att.size < 1024 ? `${att.size} B` : `${(att.size / 1024).toFixed(1)} KB`}
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : null}

                {isEditing ? (
                  <div className="space-y-2">
                    <textarea
                      ref={editRef}
                      value={draft}
                      onChange={event => setDraft(event.target.value)}
                      onKeyDown={event => handleEditKeyDown(event, message)}
                      rows={2}
                      className="w-full resize-none rounded-lg border border-sky-500/40 bg-surface-deep px-2.5 py-2 font-sans text-[13px] leading-relaxed text-content-main focus:border-sky-500 focus:outline-none"
                    />
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-mono text-[10px] text-content-dim">
                        Sends as a new branch from here
                      </span>
                      <div className="flex items-center gap-1.5">
                        <button
                          type="button"
                          onClick={cancelEdit}
                          className="rounded-md px-2 py-1 text-[11px] text-content-muted transition-colors hover:bg-surface-strong hover:text-content-main"
                        >
                          Cancel
                        </button>
                        <button
                          type="button"
                          onClick={() => void submitEdit(message)}
                          disabled={!draft.trim() || isSaving}
                          className="rounded-md bg-sky-600 px-2.5 py-1 text-[11px] font-medium text-white transition-colors hover:bg-sky-500 disabled:cursor-not-allowed disabled:bg-sky-600/40"
                        >
                          {isSaving ? 'Sending…' : 'Send'}
                        </button>
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="select-text whitespace-pre-wrap font-sans text-[13.5px] leading-relaxed">
                    {renderAttachmentLine(message.prompt)}
                  </div>
                )}
              </div>
            </div>
          );
        }

        return (
          <div key={message.id} className="group flex justify-start">
            <div className="w-full text-content-main">
              <div className="mb-1.5 flex items-center justify-between text-xs">
                <div className="flex items-center gap-2">
                  <img src={appMascot} alt="" className="h-4 w-4 flex-shrink-0 object-contain" />
                  <span className="font-semibold text-content-main">Agent</span>
                  <span
                    className="rounded bg-surface-strong/60 px-1.5 py-0.5 font-mono text-[10px] text-accent"
                    title={message.profile ? `Runner profile: ${message.profile}` : 'No runner profile configured'}
                  >
                    {profileLabel(message.profile)}
                  </span>
                  {message.turn && message.turn > 1 ? (
                    <span className="font-mono text-[10px] text-content-dim">turn {message.turn}</span>
                  ) : null}
                </div>

                <div className="flex items-center gap-2">
                  {isActive ? (
                    <span className="inline-flex items-center gap-1.5 font-mono text-[10px] text-accent">
                      <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-sky-400" />
                      <span>{statusLabel(message)}</span>
                    </span>
                  ) : message.status === 'error' ? (
                    <span className="inline-flex items-center gap-1 font-mono text-[10px] text-state-danger">
                      <AlertCircle className="h-3 w-3" />
                      <span>Failed</span>
                    </span>
                  ) : message.status === 'aborted' ? (
                    <span className="inline-flex items-center gap-1 font-mono text-[10px] text-state-warning/90">
                      <MessageSquare className="h-3 w-3" />
                      <span>Stopped</span>
                    </span>
                  ) : null}

                  {!isActive ? (
                    <span className="opacity-0 transition-opacity group-hover:opacity-100 focus-within:opacity-100">
                      <AssistantMessageActions
                        markdown={message.markdown || ''}
                        canRegenerate={!isGenerating}
                        onRegenerate={() => void regenerateResponse(message.sessionId, message.id)}
                      />
                    </span>
                  ) : null}
                </div>
              </div>

              {message.timeline && message.timeline.length > 0 ? (
                <AgentTimeline nodes={message.timeline} isLive={isActive} />
              ) : null}

              {message.markdown ? (
                <div className={message.timeline?.length ? 'mt-2' : 'mt-0.5'}>
                  <MarkdownRenderer content={message.markdown} />
                </div>
              ) : isActive && (!message.timeline || message.timeline.length === 0) ? (
                // Only before the first step lands. Once a tool starts, the stepper above is the
                // progress indicator and this banner would be stale.
                <div className="flex items-center gap-2 py-2 font-mono text-[11px] text-content-dim">
                  <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-sky-400" />
                  <span>Waiting for the first token…</span>
                </div>
              ) : null}

              {message.error ? (
                <div className="mt-3 rounded-lg border border-rose-500/30 bg-rose-500/10 p-2.5 font-mono text-[11px] text-state-danger">
                  {message.error}
                </div>
              ) : null}
            </div>
          </div>
        );
      })}
      {preview
        ? createPortal(<ImageLightbox src={preview} onClose={() => setPreview(null)} />, document.body)
        : null}

    </div>
  );
};
