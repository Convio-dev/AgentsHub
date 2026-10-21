  function setupCustomCliManager() {
    const btnAddCli = document.getElementById('btn-add-custom-cli');
    const container = document.getElementById('custom-clis-list-container');
    const modalCli = document.getElementById('modal-custom-cli');
    const btnCancelCli = document.getElementById('btn-cancel-custom-cli');
    const btnSaveCli = document.getElementById('btn-save-custom-cli');
    const btnBrowseExe = document.getElementById('btn-browse-cli-exe');
    const btnBrowseDir = document.getElementById('btn-browse-cli-dir');

    async function loadCustomClis() {
      try {
        const res = await callBridge('get_custom_clis');
        renderCustomClis(res?.clis || []);
      } catch { }
    }

    function renderCustomClis(clis) {
      if (!container) return;
      container.innerHTML = '';
      if (!clis || clis.length === 0) {
        container.innerHTML = '<div style="grid-column: 1/-1; text-align: center; color: var(--text-dim); padding: 18px;">No custom CLI engines configured. Click "Add CLI Engine" to integrate any tool (Aider, Goose, Copilot, etc.).</div>';
        return;
      }

      clis.forEach(c => {
        const id = c.id || c.Id || '';
        const name = c.name || c.Name || 'Unnamed CLI';
        const colorTag = c.colorTag || c.ColorTag || '#38bdf8';
        const exePath = c.executablePath || c.ExecutablePath || '';
        const args = c.arguments || c.Arguments || '';

        const card = document.createElement('div');
        card.style.cssText = 'background: var(--bg-input); border: 1px solid var(--border-subtle); border-radius: 6px; padding: 10px 12px; display: flex; flex-direction: column; justify-content: space-between;';
        card.innerHTML = `
          <div>
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 4px;">
              <div style="display: flex; align-items: center; gap: 6px;">
                <span style="width: 8px; height: 8px; border-radius: 50%; background: ${colorTag};"></span>
                <span style="font-size: 13px; font-weight: 700; color: var(--text-main);">${escapeHtml(name)}</span>
              </div>
              <button class="btn btn-outline btn-del-cli" data-id="${escapeHtml(id)}" type="button" style="font-size: 10px; padding: 2px 6px; color: #f87171; cursor: pointer;">Delete</button>
            </div>
            <div style="font-size: 11px; font-family: var(--font-mono); color: var(--text-dim); margin-bottom: 4px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;" title="${escapeHtml(exePath + ' ' + args)}">${escapeHtml(exePath)} ${escapeHtml(args)}</div>
          </div>
          <div style="display: flex; justify-content: flex-end; gap: 6px; margin-top: 8px;">
            <button class="btn btn-outline btn-launch-cli" data-id="${escapeHtml(id)}" type="button" style="font-size: 11px; padding: 3px 10px; cursor: pointer;">Run in Terminal</button>
          </div>
        `;
        container.appendChild(card);
      });

      container.querySelectorAll('.btn-del-cli').forEach(b => {
        b.addEventListener('click', async () => {
          if (confirm('Delete this custom CLI engine?')) {
            await callBridge('delete_custom_cli', { id: b.dataset.id });
            loadCustomClis();
            showToast('CLI engine removed.', 'info', 2500);
          }
        });
      });

      container.querySelectorAll('.btn-launch-cli').forEach(b => {
        b.addEventListener('click', async () => {
          const cliId = b.dataset.id;
          switchView('cliwebview');
          callBridge('start_cli', { cli: cliId });
          showToast(`Started custom CLI: ${cliId}`, 'success', 3000);
        });
      });
    }

    window.handleCustomClisChanged = function() {
      loadCustomClis();
    };

    btnAddCli?.addEventListener('click', () => {
      document.getElementById('modal-cli-id').value = '';
      document.getElementById('modal-cli-name').value = '';
      document.getElementById('modal-cli-exe').value = '';
      document.getElementById('modal-cli-args').value = '';
      document.getElementById('modal-cli-dir').value = '';
      if (modalCli) modalCli.style.display = 'flex';
    });

    btnCancelCli?.addEventListener('click', () => {
      if (modalCli) modalCli.style.display = 'none';
    });

    btnBrowseExe?.addEventListener('click', async () => {
      const res = await callBridge('browse_file', { title: 'Select CLI Executable or Script' });
      if (res?.path) document.getElementById('modal-cli-exe').value = res.path;
    });

    btnBrowseDir?.addEventListener('click', async () => {
      const res = await callBridge('browse_directory', { title: 'Select Working Directory' });
      if (res?.path) document.getElementById('modal-cli-dir').value = res.path;
    });

    btnSaveCli?.addEventListener('click', async () => {
      const name = document.getElementById('modal-cli-name').value.trim();
      const exe = document.getElementById('modal-cli-exe').value.trim();
      const args = document.getElementById('modal-cli-args').value.trim();
      const dir = document.getElementById('modal-cli-dir').value.trim();
      const color = document.getElementById('modal-cli-color').value;

      if (!name || !exe) {
        showToast('Engine name and executable path are required.', 'warning');
        return;
      }

      const cli = {
        name,
        executablePath: exe,
        arguments: args,
        workingDirectory: dir,
        colorTag: color
      };

      try {
        await callBridge('save_custom_cli', { cli });
        if (modalCli) modalCli.style.display = 'none';
        loadCustomClis();
        showToast(`CLI engine '${name}' saved.`, 'success');
      } catch (err) {
        showToast('Failed to save CLI: ' + (err?.message || 'Error'), 'error');
      }
    });

    loadCustomClis();
  }

  // Workspace File Watchdog & Diff Inspector Manager
window.setupCustomCliManager = setupCustomCliManager;
