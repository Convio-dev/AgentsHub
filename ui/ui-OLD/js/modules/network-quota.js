  function updateOpenCodeQuotaVisibility() {
    const isEnabled = state.settings.enableOpenCodeQuota === true || localStorage.getItem('agents_hub_enable_opencode_quota') === 'true';
    const card = document.getElementById('card-opencode-quota');
    if (card) {
      card.style.display = isEnabled ? 'block' : 'none';
    }
    if (typeof window.updateDashboardGridClasses === 'function') {
      window.updateDashboardGridClasses();
    }
  }

  function updateVpnProviderUi() {
    const isOpencodeEnabled = state.settings.enableOpenCodeQuota === true || localStorage.getItem('agents_hub_enable_opencode_quota') === 'true';
    const isProtonEnabled = isOpencodeEnabled && (state.settings.enableProtonVpnProvider === true || localStorage.getItem('agents_hub_enable_proton') === 'true');
    const providerTabs = document.getElementById('provider-tabs') || document.getElementById('vpn-provider-switcher');

    if (providerTabs) {
      providerTabs.style.display = isProtonEnabled ? 'flex' : 'none';
    }

    // Default Fallback & Safety: If Proton is disabled, strictly force active provider to WARP
    if (!isProtonEnabled && state.vpnProvider === 'proton') {
      state.vpnProvider = 'warp';
      localStorage.setItem('agents_hub_vpn_provider', 'warp');
    }

    const isWarp = state.vpnProvider === 'warp';
    const btnWarp = document.getElementById('btn-provider-warp');
    const btnProton = document.getElementById('btn-provider-proton');
    const lblReset = document.getElementById('lbl-reset-quota-btn');
    const providerBadge = document.getElementById('dash-provider-badge');
    const providerText = document.getElementById('dash-provider-text');

    if (btnWarp) {
      btnWarp.classList.toggle('active', isWarp);
      btnWarp.setAttribute('aria-selected', isWarp ? 'true' : 'false');
    }
    if (btnProton) {
      btnProton.classList.toggle('active', !isWarp);
      btnProton.setAttribute('aria-selected', !isWarp ? 'true' : 'false');
    }

    if (lblReset && !quotaCooldownInterval) {
      lblReset.textContent = isWarp ? 'Reset OpenCode Quota (WARP IP)' : 'Rotate Proton VPN Server & IP';
    }

    if (providerBadge && providerText) {
      providerBadge.className = 'ip-pill-badge ' + (state.isDirectIsp ? 'is-direct' : (isWarp ? 'is-warp' : 'is-proton'));
      providerText.textContent = state.isDirectIsp ? 'Active: Direct ISP' : (isWarp ? 'Active: Cloudflare WARP' : 'Active: Proton VPN');
    }
  }

  function startQuotaResetCooldown(durationSeconds = 12) {
    if (quotaCooldownInterval) {
      clearInterval(quotaCooldownInterval);
      quotaCooldownInterval = null;
    }

    const btn = document.getElementById('btn-reset-opencode-quota');
    const lbl = document.getElementById('lbl-reset-quota-btn');
    const icon = document.getElementById('icon-reset-quota');

    if (!btn) return;

    let remaining = durationSeconds;
    btn.disabled = true;
    btn.classList.add('btn-cooldown');
    if (icon) icon.classList.remove('spinning');
    if (lbl) lbl.textContent = `Cooldown (${remaining}s)...`;

    quotaCooldownInterval = setInterval(() => {
      remaining -= 1;
      if (remaining > 0) {
        if (lbl) lbl.textContent = `Cooldown (${remaining}s)...`;
      } else {
        clearInterval(quotaCooldownInterval);
        quotaCooldownInterval = null;
        btn.disabled = false;
        btn.classList.remove('btn-cooldown');
        updateVpnProviderUi();
      }
    }, 1000);
  }

  function escapeHtml(str) {
    if (!str) return '';
    return String(str)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }

  function renderPublicIpDisplay() {
    const v4Diff = document.getElementById('dash-ipv4-diff');
    const v6Diff = document.getElementById('dash-ipv6-diff');
    const legacyIpText = document.getElementById('dash-public-ip-text');
    const eyeIcon = document.getElementById('icon-ip-eye');
    const geoFlag = document.getElementById('dash-geo-flag');
    const geoText = document.getElementById('dash-geo-text');

    if (state.isIpMasked) {
      if (v4Diff) {
        v4Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">***.***.***</span>';
      }
      if (v6Diff) {
        v6Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">****:****:****</span>';
      }
      if (legacyIpText) {
        legacyIpText.textContent = 'IP: ***';
      }
      if (geoFlag) {
        geoFlag.style.display = 'none';
      }
      if (geoText) geoText.textContent = 'Hidden (Masked)';
      if (eyeIcon) {
        eyeIcon.innerHTML = '<path d="m2 2 20 20"/><path d="M9.88 9.88a3 3 0 1 0 4.24 4.24"/><path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68"/><path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61"/>';
      }
      updateVpnProviderUi();
      return;
    }

    if (eyeIcon) {
      eyeIcon.innerHTML = '<path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>';
    }

    const currentV4 = state.ipv4 || (state.publicIp && !state.publicIp.includes(':') ? state.publicIp : 'Fetching...');
    const currentV6 = state.ipv6 || (state.publicIp && state.publicIp.includes(':') ? state.publicIp : 'Not assigned');

    // IPv4 Row: Diff display if rotated
    if (v4Diff) {
      if (state.previousIpv4 && state.previousIpv4 !== 'unknown' && state.previousIpv4 !== currentV4 && currentV4 !== 'Fetching...') {
        v4Diff.innerHTML = `<span class="ip-old">${escapeHtml(state.previousIpv4)}</span> <span class="ip-arrow">➜</span> <span class="ip-val-curr font-mono">${escapeHtml(currentV4)}</span>`;
      } else {
        v4Diff.innerHTML = `<span class="ip-val-curr font-mono">${escapeHtml(currentV4)}</span>`;
      }
    }

    // IPv6 Row: Diff display if rotated
    if (v6Diff) {
      if (state.previousIpv6 && state.previousIpv6 !== 'Not assigned' && state.previousIpv6 !== currentV6 && currentV6 !== 'Not assigned') {
        v6Diff.innerHTML = `<span class="ip-old">${escapeHtml(state.previousIpv6)}</span> <span class="ip-arrow">➜</span> <span class="ip-val-curr font-mono">${escapeHtml(currentV6)}</span>`;
      } else {
        v6Diff.innerHTML = `<span class="ip-val-curr font-mono ${currentV6 === 'Not assigned' ? 'text-muted' : ''}">${escapeHtml(currentV6)}</span>`;
      }
    }

    // Geolocation Meta Row
    if (geoFlag) {
      if (state.countryCode) {
        geoFlag.textContent = state.countryCode.toUpperCase();
        geoFlag.style.display = 'inline-block';
      } else {
        geoFlag.style.display = 'none';
      }
    }
    if (geoText) {
      const locStr = state.city
        ? (state.country ? `${state.country} (${state.city})` : state.city)
        : (state.country || 'Detecting location...');
      geoText.textContent = locStr;
    }

    if (legacyIpText) {
      const flag = state.countryCode ? `[${state.countryCode.toUpperCase()}] ` : '';
      const locDetail = state.city ? ` (${state.city})` : (state.country ? ` (${state.country})` : '');
      legacyIpText.textContent = `IP: ${flag}${currentV4}${locDetail}`;
    }

    updateVpnProviderUi();
  }

  function formatRelativeTime(isoString) {
    if (!isoString) return 'Never';
    const timestamp = new Date(isoString).getTime();
    if (isNaN(timestamp)) return 'Never';
    const diffSec = Math.max(0, Math.floor((Date.now() - timestamp) / 1000));
    if (diffSec < 45) return 'Just now';
    if (diffSec < 3600) return `${Math.floor(diffSec / 60)}m ago`;
    if (diffSec < 86400) return `${Math.floor(diffSec / 3600)}h ago`;
    return `${Math.floor(diffSec / 86400)}d ago`;
  }

  function updateQuotaStatsDisplay() {
    if (state.activeView && state.activeView !== 'dashboard') return;
    try {
      const statsEl = document.getElementById('quota-reset-stats');
      if (!statsEl) return;

      const todayStr = new Date().toISOString().slice(0, 10);
      const savedDate = localStorage.getItem('agents_hub_rotations_date');
      let count = parseInt(localStorage.getItem('agents_hub_rotations_today') || '0', 10);

      if (savedDate !== todayStr) {
        count = 0;
        localStorage.setItem('agents_hub_rotations_today', '0');
        localStorage.setItem('agents_hub_rotations_date', todayStr);
      }

      const lastRotated = localStorage.getItem('agents_hub_last_rotated') || '';
      const relTime = formatRelativeTime(lastRotated);
      const lblStats = document.getElementById('lbl-quota-stats-text');
      if (lblStats) {
        lblStats.textContent = `Last rotated: ${relTime} • Rotations today: ${count}`;
      } else {
        statsEl.textContent = `Last rotated: ${relTime} • Rotations today: ${count}`;
      }
    } catch (err) {
      console.warn('updateQuotaStatsDisplay error:', err);
    }
  }

  function recordSuccessfulRotation() {
    const nowIso = new Date().toISOString();
    const todayStr = nowIso.slice(0, 10);
    const savedDate = localStorage.getItem('agents_hub_rotations_date');
    let count = parseInt(localStorage.getItem('agents_hub_rotations_today') || '0', 10);

    if (savedDate !== todayStr) {
      count = 1;
    } else {
      count += 1;
    }

    localStorage.setItem('agents_hub_last_rotated', nowIso);
    localStorage.setItem('agents_hub_rotations_today', String(count));
    localStorage.setItem('agents_hub_rotations_date', todayStr);
    updateQuotaStatsDisplay();
  }

  function setupQuotaAndIpWidget() {
    try {
      updateOpenCodeQuotaVisibility();
      updateQuotaStatsDisplay();
      updateVpnProviderUi();
      renderPublicIpDisplay();
      setInterval(updateQuotaStatsDisplay, 15000);
    } catch (err) {
      console.warn('setupQuotaAndIpWidget error:', err);
    }

    // Trigger initial public IP fetch immediately
    if (!state.ipv4 && !state.publicIp) {
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
    }

    // Segmented VPN Provider Controls
    document.getElementById('btn-provider-warp')?.addEventListener('click', () => {
      state.vpnProvider = 'warp';
      state.isDirectIsp = false;
      localStorage.setItem('agents_hub_vpn_provider', 'warp');
      updateVpnProviderUi();
    });

    document.getElementById('btn-provider-proton')?.addEventListener('click', async () => {
      const isOpencodeEnabled = state.settings.enableOpenCodeQuota === true || localStorage.getItem('agents_hub_enable_opencode_quota') === 'true';
      const isProtonEnabled = isOpencodeEnabled && (state.settings.enableProtonVpnProvider === true || localStorage.getItem('agents_hub_enable_proton') === 'true');
      if (!isProtonEnabled) return;

      state.vpnProvider = 'proton';
      state.isDirectIsp = false;
      localStorage.setItem('agents_hub_vpn_provider', 'proton');
      updateVpnProviderUi();

      try {
        const res = await callBridge('ensure_proton_vpn_running');
        if (res && res.launched) {
          showToast('Opening Proton VPN application...', 'info', 3500);
          appendLog('[ProtonVPN] Launching Proton VPN GUI');
        }
      } catch { }
    });

    document.getElementById('btn-toggle-mask-ip')?.addEventListener('click', async () => {
      state.isIpMasked = !state.isIpMasked;
      renderPublicIpDisplay();
      await callBridge('save_general_settings', {
        settings: { maskPublicIp: state.isIpMasked }
      });
    });

    document.getElementById('btn-refresh-public-ip')?.addEventListener('click', async () => {
      const v4Diff = document.getElementById('dash-ipv4-diff');
      const v6Diff = document.getElementById('dash-ipv6-diff');
      if (v4Diff && !state.isIpMasked) v4Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Fetching...</span>';
      if (v6Diff && !state.isIpMasked) v6Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Fetching...</span>';
      try {
        const res = await callBridge('get_public_ip');
        if (res) {
          if (res.ipv4) state.ipv4 = res.ipv4;
          if (res.ipv6) state.ipv6 = res.ipv6;
          if (res.ip) state.publicIp = res.ip;
          if (!state.ipv4 && res.ip && !res.ip.includes(':')) state.ipv4 = res.ip;
          if (!state.ipv6 && res.ip && res.ip.includes(':')) state.ipv6 = res.ip;
          state.countryCode = res.countryCode || '';
          state.country = res.country || '';
          state.countryEmoji = res.countryEmoji || '';
          state.city = res.city || '';
          renderPublicIpDisplay();
        }
      } catch { }
    });

    // Reset Quota / Rotate Tunnel Button
    document.getElementById('btn-reset-opencode-quota')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-reset-opencode-quota');
      const icon = document.getElementById('icon-reset-quota');
      const lbl = document.getElementById('lbl-reset-quota-btn');
      const v4Diff = document.getElementById('dash-ipv4-diff');
      const v6Diff = document.getElementById('dash-ipv6-diff');
      const ipContainer = document.getElementById('dash-ip-container');
      const isProton = state.vpnProvider === 'proton';

      if (btn) btn.disabled = true;
      if (icon) icon.classList.add('spinning');
      if (lbl) lbl.textContent = isProton ? 'Rotating Proton VPN Server...' : 'Rotating Tunnel & Clearing Sockets...';
      if (v4Diff && !state.isIpMasked) v4Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Rotating...</span>';
      if (v6Diff && !state.isIpMasked) v6Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Rotating...</span>';

      try {
        const res = await callBridge('reset_opencode_quota', { provider: state.vpnProvider });
        if (res && res.success) {
          state.isDirectIsp = false;

          // Record old IPs for before vs after visual diff
          if (res.old) {
            if (res.old.ipv4 && res.old.ipv4 !== 'unknown') state.previousIpv4 = res.old.ipv4;
            if (res.old.ipv6 && res.old.ipv6 !== 'Not assigned') state.previousIpv6 = res.old.ipv6;
          } else if (res.oldIp && res.oldIp !== 'unknown') {
            state.previousIpv4 = res.oldIp;
          }

          // Record new rotated IPs
          if (res.new) {
            if (res.new.ipv4) state.ipv4 = res.new.ipv4;
            if (res.new.ipv6) state.ipv6 = res.new.ipv6;
          }
          if (res.ipv4) state.ipv4 = res.ipv4;
          if (res.ipv6) state.ipv6 = res.ipv6;
          const freshIp = res.newIp || res.ip;
          if (freshIp && freshIp !== 'unknown') {
            state.publicIp = freshIp;
            if (!state.ipv4 && !freshIp.includes(':')) state.ipv4 = freshIp;
            if (!state.ipv6 && freshIp.includes(':')) state.ipv6 = freshIp;
          }

          state.countryCode = res.countryCode || '';
          state.country = res.country || '';
          state.countryEmoji = res.countryEmoji || '';
          state.city = res.city || '';

          renderPublicIpDisplay();
          recordSuccessfulRotation();

          // Play subtle success animation on IP container
          if (ipContainer) {
            ipContainer.classList.remove('pulse-success');
            void ipContainer.offsetWidth; // Force CSS reflow
            ipContainer.classList.add('pulse-success');
          }

          const provName = isProton ? 'Proton VPN' : 'Cloudflare WARP';
          const oldV4Str = (res.old && res.old.ipv4) || res.oldIp || 'Previous';
          const newV4Str = (res.new && res.new.ipv4) || res.newIp || freshIp || 'Updated';
          const flagStr = state.countryEmoji ? `${state.countryEmoji} ` : '';
          const locStr = state.city ? ` (${state.city})` : (state.country ? ` (${state.country})` : '');
          const toastMsg = res.sidecarSynced
            ? `[${provName}] Rotated: ${oldV4Str} ➜ ${flagStr}${newV4Str}${locStr} | Sidecar Re-synced`
            : `[${provName}] Rotated: ${oldV4Str} ➜ ${flagStr}${newV4Str}${locStr}`;

          showToast(toastMsg, 'success', 5000);
          appendLog(`[QuotaReset] ${provName}: ` + (res.message || toastMsg));

          // Initiate smart cooldown (12s for WARP, 60s for Proton)
          const cooldownTarget = res.cooldownSeconds || (isProton ? 60 : 12);
          startQuotaResetCooldown(cooldownTarget);
        } else {
          renderPublicIpDisplay();
          const errMsg = (res && res.message) ? res.message : 'Tunnel rotation timed out or failed';
          const isCooldown = res && (res.cooldownRemaining || (res.message && res.message.includes('Cooldown active')));
          if (isCooldown) {
            const waitSec = res.cooldownRemaining || (isProton ? 60 : 12);
            startQuotaResetCooldown(waitSec);
            showToast(errMsg, 'info', 4000);
          } else {
            showToast(`Quota Reset Failed: ${errMsg}`, 'error', 6500);
            appendLog(`[QuotaReset] Failed: ${errMsg}`);
            if (lbl) lbl.textContent = isProton ? 'Connect Proton VPN' : 'Reset Failed';
            setTimeout(() => {
              if (!quotaCooldownInterval) updateVpnProviderUi();
            }, 3500);
          }
        }
      } catch (err) {
        renderPublicIpDisplay();
        const rawMsg = err?.message && err.message !== '[object Object]'
          ? err.message
          : (err?.data?.message || (typeof err === 'object' ? JSON.stringify(err) : String(err)));
        const errMsg = (rawMsg || 'Unknown error occurred').replace(/^Error:\s*/, '');
        showToast(`Quota Reset Error: ${errMsg}`, 'error', 6500);
        appendLog('[QuotaReset] Error: ' + errMsg);
        if (lbl) lbl.textContent = isProton ? 'Connect Proton VPN' : 'Reset Failed';
        setTimeout(() => {
          if (!quotaCooldownInterval) updateVpnProviderUi();
        }, 3500);
      } finally {
        if (!quotaCooldownInterval) {
          if (icon) icon.classList.remove('spinning');
          if (btn) btn.disabled = false;
        }
      }
    });

    // Disconnect VPN Button (Revert to Direct ISP)
    document.getElementById('btn-disconnect-vpn')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-disconnect-vpn');
      const v4Diff = document.getElementById('dash-ipv4-diff');
      const v6Diff = document.getElementById('dash-ipv6-diff');
      const ipContainer = document.getElementById('dash-ip-container');

      if (btn) btn.disabled = true;
      if (v4Diff && !state.isIpMasked) v4Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Disconnecting...</span>';
      if (v6Diff && !state.isIpMasked) v6Diff.innerHTML = '<span class="ip-new font-mono font-bold text-base text-cyan-400">Restoring ISP...</span>';

      try {
        const res = await callBridge('disconnect_vpn');
        state.isDirectIsp = true;

        if (state.ipv4 && state.ipv4 !== 'Fetching...') {
          state.previousIpv4 = state.ipv4;
        }
        if (state.ipv6 && state.ipv6 !== 'Not assigned') {
          state.previousIpv6 = state.ipv6;
        }

        if (res) {
          const freshV4 = res.ipv4 || res.newIp;
          if (freshV4 && freshV4 !== 'unknown') {
            state.ipv4 = freshV4;
            state.publicIp = freshV4;
          }
          if (res.ipv6 && res.ipv6 !== 'unknown') {
            state.ipv6 = res.ipv6;
          }
          state.countryCode = res.countryCode || '';
          state.country = res.country || '';
          state.countryEmoji = res.countryEmoji || '🌐';
          state.city = res.city || '';
        }

        renderPublicIpDisplay();

        if (ipContainer) {
          ipContainer.classList.remove('pulse-success');
          void ipContainer.offsetWidth;
          ipContainer.classList.add('pulse-success');
        }

        const msg = (res && res.message) ? res.message : 'VPN disconnected. Direct ISP connection restored.';
        showToast(msg, 'info', 5000);
        appendLog('[VPN] ' + msg);
      } catch (err) {
        const rawMsg = err?.message && err.message !== '[object Object]'
          ? err.message
          : (err?.data?.message || (typeof err === 'object' ? JSON.stringify(err) : String(err)));
        const errMsg = (rawMsg || 'Disconnect failed').replace(/^Error:\s*/, '');
        showToast(`Disconnect VPN Error: ${errMsg}`, 'error', 5000);
        appendLog('[VPN] Disconnect Error: ' + errMsg);
      } finally {
        if (btn) btn.disabled = false;
      }
    });

    document.getElementById('btn-refresh-opencode-sidecar')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-refresh-opencode-sidecar');
      const svg = btn?.querySelector('svg');
      if (svg) svg.classList.add('spinning');
      try {
        const fresh = await callBridge('refresh_opencode_sidecar');
        if (fresh) {
          handleOpenCodeUpdate(fresh);
          showToast(`Sidecar ${fresh.isOnline ? 'Online (Port ' + fresh.port + ')' : 'Offline'}`, fresh.isOnline ? 'success' : 'info', 2500);
        }
      } catch (err) {
        showToast('Sidecar refresh failed: ' + err.message, 'error', 3000);
      } finally {
        if (svg) svg.classList.remove('spinning');
      }
    });

    document.getElementById('btn-purge-sessions')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-purge-sessions');
      const svg = btn?.querySelector('svg');
      if (!state.opencode?.isOnline) {
        showToast('OpenCode Sidecar is offline', 'info', 2500);
        return;
      }
      if (btn) btn.disabled = true;
      if (svg) svg.classList.add('spinning');

      try {
        const res = await callBridge('purge_idle_sessions');
        const count = res?.deletedCount ?? 0;
        if (count > 0) {
          showToast(`Cleaned ${count} idle session${count === 1 ? '' : 's'}`, 'success', 3500);
          appendLog(`[OpenCode] Cleaned ${count} idle sessions.`);
        } else {
          showToast('No idle sessions to clean', 'info', 2500);
        }
        const fresh = await callBridge('refresh_opencode_sidecar');
        if (fresh) handleOpenCodeUpdate(fresh);
      } catch (err) {
        showToast(`Purge failed: ${err.message}`, 'error', 3000);
      } finally {
        if (svg) svg.classList.remove('spinning');
        if (btn) btn.disabled = false;
      }
    });

    // Feature 2: Service Management Controls (Start, Stop, Recheck)
    document.getElementById('btn-opencode-start')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-opencode-start');
      const textSpan = btn?.querySelector('span');
      if (btn) btn.disabled = true;
      if (textSpan) textSpan.textContent = 'Starting...';
      try {
        const res = await callBridge('start_opencode_service');
        if (res && res.success) {
          showToast(res.message || 'OpenCode Desktop launched successfully', 'success', 3500);
          if (res.state) handleOpenCodeUpdate(res.state);
        } else {
          showToast(res?.message || 'Could not start OpenCode service', 'error', 4000);
        }
      } catch (err) {
        showToast('Start OpenCode failed: ' + err.message, 'error', 3500);
      } finally {
        if (btn) btn.disabled = false;
        if (textSpan) textSpan.textContent = 'Start';
      }
    });

    document.getElementById('btn-opencode-stop')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-opencode-stop');
      const textSpan = btn?.querySelector('span');
      if (btn) btn.disabled = true;
      if (textSpan) textSpan.textContent = 'Stopping...';
      try {
        const res = await callBridge('stop_opencode_service');
        if (res && res.success) {
          showToast(res.message || 'OpenCode Desktop terminated', 'info', 3000);
          if (res.state) handleOpenCodeUpdate(res.state);
        } else {
          showToast(res?.message || 'Could not stop OpenCode service', 'error', 3500);
        }
      } catch (err) {
        showToast('Stop OpenCode failed: ' + err.message, 'error', 3500);
      } finally {
        if (btn) btn.disabled = false;
        if (textSpan) textSpan.textContent = 'Stop';
      }
    });

    document.getElementById('btn-opencode-recheck')?.addEventListener('click', async () => {
      const btn = document.getElementById('btn-opencode-recheck');
      const svg = btn?.querySelector('svg');
      if (svg) svg.classList.add('spinning');
      try {
        const fresh = await callBridge('recheck_opencode_service');
        if (fresh) {
          handleOpenCodeUpdate(fresh);
          showToast(`OpenCode status: ${fresh.isOnline ? 'Online (Port ' + fresh.port + ')' : 'Offline'}`, fresh.isOnline ? 'success' : 'info', 2500);
        }
      } catch (err) {
        showToast('Recheck failed: ' + err.message, 'error', 3000);
      } finally {
        if (svg) svg.classList.remove('spinning');
      }
    });
  }

window.setupQuotaAndIpWidget = setupQuotaAndIpWidget;

window.updateOpenCodeQuotaVisibility = updateOpenCodeQuotaVisibility;
var updateOpenCodeQuotaVisibility = window.updateOpenCodeQuotaVisibility;
window.updateVpnProviderUi = updateVpnProviderUi;
var updateVpnProviderUi = window.updateVpnProviderUi;
window.renderPublicIpDisplay = renderPublicIpDisplay;
var renderPublicIpDisplay = window.renderPublicIpDisplay;
