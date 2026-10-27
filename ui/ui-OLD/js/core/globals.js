// Global Actions & App Lifecycle
  function setupGlobalActions() {
    document.getElementById('btn-ctrl-hermes')?.addEventListener('click', () => callBridge('toggle_cli', { cli: 'Hermes' }));
    document.getElementById('btn-restart-hermes')?.addEventListener('click', () => callBridge('restart_cli', { cli: 'Hermes' }));

    document.getElementById('btn-ctrl-claude')?.addEventListener('click', () => callBridge('toggle_cli', { cli: 'ClaudeCode' }));
    document.getElementById('btn-restart-claude')?.addEventListener('click', () => callBridge('restart_cli', { cli: 'ClaudeCode' }));

    document.getElementById('btn-ctrl-antigravity')?.addEventListener('click', () => callBridge('toggle_cli', { cli: 'AntigravityCli' }));
    document.getElementById('btn-restart-antigravity')?.addEventListener('click', () => callBridge('restart_cli', { cli: 'AntigravityCli' }));

    document.getElementById('btn-toggle-all-clis')?.addEventListener('click', async () => {
      const anyRunning = Object.values(state.clis).some(c => c.state === 'Running');
      await callBridge('toggle_cli', { cli: 'Hermes' });
      await callBridge('toggle_cli', { cli: 'ClaudeCode' });
      await callBridge('toggle_cli', { cli: 'AntigravityCli' });
    });

    document.getElementById('btn-refresh-state')?.addEventListener('click', loadInitialState);
    document.getElementById('btn-toast-history')?.addEventListener('click', (e) => {
      e.stopPropagation();
      toggleToastHistory();
      document.getElementById('btn-toast-history')?.setAttribute('aria-expanded', String(document.getElementById('toast-history-panel').classList.contains('open')));
    });
    document.getElementById('btn-clear-toast-history')?.addEventListener('click', (e) => {
      e.stopPropagation();
      clearToastHistory();
    });
    // Clicking anywhere else closes the panel, the same way the other header overlays work.
    document.addEventListener('click', (e) => {
      const panel = document.getElementById('toast-history-panel');
      if (!panel || !panel.classList.contains('open')) return;
      if (e.target.closest('.toast-history-wrap')) return;
      panel.classList.remove('open');
    });

    document.getElementById('btn-copy-lan-url')?.addEventListener('click', () => {
      const urlText = document.getElementById('dash-lan-url')?.textContent || '';
      navigator.clipboard.writeText(urlText);
      appendLog('Copied Web Host URL: ' + urlText);
    });
    document.getElementById('btn-open-lan-url')?.addEventListener('click', () => {
      const urlText = document.getElementById('dash-lan-url')?.textContent || '';
      callBridge('open_url_external', { url: urlText });
    });
    document.getElementById('btn-toggle-dash-url-mode')?.addEventListener('click', () => {
      if (!state.lanProxyEnabled) {
        appendLog('LAN connection is turned off. Turn on LAN from LAN Remote & QR to view network links.');
        return;
      }
      state.dashUrlMode = state.dashUrlMode === 'local' ? 'lan' : 'local';
      renderRunningPorts();
      appendLog('Switched Dashboard Web Host view to: ' + (state.dashUrlMode === 'lan' ? 'Network (LAN)' : 'Local (127.0.0.1)'));
    });

    document.getElementById('btn-copy-lan-tab-url')?.addEventListener('click', () => {
      const urlText = document.getElementById('lan-full-url')?.textContent || '';
      copyToClipboard(urlText);
      appendLog('Copied LAN Remote URL: ' + urlText);
    });
    document.getElementById('btn-open-lan-tab-url')?.addEventListener('click', () => {
      const urlText = document.getElementById('lan-full-url')?.textContent || '';
      callBridge('open_url_external', { url: urlText });
    });
    document.getElementById('btn-toggle-lan-url-mode')?.addEventListener('click', () => {
      if (!state.lanProxyEnabled) {
        appendLog('LAN connection is turned off. Turn on LAN to view network links.');
        return;
      }
      state.lanUrlMode = state.lanUrlMode === 'lan' ? 'local' : 'lan';
      renderRunningPorts();
      appendLog('Switched LAN Remote view to: ' + (state.lanUrlMode === 'lan' ? 'Network (LAN)' : 'Local (127.0.0.1)'));
    });

    document.getElementById('btn-toggle-lan-network')?.addEventListener('click', async () => {
      const targetState = !state.lanProxyEnabled;
      try {
        const res = await callBridge('toggle_lan_proxy', { enabled: targetState });
        if (res) {
          state.lanProxyEnabled = !!res.lanProxyEnabled;
          if (res.runningPorts) state.runningPorts = res.runningPorts;
          if (!state.lanProxyEnabled) {
            state.dashUrlMode = 'local';
            state.lanUrlMode = 'local';
            appendLog('LAN network connection turned OFF. All links switched to 127.0.0.1.');
          } else {
            state.lanUrlMode = 'lan';
            appendLog('LAN network connection turned ON (192.168 active).');
          }
          renderRunningPorts();
          // Which ports bound, and whether the firewall now matters, both changed.
          loadLanState(true);
        }
      } catch (err) {
        appendLog('Failed to toggle LAN network: ' + (err.message || err));
      }
    });

    document.getElementById('btn-lan-refresh')?.addEventListener('click', () => loadLanState(true));
    document.getElementById('btn-lan-firewall-allow')?.addEventListener('click', () => requestFirewallRule());
    document.getElementById('btn-lan-firewall-remove')?.addEventListener('click', () => removeFirewallRule());
    document.getElementById('lan-qr-retry')?.addEventListener('click', () => {
      lanQrUrl = '';
      renderLanPage();
    });

    // Modal: Add profile
    const modal = document.getElementById('modal-add-profile');
    const nameInp = document.getElementById('modal-profile-name-input');
    document.getElementById('btn-show-add-profile-modal')?.addEventListener('click', () => {
      nameInp.value = `Account_${(state.antigravity.profiles || []).length + 1}`;
      modal.classList.add('open');
      nameInp.focus();
    });
    document.getElementById('btn-cancel-add-profile')?.addEventListener('click', () => modal.classList.remove('open'));
    document.getElementById('btn-confirm-add-profile')?.addEventListener('click', async () => {
      const name = nameInp.value.trim();
      if (name) {
        try {
          const res = await callBridge('create_antigravity_profile', { profileName: name });
          if (res) {
            if (res.activeProfile !== undefined) state.antigravity.activeProfile = res.activeProfile;
            if (res.profiles) state.antigravity.profiles = res.profiles;
            renderProfiles();
            renderQuickProfileSelect();
            updateHeaderStatus();
          }
        } catch (e) {
          console.error('Create profile failed:', e);
        }
        modal.classList.remove('open');
      }
    });

    document.getElementById('btn-clear-logs')?.addEventListener('click', () => {
      const logsEl = document.getElementById('logs-container');
      if (logsEl) logsEl.innerHTML = '<div>[System] Logs cleared.</div>';
    });

    // Standalone Terminals Window Popouts
    const openStandalone = () => callBridge('open_standalone_terminals', { cli: normalizeCliKey(state.activeTerminalCli) });
    document.getElementById('btn-popout-terminals')?.addEventListener('click', openStandalone);
    document.getElementById('btn-popout-terminals-dash')?.addEventListener('click', openStandalone);
    document.getElementById('btn-open-standalone-hdr')?.addEventListener('click', openStandalone);
    document.getElementById('btn-open-antigravity-app-dash')?.addEventListener('click', launchAntigravityAppFromDash);
    document.getElementById('btn-toggle-profile-email-dash')?.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      isProfileEmailVisible = !isProfileEmailVisible;
      updateProfileEmailUi();
    });

    // Native System Shell Launchers (Standard & Administrator)
    document.getElementById('btn-shell-cmd')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'cmd', asAdmin: false });
      appendLog('[Shell] Launched Command Prompt (cmd.exe)');
    });
    document.getElementById('btn-shell-cmd-admin')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'cmd', asAdmin: true });
      appendLog('[Shell] Launched Command Prompt (Admin)');
    });
    document.getElementById('btn-shell-ps')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'powershell', asAdmin: false });
      appendLog('[Shell] Launched Windows PowerShell');
    });
    document.getElementById('btn-shell-ps-admin')?.addEventListener('click', () => {
      callBridge('open_system_shell', { shell: 'powershell', asAdmin: true });
      appendLog('[Shell] Launched Windows PowerShell (Admin)');
    });
  }

  // Load Initial State from C# Backend
  async function loadInitialState() {
    try {
      const res = await callBridge('get_initial_state');
      if (res) {
        state.version = res.version || '';
        renderAboutVersion();
        state.lanIp = res.lanIp || '127.0.0.1';
        state.lanUrl = res.lanUrl || 'http://127.0.0.1:9095/';
        state.qrBase64 = res.qrBase64 || '';

        if (res.clis) {
          res.clis.forEach(c => {
            state.clis[c.type] = { state: c.state, port: c.port, pid: c.pid };
          });
        }
        if (res.antigravity) {
          state.antigravity.activeProfile = res.antigravity.activeProfile || '';
          state.antigravity.profiles = res.antigravity.profiles || [];
        }
        if (res.aiConfig) {
          state.aiConfig = res.aiConfig;
        }
        if (res.enabledClis && res.terminalWorkspacePreset) {
          renderTerminalPresetState(res.terminalWorkspacePreset, res.enabledClis);
        }
        if (res.opencode) {
          state.opencode = res.opencode;
          renderOpenCodeCard();
        }
        if (res.settings) {
          state.settings = res.settings;
          const autoCheck = document.getElementById('setting-autostart-checkbox');
          const minCheck = document.getElementById('setting-minimized-checkbox');
          const trayCheck = document.getElementById('setting-tray-minimize-checkbox');
          const notifCheck = document.getElementById('setting-notifications-checkbox');
          const hotkeyCheck = document.getElementById('setting-global-hotkey-checkbox');
          const hudCheck = document.getElementById('setting-global-hud-hotkey-checkbox');
          if (autoCheck) autoCheck.checked = !!res.settings.autostart;
          if (minCheck) minCheck.checked = !!res.settings.startMinimized;
          if (trayCheck) trayCheck.checked = res.settings.minimizeToTray !== false;
          if (notifCheck) notifCheck.checked = res.settings.notifications !== false;
          if (hotkeyCheck) hotkeyCheck.checked = res.settings.globalHotkey !== false;
          const winModSelect = document.getElementById('setting-window-modifier');
          if (winModSelect && res.settings.globalHotkeyModifier) winModSelect.value = res.settings.globalHotkeyModifier;
          const winKeySelect = document.getElementById('setting-window-key');
          if (winKeySelect && res.settings.globalHotkeyKey) winKeySelect.value = res.settings.globalHotkeyKey;
          if (typeof updateWindowHotkeyPreview === 'function') updateWindowHotkeyPreview();
          if (hudCheck) hudCheck.checked = res.settings.globalHudHotkey !== false;
          const hudModSelect = document.getElementById('setting-quake-modifier');
          if (hudModSelect && res.settings.hudHotkeyModifier) hudModSelect.value = res.settings.hudHotkeyModifier;
          const hudKeySelect = document.getElementById('setting-quake-key');
          if (hudKeySelect && res.settings.hudHotkeyKey) hudKeySelect.value = res.settings.hudHotkeyKey;
          const hudTargetSelect = document.getElementById('setting-quake-target');
          if (hudTargetSelect && res.settings.hudHotkeyTarget) hudTargetSelect.value = res.settings.hudHotkeyTarget;
          if (typeof updateHotkeyPreview === 'function') updateHotkeyPreview();
          state.isIpMasked = !!res.settings.maskPublicIp;
          state.isShortcutsMasked = res.settings.maskShortcutPaths !== undefined
            ? !!res.settings.maskShortcutPaths
            : localStorage.getItem('agents_hub_mask_shortcuts') === 'true';
          const maskPathsCheck = document.getElementById('setting-mask-paths-checkbox');
          if (maskPathsCheck) maskPathsCheck.checked = state.isShortcutsMasked;
          const embedShellsCheck = document.getElementById('setting-embed-shells-checkbox');
          if (embedShellsCheck) embedShellsCheck.checked = !!res.settings.embedShellsInTerminalWorkspace;
          const hideMainCheck = document.getElementById('setting-hide-main-on-cli-checkbox');
          if (hideMainCheck) hideMainCheck.checked = !!res.settings.hideMainOnCliOpen;

          // OpenCode Quota & IP Card preference (default false / disabled)
          const opencodeQuotaEnabled = res.settings.enableOpenCodeQuota === true;
          state.settings.enableOpenCodeQuota = opencodeQuotaEnabled;
          localStorage.setItem('agents_hub_enable_opencode_quota', opencodeQuotaEnabled);
          const prefOpencodeCheck = document.getElementById('pref-enable-opencode-quota');
          if (prefOpencodeCheck) prefOpencodeCheck.checked = opencodeQuotaEnabled;
          if (typeof updateOpenCodeQuotaVisibility === 'function') {
            updateOpenCodeQuotaVisibility();
          }

          // Proton VPN Provider preference (strictly dependent on OpenCode Quota)
          const protonEnabled = opencodeQuotaEnabled && (res.settings.enableProtonVpnProvider === true);
          state.settings.enableProtonVpnProvider = protonEnabled;
          localStorage.setItem('agents_hub_enable_proton', protonEnabled);
          const prefProtonCheck = document.getElementById('pref-enable-proton') || document.getElementById('setting-enable-proton-checkbox');
          if (prefProtonCheck) prefProtonCheck.checked = protonEnabled;
          if (typeof updateProtonVpnSettingState === 'function') {
            updateProtonVpnSettingState(opencodeQuotaEnabled);
          }

          // Hardware Monitor preference (default false / disabled)
          const hwEnabled = res.settings.enableHardwareMonitor === true;
          state.settings.enableHardwareMonitor = hwEnabled;
          localStorage.setItem('agents_hub_enable_hardware_monitor', hwEnabled);
          const prefHwCheck = document.getElementById('pref-enable-hardware-monitor') || document.getElementById('setting-enable-hardware-monitor-checkbox');
          if (prefHwCheck) prefHwCheck.checked = hwEnabled;
          updateHardwareMonitorVisibility();

          // Apply initial theme & accent color
          const activeTheme = localStorage.getItem('agents_hub_theme') || res.settings.theme || 'dark';
          const activeColor = localStorage.getItem('agents_hub_accent_color') || res.settings.accentColor || '#4049B9';
          applyAdaptiveTheme(activeTheme, activeColor);

          // The dashboard owns the theme; the C# side keeps its own copy in the registry and
          // ThemeManager seeds from it at startup. If the two disagree, push ours so the CLI
          // windows match this view instead of staying on a stale dark value.
          const appliedTheme = localStorage.getItem('agents_hub_theme') || 'light';
          const backendTheme = res.settings.theme === 'light' ? 'light' : 'dark';
          if (appliedTheme !== backendTheme) {
            callBridge('save_general_settings', { settings: { theme: appliedTheme } });
          }

          // Update VPN Provider UI conditionally
          updateVpnProviderUi();
        }

        // Fetch public IP initially (dual-stack IPv4 & IPv6)
        callBridge('get_public_ip').then(r => {
          if (r) {
            if (r.ipv4) state.ipv4 = r.ipv4;
            if (r.ipv6) state.ipv6 = r.ipv6;
            if (r.ip) state.publicIp = r.ip;
            if (!state.ipv4 && r.ip && !r.ip.includes(':')) state.ipv4 = r.ip;
            if (!state.ipv6 && r.ip && r.ip.includes(':')) state.ipv6 = r.ip;
            state.countryCode = r.countryCode || '';
            state.country = r.country || '';
            state.countryEmoji = r.countryEmoji || '';
            state.city = r.city || '';
            renderPublicIpDisplay();
          }
        }).catch(() => {});

        // Update URLs in UI
        const urlEl = document.getElementById('dash-lan-url');
        const lanTabUrl = document.getElementById('lan-full-url');
        if (urlEl) urlEl.textContent = state.lanUrl;
        if (lanTabUrl) lanTabUrl.textContent = state.lanUrl;

        // Render QR Code (Fix: Uses Base64 Image Generated by QrCodeHelper!)
        if (state.qrBase64) {
          const qrImg = document.getElementById('lan-qr-img');
          const qrFallback = document.getElementById('lan-qr-fallback');
          if (qrImg) {
            qrImg.src = state.qrBase64;
            qrImg.style.display = 'block';
          }
          if (qrFallback) qrFallback.style.display = 'none';
        }

        if (res.runningPorts) {
          state.runningPorts = res.runningPorts;
        }
        state.lanProxyPort = res.lanProxyPort || 9191;
        state.lanProxyEnabled = res.lanProxyEnabled !== undefined ? !!res.lanProxyEnabled : true;
        if (!state.lanProxyEnabled) {
          state.dashUrlMode = 'local';
          state.lanUrlMode = 'local';
        }

        if (res.workspaces) {
          state.workspaces = res.workspaces;
          state.activeWorkspaceId = res.activeWorkspaceId || (res.workspaces[0] ? res.workspaces[0].id : null);
          renderWorkspacesDropdown(state.workspaces, state.activeWorkspaceId);
        }
        if (res.tokenTelemetry) {
          state.tokenTelemetry = res.tokenTelemetry;
          renderTokenTelemetry(state.tokenTelemetry);
        }

        updateHeaderStatus();
        updateDashboardCliRows();
        renderDashboardShortcuts();
        renderPublicIpDisplay();
        renderQuickProfileSelect();
        renderProfiles();
        renderPromptModelsDropdown();
        renderPromptPresets();
        renderProvidersList();
        renderDevToolsList();
        renderRunningPorts();
      }
    } catch (err) {
      console.error('Failed to load initial state:', err);
    }
  }

  // Claude Code Gateway Engine Configuration Card
window.setupGlobalActions = setupGlobalActions;
window.loadInitialState = loadInitialState;
