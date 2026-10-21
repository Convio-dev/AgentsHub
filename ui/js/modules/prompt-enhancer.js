  function switchPromptSubTab(tabName) {
    try {
      localStorage.setItem('agents_hub_prompt_subtab', tabName);
    } catch (e) {}
    const isWorkspace = tabName === 'workspace';
    const isProviders = tabName === 'providers';
    const isTelemetry = tabName === 'telemetry';

    const btnWorkspace = document.getElementById('tab-btn-prompt-workspace');
    const btnProviders = document.getElementById('tab-btn-prompt-providers');
    const btnTelemetry = document.getElementById('tab-btn-prompt-telemetry');
    const subviewWorkspace = document.getElementById('subview-prompt-workspace');
    const subviewProviders = document.getElementById('subview-prompt-providers');
    const subviewTelemetry = document.getElementById('subview-prompt-telemetry');

    if (btnWorkspace) {
      btnWorkspace.classList.toggle('active', isWorkspace);
      btnWorkspace.setAttribute('aria-selected', isWorkspace ? 'true' : 'false');
    }
    if (btnProviders) {
      btnProviders.classList.toggle('active', isProviders);
      btnProviders.setAttribute('aria-selected', isProviders ? 'true' : 'false');
    }
    if (btnTelemetry) {
      btnTelemetry.classList.toggle('active', isTelemetry);
      btnTelemetry.setAttribute('aria-selected', isTelemetry ? 'true' : 'false');
    }

    if (subviewWorkspace) subviewWorkspace.style.display = isWorkspace ? '' : 'none';
    if (subviewProviders) subviewProviders.style.display = isProviders ? '' : 'none';
    if (subviewTelemetry) subviewTelemetry.style.display = isTelemetry ? '' : 'none';

    if (isProviders) {
      renderProvidersList();
      if (!state.selectedProvider && state.aiConfig && state.aiConfig.providers && state.aiConfig.providers.length > 0) {
        selectProvider(state.aiConfig.providers[0]);
      } else if (state.selectedProvider) {
        selectProvider(state.selectedProvider);
      }
    } else if (isWorkspace) {
      renderPromptModelsDropdown();
    } else if (isTelemetry) {
      callBridge('get_token_telemetry').then(r => {
        if (r) renderTokenTelemetry(r);
      }).catch(() => {});
    }
  }

  function renderPromptModelsDropdown() {
    const select = document.getElementById('enhance-model-select');
    if (!select) return;

    select.innerHTML = '';
    const enabledProviders = (state.aiConfig.providers || []).filter(p => p.isEnabled);

    if (enabledProviders.length === 0) {
      const opt = document.createElement('option');
      opt.textContent = '(No providers enabled - configure in AI Providers tab)';
      opt.disabled = true;
      select.appendChild(opt);
      return;
    }

    enabledProviders.forEach(prov => {
      const optGroup = document.createElement('optgroup');
      optGroup.label = `-- ${prov.name}`;

      if (!prov.models || prov.models.length === 0) {
        const item = document.createElement('option');
        item.textContent = '   - (No models configured)';
        item.disabled = true;
        optGroup.appendChild(item);
      } else {
        prov.models.forEach(m => {
          const item = document.createElement('option');
          item.value = `${prov.id}::${m}`;
          item.textContent = `  - ${m}`;
          if (prov.id === state.aiConfig.selectedProviderId && m === state.aiConfig.selectedModel) {
            item.selected = true;
          }
          optGroup.appendChild(item);
        });
      }
      select.appendChild(optGroup);
    });
  }

  // Interactive Terminals - Workspace Preset Selector
  const TERMINAL_PRESET_LABELS = {
    Preset_Clean: 'Clean Slate',
    Preset_Duo: 'AI Duo (Claude + agy)',
    Preset_All: 'Full Suite'
  };

  function getPromptPresets() {
    if (!state.aiConfig) state.aiConfig = {};
    if (!Array.isArray(state.aiConfig.promptPresets)) state.aiConfig.promptPresets = [];
    return state.aiConfig.promptPresets;
  }

  function getActivePromptPreset() {
    const presets = getPromptPresets();
    const activeId = state.aiConfig.activePromptPresetId || 'preset_general';
    return presets.find(p => p.id === activeId) || presets[0] || null;
  }

  function getBuiltInPromptPreset(id) {
    const builtIns = Array.isArray(state.aiConfig.builtInPromptPresets) ? state.aiConfig.builtInPromptPresets : [];
    return builtIns.find(p => p.id === id) || null;
  }

  function loadPresetIntoDrawer(presetId) {
    const preset = getPromptPresets().find(p => p.id === presetId);
    const textarea = document.getElementById('prompt-system-instructions-input');
    const meta = document.getElementById('prompt-preset-meta');
    const deleteBtn = document.getElementById('btn-delete-preset-prompt');
    const slider = document.getElementById('prompt-temp-slider');
    const sliderVal = document.getElementById('prompt-temp-val');

    if (textarea) textarea.value = preset ? (preset.systemPrompt || '') : '';

    if (meta) {
      meta.textContent = preset
        ? (preset.isBuiltIn ? 'Built-in preset - editable, restorable via Reset' : 'Custom preset')
        : 'No preset selected';
    }

    if (deleteBtn) deleteBtn.classList.toggle('hidden', !preset || !!preset.isBuiltIn);

    const temp = Number.isFinite(state.aiConfig.promptEnhancerTemperature)
      ? state.aiConfig.promptEnhancerTemperature
      : 0.6;
    if (slider) slider.value = temp;
    if (sliderVal) sliderVal.textContent = temp.toFixed(1);
  }

  function renderPromptPresets() {
    const select = document.getElementById('enhance-style-select');
    if (!select) return;

    const presets = getPromptPresets();
    if (presets.length === 0) return;

    const activeId = state.aiConfig.activePromptPresetId || 'preset_general';
    select.innerHTML = '';
    presets.forEach(preset => {
      const opt = document.createElement('option');
      opt.value = preset.id;
      opt.textContent = preset.isBuiltIn ? preset.name : `${preset.name} (custom)`;
      if (preset.id === activeId) opt.selected = true;
      select.appendChild(opt);
    });

    loadPresetIntoDrawer(activeId);
  }

  function setupPromptEnhancer() {
    const input = document.getElementById('prompt-input');
    const output = document.getElementById('prompt-output');
    const inCount = document.getElementById('prompt-input-count');
    const outCount = document.getElementById('prompt-output-count');
    const btnExec = document.getElementById('btn-enhance-exec');
    const statusLbl = document.getElementById('prompt-enhance-status');

    const styleSelect = document.getElementById('enhance-style-select');
    const drawer = document.getElementById('prompt-instructions-drawer');
    const drawerToggle = document.getElementById('btn-toggle-prompt-instructions');
    const instructionsInput = document.getElementById('prompt-system-instructions-input');
    const tempSlider = document.getElementById('prompt-temp-slider');
    const tempVal = document.getElementById('prompt-temp-val');
    const saveStatus = document.getElementById('prompt-preset-save-status');

    const updateCounts = (text, targetEl) => {
      if (!targetEl) return;
      const chars = text.length;
      const words = text.trim() ? text.trim().split(/\s+/).length : 0;
      targetEl.textContent = `${chars} chars | ${words} words`;
    };

    const flashSaveStatus = (message, color) => {
      if (!saveStatus) return;
      saveStatus.textContent = message;
      saveStatus.style.color = color;
      setTimeout(() => { if (saveStatus.textContent === message) saveStatus.textContent = ''; }, 2500);
    };

    const persistConfig = async () => {
      await callBridge('save_ai_config', { config: state.aiConfig });
    };

    input?.addEventListener('input', () => updateCounts(input.value, inCount));
    output?.addEventListener('input', () => updateCounts(output.value, outCount));

    input?.addEventListener('keydown', (e) => {
      if (e.ctrlKey && e.key === 'Enter') {
        e.preventDefault();
        execEnhance();
      }
    });

    btnExec?.addEventListener('click', execEnhance);

    document.getElementById('btn-paste-prompt')?.addEventListener('click', async () => {
      try {
        const text = await navigator.clipboard.readText();
        input.value = text;
        updateCounts(text, inCount);
      } catch { }
    });

    document.getElementById('btn-copy-enhanced')?.addEventListener('click', () => {
      if (output.value) {
        navigator.clipboard.writeText(output.value);
        statusLbl.textContent = 'Copied to clipboard!';
        setTimeout(() => statusLbl.textContent = 'Ready', 2000);
      }
    });

    // Subview Switching: Workspace vs AI Providers
    document.getElementById('tab-btn-prompt-workspace')?.addEventListener('click', () => {
      switchPromptSubTab('workspace');
    });

    document.getElementById('tab-btn-prompt-providers')?.addEventListener('click', () => {
      switchPromptSubTab('providers');
    });

    document.getElementById('tab-btn-prompt-telemetry')?.addEventListener('click', () => {
      switchPromptSubTab('telemetry');
    });

    document.getElementById('quick-telemetry-badge')?.addEventListener('click', () => {
      switchPromptSubTab('telemetry');
    });

    document.getElementById('btn-refresh-telemetry')?.addEventListener('click', () => {
      callBridge('get_token_telemetry').then(r => {
        if (r) renderTokenTelemetry(r);
      }).catch(() => {});
    });

    document.getElementById('btn-reset-telemetry')?.addEventListener('click', async () => {
      if (confirm('Reset all token telemetry statistics?')) {
        try {
          const r = await callBridge('reset_token_telemetry');
          if (r) renderTokenTelemetry(r);
          showToast('Token telemetry reset', 'info');
        } catch (err) {
          showToast('Failed to reset telemetry: ' + err.message, 'error');
        }
      }
    });

    document.getElementById('btn-goto-providers-config')?.addEventListener('click', () => {
      switchPromptSubTab('providers');
    });

    document.getElementById('btn-back-to-enhancer')?.addEventListener('click', () => {
      switchPromptSubTab('workspace');
    });

    // Iterative refinement: push the enhanced result back into the raw input.
    document.getElementById('btn-refine-prompt')?.addEventListener('click', () => {
      if (!output.value) {
        statusLbl.textContent = 'Nothing to refine yet.';
        statusLbl.style.color = 'var(--accent-amber)';
        return;
      }
      input.value = output.value;
      updateCounts(input.value, inCount);
      input.focus();
      statusLbl.textContent = 'Sent back to the raw prompt - refine and enhance again.';
      statusLbl.style.color = 'var(--accent-green)';
    });

    // Collapsible instructions drawer
    drawerToggle?.addEventListener('click', () => {
      if (!drawer) return;
      const wasHidden = drawer.classList.contains('hidden');
      drawer.classList.toggle('hidden', !wasHidden);
      drawerToggle.classList.toggle('btn-success', wasHidden);
      drawerToggle.classList.toggle('btn-outline', !wasHidden);
      if (wasHidden) {
        loadPresetIntoDrawer(styleSelect ? styleSelect.value : state.aiConfig.activePromptPresetId);
      }
    });

    // Preset selection loads its instructions
    styleSelect?.addEventListener('change', () => {
      state.aiConfig.activePromptPresetId = styleSelect.value;
      loadPresetIntoDrawer(styleSelect.value);
    });

    tempSlider?.addEventListener('input', () => {
      const val = parseFloat(tempSlider.value);
      if (tempVal) tempVal.textContent = val.toFixed(1);
    });

    // Persist edits to the active preset
    document.getElementById('btn-save-preset-prompt')?.addEventListener('click', async () => {
      const preset = getActivePromptPreset();
      if (!preset) return;

      preset.systemPrompt = instructionsInput ? instructionsInput.value : preset.systemPrompt;
      state.aiConfig.promptEnhancerTemperature = parseFloat(tempSlider ? tempSlider.value : '0.6');
      state.aiConfig.activePromptPresetId = preset.id;

      try {
        await persistConfig();
        flashSaveStatus('Saved.', 'var(--accent-green)');
      } catch (err) {
        flashSaveStatus('Save failed: ' + err.message, 'var(--accent-red)');
      }
    });

    // Restore a built-in preset's shipped instructions
    document.getElementById('btn-reset-preset-prompt')?.addEventListener('click', async () => {
      const preset = getActivePromptPreset();
      if (!preset) return;

      const builtIn = getBuiltInPromptPreset(preset.id);
      if (!builtIn) {
        flashSaveStatus('No shipped default for this preset.', 'var(--accent-amber)');
        return;
      }

      preset.systemPrompt = builtIn.systemPrompt;
      if (instructionsInput) instructionsInput.value = builtIn.systemPrompt;

      try {
        await persistConfig();
        flashSaveStatus('Restored default instructions.', 'var(--accent-green)');
      } catch (err) {
        flashSaveStatus('Reset failed: ' + err.message, 'var(--accent-red)');
      }
    });

    // Create a custom preset from the current instructions
    document.getElementById('btn-new-preset-prompt')?.addEventListener('click', async () => {
      const name = window.prompt('Name for the new preset:', 'My Preset');
      if (!name || !name.trim()) return;

      const preset = {
        id: 'preset_custom_' + Date.now().toString(36),
        name: name.trim(),
        systemPrompt: instructionsInput ? instructionsInput.value : '',
        isBuiltIn: false
      };

      getPromptPresets().push(preset);
      state.aiConfig.activePromptPresetId = preset.id;

      try {
        await persistConfig();
        renderPromptPresets();
        flashSaveStatus(`Created "${preset.name}".`, 'var(--accent-green)');
      } catch (err) {
        flashSaveStatus('Create failed: ' + err.message, 'var(--accent-red)');
      }
    });

    // Delete a custom preset (built-ins are protected)
    document.getElementById('btn-delete-preset-prompt')?.addEventListener('click', async () => {
      const preset = getActivePromptPreset();
      if (!preset || preset.isBuiltIn) return;
      if (!window.confirm(`Delete preset "${preset.name}"?`)) return;

      state.aiConfig.promptPresets = getPromptPresets().filter(p => p.id !== preset.id);
      state.aiConfig.activePromptPresetId = 'preset_general';

      try {
        await persistConfig();
        renderPromptPresets();
        flashSaveStatus('Preset deleted.', 'var(--accent-green)');
      } catch (err) {
        flashSaveStatus('Delete failed: ' + err.message, 'var(--accent-red)');
      }
    });

    async function execEnhance() {
      const raw = input.value.trim();
      if (!raw) {
        statusLbl.textContent = 'Please enter a prompt first.';
        statusLbl.style.color = 'var(--accent-amber)';
        return;
      }

      const modelVal = document.getElementById('enhance-model-select').value;
      if (!modelVal || !modelVal.includes('::')) {
        statusLbl.textContent = 'Please select a valid model.';
        statusLbl.style.color = 'var(--accent-amber)';
        return;
      }

      const [providerId, model] = modelVal.split('::');
      const style = styleSelect ? styleSelect.value : 'preset_general';

      // Send the instructions the user currently sees, so unsaved edits still apply.
      const systemPrompt = instructionsInput ? instructionsInput.value.trim() : '';
      const temperature = tempSlider ? parseFloat(tempSlider.value) : 0.6;

      btnExec.disabled = true;
      statusLbl.textContent = 'Enhancing prompt...';
      statusLbl.style.color = 'var(--accent-green)';
      output.value = 'Enhancing in progress, please wait...';

      try {
        const res = await callBridge('enhance_prompt', {
          providerId,
          model,
          prompt: raw,
          style,
          systemPrompt,
          temperature
        });
        output.value = res.enhancedText || '';
        updateCounts(output.value, outCount);
        statusLbl.textContent = 'Enhancement complete!';
      } catch (err) {
        output.value = '';
        statusLbl.textContent = 'Failed: ' + err.message;
        statusLbl.style.color = 'var(--accent-red)';
      } finally {
        btnExec.disabled = false;
      }
    }

    try {
      const savedSubTab = localStorage.getItem('agents_hub_prompt_subtab');
      if (savedSubTab) {
        switchPromptSubTab(savedSubTab);
      }
    } catch (e) {}
  }

  // Antigravity Profiles View
window.setupPromptEnhancer = setupPromptEnhancer;

window.renderPromptModelsDropdown = renderPromptModelsDropdown;
var renderPromptModelsDropdown = window.renderPromptModelsDropdown;
window.renderPromptPresets = renderPromptPresets;
var renderPromptPresets = window.renderPromptPresets;
window.switchPromptSubTab = switchPromptSubTab;
var switchPromptSubTab = window.switchPromptSubTab;
