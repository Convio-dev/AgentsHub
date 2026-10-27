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

const STORAGE_KEY_SESSIONS = 'agentshub_sessions_v2';
const STORAGE_KEY_ACTIVE = 'agentshub_active_session_v2';

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
          if (!s.profile || s.profile.toLowerCase() === 'default') {
            s.profile = 'ZXR@1';
          }
          if (Array.isArray(s.messages)) {
            for (const m of s.messages) {
              if (m.profile && m.profile.toLowerCase() === 'default') {
                m.profile = 'ZXR@1';
              }
            }
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
    isPinned: false,
    createdAt: Date.now(),
    updatedAt: Date.now(),
    profile: 'ZXR@1',
    model: 'Gemini 3.8 Flash (High)',
    quotaPercent: 100,
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

const initialData = loadPersistedSessions();

export const useSessionStore = create<SessionStoreState>((set, get) => ({
  sessions: initialData.sessions,
  activeSessionId: initialData.activeId,

  getActiveSession: () => {
    const { sessions, activeSessionId } = get();
    return sessions[activeSessionId];
  },

  createSession: (profile = 'ZXR@1', initialModel = 'Gemini 3.8 Flash (High)') => {
    const resolvedProfile = profile.toLowerCase() === 'default' ? 'ZXR@1' : profile;
    const id = `sess_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`;
    const newSession: SessionRecord = {
      id,
      title: 'New Session',
      isTitleCustom: false,
      isPinned: false,
      createdAt: Date.now(),
      updatedAt: Date.now(),
      profile: resolvedProfile,
      model: initialModel,
      quotaPercent: 100,
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

      const resolvedProfile = profile && profile.toLowerCase() !== 'default'
        ? profile
        : (target.profile && target.profile.toLowerCase() !== 'default' ? target.profile : 'ZXR@1');

      const userMsg: ChatMessage = {
        id: userMsgId,
        sessionId,
        sender: 'user',
        prompt,
        attachments,
        timeline: [],
        status: 'completed',
        timestamp: Date.now(),
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
      };

      // Auto-generate title if this is the first message or untitled
      let newTitle = target.title;
      let isCustom = target.isTitleCustom;
      if (!isCustom || target.title === 'New Session' || target.title === 'Workspace Session') {
        newTitle = generateSessionTitle(prompt);
        isCustom = false; // still auto-generated
      }

      const updated: SessionRecord = {
        ...target,
        title: newTitle,
        isTitleCustom: isCustom,
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
          status: 'running',
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
        timeline[thoughtIndex] = {
          ...current,
          text: text ? current.text + (current.text ? '\n' : '') + text : current.text,
          durationSeconds: durationSeconds !== undefined ? durationSeconds : current.durationSeconds,
          tokens: thinkingTokens !== undefined ? thinkingTokens : current.tokens,
          isStreaming: !isDone,
          status: isDone ? 'completed' : 'running',
        };
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
      const lowerTool = tool.toLowerCase();

      let node: TimelineNode;

      if (lowerTool.includes('cmd') || lowerTool.includes('shell') || lowerTool.includes('run_command')) {
        const cmd: CommandTimelineNode = {
          id: callId,
          type: 'command',
          command: params.command || params.CommandLine || tool,
          status: 'running',
          timestamp: Date.now(),
        };
        node = cmd;
      } else if (lowerTool.includes('read') || lowerTool.includes('view') || lowerTool.includes('grep') || lowerTool.includes('list')) {
        const fileNode: FileTimelineNode = {
          id: callId,
          type: 'file',
          path: params.TargetFile || params.AbsolutePath || params.DirectoryPath || params.SearchPath || params.path || tool,
          action: lowerTool.includes('list') ? 'list' : 'read',
          count: 1,
          status: 'running',
          timestamp: Date.now(),
        };
        node = fileNode;
      } else if (lowerTool.includes('write') || lowerTool.includes('patch') || lowerTool.includes('edit') || lowerTool.includes('replace')) {
        const diffNode: DiffTimelineNode = {
          id: callId,
          type: 'diff',
          file: params.TargetFile || params.path || 'Code Edit',
          additions: 0,
          deletions: 0,
          status: 'running',
          timestamp: Date.now(),
        };
        node = diffNode;
      } else {
        // General command or tool node
        const genNode: CommandTimelineNode = {
          id: callId,
          type: 'command',
          command: `${tool} ${typeof params === 'object' ? JSON.stringify(params) : params}`,
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
            const outText = result !== undefined ? (typeof result === 'object' ? JSON.stringify(result, null, 2) : String(result)) : error;
            return {
              ...node,
              status,
              output: outText,
              durationMs,
            } as CommandTimelineNode;
          }
          if (node.type === 'file') {
            return {
              ...node,
              status,
              contentPreview: typeof result === 'string' ? result.slice(0, 300) : undefined,
            } as FileTimelineNode;
          }
          if (node.type === 'diff') {
            return {
              ...node,
              status,
              additions: typeof result === 'object' && result?.additions ? result.additions : 12,
              deletions: typeof result === 'object' && result?.deletions ? result.deletions : 4,
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
      if (messages.length > 0) {
        const lastIndex = messages.length - 1;
        const lastMsg = messages[lastIndex];
        if (lastMsg && lastMsg.sender === 'assistant') {
          // Finalize any streaming or running timeline nodes
          const finalizedTimeline = lastMsg.timeline.map(node => {
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
            status,
            error: error || lastMsg.error,
          };
        }
      }

      const updated: SessionRecord = {
        ...target,
        status,
        pid: pid || target.pid,
        messages,
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
