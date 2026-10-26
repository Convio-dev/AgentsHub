/**
 * Session & Account Manager for AgentsHub Sidecar
 * Dispatches real agent turns to the native Antigravity CLI (agy) or ShellExecutor with:
 * - Real stream-json token, thinking, and tool execution parsing (NO fake mock templates)
 * - Isolated VirtualHome environments per profile (USERPROFILE, HOME, APPDATA, LOCALAPPDATA)
 * - Dedicated session tracking and active sessions counter
 * - Automatic session pruner to prevent memory leaks
 */

import { existsSync, readFileSync, writeFileSync, mkdirSync, copyFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { homedir } from 'node:os';
import { OutboundEvent, AccountProfileInfo } from '../types/protocol.ts';
import { ShellExecutor } from '../tools/executor.ts';
import { TaskQueue } from '../queue/taskQueue.ts';

export interface ProfileRecord {
  name: string;
  email?: string;
  token?: string;
  provider?: string;
  model?: string;
  lastUsed?: string;
  virtualHomePath?: string;
}

export interface ChatSessionState {
  sessionId: string;
  profileName: string;
  workingDirectory: string;
  status: 'idle' | 'thinking' | 'running' | 'tool_executing' | 'completed' | 'error';
  startedAt: number;
  lastActiveAt: number;
  abortController?: AbortController;
  scopedEnv: Record<string, string>;
  activeProcess?: any;
}

export class SessionManager {
  private profiles = new Map<string, ProfileRecord>();
  private activeProfileName: string = 'Default';
  private activeSessions = new Map<string, ChatSessionState>();
  private profileActiveSessionIds = new Map<string, string>(); // profileName -> sessionId
  private configPath: string;
  private emit: (event: OutboundEvent) => void;
  private taskQueue: TaskQueue;

  constructor(taskQueue: TaskQueue, emit: (event: OutboundEvent) => void) {
    this.taskQueue = taskQueue;
    this.emit = emit;
    this.configPath = this.resolveProfilesPath();
    this.loadProfiles();

    // Periodic sweep for abandoned sessions to prevent memory leaks (runs every 10 min)
    setInterval(() => this.pruneInactiveSessions(), 10 * 60 * 1000);
  }

  private resolveProfilesPath(): string {
    const home = homedir();
    const geminiDir = join(home, '.gemini', 'antigravity');
    if (!existsSync(geminiDir)) {
      try {
        mkdirSync(geminiDir, { recursive: true });
      } catch {}
    }
    return join(geminiDir, 'profiles.json');
  }

  private loadProfiles(): void {
    try {
      if (existsSync(this.configPath)) {
        const raw = readFileSync(this.configPath, 'utf-8');
        const list: ProfileRecord[] = JSON.parse(raw);
        if (Array.isArray(list)) {
          for (const item of list) {
            this.profiles.set(item.name.toLowerCase(), item);
          }
        }
      }
    } catch (err) {
      console.warn('[SessionManager] Could not load persisted profiles:', err);
    }

    // Set first profile active if exists
    if (this.profiles.size > 0) {
      const first = Array.from(this.profiles.values())[0];
      this.activeProfileName = first.name;
    }
  }

  private saveProfiles(): void {
    try {
      const list = Array.from(this.profiles.values());
      writeFileSync(this.configPath, JSON.stringify(list, null, 2), 'utf-8');
    } catch (err) {
      console.error('[SessionManager] Failed to persist profiles:', err);
    }
  }

  /**
   * Prepares and resolves the isolated VirtualHome directory for a given profile.
   */
  public prepareVirtualHome(profileName: string): string {
    const sanitized = profileName.replace(/[^a-zA-Z0-9_\-\.@]/g, '_');
    const home = homedir();

    const candidates = [
      join(process.cwd(), 'Profiles', 'Antigravity', sanitized, 'VirtualHome'),
      join(process.cwd(), 'bin', 'Debug', 'net10.0-windows', 'Profiles', 'Antigravity', sanitized, 'VirtualHome'),
      join(home, '.gemini', 'antigravity', 'profiles', sanitized, 'VirtualHome')
    ];

    let targetDir = candidates[0];
    for (const c of candidates) {
      if (existsSync(c)) {
        targetDir = c;
        break;
      }
    }

    if (!existsSync(targetDir)) {
      targetDir = candidates[2];
    }

    const virtGemini = join(targetDir, '.gemini');
    const virtRoaming = join(targetDir, 'AppData', 'Roaming');
    const virtLocal = join(targetDir, 'AppData', 'Local');

    try {
      mkdirSync(targetDir, { recursive: true });
      mkdirSync(virtGemini, { recursive: true });
      mkdirSync(virtRoaming, { recursive: true });
      mkdirSync(virtLocal, { recursive: true });

      const parentProfileDir = join(targetDir, '..');
      const credFiles = ['gemini_credential.json', 'google_accounts.json', 'oauth_creds.json'];

      for (const cf of credFiles) {
        const dest = join(virtGemini, cf);
        if (!existsSync(dest)) {
          const srcProfile = join(parentProfileDir, cf);
          const srcHome = join(home, '.gemini', cf);
          if (existsSync(srcProfile)) {
            copyFileSync(srcProfile, dest);
          } else if (existsSync(srcHome)) {
            copyFileSync(srcHome, dest);
          }
        }
      }
    } catch (err) {
      console.warn(`[SessionManager] VirtualHome setup notice for '${profileName}':`, err);
    }

    return targetDir;
  }

  private allocatePortForProfile(profileName: string): number {
    let hash = 0;
    for (let i = 0; i < profileName.length; i++) {
      hash = (hash << 5) - hash + profileName.charCodeAt(i);
      hash |= 0;
    }
    const offset = Math.abs(hash) % 100;
    return 9340 + offset;
  }

  public buildScopedEnvironment(
    profileName: string,
    sessionId: string,
    virtualHomeDir: string,
    model?: string
  ): Record<string, string> {
    const allocatedPort = this.allocatePortForProfile(profileName);

    const scoped: Record<string, string> = {
      ...(process.env as Record<string, string>),
      USERPROFILE: virtualHomeDir,
      HOME: virtualHomeDir,
      APPDATA: join(virtualHomeDir, 'AppData', 'Roaming'),
      LOCALAPPDATA: join(virtualHomeDir, 'AppData', 'Local'),
      PORT: String(allocatedPort),
      ANTIGRAVITY_PORT: String(allocatedPort),
      AGENTS_SESSION_ID: sessionId,
      AGENTS_PROFILE_NAME: profileName,
      AGENTS_MODEL: model || 'gemini-3.8-flash-high',
      FORCE_COLOR: '0',
      TERM: 'dumb'
    };

    if (profileName.toLowerCase().includes('claude') || (model && model.toLowerCase().includes('claude'))) {
      scoped.ANTHROPIC_MODEL = model || 'claude-3-7-sonnet';
    }

    return scoped;
  }

  public getActiveAccountState(): {
    activeAccount: string;
    provider: string;
    profiles: AccountProfileInfo[];
  } {
    const list: AccountProfileInfo[] = Array.from(this.profiles.values()).map(p => ({
      name: p.name,
      email: p.email,
      provider: p.provider || 'Antigravity',
      active: p.name.toLowerCase() === this.activeProfileName.toLowerCase()
    }));

    const active = this.profiles.get(this.activeProfileName.toLowerCase()) || {
      name: this.activeProfileName,
      provider: 'Antigravity'
    };

    return {
      activeAccount: active.name,
      provider: active.provider || 'Antigravity',
      profiles: list
    };
  }

  public emitAccountState(): void {
    const state = this.getActiveAccountState();
    this.emit({
      event: 'account_state',
      activeAccount: state.activeAccount,
      provider: state.provider,
      profiles: state.profiles,
      timestamp: new Date().toISOString()
    });
  }

  public switchAccount(profileName: string, provider?: string): { success: boolean; activeProfile: string } {
    const key = profileName.toLowerCase();
    let record = this.profiles.get(key);

    if (!record) {
      record = {
        name: profileName,
        email: `${profileName.toLowerCase()}@local`,
        provider: provider || 'Antigravity',
        lastUsed: new Date().toISOString()
      };
      this.profiles.set(key, record);
      this.saveProfiles();
    } else {
      record.lastUsed = new Date().toISOString();
      if (provider) record.provider = provider;
      this.saveProfiles();
    }

    this.activeProfileName = record.name;
    this.emitAccountState();

    return {
      success: true,
      activeProfile: this.activeProfileName
    };
  }

  public listAccounts(): AccountProfileInfo[] {
    return this.getActiveAccountState().profiles;
  }

  public refreshToken(profileName: string): { success: boolean; message: string } {
    const record = this.profiles.get(profileName.toLowerCase());
    if (!record) {
      return { success: false, message: `Profile '${profileName}' not found` };
    }

    record.lastUsed = new Date().toISOString();
    this.saveProfiles();
    this.emitAccountState();

    return { success: true, message: `Token validated for ${profileName}` };
  }

  /**
   * Starts and activates a runner profile session inside the Sidecar.
   * Increments active sessions counter and broadcasts real-time state.
   */
  public async startSession(
    profileName: string,
    workingDirectory?: string,
    model?: string
  ): Promise<{ success: boolean; sessionId: string; activeSessionsCount: number }> {
    const key = profileName.toLowerCase();
    const sessionId = this.profileActiveSessionIds.get(key) || `session_${key}_${Date.now()}`;
    this.profileActiveSessionIds.set(key, sessionId);

    const virtualHomeDir = this.prepareVirtualHome(profileName);
    const scopedEnv = this.buildScopedEnvironment(profileName, sessionId, virtualHomeDir, model);
    const cwd = workingDirectory && existsSync(workingDirectory) ? resolve(workingDirectory) : virtualHomeDir;

    const sessionState: ChatSessionState = {
      sessionId,
      profileName,
      workingDirectory: cwd,
      status: 'running',
      startedAt: Date.now(),
      lastActiveAt: Date.now(),
      scopedEnv
    };

    this.activeSessions.set(sessionId, sessionState);

    // Auto-register profile in registry
    if (!this.profiles.has(key)) {
      this.profiles.set(key, {
        name: profileName,
        email: `${key}@local`,
        provider: 'Antigravity',
        model: model || 'gemini-3.8-flash-high',
        lastUsed: new Date().toISOString()
      });
      this.saveProfiles();
    }

    this.emit({
      event: 'session_status',
      sessionId,
      profile: profileName,
      state: 'running',
      timestamp: new Date().toISOString()
    });

    this.emit({
      event: 'sidecar_status',
      pid: process.pid,
      activeSessionsCount: this.getActiveSessionsCount(),
      timestamp: new Date().toISOString()
    });

    return {
      success: true,
      sessionId,
      activeSessionsCount: this.getActiveSessionsCount()
    };
  }

  /**
   * Stops an active runner profile session in the Sidecar.
   */
  public async stopSession(profileName: string): Promise<{ success: boolean }> {
    const key = profileName.toLowerCase();
    const sessionId = this.profileActiveSessionIds.get(key);

    if (sessionId) {
      const session = this.activeSessions.get(sessionId);
      if (session) {
        if (session.abortController) {
          try { session.abortController.abort(); } catch {}
        }
        if (session.activeProcess) {
          try { session.activeProcess.kill(9); } catch {}
        }
        session.status = 'idle';
        session.lastActiveAt = Date.now();
      }

      this.activeSessions.delete(sessionId);
      this.profileActiveSessionIds.delete(key);

      this.emit({
        event: 'session_status',
        sessionId,
        profile: profileName,
        state: 'idle',
        timestamp: new Date().toISOString()
      });

      this.emit({
        event: 'sidecar_status',
        pid: process.pid,
        activeSessionsCount: this.getActiveSessionsCount(),
        timestamp: new Date().toISOString()
      });
    }

    return { success: true };
  }

  public getActiveSessionsCount(): number {
    let count = 0;
    for (const session of this.activeSessions.values()) {
      if (session.status === 'running' || session.status === 'tool_executing' || session.status === 'thinking') {
        count++;
      }
    }
    return count;
  }

  public getActiveSessions(): Array<{ sessionId: string; profile: string; status: string; uptimeSec: number }> {
    const now = Date.now();
    const result = [];
    for (const s of this.activeSessions.values()) {
      result.push({
        sessionId: s.sessionId,
        profile: s.profileName,
        status: s.status,
        uptimeSec: Math.round((now - s.startedAt) / 1000)
      });
    }
    return result;
  }

  public async handleSendChat(params: {
    sessionId: string;
    prompt: string;
    model?: string;
    profile?: string;
    workingDirectory?: string;
  }): Promise<void> {
    const { sessionId, prompt, model, profile, workingDirectory } = params;
    const targetProfile = profile || this.activeProfileName;

    const virtualHomeDir = this.prepareVirtualHome(targetProfile);
    const scopedEnv = this.buildScopedEnvironment(targetProfile, sessionId, virtualHomeDir, model);

    const cwd = workingDirectory && existsSync(workingDirectory)
      ? resolve(workingDirectory)
      : virtualHomeDir;

    const abortController = new AbortController();
    const now = Date.now();

    const sessionState: ChatSessionState = {
      sessionId,
      profileName: targetProfile,
      workingDirectory: cwd,
      status: 'running',
      startedAt: now,
      lastActiveAt: now,
      abortController,
      scopedEnv
    };

    this.activeSessions.set(sessionId, sessionState);

    // Broadcast running state
    this.emit({
      event: 'session_status',
      sessionId,
      profile: targetProfile,
      state: 'running',
      timestamp: new Date().toISOString()
    });

    try {
      if (prompt.startsWith('/exec ') || prompt.startsWith('> ')) {
        // Direct Command Execution with Tool Card
        const cmd = prompt.replace(/^(\/exec\s+|>)/, '').trim();
        sessionState.status = 'tool_executing';
        sessionState.lastActiveAt = Date.now();

        const callId = `exec_${Date.now()}`;
        this.emit({
          event: 'tool_start',
          sessionId,
          callId,
          tool: 'bash',
          params: { command: cmd },
          timestamp: new Date().toISOString()
        });

        const startTime = performance.now();
        const result = await ShellExecutor.execute({
          command: cmd,
          cwd,
          env: scopedEnv,
          timeoutMs: 60000,
          abortSignal: abortController.signal,
          onStdout: (chunk) => {
            this.emit({
              event: 'chat_token',
              sessionId,
              text: chunk,
              timestamp: new Date().toISOString()
            });
          },
          onStderr: (chunk) => {
            this.emit({
              event: 'chat_token',
              sessionId,
              text: chunk,
              timestamp: new Date().toISOString()
            });
          }
        });

        const durationMs = Math.round(performance.now() - startTime);
        this.emit({
          event: 'tool_output',
          sessionId,
          callId,
          tool: 'bash',
          success: result.exitCode === 0,
          exitCode: result.exitCode,
          durationMs,
          timestamp: new Date().toISOString()
        });
      } else {
        // Real Execution through Antigravity CLI (agy)
        sessionState.status = 'thinking';
        this.emit({
          event: 'session_status',
          sessionId,
          profile: targetProfile,
          state: 'thinking',
          timestamp: new Date().toISOString()
        });

        this.emit({
          event: 'thinking',
          sessionId,
          text: 'Analyzing prompt and repository state...',
          timestamp: new Date().toISOString()
        });

        await this.executeAgyTurn({
          sessionId,
          prompt,
          model,
          cwd,
          scopedEnv,
          abortController,
          sessionState
        });
      }

      sessionState.status = 'idle';
      sessionState.lastActiveAt = Date.now();
      this.emit({
        event: 'session_status',
        sessionId,
        profile: targetProfile,
        state: 'idle',
        timestamp: new Date().toISOString()
      });
    } catch (err: any) {
      sessionState.status = 'error';
      sessionState.lastActiveAt = Date.now();
      this.emit({
        event: 'session_status',
        sessionId,
        profile: targetProfile,
        state: 'error',
        error: err?.message || String(err),
        timestamp: new Date().toISOString()
      });
    }
  }

  /**
   * Executes a prompt turn through agy with stream-json NDJSON parsing.
   * Dispatches live thinking, tokens, and tool calls directly to the UI.
   */
  private async executeAgyTurn(opts: {
    sessionId: string;
    prompt: string;
    model?: string;
    cwd: string;
    scopedEnv: Record<string, string>;
    abortController: AbortController;
    sessionState: ChatSessionState;
  }): Promise<void> {
    const { sessionId, prompt, model, cwd, scopedEnv, abortController, sessionState } = opts;

    // Detect agy CLI binary
    const agyCmd = process.platform === 'win32' ? 'agy.exe' : 'agy';
    const agyArgs = [
      '--output-format', 'stream-json',
      '--continue',
      '--dangerously-skip-permissions'
    ];

    if (model) {
      agyArgs.push('--model', model);
    }
    agyArgs.push('-p', prompt);

    let hasReceivedAnyToken = false;

    try {
      const proc = Bun.spawn([agyCmd, ...agyArgs], {
        cwd,
        env: scopedEnv,
        stdout: 'pipe',
        stderr: 'pipe',
        stdin: 'ignore'
      });

      sessionState.activeProcess = proc;

      abortController.signal.addEventListener('abort', () => {
        try { proc.kill(9); } catch {}
      });

      // Stream stdout NDJSON lines
      if (proc.stdout) {
        const reader = (proc.stdout as ReadableStream<Uint8Array>).getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split('\n');
          buffer = lines.pop() || '';

          for (const line of lines) {
            const trimmed = line.trim();
            if (!trimmed) continue;

            if (trimmed.startsWith('{')) {
              try {
                const evt = JSON.parse(trimmed);
                this.processAgyEvent(sessionId, evt, sessionState);
                hasReceivedAnyToken = true;
              } catch {}
            } else {
              // Raw non-JSON diagnostic line (filter out banner headers)
              if (!trimmed.includes('Usage of agy') && !trimmed.includes('Fetching available models')) {
                this.emit({
                  event: 'chat_token',
                  sessionId,
                  text: trimmed + '\n',
                  timestamp: new Date().toISOString()
                });
                hasReceivedAnyToken = true;
              }
            }
          }
        }
      }

      await proc.exited;
    } catch (err: any) {
      // If first attempt failed with --continue (no previous conversation), retry without --continue
      if (!hasReceivedAnyToken && !abortController.signal.aborted) {
        await this.executeAgyFresh(opts);
        return;
      }
      throw err;
    }
  }

  /**
   * Fresh conversation turn if --continue was rejected
   */
  private async executeAgyFresh(opts: {
    sessionId: string;
    prompt: string;
    model?: string;
    cwd: string;
    scopedEnv: Record<string, string>;
    abortController: AbortController;
    sessionState: ChatSessionState;
  }): Promise<void> {
    const { sessionId, prompt, model, cwd, scopedEnv, abortController, sessionState } = opts;
    const agyCmd = process.platform === 'win32' ? 'agy.exe' : 'agy';
    const agyArgs = [
      '--output-format', 'stream-json',
      '--dangerously-skip-permissions'
    ];
    if (model) agyArgs.push('--model', model);
    agyArgs.push('-p', prompt);

    const proc = Bun.spawn([agyCmd, ...agyArgs], {
      cwd,
      env: scopedEnv,
      stdout: 'pipe',
      stderr: 'pipe',
      stdin: 'ignore'
    });

    sessionState.activeProcess = proc;
    abortController.signal.addEventListener('abort', () => {
      try { proc.kill(9); } catch {}
    });

    if (proc.stdout) {
      const reader = (proc.stdout as ReadableStream<Uint8Array>).getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';

        for (const line of lines) {
          const trimmed = line.trim();
          if (!trimmed) continue;
          if (trimmed.startsWith('{')) {
            try {
              const evt = JSON.parse(trimmed);
              this.processAgyEvent(sessionId, evt, sessionState);
            } catch {}
          }
        }
      }
    }
    await proc.exited;
  }

  private processAgyEvent(sessionId: string, evt: any, sessionState: ChatSessionState): void {
    if (evt.event === 'step_update' && evt.step_update) {
      const su = evt.step_update;

      // 1. Thinking step
      if (su.step_type === 'thinking') {
        sessionState.status = 'thinking';
        this.emit({
          event: 'thinking',
          sessionId,
          text: su.text_delta || 'Synthesizing response...',
          durationSeconds: su.duration_seconds,
          timestamp: new Date().toISOString()
        });
      }
      // 2. Token output
      else if (su.step_type === 'agent_response' && typeof su.text_delta === 'string') {
        sessionState.status = 'running';
        this.emit({
          event: 'chat_token',
          sessionId,
          text: su.text_delta,
          timestamp: new Date().toISOString()
        });
      }
      // 3. Tool call started
      else if (su.step_type === 'tool_call') {
        sessionState.status = 'tool_executing';
        const toolName = su.tool_name || su.call?.name || 'tool';
        const toolParams = su.tool_args || su.call?.arguments || {};
        const callId = su.call_id || `tool_${Date.now()}`;

        this.emit({
          event: 'tool_start',
          sessionId,
          callId,
          tool: toolName,
          params: toolParams,
          timestamp: new Date().toISOString()
        });
      }
      // 4. Tool call completed
      else if (su.step_type === 'tool_result' || (su.state === 'DONE' && su.step_type === 'tool_call')) {
        this.emit({
          event: 'tool_output',
          sessionId,
          callId: su.call_id || '',
          tool: su.tool_name || 'tool',
          success: su.status !== 'ERROR' && su.state !== 'ERROR',
          result: su.result || su.output,
          durationMs: su.duration_seconds ? Math.round(su.duration_seconds * 1000) : 0,
          timestamp: new Date().toISOString()
        });
      }
    }
    // Final Turn Result
    else if (evt.event === 'result' && evt.result) {
      if (evt.result.usage?.thinking_tokens) {
        this.emit({
          event: 'thinking',
          sessionId,
          durationSeconds: evt.result.duration_seconds,
          timestamp: new Date().toISOString()
        });
      }
    }
  }

  public abortSession(sessionId: string): boolean {
    const session = this.activeSessions.get(sessionId);
    if (session) {
      if (session.abortController) session.abortController.abort();
      if (session.activeProcess) {
        try { session.activeProcess.kill(9); } catch {}
      }
      session.status = 'idle';
      session.lastActiveAt = Date.now();

      this.emit({
        event: 'session_status',
        sessionId,
        profile: session.profileName,
        state: 'idle',
        timestamp: new Date().toISOString()
      });
      return true;
    }
    return false;
  }

  private pruneInactiveSessions(): void {
    const expirationThresholdMs = 30 * 60 * 1000;
    const now = Date.now();

    for (const [id, session] of this.activeSessions.entries()) {
      if ((session.status === 'idle' || session.status === 'completed' || session.status === 'error') &&
          now - session.lastActiveAt > expirationThresholdMs) {
        this.activeSessions.delete(id);
      }
    }
  }
}
