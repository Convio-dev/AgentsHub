import React, { useState } from 'react';
import { TimelineRail, TimelineRow } from './TimelineRow';
import { TimelineNodeView } from './TimelineNodeView';
import { TimelineRow as TimelineRowModel } from '../../../lib/timelineGrouping';
import { formatDurationMs } from '../../../lib/toolMeta';
import { useLiveElapsed } from '../../../lib/useLiveElapsed';

type GroupRow = Extract<TimelineRowModel, { kind: 'group' }>;

interface TimelineGroupRowProps {
  row: GroupRow;
}

/**
 * A run of consecutive same-kind operations: `Ran 3 commands`, `Explored 2 files, 5 searches`.
 * Collapsed by default so a long turn stays scannable; expanding reveals the individual calls on a
 * nested rail.
 */
export const TimelineGroupRow: React.FC<TimelineGroupRowProps> = ({ row }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  const liveMs = useLiveElapsed(row.nodes[0]?.timestamp ?? 0, row.isRunning);
  const durationLabel = formatDurationMs(row.isRunning ? liveMs : row.durationMs);

  return (
    <div>
      <TimelineRow
        subject={row.label}
        emphasise
        tone={row.isRunning ? 'accent' : 'neutral'}
        isRunning={row.isRunning}
        isExpanded={isExpanded}
        alwaysShowChevron
        onToggle={() => setIsExpanded(prev => !prev)}
        meta={
          durationLabel ? (
            <span className="font-mono text-[10px] tabular-nums text-content-dim">{durationLabel}</span>
          ) : null
        }
      />

      {isExpanded ? (
        <TimelineRail className="mt-0.5">
          {row.nodes.map(node => (
            <TimelineNodeView key={node.id} node={node} />
          ))}
        </TimelineRail>
      ) : null}
    </div>
  );
};
