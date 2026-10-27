import React from 'react';
import { TimelineNode } from '../../../types/agent';
import { TimelineThinkingNode } from './TimelineThinkingNode';
import { TimelineCommandNode } from './TimelineCommandNode';
import { TimelineFileNode } from './TimelineFileNode';
import { TimelineDiffNode } from './TimelineDiffNode';
import { TimelineActionGroup } from './TimelineActionGroup';

interface AgentTimelineProps {
  nodes: TimelineNode[];
  isLive?: boolean;
}

export const AgentTimeline: React.FC<AgentTimelineProps> = ({ nodes, isLive = false }) => {
  if (!nodes || nodes.length === 0) return null;

  return (
    <div className="relative my-3 pl-6">
      {/* Connected vertical stepper rail */}
      <div className="absolute left-2.5 top-2 bottom-2 w-px bg-slate-800" />

      <div className="space-y-2">
        {nodes.map(node => {
          let content = null;
          let nodeColor = 'bg-slate-700 border-slate-600';

          switch (node.type) {
            case 'thought':
              content = <TimelineThinkingNode node={node} />;
              nodeColor = node.status === 'running' ? 'bg-sky-500 border-sky-400 animate-pulse' : 'bg-sky-600/60 border-sky-500/40';
              break;
            case 'command':
              content = <TimelineCommandNode node={node} />;
              nodeColor = node.status === 'running' ? 'bg-amber-500 border-amber-400 animate-pulse' : node.status === 'failed' ? 'bg-rose-500 border-rose-400' : 'bg-emerald-600 border-emerald-500';
              break;
            case 'file':
              content = <TimelineFileNode node={node} />;
              nodeColor = 'bg-indigo-500 border-indigo-400';
              break;
            case 'diff':
              content = <TimelineDiffNode node={node} />;
              nodeColor = 'bg-emerald-500 border-emerald-400';
              break;
            case 'group':
              content = <TimelineActionGroup node={node} />;
              nodeColor = node.status === 'running' ? 'bg-sky-500 border-sky-400 animate-pulse' : 'bg-slate-600 border-slate-500';
              break;
          }

          return (
            <div key={node.id} className="relative group">
              {/* Stepper node circle on the vertical line */}
              <div
                className={`absolute -left-[19px] top-3.5 w-2.5 h-2.5 rounded-full border ${nodeColor} shadow-sm z-10 transition-transform group-hover:scale-125`}
              />
              {content}
            </div>
          );
        })}
      </div>
    </div>
  );
};
