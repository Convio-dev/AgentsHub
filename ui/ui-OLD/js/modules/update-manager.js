  let _updateManagerInitialized = false;

  function setupUpdateManager() {
    if (_updateManagerInitialized) {
      return;
    }
    _updateManagerInitialized = true;

    const btnCheck = document.getElementById('btn-check-updates');
    const btnCheckText = document.getElementById('btn-check-updates-text');
    const btnInstall = document.getElementById('btn-install-update');
    const statusDot = document.getElementById('update-status-indicator');
    const statusText = document.getElementById('update-status-text');
    const statusSubtext = document.getElementById('update-status-subtext');
    const syncIcon = document.getElementById('icon-sync-updates');
    const progressContainer = document.getElementById('update-progress-container');
    const progressBar = document.getElementById('update-progress-bar');
    const progressPct = document.getElementById('update-progress-pct');
    const progressLabel = document.getElementById('update-progress-label');
    const notesContainer = document.getElementById('update-notes-container');
    const notesTag = document.getElementById('update-notes-version-tag');
    const notesTitle = document.getElementById('update-notes-title');
    const notesBody = document.getElementById('update-notes-body');

    function applyUpdateState(info) {
      if (!info) return;
      latestUpdateData = info;
      const isAvail = !!(info.isUpdateAvailable ?? info.IsUpdateAvailable);
      const res = info.result ?? info.Result;
      const latestVer = (info.latestVersion || info.LatestVersion || '').replace(/^v/i, '');
      const currentVer = (info.currentVersion || info.CurrentVersion || state.version || '1.0.3').replace(/^v/i, '');

      if (isAvail) {
        if (statusDot) statusDot.style.background = '#10b981';
        if (statusText) statusText.textContent = `Update Available: v${latestVer}`;
        if (statusSubtext) statusSubtext.textContent = `Current: v${currentVer} · Latest: v${latestVer}`;
        if (btnInstall) {
          btnInstall.style.display = 'inline-flex';
          btnInstall.disabled = false;
        }
        if (notesContainer) {
          notesContainer.style.display = 'block';
          if (notesTag) notesTag.textContent = `v${latestVer}`;
          if (notesTitle) notesTitle.textContent = info.releaseName || info.ReleaseName || `Release v${latestVer}`;
          if (notesBody) notesBody.textContent = info.releaseNotes || info.ReleaseNotes || 'No release notes provided.';
        }
      } else if (res === 1 || res === 'UpToDate' || (latestVer && latestVer === currentVer)) {
        if (statusDot) statusDot.style.background = '#10b981';
        if (statusText) statusText.textContent = 'Agents Hub is up to date';
        if (statusSubtext) statusSubtext.textContent = `Version v${currentVer} · Checked just now`;
        if (btnInstall) btnInstall.style.display = 'none';
        if (notesContainer) notesContainer.style.display = 'none';
      } else if (res === 2 || res === 'NoReleasesFound') {
        if (statusDot) statusDot.style.background = 'var(--text-dim)';
        if (statusText) statusText.textContent = 'No published releases found yet';
        if (statusSubtext) statusSubtext.textContent = `Current: v${currentVer} · Target: github.com/ZerroDevs/AgentsHub`;
        if (btnInstall) btnInstall.style.display = 'none';
        if (notesContainer) notesContainer.style.display = 'none';
      } else {
        if (statusDot) statusDot.style.background = '#ef4444';
        if (statusText) statusText.textContent = 'Failed to check GitHub releases';
        if (statusSubtext) statusSubtext.textContent = 'Check your network connection and retry';
        if (btnInstall) btnInstall.style.display = 'none';
        if (notesContainer) notesContainer.style.display = 'none';
      }
    }

    async function checkForUpdates(manual = true) {
      if (!btnCheck) return;
      btnCheck.disabled = true;
      if (btnCheckText) btnCheckText.textContent = 'Checking...';
      if (syncIcon) syncIcon.style.animation = 'spin 1s linear infinite';
      if (statusText) statusText.textContent = 'Checking GitHub for releases...';
      if (statusDot) statusDot.style.background = 'var(--accent-cyan)';

      try {
        const info = await callBridge('check_for_updates');
        applyUpdateState(info);
        if (manual && info) {
          const isAvail = !!(info.isUpdateAvailable ?? info.IsUpdateAvailable);
          const res = info.result ?? info.Result;
          const latestVer = (info.latestVersion || info.LatestVersion || '').replace(/^v/i, '');
          const currentVer = (info.currentVersion || info.CurrentVersion || state.version || '1.0.3').replace(/^v/i, '');

          if (isAvail) {
            showToast(`Agents Hub v${latestVer} is available!`, 'success', 5000);
          } else if (res === 1 || res === 'UpToDate' || (latestVer && latestVer === currentVer)) {
            showToast(`You are running the latest version of Agents Hub (v${currentVer}).`, 'info', 3000);
          } else if (res === 2 || res === 'NoReleasesFound') {
            showToast('No releases published on GitHub yet.', 'info', 3000);
          } else {
            showToast('Could not reach GitHub releases.', 'warn', 3500);
          }
        }
      } catch (err) {
        if (statusDot) statusDot.style.background = '#ef4444';
        if (statusText) statusText.textContent = 'Update check error';
        if (statusSubtext) statusSubtext.textContent = err?.message || 'Network request failed';
        if (manual) showToast('Failed to check for updates: ' + (err?.message || 'Network error'), 'error', 4000);
      } finally {
        btnCheck.disabled = false;
        if (btnCheckText) btnCheckText.textContent = 'Check Now';
        if (syncIcon) syncIcon.style.animation = '';
      }
    }

    btnCheck?.addEventListener('click', () => checkForUpdates(true));

    btnInstall?.addEventListener('click', async () => {
      if (!latestUpdateData || !latestUpdateData.downloadUrl) {
        showToast('No download URL available for this update.', 'error', 3500);
        return;
      }

      btnInstall.disabled = true;
      if (btnCheck) btnCheck.disabled = true;
      if (progressContainer) progressContainer.style.display = 'block';
      if (progressBar) progressBar.style.width = '0%';
      if (progressPct) progressPct.textContent = '0%';
      if (progressLabel) progressLabel.textContent = 'Downloading installer...';

      try {
        await callBridge('install_update', { downloadUrl: latestUpdateData.downloadUrl });
        showToast('Download started. The installer will launch automatically.', 'info', 4000);
      } catch (err) {
        btnInstall.disabled = false;
        if (btnCheck) btnCheck.disabled = false;
        if (progressContainer) progressContainer.style.display = 'none';
        showToast('Failed to start update: ' + (err?.message || 'Unknown error'), 'error', 4000);
      }
    });

    // Expose helpers on window
    window.triggerUpdateView = function() {
      switchView('settings');
      const updateSec = document.getElementById('update-section');
      if (updateSec) updateSec.scrollIntoView({ behavior: 'smooth' });
      checkForUpdates(true);
    };

    window.handleExternalUpdateEvent = function(eventName, payload) {
      if (eventName === 'update_available' && payload) {
        applyUpdateState(payload);
        showToast(`Update Available: Agents Hub v${payload.latestVersion}`, 'info', 7000);
      } else if (eventName === 'update_download_progress' && payload) {
        if (progressContainer) progressContainer.style.display = 'block';
        const pct = Math.round((payload.progress || 0) * 100);
        if (progressBar) progressBar.style.width = pct + '%';
        if (progressPct) progressPct.textContent = pct + '%';
        if (pct >= 100) {
          if (progressLabel) progressLabel.textContent = 'Download complete. Launching installer...';
        }
      } else if (eventName === 'update_download_failed') {
        if (progressContainer) progressContainer.style.display = 'none';
        if (btnInstall) btnInstall.disabled = false;
        if (btnCheck) btnCheck.disabled = false;
        showToast('Update download failed: ' + (payload?.error || 'Network error'), 'error', 5000);
      }
    };
  }

  // System Health & Diagnostics Doctor Manager
window.setupUpdateManager = setupUpdateManager;
