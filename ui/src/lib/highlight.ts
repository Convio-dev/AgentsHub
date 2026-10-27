/**
 * Syntax highlighting for fenced code blocks.
 *
 * Prism runs synchronously, which matters here: the assistant streams tokens into the same block, so
 * an async highlighter would flash plain text on every chunk. Grammars are imported once at module
 * load and the result is a plain HTML string that React renders with `dangerouslySetInnerHTML`
 * (Prism escapes the source, so the markup cannot carry user HTML).
 */

import Prism from 'prismjs';

// Core grammars ship with prismjs (markup, css, clike, javascript); the rest are loaded explicitly.
import 'prismjs/components/prism-markup-templating';
import 'prismjs/components/prism-typescript';
import 'prismjs/components/prism-jsx';
import 'prismjs/components/prism-tsx';
import 'prismjs/components/prism-json';
import 'prismjs/components/prism-yaml';
import 'prismjs/components/prism-bash';
import 'prismjs/components/prism-powershell';
import 'prismjs/components/prism-python';
import 'prismjs/components/prism-csharp';
import 'prismjs/components/prism-diff';
import 'prismjs/components/prism-markdown';
import 'prismjs/components/prism-sql';
import 'prismjs/components/prism-go';
import 'prismjs/components/prism-rust';
import 'prismjs/components/prism-java';
import 'prismjs/components/prism-c';
import 'prismjs/components/prism-cpp';
import 'prismjs/components/prism-ini';
import 'prismjs/components/prism-toml';

/** Fence tags people actually write, mapped to the Prism grammar that should handle them. */
const LANGUAGE_ALIASES: Record<string, string> = {
  js: 'javascript',
  mjs: 'javascript',
  cjs: 'javascript',
  jsx: 'jsx',
  ts: 'typescript',
  typescript: 'typescript',
  tsx: 'tsx',
  py: 'python',
  python: 'python',
  python3: 'python',
  cs: 'csharp',
  csharp: 'csharp',
  'c#': 'csharp',
  sh: 'bash',
  shell: 'bash',
  zsh: 'bash',
  bash: 'bash',
  console: 'bash',
  ps: 'powershell',
  ps1: 'powershell',
  powershell: 'powershell',
  pwsh: 'powershell',
  yml: 'yaml',
  yaml: 'yaml',
  json: 'json',
  jsonc: 'json',
  html: 'markup',
  xml: 'markup',
  svg: 'markup',
  markup: 'markup',
  css: 'css',
  scss: 'css',
  less: 'css',
  diff: 'diff',
  patch: 'diff',
  md: 'markdown',
  markdown: 'markdown',
  sql: 'sql',
  go: 'go',
  golang: 'go',
  rs: 'rust',
  rust: 'rust',
  java: 'java',
  kotlin: 'java',
  c: 'c',
  h: 'c',
  cpp: 'cpp',
  'c++': 'cpp',
  hpp: 'cpp',
  ini: 'ini',
  conf: 'ini',
  toml: 'toml',
};

/** Tags that carry no language information and are worth guessing from the content. */
const GENERIC_TAGS = new Set(['', 'code', 'text', 'plain', 'plaintext', 'txt', 'output']);

/** Only worth auto-detecting once there is enough content for the grammar to lock onto. */
const AUTO_DETECT_MIN_LENGTH = 40;

const AUTO_DETECT_LANGUAGES = ['python', 'bash', 'powershell', 'json', 'javascript', 'typescript', 'csharp'];

/**
 * `highlightAuto` is part of Prism's core runtime but missing from `@types/prismjs`, so the call is
 * typed narrowly here instead of casting at the call site.
 */
type AutoHighlighter = (code: string, languages?: string[]) => { language: string; value: string };
const highlightAuto = (Prism as unknown as { highlightAuto?: AutoHighlighter }).highlightAuto;

export interface HighlightResult {
  /** Escaped HTML with Prism token spans. */
  html: string;
  /** Canonical language that produced the markup, or null when rendered as plain text. */
  language: string | null;
}

function escapeHtml(code: string): string {
  return code.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function highlightWith(code: string, language: string): string | null {
  const grammar = Prism.languages[language];
  if (!grammar) return null;

  try {
    return Prism.highlight(code, grammar, language);
  } catch {
    return null;
  }
}

/**
 * Highlights a fenced block. Unknown tags render as escaped plain text rather than guessing wildly;
 * generic tags (`code`, `text`) are auto-detected when the snippet is long enough to be meaningful.
 */
export function highlightCode(code: string, fenceTag: string): HighlightResult {
  const tag = (fenceTag || '').trim().toLowerCase();

  if (GENERIC_TAGS.has(tag)) {
    if (highlightAuto && code.trim().length >= AUTO_DETECT_MIN_LENGTH) {
      try {
        const auto = highlightAuto(code, AUTO_DETECT_LANGUAGES);
        if (auto.language && auto.language !== 'plain') {
          return { html: auto.value, language: auto.language };
        }
      } catch {
        // fall through to plain text
      }
    }
    return { html: escapeHtml(code), language: null };
  }

  const language = LANGUAGE_ALIASES[tag] || tag;
  const html = highlightWith(code, language);
  if (html) return { html, language };

  return { html: escapeHtml(code), language: null };
}

/** Languages that actually resolved a grammar, used to label the block header honestly. */
export function isLanguageSupported(fenceTag: string): boolean {
  const tag = (fenceTag || '').trim().toLowerCase();
  const language = LANGUAGE_ALIASES[tag] || tag;
  return Boolean(Prism.languages[language]);
}
