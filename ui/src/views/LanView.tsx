import React, { useState, useEffect } from 'react';
import {
  Wifi,
  Copy,
  ExternalLink,
  Shield,
  RefreshCw,
  QrCode,
  CheckCircle2,
  Server,
  Smartphone,
} from 'lucide-react';
import { webviewBridge } from '../services/webviewBridge';
import { Button } from '../components/common/Button';

export const LanView: React.FC = () => {
  const [lanEnabled, setLanEnabled] = useState(true);
  const [copied, setCopied] = useState(false);
  const [lanIp, setLanIp] = useState('192.168.1.140');
  const [lanPort, setLanPort] = useState(9191);
  const [qrBase64, setQrBase64] = useState<string>('');
  const [isRefreshing, setIsRefreshing] = useState(false);

  const lanUrl = `http://${lanIp}:${lanPort}/`;

  const loadLanData = async () => {
    setIsRefreshing(true);
    try {
      const res = await webviewBridge.invoke('get_initial_state');
      if (res) {
        if (res.lanIp) setLanIp(res.lanIp);
        if (res.lanProxyPort) setLanPort(res.lanProxyPort);
        if (res.qrBase64) {
          setQrBase64(res.qrBase64);
        } else {
          const url = `http://${res.lanIp || lanIp}:${res.lanProxyPort || lanPort}/`;
          const qrRes = await webviewBridge.invoke('generate_qr_code', { url });
          if (qrRes && qrRes.qrBase64) {
            setQrBase64(qrRes.qrBase64);
          }
        }
      }
    } catch (err) {
      console.warn('Failed to load LAN state:', err);
    } finally {
      setIsRefreshing(false);
    }
  };

  useEffect(() => {
    loadLanData();
  }, []);

  const handleCopy = () => {
    navigator.clipboard?.writeText(lanUrl);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  };

  const handleOpenBrowser = () => {
    webviewBridge.invoke('open_url_external', { url: lanUrl });
  };

  const toggleLan = async () => {
    const next = !lanEnabled;
    setLanEnabled(next);
    try {
      await webviewBridge.invoke('set_lan_proxy_enabled', { enabled: next });
    } catch {}
  };

  return (
    <div className="flex-1 overflow-y-auto p-6 bg-[var(--bg-app,#090D16)] text-[var(--text-main,#F1F5F9)] select-none font-sans">
      <div className="max-w-5xl mx-auto space-y-6">
        {/* Header card: LAN Remote Access */}
        <div className="rounded-2xl border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-card,#0F172A)] p-6 space-y-5 shadow-sm">
          <div className="flex items-center justify-between pb-4 border-b border-[var(--border-subtle,#1E293B)] flex-wrap gap-3">
            <div className="flex items-center space-x-3">
              <div className="w-10 h-10 rounded-xl bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-accent">
                <Wifi className="w-5 h-5" />
              </div>
              <div>
                <h2 className="text-sm font-bold text-[var(--text-main,#F1F5F9)] tracking-wider uppercase">
                  LAN Remote Access
                </h2>
                <p className="text-xs text-[var(--text-dim,#64748B)]">
                  Open running agent sessions or control plane from a phone or laptop on the local network
                </p>
              </div>
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={loadLanData}
                disabled={isRefreshing}
                className="p-1.5 rounded-lg border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-input,#0D1322)] text-[var(--text-dim,#64748B)] hover:text-accent transition"
                title="Refresh network status & QR"
              >
                <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? 'animate-spin' : ''}`} />
              </button>

              <button
                type="button"
                onClick={toggleLan}
                className={`px-3 py-1.5 rounded-lg text-xs font-mono font-medium flex items-center space-x-2 transition ${
                  lanEnabled
                    ? 'bg-emerald-500/15 text-state-success border border-emerald-500/30'
                    : 'bg-slate-800 text-[var(--text-dim,#64748B)] border border-surface-border'
                }`}
              >
                <span className={`w-2 h-2 rounded-full ${lanEnabled ? 'bg-emerald-400 animate-pulse' : 'bg-slate-500'}`} />
                <span>{lanEnabled ? 'LAN Active' : 'LAN Off'}</span>
              </button>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-5">
            <div className="md:col-span-2 space-y-3.5">
              <div className="p-3.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between font-mono text-xs">
                <span className="text-accent font-semibold truncate">{lanUrl}</span>
                <div className="flex items-center space-x-1.5 ml-2">
                  <button
                    onClick={handleCopy}
                    className="p-1.5 rounded-lg hover:bg-[var(--bg-card,#0F172A)] text-[var(--text-dim,#64748B)] hover:text-[var(--text-main,#F1F5F9)] transition"
                    title="Copy URL"
                  >
                    {copied ? <CheckCircle2 className="w-4 h-4 text-state-success" /> : <Copy className="w-4 h-4" />}
                  </button>
                  <button
                    onClick={handleOpenBrowser}
                    className="p-1.5 rounded-lg hover:bg-[var(--bg-card,#0F172A)] text-[var(--text-dim,#64748B)] hover:text-[var(--text-main,#F1F5F9)] transition"
                    title="Open in Browser"
                  >
                    <ExternalLink className="w-4 h-4" />
                  </button>
                </div>
              </div>

              <div className="grid grid-cols-3 gap-3 text-xs font-mono">
                <div className="p-3 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
                  <span className="text-[var(--text-dim,#64748B)] block text-[10px] uppercase font-semibold">Host Address</span>
                  <span className="text-[var(--text-main,#F1F5F9)] font-semibold mt-0.5 block">{lanIp}</span>
                </div>
                <div className="p-3 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
                  <span className="text-[var(--text-dim,#64748B)] block text-[10px] uppercase font-semibold">Gateway Port</span>
                  <span className="text-[var(--text-main,#F1F5F9)] font-semibold mt-0.5 block">{lanPort}</span>
                </div>
                <div className="p-3 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)]">
                  <span className="text-[var(--text-dim,#64748B)] block text-[10px] uppercase font-semibold">Security</span>
                  <span className="text-state-success font-semibold mt-0.5 block">Token Guard</span>
                </div>
              </div>
            </div>

            {/* QR Code Real Generator Display */}
            <div className="flex flex-col items-center justify-center p-4 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-center shadow-xs">
              <div className="w-28 h-28 bg-white p-2 rounded-xl flex items-center justify-center shadow-md">
                {qrBase64 ? (
                  <img
                    src={qrBase64.startsWith('data:') ? qrBase64 : `data:image/png;base64,${qrBase64}`}
                    alt="LAN QR Code"
                    className="w-full h-full object-contain"
                  />
                ) : (
                  <div className="flex flex-col items-center justify-center text-slate-800">
                    <QrCode className="w-16 h-16" />
                    <span className="text-[9px] font-mono mt-1 font-bold">QR Ready</span>
                  </div>
                )}
              </div>
              <span className="text-[11px] text-[var(--text-dim,#64748B)] font-mono mt-3 flex items-center gap-1.5">
                <Smartphone className="w-3.5 h-3.5 text-accent" />
                <span>Scan to connect mobile</span>
              </span>
            </div>
          </div>
        </div>

        {/* Network Services Table */}
        <div className="rounded-2xl border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-card,#0F172A)] p-6 space-y-4 shadow-sm">
          <div className="flex items-center justify-between pb-3 border-b border-[var(--border-subtle,#1E293B)]">
            <h3 className="text-xs font-bold uppercase tracking-wider text-[var(--text-main,#F1F5F9)] font-mono flex items-center gap-2">
              <Server className="w-4 h-4 text-accent" />
              <span>Services on This Network</span>
            </h3>
            <span className="text-[11px] font-mono text-[var(--text-dim,#64748B)]">
              3 forwarders bound
            </span>
          </div>

          <div className="space-y-2.5">
            {[
              { name: 'Claude Code Relay', type: 'TCP Relay', port: 9229, status: 'Bound' },
              { name: 'Antigravity VirtualHome', type: 'CLI RPC', port: 9339, status: 'Healthy' },
            ].map(svc => (
              <div
                key={svc.name}
                className="p-3.5 rounded-xl bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] flex items-center justify-between font-mono text-xs hover:border-sky-500/40 transition-all shadow-xs"
              >
                <div className="flex items-center space-x-3">
                  <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse" />
                  <span className="font-semibold text-[var(--text-main,#F1F5F9)]">{svc.name}</span>
                </div>
                <div className="flex items-center space-x-4">
                  <span className="text-[var(--text-dim,#64748B)] text-[11px]">{svc.type}</span>
                  <span className="text-accent font-bold">:{svc.port}</span>
                  <span className="px-2 py-0.5 rounded text-[10px] bg-emerald-500/10 text-state-success border border-emerald-500/20 font-bold">
                    {svc.status}
                  </span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};
