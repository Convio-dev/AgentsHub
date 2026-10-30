import React, { useCallback, useEffect, useState } from 'react';
import { Brain, Check, Loader2, LogIn, Plus, Trash2 } from 'lucide-react';
import { webviewBridge } from '../../services/webviewBridge';

interface Account {
  id: string;
  label: string;
}

/**
 * DeepSeek accounts, managed here rather than from a chat.
 *
 * An account is a browser profile: switching swaps the profile folder and rebuilds the WebView2, so
 * the chat that triggered it loses its page. That is why switching lives next to the explanation
 * instead of inside a conversation.
 */
export const DeepSeekAccountsCard: React.FC = () => {
  const [accounts, setAccounts] = useState<Account[]>([]);
  const [activeId, setActiveId] = useState('');
  const [signedIn, setSignedIn] = useState<boolean | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      const res: any = await webviewBridge.invoke('deepseek_accounts');
      setAccounts(res?.accounts ?? []);
      setActiveId(res?.active ?? '');
    } catch {
      setAccounts([]);
    }

    try {
      const status: any = await webviewBridge.invoke('deepseek_status');
      setSignedIn(Boolean(status?.signedIn));
    } catch {
      setSignedIn(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const run = async (key: string, action: () => Promise<unknown>) => {
    setBusy(key);
    setError(null);
    try {
      await action();
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'That did not work.');
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="rounded-2xl border border-surface-border bg-surface-card p-3 space-y-2">
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-lg bg-sky-500/10 text-accent">
            <Brain className="h-4 w-4" />
          </span>
          <div>
            <h3 className="text-xs font-semibold text-content-main">DeepSeek accounts</h3>
            <p className="text-[10px] text-content-dim">
              Answers come from chat.deepseek.com, signed in as the active account.
            </p>
          </div>
        </div>

        <button
          type="button"
          onClick={() =>
            void run('add', async () => {
              await webviewBridge.invoke('deepseek_add_account');
            })
          }
          disabled={busy !== null}
          className="flex flex-shrink-0 items-center gap-1.5 rounded-lg border border-surface-border px-2.5 py-1.5 text-[11px] text-content-main transition-colors hover:border-sky-500 disabled:opacity-50"
        >
          {busy === 'add' ? <Loader2 className="h-3 w-3 animate-spin" /> : <Plus className="h-3 w-3" />}
          Add account
        </button>
      </div>

      <div className="grid gap-2 sm:grid-cols-2">
        {accounts.map(account => {
          const isActive = account.id === activeId;

          return (
            <div
              key={account.id}
              className={`rounded-xl border p-2.5 transition-colors ${
                isActive ? 'border-sky-500/50 bg-sky-500/5' : 'border-surface-border bg-surface-deep/60'
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="flex min-w-0 items-center gap-2">
                  <span
                    className={`h-1.5 w-1.5 flex-shrink-0 rounded-full ${isActive ? 'bg-accent' : 'bg-surface-strong'}`}
                  />
                  <span className="truncate text-xs font-medium text-content-main">{account.label}</span>
                </span>

                {isActive ? (
                  <span className="flex flex-shrink-0 items-center gap-1 font-mono text-[9.5px] uppercase tracking-wide text-accent">
                    <Check className="h-3 w-3" />
                    active
                  </span>
                ) : null}
              </div>

              <p className="mt-1 font-mono text-[10px] text-content-dim">
                {isActive
                  ? signedIn === null
                    ? 'checking sign-in...'
                    : signedIn
                      ? 'signed in'
                      : 'not signed in'
                  : 'profile kept on disk'}
              </p>

              <div className="mt-2 flex flex-wrap gap-1.5">
                {!isActive ? (
                  <button
                    type="button"
                    onClick={() =>
                      void run(`switch-${account.id}`, async () => {
                        await webviewBridge.invoke('deepseek_switch_account', { id: account.id });
                      })
                    }
                    disabled={busy !== null}
                    className="rounded-md border border-surface-border px-2 py-0.5 font-mono text-[10px] text-content-muted transition-colors hover:text-content-main disabled:opacity-50"
                  >
                    {busy === `switch-${account.id}` ? 'switching...' : 'Use this account'}
                  </button>
                ) : (
                  <button
                    type="button"
                    onClick={() => void run('login', async () => webviewBridge.invoke('deepseek_login'))}
                    disabled={busy !== null}
                    className="flex items-center gap-1 rounded-md border border-surface-border px-2 py-0.5 font-mono text-[10px] text-content-muted transition-colors hover:text-content-main disabled:opacity-50"
                  >
                    <LogIn className="h-3 w-3" />
                    {signedIn ? 'Open the window' : 'Sign in'}
                  </button>
                )}

                <button
                  type="button"
                  onClick={() =>
                    void run(`remove-${account.id}`, async () => {
                      await webviewBridge.invoke('deepseek_remove_account', { id: account.id });
                    })
                  }
                  disabled={busy !== null || accounts.length <= 1}
                  className="flex items-center gap-1 rounded-md border border-surface-border px-2 py-0.5 font-mono text-[10px] text-content-muted transition-colors hover:text-state-danger disabled:opacity-40"
                  title="Removes the account and deletes its browser profile"
                >
                  <Trash2 className="h-3 w-3" />
                  Remove
                </button>
              </div>
            </div>
          );
        })}
      </div>

      <p className="text-[10px] leading-relaxed text-content-dim">
        Each account keeps its own browser profile, so its login survives restarts. Switching accounts
        rebuilds the DeepSeek window, which is why an open chat loses its page and starts a fresh one.
      </p>

      {error ? <p className="text-[11px] text-state-danger">{error}</p> : null}
    </div>
  );
};
