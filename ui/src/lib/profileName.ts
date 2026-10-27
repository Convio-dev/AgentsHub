/**
 * Profile display rules.
 *
 * A session stores a profile *id*, which is empty when nothing has been configured yet (and may be
 * the legacy literal `default` in older records). Nothing in the UI should ever render that id raw:
 * the label comes from here so an unconfigured workspace reads "Default Profile" instead of blank or
 * a developer's local test account.
 */

export const DEFAULT_PROFILE_LABEL = 'Default Profile';

/** True when the stored profile id carries no real name. */
export function isUnsetProfile(name?: string | null): boolean {
  if (!name) return true;
  const normalised = name.trim().toLowerCase();
  return normalised === '' || normalised === 'default';
}

/** Human-readable profile name. Never returns an empty string. */
export function profileLabel(name?: string | null): string {
  return isUnsetProfile(name) ? DEFAULT_PROFILE_LABEL : (name as string).trim();
}

/** Profile id to hand to the host, or undefined to let it use the active profile. */
export function profileForDispatch(name?: string | null): string | undefined {
  return isUnsetProfile(name) ? undefined : (name as string).trim();
}
