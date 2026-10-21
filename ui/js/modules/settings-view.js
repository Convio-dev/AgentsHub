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

  function setupSettingsView() {
    // About card: open links in the system browser instead of navigating the WebView.
    document.querySelectorAll('.about-link').forEach(btn => {
      btn.addEventListener('click', () => {
        const url = btn.dataset.url;
        if (url) callBridge('open_url_external', { url });
      });
    });

    renderAboutVersion();
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
      const enableProton = (document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox'))?.checked === true;
      const enableHardwareMonitor = (document.getElementById('pref-enable-hardware-monitor') || document.getElementById('setting-enable-hardware-monitor-checkbox'))?.checked === true;
      const theme = document.documentElement.getAttribute('data-theme') || 'dark';
      const accentColor = localStorage.getItem('agents_hub_accent_color') || '#10b981';
      const feedback = document.getElementById('settings-save-feedback');

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
          enableProtonVpnProvider: enableProton,
          enableHardwareMonitor,
          theme,
          accentColor
        }
      });

      feedback.textContent = 'Preferences saved successfully!';
      setTimeout(() => feedback.textContent = '', 2500);
    });

    // Reactive checkbox toggle for Proton VPN Provider
    const protonToggle = document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox');
    protonToggle?.addEventListener('change', async (e) => {
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
  }

  let latestUpdateData = null;

window.setupSettingsView = setupSettingsView;

window.renderAboutVersion = renderAboutVersion;
var renderAboutVersion = window.renderAboutVersion;
