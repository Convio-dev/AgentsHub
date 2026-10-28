import React, { useState } from 'react';
import { Loader2, Lock } from 'lucide-react';
import { useSessionStore } from '../../store/useSessionStore';
import { useProfileStore } from '../../store/useProfileStore';
import { grantRule, PermissionScope } from '../../lib/permissionRules';
import { retryWithPermissionGrant } from '../../services/chatController';
import { isUnsetProfile } from '../../lib/profileName';

interface PermissionRequestBarProps {
  sessionId: string;
}

interface PromptCopy {
  /** The CLI's own question for this kind of permission. */
  question: string;
  /** Plural noun used by the "always allow" options. */
  noun: string;
  /** First option, phrased the way the CLI phrases it. */
  allow: string;
}

/** Permission kinds the CLI reports, grouped by the question it asks for each. */
const COMMAND_KINDS = new Set(['command', 'run_command', 'run_workflow']);
const WRITE_KINDS = new Set([
  'write_file',
  'create_file',
  'replace_file_content',
  'multi_replace_file_content',
  'edit_file',
  'sed_file',
  'notebook_edit',
]);
const READ_KINDS = new Set(['read_file', 'view_file', 'list_dir', 'find_by_name', 'grep_search', 'list_resources']);
const URL_KINDS = new Set(['url', 'read_url_content', 'open_browser_url', 'fetch_url']);

/**
 * Mirrors the wording the CLI's own prompt uses, per permission kind. The options are not a fixed
 * list in `agy` either — the question and the nouns change with what is being asked for, and a file
 * write is a different question from a shell command.
 */
function promptCopy(permission: string): PromptCopy {
  const kind = permission.toLowerCase();

  if (COMMAND_KINDS.has(kind)) {
    return { question: 'Run this command?', noun: 'commands', allow: 'Yes, run command' };
  }
  if (WRITE_KINDS.has(kind)) {
    return { question: 'Allow writing this file?', noun: 'files', allow: 'Yes, allow this write' };
  }
  if (READ_KINDS.has(kind)) {
    return { question: 'Allow access to this file?', noun: 'files', allow: 'Yes, allow access' };
  }
  if (URL_KINDS.has(kind)) {
    return { question: 'Allow access to this URL?', noun: 'URLs', allow: 'Yes, allow this URL' };
  }
  if (kind === 'mcp' || kind === 'call_mcp_tool') {
    return { question: 'Allow calling this tool?', noun: 'tools', allow: 'Yes, allow this call' };
  }
  return { question: 'Allow this action?', noun: 'actions', allow: 'Yes, allow' };
}

/**
 * The only approval channel headless `agy` allows.
 *
 * A refused tool cannot be resumed mid-turn, so each option here is the CLI's option expressed as a
 * settings rule plus a retry: run once (revoked when the turn settles), this conversation (revoked
 * when the chat goes), or persisted. The wording and the quoted scope come from the CLI so the same
 * choice reads the same way in both places.
 */
