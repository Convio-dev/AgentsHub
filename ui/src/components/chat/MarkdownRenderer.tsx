import React, { useState } from 'react';
import { Copy, Check, Terminal } from 'lucide-react';

interface MarkdownRendererProps {
  content: string;
  className?: string;
}

export const MarkdownRenderer: React.FC<MarkdownRendererProps> = ({ content, className = '' }) => {
  if (!content) return null;

  const parts = splitMarkdownBlocks(content);

  return (
    <div className={`space-y-3 text-slate-100 text-[13.5px] leading-relaxed font-sans ${className}`}>
      {parts.map((part, index) => {
        if (part.type === 'code') {
          return <CodeBlock key={index} language={part.lang} code={part.code} />;
        }
        return <ProseBlock key={index} text={part.text} />;
      })}
    </div>
  );
};

interface BlockPart {
  type: 'code' | 'prose';
  lang?: string;
  code?: string;
  text?: string;
}

function splitMarkdownBlocks(content: string): BlockPart[] {
  const parts: BlockPart[] = [];
  const regex = /```([a-zA-Z0-9_\-\+]*)\n([\s\S]*?)```/g;
  let lastIndex = 0;
  let match: RegExpExecArray | null;

  while ((match = regex.exec(content)) !== null) {
    if (match.index > lastIndex) {
      parts.push({
        type: 'prose',
        text: content.substring(lastIndex, match.index),
      });
    }

    parts.push({
      type: 'code',
      lang: match[1]?.trim() || 'code',
      code: match[2],
    });

    lastIndex = regex.lastIndex;
  }

  if (lastIndex < content.length) {
    parts.push({
      type: 'prose',
      text: content.substring(lastIndex),
    });
  }

  return parts;
}

const CodeBlock: React.FC<{ language?: string; code?: string }> = ({ language = 'code', code = '' }) => {
  const [copied, setCopied] = useState(false);
  const cleanCode = code.replace(/^\n+|\n+$/g, '');

  const copy = () => {
    navigator.clipboard.writeText(cleanCode);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="my-3 rounded-xl overflow-hidden border border-slate-800 bg-[#0b0f17] shadow-lg">
      <div className="flex items-center justify-between px-3.5 py-2 bg-slate-900/90 border-b border-slate-800 font-mono text-xs select-none">
        <span className="flex items-center gap-2 text-sky-400 font-semibold lowercase">
          <Terminal className="w-3.5 h-3.5 text-slate-400" />
          <span>{language || 'code'}</span>
        </span>
        <button
          onClick={copy}
          className="flex items-center gap-1.5 px-2 py-0.5 rounded text-[11px] text-slate-400 hover:text-slate-100 hover:bg-slate-800 transition"
          title="Copy code"
        >
          {copied ? <Check className="w-3 h-3 text-emerald-400" /> : <Copy className="w-3 h-3" />}
          <span>{copied ? 'Copied' : 'Copy'}</span>
        </button>
      </div>
      <pre className="p-4 text-xs font-mono text-slate-200 overflow-x-auto whitespace-pre leading-relaxed select-text">
        <code>{cleanCode}</code>
      </pre>
    </div>
  );
};

