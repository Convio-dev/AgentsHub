/**
 * Tool payload normalisation and display formatting for the execution stepper.
 *
 * The engine reports tool invocations as a human label ("Ran", "Explored") plus a params bag, and
 * the label is not a stable identifier. Everything downstream therefore derives its display from
 * the params, never from the label — that is what used to render `Ran {"CommandLine":"..."}`.
 */

import { ToolKind, TimelineNode } from '../types/agent';

export const COMMAND_PARAM_KEYS = ['CommandLine', 'command', 'cmd', 'commandLine', 'Command', 'shellCommand'];
const PATH_KEYS = [
  'TargetFile',
  'AbsolutePath',
  'DirectoryPath',
  'SearchPath',
  'TargetDirectory',
  'path',
  'file',
  'filePath',
  'Path',
];

/** Shell wrappers that add noise to a command preview without changing what ran. */
const SHELL_PREFIXES = [
  /^cmd(?:\.exe)?\s+\/[cC]\s+/i,
  /^powershell(?:\.exe)?\s+(?:-\w+\s+)*-command\s+/i,
  /^pwsh(?:\.exe)?\s+(?:-\w+\s+)*-command\s+/i,
  /^\/bin\/(?:ba)?sh\s+-c\s+/i,
  /^bash\s+-c\s+/i,
];

const SEARCH_TOOL = /(grep|search|find|glob|query|ripgrep|rg)/i;
const READ_TOOL = /(read|view|cat|list|dir|tree|explor|open|head|tail)/i;
const WRITE_TOOL = /(write|edit|patch|replace|apply|create|insert|delete|remove|mv|move)/i;

function firstString(source: Record<string, unknown> | undefined, keys: string[]): string | undefined {
  if (!source) return undefined;
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'string' && value.trim().length > 0) return value;
  }
  return undefined;
}

/**
 * Resolves the semantic kind of a tool call. An explicit `toolKind` from the sidecar always wins;
 * otherwise the params are inspected before the label, because labels are display text.
 */
export function classifyTool(toolName: string | undefined, params?: Record<string, unknown>): ToolKind {
  const explicit = params?.toolKind;
  if (explicit === 'command' || explicit === 'read' || explicit === 'search' || explicit === 'write' || explicit === 'other') {
    return explicit;
  }

  if (firstString(params, COMMAND_PARAM_KEYS)) return 'command';

  const probe = `${toolName || ''} ${firstString(params, PATH_KEYS) || ''}`;
  if (SEARCH_TOOL.test(probe)) return 'search';
  if (WRITE_TOOL.test(probe)) return 'write';
  if (READ_TOOL.test(probe)) return 'read';
  return 'other';
}

/** Removes shell wrappers and the wrapping quotes a Windows path or command arrives with. */
function stripShellNoise(raw: string): string {
  let text = raw.trim();
  for (const prefix of SHELL_PREFIXES) {
    text = text.replace(prefix, '');
  }
  if (text.length > 1 && text.startsWith('"') && text.endsWith('"') && !text.slice(1, -1).includes('"')) {
    text = text.slice(1, -1);
  }
  return text.trim();
}

/**
 * Builds the single-line command shown in a collapsed row. Returns an empty string when the call
 * carries no command at all, so callers can fall back to the tool name instead of printing JSON.
 */
export function extractCommand(params?: Record<string, unknown>): string {
  const raw = firstString(params, COMMAND_PARAM_KEYS);
  if (!raw) return '';
  return collapseWhitespace(stripShellNoise(raw));
}

/** Extracts the most likely file or directory a tool touched. */
export function extractPath(params?: Record<string, unknown>): string {
  return firstString(params, PATH_KEYS) || '';
}

export function collapseWhitespace(text: string): string {
  return text.replace(/\s+/g, ' ').trim();
}

/** Truncates to a single line for a collapsed row, without cutting mid-word where avoidable. */
export function singleLinePreview(text: string, maxChars = 68): string {
  const flat = collapseWhitespace(text);
  if (flat.length <= maxChars) return flat;

  const clipped = flat.slice(0, maxChars);
  const lastSpace = clipped.lastIndexOf(' ');
  const body = lastSpace > maxChars * 0.6 ? clipped.slice(0, lastSpace) : clipped;
  return `${body.trimEnd()}…`;
}

