  function renderAboutVersion() {
    const version = state.version || '';

    const about = document.getElementById('about-version');
    if (about) about.textContent = version;

    const brand = document.getElementById('brand-version');
    if (brand) brand.textContent = version.toUpperCase();

    const bootLine = document.getElementById('boot-log-line');
    if (bootLine) {
      bootLine.textContent = version ? 'Agents Hub ' + version + ' active.' : 'Agents Hub starting.';
    }
  }

  function updateProtonVpnSettingState(opencodeEnabled) {
    const protonWrap = document.getElementById('wrap-proton-vpn-setting');
    const protonCheck = document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox');
    if (protonWrap) {
      protonWrap.style.opacity = opencodeEnabled ? '1' : '0.45';
      protonWrap.style.pointerEvents = opencodeEnabled ? 'auto' : 'none';
    }
    if (protonCheck) {
      protonCheck.disabled = !opencodeEnabled;
      if (!opencodeEnabled && protonCheck.checked) {
        protonCheck.checked = false;
        state.settings.enableProtonVpnProvider = false;
        localStorage.setItem('agents_hub_enable_proton', 'false');
        if (state.vpnProvider === 'proton') {
          state.vpnProvider = 'warp';
          localStorage.setItem('agents_hub_vpn_provider', 'warp');
        }
        if (typeof updateVpnProviderUi === 'function') {
          updateVpnProviderUi();
        }
      }
    }
  }
  window.updateProtonVpnSettingState = updateProtonVpnSettingState;
  var updateProtonVpnSettingState = window.updateProtonVpnSettingState;

  function setupSettingsView() {
    // About card: open links in the system browser instead of navigating the WebView.
    document.querySelectorAll('.about-link').forEach(btn => {
      btn.addEventListener('click', () => {
        const url = btn.dataset.url;
        if (url) callBridge('open_url_external', { url });
      });
    });

    renderAboutVersion();

    // Setup Category Sidebar Navigation
    const navItems = document.querySelectorAll('.settings-nav-item');
    const categoryPanes = document.querySelectorAll('.settings-category-pane');

    function switchSettingsTab(targetTab) {
      if (!targetTab) return;
      navItems.forEach(item => {
        item.classList.toggle('active', item.dataset.tab === targetTab);
      });
      categoryPanes.forEach(pane => {
        pane.classList.toggle('active', pane.dataset.category === targetTab);
      });
      localStorage.setItem('agents_hub_active_settings_tab', targetTab);
      try {
        window.dispatchEvent(new CustomEvent('settings-tab-switched', { detail: { activeTab: targetTab } }));
      } catch (e) {}
    }
    window.switchSettingsTab = switchSettingsTab;

    navItems.forEach(item => {
      item.addEventListener('click', () => {
        switchSettingsTab(item.dataset.tab);
      });
    });

    const savedSettingsTab = localStorage.getItem('agents_hub_active_settings_tab') || 'appearance';
    switchSettingsTab(savedSettingsTab);

    // Setup Real-Time Settings Search
    const searchInput = document.getElementById('input-settings-search');
    const clearSearchBtn = document.getElementById('btn-clear-settings-search');

    if (searchInput) {
      searchInput.addEventListener('input', (e) => {
        const query = e.target.value.trim().toLowerCase();
        if (clearSearchBtn) {
          clearSearchBtn.style.display = query ? 'block' : 'none';
        }

        if (!query) {
          navItems.forEach(item => {
            item.style.opacity = '1';
            item.style.pointerEvents = 'auto';
          });
          const currentTab = localStorage.getItem('agents_hub_active_settings_tab') || 'appearance';
          switchSettingsTab(currentTab);
          return;
        }

        let firstMatchingTab = null;
        categoryPanes.forEach(pane => {
          const cat = pane.dataset.category;
          const text = pane.textContent.toLowerCase();
          const matches = text.includes(query);
          const navItem = document.querySelector(`.settings-nav-item[data-tab="${cat}"]`);

          if (navItem) {
            navItem.style.opacity = matches ? '1' : '0.35';
          }

          if (matches && !firstMatchingTab) {
            firstMatchingTab = cat;
          }
        });

        if (firstMatchingTab) {
          switchSettingsTab(firstMatchingTab);
        }
      });

      clearSearchBtn?.addEventListener('click', () => {
        searchInput.value = '';
        clearSearchBtn.style.display = 'none';
        navItems.forEach(item => {
          item.style.opacity = '1';
        });
        const currentTab = localStorage.getItem('agents_hub_active_settings_tab') || 'appearance';
        switchSettingsTab(currentTab);
        searchInput.focus();
      });
    }

    // Theme toggles
    document.querySelectorAll('.btn-theme-toggle').forEach(b => {
      b.addEventListener('click', () => {
        const theme = b.dataset.theme;
        const color = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
        applyAdaptiveTheme(theme, color);
        callBridge('save_general_settings', {
          settings: { theme, accentColor: color }
        }).catch(() => {});
      });
    });

    // Color Swatches
    document.querySelectorAll('.color-swatch').forEach(sw => {
      sw.addEventListener('click', () => {
        const color = sw.dataset.color;
        const theme = localStorage.getItem('agents_hub_theme') || 'dark';
        applyAdaptiveTheme(theme, color);
        callBridge('save_general_settings', {
          settings: { theme, accentColor: color }
        }).catch(() => {});
      });
    });

    // Color Intensity Slider
    const slider = document.getElementById('theme-intensity-slider');
    const sliderVal = document.getElementById('theme-intensity-val');
    slider?.addEventListener('input', (e) => {
      const intVal = e.target.value;
      if (sliderVal) sliderVal.textContent = intVal + '%';
      const theme = localStorage.getItem('agents_hub_theme') || 'dark';
      const color = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
      applyAdaptiveTheme(theme, color, intVal);
    });

    // Custom Color Picker & Hex Input
    const picker = document.getElementById('accent-color-picker');
    const hexInput = document.getElementById('accent-hex-input');

    picker?.addEventListener('input', (e) => {
      const color = e.target.value;
      const theme = localStorage.getItem('agents_hub_theme') || 'dark';
      applyAdaptiveTheme(theme, color);
    });

    picker?.addEventListener('change', (e) => {
      const color = e.target.value;
      const theme = localStorage.getItem('agents_hub_theme') || 'dark';
      callBridge('save_general_settings', {
        settings: { theme, accentColor: color }
      }).catch(() => {});
    });

    const triggerApplyCustomHex = () => {
      let val = hexInput.value.trim();
      if (!val) return;
      if (!val.startsWith('#')) val = '#' + val;
      const theme = localStorage.getItem('agents_hub_theme') || 'dark';
      applyAdaptiveTheme(theme, val);
      callBridge('save_general_settings', {
        settings: { theme, accentColor: val }
      }).catch(() => {});
    };

    document.getElementById('btn-apply-custom-color')?.addEventListener('click', triggerApplyCustomHex);

    hexInput?.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        triggerApplyCustomHex();
      }
    });

    // Save General Settings Button
    document.getElementById('btn-save-general-settings')?.addEventListener('click', async () => {
      const autostart = document.getElementById('setting-autostart-checkbox').checked;
      const startMinimized = document.getElementById('setting-minimized-checkbox').checked;
      const minimizeToTray = document.getElementById('setting-tray-minimize-checkbox')?.checked !== false;
      const notifications = document.getElementById('setting-notifications-checkbox')?.checked !== false;
      const globalHotkey = document.getElementById('setting-global-hotkey-checkbox')?.checked !== false;
      const globalHotkeyModifier = document.getElementById('setting-window-modifier')?.value || 'Ctrl+Alt';
      const globalHotkeyKey = document.getElementById('setting-window-key')?.value || 'H';
      const globalHudHotkey = document.getElementById('setting-global-hud-hotkey-checkbox')?.checked !== false;
      const hudHotkeyModifier = document.getElementById('setting-quake-modifier')?.value || 'None';
      const hudHotkeyKey = document.getElementById('setting-quake-key')?.value || 'F12';
      const hudHotkeyTarget = document.getElementById('setting-quake-target')?.value || 'cliwebview';
      const maskShortcutPaths = document.getElementById('setting-mask-paths-checkbox')?.checked === true;
      const embedShells = document.getElementById('setting-embed-shells-checkbox')?.checked === true;
      const hideMainOnCliOpen = document.getElementById('setting-hide-main-on-cli-checkbox')?.checked === true;
      const enableOpenCodeQuota = document.getElementById('pref-enable-opencode-quota')?.checked === true;
      let enableProton = (document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox'))?.checked === true;
      if (!enableOpenCodeQuota) {
        enableProton = false;
      }
      const enableHardwareMonitor = (document.getElementById('pref-enable-hardware-monitor') || document.getElementById('setting-enable-hardware-monitor-checkbox'))?.checked === true;
      const theme = document.documentElement.getAttribute('data-theme') || 'dark';
      const accentColor = localStorage.getItem('agents_hub_accent_color') || '#10b981';
      const feedback = document.getElementById('settings-save-feedback');

      state.settings.enableOpenCodeQuota = enableOpenCodeQuota;
      localStorage.setItem('agents_hub_enable_opencode_quota', enableOpenCodeQuota);
      if (typeof updateOpenCodeQuotaVisibility === 'function') {
        updateOpenCodeQuotaVisibility();
      }
      updateProtonVpnSettingState(enableOpenCodeQuota);

      state.settings.enableProtonVpnProvider = enableProton;
      localStorage.setItem('agents_hub_enable_proton', enableProton);
      if (!enableProton && state.vpnProvider === 'proton') {
        state.vpnProvider = 'warp';
        localStorage.setItem('agents_hub_vpn_provider', 'warp');
      }
      updateVpnProviderUi();

      state.settings.enableHardwareMonitor = enableHardwareMonitor;
      localStorage.setItem('agents_hub_enable_hardware_monitor', enableHardwareMonitor);
      updateHardwareMonitorVisibility();

      state.isShortcutsMasked = maskShortcutPaths;
      localStorage.setItem('agents_hub_mask_shortcuts', maskShortcutPaths);
      renderDashboardShortcuts();

      await callBridge('save_general_settings', {
        settings: {
          autostart,
          startMinimized,
          minimizeToTray,
          notifications,
          globalHotkey,
          globalHotkeyModifier,
          globalHotkeyKey,
          globalHudHotkey,
          hudHotkeyKey,
          hudHotkeyModifier,
          hudHotkeyTarget,
          maskShortcutPaths,
          embedShellsInTerminalWorkspace: embedShells,
          hideMainOnCliOpen,
          enableOpenCodeQuota,
          enableProtonVpnProvider: enableProton,
          enableHardwareMonitor,
          theme,
          accentColor
        }
      });

      feedback.textContent = 'Preferences saved successfully!';
      setTimeout(() => feedback.textContent = '', 2500);
    });

    // Reactive checkbox toggle for OpenCode Quota & IP Card (Master parent for Proton VPN)
    const opencodeToggle = document.getElementById('pref-enable-opencode-quota');
    opencodeToggle?.addEventListener('change', async (e) => {
      const isEnabled = !!e.target.checked;
      state.settings.enableOpenCodeQuota = isEnabled;
      localStorage.setItem('agents_hub_enable_opencode_quota', isEnabled);
      if (typeof updateOpenCodeQuotaVisibility === 'function') {
        updateOpenCodeQuotaVisibility();
      }
      updateProtonVpnSettingState(isEnabled);

      const payload = { enableOpenCodeQuota: isEnabled };
      if (!isEnabled) {
        payload.enableProtonVpnProvider = false;
      }
      await callBridge('save_general_settings', {
        settings: payload
      });
      window.dispatchEvent(new CustomEvent('preference-changed', {
        detail: { key: 'enableOpenCodeQuota', value: isEnabled }
      }));
    });

    // Reactive checkbox toggle for Proton VPN Provider (Sub-feature of OpenCode Quota)
    const protonToggle = document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox');
    protonToggle?.addEventListener('change', async (e) => {
      const isOpencodeEnabled = state.settings.enableOpenCodeQuota === true || localStorage.getItem('agents_hub_enable_opencode_quota') === 'true';
      if (!isOpencodeEnabled) {
        e.target.checked = false;
        return;
      }
      const isEnabled = !!e.target.checked;
      state.settings.enableProtonVpnProvider = isEnabled;
      localStorage.setItem('agents_hub_enable_proton', isEnabled);
      if (!isEnabled && state.vpnProvider === 'proton') {
        state.vpnProvider = 'warp';
        localStorage.setItem('agents_hub_vpn_provider', 'warp');
      }
      updateVpnProviderUi();
      await callBridge('save_general_settings', {
        settings: { enableProtonVpnProvider: isEnabled }
      });
      window.dispatchEvent(new CustomEvent('preference-changed', {
        detail: { key: 'enableProtonVpnProvider', value: isEnabled }
      }));
    });

    // Reactive checkbox toggle for System & Hardware Telemetry Monitor
    const hwToggle = document.getElementById('pref-enable-hardware-monitor') || document.getElementById('setting-enable-hardware-monitor-checkbox');
    hwToggle?.addEventListener('change', async (e) => {
      const isEnabled = !!e.target.checked;
      state.settings.enableHardwareMonitor = isEnabled;
      localStorage.setItem('agents_hub_enable_hardware_monitor', isEnabled);
      updateHardwareMonitorVisibility();
      await callBridge('save_general_settings', {
        settings: { enableHardwareMonitor: isEnabled }
      });
      window.dispatchEvent(new CustomEvent('preference-changed', {
        detail: { key: 'enableHardwareMonitor', value: isEnabled }
      }));
    });

    document.getElementById('setting-embed-shells-checkbox')?.addEventListener('change', async (e) => {
      await callBridge('save_general_settings', {
        settings: { embedShellsInTerminalWorkspace: e.target.checked }
      });
    });

    document.getElementById('setting-hide-main-on-cli-checkbox')?.addEventListener('change', async (e) => {
      await callBridge('save_general_settings', {
        settings: { hideMainOnCliOpen: e.target.checked }
      });
    });

    // Codebase Architecture & Blast-Radius Graph Feature Toggle
    const archGraphCheckbox = document.getElementById('setting-architecture-graph-checkbox');
    if (archGraphCheckbox) {
      archGraphCheckbox.checked = localStorage.getItem('agents_hub_enable_architecture_graph') === 'true'; // Default false (disabled)
      archGraphCheckbox.addEventListener('change', (e) => {
        const isEnabled = !!e.target.checked;
        localStorage.setItem('agents_hub_enable_architecture_graph', isEnabled ? 'true' : 'false');
        updateArchitectureGraphNavVisibility();
        showToast(isEnabled ? 'Architecture & Blast-Radius Graph enabled' : 'Architecture & Blast-Radius Graph disabled', 'info');
      });
    }

    // Unified Audio & Chimes Master Settings Synchronization
    const chimesCheckbox = document.getElementById('setting-audio-chimes-checkbox');
    const volumeSlider = document.getElementById('setting-audio-volume');
    const volumeVal = document.getElementById('audio-volume-val');
    const btnTestChime = document.getElementById('btn-test-chime');

    if (chimesCheckbox) {
      chimesCheckbox.checked = localStorage.getItem('agents_hub_soundpack_enabled') === 'true'; // Default turned off
      chimesCheckbox.addEventListener('change', () => {
        window.setSoundpackMasterEnabled?.(chimesCheckbox.checked);
      });
    }

    if (volumeSlider) {
      const savedVol = localStorage.getItem('agents_hub_soundpack_vol') || '70';
      volumeSlider.value = savedVol;
      if (volumeVal) volumeVal.textContent = savedVol + '%';

      volumeSlider.addEventListener('input', (e) => {
        const val = e.target.value;
        if (volumeVal) volumeVal.textContent = val + '%';
        window.setSoundpackMasterVolume?.(val);
      });
    }

    btnTestChime?.addEventListener('click', () => {
      const vol = parseInt(volumeSlider?.value || '70', 10);
      callBridge('play_sound_event', { eventName: 'task_done', theme: 'modern', volume: vol, force: true });
    });

    setupUpdateManager();
    setupSystemDoctor();
    setupMcpSkillsManager();
    setupCustomCliManager();
    setupFileWatchdogManager();
    setupSessionStateAutoSave();
    updateArchitectureGraphNavVisibility();
  }

  function updateArchitectureGraphNavVisibility() {
    const isEnabled = localStorage.getItem('agents_hub_enable_architecture_graph') === 'true';
    const navItem = document.getElementById('nav-item-architecture');
    if (navItem) {
      navItem.style.display = isEnabled ? '' : 'none';
    }
    const targetPane = document.getElementById('view-architecture');
    if (!isEnabled && targetPane && targetPane.classList.contains('active')) {
      const dashItem = document.querySelector('.nav-item[data-view="dashboard"]');
      if (dashItem) dashItem.click();
    }
  }
  window.updateArchitectureGraphNavVisibility = updateArchitectureGraphNavVisibility;

  let latestUpdateData = null;

window.setupSettingsView = setupSettingsView;

window.renderAboutVersion = renderAboutVersion;
var renderAboutVersion = window.renderAboutVersion;
