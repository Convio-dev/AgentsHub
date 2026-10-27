/**
 * Repair pass for timeline nodes restored from localStorage.
 *
 * Sessions persisted before the stepper rework hold command rows whose text is a stringified params
 * bag (`Ran {"CommandLine":"Get-ChildItem …"}`) and carry no `toolKind`. Those rows would keep
 * rendering the old way forever, so they are normalised once on load instead of being invalidated.
 */

import { TimelineNode, ToolKind } from '../types/agent';
import { extractCommand } from './toolMeta';

function looksLikeJsonPayload(text: string): boolean {
  const trimmed = text.trim();
  return trimmed.startsWith('{') && trimmed.endsWith('}');
}

function recoverParams(text: string): Record<string, unknown> | null {
  const trimmed = text.trim();
  const braceIndex = trimmed.indexOf('{');
  if (braceIndex === -1) return null;

  try {
    const parsed = JSON.parse(trimmed.slice(braceIndex));
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

function kindForNode(node: TimelineNode): ToolKind {
  switch (node.type) {
    case 'command':
      return 'command';
    case 'file':
      return node.action === 'search' ? 'search' : 'read';
    case 'diff':
      return 'write';
    default:
      return 'other';
  }
}

export function migrateTimelineNode(node: TimelineNode): TimelineNode {
  if (node.type === 'group') {
    return { ...node, children: node.children.map(migrateTimelineNode) };
  }

  const toolKind = node.toolKind || kindForNode(node);

  if (node.type === 'command') {
    // A persisted row may carry `Ran {"CommandLine":"…"}` in `command`, or keep the payload only in
    // params. Rebuild a real command string from whichever source still has it.
    let command = node.command || '';
    let params = node.params;

    if (looksLikeJsonPayload(command)) {
      const recovered = recoverParams(command);
      if (recovered) {
        params = { ...recovered, ...(params || {}) };
        command = extractCommand(params) || command.replace(/^\S+\s*/, '');
      }
    }

    if (!command || looksLikeJsonPayload(command)) {
      command = extractCommand(params) || node.toolName || 'command';
    }

    return { ...node, command, params, toolKind };
  }

  if (node.type === 'file') {
    return { ...node, toolKind };
  }

  return { ...node, toolKind };
}

export function migrateTimelineNodes(nodes: TimelineNode[] | undefined): TimelineNode[] {
  if (!Array.isArray(nodes)) return [];
  return nodes.map(migrateTimelineNode);
}
