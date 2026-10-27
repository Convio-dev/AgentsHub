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

export interface BaseTimelineNode {
  id: string;
  type: TimelineNodeType;
  timestamp: number;
  status: 'running' | 'completed' | 'failed';
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
  command: string;
  exitCode?: number;
  output?: string;
  durationMs?: number;
}

export interface FileTimelineNode extends BaseTimelineNode {
  type: 'file';
  path: string;
  action: 'read' | 'explored' | 'list';
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
  durationSeconds?: number;
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
}

export interface SessionRecord {
  id: string;
  title: string;
  isTitleCustom: boolean;
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
