  function setupSoundpackAudio() {
    const masterToggle = document.getElementById('soundpack-master-toggle');
    const masterStatus = document.getElementById('soundpack-master-status');
    const controlsBody = document.getElementById('soundpack-controls-body');
    const themeSelect = document.getElementById('soundpack-theme-select');
    const volSlider = document.getElementById('soundpack-volume');
    const volLabel = document.getElementById('soundpack-vol-label');
    const btnTest = document.getElementById('btn-test-soundpack');

    const generalCheckbox = document.getElementById('setting-audio-chimes-checkbox');
    const generalVolSlider = document.getElementById('setting-audio-volume');
    const generalVolVal = document.getElementById('audio-volume-val');

    const chkDone = document.getElementById('chk-sound-task-done');
    const chkApproval = document.getElementById('chk-sound-approval');
    const chkCheckpoint = document.getElementById('chk-sound-checkpoint');
    const chkError = document.getElementById('chk-sound-error');

    let currentConfig = {
      isEnabled: false, // Default turned off
      theme: 'modern',
      volume: 70,
      notifyTaskDone: true,
      notifyApproval: true,
      notifyCheckpoint: true,
      notifyError: true
    };

    function updateUiState(enabled) {
      if (masterToggle) masterToggle.checked = enabled;
      if (generalCheckbox) generalCheckbox.checked = enabled;
      if (masterStatus) {
        masterStatus.textContent = enabled ? 'Audio: Enabled' : 'Audio: Disabled';
        masterStatus.style.color = enabled ? 'var(--accent-cyan)' : 'var(--text-dim)';
      }
      if (controlsBody) {
        controlsBody.style.opacity = enabled ? '1' : '0.45';
        controlsBody.style.pointerEvents = enabled ? 'auto' : 'none';
      }
    }

    async function saveConfig() {
      currentConfig.isEnabled = masterToggle?.checked ?? false;
      currentConfig.theme = themeSelect?.value || 'modern';
      currentConfig.volume = parseInt(volSlider?.value || '70', 10);
      currentConfig.notifyTaskDone = chkDone?.checked ?? true;
      currentConfig.notifyApproval = chkApproval?.checked ?? true;
      currentConfig.notifyCheckpoint = chkCheckpoint?.checked ?? true;
      currentConfig.notifyError = chkError?.checked ?? true;

      localStorage.setItem('agents_hub_soundpack_enabled', currentConfig.isEnabled.toString());
      localStorage.setItem('agents_hub_soundpack_theme', currentConfig.theme);
      localStorage.setItem('agents_hub_soundpack_vol', currentConfig.volume.toString());

      try {
        await callBridge('save_soundpack_config', { config: currentConfig });
      } catch (err) {
        console.warn('Failed to save soundpack config:', err);
      }
    }

    window.setSoundpackMasterEnabled = (enabled) => {
      if (masterToggle) masterToggle.checked = enabled;
      updateUiState(enabled);
      saveConfig();
    };

    window.setSoundpackMasterVolume = (vol) => {
      if (volSlider) volSlider.value = vol;
      if (volLabel) volLabel.textContent = vol + '%';
      if (generalVolSlider) generalVolSlider.value = vol;
      if (generalVolVal) generalVolVal.textContent = vol + '%';
      saveConfig();
    };

    masterToggle?.addEventListener('change', () => {
      const enabled = masterToggle.checked;
      updateUiState(enabled);
      saveConfig();
      showToast(`Audio Soundpack & Chimes ${enabled ? 'Enabled' : 'Disabled'}`, 'info', 2500);
    });

    volSlider?.addEventListener('input', () => {
      const val = volSlider.value;
      if (volLabel) volLabel.textContent = val + '%';
      if (generalVolSlider) generalVolSlider.value = val;
      if (generalVolVal) generalVolVal.textContent = val + '%';
      saveConfig();
    });

    themeSelect?.addEventListener('change', saveConfig);
    chkDone?.addEventListener('change', saveConfig);
    chkApproval?.addEventListener('change', saveConfig);
    chkCheckpoint?.addEventListener('change', saveConfig);
    chkError?.addEventListener('change', saveConfig);

    btnTest?.addEventListener('click', () => {
      const theme = themeSelect?.value || 'modern';
      const vol = parseInt(volSlider?.value || '70', 10);
      callBridge('play_sound_event', { eventName: 'task_done', theme, volume: vol, force: true });
    });

    async function loadConfig() {
      try {
        const remote = await callBridge('get_soundpack_config');
        if (remote) {
          currentConfig = { ...currentConfig, ...remote };
        } else {
          // If no remote, default as turned off
          currentConfig.isEnabled = localStorage.getItem('agents_hub_soundpack_enabled') === 'true';
        }
      } catch {
        currentConfig.isEnabled = localStorage.getItem('agents_hub_soundpack_enabled') === 'true';
      }

      updateUiState(currentConfig.isEnabled);

      if (themeSelect && currentConfig.theme) themeSelect.value = currentConfig.theme;
      if (volSlider) {
        volSlider.value = currentConfig.volume || 70;
        if (volLabel) volLabel.textContent = volSlider.value + '%';
        if (generalVolSlider) generalVolSlider.value = volSlider.value;
        if (generalVolVal) generalVolVal.textContent = volSlider.value + '%';
      }
      if (chkDone) chkDone.checked = currentConfig.notifyTaskDone ?? true;
      if (chkApproval) chkApproval.checked = currentConfig.notifyApproval ?? true;
      if (chkCheckpoint) chkCheckpoint.checked = currentConfig.notifyCheckpoint ?? true;
      if (chkError) chkError.checked = currentConfig.notifyError ?? true;
    }

    loadConfig();
  }

  // Feature 4: Scheduled AI Automations & Background Task Runner
window.setupSoundpackAudio = setupSoundpackAudio;
