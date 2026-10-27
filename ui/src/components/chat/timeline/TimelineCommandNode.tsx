import React, { useState } from 'react';
import { Terminal, Check, X, ChevronRight, ChevronDown, Copy } from 'lucide-react';
import { CommandTimelineNode } from '../../../types/agent';

interface TimelineCommandNodeProps {
  node: CommandTimelineNode;
}

export const TimelineCommandNode: React.FC<TimelineCommandNodeProps> = ({ node }) => {
  const [isExpanded, setIsExpanded] = useState(false);
  const [copied, setCopied] = useState(false);

  const handleCopy = (e: React.MouseEvent) => {
    e.stopPropagation();
    if (node.output) {
      navigator.clipboard.writeText(node.output);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  const isRunning = node.status === 'running';
  const isFailed = node.status === 'failed' || (node.exitCode !== undefined && node.exitCode !== 0);

  return (
    <div className="my-1.5 rounded-lg border border-slate-800 bg-slate-900/60 overflow-hidden text-xs transition-all">
      <div
        onClick={() => setIsExpanded(!isExpanded)}
        className="flex items-center justify-between px-3 py-2 cursor-pointer select-none font-mono text-[11.5px] hover:bg-slate-800/50 transition-colors"
      >
        <div className="flex items-center space-x-2.5 min-w-0 max-w-[80%]">
          <Terminal className="w-3.5 h-3.5 text-sky-400 flex-shrink-0" />
          <span className="text-slate-400 flex-shrink-0">Ran</span>
          <span className="text-slate-100 font-semibold truncate text-ellipsis">{node.command}</span>
        </div>

        <div className="flex items-center space-x-2 flex-shrink-0">
          {isRunning ? (
            <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded text-[10px] font-semibold bg-amber-500/10 text-amber-400 border border-amber-500/30">
              <span className="w-1.5 h-1.5 rounded-full bg-amber-400 animate-ping" />
              <span>Running</span>
            </span>
          ) : isFailed ? (
            <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-semibold bg-rose-500/10 text-rose-400 border border-rose-500/30">
              <X className="w-3 h-3" />
              <span>Failed</span>
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-semibold bg-emerald-500/10 text-emerald-400 border border-emerald-500/30">
              <Check className="w-3 h-3" />
              {node.durationMs ? <span>{node.durationMs}ms</span> : <span>Done</span>}
            </span>
          )}

          {isExpanded ? (
            <ChevronDown className="w-3.5 h-3.5 text-sky-400" />
          ) : (
            <ChevronRight className="w-3.5 h-3.5 text-slate-500" />
          )}
        </div>
      </div>

      {isExpanded && (
        <div className="px-3 py-2.5 bg-black/40 border-t border-slate-800 text-[11px] font-mono leading-relaxed">
          <div className="flex items-center justify-between mb-1 text-slate-500">
            <span className="uppercase text-[9.5px] font-bold tracking-wider">Command Output</span>
            {node.output && (
              <button
                onClick={handleCopy}
                className="flex items-center gap-1 text-[10px] text-slate-400 hover:text-sky-300 transition-colors"
              >
                <Copy className="w-3 h-3" />
                <span>{copied ? 'Copied' : 'Copy'}</span>
              </button>
            )}
          </div>
          <pre className="text-slate-300 whitespace-pre-wrap overflow-x-auto max-h-56 p-2 rounded bg-black/50 border border-slate-800/80">
            {node.output || '<No output returned>'}
          </pre>
        </div>
      )}
    </div>
  );
};