const ProseBlock: React.FC<{ text?: string }> = ({ text = '' }) => {
  const lines = text.split('\n');
  const elements: React.ReactNode[] = [];
  let tableBuffer: string[] = [];

  const flushTable = (keyIdx: number) => {
    if (tableBuffer.length === 0) return;
    elements.push(<MarkdownTable key={`table_${keyIdx}`} rawLines={[...tableBuffer]} />);
    tableBuffer = [];
  };

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const trimmed = line.trim();

    // Table row detection
    if (trimmed.startsWith('|') && trimmed.endsWith('|')) {
      tableBuffer.push(trimmed);
      continue;
    } else if (tableBuffer.length > 0) {
      flushTable(i);
    }

    if (!trimmed) {
      elements.push(<div key={`blank_${i}`} className="h-1" />);
      continue;
    }

    // Headers
    if (line.startsWith('### ')) {
      elements.push(
        <h3 key={i} className="text-xs font-bold text-sky-400 mt-3 mb-1 font-mono uppercase tracking-wider">
          {line.slice(4)}
        </h3>
      );
      continue;
    }
    if (line.startsWith('## ')) {
      elements.push(
        <h2 key={i} className="text-sm font-bold text-slate-100 mt-3 mb-1 pb-1 border-b border-slate-800">
          {line.slice(3)}
        </h2>
      );
      continue;
    }
    if (line.startsWith('# ')) {
      elements.push(
        <h1 key={i} className="text-base font-bold text-slate-100 mt-4 mb-2 pb-1 border-b border-slate-800">
          {line.slice(2)}
        </h1>
      );
      continue;
    }

    // Bullet lists
    if (/^[\-\*•]\s+/.test(trimmed)) {
      elements.push(
        <div key={i} className="flex items-start space-x-2 pl-2 my-1">
          <span className="text-sky-400 font-bold select-none text-xs mt-1">•</span>
          <span className="flex-1 text-slate-200">{renderInlineTokens(trimmed.replace(/^[\-\*•]\s+/, ''))}</span>
        </div>
      );
      continue;
    }

    // Numbered lists
    if (/^\d+\.\s+/.test(trimmed)) {
      const numMatch = trimmed.match(/^(\d+)\.\s+(.*)/);
      elements.push(
        <div key={i} className="flex items-start space-x-2 pl-2 my-1">
          <span className="text-sky-400 font-mono text-xs select-none mt-1">{numMatch?.[1]}.</span>
          <span className="flex-1 text-slate-200">{renderInlineTokens(numMatch?.[2] || '')}</span>
        </div>
      );
      continue;
    }

    // Blockquotes
    if (trimmed.startsWith('> ')) {
      elements.push(
        <blockquote
          key={i}
          className="border-l-2 border-sky-500/60 pl-3.5 py-1.5 my-2 text-slate-300 italic bg-slate-900/50 rounded-r text-xs leading-relaxed"
        >
          {renderInlineTokens(trimmed.slice(2))}
        </blockquote>
      );
      continue;
    }

    // Standard paragraph
    elements.push(
      <p key={i} className="my-1 text-slate-200 leading-relaxed">
        {renderInlineTokens(line)}
      </p>
    );
  }

  if (tableBuffer.length > 0) {
    flushTable(lines.length);
  }

  return <div className="space-y-1">{elements}</div>;
};

const MarkdownTable: React.FC<{ rawLines: string[] }> = ({ rawLines }) => {
  if (rawLines.length === 0) return null;

  const parseRow = (line: string) => {
    return line
      .slice(1, -1)
      .split('|')
      .map(cell => cell.trim());
  };

  const headerRow = parseRow(rawLines[0]);
  const hasDivider = rawLines.length > 1 && /^[\s|:\-]+$/.test(rawLines[1]);
  const dataRows = rawLines.slice(hasDivider ? 2 : 1).map(parseRow);

  return (
    <div className="my-3 overflow-x-auto rounded-xl border border-slate-800 bg-slate-950/70 shadow-sm">
      <table className="w-full text-left text-xs border-collapse">
        <thead>
          <tr className="bg-slate-900 border-b border-slate-800 font-mono text-sky-400">
            {headerRow.map((cell, idx) => (
              <th key={idx} className="px-3.5 py-2 font-semibold tracking-wide">
                {renderInlineTokens(cell)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-800/60 text-slate-200 font-sans">
          {dataRows.map((row, rIdx) => (
            <tr key={rIdx} className="hover:bg-slate-900/40 transition">
              {row.map((cell, cIdx) => (
                <td key={cIdx} className="px-3.5 py-2">
                  {renderInlineTokens(cell)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

function renderInlineTokens(line: string): React.ReactNode[] {
  const tokens: React.ReactNode[] = [];
  const regex = /(`[^`]+`|\*\*[^*]+\*\*|\[[^\]]+\]\([^)]+\))/g;
  let lastIdx = 0;
  let match: RegExpExecArray | null;

  while ((match = regex.exec(line)) !== null) {
    if (match.index > lastIdx) {
      tokens.push(line.substring(lastIdx, match.index));
    }

    const token = match[0];
    if (token.startsWith('`') && token.endsWith('`')) {
      tokens.push(
        <code
          key={match.index}
          className="px-1.5 py-0.5 mx-0.5 rounded bg-slate-800/90 border border-slate-700/70 text-sky-300 font-mono text-[11.5px]"
        >
          {token.slice(1, -1)}
        </code>
      );
    } else if (token.startsWith('**') && token.endsWith('**')) {
      tokens.push(
        <strong key={match.index} className="font-semibold text-slate-100">
          {token.slice(2, -2)}
        </strong>
      );
    } else if (token.startsWith('[') && token.includes('](')) {
      const linkMatch = token.match(/\[([^\]]+)\]\(([^)]+)\)/);
      if (linkMatch) {
        tokens.push(
          <a
            key={match.index}
            href={linkMatch[2]}
            target="_blank"
            rel="noopener noreferrer"
            className="text-sky-400 hover:text-sky-300 underline font-medium"
          >
            {linkMatch[1]}
          </a>
        );
      } else {
        tokens.push(token);
      }
    } else {
      tokens.push(token);
    }

    lastIdx = regex.lastIndex;
  }

  if (lastIdx < line.length) {
    tokens.push(line.substring(lastIdx));
  }

  return tokens;
}
