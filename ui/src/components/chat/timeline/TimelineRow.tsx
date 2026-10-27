import React from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';

export type RowTone = 'neutral' | 'accent' | 'success' | 'danger';

const DOT_TONE: Record<RowTone, string> = {
  neutral: 'bg-[var(--border-light)]',
  accent: 'bg-sky-400',
  success: 'bg-emerald-500/70',
  danger: 'bg-rose-500',
};

/** Vertical hairline that every stepper row hangs off. One level of nesting adds one rail. */
export const TimelineRail: React.FC<{ children: React.ReactNode; className?: string }> = ({
  children,
  className = '',
}) => (
  <div className={`relative pl-[18px] ${className}`}>
    <span className="absolute bottom-1 left-[7px] top-1 w-px bg-[var(--border-light)]/70" aria-hidden="true" />
    {children}
  </div>
);

interface TimelineRowProps {
  /** Verb shown before the subject: `Ran`, `Explored`, `Thought`. Omit for a bare label. */
  verb?: string;
  subject: string;
  subjectTitle?: string;
  /** Trailing metadata: duration, diff counts, exit status. */
  meta?: React.ReactNode;
  icon?: React.ReactNode;
  isExpanded: boolean;
  onToggle: () => void;
  isRunning?: boolean;
  tone?: RowTone;
  /** Marks the row as already-emphasised content (group headers, live work). */
  emphasise?: boolean;
  alwaysShowChevron?: boolean;
  /** Rendered directly below the row when expanded. */
  drawer?: React.ReactNode;
  ariaLabel?: string;
}

/**
 * One stepper line: a dot on the rail, an optional verb, a single-line subject, trailing meta and a
 * disclosure chevron. Deliberately not a card — a turn can contain dozens of these, and bordered
 * cards at that density read as noise rather than progress.
 */
export const TimelineRow: React.FC<TimelineRowProps> = ({
  verb,
  subject,
  subjectTitle,
  meta,
  icon,
  isExpanded,
  onToggle,
  isRunning = false,
  tone = 'neutral',
  emphasise = false,
  alwaysShowChevron = false,
  drawer,
  ariaLabel,
}) => (
  <div className="relative">
    <button
      type="button"
      onClick={onToggle}
      aria-expanded={isExpanded}
      aria-label={ariaLabel || `${verb ? `${verb} ` : ''}${subject}`}
      className={`group/row flex w-full items-center gap-1.5 rounded-[5px] py-[3px] pr-1 text-left font-mono text-[11px] leading-5 transition-colors focus-visible:outline focus-visible:outline-1 focus-visible:outline-sky-500/60 ${
        isRunning ? 'bg-sky-500/[0.05] hover:bg-sky-500/[0.09]' : 'hover:bg-white/[0.035]'
      }`}
    >
      <span
        aria-hidden="true"
        className={`absolute left-[-13px] top-1/2 h-[5px] w-[5px] -translate-y-1/2 rounded-full ${DOT_TONE[tone]} ${
          isRunning ? 'animate-pulse' : ''
        }`}
      />

      {icon ? <span className="flex-shrink-0 text-content-dim">{icon}</span> : null}

      {verb ? <span className="flex-shrink-0 text-content-dim">{verb}</span> : null}

      <span
        className={`truncate ${
          emphasise ? 'font-medium text-content-main' : 'text-content-muted group-hover/row:text-content-main'
        }`}
        title={subjectTitle || subject}
      >
        {subject}
      </span>

      <span className="ml-auto flex flex-shrink-0 items-center gap-1.5 pl-2">
        {meta}
        {isExpanded ? (
          <ChevronDown className="h-3 w-3 text-content-dim" />
        ) : (
          <ChevronRight
            className={`h-3 w-3 text-content-dim ${alwaysShowChevron ? '' : 'opacity-0 group-hover/row:opacity-100'}`}
          />
        )}
      </span>
    </button>

    {isExpanded && drawer ? <div className="pb-1">{drawer}</div> : null}
  </div>
);
