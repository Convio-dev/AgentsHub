/**
 * Derives the collapsed stepper hierarchy from the flat node list the engine streams.
 *
 * The sidecar emits one event per tool call, so the hierarchy (`Ran 3 commands`, `Explored 2 files`)
 * is a presentation concern, not a transport one. Keeping the fold here means it is a pure function:
 * same nodes in, same rows out, no React state, trivially testable.
 */

import { ActionGroupTimelineNode, FileTimelineNode, GroupKind, TimelineNode } from '../types/agent';

export interface GroupCounts {
  commands: number;
  files: number;
  searches: number;
  edits: number;
}

export type TimelineRow =
  | { kind: 'node'; key: string; node: TimelineNode }
  | {
      kind: 'group';
      key: string;
      groupKind: GroupKind;
      nodes: TimelineNode[];
      label: string;
      isRunning: boolean;
      durationMs: number;
      counts: GroupCounts;
    };

/** A group needs at least this many children to be worth collapsing; a lone call reads better inline. */
const MIN_GROUP_SIZE = 2;

function bucketOf(node: TimelineNode): GroupKind | null {
  switch (node.type) {
    case 'command':
      return 'commands';
    case 'file':
      return 'explore';
    case 'diff':
      return 'edits';
    default:
      return null;
  }
}

function emptyCounts(): GroupCounts {
  return { commands: 0, files: 0, searches: 0, edits: 0 };
}

function countNode(counts: GroupCounts, node: TimelineNode): void {
  switch (node.type) {
    case 'command':
      counts.commands += 1;
      break;
    case 'file': {
      const fileNode = node as FileTimelineNode;
      if (fileNode.action === 'search') counts.searches += 1;
      else counts.files += 1;
      break;
    }
    case 'diff':
      counts.edits += 1;
      break;
    default:
      break;
  }
}

function nodeDurationMs(node: TimelineNode): number {
  if (node.type === 'command') return node.durationMs || 0;
  if (node.type === 'thought') return (node.durationSeconds || 0) * 1000;
  return 0;
}

/** Total time attributed to a run of nodes: summed durations when known, else wall-clock span. */
function measureSpan(nodes: TimelineNode[]): { durationMs: number; startedAt: number; endedAt: number } {
  if (nodes.length === 0) return { durationMs: 0, startedAt: 0, endedAt: 0 };

  const startedAt = Math.min(...nodes.map(n => n.timestamp));
  const summed = nodes.reduce((total, node) => total + nodeDurationMs(node), 0);

  const lastEnd = Math.max(
    ...nodes.map(n => n.timestamp + nodeDurationMs(n)),
    ...(nodes.some(n => n.status === 'running') ? [Date.now()] : [])
  );

  return {
    durationMs: summed > 0 ? summed : Math.max(0, lastEnd - startedAt),
    startedAt,
    endedAt: lastEnd,
  };
}

function plural(count: number, singular: string, pluralForm = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : pluralForm}`;
}

function labelFor(groupKind: GroupKind, counts: GroupCounts): string {
  switch (groupKind) {
    case 'commands':
      return `Ran ${plural(counts.commands, 'command')}`;
    case 'edits':
      return `Edited ${plural(counts.edits, 'file')}`;
    case 'explore': {
      const parts: string[] = [];
      if (counts.files > 0) parts.push(plural(counts.files, 'file'));
      if (counts.searches > 0) parts.push(plural(counts.searches, 'search', 'searches'));
      return `Explored ${parts.join(', ') || 'files'}`;
    }
    default:
      return 'Worked';
  }
}

function flushGroup(
  groupKind: GroupKind,
  nodes: TimelineNode[],
  index: number
): TimelineRow | null {
  if (nodes.length === 0) return null;

  if (nodes.length < MIN_GROUP_SIZE) {
    return { kind: 'node', key: nodes[0].id, node: nodes[0] };
  }

  const counts = emptyCounts();
  nodes.forEach(node => countNode(counts, node));
  const { durationMs } = measureSpan(nodes);

  return {
    kind: 'group',
    key: `group_${groupKind}_${nodes[0].id}_${index}`,
    groupKind,
    nodes,
    label: labelFor(groupKind, counts),
    isRunning: nodes.some(n => n.status === 'running'),
    durationMs,
    counts,
  };
}

/** Folds a flat node list into collapsible rows. Consecutive nodes of one kind share a row. */
export function buildTimelineRows(nodes: TimelineNode[]): TimelineRow[] {
  const rows: TimelineRow[] = [];

  let bucket: GroupKind | null = null;
  let pending: TimelineNode[] = [];

  const flush = () => {
    if (!bucket) return;
    const row = flushGroup(bucket, pending, rows.length);
    if (row) rows.push(row);
    pending = [];
    bucket = null;
  };

  for (const node of nodes) {
    // A node that already carries children (restored from storage) is its own row.
    if (node.type === 'group') {
      flush();
      const groupNode = node as ActionGroupTimelineNode;
      const counts = emptyCounts();
      groupNode.children.forEach(child => countNode(counts, child));
      const { durationMs } = measureSpan(groupNode.children);
      rows.push({
        kind: 'group',
        key: groupNode.id,
        groupKind: groupNode.groupKind || 'mixed',
        nodes: groupNode.children,
        label: groupNode.title,
        isRunning: groupNode.status === 'running',
        durationMs: groupNode.durationSeconds ? groupNode.durationSeconds * 1000 : durationMs,
        counts,
      });
      continue;
    }

    const nextBucket = bucketOf(node);
    if (!nextBucket) {
      flush();
      rows.push({ kind: 'node', key: node.id, node });
      continue;
    }

    if (bucket && nextBucket !== bucket) flush();
    bucket = nextBucket;
    pending.push(node);
  }

  flush();
  return rows;
}

export interface RunSummary {
  durationMs: number;
  isRunning: boolean;
  startedAt: number;
  stepCount: number;
}

/**
 * Wall-clock summary of an entire assistant turn, used for the `Worked for 33m` header. Only worth
 * showing once a turn has actually taken a while, so the caller decides with `shouldShowRunHeader`.
 */
export function summariseRun(nodes: TimelineNode[]): RunSummary {
  const { durationMs, startedAt, endedAt } = measureSpan(nodes);
  const isRunning = nodes.some(n => n.status === 'running');
  const live = isRunning ? Math.max(durationMs, Date.now() - startedAt) : durationMs;

  return {
    durationMs: live,
    isRunning,
    startedAt,
    stepCount: nodes.filter(n => n.type !== 'thought').length,
  };
}

/** Below this the header is noise: most turns finish in a few seconds. */
const RUN_HEADER_THRESHOLD_MS = 60_000;

export function shouldShowRunHeader(summary: RunSummary): boolean {
  return summary.durationMs >= RUN_HEADER_THRESHOLD_MS || (summary.isRunning && summary.durationMs >= 20_000);
}

/** Total collapsed rows a turn will render, used to keep the stepper from dominating the message. */
export function countRows(nodes: TimelineNode[]): number {
  return buildTimelineRows(nodes).length;
}
