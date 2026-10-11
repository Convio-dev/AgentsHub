// Agents Hub - Settings Controller & Preferences Integration
(function () {
  'use strict';

  function initSettings() {
    // 1. Proton VPN Provider Toggle
    const protonToggle = document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox');
    if (protonToggle) {
      const savedProtonPref = localStorage.getItem('agents_hub_enable_proton');
      protonToggle.checked = savedProtonPref === 'true';

      protonToggle.addEventListener('change', (e) => {
        const isEnabled = !!e.target.checked;
        localStorage.setItem('agents_hub_enable_proton', isEnabled);

        window.dispatchEvent(new CustomEvent('preference-changed', {
          detail: { key: 'enableProtonVpnProvider', value: isEnabled }
        }));

        if (window.AgentsHub) {
          if (window.AgentsHub.state && window.AgentsHub.state.settings) {
            window.AgentsHub.state.settings.enableProtonVpnProvider = isEnabled;
            if (!isEnabled && window.AgentsHub.state.vpnProvider === 'proton') {
              window.AgentsHub.state.vpnProvider = 'warp';
              localStorage.setItem('agents_hub_vpn_provider', 'warp');
            }
          }
          if (typeof window.AgentsHub.updateVpnProviderUi === 'function') {
            window.AgentsHub.updateVpnProviderUi();
          }
        }

        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage({
            action: 'save_general_settings',
            settings: { enableProtonVpnProvider: isEnabled }
          });
        }
      });
    }

    // 2. System & Hardware Telemetry Monitor Toggle (default disabled / false)
    const hwToggle = document.getElementById('pref-enable-hardware-monitor') || document.getElementById('setting-enable-hardware-monitor-checkbox');
    if (hwToggle) {
      const savedHwPref = localStorage.getItem('agents_hub_enable_hardware_monitor');
      hwToggle.checked = savedHwPref === 'true';

      hwToggle.addEventListener('change', (e) => {
        const isEnabled = !!e.target.checked;
        localStorage.setItem('agents_hub_enable_hardware_monitor', isEnabled);

        window.dispatchEvent(new CustomEvent('preference-changed', {
          detail: { key: 'enableHardwareMonitor', value: isEnabled }
        }));

        if (window.AgentsHub) {
          if (window.AgentsHub.state && window.AgentsHub.state.settings) {
            window.AgentsHub.state.settings.enableHardwareMonitor = isEnabled;
          }
          if (typeof window.AgentsHub.updateHardwareMonitorVisibility === 'function') {
            window.AgentsHub.updateHardwareMonitorVisibility();
          }
        }

        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.postMessage({
            action: 'save_general_settings',
            settings: { enableHardwareMonitor: isEnabled }
          });
        }
      });
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSettings);
  } else {
    initSettings();
  }

  // Expose helper API
  window.AgentsHubSettings = {
    isProtonEnabled: function () {
      return localStorage.getItem('agents_hub_enable_proton') === 'true';
    },
    setProtonEnabled: function (val) {
      const isEnabled = !!val;
      localStorage.setItem('agents_hub_enable_proton', isEnabled);
      const toggle = document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox');
      if (toggle) toggle.checked = isEnabled;
      window.dispatchEvent(new CustomEvent('preference-changed', {
        detail: { key: 'enableProtonVpnProvider', value: isEnabled }
      }));
    },
    isHardwareMonitorEnabled: function () {
      return localStorage.getItem('agents_hub_enable_hardware_monitor') === 'true';
    },
    setHardwareMonitorEnabled: function (val) {
      const isEnabled = !!val;
      localStorage.setItem('agents_hub_enable_hardware_monitor', isEnabled);
      const toggle = document.getElementById('pref-enable-hardware-monitor') || document.getElementById('setting-enable-hardware-monitor-checkbox');
      if (toggle) toggle.checked = isEnabled;
      window.dispatchEvent(new CustomEvent('preference-changed', {
        detail: { key: 'enableHardwareMonitor', value: isEnabled }
      }));
    }
  };
})();
