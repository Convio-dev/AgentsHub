// Discord Rich Presence (RPC) Settings Module
(function () {
  'use strict';

  const OFFICIAL_APP_ID = '1562567704941174834';
  const OFFICIAL_LARGE_KEY = 'app';
  const OFFICIAL_SMALL_KEY = 'cli';

  let rpcStatus = null;
  let timerInterval = null;
  let isInitialized = false;

  function notifyToast(msg, type = 'info', duration = 3000) {
    if (typeof window.showToast === 'function') {
      window.showToast(msg, type, duration);
    } else if (typeof showToast === 'function') {
      showToast(msg, type, duration);
    } else {
      console.log(`[Toast ${type}]`, msg);
    }
  }

  async function initDiscordRpcSettings() {
    if (isInitialized) return;

    if (window.__htmlPartialsPromise) {
      try {
        await window.__htmlPartialsPromise;
      } catch (e) {
        console.warn('[DiscordRPC] Error awaiting HTML partials:', e);
      }
    }

    if (isInitialized) return;

    const chkEnabled = document.getElementById('chk-discord-rpc-enabled');
    if (!chkEnabled) {
      // Partials still rendering, retry shortly
      setTimeout(initDiscordRpcSettings, 100);
      return;
    }

    isInitialized = true;
    setupEventListeners();
    await loadDiscordRpcStatus();
    startPreviewTimer();

    // Listen to push events from backend
    window.addEventListener('discord_rpc_status_changed', (e) => {
      if (e.detail) {
        rpcStatus = e.detail;
        updateUI();
      }
    });

    // Re-check when navigating to settings
    window.addEventListener('view-switched', (e) => {
      const v = e.detail?.activeView || e.detail?.viewId;
      if (v === 'settings') {
        loadDiscordRpcStatus();
      }
    });

    window.addEventListener('settings-tab-switched', (e) => {
      if (e.detail?.activeTab === 'integrations') {
        loadDiscordRpcStatus();
      }
    });
  }

  function setupEventListeners() {
    const chkEnabled = document.getElementById('chk-discord-rpc-enabled');
    const btnRefresh = document.getElementById('btn-refresh-discord-rpc');
    const btnSave = document.getElementById('btn-save-discord-rpc');
    const btnReset = document.getElementById('btn-reset-discord-rpc');
    const selPrivacy = document.getElementById('select-discord-privacy');

    // Master toggle: Instant reaction
    if (chkEnabled) {
      chkEnabled.addEventListener('change', async () => {
        const isEnabled = chkEnabled.checked;
        try {
          notifyToast(isEnabled ? 'Connecting Discord Rich Presence...' : 'Disconnecting Discord Rich Presence...', 'info', 2000);
          const res = await callBridge('toggle_discord_rpc', { enabled: isEnabled });
          if (res && res.status) {
            rpcStatus = res.status;
            updateUI();
            notifyToast(isEnabled ? 'Discord Rich Presence enabled' : 'Discord Rich Presence disconnected and disabled', isEnabled ? 'success' : 'warn');
          }
        } catch (err) {
          notifyToast('Failed to toggle Discord RPC: ' + err.message, 'error');
        }
      });
    }

    // Refresh button
    if (btnRefresh) {
      btnRefresh.addEventListener('click', async () => {
        btnRefresh.disabled = true;
        try {
          notifyToast('Refreshing Discord RPC presence...', 'info', 1500);
          const res = await callBridge('test_discord_rpc');
          if (res) {
            rpcStatus = res;
            updateUI();
            if (res.isConnected) {
              notifyToast(`Connected to Discord via ${res.connectedPipe}`, 'success');
            } else {
              notifyToast('Discord client is not running or pipe unreachable', 'warn');
            }
          }
        } catch (err) {
          notifyToast('Refresh failed: ' + err.message, 'error');
        } finally {
          btnRefresh.disabled = false;
        }
      });
    }

    // Save button
    if (btnSave) {
      btnSave.addEventListener('click', () => saveSettingsFromUI(true));
    }

    // Reset Defaults button
    if (btnReset) {
      btnReset.addEventListener('click', () => {
        const inpAppId = document.getElementById('discord-rpc-app-id');
        const inpLarge = document.getElementById('discord-rpc-large-key');
        const inpSmall = document.getElementById('discord-rpc-small-key');
        const chkTime = document.getElementById('chk-discord-time');
        const chkParty = document.getElementById('chk-discord-party');
        const chkButtons = document.getElementById('chk-discord-buttons');

        if (inpAppId) inpAppId.value = OFFICIAL_APP_ID;
        if (selPrivacy) selPrivacy.value = 'default';
        if (inpLarge) inpLarge.value = OFFICIAL_LARGE_KEY;
        if (inpSmall) inpSmall.value = OFFICIAL_SMALL_KEY;
        if (chkTime) chkTime.checked = true;
        if (chkParty) chkParty.checked = true;
        if (chkButtons) chkButtons.checked = true;

        updateLivePreview();
        saveSettingsFromUI(false);
        notifyToast('Reset to Default (Coding with AgentsHub & Buttons)', 'info');
      });
    }

    // Privacy Preset dropdown: Instant preview + auto-save to live Discord presence
    if (selPrivacy) {
      selPrivacy.addEventListener('change', () => {
        updateLivePreview();
        saveSettingsFromUI(false);
        const selectedLabel = selPrivacy.options[selPrivacy.selectedIndex]?.text || selPrivacy.value;
        notifyToast(`Preset: ${selectedLabel}`, 'info', 2000);
      });
    }

    // Auto-save on toggle options
    ['chk-discord-time', 'chk-discord-party', 'chk-discord-buttons'].forEach(id => {
      const el = document.getElementById(id);
      if (el) {
        el.addEventListener('change', () => {
          updateLivePreview();
          saveSettingsFromUI(false);
        });
      }
    });
  }

  async function loadDiscordRpcStatus() {
    try {
      const res = await callBridge('get_discord_rpc_status');
      if (res) {
        rpcStatus = res;
        populateSettings(res.settings);
        updateUI();
      }
    } catch (err) {
      console.warn('[DiscordRPC] Failed to load status:', err);
    }
  }

  function populateSettings(s) {
    if (!s) return;
    const chkEnabled = document.getElementById('chk-discord-rpc-enabled');
    const inpAppId = document.getElementById('discord-rpc-app-id');
    const selPrivacy = document.getElementById('select-discord-privacy');
    const inpLarge = document.getElementById('discord-rpc-large-key');
    const inpSmall = document.getElementById('discord-rpc-small-key');
    const chkTime = document.getElementById('chk-discord-time');
    const chkParty = document.getElementById('chk-discord-party');
    const chkButtons = document.getElementById('chk-discord-buttons');

    if (chkEnabled) chkEnabled.checked = s.enabled !== false;
    if (inpAppId) inpAppId.value = OFFICIAL_APP_ID;
    if (selPrivacy) selPrivacy.value = s.privacyMode || 'default';
    if (inpLarge) inpLarge.value = OFFICIAL_LARGE_KEY;
    if (inpSmall) inpSmall.value = OFFICIAL_SMALL_KEY;
    if (chkTime) chkTime.checked = s.showElapsedTime !== false;
    if (chkParty) chkParty.checked = s.showPartySize !== false;
    if (chkButtons) chkButtons.checked = s.showButtons !== false;
  }

  async function saveSettingsFromUI(showToastNotice = true) {
    const chkEnabled = document.getElementById('chk-discord-rpc-enabled');
    const selPrivacy = document.getElementById('select-discord-privacy');
    const chkTime = document.getElementById('chk-discord-time');
    const chkParty = document.getElementById('chk-discord-party');
    const chkButtons = document.getElementById('chk-discord-buttons');

    const newSettings = {
      enabled: chkEnabled ? chkEnabled.checked : true,
      applicationId: OFFICIAL_APP_ID,
      privacyMode: selPrivacy?.value || 'default',
      largeImageKey: OFFICIAL_LARGE_KEY,
      largeImageText: 'AgentsHub - AI Agent Station',
      smallImageKey: OFFICIAL_SMALL_KEY,
      smallImageText: 'Terminal Active',
      showElapsedTime: chkTime ? chkTime.checked : true,
      showPartySize: chkParty ? chkParty.checked : true,
      showButtons: chkButtons ? chkButtons.checked : true,
      button1Label: 'Get AgentsHub',
      button1Url: 'https://github.com/ZerroDevs/AgentsHub'
    };

    try {
      const res = await callBridge('save_discord_rpc_settings', { settings: newSettings });
      if (res && res.success) {
        rpcStatus = res.status;
        updateUI();
        if (showToastNotice) {
          notifyToast('Discord Rich Presence settings saved!', 'success');
        }
      } else {
        if (showToastNotice) {
          notifyToast('Failed to save settings: ' + (res?.message || 'Error'), 'error');
        }
      }
    } catch (err) {
      if (showToastNotice) {
        notifyToast('Error saving Discord RPC: ' + err.message, 'error');
      }
    }
  }

  function updateUI() {
    if (!rpcStatus) return;

    // Keep master toggle switch synced
    const chkEnabled = document.getElementById('chk-discord-rpc-enabled');
    if (chkEnabled && rpcStatus.settings) {
      chkEnabled.checked = rpcStatus.settings.enabled !== false;
    }

    // Status Pill
    const pill = document.getElementById('discord-rpc-status-pill');
    if (pill) {
      pill.className = 'discord-status-badge';
      if (!rpcStatus.settings?.enabled) {
        pill.classList.add('disabled');
        pill.innerHTML = `<span style="font-size: 11px;">●</span> <span>Disabled</span>`;
      } else if (rpcStatus.isConnected) {
        pill.classList.add('connected');
        pill.innerHTML = `<span style="font-size: 11px;">●</span> <span>Connected (${escapeHtml(rpcStatus.connectedPipe || 'IPC')})</span>`;
      } else {
        pill.classList.add('searching');
        pill.innerHTML = `<span class="spinner" style="width: 10px; height: 10px; border-width: 1.5px;"></span> <span>Searching for Discord...</span>`;
      }
    }

    // User details in simulator
    const simDisplayName = document.getElementById('discord-sim-display-name');
    const simUsername = document.getElementById('discord-sim-username');
    const simAvatarImg = document.getElementById('discord-sim-avatar-img');
    const simAvatarSvg = document.getElementById('discord-sim-avatar-svg');

    if (rpcStatus.discordUsername) {
      if (simDisplayName) simDisplayName.textContent = rpcStatus.discordUsername;
      if (simUsername) simUsername.textContent = '@' + rpcStatus.discordUsername;
    }

    if (rpcStatus.discordUserId && rpcStatus.discordAvatar) {
      if (simAvatarImg) {
        simAvatarImg.src = `https://cdn.discordapp.com/avatars/${rpcStatus.discordUserId}/${rpcStatus.discordAvatar}.png?size=128`;
        simAvatarImg.style.display = 'block';
        if (simAvatarSvg) simAvatarSvg.style.display = 'none';
      }
    }

    updateLivePreview();
  }

  function updateLivePreview() {
    const selPrivacy = document.getElementById('select-discord-privacy');
    const chkButtons = document.getElementById('chk-discord-buttons');
    const chkTime = document.getElementById('chk-discord-time');

    const privacy = selPrivacy?.value || 'default';
    const showBtns = chkButtons ? chkButtons.checked : true;
    const showTime = chkTime ? chkTime.checked : true;

    const simDetails = document.getElementById('discord-sim-details');
    const simState = document.getElementById('discord-sim-state');
    const simTime = document.getElementById('discord-sim-time');
    const simButton = document.getElementById('discord-sim-button');

    // Details & State simulation based on privacy mode
    if (privacy === 'default') {
      if (simDetails) simDetails.textContent = 'Coding with AgentsHub';
      if (simState) simState.textContent = '⚡ AI Agent Station';
      if (simButton) simButton.style.display = showBtns ? 'block' : 'none';
    } else if (privacy === 'stealth') {
      if (simDetails) simDetails.textContent = 'Developing in Private Codebase';
      if (simState) simState.textContent = '🤖 Coding with AI Agent';
      if (simButton) simButton.style.display = 'none';
    } else if (privacy === 'project_only') {
      if (simDetails) simDetails.textContent = rpcStatus?.currentDetails ? rpcStatus.currentDetails.split(' [')[0] : 'Working on AgentsHub';
      if (simState) simState.textContent = '⚡ Workspace Ready';
      if (simButton) simButton.style.display = showBtns ? 'block' : 'none';
    } else {
      // Transparent
      if (simDetails) simDetails.textContent = rpcStatus?.currentDetails || 'Working on: AgentsHub [main]';
      if (simState) simState.textContent = rpcStatus?.currentState || '🤖 Claude Code (1 Agent Active)';
      if (simButton) simButton.style.display = showBtns ? 'block' : 'none';
    }

    if (simTime) {
      simTime.style.display = showTime ? 'block' : 'none';
    }
  }

  function startPreviewTimer() {
    if (timerInterval) clearInterval(timerInterval);
    timerInterval = setInterval(() => {
      const simTime = document.getElementById('discord-sim-time');
      if (!simTime) return;

      const startTime = rpcStatus?.sessionStartTime ? rpcStatus.sessionStartTime * 1000 : Date.now();
      const elapsedSec = Math.max(0, Math.floor((Date.now() - startTime) / 1000));
      const hrs = String(Math.floor(elapsedSec / 3600)).padStart(2, '0');
      const mins = String(Math.floor((elapsedSec % 3600) / 60)).padStart(2, '0');
      const secs = String(elapsedSec % 60).padStart(2, '0');
      simTime.textContent = `${hrs}:${mins}:${secs} elapsed`;
    }, 1000);
  }

  // Export to global scope
  window.setupDiscordRpcSettings = initDiscordRpcSettings;

  // Run automatically when script loads or partials finish
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initDiscordRpcSettings);
  } else {
    initDiscordRpcSettings();
  }
})();
