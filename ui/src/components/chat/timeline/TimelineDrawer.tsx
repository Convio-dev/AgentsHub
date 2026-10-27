import React, { useState } from 'react';
import { Check, Copy } from 'lucide-react';
import { copyToClipboard } from '../../../lib/clipboard';

export interface DrawerSection {
  label: string;
  text: string;
  tone?: 'default' | 'danger';
}

interface TimelineDrawerProps {
  sections: DrawerSection[];
  /** Text placed on the clipboard by the drawer's copy action. Defaults to every section. */
  copyText?: string;
  emptyHint?: string;
  maxHeightClass?: string;
}

/**
 * Inline code drawer behind a stepper row: full command, parameters, stdout and stderr as readable
 * text. This is where verbatim payloads belong — the collapsed row above never shows them.
 */
export const TimelineDrawer: React.FC<TimelineDrawerProps> = ({
  sections,
  copyText,
  emptyHint = 'Nothing was captured for this step.',
  maxHeightClass = 'max-h-60',
}) => {
  const [copied, setCopied] = useState(false);

  const visible = sections.filter(section => section.text.trim().length > 0);
  const payload = copyText ?? visible.map(section => section.text).join('\n\n');

  const handleCopy = async (event: React.MouseEvent) => {
    event.stopPropagation();
    const ok = await copyToClipboard(payload);
    if (!ok) return;
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1600);
  };

  return (
    <div className="mt-0.5 overflow-hidden rounded-md border border-[var(--border-subtle)] bg-[var(--bg-terminal)]">
      <div className="flex items-center justify-between border-b border-[var(--border-subtle)] px-2 py-1">
        <span className="font-mono text-[9.5px] font-semibold uppercase tracking-[0.08em] text-content-dim">
          {visible.length > 1 ? 'Details' : visible[0]?.label || 'Details'}
        </span>

        {payload ? (
          <button
            type="button"
            onClick={handleCopy}
            className="flex items-center gap-1 rounded px-1 py-0.5 font-mono text-[10px] text-content-dim transition-colors hover:bg-white/[0.05] hover:text-content-main"
          >
            {copied ? <Check className="h-2.5 w-2.5 text-emerald-400" /> : <Copy className="h-2.5 w-2.5" />}
            <span>{copied ? 'Copied' : 'Copy'}</span>
          </button>
        ) : null}
      </div>

      <div className={`overflow-auto px-2 py-1.5 ${maxHeightClass}`}>
        {visible.length === 0 ? (
          <p className="font-mono text-[10.5px] italic text-content-dim">{emptyHint}</p>
        ) : (
          visible.map((section, index) => (
            <div key={`${section.label}_${index}`} className={index > 0 ? 'mt-2' : ''}>
              {visible.length > 1 ? (
                <div
                  className={`mb-0.5 font-mono text-[9.5px] uppercase tracking-[0.06em] ${
                    section.tone === 'danger' ? 'text-rose-400/80' : 'text-content-dim'
                  }`}
                >
                  {section.label}
                </div>
              ) : null}
              <pre
                className={`whitespace-pre-wrap break-words font-mono text-[10.5px] leading-[1.55] ${
                  section.tone === 'danger' ? 'text-rose-300' : 'text-content-muted'
                }`}
              >
                {section.text}
              </pre>
            </div>
          ))
        )}
      </div>
    </div>
  );
};
