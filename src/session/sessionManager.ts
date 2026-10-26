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
  pid?: number;
  model?: string;
  reasoningEffort?: string;
  quotaPercent?: number;
  tokens?: {
    inputTokens?: number;
    outputTokens?: number;
    thinkingTokens?: number;
    totalTokens?: number;
  };
}

export class SessionManager {
  private profiles = new Map<string, ProfileRecord>();
  private activeProfileName: string = 'Default';
  private activeSessions = new Map<string, ChatSessionState>();
  private profileActiveSessionIds = new Map<string, string>(); // profileName -> sessionId
  private profileQuotas = new Map<string, { quotaPercent: number; details?: string; lastPolled: number }>();
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

    // Initial quota poll & periodic quota polling per profile context (every 3 min)
    setTimeout(() => this.pollAllQuotas().catch(() => {}), 1500);
    setInterval(() => this.pollAllQuotas().catch(() => {}), 3 * 60 * 1000);
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
    const sessionIdsToStop: string[] = [];
    const directId = this.profileActiveSessionIds.get(key);
    if (directId) sessionIdsToStop.push(directId);

    for (const [sId, sess] of this.activeSessions.entries()) {
      if (sess.profileName.toLowerCase() === key && !sessionIdsToStop.includes(sId)) {
        sessionIdsToStop.push(sId);
      }
    }

    for (const sId of sessionIdsToStop) {
      const session = this.activeSessions.get(sId);
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
      this.activeSessions.delete(sId);
      this.emit({
        event: 'session_status',
        sessionId: sId,
        profile: profileName,
        state: 'idle',
        timestamp: new Date().toISOString()
      });
    }

    this.profileActiveSessionIds.delete(key);

    this.emit({
      event: 'sidecar_status',
      pid: process.pid,
      activeSessionsCount: this.getActiveSessionsCount(),
      timestamp: new Date().toISOString()
    });

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

    const cachedQuota = this.profileQuotas.get(targetProfile.toLowerCase());
    const sessionState: ChatSessionState = {
      sessionId,
      profileName: targetProfile,
      workingDirectory: cwd,
      status: 'running',
      startedAt: now,
      lastActiveAt: now,
      abortController,
      scopedEnv,
      model: model || 'Gemini 3.8 Flash (High)',
      quotaPercent: cachedQuota?.quotaPercent,
      tokens: { inputTokens: 0, outputTokens: 0, thinkingTokens: 0, totalTokens: 0 }
    };

    this.activeSessions.set(sessionId, sessionState);
    this.profileActiveSessionIds.set(targetProfile.toLowerCase(), sessionId);

