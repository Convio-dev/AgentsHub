import React, { useEffect, useMemo, useState } from 'react';
import {
  Cpu,
  Sparkles,
  Plus,
  RefreshCw,
  Server,
  KeyRound,
  ShieldCheck,
  ShieldAlert,
  Unplug,
  Loader2,
  AlertTriangle,
  Globe,
  FolderTree,
  ChevronDown,
  FolderOpen,
  FileJson,
  X,
  Terminal,
  ScrollText,
  History,
  Clock,
  Trash2,
  FolderDot,
} from 'lucide-react';
import { Button } from '../components/common/Button';
import { useMcpStore, McpServer, McpTransport } from '../store/useMcpStore';
import { useSettingsStore } from '../store/useSettingsStore';
import { useWorkspaceStore } from '../store/useWorkspaceStore';
import { webviewBridge } from '../services/webviewBridge';
import { NpxInstallPanel } from '../components/mcp/NpxInstallPanel';

/**
 * Connected means the CLI has actually cached tool definitions for this server. A server that still
 * needs credentials reports no state at all — the amber auth badge already says why it cannot be used,
 * and "Connected" next to "Needs auth" reads as a contradiction.
 */
function serverState(server: McpServer): { label: string; tone: string } | null {
  if (!server.isEnabled) {
    return { label: 'Disabled', tone: 'bg-surface-strong text-content-dim border-surface-border' };
  }
  if (server.requiresAuth && server.authStatus !== 'Authenticated') {
    return null;
  }
  if (server.toolCount > 0) {
    return { label: 'Connected', tone: 'bg-emerald-500/10 text-state-success border-emerald-500/30' };
  }
  return { label: 'Ready', tone: 'bg-sky-500/10 text-accent border-sky-500/30' };
}

function AuthBadge({ server }: { server: McpServer }) {
  if (!server.requiresAuth) return null;

  const map: Record<string, { label: string; tone: string; icon: React.ReactNode }> = {
    Authenticated: {
      label: 'Authenticated',
      tone: 'bg-emerald-500/10 text-state-success border-emerald-500/30',
      icon: <ShieldCheck className="h-3 w-3" />,
    },
    TokenExpired: {
      label: 'Token expired',
      tone: 'bg-rose-500/10 text-state-danger border-rose-500/30',
      icon: <ShieldAlert className="h-3 w-3" />,
    },
    NeedsAuth: {
      label: 'Needs auth',
      tone: 'bg-amber-500/10 text-state-warning border-amber-500/30',
      icon: <ShieldAlert className="h-3 w-3" />,
    },
  };

  const entry = map[server.authStatus];
  if (!entry) return null;

  return (
    <span
      className={`flex items-center gap-1 rounded border px-1.5 py-0.5 font-mono text-[9.5px] font-bold ${entry.tone}`}
      title={server.authDetails}
    >
      {entry.icon}
      {entry.label}
    </span>
  );
}

const AuthModal: React.FC<{ server: McpServer; onClose: () => void }> = ({ server, onClose }) => {
  const { saveAuth, pendingAuth } = useMcpStore();
  const [value, setValue] = useState('');
  const [error, setError] = useState<string | null>(null);

  const isBusy = pendingAuth === server.name;
  const keyName = server.authKeyName || 'ACCESS_TOKEN';

  const submit = async () => {
    if (!value.trim()) {
      setError('Enter the credential first.');
      return;
    }

    const ok = await saveAuth(server.name, keyName, value.trim());
    if (ok) onClose();
    else setError('The host could not store this credential.');
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-surface-sunken/80 p-4 backdrop-blur-sm">
      <div className="w-full max-w-md space-y-4 rounded-2xl border border-surface-border bg-surface-card p-5 shadow-2xl">
        <div className="flex items-start gap-3">
          <div className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-xl border border-sky-500/30 bg-sky-500/10 text-accent">
            <KeyRound className="h-4 w-4" />
          </div>
          <div className="min-w-0">
            <h3 className="text-sm font-bold text-content-main">Authenticate {server.name}</h3>
            <p className="mt-1 text-[11px] leading-relaxed text-content-muted">
              The value is written to <span className="font-mono text-content-main">{keyName}</span> in this
              server's environment inside the CLI's MCP config. The server picks it up on its next start.
            </p>
          </div>
        </div>

        <label className="block space-y-1.5">
          <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">{keyName}</span>
          <input
            type="password"
            value={value}
            onChange={event => {
              setValue(event.target.value);
              setError(null);
            }}
            onKeyDown={event => {
              if (event.key === 'Enter') void submit();
              if (event.key === 'Escape') onClose();
            }}
            autoFocus
            placeholder="Paste the token"
            className="w-full rounded-lg border border-surface-border bg-surface-deep px-3 py-2 font-mono text-xs text-content-main outline-none focus:border-sky-500"
          />
        </label>

        {error ? (
          <p className="flex items-center gap-1.5 text-[11px] text-state-danger">
            <AlertTriangle className="h-3.5 w-3.5" />
            {error}
          </p>
        ) : null}

        <div className="flex justify-end gap-2 pt-1">
          <Button variant="secondary" size="xs" onClick={onClose} disabled={isBusy}>
            Cancel
          </Button>
          <Button variant="primary" size="xs" onClick={() => void submit()} disabled={isBusy}>
            {isBusy ? 'Saving…' : 'Save credential'}
          </Button>
        </div>
      </div>
    </div>
  );
};

