import React, { useState, useEffect } from 'react';
import {
  Globe,
  Monitor,
  Terminal,
  Cpu,
  HardDrive,
  Wifi,
  Activity,
  Play,
  RotateCw,
  Copy,
  ExternalLink,
  Eye,
  EyeOff,
  Shield,
  Plus,
  Lock,
  Unlock,
  Check,
  Zap,
  Folder,
  ArrowRight,
  Server,
  X,
  Compass,
} from 'lucide-react';
import { useSidecarStore } from '../store/useSidecarStore';
import { useProfileStore } from '../store/useProfileStore';
import { useSessionStore } from '../store/useSessionStore';
import { webviewBridge } from '../services/webviewBridge';
import { Button } from '../components/common/Button';

export const DashboardView: React.FC = () => {
  const { health, telemetry, setActiveView } = useSidecarStore();
  const { profiles, activeProfileName, startProfile, switchActiveProfile } = useProfileStore();
  const { sessions } = useSessionStore();

  const [showQuickstart, setShowQuickstart] = useState(true);
  const [selectedPort, setSelectedPort] = useState<string>('9119');
  const [isLanMode, setIsLanMode] = useState<boolean>(false);
  const [showEmail, setShowEmail] = useState<boolean>(false);
  const [shortcutsLocked, setShortcutsLocked] = useState<boolean>(true);
  const [maskPaths, setMaskPaths] = useState<boolean>(true);
  const [copiedUrl, setCopiedUrl] = useState<boolean>(false);

  // Sparkline history data for RAM
  const [ramHistory, setRamHistory] = useState<number[]>([45, 48, 50, 52, 51, 55, 57]);

  useEffect(() => {
    if (telemetry.memoryUsedGb && telemetry.memoryTotalGb) {
      const pct = Math.round((telemetry.memoryUsedGb / telemetry.memoryTotalGb) * 100);
      setRamHistory(prev => [...prev.slice(-15), pct]);
    }
  }, [telemetry.memoryUsedGb, telemetry.memoryTotalGb]);

  const activeProfile = profiles.find(p => p.name === activeProfileName) || profiles[0];

  const currentUrl = isLanMode
    ? `http://192.168.1.100:${selectedPort}/`
    : `http://127.0.0.1:${selectedPort}/`;

  const copyUrl = () => {
    navigator.clipboard.writeText(currentUrl);
    setCopiedUrl(true);
    setTimeout(() => setCopiedUrl(false), 2000);
  };

  const openInBrowser = () => {
    webviewBridge.invoke('open_url_external', { url: currentUrl });
  };

  // Fixed: Calls C# 'open_standalone_terminals' which opens the native standalone window with the active CLI tab
  const launchStandaloneCli = () => {
    const cliKey = activeProfile?.name ? `antigravity_cli:${activeProfile.name}` : 'antigravity_cli';
    webviewBridge.invoke('open_standalone_terminals', { cli: cliKey });
  };

  // Fixed: Calls C# 'open_system_shell' with shell name and asAdmin boolean
  const launchShell = (shell: 'cmd' | 'powershell', asAdmin: boolean) => {
    webviewBridge.invoke('open_system_shell', { shell, asAdmin });
  };

  const launchTool = (toolId: string) => {
    webviewBridge.invoke('launch_tool', { toolId });
  };

  // Sparkline SVG path generator
  const getSparklinePath = () => {
    if (ramHistory.length === 0) return { area: '', line: '' };
    const width = 240;
    const height = 48;
    const min = 20;
    const max = 100;
    const points = ramHistory.map((val, idx) => {
      const x = (idx / (ramHistory.length - 1 || 1)) * width;
      const y = height - ((val - min) / (max - min)) * height;
      return `${x},${y}`;
    });
    const line = `M ${points.join(' L ')}`;
    const area = `${line} L ${width},${height} L 0,${height} Z`;
    return { area, line };
  };

  const sparkline = getSparklinePath();

  return (
    <div className="flex-1 overflow-y-auto p-6 space-y-6 font-sans bg-[var(--bg-app,#090D16)] text-[var(--text-main,#F1F5F9)] select-none">
      {/* Quick-Start Banner - Perfectly theme synced for Dark and Light modes */}
      {showQuickstart && (
        <div className="p-5 rounded-2xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] shadow-sm space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <span className="px-2.5 py-0.5 rounded-md text-[10.5px] font-mono font-bold bg-sky-500/15 text-accent border border-sky-500/30">
                QUICK-START GUIDE
              </span>
              <h3 className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">New to AgentsHub? Complete these 4 steps</h3>
            </div>
            <div className="flex items-center gap-2">
              <Button variant="secondary" size="xs" onClick={() => setActiveView('profiles')}>
                <Compass className="w-3.5 h-3.5 mr-1 text-accent" />
                Launch Tour (F1)
              </Button>
              <button
                onClick={() => setShowQuickstart(false)}
                className="p-1 text-[var(--text-dim,#64748B)] hover:text-[var(--text-main,#F1F5F9)] transition-colors"
                title="Dismiss Guide"
              >
                <X className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
          <p className="text-xs text-[var(--text-muted,#94A3B8)]">
            Follow these 4 core steps to configure your environment, run your AI agents, and start developing:
          </p>
          <div className="grid grid-cols-1 md:grid-cols-4 gap-3.5 pt-1">
            <div className="p-3.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] flex flex-col justify-between space-y-3">
              <div>
                <div className="flex items-center gap-2 mb-1.5">
                  <span className="w-5 h-5 rounded-full bg-sky-500/20 text-accent font-mono text-xs flex items-center justify-center font-bold">1</span>
                  <span className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">Set Workspace</span>
                </div>
                <p className="text-[11px] text-[var(--text-dim,#64748B)] leading-relaxed">Select target project folder for safe agent edits.</p>
              </div>
              <Button variant="secondary" size="xs" onClick={() => setActiveView('repositories')}>
                Select Folder
              </Button>
            </div>

            <div className="p-3.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] flex flex-col justify-between space-y-3">
              <div>
                <div className="flex items-center gap-2 mb-1.5">
                  <span className="w-5 h-5 rounded-full bg-sky-500/20 text-accent font-mono text-xs flex items-center justify-center font-bold">2</span>
                  <span className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">Antigravity Profile</span>
                </div>
                <p className="text-[11px] text-[var(--text-dim,#64748B)] leading-relaxed">Add profile, link your Google account, sync creds.</p>
              </div>
              <Button variant="secondary" size="xs" onClick={() => setActiveView('profiles')}>
                Manage Profiles
              </Button>
            </div>

            <div className="p-3.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] flex flex-col justify-between space-y-3">
              <div>
                <div className="flex items-center gap-2 mb-1.5">
                  <span className="w-5 h-5 rounded-full bg-sky-500/20 text-accent font-mono text-xs flex items-center justify-center font-bold">3</span>
                  <span className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">AI Dev Tools</span>
                </div>
                <p className="text-[11px] text-[var(--text-dim,#64748B)] leading-relaxed">Check Git, Node, Python & runtime health.</p>
              </div>
              <Button variant="secondary" size="xs" onClick={() => setActiveView('devtools')}>
                Check Tools
              </Button>
            </div>

            <div className="p-3.5 rounded-xl bg-sky-500/10 border border-sky-500/30 flex flex-col justify-between space-y-3">
              <div>
                <div className="flex items-center gap-2 mb-1.5">
                  <span className="w-5 h-5 rounded-full bg-sky-500 text-slate-950 font-mono text-xs flex items-center justify-center font-bold">4</span>
                  <span className="text-xs font-bold text-accent">Standalone Terminals</span>
                </div>
                <p className="text-[11px] text-[var(--text-muted,#94A3B8)] leading-relaxed">Dedicated multi-tab ConPTY terminal window.</p>
              </div>
              <Button variant="primary" size="xs" onClick={launchStandaloneCli}>
                Open Cockpit
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* Grid of Main Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        {/* CARD 1: WEB APP HOSTS & RUNNING PORTS */}
        <div className="p-5 rounded-2xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] shadow-sm space-y-4">
          <div className="flex items-start justify-between">
            <div className="flex items-center gap-3">
              <div className="w-9 h-9 rounded-xl bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-accent">
                <Globe className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-bold uppercase tracking-wider text-[var(--text-main,#F1F5F9)]">Web App Hosts & Running Ports</h3>
                <p className="text-[11px] text-[var(--text-dim,#64748B)]">Active web services & terminal interfaces on host & LAN</p>
              </div>
            </div>
            <select
              value={selectedPort}
              onChange={e => setSelectedPort(e.target.value)}
              className="px-2.5 py-1 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-xs text-[var(--text-main,#F1F5F9)] font-mono focus:outline-none"
            >
              <option value="9119">Hermes API: 9119</option>
              <option value="9229">Claude Code: 9229</option>
              <option value="9339">Antigravity CLI: 9339</option>
              <option value="9191">LAN Proxy: 9191</option>
            </select>
          </div>

          <div className="flex items-center justify-between p-3 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-xs font-mono">
            <span className="text-[var(--text-main,#F1F5F9)] truncate">{currentUrl}</span>
            <div className="flex items-center gap-1.5 flex-shrink-0">
              <button
                onClick={() => setIsLanMode(!isLanMode)}
                className="p-1.5 rounded text-[var(--text-dim,#64748B)] hover:text-accent transition-colors"
                title="Switch between Local (127.0.0.1) and LAN URL"
              >
                <RotateCw className="w-3.5 h-3.5" />
              </button>
              <button
                onClick={copyUrl}
                className="p-1.5 rounded text-[var(--text-dim,#64748B)] hover:text-accent transition-colors"
                title="Copy URL"
              >
                {copiedUrl ? <Check className="w-3.5 h-3.5 text-state-success" /> : <Copy className="w-3.5 h-3.5" />}
              </button>
              <button
                onClick={openInBrowser}
                className="p-1.5 rounded text-[var(--text-dim,#64748B)] hover:text-accent transition-colors"
                title="Open in Browser"
              >
                <ExternalLink className="w-3.5 h-3.5" />
              </button>
            </div>
          </div>

          {/* Running Ports Chips */}
          <div className="flex flex-wrap gap-2">
            {[
              { label: 'Hermes Engine', port: '9119', active: true },
              { label: 'Claude Gateway', port: '9229', active: false },
              { label: 'Antigravity CLI', port: '9339', active: false },
              { label: 'LAN Proxy', port: '9191', active: true },
            ].map(chip => (
              <span
                key={chip.port}
                className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-[10.5px] font-mono border ${
                  chip.active
                    ? 'bg-emerald-500/10 text-state-success border-emerald-500/30 font-semibold'
                    : 'bg-[var(--bg-input,#0D1322)] text-[var(--text-dim,#64748B)] border-[var(--border-subtle,#1E293B)]'
                }`}
              >
                <span className={`w-1.5 h-1.5 rounded-full ${chip.active ? 'bg-emerald-400 animate-pulse' : 'bg-slate-600'}`} />
                <span>{chip.label}:{chip.port}</span>
              </span>
            ))}
          </div>
        </div>

        {/* CARD 2: STANDALONE TERMINALS */}
        <div className="p-5 rounded-2xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] shadow-sm space-y-4">
          <div className="flex items-start justify-between">
            <div className="flex items-center gap-3">
              <div className="w-9 h-9 rounded-xl bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-accent">
                <Terminal className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-bold uppercase tracking-wider text-[var(--text-main,#F1F5F9)]">Standalone Terminals</h3>
                <p className="text-[11px] text-[var(--text-dim,#64748B)]">Open in a separate dedicated full-screen window</p>
              </div>
            </div>
            <select
              value={activeProfileName || ''}
              onChange={e => switchActiveProfile(e.target.value)}
              className="px-2.5 py-1 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-xs text-[var(--text-main,#F1F5F9)] font-mono focus:outline-none"
            >
              {profiles.map(p => (
                <option key={p.name} value={p.name}>
                  {p.name} {p.isRunning ? '[Running]' : '[Stopped]'}
                </option>
              ))}
            </select>
          </div>

          {/* Profile Summary & Pop Out (Triggers open_standalone_terminals) */}
          <div className="flex items-center justify-between p-3 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-xs">
            <span className="text-[var(--text-main,#F1F5F9)] font-medium">Antigravity Profile: {activeProfile?.name || 'None'}</span>
            <Button variant="secondary" size="xs" onClick={launchStandaloneCli}>
              <ExternalLink className="w-3.5 h-3.5 mr-1.5" />
              Pop Out
            </Button>
          </div>

          {/* Antigravity App Row */}
          <div className="flex items-center justify-between p-3 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-xs">
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
              <span className="font-semibold text-[var(--text-main,#F1F5F9)]">Antigravity App</span>
              <span className="text-[11px] text-[var(--text-dim,#64748B)]">(shares profile: {activeProfile?.name})</span>
            </div>
            <Button variant="primary" size="xs" onClick={() => launchTool('antigravity_app')}>
              <Play className="w-3 h-3 mr-1 fill-current" />
              Open App
            </Button>
          </div>

          {/* Account Email Reveal */}
          <div className="flex items-center justify-between p-3 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-xs font-mono">
            <div className="flex items-center gap-2">
              <span className="text-[var(--text-dim,#64748B)]">Account:</span>
              <span className="text-[var(--text-main,#F1F5F9)]">
                {showEmail ? activeProfile?.email || 'user@example.com' : '••••••••••••••••'}
              </span>
            </div>
            <button
              onClick={() => setShowEmail(!showEmail)}
              className="p-1 text-[var(--text-dim,#64748B)] hover:text-accent transition-colors"
              title="Toggle email visibility"
            >
              {showEmail ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
            </button>
          </div>
        </div>

        {/* CARD 3: SYSTEM TERMINALS & SHELLS */}
        <div className="p-5 rounded-2xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] shadow-sm space-y-4">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-xl bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-accent">
              <Cpu className="w-4 h-4" />
            </div>
            <div>
              <h3 className="text-xs font-bold uppercase tracking-wider text-[var(--text-main,#F1F5F9)]">System Terminals & Shells</h3>
              <p className="text-[11px] text-[var(--text-dim,#64748B)]">Launch CMD or PowerShell (Standard / Administrator)</p>
            </div>
          </div>

          <div className="space-y-3">
            {/* Command Prompt */}
            <div className="flex items-center justify-between p-3 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
              <div className="flex items-center gap-3">
                <span className="px-2.5 py-1 rounded-lg bg-[var(--bg-card,#0F172A)] text-[11px] font-mono font-bold text-accent border border-[var(--border-subtle,#1E293B)]">
                  CMD
                </span>
                <div>
                  <div className="text-xs font-semibold text-[var(--text-main,#F1F5F9)]">Command Prompt</div>
                  <div className="text-[10.5px] text-[var(--text-dim,#64748B)] font-mono">cmd.exe</div>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <Button variant="secondary" size="xs" onClick={() => launchShell('cmd', false)}>
                  Launch
                </Button>
                <Button variant="secondary" size="xs" onClick={() => launchShell('cmd', true)}>
                  Admin
                </Button>
              </div>
            </div>

            {/* PowerShell */}
            <div className="flex items-center justify-between p-3 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
              <div className="flex items-center gap-3">
                <span className="px-2.5 py-1 rounded-lg bg-sky-500/10 text-[11px] font-mono font-bold text-accent border border-sky-500/30">
                  PS
                </span>
                <div>
                  <div className="text-xs font-semibold text-[var(--text-main,#F1F5F9)]">PowerShell</div>
                  <div className="text-[10.5px] text-[var(--text-dim,#64748B)] font-mono">powershell.exe</div>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <Button variant="secondary" size="xs" onClick={() => launchShell('powershell', false)}>
                  Launch
                </Button>
                <Button variant="secondary" size="xs" onClick={() => launchShell('powershell', true)}>
                  Admin
                </Button>
              </div>
            </div>
          </div>
        </div>

        {/* CARD 4: SYSTEM & HARDWARE TELEMETRY */}
        <div className="p-5 rounded-2xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] shadow-sm space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="w-9 h-9 rounded-xl bg-emerald-500/15 border border-emerald-500/30 flex items-center justify-center text-state-success">
                <Activity className="w-4 h-4" />
              </div>
              <div>
                <h3 className="text-xs font-bold uppercase tracking-wider text-[var(--text-main,#F1F5F9)]">System & Hardware Telemetry</h3>
                <p className="text-[11px] text-[var(--text-dim,#64748B)]">Real-time resource utilization & AI model workload</p>
              </div>
            </div>
            <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-[10px] font-mono bg-emerald-500/10 text-state-success border border-emerald-500/30 font-semibold">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
              <span>Live: 2s</span>
            </div>
          </div>

          {/* RAM Tile */}
          <div className="p-4 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] space-y-2">
            <div className="flex items-center justify-between text-xs">
              <div className="flex items-center gap-2 text-[var(--text-main,#F1F5F9)] font-semibold">
                <HardDrive className="w-4 h-4 text-accent" />
                <span>Memory (RAM)</span>
              </div>
              <span className="font-mono font-bold text-[var(--text-main,#F1F5F9)]">
                {telemetry.memoryUsedGb || 15.8} GB ({Math.round(((telemetry.memoryUsedGb || 15.8) / (telemetry.memoryTotalGb || 31.9)) * 100)}%)
              </span>
            </div>

            {/* Sparkline Curve */}
            <div className="h-12 w-full overflow-hidden">
              <svg className="w-full h-full" viewBox="0 0 240 48" preserveAspectRatio="none">
                <defs>
                  <linearGradient id="ramGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="#06b6d4" stopOpacity="0.4" />
                    <stop offset="100%" stopColor="#06b6d4" stopOpacity="0.0" />
                  </linearGradient>
                </defs>
                <path d={sparkline.area} fill="url(#ramGrad)" />
                <path d={sparkline.line} stroke="#06b6d4" strokeWidth="2" fill="none" />
              </svg>
            </div>

            <div className="flex items-center justify-between text-[11px] font-mono text-[var(--text-dim,#64748B)] pt-1">
              <span>Total: {telemetry.memoryTotalGb || 31.9} GB</span>
              <span>Avail: {Math.max(0, (telemetry.memoryTotalGb || 31.9) - (telemetry.memoryUsedGb || 15.8)).toFixed(1)} GB</span>
            </div>
          </div>

          {/* Bottom Chips */}
          <div className="grid grid-cols-3 gap-2.5 text-xs font-mono">
            <div className="p-2.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
              <div className="text-[10px] text-[var(--text-dim,#64748B)]">Agents Hub</div>
              <div className="font-bold text-[var(--text-main,#F1F5F9)] mt-0.5">{telemetry.hermesMemMb || 136} MB</div>
            </div>
            <div className="p-2.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
              <div className="text-[10px] text-[var(--text-dim,#64748B)]">Network</div>
              <div className="font-bold text-state-success mt-0.5">{telemetry.netKbps || 6} KB/s</div>
            </div>
            <div className="p-2.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
              <div className="text-[10px] text-[var(--text-dim,#64748B)]">Status</div>
              <div className="font-bold text-state-success mt-0.5">Active</div>
            </div>
          </div>
        </div>
      </div>

      {/* CARD 5: PROGRAMS & QUICK SHORTCUTS */}
      <div className="p-5 rounded-2xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] shadow-sm space-y-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-9 h-9 rounded-xl bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-accent">
              <Zap className="w-4 h-4" />
            </div>
            <div>
              <h3 className="text-xs font-bold uppercase tracking-wider text-[var(--text-main,#F1F5F9)]">Programs & Quick Shortcuts</h3>
              <p className="text-[11px] text-[var(--text-dim,#64748B)]">Launch and manage custom shortcuts for AI tools, CLIs, and apps</p>
            </div>
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={() => setShortcutsLocked(!shortcutsLocked)}
              className="p-1.5 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-[var(--text-dim,#64748B)] hover:text-[var(--text-main,#F1F5F9)] transition-colors"
              title={shortcutsLocked ? 'Unlock shortcuts' : 'Lock shortcuts'}
            >
              {shortcutsLocked ? <Lock className="w-3.5 h-3.5" /> : <Unlock className="w-3.5 h-3.5 text-state-warning" />}
            </button>
            <button
              onClick={() => setMaskPaths(!maskPaths)}
              className="p-1.5 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-[var(--text-dim,#64748B)] hover:text-[var(--text-main,#F1F5F9)] transition-colors"
              title="Show / Hide paths"
            >
              {maskPaths ? <EyeOff className="w-3.5 h-3.5" /> : <Eye className="w-3.5 h-3.5" />}
            </button>
            <Button variant="primary" size="xs">
              <Plus className="w-3 h-3 mr-1" />
              Add Shortcut
            </Button>
          </div>
        </div>

        {/* Shortcuts Grid */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          {[
            {
              name: 'Antigravity IDE',
              path: 'C:\\Users\\Gaming\\AppData\\Local\\Programs\\Antigravity\\Antigravity.exe',
              icon: Zap,
              color: 'text-accent',
              id: 'antigravity_ide',
            },
            {
              name: 'Claude Desktop',
              path: 'C:\\Users\\Gaming\\AppData\\Local\\Anthropic\\Claude\\Claude.exe',
              icon: Globe,
              color: 'text-state-warning',
              id: 'claude_desktop',
            },
            {
              name: 'Antigravity App',
              path: 'C:\\Users\\Gaming\\AppData\\Local\\Programs\\AntigravityApp\\app.exe',
              icon: Terminal,
              color: 'text-state-success',
              id: 'antigravity_app',
            },
          ].map(sc => {
            const Icon = sc.icon;
            return (
              <div
                key={sc.name}
                className="p-3.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between group hover:border-sky-500/50 transition-all shadow-xs"
              >
                <div className="flex items-center gap-3 overflow-hidden">
                  <div className={`w-8 h-8 rounded-lg bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-center ${sc.color}`}>
                    <Icon className="w-4 h-4" />
                  </div>
                  <div className="truncate">
                    <div className="text-xs font-semibold text-[var(--text-main,#F1F5F9)]">{sc.name}</div>
                    <div className="text-[10px] text-[var(--text-dim,#64748B)] font-mono truncate">
                      {maskPaths ? '**' : sc.path}
                    </div>
                  </div>
                </div>
                <Button variant="secondary" size="xs" onClick={() => launchTool(sc.id)}>
                  Launch
                </Button>
              </div>
            );
          })}
        </div>
      </div>

      {/* CARD 6: CORE ENGINE & CLI CONTROLLERS */}
      <div className="p-5 rounded-2xl bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] shadow-sm space-y-4">
        <div className="flex items-center gap-3">
          <div className="w-9 h-9 rounded-xl bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-accent">
            <Server className="w-4 h-4" />
          </div>
          <div>
            <h3 className="text-xs font-bold uppercase tracking-wider text-[var(--text-main,#F1F5F9)]">Core Engine & CLI Controllers</h3>
            <p className="text-[11px] text-[var(--text-dim,#64748B)]">Manage background processes, ports, and execution state</p>
          </div>
        </div>

        <div className="space-y-3">
          {/* Hermes */}
          <div className="flex items-center justify-between p-3.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">Hermes CLI</span>
                <span className="text-[11px] text-[var(--text-dim,#64748B)] font-mono">Port 9119</span>
              </div>
              <div className="text-[11px] text-state-success mt-0.5 font-mono">State: Active</div>
            </div>
            <div className="flex items-center gap-2">
              <Button variant="primary" size="xs">
                Start
              </Button>
              <Button variant="secondary" size="xs">
                Restart
              </Button>
            </div>
          </div>

          {/* Claude Code */}
          <div className="flex items-center justify-between p-3.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">Claude Code CLI</span>
                <span className="text-[11px] text-[var(--text-dim,#64748B)] font-mono">Port 9229</span>
              </div>
              <div className="text-[11px] text-[var(--text-dim,#64748B)] mt-0.5 font-mono">State: Offline</div>
            </div>
            <div className="flex items-center gap-2">
              <Button variant="primary" size="xs">
                Start
              </Button>
              <Button variant="secondary" size="xs">
                Restart
              </Button>
            </div>
          </div>

          {/* Antigravity CLI */}
          <div className="flex items-center justify-between p-3.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">Antigravity CLI (agy)</span>
                <span className="text-[11px] text-[var(--text-dim,#64748B)] font-mono">Port 9339</span>
              </div>
              <div className="text-[11px] text-[var(--text-dim,#64748B)] mt-0.5 font-mono">State: Standby</div>
            </div>
            <div className="flex items-center gap-2">
              <select
                value={activeProfileName || ''}
                onChange={e => switchActiveProfile(e.target.value)}
                className="px-2.5 py-1 rounded-lg bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] text-xs font-mono text-[var(--text-main,#F1F5F9)]"
              >
                {profiles.map(p => (
                  <option key={p.name} value={p.name}>
                    {p.name}
                  </option>
                ))}
              </select>
              <Button variant="primary" size="xs">
                Start
              </Button>
              <Button variant="secondary" size="xs">
                Restart
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
