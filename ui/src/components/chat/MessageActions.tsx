import React, { useState } from 'react';
import { Check, Copy, Pencil, RotateCcw } from 'lucide-react';
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
    className={`flex h-6 w-6 items-center justify-center rounded text-content-dim transition-colors hover:bg-surface-strong hover:text-content-main focus-visible:outline focus-visible:outline-1 focus-visible:outline-sky-500/60 ${
      active ? 'text-state-success hover:text-state-success' : ''
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
