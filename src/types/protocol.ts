/**
 * AgentsHub Cross-Platform Sidecar Protocol
 * Defines bidirectional JSON-RPC and streaming contracts between C# .NET Host and Bun Sidecar Engine.
 */

export type AgentState =
  | 'idle'
  | 'thinking'
  | 'executing_tool'
  | 'streaming'
  | 'completed'
  | 'error'
  | 'aborted';

export type ToolType =
  | 'shell_exec'
  | 'file_read'
  | 'file_write'
  | 'file_patch'
  | 'dir_tree'
  | 'pty_session';

// --- Inbound Messages (Host -> Sidecar) ---

export interface BaseInboundRequest {
  id: string;
  action: string;
}

export interface AuthRequest extends BaseInboundRequest {
  action: 'auth';
  token: string;
}

export interface PingRequest extends BaseInboundRequest {
  action: 'ping';
}

export interface GetStatusRequest extends BaseInboundRequest {
  action: 'get_status';
}

export interface StartTaskRequest extends BaseInboundRequest {
  action: 'start_task';
  taskId: string;
  prompt: string;
  model?: string;
  profile?: string;
  workingDirectory?: string;
  environment?: Record<string, string>;
  toolsAllowed?: ToolType[];
}

export interface AbortTaskRequest extends BaseInboundRequest {
  action: 'abort_task';
  taskId: string;
  reason?: string;
}

export interface ExecuteToolRequest extends BaseInboundRequest {
  action: 'execute_tool';
  callId: string;
  taskId?: string;
  tool: ToolType;
  params: Record<string, any>;
  timeoutMs?: number;
}

export interface TerminalInputRequest extends BaseInboundRequest {
  action: 'terminal_input';
  sessionId: string;
  data: string;
}

export interface TerminalResizeRequest extends BaseInboundRequest {
  action: 'terminal_resize';
  sessionId: string;
  cols: number;
  rows: number;
}

export interface ShutdownRequest extends BaseInboundRequest {
  action: 'shutdown';
  force?: boolean;
}

export interface SwitchAccountRequest extends BaseInboundRequest {
  action: 'switch_account';
  profile: string;
  provider?: string;
}

export interface ListAccountsRequest extends BaseInboundRequest {
  action: 'list_accounts';
}

export interface RefreshTokenRequest extends BaseInboundRequest {
  action: 'refresh_token';
  profile: string;
}

export interface SendChatRequest extends BaseInboundRequest {
  action: 'send_chat';
  sessionId: string;
  prompt: string;
  model?: string;
  profile?: string;
  workingDirectory?: string;
}

export type InboundMessage =
  | AuthRequest
  | PingRequest
  | GetStatusRequest
  | StartTaskRequest
  | AbortTaskRequest
  | ExecuteToolRequest
  | TerminalInputRequest
  | TerminalResizeRequest
  | ShutdownRequest
  | SwitchAccountRequest
  | ListAccountsRequest
  | RefreshTokenRequest
  | SendChatRequest;

// --- Outbound Messages (Sidecar -> Host) ---

export interface ServerReadyEvent {
  event: 'server_ready';
  port: number;
  pid: number;
  platform: 'windows' | 'darwin' | 'linux';
  arch: string;
  version: string;
  timestamp: string;
}

export interface PongEvent {
  event: 'pong';
  replyTo?: string;
  pid: number;
  activeSessionsCount: number;
  port?: number;
  uptimeSec?: number;
  timestamp: string;
}

export interface SidecarStatusEvent {
  event: 'sidecar_status';
  status: string;
  port: number;
  pid: number;
  activeSessionsCount: number;
  uptimeSec: number;
  version: string;
  timestamp: string;
}

export interface AgentStateEvent {
  event: 'agent_state';
  taskId?: string;
  state: AgentState;
  message?: string;
  timestamp: string;
}

export interface StreamDeltaEvent {
  event: 'stream_delta';
  taskId: string;
  text: string;
  index: number;
  timestamp: string;
}

export interface ChatTokenEvent {
  event: 'chat_token';
  sessionId: string;
  text: string;
  timestamp: string;
}

export interface SessionStatusEvent {
  event: 'session_status';
  sessionId: string;
  state: 'idle' | 'running' | 'tool_executing' | 'completed' | 'error';
  error?: string;
  timestamp: string;
}

export interface AccountProfileInfo {
  name: string;
  email?: string;
  active: boolean;
  provider?: string;
}

export interface AccountStateEvent {
  event: 'account_state';
  activeAccount: string;
  provider: string;
  profiles: AccountProfileInfo[];
  timestamp: string;
}

export interface ToolStartEvent {
  event: 'tool_start';
  taskId: string;
  callId: string;
  tool: ToolType;
  params: Record<string, any>;
  timestamp: string;
}

export interface ToolOutputEvent {
  event: 'tool_output';
  taskId: string;
  callId: string;
  tool: ToolType;
  success: boolean;
  result?: any;
  error?: string;
  exitCode?: number;
  durationMs: number;
  timestamp: string;
}

export interface TaskCompletedSummary {
  success: boolean;
  durationMs: number;
  tokensUsed?: number;
  stepsCount: number;
  error?: string;
  completedAt: string;
}

export interface TaskCompletedEvent {
  event: 'task_completed';
  taskId: string;
  summary: TaskCompletedSummary;
  timestamp: string;
}

export interface TerminalDataEvent {
  event: 'terminal_data';
  sessionId: string;
  data: string;
  timestamp: string;
}

export interface ErrorEvent {
  event: 'error';
  replyTo?: string;
  code: string;
  message: string;
  timestamp: string;
}

export type OutboundEvent =
  | ServerReadyEvent
  | PongEvent
  | SidecarStatusEvent
  | AgentStateEvent
  | StreamDeltaEvent
  | ChatTokenEvent
  | SessionStatusEvent
  | AccountStateEvent
  | ToolStartEvent
  | ToolOutputEvent
  | TaskCompletedEvent
  | TerminalDataEvent
  | ErrorEvent;
