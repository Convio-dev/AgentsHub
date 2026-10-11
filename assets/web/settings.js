// Agents Hub - Settings Controller & Preferences Integration
(function () {
  'use strict';

  function initSettings() {
    const protonToggle = document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox');
    if (!protonToggle) return;

    // Load initial preference from localStorage or state
    const savedPref = localStorage.getItem('agents_hub_enable_proton');
    if (savedPref !== null) {
      protonToggle.checked = savedPref === 'true';
    } else {
      protonToggle.checked = false;
    }

    // Reactive Toggle Event Listener
    protonToggle.addEventListener('change', (e) => {
      const isEnabled = !!e.target.checked;
      localStorage.setItem('agents_hub_enable_proton', isEnabled);

      // Dispatch reactive event for dashboard card and components
      window.dispatchEvent(new CustomEvent('preference-changed', {
        detail: { key: 'enableProtonVpnProvider', value: isEnabled }
      }));

      // Directly update dashboard engine state if available
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

      // Save to C# backend bridge
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({
          action: 'save_general_settings',
          settings: { enableProtonVpnProvider: isEnabled }
        });
      }
    });
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
    }
  };
})();
