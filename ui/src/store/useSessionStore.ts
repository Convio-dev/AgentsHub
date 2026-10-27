/**
 * Multi-Session State Store (Zustand + LocalStorage Persistence)
 * Strictly isolates model, quota, tokens, PID, and chat timelines per session.
 */

import { create } from 'zustand';
import {
  SessionRecord,
  ChatMessage,
  AgentExecutionState,
  TimelineNode,
  TokenUsage,
  ThoughtTimelineNode,
  CommandTimelineNode,
  FileTimelineNode,
  DiffTimelineNode,
  ChatAttachment,
} from '../types/agent';
import { generateSessionTitle } from '../services/sessionTitleGenerator';
import { classifyTool, extractCommand, extractPath, normaliseOutput } from '../lib/toolMeta';
import { migrateTimelineNodes } from '../lib/timelineMigration';
import { isUnsetProfile } from '../lib/profileName';

const STORAGE_KEY_SESSIONS = 'agentshub_sessions_v2';

/** Titles that mean "not named yet". A session holding one of these is still eligible for naming. */
const PLACEHOLDER_TITLES = new Set(['new session', 'workspace session', 'untitled session', 'untitled', '']);

function isPlaceholderTitle(title: string | undefined): boolean {
  return PLACEHOLDER_TITLES.has((title || '').trim().toLowerCase());
}
const STORAGE_KEY_ACTIVE = 'agentshub_active_session_v2';

/** Result of a regenerate or edit: the prompt to re-send plus the id of the new reply placeholder. */
interface PromptDispatch {
  prompt: string;
  assistantMsgId: string;
}

interface SessionStoreState {
  sessions: Record<string, SessionRecord>;
  activeSessionId: string;

  // Actions
  createSession: (profile?: string, initialModel?: string) => string;
  switchSession: (sessionId: string) => void;
  renameSession: (sessionId: string, title: string) => void;
  pinSession: (sessionId: string, isPinned?: boolean) => void;
  deleteSession: (sessionId: string) => void;
  clearSessionMessages: (sessionId: string) => void;

  // Real-time Chat & Streaming Actions
  addUserPrompt: (
    sessionId: string,
    prompt: string,
    profile?: string,
    attachments?: ChatAttachment[]
  ) => { userMsgId: string; assistantMsgId: string };
  /**
   * Removes the assistant reply and its timeline, then opens a fresh empty reply for the same user
   * prompt. Returns the prompt that has to be re-sent to the engine, or null when the pair cannot be
   * resolved.
   */
  prepareRegenerate: (sessionId: string, assistantMessageId: string) => PromptDispatch | null;
  /**
   * Forks the conversation at a user message: that message and everything after it are dropped and
   * replaced by the edited prompt. Returns the edited text plus the ids of the new pair.
   */
  editUserPrompt: (sessionId: string, userMessageId: string, newPrompt: string) => PromptDispatch | null;
  /** Marks an in-flight turn as user-aborted and settles every running node. */
  markStopped: (sessionId: string) => void;
  appendChatToken: (sessionId: string, text: string) => void;
  appendThinkingChunk: (
    sessionId: string,
    text?: string,
    durationSeconds?: number,
    thinkingTokens?: number,
    state?: 'active' | 'done'
  ) => void;
  startToolNode: (sessionId: string, callId: string, tool: string, params: Record<string, any>) => void;
  finishToolNode: (
    sessionId: string,
    callId: string,
    tool: string,
    success: boolean,
    result?: any,
    error?: string,
    durationMs?: number
  ) => void;
  updateSessionStatus: (
    sessionId: string,
    status: AgentExecutionState,
    error?: string,
    pid?: number
  ) => void;
  /**
   * Records one completed turn's usage: the latest turn is kept for display and its numbers are
   * added to the session total. Called once per `token_usage` event, which the sidecar emits exactly
   * once per turn.
   */
  recordTurnUsage: (sessionId: string, usage: TokenUsage) => void;
  updateSessionTelemetry: (
    sessionId: string,
    data: {
      model?: string;
      reasoningEffort?: string;
      quotaPercent?: number;
      tokens?: Partial<TokenUsage>;
    }
  ) => void;