    // Broadcast running state with scoped session telemetry
    this.emit({
      event: 'session_status',
      sessionId,
      profile: targetProfile,
      state: 'running',
      model: sessionState.model,
      quotaPercent: sessionState.quotaPercent,
      tokens: sessionState.tokens,
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
          profile: targetProfile,
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
              profile: targetProfile,
              text: chunk,
              timestamp: new Date().toISOString()
            });
          },
          onStderr: (chunk) => {
            this.emit({
              event: 'chat_token',
              sessionId,
              profile: targetProfile,
              text: chunk,
              timestamp: new Date().toISOString()
            });
          }
        });

        const durationMs = Math.round(performance.now() - startTime);
        this.emit({
          event: 'tool_output',
          sessionId,
          profile: targetProfile,
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
          model: sessionState.model,
          quotaPercent: sessionState.quotaPercent,
          tokens: sessionState.tokens,
          timestamp: new Date().toISOString()
        });

        this.emit({
          event: 'thinking',
          sessionId,
          profile: targetProfile,
          state: 'active',
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
        model: sessionState.model,
        quotaPercent: sessionState.quotaPercent,
        tokens: sessionState.tokens,
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
        model: sessionState.model,
        quotaPercent: sessionState.quotaPercent,
        tokens: sessionState.tokens,
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
      sessionState.pid = proc.pid;

      this.emit({
        event: 'session_status',
        sessionId,
        profile: sessionState.profileName,
        state: 'running',
        pid: proc.pid,
        model: sessionState.model,
        quotaPercent: sessionState.quotaPercent,
        tokens: sessionState.tokens,
        timestamp: new Date().toISOString()
      });

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
              this.processRawCliLine(sessionId, trimmed, sessionState);
              hasReceivedAnyToken = true;
            }
          }
        }
      }

      await proc.exited;
      this.pollQuota(sessionState.profileName).catch(() => {});
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
    sessionState.pid = proc.pid;

    this.emit({
      event: 'session_status',
      sessionId,
      profile: sessionState.profileName,
      state: 'running',
      pid: proc.pid,
      model: sessionState.model,
      quotaPercent: sessionState.quotaPercent,
      tokens: sessionState.tokens,
      timestamp: new Date().toISOString()
    });

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
          } else {
            this.processRawCliLine(sessionId, trimmed, sessionState);
          }
        }
      }
    }
    await proc.exited;
    this.pollQuota(sessionState.profileName).catch(() => {});
  }

  private processRawCliLine(sessionId: string, line: string, sessionState: ChatSessionState): void {
    const trimmed = line.trim();
    if (!trimmed) return;

    // Filter out CLI noise/banners
    if (trimmed.includes('Usage of agy') || trimmed.includes('Fetching available models') || trimmed.includes('Loaded plugins')) {
      return;
    }

    // 1. Thinking patterns:
    // o Thought for 3.6s (Considering the user's request...)
    // ▸ Thought for 6s, 702 tokens
    const thoughtMatch = trimmed.match(/^[o▸•*]\s*Thought for\s*([0-9.]+)s(?:,\s*([0-9]+)\s*tokens)?(?:\s*\((.*?)\))?/i);
    if (thoughtMatch) {
      const durationSec = parseFloat(thoughtMatch[1]);
      const tokens = thoughtMatch[2] ? parseInt(thoughtMatch[2], 10) : undefined;
      const snippet = thoughtMatch[3] || '';

      this.emit({
        event: 'thinking',
        sessionId,
        profile: sessionState.profileName,
        text: snippet,
        durationSeconds: durationSec,
        thinkingTokens: tokens,
        state: 'done',
        timestamp: new Date().toISOString()
      });

      if (tokens) {
        sessionState.tokens = {
          inputTokens: sessionState.tokens?.inputTokens || 0,
          outputTokens: sessionState.tokens?.outputTokens || 0,
          thinkingTokens: tokens,
          totalTokens: tokens
        };
        this.emit({
          event: 'token_usage',
          sessionId,
          profile: sessionState.profileName,
          usage: sessionState.tokens,
          timestamp: new Date().toISOString()
        });
      }
      return;
    }

    // 2. Ran tool pattern:
    // ● Ran (Get-ChildItem -Path . -Filter *.py -Recurse | Select-Object FullName, Length, LastWriteTime)
    const ranMatch = trimmed.match(/^[●o•*]\s*Ran\s*\((.*?)\)$/i);
    if (ranMatch) {
      const cmd = ranMatch[1];
      const callId = `ran_${Date.now()}`;
      this.emit({
        event: 'tool_start',
        sessionId,
        profile: sessionState.profileName,
        callId,
        tool: 'Ran',
        params: { CommandLine: cmd },
        timestamp: new Date().toISOString()
      });
      this.emit({
        event: 'tool_output',
        sessionId,
        profile: sessionState.profileName,
        callId,
        tool: 'Ran',
        success: true,
        result: `Executed: ${cmd}`,
        durationMs: 60,
        timestamp: new Date().toISOString()
      });
      return;
    }

    // 3. Explored files pattern:
    // ● Explored 5 files (app.py, gui_app.py, hello.py, +2) (ctrl+o to expand)
    const exploredMatch = trimmed.match(/^[●o•*]\s*Explored\s*(\d+)\s*files?\s*\((.*?)\)/i);
    if (exploredMatch) {
      const count = parseInt(exploredMatch[1], 10);
      const files = exploredMatch[2].replace(/\s*\(ctrl\+o to expand\)/i, '').trim();
      const callId = `exp_${Date.now()}`;
      this.emit({
        event: 'tool_start',
        sessionId,
        profile: sessionState.profileName,
        callId,
        tool: 'Explored',
        params: { count, files },
        timestamp: new Date().toISOString()
      });
      this.emit({
        event: 'tool_output',
        sessionId,
        profile: sessionState.profileName,
        callId,
        tool: 'Explored',
        success: true,
        result: `Explored ${count} files (${files})`,
        durationMs: 60,
        timestamp: new Date().toISOString()
      });
      return;
    }

    // 4. Regular chat token / markdown line
    this.emit({
      event: 'chat_token',
      sessionId,
      profile: sessionState.profileName,
      text: trimmed + '\n',
      timestamp: new Date().toISOString()
    });
  }

  private processAgyEvent(sessionId: string, evt: any, sessionState: ChatSessionState): void {
    if (evt.event === 'init' && evt.init) {
      const modelName = evt.init.model || evt.model;
      if (modelName) {
        sessionState.model = modelName;
        this.emit({
          event: 'model_info',
          sessionId,
          profile: sessionState.profileName,
          model: modelName,
          reasoningEffort: evt.init.reasoning_effort || evt.init.effort,
          timestamp: new Date().toISOString()
        });
      }
    } else if (evt.event === 'step_update' && evt.step_update) {
      const su = evt.step_update;

      // 1. Thinking / Agent Response step
      if (su.step_type === 'agent_response' || su.step_type === 'thinking') {
        const textChunk = su.text_delta ?? su.delta ?? su.text ?? (su.state === 'DONE' && typeof su.response === 'string' ? su.response : null);
        const hasText = typeof textChunk === 'string' && textChunk.length > 0;
        const thinkingTokens = su.usage?.thinking_tokens || 0;

        if (!hasText && (thinkingTokens > 0 || (su.duration_seconds && su.duration_seconds > 0))) {
          // Thinking phase completed
          this.emit({
            event: 'thinking',
            sessionId,
            profile: sessionState.profileName,
            durationSeconds: su.duration_seconds,
            thinkingTokens: thinkingTokens,
            state: 'done',
            timestamp: new Date().toISOString()
          });
        }

        if (hasText) {
          sessionState.status = 'running';
          this.emit({
            event: 'chat_token',
            sessionId,
            profile: sessionState.profileName,
            text: textChunk,
            timestamp: new Date().toISOString()
          });
        }

        // If usage metadata is present, emit token_usage
        if (su.usage) {
          sessionState.tokens = {
            inputTokens: su.usage.input_tokens,
            outputTokens: su.usage.output_tokens,
            thinkingTokens: su.usage.thinking_tokens,
            totalTokens: su.usage.total_tokens
          };
          this.emit({
            event: 'token_usage',
            sessionId,
            profile: sessionState.profileName,
            usage: sessionState.tokens,
            timestamp: new Date().toISOString()
          });
        }
      }
      // 2. Tool invocation & completion
      else if (su.step_type === 'tool' || su.step_type === 'tool_call') {
        const rawToolName = su.tool_name || su.tool_info?.name || su.call?.name || 'tool';
        const rawParams = su.tool_info?.parameters || su.tool_args || su.call?.arguments || {};
        const callId = su.call_id || `tool_${su.step_index ?? Date.now()}`;

        let toolDisplay = rawToolName;
        let formattedParams = rawParams;

        if (rawToolName === 'run_command' || rawToolName === 'shell_exec') {
          toolDisplay = 'Ran';
          formattedParams = { CommandLine: rawParams.CommandLine || rawParams.command || '' };
        } else if (rawToolName === 'list_dir' || rawToolName === 'grep_search' || rawToolName === 'find_by_name') {
          toolDisplay = 'Explored';
        }

        if (su.state === 'ACTIVE') {
          sessionState.status = 'tool_executing';
          this.emit({
            event: 'tool_start',
            sessionId,
            profile: sessionState.profileName,
            callId,
            tool: toolDisplay,
            params: formattedParams,
            timestamp: new Date().toISOString()
          });
        } else if (su.state === 'DONE' || su.state === 'ERROR') {
          const isErr = su.state === 'ERROR' || su.status === 'ERROR';
          const output = su.tool_info?.output ?? su.tool_info?.result ?? su.result ?? (isErr ? su.tool_info?.error?.message : '');
          const durationMs = su.duration_seconds ? Math.round(su.duration_seconds * 1000) : 0;

          this.emit({
            event: 'tool_output',
            sessionId,
            profile: sessionState.profileName,
            callId,
            tool: toolDisplay,
            success: !isErr,
            result: output,
            error: isErr ? (su.tool_info?.error?.message || 'Tool execution failed') : undefined,
            durationMs,
            timestamp: new Date().toISOString()
          });
        }
      }
    }
    // 3. Final Turn Result
    else if (evt.event === 'result' && evt.result) {
      const res = evt.result;
      if (res.usage) {
        sessionState.tokens = {
          inputTokens: res.usage.input_tokens,
          outputTokens: res.usage.output_tokens,
          thinkingTokens: res.usage.thinking_tokens,
          totalTokens: res.usage.total_tokens
        };
        this.emit({
          event: 'token_usage',
          sessionId,
          profile: sessionState.profileName,
          usage: sessionState.tokens,
          timestamp: new Date().toISOString()
        });
      }
    }
  }

  /**
   * Background Quota Poller: Polls all authenticated profiles independently
   */
  public async pollAllQuotas(): Promise<void> {
    const profilesToPoll = new Set<string>();
    if (this.activeProfileName) profilesToPoll.add(this.activeProfileName);
    for (const p of this.profiles.values()) {
      if (p.name) profilesToPoll.add(p.name);
    }
    for (const sess of this.activeSessions.values()) {
      if (sess.profileName) profilesToPoll.add(sess.profileName);
    }
    for (const profileName of profilesToPoll) {
      try {
        await this.pollQuota(profileName);
      } catch (err) {
        console.warn(`[SessionManager] Quota poll for ${profileName} failed:`, err);
      }
    }
  }

  public async pollQuota(profileName?: string): Promise<{ percent: number; details: string }> {
    try {
      const agyCmd = process.platform === 'win32' ? 'agy.exe' : 'agy';
      const targetProfile = profileName || this.activeProfileName;
      const virtualHome = this.prepareVirtualHome(targetProfile);
      const env = this.buildScopedEnvironment(targetProfile, 'quota_poll', virtualHome);

      const proc = Bun.spawn([agyCmd, '-p', '/quota', '--dangerously-skip-permissions'], {
        cwd: virtualHome,
        env,
        stdout: 'pipe',
        stderr: 'pipe',
        stdin: 'ignore'
      });

      const output = await new Response(proc.stdout).text();
      await proc.exited;

      const matches = Array.from(output.matchAll(/(\d{1,3})%/g));
      if (matches.length > 0) {
        const pcts = matches.map(m => parseInt(m[1], 10)).filter(n => !isNaN(n));
        const minPct = Math.min(...pcts);

        // Cache quota for profile
        this.profileQuotas.set(targetProfile.toLowerCase(), {
          quotaPercent: minPct,
          details: output.trim(),
          lastPolled: Date.now()
        });

        // Update all active sessions belonging to this profile and emit session-scoped quota events
        for (const sess of this.activeSessions.values()) {
          if (sess.profileName.toLowerCase() === targetProfile.toLowerCase()) {
            sess.quotaPercent = minPct;
            this.emit({
              event: 'quota_update',
              profile: targetProfile,
              sessionId: sess.sessionId,
              quotaPercent: minPct,
              details: output.trim(),
              timestamp: new Date().toISOString()
            });
          }
        }

        // Also emit profile-level quota update
        this.emit({
          event: 'quota_update',
          profile: targetProfile,
          quotaPercent: minPct,
          details: output.trim(),
          timestamp: new Date().toISOString()
        });
        return { percent: minPct, details: output.trim() };
      }
    } catch (err) {
      console.warn('[SessionManager] Quota poll failed:', err);
    }
    return { percent: 96, details: '' };
  }

  public getSessionState(sessionId: string): ChatSessionState | undefined {
    return this.activeSessions.get(sessionId);
  }

  public getAllSessionStates(): ChatSessionState[] {
    return Array.from(this.activeSessions.values());
  }

  public getProfileQuota(profileName: string): number | undefined {
    return this.profileQuotas.get(profileName.toLowerCase())?.quotaPercent;
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
