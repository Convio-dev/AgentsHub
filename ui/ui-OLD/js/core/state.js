// Global Client State
window.state = {
    version: '',
    lanIp: '127.0.0.1',
    lanUrl: 'http://127.0.0.1:9095/',
    lanProxyPort: 9191,
    lanProxyEnabled: true,
    dashUrlMode: 'local',
    lanUrlMode: 'lan',
    isIpMasked: false,
    isShortcutsMasked: localStorage.getItem('agents_hub_mask_shortcuts') === 'true',
    isShortcutsLocked: localStorage.getItem('agents_hub_shortcuts_locked') !== 'false',
    publicIp: '',
    ipv4: '',
    ipv6: '',
    previousIpv4: '',
    previousIpv6: '',
    countryCode: '',
    country: '',
    countryEmoji: '',
    city: '',
    vpnProvider: (localStorage.getItem('agents_hub_enable_proton') === 'true' && localStorage.getItem('agents_hub_vpn_provider') === 'proton') ? 'proton' : 'warp',
    isDirectIsp: false,
    qrBase64: '',
    clis: {},
    antigravity: { activeProfile: '', profiles: [] },
    opencode: { isOnline: false, port: 0, activeSessions: [] },
    aiConfig: { providers: [], toolTargets: {}, devTools: [], shortcuts: [], selectedProviderId: null, selectedModel: null, promptPresets: [], builtInPromptPresets: [], activePromptPresetId: 'preset_general', promptEnhancerTemperature: 0.6 },
    settings: { autostart: false, startMinimized: false, minimizeToTray: true, notifications: true, maskPublicIp: false, maskShortcutPaths: false, enableOpenCodeQuota: localStorage.getItem('agents_hub_enable_opencode_quota') === 'true', enableProtonVpnProvider: localStorage.getItem('agents_hub_enable_proton') === 'true', enableHardwareMonitor: localStorage.getItem('agents_hub_enable_hardware_monitor') === 'true', theme: 'dark', accentColor: '#4049B9' },
    activeView: (function() {
      try {
        return localStorage.getItem('agents_hub_active_view') || 'dashboard';
      } catch {
        return 'dashboard';
      }
    })(),
    activeTerminalCli: (function() {
      try {
        return localStorage.getItem('agents_hub_active_cli_tab') || 'hermes';
      } catch {
        return 'hermes';
      }
    })(),
    selectedProvider: null,
    workspaces: [],
    activeWorkspaceId: null,
    tokenTelemetry: null,
    pendingRequests: new Map(),
    logs: [],
    currentLogFilter: 'all',
    logSearchQuery: '',
    logAutoScroll: true,
    currentPrompt: null,
    runningPorts: []
};
var state = window.state;

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}
window.escapeHtml = escapeHtml;
var escapeHtml = window.escapeHtml;
