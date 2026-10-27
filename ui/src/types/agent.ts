/**
 * Agent & Timeline TypeScript Contracts for AgentsHub Modern SPA
 */

export type AgentExecutionState =
  | 'idle'
  | 'thinking'
  | 'running'
  | 'tool_executing'
  | 'completed'
  | 'error'
  | 'aborted';

export type TimelineNodeType = 'thought' | 'command' | 'file' | 'diff' | 'group' | 'custom';

/**
 * Semantic class of a tool invocation. The sidecar and the store both normalise to this so the
 * stepper never has to pattern-match on human-readable tool labels ("Ran", "Explored"), which is
 * what previously leaked raw JSON payloads into command rows.
 */
export type ToolKind = 'command' | 'read' | 'search' | 'write' | 'other';

/** How a run of consecutive nodes is summarised in the collapsed stepper row. */
export type GroupKind = 'commands' | 'explore' | 'edits' | 'thinking' | 'mixed';

export interface BaseTimelineNode {
  id: string;
  type: TimelineNodeType;
  timestamp: number;
  status: 'running' | 'completed' | 'failed';
  /** Raw tool name as reported by the engine, kept for the drawer header. */
  toolName?: string;
  toolKind?: ToolKind;
}

export interface ThoughtTimelineNode extends BaseTimelineNode {
  type: 'thought';
  text: string;
  durationSeconds?: number;
  tokens?: number;
  isStreaming?: boolean;
}

export interface CommandTimelineNode extends BaseTimelineNode {
  type: 'command';
  /** Clean, single-line human-readable command (never a stringified JSON payload). */
  command: string;
  exitCode?: number;
  /** Merged stdout+stderr, kept for backwards compatibility with persisted sessions. */
  output?: string;
  stdout?: string;
  stderr?: string;
  params?: Record<string, unknown>;
  durationMs?: number;
}

export interface FileTimelineNode extends BaseTimelineNode {
  type: 'file';
  path: string;
  action: 'read' | 'explored' | 'list' | 'search';
  count?: number;
  contentPreview?: string;
}

export interface DiffTimelineNode extends BaseTimelineNode {
  type: 'diff';
  file: string;
  additions: number;
  deletions: number;
  patch?: string;
}

export interface ActionGroupTimelineNode extends BaseTimelineNode {
  type: 'group';
  title: string;
  summary: string;
  groupKind?: GroupKind;
  durationSeconds?: number;
  startedAt?: number;
  endedAt?: number;
  children: TimelineNode[];
}

export type TimelineNode =
  | ThoughtTimelineNode
  | CommandTimelineNode
  | FileTimelineNode
  | DiffTimelineNode
  | ActionGroupTimelineNode;

export interface TokenUsage {
  inputTokens: number;
  outputTokens: number;
  thinkingTokens: number;
  totalTokens: number;
}

export interface ChatAttachment {
  id: string;
  name: string;
  size: number;
  type?: string;
  isImage: boolean;
  dataUrl?: string;
}

export interface ChatMessage {
  id: string;
  sessionId: string;
  sender: 'user' | 'assistant' | 'system';
  prompt?: string;
  attachments?: ChatAttachment[];
  markdown?: string;
  timeline: TimelineNode[];
  status: AgentExecutionState;
  error?: string;
  profile?: string;
  timestamp: number;
  /** 1-based conversation turn this message belongs to. Used to gate title generation. */
  turn?: number;
  /** Set when this message was created by editing an earlier prompt (conversation fork). */
  forkedFrom?: string;
}

export interface SessionRecord {
  id: string;
  title: string;
  isTitleCustom: boolean;
  /**
   * Latched once the first prompt has produced a title. While true no later turn may rewrite
   * `title`; only an explicit user rename (isTitleCustom) can change it.
   */
  titleLocked?: boolean;
  isPinned: boolean;
  createdAt: number;
  updatedAt: number;
  profile: string;
  model: string;
  reasoningEffort?: string;
  quotaPercent?: number;
  tokens: TokenUsage;
  status: AgentExecutionState;
  pid?: number;
  messages: ChatMessage[];
  activeExecutionCardId?: string;
}

export type SlashCommandSource = 'builtin' | 'skill' | 'mcp' | 'profile';

export interface SlashCommand {
  /** Command body without the leading slash, e.g. `add-dir`. */
  name: string;
  description: string;
  source: SlashCommandSource;
  /** Engine that reported the command, or `*` when it applies to every engine. */
  engine?: string;
  /** Argument hint shown after the name, e.g. `<path>`. */
  hint?: string;
  /** Inserts `name + ' '` instead of sending immediately when true. */
  takesArgument?: boolean;
}

export interface RunnerProfile {
  name: string;
  email?: string;
  provider?: string;
  active: boolean;
  isRunning: boolean;
  quotaPercent?: number;
  port?: number;
  pid?: number;
}

export interface SidecarHealth {
  status: 'Online' | 'Offline' | 'Starting' | 'Connecting' | 'Error';
  port: number;
  pid: number;
  isOnline: boolean;
  latencyMs: number;
  activeSessions: number;
  version: string;
  error?: string;
}

export interface HardwareTelemetry {
  netKbps: number;
  netMbps: number;
  ramMb: number;
  totalRamMb: number;
  memoryUsedGb: number;
  memoryTotalGb: number;
  hermesMemMb: number;
}

export type ActiveView =
  | 'chat'
  | 'dashboard'
  | 'terminals'
  | 'repositories'
  | 'profiles'
  | 'mcpskills'
  | 'devtools'
  | 'lan'
  | 'settings';

export interface ProjectWorkspace {
  id: string;
  name: string;
  workingDirectory: string;
  path?: string;
  accentColor?: string;
  colorTag?: string;
  isDefault?: boolean;
  antigravityProfile?: string;
  primaryCli?: string;
  secondaryCli?: string;
  splitMode?: 'single' | 'horizontal' | 'vertical';
  splitRatio?: string;
  environmentVariables?: Record<string, string>;
}

export interface WorkspaceFileItem {
  name: string;
  path: string;
  isDirectory: boolean;
  extension: string;
}

export interface BackendSessionItem {
  sessionId: string;
  goal?: string;
  profile?: string;
  transcriptPath?: string;
  stepCount?: number;
  lastActiveUtc?: string;
}
