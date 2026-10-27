import React, { useState } from 'react';
import { FileEdit, ChevronRight, ChevronDown } from 'lucide-react';
import { DiffTimelineNode } from '../../../types/agent';

interface TimelineDiffNodeProps {
  node: DiffTimelineNode;
}

export const TimelineDiffNode: React.FC<TimelineDiffNodeProps> = ({ node }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  const fileName = node.file.split(/[/\\]/).pop() || node.file;

  return (
    <div className="my-1.5 rounded-lg border border-slate-800 bg-slate-900/50 overflow-hidden text-xs transition-all">
      <div
        onClick={() => setIsExpanded(!isExpanded)}
        className="flex items-center justify-between px-3 py-2 cursor-pointer select-none font-mono text-[11.5px] hover:bg-slate-800/50 transition-colors"
      >
        <div className="flex items-center space-x-2.5 min-w-0 max-w-[80%]">
          <FileEdit className="w-3.5 h-3.5 text-emerald-400 flex-shrink-0" />
          <span className="text-slate-400 flex-shrink-0">Edited</span>
          <span className="text-slate-100 font-medium truncate" title={node.file}>
            {fileName}
          </span>
        </div>

        <div className="flex items-center space-x-2 flex-shrink-0">
          <span className="inline-flex items-center gap-1 font-mono text-[10.5px]">
            <span className="px-1.5 py-0.5 rounded bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 font-semibold">
              +{node.additions || 0}
            </span>
            <span className="px-1.5 py-0.5 rounded bg-rose-500/10 text-rose-400 border border-rose-500/20 font-semibold">
              -{node.deletions || 0}
            </span>
          </span>

          {isExpanded ? (
            <ChevronDown className="w-3.5 h-3.5 text-sky-400" />
          ) : (
            <ChevronRight className="w-3.5 h-3.5 text-slate-500" />
          )}
        </div>
      </div>

      {isExpanded && node.patch && (
        <div className="px-3 py-2 bg-black/40 border-t border-slate-800 text-[11px] font-mono leading-relaxed">
          <div className="text-slate-500 uppercase text-[9.5px] font-bold tracking-wider mb-1">Diff Preview</div>
          <pre className="text-slate-300 whitespace-pre-wrap overflow-x-auto max-h-48 p-2 rounded bg-black/50 border border-slate-800/80">
            {node.patch}
          </pre>
        </div>
      )}
    </div>
  );
};
