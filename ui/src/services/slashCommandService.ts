/**
 * Client for the host's slash command discovery.
 *
 * Discovery lives in the host (`get_slash_commands`) because only the host can see the active CLI,
 * the installed skills on disk, the MCP servers and the configured profiles at once. This module
 * only normalises the payload, so a partially-populated response can never crash the palette.
 */

import { SlashCommand, SlashCommandSource } from '../types/agent';
import { webviewBridge } from './webviewBridge';

const VALID_SOURCES: SlashCommandSource[] = ['builtin', 'skill', 'mcp', 'profile'];

function normaliseSource(value: unknown): SlashCommandSource {
  return VALID_SOURCES.includes(value as SlashCommandSource) ? (value as SlashCommandSource) : 'builtin';
}

function normaliseCommand(raw: unknown): SlashCommand | null {
  if (!raw || typeof raw !== 'object') return null;
  const record = raw as Record<string, unknown>;

  const rawName = typeof record.name === 'string' ? record.name : '';
  const name = rawName.replace(/^\//, '').trim();
  if (!name) return null;

  return {
    name,
    description: typeof record.description === 'string' ? record.description.trim() : '',
    source: normaliseSource(record.source),
    engine: typeof record.engine === 'string' ? record.engine : undefined,
    hint: typeof record.hint === 'string' && record.hint.trim() ? record.hint.trim() : undefined,
    takesArgument: record.takesArgument === true || Boolean(record.hint),
  };
}

export interface SlashCommandQuery {
  /** Active CLI id, so the host can return engine-specific commands. */
  cli?: string;
  /** Workspace root, used to find project-level skills and command manifests. */
  cwd?: string;
}

export async function fetchSlashCommands(query: SlashCommandQuery = {}): Promise<SlashCommand[]> {
  const response = await webviewBridge.invoke<{ commands?: unknown[]; engine?: string } | null>(
    'get_slash_commands',
    { cli: query.cli, cwd: query.cwd },
    12000
  );

  const rawList = Array.isArray(response?.commands) ? response!.commands : [];
  const seen = new Set<string>();
  const commands: SlashCommand[] = [];

  for (const raw of rawList) {
    const command = normaliseCommand(raw);
    if (!command || seen.has(command.name)) continue;
    seen.add(command.name);
    commands.push(command);
  }

  return commands;
}
