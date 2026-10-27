/**
 * Chat orchestration shared by the composer, the message action bar and the session header.
 *
 * Every entry point that starts or stops a turn goes through here so the store transitions and the
 * bridge calls cannot drift apart. The store itself stays a pure state machine.
 */

import { webviewBridge } from './webviewBridge';
import { useSessionStore } from '../store/useSessionStore';
import { useWorkspaceStore } from '../store/useWorkspaceStore';
import { ChatAttachment } from '../types/agent';
import { profileForDispatch } from '../lib/profileName';
import { resolveModelId, useModelStore } from '../store/useModelStore';

interface DispatchTarget {
  sessionId: string;
  prompt: string;
}

function resolveContext(sessionId: string) {
  const session = useSessionStore.getState().sessions[sessionId];
  const { models } = useModelStore.getState();

  // The session stores whatever the picker last wrote, which for older sessions can be a display
  // label rather than an id. Resolving against the catalog turns that back into a real id — or into
  // `undefined`, which means "let the engine use its default".
  const model =
    models.length > 0
      ? resolveModelId(models, session?.model, session?.reasoningEffort)
      : session?.model || undefined;

  return {
    model,
    profile: profileForDispatch(session?.profile),
    /**
     * Effort is deliberately not sent. The engine rejects a tiered model id combined with
     * `--effort` ("conflicts with --effort=low") and rejects `--effort` for models without tiers,
     * because the tier is already encoded in the model id. Choosing an effort selects that id.
     */
    workingDirectory: useWorkspaceStore.getState().activeWorkspace?.workingDirectory || undefined,
  };
}

async function dispatch({ sessionId, prompt }: DispatchTarget): Promise<void> {
  const context = resolveContext(sessionId);

  try {
    await webviewBridge.invoke('send_chat', {
      sessionId,
      prompt,
      model: context.model,
      profile: context.profile,
      workingDirectory: context.workingDirectory,
    });
  } catch (err) {
    const message = err instanceof Error ? err.message : 'Failed to dispatch chat prompt';
    useSessionStore.getState().updateSessionStatus(sessionId, 'error', message);
  }
}

/** Appends the user prompt plus a pending reply, then hands the prompt to the engine. */
export async function sendPrompt(
  sessionId: string,
  prompt: string,
  attachments?: ChatAttachment[]
): Promise<void> {
  useSessionStore.getState().addUserPrompt(sessionId, prompt, undefined, attachments);
  await dispatch({ sessionId, prompt });
}

/**
 * Stops the active turn: the sidecar kills the child process, then the store settles the UI. The
 * local state is updated even when the bridge call fails, because leaving the composer disabled on
 * a dead sidecar would trap the user.
 */
export async function stopGeneration(sessionId: string): Promise<void> {
  try {
    await webviewBridge.invoke('abort_task', { taskId: sessionId, reason: 'user_stopped' });
  } catch (err) {
    console.warn('[chatController] abort_task failed:', err);
  } finally {
    useSessionStore.getState().markStopped(sessionId);
  }
}

/** Replaces one assistant reply with a fresh attempt at the same user prompt. */
export async function regenerateResponse(sessionId: string, assistantMessageId: string): Promise<void> {
  const prepared = useSessionStore.getState().prepareRegenerate(sessionId, assistantMessageId);
  if (!prepared) return;
  await dispatch({ sessionId, prompt: prepared.prompt });
}

/** Forks the thread at a user message and runs the edited prompt. */
export async function editAndResend(
  sessionId: string,
  userMessageId: string,
  newPrompt: string
): Promise<void> {
  const prepared = useSessionStore.getState().editUserPrompt(sessionId, userMessageId, newPrompt);
  if (!prepared) return;
  await dispatch({ sessionId, prompt: prepared.prompt });
}
