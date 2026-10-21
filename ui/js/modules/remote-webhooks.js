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
  }

  // Feature 8: Proof-of-Work Timelapse & GIF Recorder
window.setupRemoteWebhooks = setupRemoteWebhooks;
