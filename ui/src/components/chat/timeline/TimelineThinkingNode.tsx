import React, { useState, useEffect } from 'react';
import { Sparkles, ChevronRight, ChevronDown } from 'lucide-react';
import { ThoughtTimelineNode } from '../../../types/agent';

interface TimelineThinkingNodeProps {
  node: ThoughtTimelineNode;
}

export const TimelineThinkingNode: React.FC<TimelineThinkingNodeProps> = ({ node }) => {
  const [isExpanded, setIsExpanded] = useState(false);
  const [elapsedSeconds, setElapsedSeconds] = useState<number>(node.durationSeconds || 0);

  // Live timer tick when thought is streaming
  useEffect(() => {
    if (node.status !== 'running') {
      if (node.durationSeconds) setElapsedSeconds(node.durationSeconds);
      return;
    }

    const start = node.timestamp || Date.now();
    const interval = setInterval(() => {
      const sec = Math.max(0, (Date.now() - start) / 1000);
      setElapsedSeconds(Number(sec.toFixed(1)));
    }, 100);

    return () => clearInterval(interval);
  }, [node.status, node.timestamp, node.durationSeconds]);

  const isStreaming = node.status === 'running';

  const label = isStreaming
    ? `Thinking... ${elapsedSeconds.toFixed(1)}s`
    : `Thought for ${elapsedSeconds > 0 ? `${elapsedSeconds.toFixed(1)}s` : 'a few moments'}${
        node.tokens ? `, ${node.tokens.toLocaleString()} tokens` : ''
      }`;

  return (
    <div className="my-1.5 rounded-lg border border-sky-500/20 bg-sky-950/20 overflow-hidden transition-all text-xs">
      <div
        onClick={() => setIsExpanded(!isExpanded)}
        className="flex items-center justify-between px-3 py-1.5 cursor-pointer select-none font-mono text-[11px] hover:bg-sky-500/10 transition-colors"
      >
        <div className="flex items-center space-x-2 text-sky-400">
          <Sparkles className={`w-3.5 h-3.5 ${isStreaming ? 'animate-pulse text-sky-300' : 'text-sky-400'}`} />
          <span className="font-medium">{label}</span>
        </div>
        <div className="flex items-center space-x-2 text-slate-400">
          {isExpanded ? (
            <ChevronDown className="w-3.5 h-3.5 text-sky-400" />
          ) : (
            <ChevronRight className="w-3.5 h-3.5 text-slate-500" />
          )}
        </div>
      </div>

      {isExpanded && (
        <div className="px-3 py-2 text-[11.5px] font-mono text-slate-300 border-t border-sky-500/15 max-h-56 overflow-y-auto whitespace-pre-wrap leading-relaxed bg-black/30">
          {node.text ? node.text : <span className="text-slate-500 italic">Thinking in progress...</span>}
        </div>
      )}
    </div>
  );
};