const ConfirmDialog: React.FC<{
  title: string;
  message: string;
  onConfirm: () => void;
  onCancel: () => void;
}> = ({ title, message, onConfirm, onCancel }) => (
  <div className="fixed inset-0 z-[60] flex items-center justify-center bg-surface-sunken/80 p-4 backdrop-blur-sm">
    <div className="w-full max-w-sm space-y-3 rounded-2xl border border-surface-border bg-surface-card p-5 shadow-2xl">
      <h3 className="text-sm font-bold text-content-main">{title}</h3>
      <p className="text-[11.5px] leading-relaxed text-content-muted">{message}</p>
      <div className="flex justify-end gap-2 pt-1">
        <Button variant="secondary" size="xs" onClick={onCancel}>
          Cancel
        </Button>
        <Button variant="danger" size="xs" onClick={onConfirm}>
          Delete
        </Button>
      </div>
    </div>
  </div>
);

const RegisterModal: React.FC<{ onClose: () => void }> = ({ onClose }) => {
  const { saveServer } = useMcpStore();
  const [name, setName] = useState('');
  const [transport, setTransport] = useState<McpTransport>('stdio');
  const [command, setCommand] = useState('npx');
  const [args, setArgs] = useState('');
  const [serverUrl, setServerUrl] = useState('');
  const [description, setDescription] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const submit = async () => {
    if (!name.trim()) {
      setError('A server name is required.');
      return;
    }
    if (transport === 'sse' && !serverUrl.trim()) {
      setError('A server URL is required for an HTTP server.');
      return;
    }

    setIsSaving(true);
    const ok = await saveServer({
      name,
      transport,
      command,
      args: args.split(/\s+/).filter(Boolean),
      serverUrl,
      description,
    });
    setIsSaving(false);

    if (ok) onClose();
    else setError('The host could not write this server into the CLI config.');
  };

  const field = 'w-full rounded-lg border border-surface-border bg-surface-deep px-3 py-1.5 font-mono text-xs text-content-main outline-none focus:border-sky-500';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-surface-sunken/80 p-4 backdrop-blur-sm">
      <div className="w-full max-w-md space-y-3 rounded-2xl border border-surface-border bg-surface-card p-5 shadow-2xl">
        <div className="flex items-start justify-between">
          <div>
            <h3 className="text-sm font-bold text-content-main">Register MCP server</h3>
            <p className="mt-1 text-[11px] text-content-muted">
              Written to the CLI's <span className="font-mono">mcp_config.json</span>.
            </p>
          </div>
          <button type="button" onClick={onClose} className="text-content-dim hover:text-content-main">
            <X className="h-4 w-4" />
          </button>
        </div>

        <label className="block space-y-1">
          <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Name</span>
          <input value={name} onChange={e => setName(e.target.value)} placeholder="my-mcp-server" className={field} autoFocus />
        </label>

        <div className="flex gap-1.5">
          {(['stdio', 'sse'] as McpTransport[]).map(option => (
            <button
              key={option}
              type="button"
              onClick={() => setTransport(option)}
              className={`flex-1 rounded-lg border px-2 py-1 font-mono text-[10px] uppercase transition-colors ${
                transport === option
                  ? 'border-sky-500/50 bg-sky-500/15 text-accent'
                  : 'border-surface-border bg-surface-deep text-content-muted hover:text-content-main'
              }`}
            >
              {option}
            </button>
          ))}
        </div>

        {transport === 'stdio' ? (
          <>
            <label className="block space-y-1">
              <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Command</span>
              <input value={command} onChange={e => setCommand(e.target.value)} placeholder="npx" className={field} />
            </label>
            <label className="block space-y-1">
              <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Arguments</span>
              <input value={args} onChange={e => setArgs(e.target.value)} placeholder="-y some-mcp@latest" className={field} />
            </label>
          </>
        ) : (
          <label className="block space-y-1">
            <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Server URL</span>
            <input value={serverUrl} onChange={e => setServerUrl(e.target.value)} placeholder="https://mcp.example.com/mcp" className={field} />
          </label>
        )}

        <label className="block space-y-1">
          <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Description</span>
          <input value={description} onChange={e => setDescription(e.target.value)} placeholder="Optional" className={field} />
        </label>

        {error ? (
          <p className="flex items-center gap-1.5 text-[11px] text-state-danger">
            <AlertTriangle className="h-3.5 w-3.5" />
            {error}
          </p>
        ) : null}

        <div className="flex justify-end gap-2 pt-1">
          <Button variant="secondary" size="xs" onClick={onClose} disabled={isSaving}>
            Cancel
          </Button>
          <Button variant="primary" size="xs" onClick={() => void submit()} disabled={isSaving}>
            {isSaving ? 'Saving…' : 'Register'}
          </Button>
        </div>
      </div>
    </div>
  );
};