  // Getters
  getActiveSession: () => SessionRecord | undefined;
}

function loadPersistedSessions(): { sessions: Record<string, SessionRecord>; activeId: string } {
  try {
    const rawSessions = localStorage.getItem(STORAGE_KEY_SESSIONS);
    const rawActive = localStorage.getItem(STORAGE_KEY_ACTIVE);

    if (rawSessions) {
      const parsed = JSON.parse(rawSessions);
      if (parsed && typeof parsed === 'object' && Object.keys(parsed).length > 0) {
        for (const k of Object.keys(parsed)) {
          const s = parsed[k];
          // Legacy records may hold a bare "default" (or nothing at all); an empty id means
          // "unconfigured" and the host resolves the active profile from there.
          if (isUnsetProfile(s.profile)) {
            s.profile = '';
          }
          if (Array.isArray(s.messages)) {
            for (const m of s.messages) {
              if (isUnsetProfile(m.profile)) {
                m.profile = '';
              }
              // Repair rows written by the old renderer, which stored stringified params bags as
              // command text and never recorded a tool kind.
              if (Array.isArray(m.timeline)) {
                m.timeline = migrateTimelineNodes(m.timeline);
              }
            }
          }
          // A session that already has messages has had its one title generation; latch it so the
          // old retitle-on-every-turn behaviour cannot come back through a stale record.
          if (typeof s.titleLocked !== 'boolean') {
            // Only latch a real title. Locking a placeholder is what made sessions stay "New Session"
            // forever: the record had messages, so it was marked as already named.
            s.titleLocked = Array.isArray(s.messages) && s.messages.length > 0 && !isPlaceholderTitle(s.title);
          }
        }
        const activeId = rawActive && parsed[rawActive] ? rawActive : Object.keys(parsed)[0];
        return { sessions: parsed, activeId };
      }
    }
  } catch (err) {
    console.warn('[SessionStore] Failed to load local sessions:', err);
  }

  // Create initial default session
  const defaultId = `sess_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`;
  const defaultSession: SessionRecord = {
    id: defaultId,
    title: 'Workspace Session',
    isTitleCustom: false,
    titleLocked: false,
    isPinned: false,
    createdAt: Date.now(),
    updatedAt: Date.now(),
    profile: '',
    // Unknown until the engine reports one; the composer shows the resolved default.
    model: '',
    quotaPercent: undefined,
    tokens: { inputTokens: 0, outputTokens: 0, thinkingTokens: 0, totalTokens: 0 },
    status: 'idle',
    messages: [],
  };

  return {
    sessions: { [defaultId]: defaultSession },
    activeId: defaultId,
  };
}

function persistState(sessions: Record<string, SessionRecord>, activeId: string) {
  try {
    localStorage.setItem(STORAGE_KEY_SESSIONS, JSON.stringify(sessions));
    localStorage.setItem(STORAGE_KEY_ACTIVE, activeId);
  } catch (err) {
    console.warn('[SessionStore] Could not persist sessions:', err);
  }
}

/**
 * Splits a tool result into stdout/stderr for the code drawer. Engines return either a plain string
 * or a structured object, and a failure belongs in stderr rather than in the output stream.
 */
