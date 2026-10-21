  function renderProvidersList() {
    const listEl = document.getElementById('providers-sidebar-list');
    if (!listEl) return;

    listEl.innerHTML = '';
    const providers = state.aiConfig.providers || [];

    providers.forEach(p => {
      const item = document.createElement('div');
      item.className = 'nav-item ' + (state.selectedProvider && state.selectedProvider.id === p.id ? 'active' : '');
      item.style.padding = '8px 10px';
      item.innerHTML = `
        <span style="color: ${p.isEnabled ? 'var(--accent-green)' : 'var(--text-dim)'}; font-size: 14px;">${p.isEnabled ? '●' : '○'}</span>
        <span style="flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${p.name}</span>
      `;
      item.onclick = () => selectProvider(p);
      listEl.appendChild(item);
    });

    if (!state.selectedProvider && providers.length > 0) {
      selectProvider(providers[0]);
    }
  }

  function selectProvider(prov) {
    state.selectedProvider = prov;
    renderProvidersList();

    const nameInput = document.getElementById('prov-name-input');
    const enabledCheck = document.getElementById('prov-enabled-checkbox');
    const urlInput = document.getElementById('prov-url-input');
    const keyInput = document.getElementById('prov-key-input');
    const feedback = document.getElementById('prov-save-feedback');

    if (nameInput) nameInput.value = prov.name;
    if (enabledCheck) enabledCheck.checked = prov.isEnabled;
    if (urlInput) urlInput.value = prov.baseUrl;
    if (keyInput) keyInput.value = prov.apiKey;
    if (feedback) feedback.textContent = '';

    renderProviderModels(prov);
  }

  function renderProviderModels(prov) {
    const list = document.getElementById('prov-models-list');
    if (!list) return;

    list.innerHTML = '';
    const models = prov.models || [];

    models.forEach(m => {
      const tag = document.createElement('div');
      tag.style = 'display: flex; justify-content: space-between; align-items: center; padding: 4px 8px; background: rgba(255,255,255,0.03); border-radius: 4px; margin-bottom: 4px; font-size: 12px; font-family: var(--font-mono);';
      tag.innerHTML = `
        <span>${m}</span>
        <button class="btn-icon btn-del-model" data-model="${m}" style="padding: 1px 6px; color: var(--accent-red);">×</button>
      `;
      list.appendChild(tag);
    });

    list.querySelectorAll('.btn-del-model').forEach(b => {
      b.onclick = () => {
        prov.models = prov.models.filter(x => x !== b.dataset.model);
        renderProviderModels(prov);
      };
    });
  }

  function setupProvidersView() {
    document.getElementById('btn-add-model')?.addEventListener('click', () => {
      if (!state.selectedProvider) return;
      const inp = document.getElementById('prov-new-model-input');
      const val = inp.value.trim();
      if (val && !state.selectedProvider.models.includes(val)) {
        state.selectedProvider.models.push(val);
        renderProviderModels(state.selectedProvider);
        inp.value = '';
      }
    });

    document.getElementById('btn-add-provider')?.addEventListener('click', () => {
      const newProv = {
        id: 'prov_' + Math.random().toString(36).substring(2, 9),
        name: 'New Provider',
        baseUrl: 'https://api.openai.com/v1',
        apiKey: '',
        isEnabled: true,
        models: ['gpt-4o-mini']
      };
      state.aiConfig.providers.push(newProv);
      selectProvider(newProv);
    });

    document.getElementById('btn-fetch-models')?.addEventListener('click', async () => {
      if (!state.selectedProvider) return;
      const btn = document.getElementById('btn-fetch-models');
      const feedback = document.getElementById('prov-save-feedback');
      btn.disabled = true;
      feedback.textContent = 'Fetching models from API...';
      feedback.style.color = 'var(--text-muted)';

      try {
        const res = await callBridge('fetch_models', { providerId: state.selectedProvider.id });
        state.selectedProvider.models = res.models || state.selectedProvider.models;
        renderProviderModels(state.selectedProvider);
        feedback.textContent = `Fetched ${res.total} models (+${res.added} new added)!`;
        feedback.style.color = 'var(--accent-green)';
      } catch (err) {
        feedback.textContent = 'Failed: ' + err.message;
        feedback.style.color = 'var(--accent-red)';
      } finally {
        btn.disabled = false;
      }
    });

    document.querySelectorAll('.prov-preset-btn').forEach(b => {
      b.addEventListener('click', () => {
        if (!state.selectedProvider) return;
        const p = b.dataset.preset;
        const presets = {
          'OpenRouter': { url: 'https://openrouter.ai/api/v1', models: ['anthropic/claude-3.5-sonnet', 'deepseek/deepseek-chat', 'openai/gpt-4o-mini'] },
          'OpenAI': { url: 'https://api.openai.com/v1', models: ['gpt-4o', 'gpt-4o-mini', 'o3-mini'] },
          'Groq': { url: 'https://api.groq.com/openai/v1', models: ['llama-3.3-70b-versatile', 'deepseek-r1-distill-llama-70b'] },
          'DeepSeek': { url: 'https://api.deepseek.com/v1', models: ['deepseek-chat', 'deepseek-reasoner'] },
          'Ollama': { url: 'http://localhost:11434/v1', models: ['llama3.2', 'qwen2.5-coder'] }
        };
        if (presets[p]) {
          document.getElementById('prov-name-input').value = p;
          document.getElementById('prov-url-input').value = presets[p].url;
          state.selectedProvider.name = p;
          state.selectedProvider.baseUrl = presets[p].url;
          presets[p].models.forEach(m => {
            if (!state.selectedProvider.models.includes(m)) state.selectedProvider.models.push(m);
          });
          renderProviderModels(state.selectedProvider);
        }
      });
    });

    document.getElementById('btn-save-current-provider')?.addEventListener('click', async () => {
      if (!state.selectedProvider) return;
      const feedback = document.getElementById('prov-save-feedback');

      state.selectedProvider.name = document.getElementById('prov-name-input').value.trim();
      state.selectedProvider.isEnabled = document.getElementById('prov-enabled-checkbox').checked;
      state.selectedProvider.baseUrl = document.getElementById('prov-url-input').value.trim();
      state.selectedProvider.apiKey = document.getElementById('prov-key-input').value.trim();

      await callBridge('save_ai_config', { config: state.aiConfig });
      renderProvidersList();
      renderPromptModelsDropdown();

      feedback.textContent = 'Provider saved successfully!';
      feedback.style.color = 'var(--accent-green)';
      setTimeout(() => feedback.textContent = '', 2500);
    });

    document.getElementById('btn-toggle-key-view')?.addEventListener('click', () => {
      const inp = document.getElementById('prov-key-input');
      inp.type = inp.type === 'password' ? 'text' : 'password';
    });
  }

  // Feature 8: Centralized Structured Logging System & Execution Inspector
  state.logs = [];
  state.currentLogFilter = 'all';
  state.logSearchQuery = '';
  state.logAutoScroll = true;
  state.currentPrompt = null;
  let logIdCounter = 0;

window.setupProvidersView = setupProvidersView;

window.renderProvidersList = renderProvidersList;
var renderProvidersList = window.renderProvidersList;
window.selectProvider = selectProvider;
var selectProvider = window.selectProvider;
