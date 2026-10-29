import React, { useMemo, useState } from 'react';
import { Check, Copy, SquareTerminal } from 'lucide-react';
import { copyToClipboard } from '../../lib/clipboard';
import { highlightCode } from '../../lib/highlight';

interface MarkdownRendererProps {
  content: string;
  className?: string;
}

/**
 * Renders assistant prose.
 *
 * Every colour here resolves through a theme token. The previous version hardcoded `text-slate-100`
 * for the container and `text-slate-200` for paragraphs, which is why the reply became invisible on
 * the light surface: light grey text on a white card.
 */
export const MarkdownRenderer: React.FC<MarkdownRendererProps> = ({ content, className = '' }) => {
  if (!content) return null;

  const parts = splitMarkdownBlocks(content);

  return (
    <div className={`space-y-3 font-sans text-[13.5px] leading-relaxed text-content-main ${className}`}>
      {parts.map((part, index) =>
        part.type === 'code' ? (
          <CodeBlock key={index} language={part.lang} code={part.code} />
        ) : (
          <ProseBlock key={index} text={part.text} />
        )
      )}
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
      parts.push({ type: 'prose', text: content.substring(lastIndex, match.index) });
    }

    parts.push({ type: 'code', lang: match[1]?.trim() || 'code', code: match[2] });
    lastIndex = regex.lastIndex;
  }

  if (lastIndex < content.length) {
    parts.push({ type: 'prose', text: content.substring(lastIndex) });
  }

  return parts;
}

/** Above this the per-chunk highlighting cost during streaming outweighs the benefit. */
const HIGHLIGHT_SIZE_LIMIT = 20_000;

const CodeBlock: React.FC<{ language?: string; code?: string }> = ({ language = 'code', code = '' }) => {
  const [copied, setCopied] = useState(false);
  const cleanCode = code.replace(/^\n+|\n+$/g, '');

  const highlighted = useMemo(() => {
    if (cleanCode.length > HIGHLIGHT_SIZE_LIMIT) return null;
    return highlightCode(cleanCode, language);
  }, [cleanCode, language]);

  const copy = async () => {
    const ok = await copyToClipboard(cleanCode);
    if (!ok) return;
    setCopied(true);
    window.setTimeout(() => setCopied(false), 1600);
  };

  return (
    <div className="my-3 overflow-hidden rounded-lg bg-surface-code">
      <div className="flex select-none items-center justify-between bg-surface-codeHeader/70 px-3.5 py-1.5 font-mono text-xs">
        <span className="flex items-center gap-2 font-semibold lowercase text-accent">
          <SquareTerminal className="h-3.5 w-3.5 text-content-dim" />
          <span>{highlighted?.language || language || 'code'}</span>
        </span>
        <button
          type="button"
          onClick={copy}
          className="flex items-center gap-1.5 rounded px-2 py-0.5 text-[11px] text-content-muted transition-colors hover:bg-surface-hover hover:text-content-main"
          title="Copy code"
        >
          {copied ? <Check className="h-3 w-3 text-state-success" /> : <Copy className="h-3 w-3" />}
          <span>{copied ? 'Copied' : 'Copy'}</span>
        </button>
      </div>
      <pre className="code-highlight overflow-x-auto whitespace-pre p-4 font-mono text-xs leading-relaxed text-content-main">
        {highlighted ? (
          // Prism escapes the source before wrapping tokens, so this is markup we generated.
          <code dangerouslySetInnerHTML={{ __html: highlighted.html }} />
        ) : (
          <code>{cleanCode}</code>
        )}
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

    if (line.startsWith('### ')) {
      elements.push(
        <h3 key={i} className="mb-1 mt-3 font-mono text-xs font-bold uppercase tracking-wider text-accent">
          {line.slice(4)}
        </h3>
      );
      continue;
    }
    if (line.startsWith('## ')) {
      elements.push(
        <h2 key={i} className="mb-1 mt-4 text-sm font-bold text-content-main">
          {line.slice(3)}
        </h2>
      );
      continue;
    }
    if (line.startsWith('# ')) {
      elements.push(
        <h1 key={i} className="mb-2 mt-5 text-base font-bold text-content-main">
          {line.slice(2)}
        </h1>
      );
      continue;
    }

    if (/^[\-*•]\s+/.test(trimmed)) {
      elements.push(
        <div key={i} className="my-1 flex items-start space-x-2 pl-2">
          <span className="mt-1 select-none text-xs font-bold text-accent">•</span>
          <span className="flex-1 text-content-main">{renderInlineTokens(trimmed.replace(/^[\-*•]\s+/, ''))}</span>
        </div>
      );
      continue;
    }

    if (/^\d+\.\s+/.test(trimmed)) {
      const numMatch = trimmed.match(/^(\d+)\.\s+(.*)/);
      elements.push(
        <div key={i} className="my-1 flex items-start space-x-2 pl-2">
          <span className="mt-1 select-none font-mono text-xs text-accent">{numMatch?.[1]}.</span>
          <span className="flex-1 text-content-main">{renderInlineTokens(numMatch?.[2] || '')}</span>
        </div>
      );
      continue;
    }

    if (trimmed.startsWith('> ')) {
      elements.push(
        <blockquote
          key={i}
          className="my-2 rounded-r border-l-2 border-accent/60 bg-surface-sunken py-1.5 pl-3.5 pr-2 text-xs italic leading-relaxed text-content-muted"
        >
          {renderInlineTokens(trimmed.slice(2))}
        </blockquote>
      );
      continue;
    }

    elements.push(
      <p key={i} className="my-1 leading-relaxed text-content-main">
        {renderInlineTokens(line)}
      </p>
    );
  }

  if (tableBuffer.length > 0) flushTable(lines.length);

  return <div className="space-y-1">{elements}</div>;
};

const MarkdownTable: React.FC<{ rawLines: string[] }> = ({ rawLines }) => {
  if (rawLines.length === 0) return null;

  const parseRow = (line: string) =>
    line
      .slice(1, -1)
      .split('|')
      .map(cell => cell.trim());

  const headerRow = parseRow(rawLines[0]);
  const hasDivider = rawLines.length > 1 && /^[\s|:\-]+$/.test(rawLines[1]);
  const dataRows = rawLines.slice(hasDivider ? 2 : 1).map(parseRow);

  return (
    <div className="my-3 overflow-x-auto rounded-xl border border-surface-border bg-surface-code">
      <table className="w-full border-collapse text-left text-xs">
        <thead>
          <tr className="border-b border-surface-border bg-surface-codeHeader font-mono text-accent">
            {headerRow.map((cell, idx) => (
              <th key={idx} className="px-3.5 py-2 font-semibold tracking-wide">
                {renderInlineTokens(cell)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-surface-border/60 font-sans text-content-main">
          {dataRows.map((row, rIdx) => (
            <tr key={rIdx} className="transition-colors hover:bg-surface-hover">
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
          className="mx-0.5 rounded bg-surface-strong px-1.5 py-0.5 font-mono text-[11.5px] text-accent"
        >
          {token.slice(1, -1)}
        </code>
      );
    } else if (token.startsWith('**') && token.endsWith('**')) {
      tokens.push(
        <strong key={match.index} className="font-semibold text-content-main">
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
            className="font-medium text-accent underline decoration-accent/40 hover:decoration-accent"
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
