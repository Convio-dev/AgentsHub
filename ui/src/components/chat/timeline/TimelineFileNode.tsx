import React, { useState } from 'react';
import { FileCode, FolderSearch, ChevronRight, ChevronDown } from 'lucide-react';
import { FileTimelineNode } from '../../../types/agent';

interface TimelineFileNodeProps {
  node: FileTimelineNode;
}

export const TimelineFileNode: React.FC<TimelineFileNodeProps> = ({ node }) => {
  const [isExpanded, setIsExpanded] = useState(false);

  const isList = node.action === 'list';
  const label = isList ? 'Listed' : 'Explored';
  const fileName = node.path.split(/[/\\]/).pop() || node.path;

  return (
    <div className="my-1.5 rounded-lg border border-slate-800 bg-slate-900/50 overflow-hidden text-xs transition-all">
      <div
        onClick={() => setIsExpanded(!isExpanded)}
        className="flex items-center justify-between px-3 py-2 cursor-pointer select-none font-mono text-[11.5px] hover:bg-slate-800/50 transition-colors"
      >
        <div className="flex items-center space-x-2.5 min-w-0 max-w-[85%]">
          {isList ? (
            <FolderSearch className="w-3.5 h-3.5 text-indigo-400 flex-shrink-0" />
          ) : (
            <FileCode className="w-3.5 h-3.5 text-sky-400 flex-shrink-0" />
          )}
          <span className="text-slate-400 flex-shrink-0">{label}</span>
          <span className="text-slate-100 font-medium truncate" title={node.path}>
            {fileName}
          </span>
          {node.path !== fileName && (
            <span className="text-slate-500 text-[10.5px] truncate hidden sm:inline" title={node.path}>
              ({node.path})
            </span>
          )}
        </div>

        <div className="flex items-center space-x-2 flex-shrink-0">
          {node.count && node.count > 1 && (
            <span className="px-1.5 py-0.5 rounded text-[10px] bg-slate-800 text-slate-400 border border-slate-700">
              {node.count} files
            </span>
          )}

          {isExpanded ? (
            <ChevronDown className="w-3.5 h-3.5 text-sky-400" />
          ) : (
            <ChevronRight className="w-3.5 h-3.5 text-slate-500" />
          )}
        </div>
      </div>

      {isExpanded && node.contentPreview && (
        <div className="px-3 py-2 bg-black/40 border-t border-slate-800 text-[11px] font-mono leading-relaxed">
          <div className="text-slate-500 uppercase text-[9.5px] font-bold tracking-wider mb-1">Preview</div>
          <pre className="text-slate-300 whitespace-pre-wrap overflow-x-auto max-h-48 p-2 rounded bg-black/50 border border-slate-800/80">
            {node.contentPreview}
          </pre>
        </div>
      )}
    </div>
  );
};