const NewSkillModal: React.FC<{ onClose: () => void; workspaceLabel: string }> = ({
  onClose,
  workspaceLabel,
}) => {
  const { load } = useMcpStore();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [content, setContent] = useState('');
  const [scope, setScope] = useState<'global' | 'workspace'>('workspace');
  const [error, setError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  const submit = async () => {
    if (!name.trim()) return setError('A name is required.');
    if (!description.trim()) return setError('A description is required — it tells the agent when to use this skill.');
    if (!content.trim()) return setError('Instructions are required.');

    setIsSaving(true);
    try {
      await webviewBridge.invoke('create_project_skill', {
        name: name.trim(),
        description: description.trim(),
        content,
        scope,
      });
      await load(true);
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'The host could not write this skill.');
    } finally {
      setIsSaving(false);
    }
  };

  const field =
    'w-full rounded-lg border border-surface-border bg-surface-deep px-3 py-1.5 font-mono text-xs text-content-main outline-none focus:border-sky-500';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-surface-sunken/80 p-4 backdrop-blur-sm">
      <div className="w-full max-w-lg space-y-3 rounded-2xl border border-surface-border bg-surface-card p-5 shadow-2xl">
        <div>
          <h3 className="text-sm font-bold text-content-main">Create skill</h3>
          <p className="mt-1 text-[11px] text-content-muted">
            Writes <span className="font-mono">SKILL.md</span> into{' '}
            {scope === 'global' ? <span className="font-mono">~/.agents/skills</span> : <span className="font-mono">{workspaceLabel}</span>}.
          </p>
        </div>

        <div className="space-y-1">
          <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Install to</span>
          <div className="flex gap-1.5">
            {([
              ['workspace', 'Workspace', workspaceLabel],
              ['global', 'Global', 'Your user profile'],
            ] as const).map(([value, label, hint]) => (
              <button
                key={value}
                type="button"
                onClick={() => setScope(value)}
                className={`flex-1 rounded-lg border px-2 py-1.5 text-left transition-colors ${
                  scope === value
                    ? 'border-sky-500/50 bg-sky-500/15 text-accent'
                    : 'border-surface-border bg-surface-deep text-content-muted hover:text-content-main'
                }`}
              >
                <span className="block font-mono text-[11px]">{label}</span>
                <span className="block truncate text-[9.5px] opacity-70">{hint}</span>
              </button>
            ))}
          </div>
        </div>

        <label className="block space-y-1">
          <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Name</span>
          <input value={name} onChange={e => setName(e.target.value)} placeholder="release-notes" className={field} autoFocus />
        </label>

        <label className="block space-y-1">
          <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Description (required)</span>
          <input
            value={description}
            onChange={e => setDescription(e.target.value)}
            placeholder="When the agent should use this skill"
            className={field}
          />
        </label>

        <label className="block space-y-1">
          <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Instructions (required)</span>
          <textarea
            value={content}
            onChange={e => setContent(e.target.value)}
            rows={7}
            placeholder="What the agent should do, step by step."
            className={`${field} resize-y leading-relaxed`}
          />
        </label>

        {error ? (
          <p className="flex items-center gap-1.5 text-[11px] text-state-danger">
            <AlertTriangle className="h-3.5 w-3.5" />
            {error}
          </p>
        ) : null}

        <div className="flex justify-end gap-2 pt-1">
          <Button variant="secondary" size="xs" onClick={onClose} disabled={isSaving}>
            Cancel
          </Button>
          <Button variant="primary" size="xs" onClick={() => void submit()} disabled={isSaving}>
            {isSaving ? 'Creating…' : 'Create skill'}
          </Button>
        </div>
      </div>
    </div>
  );
};

