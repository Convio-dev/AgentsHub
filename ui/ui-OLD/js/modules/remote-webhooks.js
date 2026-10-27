  function setupRemoteWebhooks() {
    const chkEnabled = document.getElementById('chk-webhook-enabled');
    const inpUrl = document.getElementById('webhook-url-input');
    const inpMention = document.getElementById('webhook-mention-input');
    const chkDone = document.getElementById('chk-hook-notify-done');
    const chkError = document.getElementById('chk-hook-notify-error');
    const chkApproval = document.getElementById('chk-hook-notify-approval');
    const btnSave = document.getElementById('btn-save-webhook-config');
    const btnTest = document.getElementById('btn-test-remote-webhook');

    async function loadConfig() {
      try {
        const cfg = await callBridge('get_remote_webhook_config');
        if (cfg) {
          if (chkEnabled) chkEnabled.checked = cfg.isEnabled ?? false;
          if (inpUrl) inpUrl.value = cfg.discordWebhookUrl || '';
          if (inpMention) inpMention.value = cfg.mentionRoleId || '';
          if (chkDone) chkDone.checked = cfg.notifyOnTaskDone ?? true;
          if (chkError) chkError.checked = cfg.notifyOnError ?? true;
          if (chkApproval) chkApproval.checked = cfg.notifyOnApproval ?? true;
        }
      } catch (err) {
        console.error('Failed to load webhook config:', err);
      }
    }

    btnSave?.addEventListener('click', async () => {
      const config = {
        isEnabled: chkEnabled?.checked ?? false,
        discordWebhookUrl: inpUrl?.value?.trim() || '',
        mentionRoleId: inpMention?.value?.trim() || '',
        notifyOnTaskDone: chkDone?.checked ?? true,
        notifyOnError: chkError?.checked ?? true,
        notifyOnApproval: chkApproval?.checked ?? true
      };

      try {
        const res = await callBridge('save_remote_webhook_config', { config });
        if (res?.success) {
          showToast('Discord webhook configuration saved', 'success');
        }
      } catch (err) {
        showToast(`Save error: ${err.message}`, 'error');
      }
    });

    btnTest?.addEventListener('click', async () => {
      showToast('Dispatching test webhook embed...', 'info');
      try {
        const res = await callBridge('test_remote_webhook', {
          title: 'Agents Hub Test Ping',
          description: 'Webhook dispatched successfully from Agents Hub.'
        });
        if (res?.success) {
          showToast('Discord webhook received successfully!', 'success');
        } else {
          showToast('Failed to send webhook. Check URL configuration.', 'error');
        }
      } catch (err) {
        showToast(`Webhook error: ${err.message}`, 'error');
      }
    });

    loadConfig();
    setupPocketCommander();
  }

  function setupPocketCommander() {
    const chkEnabled = document.getElementById('chk-pocket-enabled');
    const statusPill = document.getElementById('pocket-status-pill');
    const statusText = document.getElementById('pocket-status-text');

    // Platform switcher tabs
    const btnTabTelegram = document.getElementById('btn-pocket-tab-telegram');
    const btnTabDiscord = document.getElementById('btn-pocket-tab-discord');
    const btnTabBoth = document.getElementById('btn-pocket-tab-both');
    const secTelegram = document.getElementById('pocket-section-telegram');
    const secDiscord = document.getElementById('pocket-section-discord');

    // Telegram elements
    const inpToken = document.getElementById('pocket-bot-token-input');
    const pinDisplay = document.getElementById('pocket-pairing-pin-display');
    const btnGenPin = document.getElementById('btn-gen-pocket-pin');
    const btnUnpair = document.getElementById('btn-unpair-pocket');
    const pairedBadge = document.getElementById('pocket-paired-device-badge');

    // Discord elements
    const inpDiscordToken = document.getElementById('pocket-discord-token-input');
    const pinDiscordDisplay = document.getElementById('pocket-discord-pin-display');
    const btnGenDiscordPin = document.getElementById('btn-gen-pocket-discord-pin');
    const btnUnpairDiscord = document.getElementById('btn-unpair-pocket-discord');
    const pairedDiscordBadge = document.getElementById('pocket-paired-discord-badge');

    // Permissions & Filters
    const chkAllowRun = document.getElementById('chk-pocket-allow-run');
    const chkAllowActive = document.getElementById('chk-pocket-allow-active');
    const chkAllowPrompts = document.getElementById('chk-pocket-allow-prompts');
    const chkAllowScreen = document.getElementById('chk-pocket-allow-screen');
    const chkAllowDiff = document.getElementById('chk-pocket-allow-diff');
    const chkAllowCancel = document.getElementById('chk-pocket-allow-cancel');

    const chkNotifyDone = document.getElementById('chk-pocket-notify-done');
    const chkNotifyError = document.getElementById('chk-pocket-notify-error');
    const chkNotifyApproval = document.getElementById('chk-pocket-notify-approval');

    const btnSave = document.getElementById('btn-save-pocket-config');
    const btnTest = document.getElementById('btn-test-pocket-bot');

    function syncToggleBadge(chk, labelId) {
      const lbl = document.getElementById(labelId);
      if (!lbl || !chk) return;
      const isOn = chk.checked;
      lbl.textContent = isOn ? 'ON' : 'OFF';
      lbl.className = `bot-feature-status-label ${isOn ? 'on' : 'off'}`;
    }

    function syncAllToggleBadges() {
      syncToggleBadge(chkEnabled, 'lbl-status-pocket-master');
      syncToggleBadge(chkAllowRun, 'lbl-status-allow-run');
      syncToggleBadge(chkAllowActive, 'lbl-status-allow-active');
      syncToggleBadge(chkAllowPrompts, 'lbl-status-allow-prompts');
      syncToggleBadge(chkAllowScreen, 'lbl-status-allow-screen');
      syncToggleBadge(chkAllowDiff, 'lbl-status-allow-diff');
      syncToggleBadge(chkAllowCancel, 'lbl-status-allow-cancel');
      syncToggleBadge(chkNotifyDone, 'lbl-status-notify-done');
      syncToggleBadge(chkNotifyError, 'lbl-status-notify-error');
      syncToggleBadge(chkNotifyApproval, 'lbl-status-notify-approval');
    }

    let activePlatform = 'telegram';

    function setPlatformTab(platform) {
      activePlatform = platform;
      const allTabs = [btnTabTelegram, btnTabDiscord, btnTabBoth];
      allTabs.forEach(b => {
        if (b) {
          b.style.background = '';
          b.style.borderColor = 'var(--border-subtle)';
          b.style.color = 'var(--text-muted)';
        }
      });

      if (platform === 'telegram') {
        if (btnTabTelegram) {
          btnTabTelegram.style.background = 'rgba(0, 136, 204, 0.15)';
          btnTabTelegram.style.borderColor = '#0088cc';
          btnTabTelegram.style.color = 'var(--text-main)';
        }
        if (secTelegram) secTelegram.style.display = 'block';
        if (secDiscord) secDiscord.style.display = 'none';
      } else if (platform === 'discord') {
        if (btnTabDiscord) {
          btnTabDiscord.style.background = 'rgba(88, 101, 242, 0.15)';
          btnTabDiscord.style.borderColor = '#5865f2';
          btnTabDiscord.style.color = 'var(--text-main)';
        }
        if (secTelegram) secTelegram.style.display = 'none';
        if (secDiscord) secDiscord.style.display = 'block';
      } else {
        if (btnTabBoth) {
          btnTabBoth.style.background = 'rgba(16, 185, 129, 0.15)';
          btnTabBoth.style.borderColor = 'var(--accent-green)';
          btnTabBoth.style.color = 'var(--text-main)';
        }
        if (secTelegram) secTelegram.style.display = 'block';
        if (secDiscord) secDiscord.style.display = 'block';
      }
    }

    btnTabTelegram?.addEventListener('click', () => setPlatformTab('telegram'));
    btnTabDiscord?.addEventListener('click', () => setPlatformTab('discord'));
    btnTabBoth?.addEventListener('click', () => setPlatformTab('both'));

    async function loadPocketConfig() {
      try {
        const cfg = await callBridge('get_pocket_commander_config');
        if (cfg) {
          if (chkEnabled) chkEnabled.checked = cfg.isEnabled ?? false;
          const defaultPlatform = cfg.platform || (cfg.discordBotToken && !cfg.telegramBotToken ? 'discord' : 'telegram');
          setPlatformTab(defaultPlatform);

          if (inpToken) inpToken.value = cfg.telegramBotToken || '';
          if (pinDisplay) pinDisplay.textContent = cfg.pairingPin || '---';

          if (inpDiscordToken) inpDiscordToken.value = cfg.discordBotToken || '';
          if (pinDiscordDisplay) pinDiscordDisplay.textContent = cfg.discordPairingPin || '---';

          if (chkAllowRun) chkAllowRun.checked = cfg.allowRun ?? true;
          if (chkAllowActive) chkAllowActive.checked = cfg.allowActive ?? true;
          if (chkAllowPrompts) chkAllowPrompts.checked = cfg.allowPrompts ?? true;
          if (chkAllowScreen) chkAllowScreen.checked = cfg.allowScreenshots ?? true;
          if (chkAllowDiff) chkAllowDiff.checked = cfg.allowGitDiff ?? true;
          if (chkAllowCancel) chkAllowCancel.checked = cfg.allowCancel ?? true;

          if (chkNotifyDone) chkNotifyDone.checked = cfg.notifyOnTaskDone ?? true;
          if (chkNotifyError) chkNotifyError.checked = cfg.notifyOnError ?? true;
          if (chkNotifyApproval) chkNotifyApproval.checked = cfg.notifyOnApproval ?? true;

          syncAllToggleBadges();
          updatePairingStatus(cfg);
        }
      } catch (err) {
        console.error('Failed to load pocket commander config:', err);
      }
    }

    function updatePairingStatus(cfg) {
      const isTgPaired = cfg && cfg.authorizedChatId && cfg.authorizedChatId !== 0;
      if (pairedBadge) {
        pairedBadge.style.display = isTgPaired ? 'inline-flex' : 'none';
        if (isTgPaired) pairedBadge.textContent = `Paired: ${cfg.authorizedUsername || 'ID: ' + cfg.authorizedChatId}`;
      }
      if (btnUnpair) btnUnpair.style.display = isTgPaired ? 'inline-block' : 'none';

      const isDcPaired = cfg && cfg.discordAuthorizedUserId && cfg.discordAuthorizedUserId !== '';
      if (pairedDiscordBadge) {
        pairedDiscordBadge.style.display = isDcPaired ? 'inline-flex' : 'none';
        if (isDcPaired) pairedDiscordBadge.textContent = `Paired: ${cfg.discordAuthorizedUsername || 'ID: ' + cfg.discordAuthorizedUserId}`;
      }
      if (btnUnpairDiscord) btnUnpairDiscord.style.display = isDcPaired ? 'inline-block' : 'none';

      if (statusText && statusPill) {
        if (!cfg.isEnabled) {
          statusText.textContent = 'Offline (Disabled)';
          statusPill.className = 'discord-status-badge offline';
          statusPill.style.background = 'rgba(100, 116, 139, 0.15)';
          statusPill.style.color = 'var(--text-dim)';
        } else if (isTgPaired || isDcPaired) {
          const names = [];
          if (isTgPaired) names.push('Telegram');
          if (isDcPaired) names.push('Discord');
          statusText.textContent = `Online (${names.join(' + ')})`;
          statusPill.className = 'discord-status-badge online';
          statusPill.style.background = 'rgba(16, 185, 129, 0.15)';
          statusPill.style.color = 'var(--accent-green)';
        } else {
          statusText.textContent = 'Listening (Unpaired)';
          statusPill.className = 'discord-status-badge searching';
          statusPill.style.background = 'rgba(59, 130, 246, 0.15)';
          statusPill.style.color = '#3b82f6';
        }
      }
    }

    // Attach immediate live status badge update and auto-save on switch flip
    const allFeatureToggles = [
      chkEnabled,
      chkAllowRun,
      chkAllowActive,
      chkAllowPrompts,
      chkAllowScreen,
      chkAllowDiff,
      chkAllowCancel,
      chkNotifyDone,
      chkNotifyError,
      chkNotifyApproval
    ];

    allFeatureToggles.forEach(t => {
      t?.addEventListener('change', () => {
        syncAllToggleBadges();
        btnSave?.click();
      });
    });

    btnSave?.addEventListener('click', async () => {
      const isEnabled = chkEnabled?.checked ?? false;
      syncAllToggleBadges();

      const config = {
        isEnabled,
        platform: activePlatform,
        telegramBotToken: inpToken?.value?.trim() || '',
        discordBotToken: inpDiscordToken?.value?.trim() || '',
        allowRun: chkAllowRun?.checked ?? true,
        allowActive: chkAllowActive?.checked ?? true,
        allowPrompts: chkAllowPrompts?.checked ?? true,
        allowScreenshots: chkAllowScreen?.checked ?? true,
        allowGitDiff: chkAllowDiff?.checked ?? true,
        allowCancel: chkAllowCancel?.checked ?? true,
        notifyOnTaskDone: chkNotifyDone?.checked ?? true,
        notifyOnError: chkNotifyError?.checked ?? true,
        notifyOnApproval: chkNotifyApproval?.checked ?? true
      };

      try {
        const res = await callBridge('save_pocket_commander_config', { config });
        if (res?.success) {
          showToast(isEnabled ? 'Pocket Commander enabled & running!' : 'Pocket Commander disabled (Bots Offline)', isEnabled ? 'success' : 'info');
          if (res.config) updatePairingStatus(res.config);
        }
      } catch (err) {
        showToast(`Save error: ${err.message}`, 'error');
      }
    });

    btnTest?.addEventListener('click', async () => {
      if (activePlatform === 'discord' || activePlatform === 'both') {
        const dToken = inpDiscordToken?.value?.trim() || '';
        if (dToken) {
          showToast('Testing Discord Bot Token connection...', 'info');
          try {
            const res = await callBridge('test_pocket_commander_token', { token: dToken, platform: 'discord' });
            if (res?.success) {
              showToast(`Connected to Discord Bot: @${res.username}!`, 'success');
            } else {
              showToast(`Discord Bot error: ${res.error || 'Invalid token'}`, 'error');
            }
          } catch (e) {
            showToast(`Discord test error: ${e.message}`, 'error');
          }
        }
      }

      if (activePlatform === 'telegram' || activePlatform === 'both') {
        const tToken = inpToken?.value?.trim() || '';
        if (tToken) {
          showToast('Testing Telegram Bot Token connection...', 'info');
          try {
            const res = await callBridge('test_pocket_commander_token', { token: tToken, platform: 'telegram' });
            if (res?.success) {
              showToast(`Connected to Telegram Bot: @${res.username}!`, 'success');
            } else {
              showToast(`Telegram Bot error: ${res.error || 'Invalid token'}`, 'error');
            }
          } catch (e) {
            showToast(`Telegram test error: ${e.message}`, 'error');
          }
        }
      }

      if (activePlatform === 'telegram' && !inpToken?.value?.trim()) {
        showToast('Please enter a Telegram Bot Token first.', 'info');
      } else if (activePlatform === 'discord' && !inpDiscordToken?.value?.trim()) {
        showToast('Please enter a Discord Bot Token first.', 'info');
      }
    });

    btnGenPin?.addEventListener('click', async () => {
      try {
        const res = await callBridge('generate_pocket_pairing_pin', { platform: 'telegram' });
        if (res?.pin) {
          if (pinDisplay) pinDisplay.textContent = res.pin;
          showToast(`Fresh Telegram Pairing PIN: ${res.pin}`, 'info');
        }
      } catch (err) {
        showToast(`Error: ${err.message}`, 'error');
      }
    });

    btnGenDiscordPin?.addEventListener('click', async () => {
      try {
        const res = await callBridge('generate_pocket_pairing_pin', { platform: 'discord' });
        if (res?.pin) {
          if (pinDiscordDisplay) pinDiscordDisplay.textContent = res.pin;
          showToast(`Fresh Discord Pairing PIN: ${res.pin}`, 'info');
        }
      } catch (err) {
        showToast(`Error: ${err.message}`, 'error');
      }
    });

    // Hover-to-copy handler for pairing PINs
    function wirePinCopy(displayElem, platformName) {
      if (!displayElem) return;
      displayElem.addEventListener('click', async () => {
        const text = displayElem.textContent?.trim() || '';
        if (!text || text === '---') return;

        try {
          if (navigator.clipboard && navigator.clipboard.writeText) {
            await navigator.clipboard.writeText(text);
          } else {
            const ta = document.createElement('textarea');
            ta.value = text;
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            document.body.removeChild(ta);
          }

          displayElem.classList.add('copied');
          showToast(`Copied ${platformName} PIN: ${text}`, 'success');

          setTimeout(() => {
            displayElem.classList.remove('copied');
          }, 1600);
        } catch (e) {
          showToast(`Failed to copy PIN: ${e.message}`, 'error');
        }
      });
    }

    wirePinCopy(pinDisplay, 'Telegram');
    wirePinCopy(pinDiscordDisplay, 'Discord');

    btnUnpair?.addEventListener('click', async () => {
      try {
        const res = await callBridge('unpair_pocket_commander', { platform: 'telegram' });
        if (res?.success) {
          showToast('Telegram phone unpaired.', 'info');
          if (res.config) {
            if (pinDisplay) pinDisplay.textContent = res.config.pairingPin || '---';
            updatePairingStatus(res.config);
          }
        }
      } catch (err) {
        showToast(`Unpair error: ${err.message}`, 'error');
      }
    });

    btnUnpairDiscord?.addEventListener('click', async () => {
      try {
        const res = await callBridge('unpair_pocket_commander', { platform: 'discord' });
        if (res?.success) {
          showToast('Discord user unpaired.', 'info');
          if (res.config) {
            if (pinDiscordDisplay) pinDiscordDisplay.textContent = res.config.discordPairingPin || '---';
            updatePairingStatus(res.config);
          }
        }
      } catch (err) {
        showToast(`Unpair error: ${err.message}`, 'error');
      }
    });

    // Real-time IPC notifications from background poller
    window.addEventListener('pocket_commander_command_received', (e) => {
      const detail = e.detail || {};
      showToast(`Remote command: ${detail.cmd} (${detail.user || 'User'})`, 'info');
    });

    window.addEventListener('pocket_commander_status_changed', (e) => {
      loadPocketConfig();
    });

    loadPocketConfig();
  }

  // Feature 8: Proof-of-Work Timelapse & GIF Recorder
window.setupRemoteWebhooks = setupRemoteWebhooks;
