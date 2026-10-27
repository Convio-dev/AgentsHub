import React, { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { ThoughtTimelineNode } from '../../../types/agent';
import { TimelineRow } from './TimelineRow';
import { TimelineDrawer } from './TimelineDrawer';
import { formatDurationMs } from '../../../lib/toolMeta';
import { useLiveElapsed } from '../../../lib/useLiveElapsed';

interface TimelineThinkingNodeProps {
  node: ThoughtTimelineNode;
}

/**
 * A reasoning segment. Collapsed it is just `Thought for 1s`; while the model is still thinking the
 * label carries a live counter, so liveness is communicated by data rather than a looping animation.
 */
export const TimelineThinkingNode: React.FC<TimelineThinkingNodeProps> = ({ node }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  const isStreaming = node.status === 'running';
  const liveMs = useLiveElapsed(node.timestamp, isStreaming);

  // Nothing was streamed and no reasoning tokens were counted: there is no step to show. This also
  // covers rows restored from storage before the store learned to prune them.
  const hasContent = Boolean(node.text?.trim()) || Boolean(node.tokens);
  if (!hasContent && !isStreaming) return null;

  const elapsedMs = isStreaming ? liveMs : (node.durationSeconds || 0) * 1000;
  const durationLabel = formatDurationMs(elapsedMs);

  const label = isStreaming
    ? durationLabel
      ? `Thinking ${durationLabel}`
      : 'Thinking'
    : durationLabel
      ? `Thought for ${durationLabel}`
      : 'Thought';

  const tokenLabel = node.tokens ? `${node.tokens.toLocaleString()} tokens` : '';

  return (
    <TimelineRow
      icon={isStreaming ? <Loader2 className="h-3 w-3 animate-spin text-accent" /> : undefined}
      subject={label}
      subjectTitle={node.text || label}
      tone={isStreaming ? 'accent' : 'neutral'}
      isRunning={isStreaming}
      isExpanded={isExpanded}
      onToggle={() => setIsExpanded(prev => !prev)}
      meta={
        tokenLabel ? (
          <span className="font-mono text-[10px] tabular-nums text-content-dim">{tokenLabel}</span>
        ) : null
      }
      drawer={
        <TimelineDrawer
          sections={[{ label: 'Reasoning', text: node.text }]}
          emptyHint={isStreaming ? 'Reasoning is streaming in…' : 'No reasoning text was emitted for this step.'}
        />
      }
    />
  );
};
