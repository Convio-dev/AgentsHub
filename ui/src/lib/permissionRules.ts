/**
 * Allow-rule bookkeeping.
 *
 * Headless `agy` cannot answer a permission prompt, so every choice in the composer's prompt is
 * expressed as a `permissions.allow` entry in the profile's settings — the only lever the CLI reads.
 * Scope is therefore emulated by adding the rule and taking it back out again:
 *
 * - run once          → grant, retry the turn, revoke when that turn settles
 * - this conversation → grant, revoke when the chat is deleted or the app closes
 * - persist           → grant, leave it alone
 */

import { webviewBridge } from '../services/webviewBridge';

export type PermissionScope = 'once' | 'conversation' | 'persist';

export interface GrantedRule {
  profile: string;
  rule: string;
}

export async function grantRule(profile: string, rule: string): Promise<void> {
  // The bridge rejects on a refused write, so callers see the failure rather than a silent no-op.
  await webviewBridge.invoke('grant_permission', { profile, rule });
}

/** Best-effort removal: a stale rule is worth a warning, never a broken UI. */
export async function revokeRule(entry: GrantedRule): Promise<void> {
  try {
    await webviewBridge.invoke('revoke_permission', { profile: entry.profile, rule: entry.rule });
  } catch (err) {
    console.warn('[permissionRules] revoke failed:', entry, err);
  }
}
