  function setupPreCommitSecurity() {
    const btnHdrAudit = document.getElementById('btn-run-security-gatekeeper');
    const badgeScore = document.getElementById('security-score-badge');
    const chkGatekeeper = document.getElementById('chk-security-gatekeeper-enabled');
    const btnFullAudit = document.getElementById('btn-run-full-audit');
    const modalSec = document.getElementById('modal-security-gatekeeper');
    const btnCloseModal = document.getElementById('btn-close-security-modal');
    const btnRescan = document.getElementById('btn-modal-rescan-security');
    const issuesListEl = document.getElementById('modal-security-issues-list');

    if (chkGatekeeper) {
      const saved = localStorage.getItem('agents_hub_security_gatekeeper_enabled');
      chkGatekeeper.checked = saved !== 'false';
      chkGatekeeper.addEventListener('change', () => {
        localStorage.setItem('agents_hub_security_gatekeeper_enabled', chkGatekeeper.checked.toString());
        showToast(`Security Gatekeeper ${chkGatekeeper.checked ? 'Enabled' : 'Disabled'}`, 'info');
      });
    }

    async function runAudit(showModal = false) {
      try {
        const wsDir = state.activeWorkspace?.path || null;
        const res = await callBridge('audit_workspace_security', { workspaceDir: wsDir });
        const score = res?.safetyScore ?? 100;

        if (badgeScore) {
          badgeScore.textContent = `${score}/100`;
          if (score >= 90) {
            badgeScore.style.background = 'rgba(16,185,129,0.15)';
            badgeScore.style.color = '#10b981';
          } else if (score >= 70) {
            badgeScore.style.background = 'rgba(245,158,11,0.15)';
            badgeScore.style.color = '#f59e0b';
          } else {
            badgeScore.style.background = 'rgba(239,68,68,0.15)';
            badgeScore.style.color = '#ef4444';
          }
        }

        if (showModal) {
          renderSecurityModal(res);
          if (modalSec) {
            modalSec.classList.add('open', 'active');
            modalSec.style.display = 'flex';
          }
        }
      } catch (err) {
        console.error('Security audit error:', err);
      }
    }

    function closeSecurityModal() {
      if (modalSec) {
        modalSec.classList.remove('open', 'active');
        modalSec.style.display = 'none';
      }
    }

    function renderSecurityModal(result) {
      const score = result?.safetyScore ?? 100;
      const scorePill = document.getElementById('modal-security-score-pill');
      const titleEl = document.getElementById('modal-security-status-title');
      const descEl = document.getElementById('modal-security-status-desc');
      const highEl = document.getElementById('modal-sec-high-count');
      const medEl = document.getElementById('modal-sec-med-count');
      const lowEl = document.getElementById('modal-sec-low-count');

      if (scorePill) scorePill.textContent = `Score: ${score}/100`;
      if (highEl) highEl.textContent = `${result?.highRiskCount || 0} High`;
      if (medEl) medEl.textContent = `${result?.mediumRiskCount || 0} Medium`;
      if (lowEl) lowEl.textContent = `${result?.lowRiskCount || 0} Low`;

      if (score >= 90) {
        if (titleEl) { titleEl.textContent = 'Working Tree Clean'; titleEl.style.color = '#10b981'; }
        if (descEl) descEl.textContent = 'No secret leaks or shell hazards detected.';
      } else {
        if (titleEl) { titleEl.textContent = 'Vulnerabilities / Secrets Detected'; titleEl.style.color = '#ef4444'; }
        if (descEl) descEl.textContent = 'High-risk credentials found. Recommended to remediate before committing.';
      }

      if (issuesListEl) {
        const issues = result?.issues || [];
        if (issues.length === 0) {
          issuesListEl.innerHTML = '<div style="font-size: 12px; color: var(--text-dim); text-align: center; padding: 24px;">Workspace passed all security guardrail inspections.</div>';
          return;
        }

        issuesListEl.innerHTML = issues.map(iss => `
          <div class="security-issue-card severity-${(iss.severity || 'low').toLowerCase()}">
            <div style="display: flex; justify-content: space-between; align-items: center;">
              <b style="font-size: 12px; color: var(--text-main);">${escapeHtml(iss.ruleName)}</b>
              <span class="status-badge" style="font-size: 9.5px; text-transform: uppercase;">${escapeHtml(iss.severity)}</span>
            </div>
            <div style="font-size: 11px; font-family: var(--font-mono); color: var(--accent-cyan); word-break: break-all;">
              ${escapeHtml(iss.filePath)}:${iss.lineNumber}
            </div>
            ${iss.snippet ? `<div style="font-size: 11px; font-family: var(--font-mono); color: var(--text-dim); background: rgba(0,0,0,0.3); padding: 4px 6px; border-radius: 4px;">${escapeHtml(iss.snippet)}</div>` : ''}
            <div style="font-size: 11px; color: var(--text-muted); font-style: italic;">
              ${escapeHtml(iss.recommendation)}
            </div>
          </div>
        `).join('');
      }
    }

    btnHdrAudit?.addEventListener('click', () => runAudit(true));
    btnFullAudit?.addEventListener('click', () => runAudit(true));
    btnRescan?.addEventListener('click', () => runAudit(true));
    btnCloseModal?.addEventListener('click', closeSecurityModal);
    modalSec?.addEventListener('click', (e) => {
      if (e.target === modalSec) closeSecurityModal();
    });

    runAudit(false);
  }

  // Feature 7: Remote Agent Trigger & Discord Bot / Webhook
window.setupPreCommitSecurity = setupPreCommitSecurity;