function shapeCommandResult(
  result: unknown,
  success: boolean,
  error?: string
): { stdout?: string; stderr?: string; exitCode?: number } {
  if (result && typeof result === 'object') {
    const record = result as Record<string, unknown>;
    const stdout =
      typeof record.stdout === 'string' ? record.stdout : typeof record.output === 'string' ? record.output : '';
    const stderr =
      typeof record.stderr === 'string' ? record.stderr : typeof record.error === 'string' ? record.error : '';
    const exitCode = typeof record.exitCode === 'number' ? record.exitCode : undefined;

    if (stdout || stderr || exitCode !== undefined) {
      return {
        stdout: normaliseOutput(stdout) || undefined,
        stderr: normaliseOutput(stderr) || undefined,
        exitCode,
      };
    }
  }

  const text =
    result === undefined || result === null
      ? ''
      : typeof result === 'string'
        ? result
        : JSON.stringify(result, null, 2);

  if (success) return { stdout: normaliseOutput(text) || undefined };
  return { stderr: normaliseOutput(text || error || 'Command failed') || undefined };
}

const initialData = loadPersistedSessions();

export const useSessionStore = create<SessionStoreState>((set, get) => ({
  sessions: initialData.sessions,
  activeSessionId: initialData.activeId,

  getActiveSession: () => {
    const { sessions, activeSessionId } = get();
    return sessions[activeSessionId];
  },

  createSession: (profile = '', initialModel = '') => {
    const resolvedProfile = isUnsetProfile(profile) ? '' : profile;
    const id = `sess_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`;
    const newSession: SessionRecord = {
      id,
      title: 'New Session',
      isTitleCustom: false,
      titleLocked: false,
      isPinned: false,
      createdAt: Date.now(),
      updatedAt: Date.now(),
      profile: resolvedProfile,
      model: initialModel,
      // Unknown until this profile's quota is actually read; the ring renders an empty track.
      quotaPercent: undefined,
      tokens: { inputTokens: 0, outputTokens: 0, thinkingTokens: 0, totalTokens: 0 },
      status: 'idle',
      messages: [],
    };

    set(state => {
      const nextSessions = { ...state.sessions, [id]: newSession };
      persistState(nextSessions, id);
      return {
        sessions: nextSessions,
        activeSessionId: id,
      };
    });

    return id;
  },

  switchSession: (sessionId: string) => {
    set(state => {
      if (!state.sessions[sessionId]) return state;
      persistState(state.sessions, sessionId);
      return { activeSessionId: sessionId };
    });
  },

  renameSession: (sessionId: string, title: string) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target) return state;

      const updated: SessionRecord = {
        ...target,
        title: title.trim() || 'Untitled Session',
        isTitleCustom: true,
        titleLocked: true,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });
  },

  pinSession: (sessionId: string, isPinned?: boolean) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target) return state;

      const updated: SessionRecord = {
        ...target,
        isPinned: isPinned !== undefined ? isPinned : !target.isPinned,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });
  },

  deleteSession: (sessionId: string) => {
    set(state => {
      const keys = Object.keys(state.sessions);
      if (keys.length <= 1) {
        // Clear instead of removing last remaining session
        const singleId = keys[0];
        const cleared: SessionRecord = {
          ...state.sessions[singleId],
          title: 'Workspace Session',
          isTitleCustom: false,
          titleLocked: false,
          messages: [],
          status: 'idle',
          tokens: { inputTokens: 0, outputTokens: 0, thinkingTokens: 0, totalTokens: 0 },
        };
        const nextSessions = { [singleId]: cleared };
        persistState(nextSessions, singleId);
        return { sessions: nextSessions, activeSessionId: singleId };
      }

      const nextSessions = { ...state.sessions };
      delete nextSessions[sessionId];

      let nextActiveId = state.activeSessionId;
      if (state.activeSessionId === sessionId) {
        nextActiveId = Object.keys(nextSessions)[0];
      }

      persistState(nextSessions, nextActiveId);
      return {
        sessions: nextSessions,
        activeSessionId: nextActiveId,
      };
    });
  },

  clearSessionMessages: (sessionId: string) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target) return state;

      const updated: SessionRecord = {
        ...target,
        messages: [],
        status: 'idle',
        activeExecutionCardId: undefined,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });
  },

  addUserPrompt: (sessionId: string, prompt: string, profile?: string, attachments?: ChatAttachment[]) => {
    const userMsgId = `msg_user_${Date.now()}`;
    const assistantMsgId = `msg_asst_${Date.now() + 1}`;

    set(state => {
      const target = state.sessions[sessionId];
      if (!target) return state;

      const resolvedProfile = !isUnsetProfile(profile)
        ? (profile as string)
        : (isUnsetProfile(target.profile) ? '' : target.profile);

      const turn = target.messages.filter(m => m.sender === 'user').length + 1;

      const userMsg: ChatMessage = {
        id: userMsgId,
        sessionId,
        sender: 'user',
        prompt,
        attachments,
        timeline: [],
        status: 'completed',
        timestamp: Date.now(),
        turn,
      };

      const assistantMsg: ChatMessage = {
        id: assistantMsgId,
        sessionId,
        sender: 'assistant',
        markdown: '',
        timeline: [],
        status: 'thinking',
        profile: resolvedProfile,
        timestamp: Date.now(),
        turn,
      };

      // Name the session from the first prompt, then latch it. A session still sitting on a
      // placeholder (including one persisted by an older build) gets another chance here, so a stuck
      // "New Session" recovers on its next message instead of staying nameless.
      const isFirstTurn = target.messages.length === 0;
      const shouldGenerateTitle = !target.isTitleCustom && (isFirstTurn || isPlaceholderTitle(target.title));

      const candidate = shouldGenerateTitle ? generateSessionTitle(prompt) : target.title;
      // The generator returns a placeholder when the prompt carries no usable words ("hi", "thanks").
      // Keeping the old title and staying unlocked lets a later turn try again.
      const newTitle = isPlaceholderTitle(candidate) ? target.title : candidate;
      const titleLocked = target.isTitleCustom || target.titleLocked || !isPlaceholderTitle(newTitle);

      const updated: SessionRecord = {
        ...target,
        title: newTitle,
        isTitleCustom: target.isTitleCustom,
        titleLocked,
        profile: resolvedProfile,
        status: 'thinking',
        activeExecutionCardId: assistantMsgId,
        messages: [...target.messages, userMsg, assistantMsg],
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });

    return { userMsgId, assistantMsgId };
  },

  prepareRegenerate: (sessionId, assistantMessageId) => {
    const target = get().sessions[sessionId];
    if (!target) return null;

    const index = target.messages.findIndex(m => m.id === assistantMessageId);
    if (index < 0 || target.messages[index].sender !== 'assistant') return null;

    let userIndex = -1;
    for (let i = index - 1; i >= 0; i -= 1) {
      if (target.messages[i].sender === 'user') {
        userIndex = i;
        break;
      }
    }
    if (userIndex < 0) return null;

    const sourcePrompt = target.messages[userIndex].prompt || '';
    const assistantMsgId = `msg_asst_${Date.now()}`;
    const placeholder: ChatMessage = {
      id: assistantMsgId,
      sessionId,
      sender: 'assistant',
      markdown: '',
      timeline: [],
      status: 'thinking',
      profile: target.profile,
      timestamp: Date.now(),
      turn: target.messages[index].turn,
    };

    // Keep the user message, replace only the reply that is being regenerated.
    const messages = [...target.messages.slice(0, index), placeholder];
    const updated: SessionRecord = {
      ...target,
      messages,
      status: 'thinking',
      activeExecutionCardId: assistantMsgId,
      updatedAt: Date.now(),
    };

    set(state => {
      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });

    return { prompt: sourcePrompt, assistantMsgId };
  },

  editUserPrompt: (sessionId, userMessageId, newPrompt) => {
    const target = get().sessions[sessionId];
    if (!target) return null;

    const index = target.messages.findIndex(m => m.id === userMessageId);
    if (index < 0 || target.messages[index].sender !== 'user') return null;

    const original = target.messages[index];
    const text = newPrompt.trim();
    if (!text) return null;

    const turn = original.turn ?? target.messages.slice(0, index + 1).filter(m => m.sender === 'user').length;
    const userMsgId = `msg_user_${Date.now()}`;
    const assistantMsgId = `msg_asst_${Date.now() + 1}`;

    const userMsg: ChatMessage = {
      ...original,
      id: userMsgId,
      prompt: text,
      timestamp: Date.now(),
      status: 'completed',
      turn,
      forkedFrom: original.id,
    };

    const assistantMsg: ChatMessage = {
      id: assistantMsgId,
      sessionId,
      sender: 'assistant',
      markdown: '',
      timeline: [],
      status: 'thinking',
      profile: target.profile,
      timestamp: Date.now(),
      turn,
    };

    // Fork: everything from the edited prompt onward is dropped, the rest of the thread survives.
    const messages = [...target.messages.slice(0, index), userMsg, assistantMsg];
    const updated: SessionRecord = {
      ...target,
      messages,
      status: 'thinking',
      activeExecutionCardId: assistantMsgId,
      updatedAt: Date.now(),
    };

    set(state => {
      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });

    return { prompt: text, assistantMsgId };
  },

  markStopped: sessionId => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target) return state;

      const messages = [...target.messages];
      const lastIndex = messages.length - 1;
      const lastMsg = messages[lastIndex];

      if (lastMsg && lastMsg.sender === 'assistant') {
        messages[lastIndex] = {
          ...lastMsg,
          status: 'aborted',
          timeline: lastMsg.timeline.map(node => {
            if (node.status !== 'running') return node;
            // A command that was killed did not finish; reasoning that was cut short still ran.
            return { ...node, status: node.type === 'command' ? 'failed' : 'completed' };
          }),
        };
      }

      const updated: SessionRecord = {
        ...target,
        status: 'aborted',
        activeExecutionCardId: undefined,
        messages,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });
  },

  appendChatToken: (sessionId: string, text: string) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target || target.messages.length === 0) return state;

      const messages = [...target.messages];
      const lastIndex = messages.length - 1;
      const lastMsg = messages[lastIndex];

      if (lastMsg && lastMsg.sender === 'assistant') {
        messages[lastIndex] = {
          ...lastMsg,
          markdown: (lastMsg.markdown || '') + text,
          status: lastMsg.status === 'aborted' ? 'aborted' : 'running',
        };

        const updated: SessionRecord = {
          ...target,
          messages,
          updatedAt: Date.now(),
        };

        const nextSessions = { ...state.sessions, [sessionId]: updated };
        return { sessions: nextSessions };
      }

      return state;
    });
  },

  appendThinkingChunk: (sessionId, text, durationSeconds, thinkingTokens, thinkingState) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target || target.messages.length === 0) return state;

      const messages = [...target.messages];
      const lastIndex = messages.length - 1;
      const lastMsg = messages[lastIndex];

      if (!lastMsg || lastMsg.sender !== 'assistant') return state;

      const timeline = [...lastMsg.timeline];
      // Find existing thought node or create new
      let thoughtIndex = timeline.findIndex(n => n.type === 'thought' && n.status === 'running');

      if (thoughtIndex === -1 && thinkingState !== 'done') {
        const newNode: ThoughtTimelineNode = {
          id: `thought_${Date.now()}`,
          type: 'thought',
          text: text || '',
          durationSeconds: durationSeconds || 0,
          tokens: thinkingTokens || 0,
          isStreaming: true,
          status: 'running',
          timestamp: Date.now(),
        };
        timeline.push(newNode);
      } else if (thoughtIndex >= 0) {
        const current = timeline[thoughtIndex] as ThoughtTimelineNode;
        const isDone = thinkingState === 'done';
        const mergedText = text ? current.text + (current.text ? '\n' : '') + text : current.text;
        const mergedTokens = thinkingTokens !== undefined ? thinkingTokens : current.tokens;

        // Nothing was actually said and no reasoning tokens were counted, so there is no step to
        // show. Removing it here is what keeps "Thought" rows with an empty drawer out of the chat.
        if (isDone && !mergedText.trim() && !mergedTokens) {
          timeline.splice(thoughtIndex, 1);
        } else {
          timeline[thoughtIndex] = {
            ...current,
            text: mergedText,
            durationSeconds: durationSeconds !== undefined ? durationSeconds : current.durationSeconds,
            tokens: mergedTokens,
            isStreaming: !isDone,
            status: isDone ? 'completed' : 'running',
          };
        }
      }

      messages[lastIndex] = {
        ...lastMsg,
        timeline,
        status: thinkingState === 'done' ? 'running' : 'thinking',
      };

      const updated: SessionRecord = {
        ...target,
        status: thinkingState === 'done' ? target.status : 'thinking',
        messages,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      return { sessions: nextSessions };
    });
  },

  startToolNode: (sessionId, callId, tool, params) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target || target.messages.length === 0) return state;

      const messages = [...target.messages];
      const lastIndex = messages.length - 1;
      const lastMsg = messages[lastIndex];

      if (!lastMsg || lastMsg.sender !== 'assistant') return state;

      const timeline = [...lastMsg.timeline];
      const toolKind = classifyTool(tool, params);

      let node: TimelineNode;

      if (toolKind === 'command') {
        const cmd: CommandTimelineNode = {
          id: callId,
          type: 'command',
          // The engine's label ("Ran") is display text, not a command. Only the params carry the
          // command, and a payload that has none must never be printed as JSON.
          command: extractCommand(params) || tool,
          params,
          toolName: tool,
          toolKind,
          status: 'running',
          timestamp: Date.now(),
        };
        node = cmd;
      } else if (toolKind === 'read' || toolKind === 'search') {
        const fileNode: FileTimelineNode = {
          id: callId,
          type: 'file',
          path: extractPath(params) || tool,
          action: toolKind === 'search' ? 'search' : 'read',
          count: 1,
          toolName: tool,
          toolKind,
          status: 'running',
          timestamp: Date.now(),
        };
        node = fileNode;
      } else if (toolKind === 'write') {
        const diffNode: DiffTimelineNode = {
          id: callId,
          type: 'diff',
          file: extractPath(params) || 'Code edit',
          additions: 0,
          deletions: 0,
          toolName: tool,
          toolKind,
          status: 'running',
          timestamp: Date.now(),
        };
        node = diffNode;
      } else {
        // Unrecognised tool: fall back to its name and target, never to a stringified params bag.
        const genNode: CommandTimelineNode = {
          id: callId,
          type: 'command',
          command: extractPath(params) || tool,
          params,
          toolName: tool,
          toolKind: 'other',
          status: 'running',
          timestamp: Date.now(),
        };
        node = genNode;
      }

      timeline.push(node);

      messages[lastIndex] = {
        ...lastMsg,
        timeline,
        status: 'tool_executing',
      };

      const updated: SessionRecord = {
        ...target,
        status: 'tool_executing',
        messages,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      return { sessions: nextSessions };
    });
  },

  finishToolNode: (sessionId, callId, _tool, success, result, error, durationMs) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target || target.messages.length === 0) return state;

      const messages = [...target.messages];
      const lastIndex = messages.length - 1;
      const lastMsg = messages[lastIndex];

      if (!lastMsg || lastMsg.sender !== 'assistant') return state;

      const timeline = lastMsg.timeline.map(node => {
        if (node.id === callId) {
          const status: 'completed' | 'failed' = success ? 'completed' : 'failed';
          if (node.type === 'command') {
            const shaped = shapeCommandResult(result, success, error);
            return {
              ...node,
              status,
              ...shaped,
              durationMs,
            } as CommandTimelineNode;
          }
          if (node.type === 'file') {
            const preview = typeof result === 'string' ? result : result?.output ?? result?.content ?? '';
            return {
              ...node,
              status,
              contentPreview: typeof preview === 'string' ? preview.slice(0, 4000) : undefined,
            } as FileTimelineNode;
          }
          if (node.type === 'diff') {
            return {
              ...node,
              status,
              additions: typeof result === 'object' && result?.additions ? result.additions : node.additions,
              deletions: typeof result === 'object' && result?.deletions ? result.deletions : node.deletions,
              patch: typeof result === 'string' ? result : result?.patch,
            } as DiffTimelineNode;
          }
          return { ...node, status } as TimelineNode;
        }
        return node;
      });

      messages[lastIndex] = {
        ...lastMsg,
        timeline,
        status: 'running',
      };

      const updated: SessionRecord = {
        ...target,
        messages,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      return { sessions: nextSessions };
    });
  },

  updateSessionStatus: (sessionId, status, error, pid) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target) return state;

      const messages = [...target.messages];
      const lastMsg = messages[messages.length - 1];

      // The sidecar answers an abort with `idle`, which can land after the local `aborted` state.
      // Keeping `aborted` is what makes the Stopped badge survive that race.
      const resolvedStatus: AgentExecutionState =
        status === 'idle' && lastMsg?.sender === 'assistant' && lastMsg.status === 'aborted'
          ? 'aborted'
          : status;

      if (messages.length > 0) {
        const lastIndex = messages.length - 1;
        if (lastMsg && lastMsg.sender === 'assistant') {
          // Finalize any streaming or running timeline nodes
          const finalizedTimeline = lastMsg.timeline
            .filter(node => {
              // A thought row with no text and no token count has nothing to expand.
              if (node.type !== 'thought') return true;
              return Boolean(node.text.trim()) || Boolean(node.tokens);
            })
            .map(node => {
              if (node.status === 'running') {
                if (node.type === 'thought') {
                  return { ...node, status: 'completed' as const, isStreaming: false };
                }
                return { ...node, status: 'completed' as const };
              }
              return node;
            });

          messages[lastIndex] = {
            ...lastMsg,
            timeline: finalizedTimeline,
            status: resolvedStatus,
            error: error || lastMsg.error,
          };
        }
      }

      const isTerminal =
        resolvedStatus === 'idle' || resolvedStatus === 'completed' || resolvedStatus === 'error' || resolvedStatus === 'aborted';

      const updated: SessionRecord = {
        ...target,
        status: resolvedStatus,
        pid: pid || target.pid,
        activeExecutionCardId: isTerminal ? undefined : target.activeExecutionCardId,
        messages,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });
  },

  recordTurnUsage: (sessionId, usage) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target) return state;

      const totals = target.tokens || { inputTokens: 0, outputTokens: 0, thinkingTokens: 0, totalTokens: 0 };
      const accumulated: TokenUsage = {
        inputTokens: totals.inputTokens + (usage.inputTokens || 0),
        outputTokens: totals.outputTokens + (usage.outputTokens || 0),
        thinkingTokens: totals.thinkingTokens + (usage.thinkingTokens || 0),
        totalTokens: totals.totalTokens + (usage.totalTokens || 0),
      };

      const updated: SessionRecord = {
        ...target,
        tokens: accumulated,
        lastTurnTokens: usage,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });
  },

  updateSessionTelemetry: (sessionId, data) => {
    set(state => {
      const target = state.sessions[sessionId];
      if (!target) return state;

      const updated: SessionRecord = {
        ...target,
        model: data.model !== undefined ? data.model : target.model,
        reasoningEffort: data.reasoningEffort !== undefined ? data.reasoningEffort : target.reasoningEffort,
        quotaPercent: data.quotaPercent !== undefined ? data.quotaPercent : target.quotaPercent,
        tokens: data.tokens ? { ...target.tokens, ...data.tokens } : target.tokens,
        updatedAt: Date.now(),
      };

      const nextSessions = { ...state.sessions, [sessionId]: updated };
      persistState(nextSessions, state.activeSessionId);
      return { sessions: nextSessions };
    });
  },
}));
