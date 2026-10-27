  function setupSystemDoctor() {
    const btnScan = document.getElementById('btn-run-diagnostics');
    const scorePill = document.getElementById('system-doctor-score');
    const container = document.getElementById('diagnostics-list-container');
    const scanIcon = document.getElementById('icon-doctor-scan');

    async function runScan() {
      if (!btnScan || !container) return;
      btnScan.disabled = true;
      if (scanIcon) scanIcon.style.animation = 'spin 1s linear infinite';
      if (scorePill) {
        scorePill.textContent = 'Scanning...';
        scorePill.style.color = 'var(--accent-cyan)';
      }

      try {
        const res = await callBridge('run_system_diagnostics');
        const items = res?.diagnostics || [];
        renderDiagnostics(items);
      } catch (err) {
        showToast('Diagnostics failed: ' + (err?.message || 'Error'), 'error');
      } finally {
        btnScan.disabled = false;
        if (scanIcon) scanIcon.style.animation = '';
      }
    }

    function renderDiagnostics(items) {
      if (!container) return;
      container.innerHTML = '';
      let errors = 0;
      let warnings = 0;

      items.forEach(item => {
        const id = item.id || item.Id || '';
        const title = item.title || item.Title || 'Diagnostic Check';
        const status = item.status || item.Status || 'Pass';
        const details = item.details || item.Details || '';
        const canAutoFix = !!(item.canAutoFix ?? item.CanAutoFix);
        const fixLabel = item.fixLabel || item.FixLabel || 'Fix';

        if (status === 'Error') errors++;
        else if (status === 'Warning') warnings++;

        const card = document.createElement('div');
        card.style.cssText = 'background: var(--bg-input); border: 1px solid var(--border-subtle); border-radius: 6px; padding: 10px 12px; display: flex; flex-direction: column; justify-content: space-between;';

        const statusColor = status === 'Pass' ? '#10b981' : status === 'Warning' ? '#f59e0b' : '#ef4444';
        const statusBg = status === 'Pass' ? 'rgba(16,185,129,0.1)' : status === 'Warning' ? 'rgba(245,158,11,0.1)' : 'rgba(239,68,68,0.1)';

        card.innerHTML = `
          <div>
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 4px;">
              <span style="font-size: 12px; font-weight: 600; color: var(--text-main);">${escapeHtml(title)}</span>
              <span style="font-size: 10.5px; font-weight: 700; padding: 2px 6px; border-radius: 4px; background: ${statusBg}; color: ${statusColor};">${status}</span>
            </div>
            <div style="font-size: 11px; color: var(--text-dim); line-height: 1.4; word-break: break-word;">${escapeHtml(details)}</div>
          </div>
          ${canAutoFix ? `
            <div style="margin-top: 8px; display: flex; justify-content: flex-end;">
              <button class="btn btn-outline btn-autofix" data-issue="${escapeHtml(id)}" type="button" style="font-size: 10.5px; padding: 3px 8px; color: var(--accent-cyan); border-color: rgba(6,182,212,0.3); cursor: pointer;">${escapeHtml(fixLabel)}</button>
            </div>
          ` : ''}
        `;
        container.appendChild(card);
      });

      container.querySelectorAll('.btn-autofix').forEach(b => {
        b.addEventListener('click', async () => {
          const issueId = b.dataset.issue;
          b.disabled = true;
          b.textContent = 'Fixing...';
          try {
            const fixRes = await callBridge('autofix_diagnostic_issue', { issueId });
            if (fixRes?.diagnostics) renderDiagnostics(fixRes.diagnostics);
            showToast('Fix applied successfully.', 'success', 3000);
          } catch (e) {
            showToast('Auto-fix failed: ' + (e?.message || 'Error'), 'error');
            b.disabled = false;
          }
        });
      });

      if (scorePill) {
        if (errors > 0) {
          scorePill.textContent = `${errors} Issue(s) Detected`;
          scorePill.style.color = '#ef4444';
          scorePill.style.background = 'rgba(239,68,68,0.1)';
        } else if (warnings > 0) {
          scorePill.textContent = `${warnings} Warning(s)`;
          scorePill.style.color = '#f59e0b';
          scorePill.style.background = 'rgba(245,158,11,0.1)';
        } else {
          scorePill.textContent = '100% Healthy';
          scorePill.style.color = '#10b981';
          scorePill.style.background = 'rgba(16,185,129,0.1)';
        }
      }
    }

    btnScan?.addEventListener('click', runScan);
  }

  // MCP Servers & Agent Skills Manager
window.setupSystemDoctor = setupSystemDoctor;
