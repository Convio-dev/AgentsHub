import React, { useState } from 'react';
import { Check, Copy, Pencil, RotateCcw, Square } from 'lucide-react';
import { copyToClipboard } from '../../lib/clipboard';

interface ActionButtonProps {
  label: string;
  onClick: () => void;
  children: React.ReactNode;
  active?: boolean;
}

const ActionButton: React.FC<ActionButtonProps> = ({ label, onClick, children, active = false }) => (
  <button
    type="button"
    onClick={onClick}
    title={label}
    aria-label={label}
    className={`flex h-6 w-6 items-center justify-center rounded text-content-dim transition-colors hover:bg-white/[0.06] hover:text-content-main focus-visible:outline focus-visible:outline-1 focus-visible:outline-sky-500/60 ${
      active ? 'text-emerald-400 hover:text-emerald-400' : ''
    }`}
  >
    {children}
  </button>
);

interface AssistantMessageActionsProps {
  markdown: string;
  onRegenerate: () => void;
  canRegenerate: boolean;
}

/** Copy and regenerate, revealed on hover once a reply has finished. */
export const AssistantMessageActions: React.FC<AssistantMessageActionsProps> = ({
  markdown,
  onRegenerate,
  canRegenerate,
}) => {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    const ok = await copyToClipboard(markdown);
    if (!ok) return;
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1600);
  };

  return (
    <div className="flex items-center gap-0.5">
      {markdown ? (
        <ActionButton label={copied ? 'Copied' : 'Copy response'} onClick={handleCopy} active={copied}>
          {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
        </ActionButton>
      ) : null}

      {canRegenerate ? (
        <ActionButton label="Regenerate response" onClick={onRegenerate}>
          <RotateCcw className="h-3.5 w-3.5" />
        </ActionButton>
      ) : null}
    </div>
  );
};

interface StopGeneratingButtonProps {
  onStop: () => void;
  label?: string;
}

/**
 * Stops the in-flight turn. Visible only while the engine is working, because that is the only time
 * there is something to stop.
 */
export const StopGeneratingButton: React.FC<StopGeneratingButtonProps> = ({ onStop, label = 'Stop' }) => (
  <button
    type="button"
    onClick={onStop}
    title="Stop generating (aborts the running task)"
    className="flex items-center gap-1.5 rounded-md border border-[var(--border-subtle)] bg-[var(--bg-card)] px-2 py-0.5 font-mono text-[10px] font-medium text-content-muted transition-colors hover:border-rose-500/40 hover:bg-rose-500/10 hover:text-rose-300 focus-visible:outline focus-visible:outline-1 focus-visible:outline-rose-500/60"
  >
    <Square className="h-2.5 w-2.5 fill-current" />
    <span>{label}</span>
  </button>
);

interface UserMessageActionsProps {
  onEdit: () => void;
}

/** Edit action for a user bubble; the pencil opens the prompt in place. */
export const UserMessageActions: React.FC<UserMessageActionsProps> = ({ onEdit }) => (
  <div className="flex items-center gap-0.5">
    <ActionButton label="Edit prompt" onClick={onEdit}>
      <Pencil className="h-3.5 w-3.5" />
    </ActionButton>
  </div>
);