const ServerCard: React.FC<{ server: McpServer; onAuthenticate: (server: McpServer) => void }> = ({
  server,
  onAuthenticate,
}) => {
  const { revokeAuth, pendingAuth } = useMcpStore();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const state = serverState(server);
  const isBusy = pendingAuth === server.name;
  const [isToolsOpen, setIsToolsOpen] = useState(false);

  return (
    <div className="flex flex-col gap-3 rounded-xl border border-surface-border bg-surface-card p-4 shadow-sm transition-colors hover:border-sky-500/40">
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 items-center gap-2">
          <div className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg border border-sky-500/30 bg-sky-500/15 text-accent">
            <Cpu className="h-3.5 w-3.5" />
          </div>
          <span className="truncate font-mono text-xs font-bold text-content-main">{server.name}</span>
        </div>

        <div className="flex flex-shrink-0 items-center gap-1.5">
          <AuthBadge server={server} />
          {state ? (
            <span className={`rounded border px-1.5 py-0.5 font-mono text-[9.5px] font-bold ${state.tone}`}>
              {state.label}
            </span>
          ) : null}
          <button
            type="button"
            onClick={() => setConfirmDelete(true)}
            title="Remove this server"
            className="rounded p-0.5 text-content-dim transition-colors hover:bg-rose-500/10 hover:text-state-danger"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      <p className="line-clamp-2 flex-1 text-[11.5px] leading-relaxed text-content-muted">
        {server.description || 'No description reported by the host.'}
      </p>

      <div className="flex items-center gap-2 font-mono text-[10px] text-content-dim">
        <span className="rounded border border-surface-border bg-surface-deep px-1.5 py-0.5 uppercase">
          {server.transport}
        </span>
        <button
          type="button"
          onClick={() => setIsToolsOpen(open => !open)}
          disabled={server.tools.length === 0}
          className="rounded px-1 py-0.5 transition-colors hover:bg-surface-hover hover:text-content-main disabled:cursor-default disabled:hover:bg-transparent"
          title={server.tools.length > 0 ? 'Show the tools this server exposes' : 'No tool list cached yet'}
        >
          {server.toolCount} tools {server.tools.length > 0 ? (isToolsOpen ? '▾' : '▸') : ''}
        </button>
        {server.serverUrl ? (
          <span className="truncate opacity-80" title={server.serverUrl}>
            {server.serverUrl.replace(/^https?:\/\//, '')}
          </span>
        ) : server.command ? (
          <span className="truncate opacity-80" title={server.command}>
            {server.command}
          </span>
        ) : null}
      </div>

      {isToolsOpen && server.tools.length > 0 ? (
        <div className="max-h-28 overflow-y-auto rounded-lg border border-surface-border/60 bg-surface-deep p-2">
          <div className="flex flex-wrap gap-1">
            {server.tools.map(tool => (
              <span
                key={tool}
                className="rounded border border-surface-border bg-surface-card px-1.5 py-0.5 font-mono text-[9px] text-content-muted"
              >
                {tool}
              </span>
            ))}
          </div>
        </div>
      ) : null}

      {confirmDelete ? (
        <ConfirmDialog
          title={`Remove ${server.name}?`}
          message="This deletes the server entry from the CLI's MCP configuration. Its cached tool list is left alone."
          onCancel={() => setConfirmDelete(false)}
          onConfirm={() => {
            setConfirmDelete(false);
            void useMcpStore.getState().deleteServer(server.id);
          }}
        />
      ) : null}

      {server.requiresAuth ? (
        <div className="flex items-center justify-between gap-2 border-t border-surface-border/60 pt-2.5">
          <span className="truncate font-mono text-[9.5px] text-content-dim" title={server.authDetails}>
            {server.authDetails || (server.authKeyName ?? 'OAuth')}
          </span>

          {server.authStatus === 'Authenticated' ? (
            <button
              type="button"
              onClick={() => void revokeAuth(server)}
              disabled={isBusy}
              className="flex flex-shrink-0 items-center gap-1 rounded-md border border-rose-500/30 bg-rose-500/10 px-2 py-0.5 text-[10px] font-medium text-state-danger transition-colors hover:bg-rose-500/20 disabled:opacity-60"
            >
              {isBusy ? <Loader2 className="h-3 w-3 animate-spin" /> : <Unplug className="h-3 w-3" />}
              Unlink
            </button>
          ) : (
            <button
              type="button"
              onClick={() => onAuthenticate(server)}
              disabled={isBusy}
              className="flex flex-shrink-0 items-center gap-1 rounded-md border border-amber-500/40 bg-amber-500/10 px-2 py-0.5 text-[10px] font-medium text-state-warning transition-colors hover:bg-amber-500/20 disabled:opacity-60"
            >
              <KeyRound className="h-3 w-3" />
              Authenticate
            </button>
          )}
        </div>
      ) : null}
    </div>
  );
};

export const McpSkillsView: React.FC = () => {
  const { servers, skills, isLoading, hasLoaded, error, load, toggleSkill, pendingSkill, lastSyncedAt, revealSkill } =
    useMcpStore();
  const [authTarget, setAuthTarget] = useState<McpServer | null>(null);
  const [isRegisterOpen, setIsRegisterOpen] = useState(false);
  const [installRequest, setInstallRequest] = useState<{ command: string; scope: 'global' | 'workspace' } | null>(null);
  const [npxScope, setNpxScope] = useState<'global' | 'workspace'>('workspace');
  const [isNpxPromptOpen, setIsNpxPromptOpen] = useState(false);
  const [isSkillModalOpen, setIsSkillModalOpen] = useState(false);
  const [npxPackage, setNpxPackage] = useState('antislop-ai');
  const [activeTab, setActiveTab] = useState<'mcp' | 'skills' | 'rules' | 'sessions' | 'automations'>('mcp');
  // One pending destructive action at a time, shared by rules and sessions.
  const [pendingDelete, setPendingDelete] = useState<
    { kind: 'rule' | 'session'; id: string; label: string; transcriptPath?: string } | null
  >(null);

  // The scope picker names the actual workspace, not a generic "current project folder".
  const activeWorkspace = useWorkspaceStore(state => state.activeWorkspace);
  const workspaceLabel = activeWorkspace?.name?.trim() || 'Current project folder';

  const {
    agentRules,
    agentSessions,
    automations,
    loadAgentRules,
    loadAgentSessions,
    loadAutomations,
  } = useSettingsStore();
  const [openSections, setOpenSections] = useState({ mcp: true, skills: true });

  const toggleSection = (key: 'mcp' | 'skills') =>
    setOpenSections(current => ({ ...current, [key]: !current[key] }));

  const startInstall = () => {
    const pkg = npxPackage.trim();
    if (!pkg) return;
    setInstallRequest({ command: `npx -y ${pkg}`, scope: npxScope });
    setIsNpxPromptOpen(false);
  };

  const syncedLabel = lastSyncedAt
    ? new Date(lastSyncedAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
    : null;

  useEffect(() => {
    void load();
  }, [load]);

  // The other three tabs read the same store the Settings page uses, so both show one truth.
  useEffect(() => {
    if (activeTab === 'rules') void loadAgentRules();
    if (activeTab === 'sessions') void loadAgentSessions();
    if (activeTab === 'automations') void loadAutomations();
  }, [activeTab, loadAgentRules, loadAgentSessions, loadAutomations]);

  const skillGroups = useMemo(() => {
    const workspace = skills.filter(skill => skill.scope === 'Workspace');
    const global = skills.filter(skill => skill.scope !== 'Workspace');
    return { workspace, global };
  }, [skills]);

  const authRequired = servers.filter(server => server.requiresAuth && server.authStatus !== 'Authenticated').length;

  return (
    <div className="flex-1 select-none overflow-y-auto bg-surface-deep p-6 font-sans text-content-main">
      <div className="flex items-start justify-between gap-4 border-b border-surface-border pb-4">
        <div>
          <h1 className="text-lg font-bold tracking-tight text-content-main">MCP Servers &amp; Agent Skills</h1>
          <p className="mt-0.5 font-mono text-xs text-content-dim">
            {hasLoaded
              ? `${servers.length} servers · ${skills.length} skills${authRequired > 0 ? ` · ${authRequired} need auth` : ''}${syncedLabel ? ` · synced ${syncedLabel}` : ''}`
              : 'Scanning the CLI configuration…'}
          </p>
        </div>

        <div className="flex flex-shrink-0 items-center gap-2">
          <Button
            variant="secondary"
            size="xs"
            onClick={() => void load(true)}
            disabled={isLoading}
            leftIcon={<RefreshCw className={`h-3.5 w-3.5 ${isLoading ? 'animate-spin' : ''}`} />}
          >
            {isLoading ? 'Syncing…' : 'Sync'}
          </Button>
          <Button
            variant="secondary"
            size="xs"
            onClick={() => setIsNpxPromptOpen(true)}
            leftIcon={<Terminal className="h-3.5 w-3.5" />}
          >
            Install via NPX
          </Button>
          <Button
            variant="secondary"
            size="xs"
            onClick={() => setIsSkillModalOpen(true)}
            leftIcon={<Sparkles className="h-3.5 w-3.5" />}
          >
            New Skill
          </Button>
          <Button
            variant="primary"
            size="xs"
            onClick={() => setIsRegisterOpen(true)}
            leftIcon={<Plus className="h-3.5 w-3.5" />}
          >
            Register Server
          </Button>
        </div>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-1.5 border-b border-surface-border pb-2">
        {([
          ['mcp', 'MCP Servers', servers.length],
          ['skills', 'Agent Skills', skills.length],
          ['rules', 'Agent Rules', agentRules.length],
          ['sessions', 'Agent Sessions', agentSessions.length],
          ['automations', 'Automations', automations.length],
        ] as const).map(([key, label, count]) => (
          <button
            key={key}
            type="button"
            onClick={() => setActiveTab(key)}
            className={`rounded-lg border px-2.5 py-1 font-mono text-[10.5px] transition-colors ${
              activeTab === key
                ? 'border-sky-500/50 bg-sky-500/10 text-accent'
                : 'border-transparent text-content-muted hover:bg-surface-hover hover:text-content-main'
            }`}
          >
            {label} ({count})
          </button>
        ))}
      </div>

      {error ? (
        <div className="mt-4 flex items-center gap-2 rounded-lg border border-rose-500/30 bg-rose-500/10 px-3 py-2 text-[11px] text-state-danger">
          <AlertTriangle className="h-3.5 w-3.5" />
          {error}
        </div>
      ) : null}

      {activeTab === 'mcp' ? (
      <section className="mt-5 space-y-3">
        <div className="flex items-center justify-between gap-3">
          <button
            type="button"
            onClick={() => toggleSection('mcp')}
            className="flex items-center gap-2 font-mono text-xs font-bold uppercase tracking-wider text-content-main transition-colors hover:text-accent"
          >
            <ChevronDown className={`h-3.5 w-3.5 transition-transform ${openSections.mcp ? '' : '-rotate-90'}`} />
            <Server className="h-3.5 w-3.5 text-accent" />
            MCP Servers
            <span className="font-normal text-content-dim">({servers.length})</span>
          </button>

          <button
            type="button"
            onClick={() => void webviewBridge.invoke('open_mcp_config').catch(() => {})}
            title="Open the CLI's mcp_config.json in your editor"
            className="flex items-center gap-1.5 rounded-md border border-surface-border bg-surface-deep px-2 py-0.5 font-mono text-[10px] text-content-muted transition-colors hover:border-sky-500/50 hover:text-accent"
          >
            <FileJson className="h-3 w-3" />
            View Raw Config
          </button>
        </div>

        {!openSections.mcp ? null : !hasLoaded && isLoading ? (
          <div className="grid grid-cols-1 gap-3.5 md:grid-cols-2">
            {[0, 1, 2, 3].map(index => (
              <div key={index} className="h-[148px] animate-pulse rounded-xl border border-surface-border bg-surface-card" />
            ))}
          </div>
        ) : servers.length === 0 ? (
          <p className="rounded-xl border border-dashed border-surface-border bg-surface-card px-4 py-8 text-center font-mono text-[11px] text-content-dim">
            No MCP servers found in the CLI configuration.
          </p>
        ) : (
          <div className="grid grid-cols-1 gap-3.5 md:grid-cols-2">
            {servers.map(server => (
              <ServerCard key={server.id || server.name} server={server} onAuthenticate={setAuthTarget} />
            ))}
          </div>
        )}
      </section>
      ) : null}

      {activeTab === 'skills' ? (
      <section className="mt-5 space-y-3">
        <button
          type="button"
          onClick={() => toggleSection('skills')}
          className="flex w-full items-center gap-2 font-mono text-xs font-bold uppercase tracking-wider text-content-main transition-colors hover:text-accent"
        >
          <ChevronDown className={`h-3.5 w-3.5 transition-transform ${openSections.skills ? '' : '-rotate-90'}`} />
          <Sparkles className="h-3.5 w-3.5 text-accent" />
          Agent Skills
          <span className="font-normal text-content-dim">({skills.length})</span>
        </button>

        {!openSections.skills ? null : skills.length === 0 ? (
          <p className="rounded-xl border border-dashed border-surface-border bg-surface-card px-4 py-8 text-center font-mono text-[11px] text-content-dim">
            {hasLoaded ? 'No skills found in the workspace or global skill directories.' : 'Scanning…'}
          </p>
        ) : (
          [
            { key: 'Workspace', icon: <FolderTree className="h-3 w-3" />, rows: skillGroups.workspace },
            { key: 'Global', icon: <Globe className="h-3 w-3" />, rows: skillGroups.global },
          ].map(group =>
            group.rows.length === 0 ? null : (
              <div key={group.key} className="space-y-2">
                <div className="flex items-center gap-1.5 font-mono text-[10px] uppercase tracking-[0.06em] text-content-dim">
                  {group.icon}
                  {group.key}
                  <span className="opacity-60">({group.rows.length})</span>
                </div>

                <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
                  {group.rows.map(skill => {
                    const isBusy = pendingSkill === skill.directoryPath;

                    return (
                      <div
                        key={skill.directoryPath || skill.id}
                        className={`flex flex-col gap-2 rounded-xl border bg-surface-card p-3.5 shadow-sm transition-colors ${
                          skill.isEnabled ? 'border-surface-border' : 'border-surface-border/60 opacity-70'
                        }`}
                      >
                        <div className="flex items-start justify-between gap-2">
                          <span className="flex min-w-0 items-center gap-1.5 font-mono text-xs font-bold text-accent">
                            <Sparkles className="h-3.5 w-3.5 flex-shrink-0" />
                            <span className="truncate" title={skill.name}>
                              {skill.name}
                            </span>
                          </span>

                          <button
                            type="button"
                            onClick={() => void toggleSkill(skill.directoryPath, !skill.isEnabled)}
                            disabled={isBusy}
                            title={
                              skill.isEnabled
                                ? 'Disable — renames SKILL.md so the agent stops loading it'
                                : 'Enable — restores SKILL.md'
                            }
                            className={`relative h-4 w-7 flex-shrink-0 rounded-full border transition-colors ${
                              skill.isEnabled
                                ? 'border-emerald-500/40 bg-emerald-500/30'
                                : 'border-surface-border bg-surface-strong'
                            } disabled:opacity-60`}
                          >
                            <span
                              className={`absolute top-0.5 h-3 w-3 rounded-full bg-white transition-all ${
                                skill.isEnabled ? 'left-3.5' : 'left-0.5'
                              }`}
                            />
                            {isBusy ? (
                              <Loader2 className="absolute inset-0 m-auto h-3 w-3 animate-spin text-content-main" />
                            ) : null}
                          </button>
                        </div>

                        <p className="line-clamp-2 text-[11px] leading-relaxed text-content-muted">
                          {skill.description || 'No description in frontmatter.'}
                        </p>

                        <div className="flex items-center justify-between gap-2 font-mono text-[9.5px] text-content-dim">
                          <div className="flex min-w-0 items-center gap-1.5">
                            <span
                              className={`flex-shrink-0 rounded border px-1.5 py-0.5 uppercase ${
                                skill.scope === 'Workspace'
                                  ? 'border-sky-500/30 bg-sky-500/10 text-accent'
                                  : 'border-surface-border bg-surface-deep text-content-muted'
                              }`}
                            >
                              {skill.scope}
                            </span>
                            {skill.scope === 'Workspace' && skill.workspaceName ? (
                              <span
                                className="truncate text-content-muted"
                                title={`Owned by workspace ${skill.workspaceName}`}
                              >
                                {skill.workspaceName}
                              </span>
                            ) : null}
                          </div>

                          <div className="flex flex-shrink-0 items-center gap-1.5">
                            <span>{skill.isEnabled ? 'Active' : 'Disabled'}</span>
                            <button
                              type="button"
                              onClick={() => void revealSkill(skill.directoryPath)}
                              title="Show this skill's folder"
                              className="rounded p-0.5 text-content-dim transition-colors hover:bg-surface-hover hover:text-content-main"
                            >
                              <FolderOpen className="h-3.5 w-3.5" />
                            </button>
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )
          )
        )}
      </section>
      ) : null}

      {activeTab === 'rules' ? (
        <section className="mt-5 space-y-2">
          {agentRules.length === 0 ? (
            <p className="rounded-xl border border-dashed border-surface-border bg-surface-card px-4 py-8 text-center font-mono text-[11px] text-content-dim">
              No agent rules configured.
            </p>
          ) : (
            agentRules.map(rule => (
              <div key={rule.id} className="flex items-center justify-between gap-3 rounded-xl border border-surface-border bg-surface-card px-4 py-2.5">
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <ScrollText className="h-3.5 w-3.5 flex-shrink-0 text-accent" />
                    <span className="truncate font-mono text-xs text-content-main">{rule.name}</span>
                  </div>
                  <p className="mt-0.5 truncate text-[11px] text-content-muted">{rule.description || rule.sourceFilePath}</p>
                </div>
                <div className="flex flex-shrink-0 items-center gap-2">
                  <span className="rounded border border-surface-border bg-surface-deep px-1.5 py-0.5 font-mono text-[9.5px] uppercase text-content-dim">
                    {rule.scope}
                  </span>
                  <button
                    type="button"
                    onClick={() => setPendingDelete({ kind: 'rule', id: rule.id, label: rule.name })}
                    title="Delete this rule"
                    className="rounded p-0.5 text-content-dim transition-colors hover:bg-rose-500/10 hover:text-state-danger"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              </div>
            ))
          )}
        </section>
      ) : null}

      {activeTab === 'sessions' ? (
        <section className="mt-5 space-y-2">
          {agentSessions.length === 0 ? (
            <p className="rounded-xl border border-dashed border-surface-border bg-surface-card px-4 py-8 text-center font-mono text-[11px] text-content-dim">
              No agent sessions recorded yet.
            </p>
          ) : (
            agentSessions.slice(0, 50).map(session => {
              const folder = session.workspaceDir
                ? session.workspaceDir.split(/[\\/]/).filter(Boolean).pop() || session.workspaceDir
                : 'no workspace';
              const created = session.createdAt
                ? new Date(session.createdAt).toLocaleString([], {
                    month: 'short',
                    day: 'numeric',
                    hour: '2-digit',
                    minute: '2-digit',
                  })
                : '—';

              return (
                <div
                  key={session.sessionId}
                  className="flex items-center justify-between gap-3 rounded-xl border border-surface-border bg-surface-card px-4 py-2.5"
                >
                  <div className="min-w-0">
                    <div className="flex min-w-0 items-center gap-2">
                      <History className="h-3.5 w-3.5 flex-shrink-0 text-accent" />
                      <span className="truncate font-mono text-xs text-content-main">
                        {session.sessionId.slice(0, 8)}…{session.sessionId.slice(-4)}
                      </span>
                      <span className="flex-shrink-0 rounded border border-surface-border bg-surface-deep px-1.5 py-0.5 font-mono text-[9px] text-content-muted">
                        {session.stepCount ?? 0} steps
                      </span>
                      {session.status ? (
                        <span
                          className={`flex-shrink-0 rounded border px-1.5 py-0.5 font-mono text-[9px] ${
                            session.status === 'completed'
                              ? 'border-emerald-500/30 bg-emerald-500/10 text-state-success'
                              : 'border-surface-border bg-surface-deep text-content-muted'
                          }`}
                        >
                          {session.status}
                        </span>
                      ) : null}
                    </div>

                    <div className="mt-1 flex min-w-0 items-center gap-1.5 font-mono text-[10px] text-content-dim">
                      <FolderDot className="h-3 w-3 flex-shrink-0" />
                      <span className="truncate" title={session.workspaceDir}>
                        {folder}
                      </span>
                      <span className="opacity-60">·</span>
                      <span className="flex-shrink-0">{created}</span>
                    </div>
                  </div>

                  <button
                    type="button"
                    onClick={() =>
                      setPendingDelete({
                        kind: 'session',
                        id: session.sessionId,
                        label: session.sessionId.slice(0, 8),
                        transcriptPath: session.transcriptPath,
                      })
                    }
                    title="Delete this session transcript"
                    className="flex-shrink-0 rounded p-1 text-content-dim transition-colors hover:bg-rose-500/10 hover:text-state-danger"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              );
            })
          )}
        </section>
      ) : null}

      {activeTab === 'automations' ? (
        <section className="mt-5 space-y-2">
          {automations.length === 0 ? (
            <p className="rounded-xl border border-dashed border-surface-border bg-surface-card px-4 py-8 text-center font-mono text-[11px] text-content-dim">
              No scheduled automations.
            </p>
          ) : (
            automations.map(task => (
              <div key={task.id} className="flex items-center justify-between gap-3 rounded-xl border border-surface-border bg-surface-card px-4 py-2.5">
                <div className="flex min-w-0 items-center gap-2">
                  <Clock className="h-3.5 w-3.5 flex-shrink-0 text-accent" />
                  <span className="truncate font-mono text-xs text-content-main">{task.name}</span>
                </div>
                <span className="flex-shrink-0 font-mono text-[9.5px] text-content-dim">{task.scheduleExpression}</span>
              </div>
            ))
          )}
        </section>
      ) : null}

      {authTarget ? <AuthModal server={authTarget} onClose={() => setAuthTarget(null)} /> : null}
      {isRegisterOpen ? <RegisterModal onClose={() => setIsRegisterOpen(false)} /> : null}
      {isSkillModalOpen ? (
        <NewSkillModal onClose={() => setIsSkillModalOpen(false)} workspaceLabel={workspaceLabel} />
      ) : null}

      {pendingDelete ? (
        <ConfirmDialog
          title={pendingDelete.kind === 'rule' ? 'Delete this rule?' : 'Delete this session?'}
          message={
            pendingDelete.kind === 'rule'
              ? `"${pendingDelete.label}" will be removed from the agent rules.`
              : `The transcript for ${pendingDelete.label} will be deleted from disk. This cannot be undone.`
          }
          onCancel={() => setPendingDelete(null)}
          onConfirm={() => {
            const target = pendingDelete;
            setPendingDelete(null);

            if (target.kind === 'rule') {
              void webviewBridge
                .invoke('delete_agent_rule', { id: target.id })
                .then(() => loadAgentRules())
                .catch(() => {});
            } else {
              void webviewBridge
                .invoke('delete_agent_session', { transcriptPath: target.transcriptPath })
                .then(() => loadAgentSessions())
                .catch(() => {});
            }
          }}
        />
      ) : null}

      {isNpxPromptOpen ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-surface-sunken/80 p-4 backdrop-blur-sm">
          <div className="w-full max-w-sm space-y-3 rounded-2xl border border-surface-border bg-surface-card p-5 shadow-2xl">
            <div>
              <h3 className="text-sm font-bold text-content-main">Install via NPX</h3>
              <p className="mt-1 text-[11px] text-content-muted">
                Runs in an interactive console so you can answer the installer's prompts.
              </p>
            </div>

            <div className="space-y-1">
              <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Install to</span>
              <div className="flex gap-1.5">
                {([
                  ['workspace', 'Workspace', workspaceLabel],
                  ['global', 'Global', 'Your user profile'],
                ] as const).map(([value, label, hint]) => (
                  <button
                    key={value}
                    type="button"
                    onClick={() => setNpxScope(value)}
                    title={hint}
                    className={`flex-1 rounded-lg border px-2 py-1.5 text-left transition-colors ${
                      npxScope === value
                        ? 'border-sky-500/50 bg-sky-500/15 text-accent'
                        : 'border-surface-border bg-surface-deep text-content-muted hover:text-content-main'
                    }`}
                  >
                    <span className="block font-mono text-[11px]">{label}</span>
                    <span className="block text-[9.5px] opacity-70">{hint}</span>
                  </button>
                ))}
              </div>
            </div>

            <label className="block space-y-1">
              <span className="font-mono text-[10px] uppercase tracking-wide text-content-dim">Package</span>
              <input
                value={npxPackage}
                onChange={e => setNpxPackage(e.target.value)}
                onKeyDown={e => {
                  if (e.key === 'Enter') startInstall();
                }}
                className="w-full rounded-lg border border-surface-border bg-surface-deep px-3 py-1.5 font-mono text-xs text-content-main outline-none focus:border-sky-500"
                autoFocus
              />
              <span className="block text-[10px] text-content-dim">
                Any npm package — runs <span className="font-mono">npx -y &lt;package&gt;</span>. The default is
                only a suggestion.
              </span>
            </label>

            <div className="flex justify-end gap-2 pt-1">
              <Button variant="secondary" size="xs" onClick={() => setIsNpxPromptOpen(false)}>
                Cancel
              </Button>
              <Button variant="primary" size="xs" onClick={startInstall}>
                Start install
              </Button>
            </div>
          </div>
        </div>
      ) : null}

      {installRequest ? (
        <NpxInstallPanel
          command={installRequest.command}
          scope={installRequest.scope}
          title="Install via NPX"
          onClose={() => {
            setInstallRequest(null);
            void load(true);
          }}
        />
      ) : null}
    </div>
  );
};
