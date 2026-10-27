  var escapeHtml = window.escapeHtml || function(s) {
    if (!s) return '';
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  };

  function renderWorkspacesDropdown(workspaces, activeId) {
    const list = (workspaces && workspaces.length > 0) ? workspaces : (state.workspaces || []);
    const effectiveActiveId = activeId || state.activeWorkspaceId;
    const activeWs = list.find(w => w.id === effectiveActiveId) || list[0] || null;

    // Header active badge in Profiles view
    const badge = document.getElementById('active-workspace-status-badge');
    if (badge) {
      badge.textContent = activeWs ? activeWs.name : 'Default Workspace';
    }

    // Header count badge
    const countBadge = document.getElementById('workspaces-count-badge');
    if (countBadge) {
      countBadge.textContent = `${list.length} Workspace${list.length === 1 ? '' : 's'}`;
    }

    // Grid in Profiles view
    const grid = document.getElementById('profiles-workspaces-grid');
    if (grid) {
      grid.innerHTML = '';
      if (list.length === 0) {
        grid.innerHTML = '<div style="color: var(--text-dim); font-size: 12px; grid-column: 1 / -1; padding: 12px 0;">No workspaces created yet. Click "New Workspace" to create one.</div>';
      } else {
        list.forEach(ws => {
          const isActive = ws.id === effectiveActiveId;
          const card = document.createElement('div');
          card.className = 'ws-item-card' + (isActive ? ' active' : '');
          
          const color = ws.colorTag || '#06b6d4';
          card.style.setProperty('--ws-color', color);

          const profileText = ws.antigravityProfile ? ws.antigravityProfile : 'Current Profile';
          const hasCustomDir = !!ws.workingDirectory;
          const dirText = hasCustomDir ? ws.workingDirectory : 'Default (Isolated VirtualHome)';
          let dirBaseName = 'VirtualHome';
          if (hasCustomDir) {
            const clean = ws.workingDirectory.replace(/[\\/]+$/, '');
            dirBaseName = clean.split(/[\\/]/).pop() || clean;
          }

          const isSplit = !!ws.splitMode && ws.splitMode !== 'single';
          const splitText = isSplit
            ? `${ws.splitMode === 'vertical' ? 'Vertical' : 'Horizontal'} split (${ws.primaryCli} + ${ws.secondaryCli})`
            : `Single (${ws.primaryCli})`;

          card.innerHTML = `
            <div>
              <div class="ws-item-title">
                <div style="display: flex; align-items: center; gap: 7px; min-width: 0;">
                  <svg class="icon-svg" style="width: 15px; height: 15px; color: ${color}; flex-shrink: 0;" viewBox="0 0 24 24"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>
                  <span style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${escapeHtml(ws.name || ws.id)}</span>
                </div>
                ${isActive ? '<span style="font-size: 10px; background: rgba(6, 182, 212, 0.18); color: var(--accent-cyan); padding: 1px 7px; border-radius: 4px; font-weight: 700; display: inline-flex; align-items: center; gap: 4px; flex-shrink: 0;"><span style="display: inline-block; width: 6px; height: 6px; border-radius: 50%; background: var(--accent-cyan); box-shadow: 0 0 6px var(--accent-cyan);"></span>ACTIVE</span>' : ''}
              </div>
              <div class="ws-item-dir-box" title="${escapeHtml(dirText)}">
                <div style="display: flex; align-items: center; gap: 5px; min-width: 0; flex: 1;">
                  <span style="font-size: 11px; font-weight: 600; color: var(--text-main); flex-shrink: 0;">${escapeHtml(dirBaseName)}</span>
                  <span class="ws-item-dir">${escapeHtml(hasCustomDir ? dirText : 'VirtualHome')}</span>
                </div>
                ${hasCustomDir ? `<button class="btn-ws-open-dir" type="button" title="Open folder in File Explorer" data-path="${escapeHtml(ws.workingDirectory)}"><svg class="icon-svg" style="width: 12px; height: 12px;" viewBox="0 0 24 24"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg></button>` : ''}
              </div>
              <div class="ws-item-meta">
                <span class="ws-item-chip" title="Bound Antigravity Profile">
                  <svg class="icon-svg" viewBox="0 0 24 24"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
                  <span>${escapeHtml(profileText)}</span>
                </span>
                <span class="ws-item-chip" title="Terminal layout">
                  <svg class="icon-svg" viewBox="0 0 24 24"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/></svg>
                  <span>${escapeHtml(splitText)}</span>
                </span>
              </div>
            </div>
            <div class="ws-item-actions">
              ${!isActive ? `<button class="btn btn-outline btn-ws-activate" data-id="${escapeHtml(ws.id)}" type="button" style="padding: 3px 9px; font-size: 11px;">Activate</button>` : ''}
              <button class="btn btn-outline btn-ws-edit" data-id="${escapeHtml(ws.id)}" type="button" style="padding: 3px 9px; font-size: 11px;">Edit</button>
              ${!ws.isDefault ? `<button class="btn btn-danger btn-ws-delete" data-id="${escapeHtml(ws.id)}" type="button" style="padding: 3px 9px; font-size: 11px;">Delete</button>` : ''}
            </div>
          `;

          // Clicking card body activates if not active
          card.addEventListener('click', (e) => {
            if (e.target.closest('button')) return;
            if (!isActive) switchWorkspace(ws.id);
          });

          card.querySelector('.btn-ws-open-dir')?.addEventListener('click', (e) => {
            e.stopPropagation();
            if (ws.workingDirectory) {
              callBridge('open_folder', { path: ws.workingDirectory });
            }
          });

          card.querySelector('.btn-ws-activate')?.addEventListener('click', (e) => {
            e.stopPropagation();
            switchWorkspace(ws.id);
          });

          card.querySelector('.btn-ws-edit')?.addEventListener('click', (e) => {
            e.stopPropagation();
            openWorkspaceModal(ws.id);
          });

          card.querySelector('.btn-ws-delete')?.addEventListener('click', (e) => {
            e.stopPropagation();
            deleteWorkspaceDirectly(ws.id, ws.name);
          });

          grid.appendChild(card);
        });
      }
    }
  }

  async function deleteWorkspaceDirectly(id, name) {
    if (!id) return;
    if (!confirm(`Are you sure you want to delete workspace '${name || id}'?`)) return;
    try {
      const res = await callBridge('delete_workspace', { id });
      if (res && res.success) {
        if (res.workspaces) {
          state.workspaces = res.workspaces;
          state.activeWorkspaceId = res.activeWorkspaceId;
          renderWorkspacesDropdown(state.workspaces, state.activeWorkspaceId);
        }
        showToast('Workspace deleted', 'info');
      }
    } catch (err) {
      alert('Failed to delete workspace: ' + err.message);
    }
  }

  function openWorkspaceModal(workspaceId = null) {
    const modal = document.getElementById('modal-workspace');
    if (!modal) return;

    const titleEl = document.getElementById('modal-ws-title');
    const idInput = document.getElementById('modal-ws-id');
    const nameInput = document.getElementById('modal-ws-name');
    const dirInput = document.getElementById('modal-ws-dir');
    const profileSelect = document.getElementById('modal-ws-profile');
    const primaryCliSelect = document.getElementById('modal-ws-primary-cli');
    const secondaryCliSelect = document.getElementById('modal-ws-secondary-cli');
    const splitModeSelect = document.getElementById('modal-ws-split-mode');
    const splitRatioSelect = document.getElementById('modal-ws-split-ratio');
    const deleteBtn = document.getElementById('btn-delete-ws-modal');

    // Populate profiles dropdown
    if (profileSelect) {
      profileSelect.innerHTML = '';
      const defOpt = document.createElement('option');
      defOpt.value = '';
      defOpt.textContent = 'Keep Current Profile';
      profileSelect.appendChild(defOpt);

      if (state.antigravity && state.antigravity.profiles) {
        state.antigravity.profiles.forEach(p => {
          const opt = document.createElement('option');
          opt.value = p.name || p;
          opt.textContent = p.name || p;
          profileSelect.appendChild(opt);
        });
      }
    }

    let ws = null;
    if (workspaceId && state.workspaces) {
      ws = state.workspaces.find(w => w.id === workspaceId);
    }

    const currentColor = (ws && ws.colorTag) ? ws.colorTag : '#06b6d4';
    const colorInput = document.getElementById('modal-ws-color');
    if (colorInput) colorInput.value = currentColor;

    const paletteEl = document.getElementById('modal-ws-color-palette');
    if (paletteEl) {
      paletteEl.innerHTML = '';
      WS_PRESET_COLORS.forEach(hex => {
        const swatch = document.createElement('div');
        swatch.className = 'ws-color-swatch' + (hex.toLowerCase() === currentColor.toLowerCase() ? ' selected' : '');
        swatch.style.backgroundColor = hex;
        swatch.title = hex;
        swatch.addEventListener('click', () => {
          paletteEl.querySelectorAll('.ws-color-swatch').forEach(s => s.classList.remove('selected'));
          swatch.classList.add('selected');
          if (colorInput) colorInput.value = hex;
        });
        paletteEl.appendChild(swatch);
      });
    }

    if (ws) {
      if (titleEl) titleEl.textContent = 'Edit Project Workspace';
      if (idInput) idInput.value = ws.id;
      if (nameInput) nameInput.value = ws.name || '';
      if (dirInput) dirInput.value = ws.workingDirectory || '';
      if (profileSelect) profileSelect.value = ws.antigravityProfile || '';
      if (primaryCliSelect) primaryCliSelect.value = ws.primaryCli || 'Hermes';
      if (secondaryCliSelect) secondaryCliSelect.value = ws.secondaryCli || 'none';
      if (splitModeSelect) splitModeSelect.value = ws.splitMode || 'single';
      if (splitRatioSelect) splitRatioSelect.value = String(ws.splitRatio || 0.5);
      if (deleteBtn) {
        deleteBtn.classList.toggle('hidden', !!ws.isDefault);
      }
    } else {
      if (titleEl) titleEl.textContent = 'New Project Workspace';
      if (idInput) idInput.value = '';
      if (nameInput) nameInput.value = '';
      if (dirInput) dirInput.value = '';
      if (profileSelect) profileSelect.value = '';
      if (primaryCliSelect) primaryCliSelect.value = 'Hermes';
      if (secondaryCliSelect) secondaryCliSelect.value = 'none';
      if (splitModeSelect) splitModeSelect.value = 'single';
      if (splitRatioSelect) splitRatioSelect.value = '0.5';
      if (deleteBtn) deleteBtn.classList.add('hidden');
    }

    // "open" is the class .modal-overlay is styled for, the same one every other modal uses.
    // "active" matched no rule, so the dialog stayed at display:none and every workspace button
    // looked dead.
    modal.classList.add('open');
    if (nameInput) setTimeout(() => nameInput.focus(), 60);
  }

  function closeWorkspaceModal() {
    const modal = document.getElementById('modal-workspace');
    if (modal) modal.classList.remove('open');
  }

  async function saveWorkspaceFromModal() {
    const idInput = document.getElementById('modal-ws-id');
    const nameInput = document.getElementById('modal-ws-name');
    const dirInput = document.getElementById('modal-ws-dir');
    const colorInput = document.getElementById('modal-ws-color');
    const profileSelect = document.getElementById('modal-ws-profile');
    const primaryCliSelect = document.getElementById('modal-ws-primary-cli');
    const secondaryCliSelect = document.getElementById('modal-ws-secondary-cli');
    const splitModeSelect = document.getElementById('modal-ws-split-mode');
    const splitRatioSelect = document.getElementById('modal-ws-split-ratio');

    const name = nameInput?.value.trim();
    if (!name) {
      alert('Workspace name is required.');
      return;
    }

    const payload = {
      workspace: {
        id: idInput?.value || undefined,
        name: name,
        colorTag: colorInput?.value || '#06b6d4',
        workingDirectory: dirInput?.value.trim() || '',
        antigravityProfile: profileSelect?.value || '',
        primaryCli: primaryCliSelect?.value || 'Hermes',
        secondaryCli: secondaryCliSelect?.value || 'none',
        splitMode: splitModeSelect?.value || 'single',
        splitRatio: parseFloat(splitRatioSelect?.value || '0.5')
      }
    };

    try {
      const res = await callBridge('save_workspace', payload);
      if (res && res.success) {
        closeWorkspaceModal();
        if (res.workspaces) {
          state.workspaces = res.workspaces;
          state.activeWorkspaceId = res.activeWorkspaceId;
          renderWorkspacesDropdown(state.workspaces, state.activeWorkspaceId);
        }
        showToast(`Workspace '${name}' saved`, 'success');
      }
    } catch (err) {
      alert('Failed to save workspace: ' + err.message);
    }
  }

  async function deleteWorkspaceFromModal() {
    const idInput = document.getElementById('modal-ws-id');
    const id = idInput?.value;
    if (!id) return;

    if (!confirm('Are you sure you want to delete this workspace?')) return;

    try {
      const res = await callBridge('delete_workspace', { id });
      if (res && res.success) {
        closeWorkspaceModal();
        if (res.workspaces) {
          state.workspaces = res.workspaces;
          state.activeWorkspaceId = res.activeWorkspaceId;
          renderWorkspacesDropdown(state.workspaces, state.activeWorkspaceId);
        }
        showToast('Workspace deleted', 'info');
      }
    } catch (err) {
      alert('Failed to delete workspace: ' + err.message);
    }
  }

  async function switchWorkspace(workspaceId) {
    if (!workspaceId) return;
    if (workspaceId === state.activeWorkspaceId) {
      showToast('That workspace is already active.', 'info');
      return;
    }
    try {
      const res = await callBridge('activate_workspace', { id: workspaceId });
      if (res && res.success) {
        state.activeWorkspaceId = workspaceId;
        window.refreshAgentRules?.();
      }
    } catch (err) {
      showToast('Workspace switch error: ' + err.message, 'error');
    }
  }

  // ==========================================================================
  // Token & Cost Analytics Engine
  // ==========================================================================
  function renderTokenTelemetry(data) {
    if (!data) return;

    // Quick summary pills inside Prompt Enhancer
    const quickTokens = document.getElementById('quick-tokens-val');
    const quickSavings = document.getElementById('quick-savings-val');
    if (quickTokens) quickTokens.textContent = Number(data.totalTokens || 0).toLocaleString();
    if (quickSavings) quickSavings.textContent = '$' + Number(data.estimatedSavingsDollars || 0).toFixed(2);

    // KPI Cards
    const totalTokensEl = document.getElementById('telem-total-tokens');
    const breakdownEl = document.getElementById('telem-token-breakdown');
    const savingsEl = document.getElementById('telem-estimated-savings');
    const reqRateEl = document.getElementById('telem-req-rate');
    const totalReqsEl = document.getElementById('telem-total-requests');

    if (totalTokensEl) totalTokensEl.textContent = Number(data.totalTokens || 0).toLocaleString();
    if (breakdownEl) {
      const pT = Number(data.promptTokens || 0).toLocaleString();
      const cT = Number(data.completionTokens || 0).toLocaleString();
      breakdownEl.textContent = `Prompt: ${pT} | Completion: ${cT}`;
    }
    if (savingsEl) {
      savingsEl.textContent = '$' + Number(data.estimatedSavingsDollars || 0).toFixed(2);
    }
    if (reqRateEl) {
      reqRateEl.innerHTML = `${data.requestsPerMinute || 0} <span style="font-size: 13px; font-weight: normal; color: var(--text-muted);">req/min</span>`;
    }
    if (totalReqsEl) {
      totalReqsEl.textContent = `Total Requests: ${Number(data.totalRequests || 0).toLocaleString()}`;
    }

    // Render Throughput SVG Bar Chart
    renderTelemetryChart(data.timeSeries || []);

    // Render Model Distribution Bars
    renderTelemetryModels(data.modelsBreakdown || []);
  }

  function renderTelemetryChart(timeSeries) {
    const svg = document.getElementById('telem-throughput-svg');
    if (!svg) return;

    svg.innerHTML = '';
    const width = 600;
    const height = 130;
    const paddingBottom = 22;
    const chartHeight = height - paddingBottom;

    if (!timeSeries || timeSeries.length === 0) {
      const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
      text.setAttribute('x', String(width / 2));
      text.setAttribute('y', String(height / 2));
      text.setAttribute('text-anchor', 'middle');
      text.setAttribute('fill', '#71717a');
      text.setAttribute('font-size', '12');
      text.textContent = 'Waiting for proxy and prompt activity...';
      svg.appendChild(text);
      return;
    }

    let maxTokens = 0;
    timeSeries.forEach(b => {
      if (b.tokens > maxTokens) maxTokens = b.tokens;
    });
    if (maxTokens <= 0) maxTokens = 100;

    const count = timeSeries.length;
    const barWidth = Math.max(14, (width - (count * 6)) / count);
    const gap = (width - (count * barWidth)) / (count + 1);

    timeSeries.forEach((bucket, i) => {
      const x = gap + i * (barWidth + gap);
      const ratio = bucket.tokens / maxTokens;
      const barH = Math.max(3, ratio * (chartHeight - 14));
      const y = chartHeight - barH;

      const rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
      rect.setAttribute('x', String(x));
      rect.setAttribute('y', String(y));
      rect.setAttribute('width', String(barWidth));
      rect.setAttribute('height', String(barH));
      rect.setAttribute('class', 'telem-bar');

      const title = document.createElementNS('http://www.w3.org/2000/svg', 'title');
      title.textContent = `${bucket.timeLabel}: ${bucket.tokens.toLocaleString()} tokens (${bucket.requests} requests)`;
      rect.appendChild(title);
      svg.appendChild(rect);

      // Label every 3rd or 4th bucket
      if (i % 3 === 0 || i === count - 1) {
        const text = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        text.setAttribute('x', String(x + barWidth / 2));
        text.setAttribute('y', String(height - 4));
        text.setAttribute('text-anchor', 'middle');
        text.setAttribute('fill', '#71717a');
        text.setAttribute('font-size', '9.5');
        text.setAttribute('font-family', 'monospace');
        text.textContent = bucket.timeLabel || '';
        svg.appendChild(text);
      }
    });
  }

  function renderTelemetryModels(models) {
    const container = document.getElementById('telem-models-list');
    if (!container) return;

    if (!models || models.length === 0) {
      container.innerHTML = '<div style="font-size: 12px; color: var(--text-dim); text-align: center; padding: 24px 0;">No model telemetry recorded yet. Requests made via proxy or prompt enhancer will appear here.</div>';
      return;
    }

    container.innerHTML = '';
    models.forEach(m => {
      const row = document.createElement('div');
      row.className = 'telem-model-row';

      const info = document.createElement('div');
      info.className = 'telem-model-info';

      const name = document.createElement('span');
      name.className = 'telem-model-name';
      name.textContent = m.model;

      const stats = document.createElement('span');
      stats.className = 'telem-model-stats';
      stats.textContent = `${Number(m.tokens || 0).toLocaleString()} tokens (${m.percentage}%)`;

      info.appendChild(name);
      info.appendChild(stats);

      const track = document.createElement('div');
      track.className = 'telem-bar-track';

      const fill = document.createElement('div');
      fill.className = 'telem-bar-fill';
      fill.style.width = Math.max(3, Math.min(100, m.percentage || 0)) + '%';

      track.appendChild(fill);
      row.appendChild(info);
      row.appendChild(track);
      container.appendChild(row);
    });
  }

  function updateWindowHotkeyPreview() {
    const winCheckbox = document.getElementById('setting-global-hotkey-checkbox');
    const winModifier = document.getElementById('setting-window-modifier');
    const winKey = document.getElementById('setting-window-key');
    const winPreview = document.getElementById('window-hotkey-preview');
    const winOptions = document.getElementById('window-hotkey-options');

    const enabled = winCheckbox ? winCheckbox.checked : true;
    if (winOptions) {
      winOptions.style.opacity = enabled ? '1' : '0.4';
      winOptions.style.pointerEvents = enabled ? 'auto' : 'none';
    }
    if (!winPreview) return;
    if (!enabled) {
      winPreview.textContent = 'Hotkey Disabled';
      winPreview.style.color = 'var(--text-dim)';
      return;
    }
    const mod = winModifier?.value || 'Ctrl+Alt';
    const key = winKey?.value || 'H';
    const keyText = key === 'Oem3' ? '~ (Tilde)' : key;
    const comboText = mod === 'None' ? keyText : `${mod} + ${keyText}`;
    winPreview.textContent = `Active Hotkey: ${comboText}`;
    winPreview.style.color = 'var(--accent-cyan)';
  }

  function updateHotkeyPreview() {
    const hudCheckbox = document.getElementById('setting-global-hud-hotkey-checkbox');
    const hudModifier = document.getElementById('setting-quake-modifier');
    const hudKey = document.getElementById('setting-quake-key');
    const hudPreview = document.getElementById('quake-hotkey-preview');
    const hudOptions = document.getElementById('quake-hotkey-options');

    const enabled = hudCheckbox ? hudCheckbox.checked : true;
    if (hudOptions) {
      hudOptions.style.opacity = enabled ? '1' : '0.4';
      hudOptions.style.pointerEvents = enabled ? 'auto' : 'none';
    }
    if (!hudPreview) return;
    if (!enabled) {
      hudPreview.textContent = 'Hotkey Disabled';
      hudPreview.style.color = 'var(--text-dim)';
      return;
    }
    const mod = hudModifier?.value || 'None';
    const key = hudKey?.value || 'F12';
    const keyText = key === 'Oem3' ? '~ (Tilde)' : key;
    const comboText = mod === 'None' ? keyText : `${mod} + ${keyText}`;
    hudPreview.textContent = `Active Hotkey: ${comboText}`;
    hudPreview.style.color = 'var(--accent-cyan)';
  }

  function setupWorkspaces() {
    // Collapsible Project Workspaces Card in Profiles View
    const wsCardHeader = document.getElementById('btn-toggle-workspaces-card');
    const wsCardBody = document.getElementById('workspaces-card-body');
    const wsCardChevron = document.getElementById('workspaces-card-chevron-text');
    const wsCardIcon = document.getElementById('workspaces-card-chevron-icon');

    function setWorkspacesCardOpen(isOpen) {
      if (wsCardBody) wsCardBody.style.display = isOpen ? '' : 'none';
      if (wsCardChevron) wsCardChevron.textContent = isOpen ? 'Close' : 'Open';
      if (wsCardIcon) wsCardIcon.style.transform = isOpen ? 'rotate(180deg)' : '';
      localStorage.setItem('workspaces_card_open', isOpen ? 'true' : 'false');
    }

    const savedWsOpen = localStorage.getItem('workspaces_card_open');
    setWorkspacesCardOpen(savedWsOpen !== 'false');

    wsCardHeader?.addEventListener('click', () => {
      const isCurrentlyOpen = wsCardBody && wsCardBody.style.display !== 'none';
      setWorkspacesCardOpen(!isCurrentlyOpen);
    });

    document.getElementById('btn-collapse-workspaces-card')?.addEventListener('click', (e) => {
      e.stopPropagation();
      const isCurrentlyOpen = wsCardBody && wsCardBody.style.display !== 'none';
      setWorkspacesCardOpen(!isCurrentlyOpen);
    });

    document.getElementById('btn-profiles-new-workspace')?.addEventListener('click', (e) => {
      e.stopPropagation();
      openWorkspaceModal(null);
    });

    // Workspace Modal Dialog
    document.getElementById('btn-browse-modal-ws-dir')?.addEventListener('click', async () => {
      try {
        const res = await callBridge('browse_directory', { title: 'Select Project Working Directory' });
        if (res && res.path) {
          const dirInput = document.getElementById('modal-ws-dir');
          if (dirInput) dirInput.value = res.path;
        }
      } catch { }
    });

    document.getElementById('btn-save-ws-modal')?.addEventListener('click', saveWorkspaceFromModal);
    document.getElementById('btn-cancel-ws-modal')?.addEventListener('click', closeWorkspaceModal);
    document.getElementById('btn-delete-ws-modal')?.addEventListener('click', deleteWorkspaceFromModal);

    // Same dismissal as the other editors: click the backdrop or press Escape.
    const wsModal = document.getElementById('modal-workspace');
    wsModal?.addEventListener('click', (e) => {
      if (e.target === wsModal) closeWorkspaceModal();
    });
    window.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && wsModal?.classList.contains('open')) closeWorkspaceModal();
    });

    // Main Window Global Hotkey Settings controls
    const winCheckbox = document.getElementById('setting-global-hotkey-checkbox');
    const winModifier = document.getElementById('setting-window-modifier');
    const winKey = document.getElementById('setting-window-key');

    updateWindowHotkeyPreview();

    winCheckbox?.addEventListener('change', () => {
      updateWindowHotkeyPreview();
      callBridge('save_general_settings', {
        settings: {
          globalHotkey: winCheckbox.checked,
          globalHotkeyModifier: winModifier?.value || 'Ctrl+Alt',
          globalHotkeyKey: winKey?.value || 'H'
        }
      });
      showToast(`Global window hotkey ${winCheckbox.checked ? 'enabled' : 'disabled'}`, 'info');
    });

    winModifier?.addEventListener('change', () => {
      updateWindowHotkeyPreview();
      callBridge('save_general_settings', {
        settings: {
          globalHotkey: winCheckbox?.checked !== false,
          globalHotkeyModifier: winModifier.value,
          globalHotkeyKey: winKey?.value || 'H'
        }
      });
    });

    winKey?.addEventListener('change', () => {
      updateWindowHotkeyPreview();
      callBridge('save_general_settings', {
        settings: {
          globalHotkey: winCheckbox?.checked !== false,
          globalHotkeyModifier: winModifier?.value || 'Ctrl+Alt',
          globalHotkeyKey: winKey.value
        }
      });
    });

    document.getElementById('btn-trigger-window-hotkey-settings')?.addEventListener('click', () => {
      callBridge('toggle_main_window');
    });

    // Quake HUD Settings controls
    const hudCheckbox = document.getElementById('setting-global-hud-hotkey-checkbox');
    const hudModifier = document.getElementById('setting-quake-modifier');
    const hudKey = document.getElementById('setting-quake-key');
    const hudTarget = document.getElementById('setting-quake-target');

    updateHotkeyPreview();

    hudCheckbox?.addEventListener('change', () => {
      updateHotkeyPreview();
      callBridge('save_general_settings', {
        settings: {
          globalHudHotkey: hudCheckbox.checked,
          hudHotkeyKey: hudKey?.value || 'F12',
          hudHotkeyModifier: hudModifier?.value || 'None',
          hudHotkeyTarget: hudTarget?.value || 'cliwebview'
        }
      });
      showToast(`Global Quake hotkey ${hudCheckbox.checked ? 'enabled' : 'disabled'}`, 'info');
    });

    hudModifier?.addEventListener('change', () => {
      updateHotkeyPreview();
      callBridge('save_general_settings', {
        settings: {
          globalHudHotkey: hudCheckbox?.checked !== false,
          hudHotkeyKey: hudKey?.value || 'F12',
          hudHotkeyModifier: hudModifier.value,
          hudHotkeyTarget: hudTarget?.value || 'cliwebview'
        }
      });
    });

    hudKey?.addEventListener('change', () => {
      updateHotkeyPreview();
      callBridge('save_general_settings', {
        settings: {
          globalHudHotkey: hudCheckbox?.checked !== false,
          hudHotkeyKey: hudKey.value,
          hudHotkeyModifier: hudModifier?.value || 'None',
          hudHotkeyTarget: hudTarget?.value || 'cliwebview'
        }
      });
    });

    hudTarget?.addEventListener('change', () => {
      callBridge('save_general_settings', {
        settings: {
          globalHudHotkey: hudCheckbox?.checked !== false,
          hudHotkeyKey: hudKey?.value || 'F12',
          hudHotkeyModifier: hudModifier?.value || 'None',
          hudHotkeyTarget: hudTarget.value
        }
      });
      showToast(`Quake toggle target set to ${hudTarget.options[hudTarget.selectedIndex].text}`, 'info');
    });

    document.getElementById('btn-trigger-quake-hud-settings')?.addEventListener('click', () => {
      callBridge('toggle_quake_hud');
    });

    renderWorkspacesDropdown(state.workspaces, state.activeWorkspaceId);
  }

  /* ═══════════════════════════════════════════════════════════════════════════
     Agents Hub v1.0.4 - Mega Features Implementation
     ═══════════════════════════════════════════════════════════════════════════ */

  // Feature 2: Workspace Snapshot & Time-Machine Checkpoints
window.setupWorkspaces = setupWorkspaces;
window.renderWorkspacesDropdown = renderWorkspacesDropdown;
window.renderTokenTelemetry = renderTokenTelemetry;

window.updateWindowHotkeyPreview = updateWindowHotkeyPreview;
var updateWindowHotkeyPreview = window.updateWindowHotkeyPreview;
window.updateHotkeyPreview = updateHotkeyPreview;
var updateHotkeyPreview = window.updateHotkeyPreview;
window.switchWorkspace = switchWorkspace;
var switchWorkspace = window.switchWorkspace;
