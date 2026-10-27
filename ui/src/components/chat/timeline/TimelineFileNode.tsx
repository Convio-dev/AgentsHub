import React, { useState } from 'react';
import { FileSearch, FileText } from 'lucide-react';
import { FileTimelineNode } from '../../../types/agent';
import { TimelineRow } from './TimelineRow';
import { TimelineDrawer } from './TimelineDrawer';
import { fileName, normaliseOutput, verbForNode } from '../../../lib/toolMeta';

interface TimelineFileNodeProps {
  node: FileTimelineNode;
}

/** A file read, directory listing or content search. Collapsed to `Explored app.ts`. */
export const TimelineFileNode: React.FC<TimelineFileNodeProps> = ({ node }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  const isRunning = node.status === 'running';
  const isSearch = node.action === 'search';
  const displayName = fileName(node.path) || node.path;

  return (
    <TimelineRow
      icon={isSearch ? <FileSearch className="h-3 w-3" /> : <FileText className="h-3 w-3" />}
      verb={verbForNode(node)}
      subject={displayName}
      subjectTitle={node.path}
      tone={isRunning ? 'accent' : node.status === 'failed' ? 'danger' : 'neutral'}
      isRunning={isRunning}
      isExpanded={isExpanded}
      onToggle={() => setIsExpanded(prev => !prev)}
      meta={
        node.count && node.count > 1 ? (
          <span className="font-mono text-[10px] tabular-nums text-content-dim">{node.count} matches</span>
        ) : null
      }
      drawer={
        <TimelineDrawer
          sections={[
            { label: 'Path', text: node.path },
            { label: 'Result', text: normaliseOutput(node.contentPreview) },
          ]}
          emptyHint="No content was captured for this file operation."
        />
      }
    />
  );
};
