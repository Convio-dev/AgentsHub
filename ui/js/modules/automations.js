  function setupScheduledAutomations() {
    const gridEl = document.getElementById('automations-tasks-grid');
    const btnRefresh = document.getElementById('btn-refresh-automations');
    const btnNew = document.getElementById('btn-create-automation-task');
    const btnAddHdr = document.getElementById('btn-add-automation-task');
    const modalAuto = document.getElementById('modal-automation-task');
    const btnCancel = document.getElementById('btn-cancel-auto-task');
    const btnSave = document.getElementById('btn-save-auto-task');

    const inpId = document.getElementById('modal-auto-task-id');
    const inpName = document.getElementById('modal-auto-task-name');
    const selCli = document.getElementById('modal-auto-task-cli');
    const selInterval = document.getElementById('modal-auto-task-interval');
    const customWrap = document.getElementById('modal-auto-task-custom-wrap');
    const inpCron = document.getElementById('modal-auto-task-cron');
    const descText = document.getElementById('modal-auto-schedule-desc-text');
    const txtPrompt = document.getElementById('modal-auto-task-prompt');
    const chkEnabled = document.getElementById('modal-auto-task-enabled');

    let automationsCache = [];

    function formatScheduleDescription(cron) {
      if (!cron) return 'Manual';
      const c = cron.trim();
      if (c === '*/15 * * * *') return 'Every 15 minutes';
      if (c === '*/30 * * * *') return 'Every 30 minutes';
      if (c === '0 * * * *') return 'Hourly (every hour)';
      if (c === '0 */2 * * *') return 'Every 2 hours';
      if (c === '0 */6 * * *') return 'Every 6 hours';
      if (c === '0 9 * * *') return 'Daily at 9:00 AM';
      if (c === '0 0 * * *') return 'Daily at midnight';
      if (c === '0 9 * * 1') return 'Weekly on Monday (9:00 AM)';
      if (c.startsWith('*/')) {
        const mins = c.split(' ')[0].replace('*/', '');
        return `Every ${mins} minutes`;
      }
      return c;
    }

    function updateScheduleDesc() {
      const val = selInterval?.value;
      if (val === 'custom') {
        if (customWrap) customWrap.style.display = 'block';
        if (descText) descText.textContent = `Custom Cron: ${inpCron?.value || '* * * * *'}`;
      } else {
        if (customWrap) customWrap.style.display = 'none';
        if (inpCron) inpCron.value = val;
        if (descText) descText.textContent = `Runs automatically: ${formatScheduleDescription(val)}.`;
      }
    }

    selInterval?.addEventListener('change', updateScheduleDesc);
    inpCron?.addEventListener('input', () => {
      if (selInterval?.value === 'custom' && descText) {
        descText.textContent = `Custom Cron: ${inpCron.value}`;
      }
    });

    async function loadAutomations() {
      try {
        const wsId = state.activeWorkspace?.id || null;
        const res = await callBridge('get_scheduled_automations', { workspaceId: wsId });
        automationsCache = res?.tasks || [];
        const countEl = document.getElementById('automations-count');
        if (countEl) countEl.textContent = automationsCache.length;
        renderAutomations(automationsCache);
      } catch (err) {
        console.error('Failed to load automations:', err);
      }
    }

    function renderAutomations(tasks) {
      if (!gridEl) return;
      if (!tasks || tasks.length === 0) {
        gridEl.innerHTML = '<div style="font-size: 11.5px; color: var(--text-dim); padding: 24px; text-align: center; grid-column: 1 / -1;">No scheduled automations configured. Click "+ New Task" to set up a recurring AI runner.</div>';
        return;
      }

      gridEl.innerHTML = tasks.map(t => {
        const lastRunStr = t.lastRunUtc ? new Date(t.lastRunUtc).toLocaleTimeString() : 'Never';
        const nextDueStr = t.nextDueUtc ? new Date(t.nextDueUtc).toLocaleTimeString() : 'Scheduled';
        const statusBadgeColor = t.lastRunStatus === 'Success' ? '#10b981' : (t.lastRunStatus === 'Error' ? '#ef4444' : 'var(--text-dim)');

        return `
          <div class="automation-card" data-id="${t.id}">
            <div class="automation-card-head">
              <div>
                <div style="font-weight: 700; font-size: 13px; color: var(--text-main);">${escapeHtml(t.name)}</div>
                <div style="font-size: 11px; font-weight: 600; color: #a855f7; margin-top: 2px;">
                  ${escapeHtml(formatScheduleDescription(t.scheduleExpression))}
                  <span style="font-family: var(--font-mono); font-weight: 400; opacity: 0.6; font-size: 10px;">(${escapeHtml(t.scheduleExpression)})</span>
                </div>
              </div>
              <span class="status-badge" style="background: rgba(139,92,246,0.15); color: #8b5cf6;">${escapeHtml(t.targetCli)}</span>
            </div>
            <div style="font-size: 11.5px; color: var(--text-muted); line-height: 1.4; max-height: 48px; overflow: hidden; text-overflow: ellipsis;">
              ${escapeHtml(t.promptToExecute)}
            </div>
            <div style="display: flex; justify-content: space-between; align-items: center; font-size: 10.5px; color: var(--text-dim); border-top: 1px solid var(--border-subtle); padding-top: 8px;">
              <span>Last: <b style="color: ${statusBadgeColor};">${escapeHtml(t.lastRunStatus || 'Pending')}</b> (${lastRunStr})</span>
              <span>Next: ${nextDueStr}</span>
            </div>
            <div style="display: flex; justify-content: space-between; align-items: center; margin-top: 4px;">
              <label style="display: flex; align-items: center; gap: 5px; font-size: 11px; cursor: pointer; color: var(--text-main);">
                <input type="checkbox" class="auto-task-toggle" ${t.isEnabled ? 'checked' : ''} style="accent-color: #8b5cf6;" />
                <span>${t.isEnabled ? 'Enabled' : 'Disabled'}</span>
              </label>
              <div style="display: flex; gap: 6px;">
                <button type="button" class="btn btn-outline btn-auto-trigger" style="padding: 2px 8px; font-size: 10.5px; color: var(--accent-cyan);">Run Now</button>
                <button type="button" class="btn btn-outline btn-auto-edit" style="padding: 2px 7px; font-size: 10.5px;">Edit</button>
                <button type="button" class="btn btn-outline btn-auto-delete" style="padding: 2px 6px; font-size: 10.5px; color: var(--accent-red);">✕</button>
              </div>
            </div>
          </div>
        `;
      }).join('');

      gridEl.querySelectorAll('.automation-card').forEach(card => {
        const id = card.getAttribute('data-id');
        const task = tasks.find(t => t.id === id);
        if (!task) return;

        card.querySelector('.auto-task-toggle')?.addEventListener('change', async (e) => {
          task.isEnabled = e.target.checked;
          await callBridge('save_scheduled_automation', { task });
          showToast(`Automation "${task.name}" ${task.isEnabled ? 'enabled' : 'disabled'}`, 'info');
          loadAutomations();
        });

        card.querySelector('.btn-auto-trigger')?.addEventListener('click', async () => {
          showToast(`Triggering "${task.name}"...`, 'info');
          const res = await callBridge('trigger_scheduled_automation', { taskId: id });
          if (res?.success) {
            showToast(`Task "${task.name}" executed`, 'success');
            loadAutomations();
          } else {
            showToast('Task trigger failed', 'error');
          }
        });

        card.querySelector('.btn-auto-edit')?.addEventListener('click', () => {
          openAutoTaskModal(task);
        });

        card.querySelector('.btn-auto-delete')?.addEventListener('click', async () => {
          if (!confirm(`Delete task "${task.name}"?`)) return;
          await callBridge('delete_scheduled_automation', { taskId: id });
          showToast('Automation deleted', 'info');
          loadAutomations();
        });
      });
    }

    function openAutoTaskModal(task = null) {
      if (inpId) inpId.value = task ? task.id : ('task_' + Date.now());
      if (inpName) inpName.value = task ? task.name : '';
      if (selCli) selCli.value = task ? task.targetCli : 'claude_code';
      const cron = task ? task.scheduleExpression : '*/30 * * * *';
      if (inpCron) inpCron.value = cron;

      if (selInterval) {
        const hasOption = Array.from(selInterval.options).some(o => o.value === cron);
        if (hasOption) {
          selInterval.value = cron;
        } else {
          selInterval.value = 'custom';
        }
      }
      updateScheduleDesc();

      if (txtPrompt) txtPrompt.value = task ? task.promptToExecute : '';
      if (chkEnabled) chkEnabled.checked = task ? task.isEnabled : true;
      if (modalAuto) {
        modalAuto.classList.add('open', 'active');
        modalAuto.style.display = 'flex';
      }
      if (inpName) setTimeout(() => inpName.focus(), 60);
    }

    function closeAutoTaskModal() {
      if (modalAuto) {
        modalAuto.classList.remove('open', 'active');
        modalAuto.style.display = 'none';
      }
    }

    btnNew?.addEventListener('click', () => openAutoTaskModal(null));
    btnAddHdr?.addEventListener('click', () => openAutoTaskModal(null));
    btnRefresh?.addEventListener('click', loadAutomations);
    btnCancel?.addEventListener('click', closeAutoTaskModal);
    modalAuto?.addEventListener('click', (e) => {
      if (e.target === modalAuto) closeAutoTaskModal();
    });

    btnSave?.addEventListener('click', async () => {
      const name = inpName?.value?.trim();
      const promptText = txtPrompt?.value?.trim();
      if (!name || !promptText) {
        showToast('Please provide a task name and prompt', 'warning');
        return;
      }

      const task = {
        id: inpId?.value || ('task_' + Date.now()),
        workspaceId: state.activeWorkspace?.id || null,
        name,
        targetCli: selCli?.value || 'claude_code',
        scheduleExpression: inpCron?.value?.trim() || '*/30 * * * *',
        promptToExecute: promptText,
        isEnabled: chkEnabled?.checked ?? true
      };

      try {
        const res = await callBridge('save_scheduled_automation', { task });
        if (res?.success) {
          showToast(`Automation "${task.name}" saved`, 'success');
          closeAutoTaskModal();
          loadAutomations();
        }
      } catch (err) {
        showToast(`Save error: ${err.message}`, 'error');
      }
    });

    window.loadAutomations = loadAutomations;
  }

  // Feature 5: Push-to-Talk Voice Commander
window.setupScheduledAutomations = setupScheduledAutomations;
