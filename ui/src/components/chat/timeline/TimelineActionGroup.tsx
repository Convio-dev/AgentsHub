import React, { useState } from 'react';
import { Layers, ChevronRight, ChevronDown } from 'lucide-react';
import { ActionGroupTimelineNode } from '../../../types/agent';
import { TimelineCommandNode } from './TimelineCommandNode';
import { TimelineFileNode } from './TimelineFileNode';
import { TimelineDiffNode } from './TimelineDiffNode';
import { TimelineThinkingNode } from './TimelineThinkingNode';

interface TimelineActionGroupProps {
  node: ActionGroupTimelineNode;
}

export const TimelineActionGroup: React.FC<TimelineActionGroupProps> = ({ node }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  const isRunning = node.status === 'running';

  return (
    <div className="my-2 rounded-lg border border-slate-700/80 bg-slate-900/80 overflow-hidden text-xs shadow-sm">
      <div
        onClick={() => setIsExpanded(!isExpanded)}
        className="flex items-center justify-between px-3.5 py-2 cursor-pointer select-none font-mono text-[11.5px] bg-slate-800/40 hover:bg-slate-800/70 transition-colors"
      >
        <div className="flex items-center space-x-2.5 min-w-0">
          <Layers className="w-4 h-4 text-sky-400 flex-shrink-0" />
          <span className="text-slate-200 font-semibold">{node.title}</span>
          {node.summary && <span className="text-slate-400 text-[11px] truncate">{node.summary}</span>}
        </div>

        <div className="flex items-center space-x-2 flex-shrink-0">
          {isRunning ? (
            <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-[10px] font-semibold bg-sky-500/10 text-sky-400 border border-sky-500/30">
              <span className="w-1.5 h-1.5 rounded-full bg-sky-400 animate-ping" />
              <span>Working</span>
            </span>
          ) : (
            node.durationSeconds && (
              <span className="text-slate-400 text-[10.5px]">
                {node.durationSeconds.toFixed(1)}s
              </span>
            )
          )}

          {isExpanded ? (
            <ChevronDown className="w-3.5 h-3.5 text-sky-400" />
          ) : (
            <ChevronRight className="w-3.5 h-3.5 text-slate-500" />
          )}
        </div>
      </div>

      {isExpanded && (
        <div className="p-2 space-y-1 bg-black/20 border-t border-slate-800">
          {node.children.map(child => {
            switch (child.type) {
              case 'thought':
                return <TimelineThinkingNode key={child.id} node={child} />;
              case 'command':
                return <TimelineCommandNode key={child.id} node={child} />;
              case 'file':
                return <TimelineFileNode key={child.id} node={child} />;
              case 'diff':
                return <TimelineDiffNode key={child.id} node={child} />;
              default:
                return null;
            }
          })}
        </div>
      )}
    </div>
  );
};
