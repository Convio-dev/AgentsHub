import React, { useEffect } from 'react';
import {
  ArrowLeft,
  ArrowRight,
  Check,
  Compass,
  Cpu,
  Download,
  FolderTree,
  Layers,
  MessageSquare,
  ShieldCheck,
  Sparkles,
  Terminal,
  X,
} from 'lucide-react';
import { Button } from '../common/Button';
import { useGuideStore } from '../../store/useGuideStore';
import { useSidecarStore } from '../../store/useSidecarStore';

/**
 * The user guide. Opened once on a fresh install, then from F1 or Settings → About.
 *
 * Copy here names what the program actually ships with: the five chat engines, the folders it
 * reads, and the runtimes the dependency checker installs. Nothing is described that the app
 * cannot open for you in one click.
 */
const STEPS = [
  { id: 'how', label: 'How it works', icon: Compass },
  { id: 'chat', label: 'Chats', icon: MessageSquare },
  { id: 'runtime', label: 'Runtimes', icon: Cpu },
  { id: 'keys', label: 'Shortcuts', icon: Terminal },
] as const;

const TILE = 'rounded-lg border border-surface-border bg-surface-deep p-3';
const TILE_TITLE = 'flex items-center gap-1.5 text-xs font-bold text-content-main';
const BODY = 'text-[11.5px] leading-relaxed text-content-muted';

