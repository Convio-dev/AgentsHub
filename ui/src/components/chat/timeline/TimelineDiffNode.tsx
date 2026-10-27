import React, { useState } from 'react';
import { FileDiff } from 'lucide-react';
import { DiffTimelineNode } from '../../../types/agent';
import { TimelineRow } from './TimelineRow';
import { TimelineDrawer } from './TimelineDrawer';
import { fileName, normaliseOutput } from '../../../lib/toolMeta';

interface TimelineDiffNodeProps {
  node: DiffTimelineNode;
}

/** A file edit, collapsed to `Edited store.ts +12 −4`. */
export const TimelineDiffNode: React.FC<TimelineDiffNodeProps> = ({ node }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  const isRunning = node.status === 'running';
  const displayName = fileName(node.file) || node.file;
  const hasCounts = node.additions > 0 || node.deletions > 0;

  return (
    <TimelineRow
      icon={<FileDiff className="h-3 w-3" />}
      verb={isRunning ? 'Editing' : 'Edited'}
      subject={displayName}
      subjectTitle={node.file}
      tone={isRunning ? 'accent' : node.status === 'failed' ? 'danger' : 'neutral'}
      isRunning={isRunning}
      isExpanded={isExpanded}
      onToggle={() => setIsExpanded(prev => !prev)}
      meta={
        hasCounts ? (
          <span className="font-mono text-[10px] tabular-nums">
            <span className="text-emerald-400/90">+{node.additions}</span>
            <span className="mx-1 text-content-dim">/</span>
            <span className="text-rose-400/90">−{node.deletions}</span>
          </span>
        ) : null
      }
      drawer={
        <TimelineDrawer
          sections={[
            { label: 'File', text: node.file },
            { label: 'Patch', text: normaliseOutput(node.patch) },
          ]}
          emptyHint="The engine did not return a patch for this edit."
        />
      }
    />
  );
};
