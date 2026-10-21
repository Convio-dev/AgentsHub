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
  if (typeof window.setupDiscordRpcSettings === 'function') window.setupDiscordRpcSettings();
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
  if (typeof window.initRepositoriesModule === 'function') window.initRepositoriesModule();

  function updateDashboardGridClasses() {
    const grid = document.querySelector('#view-dashboard .card-grid');
    if (!grid) return;
    const isOpencode = (window.state?.settings?.enableOpenCodeQuota === true) || (localStorage.getItem('agents_hub_enable_opencode_quota') === 'true');
    const isHardware = (window.state?.settings?.enableHardwareMonitor === true) || (localStorage.getItem('agents_hub_enable_hardware_monitor') === 'true');
    grid.classList.toggle('has-opencode', isOpencode);
    grid.classList.toggle('has-hardware-monitor', isHardware);
  }
  window.updateDashboardGridClasses = updateDashboardGridClasses;
  updateDashboardGridClasses();

  // 5. Expose AgentsHub Global API
  window.AgentsHub = {
    state: window.state,
    callBridge: window.callBridge,
    updateDashboardGridClasses: window.updateDashboardGridClasses,
    updateOpenCodeQuotaVisibility: window.updateOpenCodeQuotaVisibility,
    updateProtonVpnSettingState: window.updateProtonVpnSettingState,
    updateVpnProviderUi: window.updateVpnProviderUi,
    updateHardwareMonitorVisibility: window.updateHardwareMonitorVisibility,
    showToast: window.showToast
  };

  // 6. Handle Preference Changes
  window.addEventListener('preference-changed', (e) => {
    if (e.detail && (e.detail.key === 'enableOpenCodeQuota' || e.detail.key === 'enableHardwareMonitor')) {
      updateDashboardGridClasses();
    }
    if (e.detail && e.detail.key === 'enableOpenCodeQuota') {
      window.state.settings.enableOpenCodeQuota = !!e.detail.value;
      if (typeof window.updateOpenCodeQuotaVisibility === 'function') window.updateOpenCodeQuotaVisibility();
      if (typeof window.updateProtonVpnSettingState === 'function') window.updateProtonVpnSettingState(!!e.detail.value);
    }
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
