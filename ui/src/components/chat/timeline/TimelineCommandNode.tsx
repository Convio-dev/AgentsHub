import React, { useState } from 'react';
import { SquareTerminal } from 'lucide-react';
import { CommandTimelineNode } from '../../../types/agent';
import { TimelineRow } from './TimelineRow';
import { TimelineDrawer } from './TimelineDrawer';
import {
  COMMAND_PARAM_KEYS,
  formatDurationMs,
  normaliseOutput,
  prettyParams,
  singleLinePreview,
  nodeSubject,
} from '../../../lib/toolMeta';
import { useLiveElapsed } from '../../../lib/useLiveElapsed';

interface TimelineCommandNodeProps {
  node: CommandTimelineNode;
}

/**
 * A single shell command. The collapsed row is one line — `Ran git show --stat 38cc53e…` — and the
 * full command, parameters and output live behind the disclosure.
 */
export const TimelineCommandNode: React.FC<TimelineCommandNodeProps> = ({ node }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  const isRunning = node.status === 'running';
  const isFailed = node.status === 'failed' || (node.exitCode !== undefined && node.exitCode !== 0);
  const liveMs = useLiveElapsed(node.timestamp, isRunning);

  const stdout = normaliseOutput(node.stdout ?? node.output);
  const stderr = normaliseOutput(node.stderr);
  const params = prettyParams(node.params, COMMAND_PARAM_KEYS);

  const durationLabel = isRunning ? formatDurationMs(liveMs) : formatDurationMs(node.durationMs);

  return (
    <TimelineRow
      icon={<SquareTerminal className="h-3 w-3" />}
      verb={isRunning ? 'Running' : 'Ran'}
      subject={nodeSubject(node)}
      subjectTitle={node.command}
      tone={isRunning ? 'accent' : isFailed ? 'danger' : 'neutral'}
      isRunning={isRunning}
      isExpanded={isExpanded}
      onToggle={() => setIsExpanded(prev => !prev)}
      meta={
        <>
          {isFailed && node.exitCode !== undefined ? (
            <span className="font-mono text-[10px] text-state-danger/90">exit {node.exitCode}</span>
          ) : null}
          {durationLabel ? (
            <span className="font-mono text-[10px] tabular-nums text-content-dim">{durationLabel}</span>
          ) : null}
        </>
      }
      drawer={
        <TimelineDrawer
          sections={[
            { label: 'Command', text: node.command },
            { label: 'Parameters', text: params },
            { label: 'Output', text: stdout },
            { label: 'Error', text: stderr, tone: 'danger' },
          ]}
          emptyHint="This command produced no captured output."
        />
      }
    />
  );
};
