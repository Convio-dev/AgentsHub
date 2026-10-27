  function setupClaudeGatewayConfig() {
    const card = document.getElementById('claude-gateway-config-card');
    if (!card) return;

    const toggleHeader = document.getElementById('btn-toggle-claude-config');
    const collapseBtn = document.getElementById('btn-collapse-claude-config');
    const chevronText = document.getElementById('claude-config-chevron');
    const chevronIcon = document.getElementById('claude-config-chevron-icon');
    const body = document.getElementById('claude-config-body');
    const badge = document.getElementById('claude-config-status-badge');
    const injectionCheckbox = document.getElementById('claude-injection-enable-checkbox');

    const providerSelect = document.getElementById('claude-provider-select');
    const btnAddProvider = document.getElementById('btn-add-claude-provider');
    const btnDeleteProvider = document.getElementById('btn-delete-claude-provider');
    const baseUrlInput = document.getElementById('claude-baseurl-input');
    const apiKeysContainer = document.getElementById('claude-apikeys-container');
    const btnAddApiKey = document.getElementById('btn-add-claude-apikey');
    const keysCountBadge = document.getElementById('claude-keys-count-badge');
    const btnSaveProvider = document.getElementById('btn-save-claude-provider');

    const balanceText = document.getElementById('claude-key-balance-text');
    const modelsCountText = document.getElementById('claude-free-models-count-text');
    const btnFetchModels = document.getElementById('btn-fetch-models-live');
    const btnLogout = document.getElementById('btn-claude-logout');

    let providersMap = {};
    let isOpen = localStorage.getItem('agents_hub_claude_config_open') === 'true';

    function updateCollapseUi() {
      if (!body) return;
      body.style.display = isOpen ? 'block' : 'none';
      if (chevronText) chevronText.textContent = isOpen ? 'Close Config' : 'Open Config';
      if (chevronIcon) chevronIcon.style.transform = isOpen ? 'rotate(180deg)' : 'rotate(0deg)';
      localStorage.setItem('agents_hub_claude_config_open', isOpen ? 'true' : 'false');
    }

    const toggleCollapse = () => {
      isOpen = !isOpen;
      updateCollapseUi();
    };

    toggleHeader?.addEventListener('click', (e) => {
      if (e.target.closest('#btn-collapse-claude-config') || e.target.closest('#claude-injection-enable-checkbox')) return;
      toggleCollapse();
    });
    collapseBtn?.addEventListener('click', (e) => {
      e.stopPropagation();
      toggleCollapse();
    });

    function updateBadge(enabled, hasKey) {
      if (!badge) return;
      if (!enabled) {
        badge.textContent = 'Injection Disabled (Stock Anthropic Mode)';
        badge.style.background = 'rgba(100, 116, 139, 0.15)';
        badge.style.color = 'var(--text-dim)';
      } else {
        badge.textContent = hasKey ? 'Configured' : 'Key Needed';
        badge.style.background = hasKey ? 'rgba(34, 197, 94, 0.15)' : 'rgba(245, 158, 11, 0.15)';
        badge.style.color = hasKey ? 'var(--accent-green)' : 'var(--warning-amber)';
      }
    }

    function renderApiKeyRows(keys) {
      if (!apiKeysContainer) return;
      apiKeysContainer.innerHTML = '';
      const keyList = (keys && keys.length > 0) ? keys : [''];
      if (keysCountBadge) {
        keysCountBadge.textContent = `${keyList.length} ${keyList.length === 1 ? 'key' : 'keys'}`;
      }

      keyList.forEach((k, idx) => {
        const row = document.createElement('div');
        row.className = 'claude-apikey-row';
        row.style.cssText = 'display: flex; align-items: center; gap: 6px;';

        const label = document.createElement('span');
        label.className = 'claude-key-badge';
        label.style.cssText = 'font-size: 10.5px; font-family: var(--font-mono); color: var(--text-dim); min-width: 24px; text-align: center; font-weight: 600;';
        label.textContent = `#${idx + 1}`;

        const input = document.createElement('input');
        input.type = 'password';
        input.className = 'input-text claude-key-input';
        input.style.cssText = 'font-family: var(--font-mono); font-size: 12px; flex: 1;';
        input.placeholder = `OpenRouter API key #${idx + 1} (sk-or-v1-...)`;
        input.value = k || '';

        const eyeBtn = document.createElement('button');
        eyeBtn.type = 'button';
        eyeBtn.className = 'btn btn-outline';
        eyeBtn.title = 'Show or Hide Key';
        eyeBtn.style.cssText = 'padding: 0 8px; height: 30px; display: inline-flex; align-items: center; justify-content: center;';
        eyeBtn.innerHTML = '<svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24"><path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/></svg>';
        eyeBtn.addEventListener('click', (e) => {
          e.stopPropagation();
          const isPass = input.type === 'password';
          input.type = isPass ? 'text' : 'password';
          eyeBtn.innerHTML = isPass
            ? '<svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24"><path d="m2 2 20 20"/><path d="M9.88 9.88a3 3 0 1 0 4.24 4.24"/><path d="M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68"/><path d="M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61"/></svg>'
            : '<svg class="icon-svg" style="width: 13px; height: 13px;" viewBox="0 0 24 24"><path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/></svg>';
        });

        const testBtn = document.createElement('button');
        testBtn.type = 'button';
        testBtn.className = 'btn btn-outline';
        testBtn.title = 'Test this specific key';
        testBtn.style.cssText = 'padding: 0 8px; height: 30px; font-size: 11px;';
        testBtn.textContent = 'Test';
        testBtn.addEventListener('click', async (e) => {
          e.stopPropagation();
          const val = input.value.trim();
          if (!val) {
            showToast(`Key #${idx + 1} is empty`, 'error', 2500);
            return;
          }
          testBtn.disabled = true;
          testBtn.textContent = '...';
          try {
            const res = await callBridge('check_openrouter_key', { apiKey: val });
            if (res && res.valid) {
              showToast(`Key #${idx + 1} valid! ${res.message || ''}`, 'success', 3500);
            } else {
              showToast(`Key #${idx + 1} check: ${res?.message || 'Invalid key'}`, 'error', 3500);
            }
          } catch (err) {
            showToast(`Key #${idx + 1} check error: ${err.message}`, 'error', 3500);
          } finally {
            testBtn.disabled = false;
            testBtn.textContent = 'Test';
          }
        });

        const delBtn = document.createElement('button');
        delBtn.type = 'button';
        delBtn.className = 'btn btn-danger';
        delBtn.title = 'Remove this key';
        delBtn.style.cssText = 'padding: 0 8px; height: 30px; font-size: 11px;';
        delBtn.textContent = '✕';
        delBtn.addEventListener('click', (e) => {
          e.stopPropagation();
          const allRows = apiKeysContainer.querySelectorAll('.claude-apikey-row');
          if (allRows.length <= 1) {
            input.value = '';
            showToast('Key cleared', 'info', 2000);
          } else {
            row.remove();
            renumberKeyRows();
          }
        });

        row.appendChild(label);
        row.appendChild(input);
        row.appendChild(eyeBtn);
        row.appendChild(testBtn);
        row.appendChild(delBtn);
        apiKeysContainer.appendChild(row);
      });
    }

    function renumberKeyRows() {
      if (!apiKeysContainer) return;
      const rows = apiKeysContainer.querySelectorAll('.claude-apikey-row');
      rows.forEach((r, i) => {
        const lbl = r.querySelector('.claude-key-badge');
        if (lbl) lbl.textContent = `#${i + 1}`;
        const inp = r.querySelector('.claude-key-input');
        if (inp) inp.placeholder = `OpenRouter API key #${i + 1} (sk-or-v1-...)`;
      });
      if (keysCountBadge) {
        keysCountBadge.textContent = `${rows.length} ${rows.length === 1 ? 'key' : 'keys'}`;
      }
    }

    btnAddApiKey?.addEventListener('click', (e) => {
      e.stopPropagation();
      if (!apiKeysContainer) return;
      const currentKeys = Array.from(apiKeysContainer.querySelectorAll('.claude-key-input')).map(i => i.value);
      currentKeys.push('');
      renderApiKeyRows(currentKeys);
      const inputs = apiKeysContainer.querySelectorAll('.claude-key-input');
      if (inputs.length > 0) {
        inputs[inputs.length - 1].focus();
      }
    });

    function getEnteredApiKeys() {
      if (!apiKeysContainer) return [];
      const inputs = Array.from(apiKeysContainer.querySelectorAll('.claude-key-input'));
      return inputs.map(i => i.value.trim()).filter(Boolean);
    }

    async function checkKeyHealth(key) {
      if (!balanceText) return;
      if (!key) {
        balanceText.textContent = 'Ready (No key set)';
        balanceText.style.color = 'var(--text-dim)';
        return;
      }
      balanceText.textContent = 'Checking key balance...';
      try {
        const res = await callBridge('check_openrouter_key', { apiKey: key });
        if (res && res.valid) {
          balanceText.textContent = res.message || 'Key valid';
          balanceText.style.color = 'var(--text-main)';
        } else {
          balanceText.textContent = res?.message || 'Invalid or unverified key';
          balanceText.style.color = 'var(--accent-red)';
        }
      } catch (err) {
        balanceText.textContent = 'Check failed: ' + (err.message || err);
        balanceText.style.color = 'var(--text-dim)';
      }
    }

    async function loadFreeModelsCount() {
      try {
        const res = await callBridge('get_openrouter_free_models', { force: false });
        if (res && res.models && modelsCountText) {
          modelsCountText.textContent = `${res.models.length} free models ready`;
        }
      } catch { }
    }

    function onProviderSelected(provId) {
      const p = providersMap[provId];
      if (!p) return;
      const baseUrl = p.baseUrl || p.BaseUrl || '';
      const apiKeys = p.apiKeys || p.ApiKeys || (p.apiKey || p.ApiKey ? [p.apiKey || p.ApiKey] : []);
      const isPreset = !!(p.isPreset !== undefined ? p.isPreset : (p.IsPreset !== undefined ? p.IsPreset : provId.startsWith('preset_')));
      if (baseUrlInput) baseUrlInput.value = baseUrl;
      renderApiKeyRows(apiKeys);
      if (btnDeleteProvider) {
        btnDeleteProvider.style.display = isPreset ? 'none' : 'inline-block';
      }
      const enabled = injectionCheckbox ? injectionCheckbox.checked : true;
      const hasKey = apiKeys.length > 0 && !!apiKeys[0];
      updateBadge(enabled, hasKey);
      if (hasKey) {
        checkKeyHealth(apiKeys[0]);
      } else if (balanceText) {
        balanceText.textContent = 'Ready (No key set)';
        balanceText.style.color = 'var(--text-dim)';
      }
    }

    function populateProviders(providers, selectedId) {
      providersMap = {};
      if (!providerSelect) return;
      providerSelect.innerHTML = '';
      (providers || []).forEach(p => {
        const id = p.id || p.Id;
        const name = p.name || p.Name || id;
        providersMap[id] = p;
        const opt = document.createElement('option');
        opt.value = id;
        opt.textContent = name;
        if (id === selectedId) opt.selected = true;
        providerSelect.appendChild(opt);
      });
      const activeId = selectedId || (providers && providers[0] ? (providers[0].id || providers[0].Id) : 'preset_local_proxy');
      providerSelect.value = activeId;
      onProviderSelected(activeId);
    }

    // Provider select change
    providerSelect?.addEventListener('change', async () => {
      const provId = providerSelect.value;
      onProviderSelected(provId);
      try {
        await callBridge('select_claude_gateway_provider', { providerId: provId });
      } catch (err) {
        console.warn('Select provider error:', err);
      }
    });

    // Master injection toggle
    injectionCheckbox?.addEventListener('change', async () => {
      const enabled = injectionCheckbox.checked;
      try {
        const res = await callBridge('toggle_claude_injection', { enabled });
        const curProv = providersMap[providerSelect ? providerSelect.value : ''];
        const curKey = curProv ? (curProv.apiKey || curProv.ApiKey || '') : '';
        updateBadge(enabled, !!curKey);
        showToast(enabled ? 'Claude environment injection enabled' : 'Stock Anthropic mode active (injection disabled)', 'info', 3000);
      } catch (err) {
        showToast('Failed to toggle injection: ' + err.message, 'error', 3500);
      }
    });

    // Add Custom Provider
    btnAddProvider?.addEventListener('click', async (e) => {
      e.stopPropagation();
      const name = prompt('Enter a name for the new Gateway Provider (e.g. Groq Gateway, TokenHarbor):');
      if (!name || !name.trim()) return;
      try {
        const res = await callBridge('add_claude_gateway_provider', {
          name: name.trim(),
          baseUrl: 'https://openrouter.ai/api/v1',
          apiKey: ''
        });
        if (res && res.provider) {
          const newProv = res.provider;
          const newId = newProv.id || newProv.Id;
          const newName = newProv.name || newProv.Name || newId;
          providersMap[newId] = newProv;
          const opt = document.createElement('option');
          opt.value = newId;
          opt.textContent = newName;
          opt.selected = true;
          providerSelect.appendChild(opt);
          providerSelect.value = newId;
          onProviderSelected(newId);
          showToast(`Provider "${newName}" added`, 'success', 3000);
        }
      } catch (err) {
        showToast('Add provider failed: ' + err.message, 'error', 3500);
      }
    });

    // Delete Custom Provider
    btnDeleteProvider?.addEventListener('click', async (e) => {
      e.stopPropagation();
      const provId = providerSelect.value;
      const p = providersMap[provId];
      const isPreset = p ? !!(p.isPreset !== undefined ? p.isPreset : p.IsPreset) : provId.startsWith('preset_');
      if (!p || isPreset) return;
      const provName = p.name || p.Name || provId;
      const confirmed = confirm(`Delete custom provider "${provName}"?`);
      if (!confirmed) return;
      try {
        const res = await callBridge('delete_claude_gateway_provider', { providerId: provId });
        if (res && res.success) {
          delete providersMap[provId];
          const opt = providerSelect.querySelector(`option[value="${provId}"]`);
          if (opt) opt.remove();
          const nextId = res.selectedGatewayId || 'preset_local_proxy';
          providerSelect.value = nextId;
          onProviderSelected(nextId);
          showToast(`Provider "${provName}" deleted`, 'info', 3000);
        }
      } catch (err) {
        showToast('Delete provider failed: ' + err.message, 'error', 3500);
      }
    });

    // Save Provider
    btnSaveProvider?.addEventListener('click', async (e) => {
      e.stopPropagation();
      const provId = providerSelect ? providerSelect.value : 'preset_local_proxy';
      const p = providersMap[provId];
      const provName = p ? (p.name || p.Name || provId) : provId;
      const baseUrl = baseUrlInput?.value?.trim() || '';
      const apiKeys = getEnteredApiKeys();
      const primaryKey = apiKeys[0] || '';

      btnSaveProvider.disabled = true;
      try {
        const res = await callBridge('save_claude_gateway_config', {
          providerId: provId,
          name: provName,
          baseUrl: baseUrl,
          apiKey: primaryKey,
          apiKeys: apiKeys
        });
        if (res && res.success) {
          if (p) {
            p.baseUrl = baseUrl;
            p.apiKey = primaryKey;
            p.apiKeys = apiKeys;
            p.BaseUrl = baseUrl;
            p.ApiKey = primaryKey;
            p.ApiKeys = apiKeys;
          }
          showToast(`Saved ${apiKeys.length} API key(s) for ${provName}`, 'success', 3000);
          const enabled = injectionCheckbox ? injectionCheckbox.checked : true;
          updateBadge(enabled, apiKeys.length > 0);
          if (primaryKey && (baseUrl.includes('openrouter.ai') || baseUrl.includes('127.0.0.1:9230') || provId.startsWith('preset_'))) {
            await checkKeyHealth(primaryKey);
          } else if (balanceText) {
            balanceText.textContent = apiKeys.length > 0 ? `${apiKeys.length} key(s) configured` : 'Ready (No key set)';
            balanceText.style.color = 'var(--text-dim)';
          }
        }
      } catch (err) {
        showToast('Save failed: ' + err.message, 'error', 3500);
      } finally {
        btnSaveProvider.disabled = false;
      }
    });

    // Fetch free models live
    btnFetchModels?.addEventListener('click', async () => {
      const svg = btnFetchModels.querySelector('svg');
      if (svg) svg.classList.add('spinning');
      btnFetchModels.disabled = true;
      try {
        const res = await callBridge('refresh_openrouter_free_models');
        if (res && res.models) {
          if (modelsCountText) modelsCountText.textContent = `${res.models.length} free models available`;
          showToast(`Fetched ${res.models.length} free models from OpenRouter`, 'success', 3500);
        }
      } catch (err) {
        showToast('Fetch failed: ' + err.message, 'error', 3500);
      } finally {
        if (svg) svg.classList.remove('spinning');
        btnFetchModels.disabled = false;
      }
    });

    // Claude /logout
    btnLogout?.addEventListener('click', async () => {
      const confirmed = confirm('This will run "claude /logout" to clear stored Anthropic login tokens and prevent OAuth token precedence conflicts. Proceed?');
      if (!confirmed) return;

      btnLogout.disabled = true;
      try {
        const res = await callBridge('run_claude_logout');
        if (res && res.success) {
          showToast('Claude login credentials cleared.', 'success', 4000);
          appendLog('[Claude] Cleared official Anthropic credentials via /logout.');
        } else {
          showToast(res?.message || 'Logout failed', 'error', 3500);
        }
      } catch (err) {
        showToast('Logout error: ' + err.message, 'error', 3500);
      } finally {
        btnLogout.disabled = false;
      }
    });

    // Initial load
    callBridge('get_claude_gateway_config').then(res => {
      if (res) {
        if (injectionCheckbox) {
          injectionCheckbox.checked = res.injectionEnabled !== false;
        }
        populateProviders(res.providers || [], res.selectedGatewayId || 'preset_local_proxy');
        const activeProv = res.activeProvider || (res.providers && res.providers[0]);
        const activeKey = activeProv ? (activeProv.apiKey || activeProv.ApiKey || '') : '';
        updateBadge(res.injectionEnabled !== false, !!activeKey);
      }
    }).catch(err => {
      console.warn('Failed to load claude gateway config:', err);
    });

    updateCollapseUi();
    loadFreeModelsCount();
  }

  // ==========================================================================
  // Project Workspaces & Context Profiles
  // ==========================================================================
  const WS_PRESET_COLORS = ['#06b6d4', '#10b981', '#6366f1', '#8b5cf6', '#f59e0b', '#ec4899', '#3b82f6'];

window.setupClaudeGatewayConfig = setupClaudeGatewayConfig;
