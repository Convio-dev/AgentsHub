  function setupFileWatchdogManager() {
    const pill = document.getElementById('file-watchdog-pill');
    const pillText = document.getElementById('file-watchdog-pill-text');
    const modal = document.getElementById('modal-file-diff');
    const btnClose = document.getElementById('btn-close-file-diff');
    const btnClear = document.getElementById('btn-clear-file-changes');
    const listEl = document.getElementById('file-changes-list');
    const filenameEl = document.getElementById('diff-viewer-filename');
    const codeEl = document.getElementById('diff-viewer-code');
    const btnRevert = document.getElementById('btn-revert-file');
    const btnDismissPill = document.getElementById('btn-close-watchdog-pill');

    let currentSelectedPath = null;
    let changeEvents = [];
    let isPillDismissed = false;

    async function refreshChanges() {
      try {
        const res = await callBridge('get_recent_file_changes');
        changeEvents = res?.changes || [];
        updatePill();
        if (modal && modal.style.display === 'flex') {
          renderList();
        }
      } catch { }
    }

    function updatePill() {
      if (!pill || !pillText) return;
      if (changeEvents.length > 0 && !isPillDismissed) {
        pill.style.display = 'inline-flex';
        pillText.textContent = `${changeEvents.length} workspace file${changeEvents.length > 1 ? 's' : ''} modified`;
      } else {
        pill.style.display = 'none';
      }
    }

    function renderFormattedDiff(diffText, containerEl) {
      if (!containerEl) return;
      containerEl.innerHTML = '';

      if (!diffText || !diffText.trim()) {
        containerEl.innerHTML = '<div style="padding: 32px 16px; text-align: center; color: var(--text-dim); font-size: 12px;">No uncommitted modifications detected.</div>';
        return;
      }

      const lines = diffText.split(/\r?\n/);
      const frag = document.createDocumentFragment();

      lines.forEach((line) => {
        const row = document.createElement('div');
        row.className = 'diff-line';

        if (line.startsWith('+++') || line.startsWith('---') || line.startsWith('diff --git') || line.startsWith('index ')) {
          row.classList.add('diff-line-header');
          const gutter = document.createElement('span');
          gutter.className = 'diff-line-gutter';
          gutter.textContent = ' ';
          const text = document.createElement('span');
          text.className = 'diff-line-text';
          text.textContent = line;
          row.appendChild(gutter);
          row.appendChild(text);
        } else if (line.startsWith('@@')) {
          row.classList.add('diff-line-hunk');
          const gutter = document.createElement('span');
          gutter.className = 'diff-line-gutter';
          gutter.textContent = '@';
          const text = document.createElement('span');
          text.className = 'diff-line-text';
          text.textContent = line;
          row.appendChild(gutter);
          row.appendChild(text);
        } else if (line.startsWith('+')) {
          row.classList.add('diff-line-add');
          const gutter = document.createElement('span');
          gutter.className = 'diff-line-gutter';
          gutter.textContent = '+';
          const text = document.createElement('span');
          text.className = 'diff-line-text';
          text.textContent = line.substring(1);
          row.appendChild(gutter);
          row.appendChild(text);
        } else if (line.startsWith('-')) {
          row.classList.add('diff-line-del');
          const gutter = document.createElement('span');
          gutter.className = 'diff-line-gutter';
          gutter.textContent = '-';
          const text = document.createElement('span');
          text.className = 'diff-line-text';
          text.textContent = line.substring(1);
          row.appendChild(gutter);
          row.appendChild(text);
        } else {
          const gutter = document.createElement('span');
          gutter.className = 'diff-line-gutter';
          gutter.textContent = ' ';
          const text = document.createElement('span');
          text.className = 'diff-line-text';
          text.textContent = line.startsWith(' ') ? line.substring(1) : line;
          row.appendChild(gutter);
          row.appendChild(text);
        }

        frag.appendChild(row);
      });

      containerEl.appendChild(frag);
    }

    function renderList() {
      if (!listEl) return;
      listEl.innerHTML = '';
      if (changeEvents.length === 0) {
        listEl.innerHTML = '<div style="text-align: center; color: var(--text-dim); padding: 24px 12px; font-size: 11.5px;">No recent workspace modifications.</div>';
        if (codeEl) codeEl.innerHTML = '<div style="padding: 32px 16px; text-align: center; color: var(--text-dim); font-size: 12px;">No file selected.</div>';
        if (filenameEl) filenameEl.textContent = 'Select a file to view diff';
        if (btnRevert) btnRevert.style.display = 'none';
        return;
      }

      changeEvents.forEach((ev) => {
        const relPath = ev.relativePath || ev.RelativePath || ev.path || ev.Path || '';
        const changeType = ev.changeType || ev.ChangeType || 'Modified';
        const timestamp = ev.timestamp || ev.Timestamp || '';
        const isSelected = currentSelectedPath && currentSelectedPath.toLowerCase() === relPath.toLowerCase();

        const item = document.createElement('div');
        item.className = 'diff-file-item' + (isSelected ? ' active' : '');

        const badgeColor = changeType === 'Created' ? '#10b981' : changeType === 'Deleted' ? '#ef4444' : '#38bdf8';
        const badgeBg = changeType === 'Created' ? 'rgba(16,185,129,0.14)' : changeType === 'Deleted' ? 'rgba(239,68,68,0.14)' : 'rgba(56,189,248,0.14)';

        const parts = relPath.replace(/\\/g, '/').split('/');
        const fileName = parts.pop() || relPath;
        const dirName = parts.length > 0 ? parts.join('/') + '/' : '';

        item.innerHTML = `
          <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 3px;">
            <span style="font-size: 10px; font-weight: 700; padding: 1px 6px; border-radius: 3px; background: ${badgeBg}; color: ${badgeColor};">${escapeHtml(changeType)}</span>
            <span style="font-size: 10px; color: var(--text-dim); font-family: var(--font-mono);">${escapeHtml(timestamp)}</span>
          </div>
          <div style="font-size: 11.5px; font-weight: 600; color: var(--text-main); overflow: hidden; text-overflow: ellipsis; white-space: nowrap;" title="${escapeHtml(relPath)}">${escapeHtml(fileName)}</div>
          ${dirName ? `<div style="font-size: 10px; font-family: var(--font-mono); color: var(--text-dim); overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">${escapeHtml(dirName)}</div>` : ''}
        `;

        item.onclick = () => selectFileForDiff(relPath);
        listEl.appendChild(item);
      });

      if (!currentSelectedPath && changeEvents.length > 0) {
        const firstRel = changeEvents[0].relativePath || changeEvents[0].RelativePath || changeEvents[0].path || changeEvents[0].Path;
        if (firstRel) selectFileForDiff(firstRel);
      }
    }

    async function selectFileForDiff(relPath) {
      if (!relPath) return;
      currentSelectedPath = relPath;
      if (filenameEl) filenameEl.textContent = relPath;
      if (codeEl) codeEl.innerHTML = '<div style="padding: 24px; color: var(--text-dim); text-align: center; font-size: 12px;">Loading diff...</div>';
      if (btnRevert) btnRevert.style.display = 'inline-block';

      renderList();

      try {
        const res = await callBridge('get_file_diff', { path: relPath });
        if (codeEl) {
          renderFormattedDiff(res?.diff, codeEl);
        }
      } catch (err) {
        if (codeEl) {
          codeEl.innerHTML = `<div style="padding: 20px; color: #ef4444; font-family: var(--font-mono); font-size: 11.5px;">Failed to load diff: ${escapeHtml(err?.message || 'Error')}</div>`;
        }
      }
    }

    btnRevert?.addEventListener('click', async () => {
      if (!currentSelectedPath) return;
      if (confirm(`Revert modifications to ${currentSelectedPath}?`)) {
        try {
          const res = await callBridge('revert_file_change', { path: currentSelectedPath });
          if (res?.success) {
            showToast(`Reverted ${currentSelectedPath}`, 'success');
            refreshChanges();
          } else {
            showToast(`Could not revert ${currentSelectedPath}`, 'warning');
          }
        } catch (e) {
          showToast('Revert failed: ' + (e?.message || 'Error'), 'error');
        }
      }
    });

    btnClear?.addEventListener('click', async () => {
      await callBridge('clear_file_changes');
      changeEvents = [];
      currentSelectedPath = null;
      isPillDismissed = false;
      updatePill();
      renderList();
    });

    btnDismissPill?.addEventListener('click', (e) => {
      e.stopPropagation();
      isPillDismissed = true;
      if (pill) pill.style.display = 'none';
    });

    pill?.addEventListener('click', () => {
      if (modal) {
        modal.style.display = 'flex';
        renderList();
        if (!currentSelectedPath && changeEvents.length > 0) {
          const firstRel = changeEvents[0].relativePath || changeEvents[0].RelativePath || changeEvents[0].path || changeEvents[0].Path;
          if (firstRel) selectFileForDiff(firstRel);
        }
      }
    });

    btnClose?.addEventListener('click', () => {
      if (modal) modal.style.display = 'none';
    });

    window.handleWorkspaceFileEvent = function(fileEvent) {
      if (fileEvent) {
        isPillDismissed = false;
        changeEvents.unshift(fileEvent);
        if (changeEvents.length > 100) changeEvents.pop();
        updatePill();
        if (modal && modal.style.display === 'flex') {
          renderList();
        }
      }
    };

    refreshChanges();
  }

  // Session State Auto-Save Manager
window.setupFileWatchdogManager = setupFileWatchdogManager;
