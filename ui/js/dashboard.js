// Agents Hub - Modern Modular Entry Point Coordinator
(async function initAgentsHub() {
  'use strict';

  // 1. Wait for HTML partial template includes (views & modals)
  if (window.__htmlPartialsPromise) {
    await window.__htmlPartialsPromise;
  }

  // 2. Initialize Theme immediately
  try {
    const savedTheme = localStorage.getItem('agents_hub_theme') || 'dark';
    const savedColor = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
    if (typeof window.applyAdaptiveTheme === 'function') {
      window.applyAdaptiveTheme(savedTheme, savedColor);
    }
  } catch (e) {
    console.warn('Theme initialization error:', e);
  }

  // 3. Load Initial Application State from Backend Bridge First
  if (typeof window.loadInitialState === 'function') {
    try {
      await window.loadInitialState();
    } catch (e) {
      console.warn('Initial state preload error:', e);
    }
  }

  // 4. Initialize Core Subsystems
  if (typeof window.setupNavigation === 'function') window.setupNavigation();
  if (typeof window.setupCommandPalette === 'function') window.setupCommandPalette();
  if (typeof window.setupHardwareTelemetry === 'function') window.setupHardwareTelemetry();
  if (typeof window.setupLogFilterListeners === 'function') window.setupLogFilterListeners();
  if (typeof window.setupPromptModalListeners === 'function') window.setupPromptModalListeners();

  // 5. Initialize Feature Modules
  if (typeof window.setupSettingsView === 'function') window.setupSettingsView();
  if (typeof window.setupTerminalPresets === 'function') window.setupTerminalPresets();
  if (typeof window.setupDependencies === 'function') window.setupDependencies();
  if (typeof window.setupPromptEnhancer === 'function') window.setupPromptEnhancer();
  if (typeof window.setupProvidersView === 'function') window.setupProvidersView();
  if (typeof window.setupClaudeGatewayConfig === 'function') window.setupClaudeGatewayConfig();
  if (typeof window.setupWorkspaces === 'function') window.setupWorkspaces();
  if (typeof window.setupWorkspaceSnapshots === 'function') window.setupWorkspaceSnapshots();
  if (typeof window.setupSoundpackAudio === 'function') window.setupSoundpackAudio();
  if (typeof window.setupScheduledAutomations === 'function') window.setupScheduledAutomations();
  if (typeof window.setupVoiceCommander === 'function') window.setupVoiceCommander();
  if (typeof window.setupPreCommitSecurity === 'function') window.setupPreCommitSecurity();
  if (typeof window.setupRemoteWebhooks === 'function') window.setupRemoteWebhooks();
  if (typeof window.setupTimelapseRecorder === 'function') window.setupTimelapseRecorder();
  if (typeof window.setupDevToolsEditor === 'function') window.setupDevToolsEditor();
  if (typeof window.setupShortcutEditor === 'function') window.setupShortcutEditor();
  if (typeof window.setupQuotaAndIpWidget === 'function') window.setupQuotaAndIpWidget();
  if (typeof window.renderOpenCodeCard === 'function') window.renderOpenCodeCard();
  if (typeof window.setupUpdateManager === 'function') window.setupUpdateManager();
  if (typeof window.setupSystemDoctor === 'function') window.setupSystemDoctor();
  if (typeof window.setupMcpSkillsManager === 'function') window.setupMcpSkillsManager();
  if (typeof window.setupCustomCliManager === 'function') window.setupCustomCliManager();
  if (typeof window.setupFileWatchdogManager === 'function') window.setupFileWatchdogManager();
  if (typeof window.setupSessionStateAutoSave === 'function') window.setupSessionStateAutoSave();
  if (typeof window.setupGlobalActions === 'function') window.setupGlobalActions();

  // 5. Expose AgentsHub Global API
  window.AgentsHub = {
    state: window.state,
    callBridge: window.callBridge,
    updateVpnProviderUi: window.updateVpnProviderUi,
    updateHardwareMonitorVisibility: window.updateHardwareMonitorVisibility,
    showToast: window.showToast
  };

  // 6. Handle Preference Changes
  window.addEventListener('preference-changed', (e) => {
    if (e.detail && e.detail.key === 'enableProtonVpnProvider') {
      window.state.settings.enableProtonVpnProvider = !!e.detail.value;
      if (!window.state.settings.enableProtonVpnProvider && window.state.vpnProvider === 'proton') {
        window.state.vpnProvider = 'warp';
        localStorage.setItem('agents_hub_vpn_provider', 'warp');
      }
      if (typeof window.updateVpnProviderUi === 'function') window.updateVpnProviderUi();
    }
    if (e.detail && e.detail.key === 'enableHardwareMonitor') {
      window.state.settings.enableHardwareMonitor = !!e.detail.value;
      if (typeof window.updateHardwareMonitorVisibility === 'function') window.updateHardwareMonitorVisibility();
    }
  });
})();
