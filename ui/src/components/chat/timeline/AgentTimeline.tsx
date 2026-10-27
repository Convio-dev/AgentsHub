import React, { useMemo, useState } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { TimelineNode } from '../../../types/agent';
import { TimelineRail } from './TimelineRow';
import { TimelineGroupRow } from './TimelineGroupRow';
import { TimelineNodeView } from './TimelineNodeView';
import {
  buildTimelineRows,
  shouldShowRunHeader,
  summariseRun,
} from '../../../lib/timelineGrouping';
import { formatDurationMs } from '../../../lib/toolMeta';
import { useLiveElapsed } from '../../../lib/useLiveElapsed';

interface AgentTimelineProps {
  nodes: TimelineNode[];
  isLive?: boolean;
}

/**
 * The execution stepper for one assistant turn.
 *
 * The engine streams a flat list of steps; this folds them into collapsed groups (`Ran 3 commands`,
 * `Explored 2 files`) so a long turn reads as a handful of lines instead of dozens of cards. Long
 * turns also get a `Worked for 33m` header that collapses the whole stepper.
 */
export const AgentTimeline: React.FC<AgentTimelineProps> = ({ nodes, isLive = false }) => {
  const [isBodyCollapsed, setIsBodyCollapsed] = useState(false);

  const rows = useMemo(() => buildTimelineRows(nodes), [nodes]);
  const summary = useMemo(() => summariseRun(nodes), [nodes]);

  const isRunning = summary.isRunning || isLive;
  const liveMs = useLiveElapsed(summary.startedAt, isRunning);
  const showHeader = shouldShowRunHeader({ ...summary, durationMs: isRunning ? liveMs : summary.durationMs });

  if (!nodes || nodes.length === 0) return null;

  const headerLabel = isRunning
    ? `Working ${formatDurationMs(liveMs)}`
    : `Worked for ${formatDurationMs(summary.durationMs)}`;

  return (
    <div className="my-2.5">
      {showHeader ? (
        <button
          type="button"
          onClick={() => setIsBodyCollapsed(prev => !prev)}
          aria-expanded={!isBodyCollapsed}
          className="group/run mb-1 flex items-center gap-1.5 rounded-[5px] py-[3px] pr-1 text-left font-mono text-[11px] text-content-dim transition-colors hover:bg-white/[0.035] hover:text-content-main"
        >
          <span className="tabular-nums">{headerLabel}</span>
          {isBodyCollapsed ? (
            <ChevronRight className="h-3 w-3" />
          ) : (
            <ChevronDown className="h-3 w-3" />
          )}
        </button>
      ) : null}

      {isBodyCollapsed ? null : (
        <TimelineRail>
          {rows.map(row =>
            row.kind === 'group' ? (
              <TimelineGroupRow key={row.key} row={row} />
            ) : (
              <TimelineNodeView key={row.key} node={row.node} />
            )
          )}
        </TimelineRail>
      )}
    </div>
  );
};