export function fileName(path: string): string {
  if (!path) return '';
  const parts = path.split(/[/\\]/).filter(Boolean);
  return parts[parts.length - 1] || path;
}

/** Compact duration used on collapsed rows: `840ms`, `2.4s`, `34s`, `1m 20s`, `33m`. */
export function formatDurationMs(ms: number | undefined): string {
  if (!ms || ms < 0) return '';
  if (ms < 1000) return `${Math.round(ms)}ms`;

  const totalSeconds = ms / 1000;
  if (totalSeconds < 10) return `${totalSeconds.toFixed(1)}s`;
  if (totalSeconds < 60) return `${Math.round(totalSeconds)}s`;

  const minutes = Math.floor(totalSeconds / 60);
  const seconds = Math.round(totalSeconds % 60);
  if (minutes < 10 && seconds > 0) return `${minutes}m ${seconds}s`;
  return `${minutes}m`;
}

/** Same scale, but always from seconds (the engine reports thought durations that way). */
export function formatDurationSeconds(seconds: number | undefined): string {
  if (seconds === undefined || seconds < 0) return '';
  return formatDurationMs(seconds * 1000);
}

/** Verb shown before a path in a single row: `Explored src/app.ts`. */
export function verbForNode(node: TimelineNode): string {
  switch (node.type) {
    case 'command':
      return 'Ran';
    case 'file':
      if (node.action === 'search') return 'Searched';
      if (node.action === 'list') return 'Listed';
      return 'Explored';
    case 'diff':
      return 'Edited';
    case 'thought':
      return 'Thought';
    default:
      return 'Used';
  }
}

/**
 * Text shown after the verb in a single collapsed row. Always a single line, never a JSON payload.
 */
export function nodeSubject(node: TimelineNode): string {
  switch (node.type) {
    case 'command': {
      const command = collapseWhitespace(node.command || '');
      if (command && !command.startsWith('{')) return singleLinePreview(command, 72);
      // Defensive: a legacy persisted node may still hold a stringified params bag.
      const recovered = extractCommand(node.params);
      if (recovered) return singleLinePreview(recovered, 72);
      return node.toolName ? singleLinePreview(node.toolName, 72) : 'command';
    }
    case 'file':
      return singleLinePreview(fileName(node.path) || node.path, 72);
    case 'diff':
      return singleLinePreview(fileName(node.file) || node.file, 72);
    case 'thought':
      return singleLinePreview(node.text || '', 72);
    default:
      return '';
  }
}

/** Whether a node's status should read as still in flight. */
export function isNodeRunning(node: TimelineNode): boolean {
  return node.status === 'running';
}

/**
 * Turns a tool result into readable text for the code drawer.
 *
 * Engine output arrives in three shapes: plain text, a JSON string literal, or an object that was
 * stringified with escaped newlines. All three should read as plain text once expanded — printing
 * `"line1\\nline2"` in the drawer is the escaped form the user is trying to get away from.
 */
export function normaliseOutput(raw: string | undefined): string {
  if (!raw) return '';
  let text = raw.trim();
  if (!text) return '';

  if (text.startsWith('"') && text.endsWith('"') && text.length > 1) {
    try {
      const parsed = JSON.parse(text);
      if (typeof parsed === 'string') text = parsed;
    } catch {
      // not a JSON string literal, keep the raw text
    }
  }

  if (!text.includes('\n') && text.includes('\\n')) {
    text = text.replace(/\\r\\n|\\n/g, '\n').replace(/\\t/g, '\t').replace(/\\"/g, '"');
  }

  return text;
}

/** Formats a params bag for the drawer, returning '' when there is nothing beyond the command. */
export function prettyParams(params: Record<string, unknown> | undefined, omitKeys: string[] = []): string {
  if (!params) return '';

  const entries = Object.entries(params).filter(([key]) => !omitKeys.includes(key));
  if (entries.length === 0) return '';

  try {
    return JSON.stringify(Object.fromEntries(entries), null, 2);
  } catch {
    return '';
  }
}