export const PermissionRequestBar: React.FC<PermissionRequestBarProps> = ({ sessionId }) => {
  const denials = useSessionStore(state => state.pendingDenials[sessionId]);
  const dismiss = useSessionStore(state => state.dismissPermissionDenial);
  const registerGrantedRule = useSessionStore(state => state.registerGrantedRule);
  const sessionStatus = useSessionStore(state => state.sessions[sessionId]?.status);
  const activeProfileName = useProfileStore(state => state.activeProfileName);

  const [busyScope, setBusyScope] = useState<PermissionScope | 'cancel' | null>(null);
  const [error, setError] = useState<string | null>(null);

  const denial = denials?.[0];
  if (!denial) return null;

  const profile = !isUnsetProfile(denial.profile) ? (denial.profile as string) : activeProfileName;
  const subject = denial.command || denial.target || 'this request';
  const copy = promptCopy(denial.permission);
  // A turn that is still running reads its settings at start, so retrying now would race it.
  const turnRunning =
    sessionStatus === 'thinking' || sessionStatus === 'running' || sessionStatus === 'tool_executing';

  const choose = async (scope: PermissionScope | 'cancel') => {
    if (busyScope) return;

    if (scope === 'cancel') {
      dismiss(sessionId, denial.rule);
      return;
    }

    if (!denial.rule) {
      setError('This denial has no recognisable allow-rule, so it can only be cancelled.');
      return;
    }

    setError(null);
    setBusyScope(scope);

    try {
      await grantRule(profile, denial.rule);
    } catch (err) {
      setBusyScope(null);
      setError(err instanceof Error ? err.message : 'The rule could not be written.');
      return;
    }

    registerGrantedRule(sessionId, profile, denial.rule, scope);
    dismiss(sessionId, denial.rule);
    setBusyScope(null);

    // The refused turn already ended, so the rule only takes effect on a fresh attempt.
    if (turnRunning) return;

    const target = useSessionStore.getState().sessions[sessionId];
    const lastReply = [...(target?.messages ?? [])].reverse().find(m => m.sender === 'assistant');
    if (lastReply) {
      await retryWithPermissionGrant(sessionId, lastReply.id, denial);
    }
  };

  const options: Array<{ scope: PermissionScope | 'cancel'; label: React.ReactNode }> = [
    { scope: 'once', label: copy.allow },
    {
      scope: 'conversation',
      label: (
        <>
          Yes, and always allow in this conversation for {copy.noun} that start with{' '}
          <span className="font-mono text-[10.5px] text-content-muted">'{subject}'</span>
        </>
      ),
    },
    {
      scope: 'persist',
      label: (
        <>
          Yes, and always allow for {copy.noun} that start with{' '}
          <span className="font-mono text-[10.5px] text-content-muted">'{subject}'</span> (Persist to
          settings.json)
        </>
      ),
    },
    { scope: 'cancel', label: 'No, cancel' },
  ];

  return (
    <div className="mx-3 mb-2 rounded-lg border border-edge-code bg-surface-code/60 px-3 py-2.5">
      <div className="flex items-start gap-2">
        <Lock className="mt-0.5 h-3.5 w-3.5 flex-shrink-0 text-state-warning" />

        <div className="min-w-0 flex-1">
          <p className="text-[11px] font-medium text-content-main">Requesting permission for:</p>
          <p className="mt-0.5 break-words font-mono text-[10.5px] leading-relaxed text-content-muted">
            {subject}
          </p>

          <p className="mt-2 text-[11px] font-medium text-content-main">{copy.question}</p>

          <div className="mt-1 flex flex-col">
            {options.map((option, index) => (
              <button
                key={option.scope}
                type="button"
                onClick={() => void choose(option.scope)}
                disabled={busyScope !== null}
                className={`flex items-start gap-1.5 rounded px-1 py-0.5 text-left text-[11px] leading-relaxed transition hover:bg-surface-hover disabled:opacity-50 ${
                  option.scope === 'cancel' ? 'text-content-dim' : 'text-content-main'
                }`}
              >
                <span className="mt-px font-mono text-[10px] text-content-dim">{index + 1}.</span>
                <span className="min-w-0 break-words">
                  {option.label}
                  {busyScope === option.scope ? (
                    <Loader2 className="ml-1 inline h-3 w-3 animate-spin align-text-bottom" />
                  ) : null}
                </span>
              </button>
            ))}
          </div>

          {error ? <p className="mt-1.5 text-[10.5px] leading-relaxed text-state-danger">{error}</p> : null}
          {!error ? (
            <p className="mt-2 text-[10px] leading-relaxed text-content-dim">
              A running turn cannot be interrupted for an answer, so the choice is saved as a rule and
              the turn is retried. "This conversation" ends when the chat is deleted.
            </p>
          ) : null}
        </div>
      </div>

      {denials.length > 1 ? (
        <p className="mt-1.5 pl-5 text-[10px] text-content-dim">
          {denials.length - 1} more blocked call{denials.length - 1 === 1 ? '' : 's'} after this one.
        </p>
      ) : null}
    </div>
  );
};
