import React from 'react';
import { Cpu, Sparkles, Plus, Terminal } from 'lucide-react';
import { Button } from '../components/common/Button';

export const McpSkillsView: React.FC = () => {
  const mcpList = [
    { name: 'chrome-devtools-mcp', desc: 'DevTools automation and browser inspection', tools: 22, status: 'Active' },
    { name: 'codebase-memory-mcp', desc: 'Knowledge graph memory and symbol discovery', tools: 16, status: 'Active' },
    { name: 'firebase-mcp-server', desc: 'Firebase auth, realtime database, and storage', tools: 24, status: 'Ready' },
    { name: 'blender', desc: '3D viewport control and Blender asset manipulation', tools: 9, status: 'Ready' },
    { name: 'Roblox_Studio', desc: 'Roblox Studio Luau code and asset automation', tools: 26, status: 'Ready' },
  ];

  const skillList = [
    { name: 'antislop', desc: 'Rules for AI Coding Agents to prevent generic AI slop' },
    { name: 'caveman', desc: 'Terse high-speed developer voice keeping technical facts' },
    { name: 'defuddle', desc: 'Clean token-efficient markdown extraction from web pages' },
  ];

  return (
    <div className="flex-1 overflow-y-auto p-6 space-y-6 font-sans bg-[var(--bg-app,#090D16)] select-none text-[var(--text-main,#F1F5F9)]">
      <div className="flex items-center justify-between pb-4 border-b border-[var(--border-subtle,#1E293B)]">
        <div>
          <h1 className="text-lg font-bold text-[var(--text-main,#F1F5F9)] tracking-tight">MCP Servers & Agent Skills</h1>
          <p className="text-xs text-[var(--text-dim,#64748B)] mt-0.5 font-mono">
            Model Context Protocol integrations and modular agent skills
          </p>
        </div>
        <Button variant="primary" size="xs" leftIcon={<Plus className="w-3.5 h-3.5" />}>
          Register Server
        </Button>
      </div>

      <div className="space-y-4">
        <h2 className="text-xs font-bold uppercase tracking-wider text-[var(--text-main,#F1F5F9)] font-mono">
          Connected MCP Servers
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3.5">
          {mcpList.map((m, i) => (
            <div key={i} className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] space-y-2.5 shadow-sm hover:border-sky-500/40 transition-all">
              <div className="flex items-center justify-between">
                <span className="font-mono text-xs font-bold text-[var(--text-main,#F1F5F9)] flex items-center gap-2">
                  <div className="w-7 h-7 rounded-lg bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-sky-400">
                    <Cpu className="w-3.5 h-3.5" />
                  </div>
                  <span>{m.name}</span>
                </span>
                <span className="px-2 py-0.5 rounded text-[10px] font-mono bg-emerald-500/10 text-emerald-400 border border-emerald-500/20 font-semibold">
                  {m.status}
                </span>
              </div>
              <p className="text-xs text-[var(--text-muted,#94A3B8)]">{m.desc}</p>
              <div className="text-[11px] font-mono text-[var(--text-dim,#64748B)] pt-1">
                {m.tools} Tools Exposed
              </div>
            </div>
          ))}
        </div>
      </div>

      <div className="space-y-4 pt-4 border-t border-[var(--border-subtle,#1E293B)]">
        <h2 className="text-xs font-bold uppercase tracking-wider text-[var(--text-main,#F1F5F9)] font-mono">
          Loaded Skills
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3.5">
          {skillList.map((s, i) => (
            <div key={i} className="p-4 rounded-xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] space-y-2 shadow-sm hover:border-sky-500/40 transition-all">
              <div className="flex items-center justify-between">
                <span className="font-mono text-xs font-bold text-sky-400 flex items-center gap-1.5">
                  <Sparkles className="w-3.5 h-3.5 text-sky-400" />
                  <span>{s.name}</span>
                </span>
                <span className="text-[10px] font-mono text-[var(--text-dim,#64748B)]">Active</span>
              </div>
              <p className="text-xs text-[var(--text-muted,#94A3B8)] leading-relaxed">{s.desc}</p>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