export const UserGuideModal: React.FC = () => {
  const { isOpen, step, setStep, close } = useGuideStore();
  const { setActiveView, setSettingsCategory } = useSidecarStore();

  // Escape closes it from anywhere, the same as every other overlay in the app.
  useEffect(() => {
    if (!isOpen) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') close();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [isOpen, close]);

  if (!isOpen) return null;

  const go = (view: 'chat' | 'profiles' | 'settings' | 'mcpskills', category?: string) => {
    if (category) setSettingsCategory(category);
    setActiveView(view);
    close();
  };

  const current = STEPS[step] ?? STEPS[0];

  return (
    <div className="fixed inset-0 z-[70] flex items-center justify-center bg-black/70 p-4 backdrop-blur-sm">
      <div className="flex max-h-[86vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl border border-surface-border bg-surface-card shadow-2xl">
        <div className="flex items-start justify-between gap-3 border-b border-surface-border p-4">
          <div className="flex min-w-0 items-center gap-3">
            <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl border border-sky-500/30 bg-sky-500/10 text-accent">
              <Layers className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <h2 className="text-sm font-bold text-content-main">AgentsHub guide</h2>
              <p className="font-mono text-[10px] text-content-dim">F1 opens or closes this · Esc closes it</p>
            </div>
          </div>

          <button
            type="button"
            onClick={close}
            title="Close (Esc)"
            className="rounded-lg p-1 text-content-dim transition-colors hover:bg-surface-hover hover:text-content-main"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        <div className="flex gap-1 overflow-x-auto border-b border-surface-border px-3 pt-2">
          {STEPS.map((entry, index) => {
            const Icon = entry.icon;
            const active = index === step;

            return (
              <button
                key={entry.id}
                type="button"
                onClick={() => setStep(index)}
                className={`flex flex-shrink-0 items-center gap-1.5 rounded-t-lg border-b-2 px-2.5 py-1.5 font-mono text-[10.5px] transition-colors ${
                  active
                    ? 'border-sky-500 text-accent'
                    : 'border-transparent text-content-muted hover:text-content-main'
                }`}
              >
                <Icon className="h-3 w-3" />
                {index + 1}. {entry.label}
              </button>
            );
          })}
        </div>

        <div className="flex-1 space-y-3 overflow-y-auto p-4">
          {current.id === 'how' ? (
            <>
              <p className={BODY}>
                One window for the whole agent setup: pick a project folder, chat with a coding agent in it, and
                watch the same work in a real terminal. Everything runs on this machine — no hosted backend holds
                your code.
              </p>

              <div className="grid grid-cols-1 gap-2.5 md:grid-cols-2">
                <div className={TILE}>
                  <div className={TILE_TITLE}>
                    <FolderTree className="h-3.5 w-3.5 text-accent" />
                    Workspace
                  </div>
                  <p className={`mt-1 ${BODY}`}>
                    The folder an agent is allowed to edit. Every chat, terminal and git action is scoped to it.
                    Switch workspaces from the header.
                  </p>
                </div>
                <div className={TILE}>
                  <div className={TILE_TITLE}>
                    <MessageSquare className="h-3.5 w-3.5 text-accent" />
                    Chat
                  </div>
                  <p className={`mt-1 ${BODY}`}>
                    Talk to an agent in normal chat. The model and engine are picked per session in the chat header,
                    so two tabs can run different engines on the same workspace.
                  </p>
                </div>
                <div className={TILE}>
                  <div className={TILE_TITLE}>
                    <Terminal className="h-3.5 w-3.5 text-accent" />
                    Terminals
                  </div>
                  <p className={`mt-1 ${BODY}`}>
                    Real ConPTY shells, embedded or popped out. Useful for watching what an agent actually runs,
                    or for driving a CLI yourself.
                  </p>
                </div>
                <div className={TILE}>
                  <div className={TILE_TITLE}>
                    <ShieldCheck className="h-3.5 w-3.5 text-accent" />
                    Profiles
                  </div>
                  <p className={`mt-1 ${BODY}`}>
                    Each profile keeps its own sign-in and config on disk. Logging into a second account never
                    logs you out of the first.
                  </p>
                </div>
              </div>

              <div className={`${TILE} border-sky-500/30`}>
                <div className={TILE_TITLE}>
                  <Sparkles className="h-3.5 w-3.5 text-accent" />
                  First run, in order
                </div>
                <ol className={`mt-1.5 space-y-1 ${BODY}`}>
                  <li>1. Point a workspace at your project folder.</li>
                  <li>2. Install the runtimes the engines need (next slide).</li>
                  <li>3. Sign in to the engine you want to use.</li>
                  <li>4. Open Chat and send a prompt.</li>
                </ol>
              </div>
            </>
          ) : null}

          {current.id === 'chat' ? (
            <>
              <p className={BODY}>
                Five engines run inside the app. Four of them are CLIs already on your machine; the chat header
                shows which one the current session uses, and the model picker next to it is per session.
              </p>

              <div className="space-y-1.5">
                {[
                  ['Antigravity CLI', 'Terminal agent, one sign-in per profile. The usual default.'],
                  ['Claude Code', 'Terminal agent, needs Node.js.'],
                  ['OpenCode', 'Terminal agent with a free-model filter in its picker.'],
                  ['Vyce', 'Terminal agent that streams its own tool calls.'],
                  ['Qwen', 'Terminal agent from the Qwen Code CLI.'],
                  ['DeepSeek', 'Drives chat.deepseek.com in its own browser profile — no API key needed.'],
                ].map(([name, detail]) => (
                  <div key={name} className="flex items-start gap-2 rounded-lg border border-surface-border bg-surface-deep px-3 py-2">
                    <Check className="mt-0.5 h-3 w-3 flex-shrink-0 text-state-success" />
                    <div className="min-w-0">
                      <span className="font-mono text-[11px] font-bold text-content-main">{name}</span>
                      <p className={`${BODY} ml-2 inline`}> {detail}</p>
                    </div>
                  </div>
                ))}
              </div>

              <p className={BODY}>
                While a turn is running the composer stays usable: anything you send is queued and goes out as soon
                as the current answer finishes.
              </p>

              <div className="flex flex-wrap gap-2">
                <Button variant="primary" size="xs" onClick={() => go('chat')} leftIcon={<MessageSquare className="h-3.5 w-3.5" />}>
                  Open chat
                </Button>
                <Button variant="secondary" size="xs" onClick={() => go('profiles')} leftIcon={<ShieldCheck className="h-3.5 w-3.5" />}>
                  Manage profiles and sign-ins
                </Button>
              </div>
            </>
          ) : null}

          {current.id === 'runtime' ? (
            <>
              <p className={BODY}>
                Settings → AI Engines &amp; Runtimes checks every item below and installs the missing ones for you.
                Nothing has to be downloaded by hand.
              </p>

              <div className="space-y-2">
                <div className={TILE}>
                  <div className={TILE_TITLE}>
                    <Cpu className="h-3.5 w-3.5 text-accent" />
                    Required by the engines
                  </div>
                  <p className={`mt-1 ${BODY}`}>
                    Node.js LTS and Git for Windows are needed by most CLIs; Python 3.10+ by Aider and the skill
                    scripts. Missing any of them shows up as a red row on that settings page.
                  </p>
                </div>

                <div className={TILE}>
                  <div className={TILE_TITLE}>
                    <Download className="h-3.5 w-3.5 text-accent" />
                    The engines themselves
                  </div>
                  <p className={`mt-1 ${BODY}`}>
                    Antigravity CLI, Claude Code, OpenCode, Vyce, Qwen Code, Codex, Crush and Aider are listed with
                    their install command. <span className="text-content-main">Install Missing</span> runs them all,
                    and each row can be installed or rechecked on its own.
                  </p>
                </div>

                <div className={TILE}>
                  <div className={TILE_TITLE}>
                    <ShieldCheck className="h-3.5 w-3.5 text-accent" />
                    Sign-ins are separate
                  </div>
                  <p className={`mt-1 ${BODY}`}>
                    Installing an engine does not log it in. Antigravity and the browser engines sign in per profile,
                    from Profiles; the rest sign in on first launch.
                  </p>
                </div>
              </div>

              <div className="flex flex-wrap gap-2">
                <Button
                  variant="primary"
                  size="xs"
                  onClick={() => go('settings', 'engines')}
                  leftIcon={<Download className="h-3.5 w-3.5" />}
                >
                  Open dependency installer
                </Button>
                <Button
                  variant="secondary"
                  size="xs"
                  onClick={() => go('settings', 'engines')}
                  leftIcon={<Cpu className="h-3.5 w-3.5" />}
                >
                  Check what is installed
                </Button>
                <Button variant="secondary" size="xs" onClick={() => go('mcpskills')}>
                  MCP servers and skills
                </Button>
              </div>
            </>
          ) : null}

          {current.id === 'keys' ? (
            <>
              <p className={BODY}>The shortcuts worth remembering.</p>

              <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
                {[
                  ['F1', 'Open or close this guide'],
                  ['Esc', 'Close any overlay'],
                  ['Ctrl + Enter', 'Send the prompt'],
                  ['Ctrl + C', 'Interrupt the agent'],
                  ['Ctrl + /', 'Toggle the side panel'],
                  ['Ctrl + Alt + H', 'Show or hide the window'],
                ].map(([key, desc]) => (
                  <div
                    key={key}
                    className="flex items-center justify-between gap-2 rounded-lg border border-surface-border bg-surface-deep px-3 py-1.5"
                  >
                    <span className="text-[11.5px] text-content-muted">{desc}</span>
                    <kbd className="rounded border border-surface-border bg-surface-card px-1.5 py-0.5 font-mono text-[10px] font-bold text-accent">
                      {key}
                    </kbd>
                  </div>
                ))}
              </div>

              <p className={BODY}>
                This guide stays reachable from F1 and from Settings → About &amp; Updates. It will not open by
                itself again.
              </p>
            </>
          ) : null}
        </div>

        <div className="flex items-center justify-between gap-2 border-t border-surface-border bg-surface-deep px-4 py-2.5">
          <div className="flex items-center gap-1.5">
            {STEPS.map((entry, index) => (
              <button
                key={entry.id}
                type="button"
                onClick={() => setStep(index)}
                title={entry.label}
                className={`h-1.5 rounded-full transition-all ${
                  index === step ? 'w-5 bg-sky-500' : 'w-1.5 bg-surface-strong hover:bg-content-dim'
                }`}
              />
            ))}
          </div>

          <div className="flex items-center gap-2">
            <Button variant="secondary" size="xs" disabled={step === 0} onClick={() => setStep(step - 1)} leftIcon={<ArrowLeft className="h-3.5 w-3.5" />}>
              Back
            </Button>

            {step < STEPS.length - 1 ? (
              <Button variant="primary" size="xs" onClick={() => setStep(step + 1)} rightIcon={<ArrowRight className="h-3.5 w-3.5" />}>
                Next
              </Button>
            ) : (
              <Button variant="primary" size="xs" onClick={close} leftIcon={<Check className="h-3.5 w-3.5" />}>
                Done
              </Button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
