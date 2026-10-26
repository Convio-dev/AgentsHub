  function setupMcpSkillsManager() {
    const btnRefresh = document.getElementById('btn-refresh-mcp-skills');
    const btnAdd = document.getElementById('btn-add-mcp-server');
    const btnAddSkill = document.getElementById('btn-add-agent-skill');
    const btnAddRule = document.getElementById('btn-add-agent-rule');
    const btnAddAutomation = document.getElementById('btn-add-automation-task');
    const btnNpxInstaller = document.getElementById('btn-npx-installer');
    const tabMcp = document.getElementById('tab-btn-mcp');
    const tabSkills = document.getElementById('tab-btn-skills');
    const tabRules = document.getElementById('tab-btn-rules');
    const tabSessions = document.getElementById('tab-btn-sessions');
    const tabAutomations = document.getElementById('tab-btn-automations');
    const mcpWrapper = document.getElementById('mcp-section-wrapper');
    const mcpContainer = document.getElementById('mcp-servers-container');
    const skillsContainer = document.getElementById('agent-skills-container');
    const skillsGrid = document.getElementById('agent-skills-grid');
    const rulesContainer = document.getElementById('agent-rules-container');
    const sessionsContainer = document.getElementById('agent-sessions-container');
    const automationsContainer = document.getElementById('agent-automations-container');
    const mcpCount = document.getElementById('mcp-count');
    const skillsCount = document.getElementById('skills-count');
    const rulesCount = document.getElementById('rules-count');
    const sessionsCount = document.getElementById('sessions-count');
    const automationsCount = document.getElementById('automations-count');

    // Agent Skills filtering & modals
    const skillSearchInput = document.getElementById('skill-search-input');
    const filterCountAll = document.getElementById('skills-filter-count-all');
    const filterCountProject = document.getElementById('skills-filter-count-project');
    const filterCountBuiltin = document.getElementById('skills-filter-count-builtin');
    const modalSkill = document.getElementById('modal-skill-editor');
    const modalAddSkill = document.getElementById('modal-add-skill');

    let allSkillsCache = [];
    let activeSkillFilter = 'all';
    let skillSearchQuery = '';

    const modalMcp = document.getElementById('modal-mcp-server');
    const btnCancelMcp = document.getElementById('btn-cancel-mcp-modal');
    const btnSaveMcp = document.getElementById('btn-save-mcp-modal');
    const templateSelect = document.getElementById('modal-mcp-template');

    const modalAuth = document.getElementById('modal-mcp-auth');
    const authTitle = document.getElementById('modal-auth-title');
    const authServerDisplay = document.getElementById('modal-auth-server-display');
    const authBadge = document.getElementById('modal-auth-badge');
    const authDetails = document.getElementById('modal-auth-details');
    const authKeyLabel = document.getElementById('modal-auth-key-label');
    const authKeyInput = document.getElementById('modal-auth-key-input');
    const authLink = document.getElementById('modal-auth-link');
    const authFeedback = document.getElementById('modal-auth-feedback');
    const btnTestAuth = document.getElementById('btn-test-mcp-auth');
    const btnSaveAuth = document.getElementById('btn-save-mcp-auth');
    const btnCloseAuth = document.getElementById('btn-close-mcp-auth');
    const btnToggleKeyVis = document.getElementById('btn-toggle-auth-key-visibility');
    const inputHiddenServer = document.getElementById('modal-auth-server-name');
    const inputHiddenKey = document.getElementById('modal-auth-key-name');

    // Agent Rules elements
    const rulesGrid = document.getElementById('rules-cards-grid');
    const wsNameEl = document.getElementById('rules-active-workspace-name');
    const isolationDescEl = document.getElementById('rules-isolation-status-desc');
    const chkIsolation = document.getElementById('chk-workspace-rule-isolation');
    const modalRule = document.getElementById('modal-rule-editor');
    const btnCancelRule = document.getElementById('btn-cancel-rule-modal');
    const btnSaveRule = document.getElementById('btn-save-rule-modal');
    const btnDeleteRuleModal = document.getElementById('btn-delete-rule-modal');

    // NPX installer elements
    const modalNpx = document.getElementById('modal-npx-installer');
    const btnCancelNpx = document.getElementById('btn-cancel-npx-modal');
    const btnLaunchNpx = document.getElementById('btn-launch-npx-modal');
    const selectNpxPreset = document.getElementById('modal-npx-preset');
    const inputNpxCmd = document.getElementById('modal-npx-command');
    const workdirNpxEl = document.getElementById('modal-npx-workdir');

    let currentRulesCache = [];

    const templates = {
      filesystem: { name: 'filesystem', cmd: 'npx', args: '-y @modelcontextprotocol/server-filesystem C:\\Projects', scope: 'Global' },
      sqlite: { name: 'sqlite-db', cmd: 'npx', args: '-y @modelcontextprotocol/server-sqlite --db-path C:\\Projects\\app.db', scope: 'Global' },
      github: { name: 'github', cmd: 'npx', args: '-y @modelcontextprotocol/server-github', scope: 'Global', env: 'GITHUB_PERSONAL_ACCESS_TOKEN=' },
      memory: { name: 'memory-graph', cmd: 'npx', args: '-y @modelcontextprotocol/server-memory', scope: 'Global' },
      puppeteer: { name: 'puppeteer', cmd: 'npx', args: '-y @modelcontextprotocol/server-puppeteer', scope: 'Global' }
    };

    templateSelect?.addEventListener('change', (e) => {
      const val = e.target.value;
      if (val && templates[val]) {
        const t = templates[val];
        document.getElementById('modal-mcp-name').value = t.name;
        document.getElementById('modal-mcp-cmd').value = t.cmd;
        document.getElementById('modal-mcp-args').value = t.args;
        document.getElementById('modal-mcp-scope').value = t.scope;
        if (t.env) document.getElementById('modal-mcp-env').value = t.env;
      }
    });

    function switchSubTab(target) {
      const subtabs = [
        { id: 'mcp', btn: tabMcp },
        { id: 'skills', btn: tabSkills },
        { id: 'rules', btn: tabRules },
        { id: 'sessions', btn: tabSessions },
        { id: 'automations', btn: tabAutomations }
      ];

      subtabs.forEach(st => {
        if (!st.btn) return;
        const isActive = st.id === target;
        st.btn.classList.toggle('active', isActive);
        if (isActive) {
          st.btn.classList.add('bg-sky-950/60', 'text-sky-400', 'border-sky-800/50');
          st.btn.classList.remove('hover:bg-surface-card', 'text-slate-400', 'border-transparent');
        } else {
          st.btn.classList.remove('bg-sky-950/60', 'text-sky-400', 'border-sky-800/50');
          st.btn.classList.add('hover:bg-surface-card', 'text-slate-400', 'border-transparent');
        }
      });

      if (mcpWrapper) mcpWrapper.style.display = target === 'mcp' ? 'block' : 'none';
      if (mcpContainer) mcpContainer.style.display = target === 'mcp' ? 'grid' : 'none';
      if (skillsContainer) skillsContainer.style.display = target === 'skills' ? 'block' : 'none';
      if (rulesContainer) rulesContainer.style.display = target === 'rules' ? 'block' : 'none';
      if (sessionsContainer) sessionsContainer.style.display = target === 'sessions' ? 'block' : 'none';
      if (automationsContainer) automationsContainer.style.display = target === 'automations' ? 'block' : 'none';

      if (btnAdd) btnAdd.style.display = target === 'mcp' ? 'inline-flex' : 'none';
      if (btnAddSkill) btnAddSkill.style.display = target === 'skills' ? 'inline-flex' : 'none';
      if (btnAddRule) btnAddRule.style.display = target === 'rules' ? 'inline-flex' : 'none';
      if (btnAddAutomation) btnAddAutomation.style.display = target === 'automations' ? 'inline-flex' : 'none';

      if (target === 'rules') {
        loadAgentRules();
      } else if (target === 'sessions') {
        loadAgentSessions();
      } else if (target === 'automations') {
        if (typeof window.loadAutomations === 'function') window.loadAutomations();
      }
    }

    tabMcp?.addEventListener('click', () => switchSubTab('mcp'));
    tabSkills?.addEventListener('click', () => switchSubTab('skills'));
    tabRules?.addEventListener('click', () => switchSubTab('rules'));
    tabSessions?.addEventListener('click', () => switchSubTab('sessions'));
    tabAutomations?.addEventListener('click', () => switchSubTab('automations'));
    document.getElementById('btn-refresh-sessions')?.addEventListener('click', loadAgentSessions);

    let currentSessionsCache = [];
    let selectedSessionPath = null;

    const modalDelSession = document.getElementById('modal-confirm-delete-session');
    const modalDelSessionId = document.getElementById('modal-del-session-id');
    const modalDelSessionPath = document.getElementById('modal-del-session-path');
    const btnCancelDelSession = document.getElementById('btn-cancel-del-session');
    const btnConfirmDelSession = document.getElementById('btn-confirm-del-session');
    const btnDeleteActiveSession = document.getElementById('btn-delete-active-session');

    function promptDeleteSession(sessionId, transcriptPath) {
      if (modalDelSessionId) modalDelSessionId.textContent = sessionId;
      if (modalDelSessionPath) modalDelSessionPath.value = transcriptPath;
      if (modalDelSession) {
        modalDelSession.classList.add('open', 'active');
        modalDelSession.style.display = 'flex';
      }
    }

    function closeDelSessionModal() {
      if (modalDelSession) {
        modalDelSession.classList.remove('open', 'active');
        modalDelSession.style.display = 'none';
      }
    }

    btnCancelDelSession?.addEventListener('click', closeDelSessionModal);
    modalDelSession?.addEventListener('click', (e) => {
      if (e.target === modalDelSession) closeDelSessionModal();
    });

    btnDeleteActiveSession?.addEventListener('click', () => {
      if (!selectedSessionPath) return;
      const sess = currentSessionsCache.find(s => s.transcriptPath === selectedSessionPath);
      if (sess) {
        promptDeleteSession(sess.sessionId, sess.transcriptPath);
      }
    });

    btnConfirmDelSession?.addEventListener('click', async () => {
      const path = modalDelSessionPath?.value;
      if (!path) return;

      try {
        const res = await callBridge('delete_agent_session', { transcriptPath: path });
        if (res?.success) {
          showToast('Agent session deleted permanently', 'success');
          closeDelSessionModal();

          if (selectedSessionPath === path) {
            selectedSessionPath = null;
            const emptyEl = document.getElementById('session-details-empty');
            const contentEl = document.getElementById('session-details-content');
            if (emptyEl) emptyEl.style.display = 'flex';
            if (contentEl) contentEl.style.display = 'none';
          }

          loadAgentSessions();
        } else {
          showToast('Failed to delete session', 'error');
        }
      } catch (err) {
        showToast('Error deleting session: ' + err.message, 'error');
      }
    });

    async function loadAgentSessions() {
      try {
        const res = await callBridge('get_agent_sessions');
        currentSessionsCache = res?.sessions || [];
        if (sessionsCount) sessionsCount.textContent = currentSessionsCache.length;
        renderSessionsList(currentSessionsCache);
      } catch (err) {
        console.error('Failed to load agent sessions:', err);
      }
    }

    function renderSessionsList(sessions) {
      const listEl = document.getElementById('sessions-items-list');
      if (!listEl) return;
      if (!sessions || sessions.length === 0) {
        listEl.innerHTML = '<div style="font-size: 11px; color: var(--text-dim); text-align: center; padding: 20px;">No agent trajectories discovered.</div>';
        return;
      }

      listEl.innerHTML = sessions.map(s => {
        const isActive = s.transcriptPath === selectedSessionPath;
        const timeStr = s.updatedAt ? new Date(s.updatedAt).toLocaleTimeString() : '';
        return `
          <div class="session-item-card ${isActive ? 'active' : ''}" data-path="${escapeHtml(s.transcriptPath)}">
            <div style="display: flex; justify-content: space-between; align-items: center;">
              <span style="font-weight: 700; font-size: 11.5px; color: var(--text-main); font-family: var(--font-mono);">${escapeHtml(s.sessionId.substring(0, 12))}...</span>
              <div style="display: flex; align-items: center; gap: 4px;">
                <span class="status-badge" style="font-size: 9.5px; padding: 1px 5px;">${escapeHtml(s.modelName || 'agent')}</span>
                <button type="button" class="btn-card-del-session" data-path="${escapeHtml(s.transcriptPath)}" title="Delete Session" style="background: transparent; border: none; color: var(--text-dim); cursor: pointer; padding: 2px 4px; border-radius: 3px; display: inline-flex; align-items: center; transition: color 0.15s ease;">
                  <svg class="icon-svg" viewBox="0 0 24 24" style="width: 11px; height: 11px;"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
                </button>
              </div>
            </div>
            <div style="display: flex; justify-content: space-between; font-size: 10.5px; color: var(--text-dim);">
              <span>${s.stepCount} steps</span>
              <span>${timeStr}</span>
            </div>
          </div>
        `;
      }).join('');

      listEl.querySelectorAll('.session-item-card').forEach(card => {
        card.addEventListener('click', (e) => {
          if (e.target.closest('.btn-card-del-session')) return;
          const path = card.getAttribute('data-path');
          const sess = sessions.find(s => s.transcriptPath === path);
          if (sess) selectSession(sess);
        });
      });

      listEl.querySelectorAll('.btn-card-del-session').forEach(btn => {
        btn.addEventListener('click', (e) => {
          e.stopPropagation();
          const path = btn.getAttribute('data-path');
          const sess = sessions.find(s => s.transcriptPath === path);
          if (sess) promptDeleteSession(sess.sessionId, sess.transcriptPath);
        });
      });
    }

    async function selectSession(sess) {
      selectedSessionPath = sess.transcriptPath;
      renderSessionsList(currentSessionsCache);

      const emptyEl = document.getElementById('session-details-empty');
      const contentEl = document.getElementById('session-details-content');
      const idEl = document.getElementById('session-inspector-id');
      const subEl = document.getElementById('session-inspector-sub');
      const stepsBadge = document.getElementById('session-steps-badge');
      const statusBadge = document.getElementById('session-status-badge');
      const timelineEl = document.getElementById('session-timeline-steps');

      if (emptyEl) emptyEl.style.display = 'none';
      if (contentEl) contentEl.style.display = 'flex';

      if (idEl) idEl.textContent = `Session: ${sess.sessionId}`;
      if (subEl) subEl.textContent = `Workspace: ${sess.workspaceDir || sess.transcriptPath} | Created: ${sess.createdAt ? new Date(sess.createdAt).toLocaleString() : '--'}`;
      if (stepsBadge) stepsBadge.textContent = `${sess.stepCount} Steps`;
      if (statusBadge) statusBadge.textContent = sess.status || 'Done';

      if (timelineEl) {
        timelineEl.innerHTML = '<div style="font-size: 11.5px; color: var(--text-dim); padding: 10px;">Loading trajectory timeline...</div>';
      }

      try {
        const res = await callBridge('get_transcript_steps', { transcriptPath: sess.transcriptPath });
        const steps = res?.steps || [];
        renderTimelineSteps(steps);
      } catch (err) {
        if (timelineEl) {
          timelineEl.innerHTML = `<div style="font-size: 11.5px; color: var(--accent-red); padding: 10px;">Failed to load trajectory steps: ${escapeHtml(err.message)}</div>`;
        }
      }
    }

    function renderTimelineSteps(steps) {
      const timelineEl = document.getElementById('session-timeline-steps');
      if (!timelineEl) return;
      if (!steps || steps.length === 0) {
        timelineEl.innerHTML = '<div style="font-size: 11.5px; color: var(--text-dim); padding: 10px;">No steps recorded in trajectory.</div>';
        return;
      }

      timelineEl.innerHTML = steps.map(st => {
        let pillClass = 'model';
        let typeName = st.type || 'Step';
        if (st.source === 'USER_EXPLICIT' || st.type === 'USER_INPUT') {
          pillClass = 'user';
          typeName = 'User Input';
        } else if (st.toolCalls && st.toolCalls.length > 0) {
          pillClass = 'tool';
          typeName = 'Tool Call';
        } else if (st.status === 'ERROR') {
          pillClass = 'error';
          typeName = 'Error';
        }

        const thinkingHtml = st.thinking ? `
          <details class="step-thinking-block" style="cursor: pointer; margin-top: 4px;">
            <summary style="font-weight: 600; font-size: 10.5px; color: #a855f7;">Thought Process</summary>
            <div style="margin-top: 4px; font-style: normal;">${escapeHtml(st.thinking)}</div>
          </details>
        ` : '';

        const toolsHtml = (st.toolCalls && st.toolCalls.length > 0) ? `
          <div style="display: flex; flex-direction: column; gap: 4px; margin-top: 6px;">
            ${st.toolCalls.map(tc => `
              <div class="tool-call-badge">
                <div style="color: #c084fc; font-weight: 700;">Tool: ${escapeHtml(tc.toolName)}</div>
                ${tc.argumentsJson ? `<div style="color: var(--text-dim); margin-top: 2px; white-space: pre-wrap; font-size: 10px;">Args: ${escapeHtml(tc.argumentsJson)}</div>` : ''}
              </div>
            `).join('')}
          </div>
        ` : '';

        const timeStr = st.timestamp ? new Date(st.timestamp).toLocaleTimeString() : '';

        return `
          <div class="timeline-step-item">
            <div class="timeline-step-header">
              <div style="display: flex; align-items: center; gap: 6px;">
                <span style="font-family: var(--font-mono); font-weight: 700; color: var(--text-dim);">#${st.stepIndex}</span>
                <span class="step-type-pill ${pillClass}">${escapeHtml(typeName)}</span>
              </div>
              <span style="color: var(--text-dim); font-size: 10.5px;">${timeStr}</span>
            </div>
            ${st.content ? `<div class="step-content-body">${escapeHtml(st.content)}</div>` : ''}
            ${thinkingHtml}
            ${toolsHtml}
          </div>
        `;
      }).join('');
    }

    async function loadMcpAndSkills() {
      try {
        const res = await callBridge('get_mcp_and_skills');
        const servers = res?.servers || [];
        const skills = res?.skills || [];

        if (mcpCount) mcpCount.textContent = servers.length;
        if (skillsCount) skillsCount.textContent = skills.length;

        renderServers(servers);
        renderSkills(skills);
      } catch (err) {
        showToast('Failed to load MCP & Skills: ' + (err?.message || 'Error'), 'error');
      }

      loadAgentRules();
    }

    async function loadAgentRules() {
      try {
        const res = await callBridge('get_agent_rules');
        const rules = res?.rules || res?.Rules || [];
        currentRulesCache = rules;

        if (rulesCount) rulesCount.textContent = rules.length;

        const activeWsName = res?.activeWorkspaceName || res?.ActiveWorkspaceName || '--';
        if (wsNameEl) wsNameEl.textContent = activeWsName;

        const isIsolated = !!(res?.isIsolated ?? res?.IsIsolated);
        if (chkIsolation) chkIsolation.checked = isIsolated;

        if (isolationDescEl) {
          if (isIsolated) {
            isolationDescEl.textContent = 'Project Rule Isolation Active (Global Rules Ignored)';
            isolationDescEl.style.color = '#f59e0b';
          } else {
            isolationDescEl.textContent = 'Inheriting Global Rules + Project Rules';
            isolationDescEl.style.color = 'var(--text-dim)';
          }
        }

        renderRules(rules);
      } catch (err) {
        console.error('Failed to load agent rules:', err);
      }
    }

    window.refreshAgentRules = loadAgentRules;

    function renderRules(rules) {
      if (!rulesGrid) return;
      rulesGrid.innerHTML = '';

      if (!rules || rules.length === 0) {
        rulesGrid.innerHTML = '<div style="grid-column: 1/-1; text-align: center; color: var(--text-dim); padding: 22px;">No agent rules discovered. Click "Add Rule" or "Install via NPX" to configure governance rules.</div>';
        return;
      }

      rules.forEach(r => {
        const id = r.id || r.Id || '';
        const name = r.name || r.Name || 'Unnamed Rule';
        const desc = r.description || r.Description || 'Agent governance rule';
        const scope = r.scope || r.Scope || 'Project';
        const srcPath = r.sourceFilePath || r.SourceFilePath || '';
        const isEnabled = !!(r.isEnabled ?? r.IsEnabled);
        const isIsolated = !!(r.isExcludedByIsolation ?? r.IsExcludedByIsolation);
        const tokenFootprint = r.tokenFootprintApprox || r.TokenFootprintApprox || 0;

        const isProject = scope.toLowerCase() === 'project';
        const scopeClass = isProject ? 'project' : 'global';
        const scopeLabel = isProject ? 'Project' : 'Global';

        let cardClass = 'rule-card';
        if (!isEnabled) cardClass += ' is-disabled';
        if (isIsolated) cardClass += ' is-isolated';

        let statusBadge = '';
        if (isIsolated) {
          statusBadge = '<span style="font-size: 10.5px; color: #f59e0b; font-weight: 600;">Ignored by Isolation</span>';
        } else if (isEnabled) {
          statusBadge = '<span style="font-size: 10.5px; color: #10b981; font-weight: 600;">Active</span>';
        } else {
          statusBadge = '<span style="font-size: 10.5px; color: var(--text-dim);">Disabled</span>';
        }

        const card = document.createElement('div');
        card.className = cardClass;
        card.innerHTML = `
          <div>
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px; gap: 8px;">
              <div style="display: flex; align-items: center; gap: 6px; overflow: hidden;">
                <svg class="icon-svg" viewBox="0 0 24 24" style="width: 14px; height: 14px; flex-shrink: 0; color: ${isProject ? '#10b981' : '#c084fc'};"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><polyline points="14 2 14 8 20 8"/></svg>
                <span style="font-size: 13px; font-weight: 700; color: var(--text-main); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;" title="${escapeHtml(name)}">${escapeHtml(name)}</span>
              </div>
              <div style="display: flex; align-items: center; gap: 6px; flex-shrink: 0;">
                <span class="badge-rule-scope ${scopeClass}">${escapeHtml(scopeLabel)}</span>
                <span class="rule-token-badge" title="Estimated token overhead in agent context">~${tokenFootprint} tok</span>
              </div>
            </div>

            <div style="font-size: 11.5px; color: var(--text-dim); line-height: 1.4; margin-bottom: 6px;">
              ${escapeHtml(desc)}
            </div>

            <div style="font-size: 10px; font-family: var(--font-mono); color: var(--text-dim); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; margin-bottom: 2px;" title="${escapeHtml(srcPath)}">
              ${escapeHtml(srcPath)}
            </div>
          </div>

          <div style="display: flex; align-items: center; justify-content: space-between; border-top: 1px solid var(--border-subtle); padding-top: 8px; margin-top: 6px;">
            <div style="display: flex; align-items: center; gap: 8px;">
              <label class="toggle-switch" title="${isIsolated ? 'Rule is currently bypassed by Project Rule Isolation' : 'Toggle rule on/off for current workspace'}">
                <input type="checkbox" class="chk-rule-toggle" data-id="${escapeHtml(id)}" ${isEnabled ? 'checked' : ''} ${isIsolated ? 'disabled' : ''} />
                <span class="toggle-slider"></span>
              </label>
              ${statusBadge}
            </div>

            <div style="display: flex; align-items: center; gap: 6px;">
              <button class="btn btn-outline btn-edit-rule" data-id="${escapeHtml(id)}" type="button" style="font-size: 10.5px; padding: 2px 8px; cursor: pointer;">Edit</button>
              <button class="btn btn-outline btn-del-rule" data-id="${escapeHtml(id)}" data-scope="${escapeHtml(scope)}" data-src="${escapeHtml(srcPath)}" type="button" style="font-size: 10.5px; padding: 2px 7px; color: #f87171; border-color: rgba(248,113,113,0.3); cursor: pointer;">Delete</button>
            </div>
          </div>
        `;
        rulesGrid.appendChild(card);
      });

      rulesGrid.querySelectorAll('.chk-rule-toggle').forEach(chk => {
        chk.addEventListener('change', async (e) => {
          const ruleId = e.target.dataset.id;
          const enabled = e.target.checked;
          try {
            const res = await callBridge('toggle_workspace_rule', { ruleId, enabled });
            if (res && res.success) {
              showToast(`Rule '${ruleId}' ${enabled ? 'enabled' : 'disabled'}.`, 'success', 2000);
              loadAgentRules();
            } else {
              e.target.checked = !enabled;
              showToast('Failed to toggle rule.', 'error');
            }
          } catch (err) {
            e.target.checked = !enabled;
            showToast('Error toggling rule: ' + (err?.message || 'Error'), 'error');
          }
        });
      });

      rulesGrid.querySelectorAll('.btn-edit-rule').forEach(btn => {
        btn.addEventListener('click', () => {
          const ruleId = btn.dataset.id;
          const rule = currentRulesCache.find(r => (r.id || r.Id) === ruleId);
          if (rule) openRuleEditor(rule);
        });
      });

      rulesGrid.querySelectorAll('.btn-del-rule').forEach(btn => {
        btn.addEventListener('click', async () => {
          const ruleId = btn.dataset.id;
          const scope = btn.dataset.scope;
          const sourceFilePath = btn.dataset.src;
          if (confirm(`Are you sure you want to delete rule '${ruleId}'?`)) {
            try {
              const res = await callBridge('delete_agent_rule', { id: ruleId, scope, sourceFilePath });
              if (res && res.success) {
                showToast(`Rule '${ruleId}' deleted.`, 'info');
                loadAgentRules();
              } else {
                showToast('Failed to delete rule.', 'error');
              }
            } catch (err) {
              showToast('Delete error: ' + (err?.message || 'Error'), 'error');
            }
          }
        });
      });
    }

    chkIsolation?.addEventListener('change', async (e) => {
      const isolate = e.target.checked;
      try {
        const res = await callBridge('set_workspace_rule_isolation', { isolate });
        if (res && res.success) {
          showToast(isolate ? 'Project rules isolated (global rules disabled).' : 'Project now inherits global rules.', 'info', 3000);
          loadAgentRules();
        } else {
          e.target.checked = !isolate;
          showToast('Failed to update rule isolation.', 'error');
        }
      } catch (err) {
        e.target.checked = !isolate;
        showToast('Isolation error: ' + (err?.message || 'Error'), 'error');
      }
    });

    function openRuleEditor(rule) {
      if (!modalRule) return;
      const isEdit = !!rule;
      const titleEl = document.getElementById('modal-rule-title');
      if (titleEl) titleEl.textContent = isEdit ? `Edit Rule: ${rule.name || rule.Name}` : 'Add Agent Governance Rule';

      const idEl = document.getElementById('modal-rule-id');
      const srcEl = document.getElementById('modal-rule-source-path');
      const nameEl = document.getElementById('modal-rule-name');
      const scopeEl = document.getElementById('modal-rule-scope');
      const descEl = document.getElementById('modal-rule-desc');
      const contentEl = document.getElementById('modal-rule-content');

      if (idEl) idEl.value = isEdit ? (rule.id || rule.Id) : '';
      if (srcEl) srcEl.value = isEdit ? (rule.sourceFilePath || rule.SourceFilePath || '') : '';
      if (nameEl) {
        nameEl.value = isEdit ? (rule.name || rule.Name) : '';
        nameEl.disabled = isEdit;
      }
      if (scopeEl) scopeEl.value = isEdit ? (rule.scope || rule.Scope || 'Project') : 'Project';
      if (descEl) descEl.value = isEdit ? (rule.description || rule.Description || '') : '';
      if (contentEl) contentEl.value = isEdit ? (rule.content || rule.Content || '') : '';

      if (btnDeleteRuleModal) {
        btnDeleteRuleModal.classList.toggle('hidden', !isEdit);
      }

      modalRule.style.display = 'flex';
    }

    btnAddRule?.addEventListener('click', () => {
      openRuleEditor(null);
    });

    btnCancelRule?.addEventListener('click', () => {
      if (modalRule) modalRule.style.display = 'none';
    });

    btnDeleteRuleModal?.addEventListener('click', async () => {
      const ruleId = document.getElementById('modal-rule-id')?.value;
      const scope = document.getElementById('modal-rule-scope')?.value;
      const sourceFilePath = document.getElementById('modal-rule-source-path')?.value;
      if (!ruleId) return;

      if (confirm(`Delete rule '${ruleId}'?`)) {
        try {
          const res = await callBridge('delete_agent_rule', { id: ruleId, scope, sourceFilePath });
          if (res && res.success) {
            showToast(`Rule '${ruleId}' deleted.`, 'info');
            if (modalRule) modalRule.style.display = 'none';
            loadAgentRules();
          } else {
            showToast('Failed to delete rule.', 'error');
          }
        } catch (err) {
          showToast('Delete error: ' + (err?.message || 'Error'), 'error');
        }
      }
    });

    btnSaveRule?.addEventListener('click', async () => {
      const id = document.getElementById('modal-rule-id')?.value.trim() || '';
      const name = document.getElementById('modal-rule-name')?.value.trim() || '';
      const scope = document.getElementById('modal-rule-scope')?.value || 'Project';
      const description = document.getElementById('modal-rule-desc')?.value.trim() || '';
      const content = document.getElementById('modal-rule-content')?.value || '';
      const sourceFilePath = document.getElementById('modal-rule-source-path')?.value || '';

      if (!name) {
        showToast('Rule identifier / name is required.', 'warning');
        return;
      }

      const ruleObj = {
        id: id || name.toLowerCase().replace(/[^a-z0-9_-]/g, '-'),
        name,
        scope,
        description: description || name,
        content: content || `# ${name}\n\nAdd rule directives here...`,
        sourceFilePath,
        isEnabled: true
      };

      btnSaveRule.disabled = true;
      btnSaveRule.textContent = 'Saving...';
      try {
        const res = await callBridge('save_agent_rule', { rule: ruleObj });
        if (res && res.success) {
          showToast(`Rule '${name}' saved successfully.`, 'success');
          if (modalRule) modalRule.style.display = 'none';
          loadAgentRules();
        } else {
          showToast('Failed to save rule: ' + (res?.message || 'Error'), 'error');
        }
      } catch (err) {
        showToast('Save rule error: ' + (err?.message || 'Error'), 'error');
      } finally {
        btnSaveRule.disabled = false;
        btnSaveRule.textContent = 'Save Rule';
      }
    });

    btnNpxInstaller?.addEventListener('click', () => {
      const activeWs = (state.workspaces || []).find(w => w.id === state.activeWorkspaceId) || state.workspaces?.[0];
      const workDir = activeWs?.workingDirectory || activeWs?.path || 'Current Workspace';
      if (workdirNpxEl) workdirNpxEl.textContent = workDir;
      if (modalNpx) modalNpx.style.display = 'flex';
    });

    btnCancelNpx?.addEventListener('click', () => {
      if (modalNpx) modalNpx.style.display = 'none';
    });

    selectNpxPreset?.addEventListener('change', (e) => {
      const val = e.target.value;
      if (val && val !== 'custom') {
        if (inputNpxCmd) inputNpxCmd.value = val;
      }
    });

    btnLaunchNpx?.addEventListener('click', async () => {
      const command = inputNpxCmd?.value.trim();
      if (!command) {
        showToast('Command cannot be empty.', 'warning');
        return;
      }

      const modeRadio = document.querySelector('input[name="npx-exec-mode"]:checked');
      const mode = modeRadio ? modeRadio.value : 'embedded';

      btnLaunchNpx.disabled = true;
      btnLaunchNpx.textContent = 'Launching...';

      try {
        const res = await callBridge('launch_interactive_npx_installer', {
          command,
          mode
        });

        if (res && res.success) {
          if (modalNpx) modalNpx.style.display = 'none';
          if (mode === 'embedded') {
            showToast(`Launched '${command}' in terminal tab.`, 'info', 3000);
            const termNavBtn = document.querySelector('[data-view="terminals"]');
            if (termNavBtn) termNavBtn.click();
          } else {
            showToast(`Launched interactive installer in dedicated console window.`, 'success', 4000);
          }
        } else {
          showToast('Failed to launch NPX installer: ' + (res?.message || 'Unknown error'), 'error');
        }
      } catch (err) {
        showToast('Launch installer error: ' + (err?.message || 'Error'), 'error');
      } finally {
        btnLaunchNpx.disabled = false;
        btnLaunchNpx.textContent = 'Launch Installer';
      }
    });

    function renderServers(servers) {
      if (!mcpContainer) return;
      mcpContainer.innerHTML = '';
      if (!servers || servers.length === 0) {
        mcpContainer.innerHTML = '<div style="grid-column: 1/-1; text-align: center; color: var(--text-dim); padding: 18px;">No MCP servers configured yet. Click "Add MCP Server" to install one.</div>';
        return;
      }

      servers.forEach(s => {
        const id = s.id || s.Id || '';
        const name = s.name || s.Name || 'Unnamed Server';
        const scope = s.scope || s.Scope || 'Global';
        const command = s.command || s.Command || '';
        const args = s.args || s.Args || [];
        const description = s.description || s.Description || (command + ' ' + (Array.isArray(args) ? args.join(' ') : ''));

        const requiresAuth = !!(s.requiresAuth ?? s.RequiresAuth);
        const authStatus = s.authStatus || s.AuthStatus || 'NotRequired';
        const authDetails = s.authDetails || s.AuthDetails || '';
        const authKeyName = s.authKeyName || s.AuthKeyName || 'API_KEY';

        let authBadgeHtml = '';
        if (requiresAuth) {
          if (authStatus === 'Authenticated') {
            authBadgeHtml = '<span class="badge-auth badge-auth-ok">✓ Authenticated</span>';
          } else if (authStatus === 'TokenExpired') {
            authBadgeHtml = '<span class="badge-auth badge-auth-expired">⚠ Token Expired</span>';
          } else {
            authBadgeHtml = '<span class="badge-auth badge-auth-missing">✗ Key Required</span>';
          }
        } else {
          authBadgeHtml = '<span class="badge-auth badge-auth-local">Local Tool</span>';
        }

        const card = document.createElement('div');
        card.style.cssText = 'background: var(--bg-input); border: 1px solid var(--border-subtle); border-radius: 6px; padding: 10px 12px; display: flex; flex-direction: column; justify-content: space-between;';
        card.innerHTML = `
          <div>
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 4px; gap: 8px;">
              <span style="font-size: 13px; font-weight: 700; color: var(--text-main);">${escapeHtml(name)}</span>
              <div style="display: flex; align-items: center; gap: 6px;">
                ${authBadgeHtml}
                <span style="font-size: 10px; font-weight: 600; padding: 2px 6px; border-radius: 4px; background: rgba(168,85,247,0.15); color: #c084fc;">${escapeHtml(scope)}</span>
              </div>
            </div>
            <div style="font-size: 11px; color: var(--text-dim); margin-bottom: 6px; line-height: 1.4;">${escapeHtml(description)}</div>
            ${requiresAuth && authDetails ? `
              <div style="font-size: 10.5px; color: ${authStatus === 'TokenExpired' ? '#f59e0b' : authStatus === 'NeedsAuth' ? '#ef4444' : 'var(--text-dim)'}; margin-bottom: 6px; display: flex; align-items: center; gap: 4px;">
                <span>${escapeHtml(authDetails)}</span>
              </div>
            ` : ''}
          </div>
          <div style="display: flex; align-items: center; justify-content: space-between; border-top: 1px solid var(--border-subtle); padding-top: 6px; margin-top: 6px;">
            <div style="display: flex; align-items: center; gap: 6px;">
              <span style="font-size: 10.5px; color: #10b981; font-weight: 600;">Active</span>
              ${requiresAuth ? `
                <button class="btn btn-outline btn-auth-mcp" data-name="${escapeHtml(name)}" data-keyname="${escapeHtml(authKeyName)}" data-status="${escapeHtml(authStatus)}" data-details="${escapeHtml(authDetails)}" type="button" style="font-size: 10px; padding: 2px 7px; color: var(--accent-cyan); border-color: rgba(6,182,212,0.3); cursor: pointer;">
                  ${authStatus === 'Authenticated' ? 'Edit Key' : 'Configure Key / Auth'}
                </button>
              ` : ''}
            </div>
            ${!id.startsWith('agy_') && !id.startsWith('claude_') ? `
              <button class="btn btn-outline btn-del-mcp" data-id="${escapeHtml(id)}" type="button" style="font-size: 10.5px; padding: 2px 6px; color: #f87171; cursor: pointer;">Remove</button>
            ` : '<span style="font-size: 10px; color: var(--text-dim);">Integrated</span>'}
          </div>
        `;
        mcpContainer.appendChild(card);
      });

      mcpContainer.querySelectorAll('.btn-auth-mcp').forEach(b => {
        b.addEventListener('click', () => {
          const sName = b.dataset.name;
          const kName = b.dataset.keyname || 'API_KEY';
          const sStatus = b.dataset.status || 'NeedsAuth';
          const sDetails = b.dataset.details || '';

          if (inputHiddenServer) inputHiddenServer.value = sName;
          if (inputHiddenKey) inputHiddenKey.value = kName;
          if (authKeyInput) {
            authKeyInput.value = '';
            authKeyInput.type = 'password';
          }
          if (btnToggleKeyVis) btnToggleKeyVis.textContent = 'Show';
          if (authFeedback) authFeedback.style.display = 'none';

          if (authTitle) authTitle.textContent = `Configure Authentication: ${sName}`;
          if (authServerDisplay) authServerDisplay.textContent = sName;
          if (authDetails) authDetails.textContent = sDetails || `Configure access credentials for ${sName}.`;

          if (authBadge) {
            if (sStatus === 'Authenticated') {
              authBadge.className = 'badge-auth badge-auth-ok';
              authBadge.textContent = '✓ Authenticated';
            } else if (sStatus === 'TokenExpired') {
              authBadge.className = 'badge-auth badge-auth-expired';
              authBadge.textContent = '⚠ Token Expired';
            } else {
              authBadge.className = 'badge-auth badge-auth-missing';
              authBadge.textContent = '✗ Key Required';
            }
          }

          if (sName.toLowerCase().includes('supabase')) {
            if (authKeyLabel) authKeyLabel.textContent = 'Supabase Personal Access Token (or Service Role Key):';
            if (authKeyInput) authKeyInput.placeholder = 'sbp_... or ey...';
            if (authLink) {
              authLink.style.display = 'inline-flex';
              authLink.onclick = (e) => {
                e.preventDefault();
                callBridge('open_url_external', { url: 'https://supabase.com/dashboard/account/tokens' });
              };
            }
          } else if (sName.toLowerCase().includes('github')) {
            if (authKeyLabel) authKeyLabel.textContent = 'GitHub Personal Access Token:';
            if (authKeyInput) authKeyInput.placeholder = 'ghp_...';
            if (authLink) {
              authLink.style.display = 'inline-flex';
              authLink.onclick = (e) => {
                e.preventDefault();
                callBridge('open_url_external', { url: 'https://github.com/settings/tokens' });
              };
            }
          } else {
            if (authKeyLabel) authKeyLabel.textContent = `${kName}:`;
            if (authKeyInput) authKeyInput.placeholder = 'Enter key or token...';
            if (authLink) authLink.style.display = 'none';
          }

          if (modalAuth) modalAuth.style.display = 'flex';
        });
      });

      mcpContainer.querySelectorAll('.btn-del-mcp').forEach(b => {
        b.addEventListener('click', async () => {
          if (confirm('Remove this MCP server?')) {
            await callBridge('delete_mcp_server', { id: b.dataset.id });
            loadMcpAndSkills();
            showToast('MCP server removed.', 'info', 2500);
          }
        });
      });
    }

    // Skills search & filter toolbar events
    document.querySelectorAll('.skill-filter-pill').forEach(pill => {
      pill.addEventListener('click', () => {
        document.querySelectorAll('.skill-filter-pill').forEach(p => p.classList.remove('active'));
        pill.classList.add('active');
        activeSkillFilter = pill.dataset.filter || 'all';
        applySkillsFilter();
      });
    });

    skillSearchInput?.addEventListener('input', (e) => {
      skillSearchQuery = (e.target.value || '').trim().toLowerCase();
      applySkillsFilter();
    });

    function renderSkills(skills) {
      allSkillsCache = skills || [];

      if (filterCountAll) filterCountAll.textContent = allSkillsCache.length;
      if (filterCountProject) {
        filterCountProject.textContent = allSkillsCache.filter(s => (s.scope || s.Scope || '').toLowerCase() === 'project').length;
      }
      if (filterCountBuiltin) {
        filterCountBuiltin.textContent = allSkillsCache.filter(s => (s.scope || s.Scope || '').toLowerCase() === 'builtin').length;
      }

      applySkillsFilter();
    }

    function applySkillsFilter() {
      if (!skillsGrid) return;

      let filtered = allSkillsCache;

      // Filter by scope tab
      if (activeSkillFilter === 'project') {
        filtered = filtered.filter(s => (s.scope || s.Scope || '').toLowerCase() === 'project');
      } else if (activeSkillFilter === 'builtin') {
        filtered = filtered.filter(s => (s.scope || s.Scope || '').toLowerCase() === 'builtin');
      }

      // Filter by search query
      if (skillSearchQuery) {
        filtered = filtered.filter(s => {
          const name = (s.name || s.Name || '').toLowerCase();
          const desc = (s.description || s.Description || '').toLowerCase();
          const path = (s.path || s.Path || '').toLowerCase();
          const wsName = (s.workspaceName || s.WorkspaceName || '').toLowerCase();
          return name.includes(skillSearchQuery) ||
                 desc.includes(skillSearchQuery) ||
                 path.includes(skillSearchQuery) ||
                 wsName.includes(skillSearchQuery);
        });
      }

      renderFilteredSkills(filtered);
    }

    function renderFilteredSkills(skills) {
      if (!skillsGrid) return;
      skillsGrid.innerHTML = '';

      if (!skills || skills.length === 0) {
        skillsGrid.innerHTML = '<div style="grid-column: 1/-1; text-align: center; color: var(--text-dim); padding: 22px;">No skills match your filter or search query.</div>';
        return;
      }

      skills.forEach(sk => {
        const id = sk.id || sk.Id || '';
        const name = sk.name || sk.Name || 'Unnamed Skill';
        const rawScope = sk.scope || sk.Scope || 'Builtin';
        const isProject = rawScope.toLowerCase() === 'project';
        const scope = isProject ? 'Project' : 'Builtin';
        const description = sk.description || sk.Description || 'Agent capability extension';
        const path = sk.path || sk.Path || '';
        const wsId = sk.workspaceId || sk.WorkspaceId || '';
        const wsName = sk.workspaceName || sk.WorkspaceName || '';
        const tokenCount = sk.tokenFootprintApprox || sk.TokenFootprintApprox || Math.max(1, Math.round(path.length / 2));

        const card = document.createElement('div');
        card.className = 'skill-card';

        let scopeBadgeHtml = '';
        if (isProject) {
          const badgeLabel = wsName ? `Project · ${escapeHtml(wsName)}` : 'Project';
          scopeBadgeHtml = `<button type="button" class="badge-skill-scope project btn-skill-jump-ws" data-wsid="${escapeHtml(wsId)}" data-wsname="${escapeHtml(wsName)}" data-path="${escapeHtml(path)}" title="Belongs to workspace: ${escapeHtml(wsName || 'Workspace')}. Click to switch workspace & inspect directives.">${badgeLabel}</button>`;
        } else {
          scopeBadgeHtml = `<span class="badge-skill-scope builtin" title="Core Antigravity Builtin Skill">Builtin</span>`;
        }

        card.innerHTML = `
          <div>
            <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px; gap: 8px;">
              <div style="display: flex; align-items: center; gap: 6px; overflow: hidden;">
                <svg class="icon-svg" viewBox="0 0 24 24" style="width: 14px; height: 14px; flex-shrink: 0; color: ${isProject ? '#38bdf8' : 'var(--text-dim)'};"><rect width="18" height="18" x="3" y="3" rx="2"/><path d="M7 7h10M7 12h10M7 17h10"/></svg>
                <span style="font-size: 13px; font-weight: 700; color: var(--text-main); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;" title="${escapeHtml(name)}">${escapeHtml(name)}</span>
              </div>
              <div style="display: flex; align-items: center; gap: 6px; flex-shrink: 0;">
                ${scopeBadgeHtml}
                <span class="skill-token-badge" title="Estimated prompt token overhead">~${tokenCount} tok</span>
              </div>
            </div>

            <div style="font-size: 11.5px; color: var(--text-dim); line-height: 1.4; margin-bottom: 6px; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;">
              ${escapeHtml(description)}
            </div>

            <div class="skill-path-line" data-path="${escapeHtml(path)}" style="font-size: 10px; font-family: var(--font-mono); color: var(--text-dim); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer;" title="Click to reveal in Explorer: ${escapeHtml(path)}">
              ${escapeHtml(path)}
            </div>
          </div>

          <div style="display: flex; align-items: center; justify-content: space-between; border-top: 1px solid var(--border-subtle); padding-top: 8px; margin-top: 6px;">
            <span style="font-size: 10.5px; color: ${isProject ? '#38bdf8' : 'var(--text-dim)'}; font-weight: 600;">
              ${isProject ? 'Project (Editable)' : 'Builtin (Read-Only)'}
            </span>

            <div style="display: flex; align-items: center; gap: 4px;">
              <button type="button" class="skill-action-btn btn-card-explorer" data-path="${escapeHtml(path)}" title="Reveal in File Explorer">
                <svg class="icon-svg" viewBox="0 0 24 24" style="width: 12px; height: 12px;"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>
              </button>
              <button type="button" class="skill-action-btn btn-card-editor" data-path="${escapeHtml(path)}" title="Open in External Code Editor">
                <svg class="icon-svg" viewBox="0 0 24 24" style="width: 12px; height: 12px;"><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/><polyline points="15 3 21 3 21 9"/><line x1="10" y1="14" x2="21" y2="3"/></svg>
              </button>
              ${!isProject ? `
                <button type="button" class="skill-action-btn btn-card-fork" data-path="${escapeHtml(path)}" title="Fork to Active Workspace">
                  <svg class="icon-svg" viewBox="0 0 24 24" style="width: 12px; height: 12px; color: #38bdf8;"><circle cx="12" cy="18" r="3"/><circle cx="6" cy="6" r="3"/><circle cx="18" cy="6" r="3"/><path d="M18 9v1a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V9"/><line x1="12" y1="12" x2="12" y2="15"/></svg>
                </button>
              ` : `
                <button type="button" class="skill-action-btn btn-card-delete" data-path="${escapeHtml(path)}" data-name="${escapeHtml(name)}" title="Delete Skill">
                  <svg class="icon-svg" viewBox="0 0 24 24" style="width: 12px; height: 12px; color: #f87171;"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
                </button>
              `}
              <button type="button" class="btn btn-outline btn-card-inspect" data-path="${escapeHtml(path)}" style="font-size: 10.5px; padding: 2px 8px; cursor: pointer;">
                ${isProject ? 'Edit' : 'View'}
              </button>
            </div>
          </div>
        `;

        skillsGrid.appendChild(card);
      });

      // Attach card event listeners
      skillsGrid.querySelectorAll('.btn-skill-jump-ws').forEach(btn => {
        btn.addEventListener('click', async () => {
          const wsId = btn.dataset.wsid;
          const wsName = btn.dataset.wsname;
          const path = btn.dataset.path;
          if (wsId && wsId !== state.activeWorkspaceId) {
            await switchWorkspace(wsId);
            showToast(`Switched active workspace to: ${wsName || wsId}`, 'success', 2500);
          }
          if (path) openSkillEditor(path);
        });
      });

      skillsGrid.querySelectorAll('.skill-path-line, .btn-card-explorer').forEach(el => {
        el.addEventListener('click', async () => {
          const path = el.dataset.path;
          if (path) {
            await callBridge('reveal_skill_in_explorer', { path });
          }
        });
      });

      skillsGrid.querySelectorAll('.btn-card-editor').forEach(btn => {
        btn.addEventListener('click', async () => {
          const path = btn.dataset.path;
          if (path) {
            await callBridge('open_skill_in_editor', { path });
          }
        });
      });

      skillsGrid.querySelectorAll('.btn-card-fork').forEach(btn => {
        btn.addEventListener('click', async () => {
          const path = btn.dataset.path;
          if (path) {
            await forkSkill(path);
          }
        });
      });

      skillsGrid.querySelectorAll('.btn-card-delete').forEach(btn => {
        btn.addEventListener('click', async () => {
          const path = btn.dataset.path;
          const name = btn.dataset.name || 'this skill';
          if (confirm(`Are you sure you want to delete custom skill '${name}'?`)) {
            try {
              const res = await callBridge('delete_project_skill', { path });
              if (res && res.success) {
                showToast(`Skill '${name}' deleted.`, 'info');
                loadMcpAndSkills();
              } else {
                showToast('Failed to delete skill.', 'error');
              }
            } catch (err) {
              showToast('Delete error: ' + (err?.message || 'Error'), 'error');
            }
          }
        });
      });

      skillsGrid.querySelectorAll('.btn-card-inspect').forEach(btn => {
        btn.addEventListener('click', () => {
          const path = btn.dataset.path;
          if (path) openSkillEditor(path);
        });
      });
    }

    async function forkSkill(sourcePath) {
      try {
        const activeWs = (state.workspaces || []).find(w => w.id === state.activeWorkspaceId) || state.workspaces?.[0];
        const res = await callBridge('fork_skill_to_workspace', {
          sourcePath,
          workspaceId: state.activeWorkspaceId
        });

        if (res && res.success) {
          showToast(`Forked skill into workspace '${activeWs?.name || 'current'}'`, 'success', 3500);
          await loadMcpAndSkills();
          if (res.skill?.path) {
            openSkillEditor(res.skill.path);
          }
        } else {
          showToast('Failed to fork skill: ' + (res?.message || 'Unknown error'), 'error');
        }
      } catch (err) {
        showToast('Fork error: ' + (err?.message || 'Error'), 'error');
      }
    }

    async function openSkillEditor(skillPath) {
      if (!modalSkill) return;

      try {
        const res = await callBridge('get_skill_content', { path: skillPath });
        if (!res) {
          showToast('Could not load skill content.', 'error');
          return;
        }

        const isBuiltin = (res.scope || '').toLowerCase() === 'builtin';
        const name = res.name || 'Skill';
        const desc = res.description || '';
        const content = res.content || '';
        const tokenCount = res.tokenCount || Math.max(1, Math.round(content.length / 4));

        const matchedItem = allSkillsCache.find(s => (s.path || s.Path) === skillPath);
        const wsId = matchedItem?.workspaceId || matchedItem?.WorkspaceId || '';
        const wsName = matchedItem?.workspaceName || matchedItem?.WorkspaceName || '';

        const titleEl = document.getElementById('modal-skill-title');
        if (titleEl) titleEl.textContent = `Agent Skill: ${name}`;

        const hiddenPath = document.getElementById('modal-skill-path');
        if (hiddenPath) hiddenPath.value = skillPath;

        const hiddenScope = document.getElementById('modal-skill-scope');
        if (hiddenScope) hiddenScope.value = res.scope || (isBuiltin ? 'Builtin' : 'Project');

        const hiddenWsId = document.getElementById('modal-skill-wsid');
        if (hiddenWsId) hiddenWsId.value = wsId;

        const scopeBadge = document.getElementById('modal-skill-scope-badge');
        if (scopeBadge) {
          scopeBadge.className = 'badge-skill-scope ' + (isBuiltin ? 'builtin' : 'project');
          scopeBadge.textContent = isBuiltin ? 'Builtin' : (wsName ? `Project · ${wsName}` : 'Project');
        }

        const tokenBadge = document.getElementById('modal-skill-token-badge');
        if (tokenBadge) {
          tokenBadge.textContent = `~${tokenCount} tok`;
        }

        const scopeDesc = document.getElementById('modal-skill-scope-desc');
        const btnJumpWs = document.getElementById('btn-modal-skill-jump-ws');
        if (isBuiltin) {
          if (scopeDesc) scopeDesc.textContent = 'Core Antigravity Builtin Skill (Immutable). Read directives below, or click "Fork to Workspace" to customize.';
          if (btnJumpWs) btnJumpWs.style.display = 'none';
        } else {
          if (scopeDesc) scopeDesc.textContent = `Project Workspace Skill for ${wsName || 'active project'}.`;
          if (btnJumpWs) {
            btnJumpWs.style.display = (wsId && wsId !== state.activeWorkspaceId) ? 'inline-flex' : 'none';
            btnJumpWs.textContent = `Switch to ${wsName || 'Workspace'}`;
            btnJumpWs.onclick = async () => {
              if (wsId) {
                await switchWorkspace(wsId);
                showToast(`Switched active workspace to: ${wsName || wsId}`, 'success', 2500);
                btnJumpWs.style.display = 'none';
              }
            };
          }
        }

        const inputName = document.getElementById('modal-skill-name');
        const inputDesc = document.getElementById('modal-skill-desc');
        const textContent = document.getElementById('modal-skill-content');
        const pathDisplay = document.getElementById('modal-skill-path-display');
        const editHint = document.getElementById('modal-skill-edit-hint');

        if (inputName) {
          inputName.value = name;
          inputName.disabled = isBuiltin;
        }
        if (inputDesc) {
          inputDesc.value = desc;
          inputDesc.disabled = isBuiltin;
        }
        if (pathDisplay) {
          pathDisplay.textContent = skillPath;
        }
        if (textContent) {
          textContent.value = content;
          textContent.readOnly = isBuiltin;
          textContent.style.background = isBuiltin ? 'var(--bg-input)' : '';
        }
        if (editHint) {
          editHint.textContent = isBuiltin ? 'Read-only builtin documentation' : 'Directives saved in SKILL.md';
        }

        // Action buttons
        const btnFork = document.getElementById('btn-modal-fork-skill');
        const btnDelete = document.getElementById('btn-modal-delete-skill');
        const btnSave = document.getElementById('btn-modal-save-skill');

        if (btnFork) btnFork.classList.toggle('hidden', !isBuiltin);
        if (btnDelete) btnDelete.classList.toggle('hidden', isBuiltin);
        if (btnSave) btnSave.classList.toggle('hidden', isBuiltin);

        modalSkill.style.display = 'flex';
      } catch (err) {
        showToast('Error opening skill: ' + (err?.message || 'Error'), 'error');
      }
    }

    document.getElementById('btn-modal-close-skill')?.addEventListener('click', () => {
      if (modalSkill) modalSkill.style.display = 'none';
    });

    document.getElementById('btn-modal-save-skill')?.addEventListener('click', async () => {
      const skillPath = document.getElementById('modal-skill-path')?.value;
      const name = document.getElementById('modal-skill-name')?.value.trim();
      const description = document.getElementById('modal-skill-desc')?.value.trim();
      const content = document.getElementById('modal-skill-content')?.value;

      if (!skillPath || !name) {
        showToast('Skill identifier/name is required.', 'warning');
        return;
      }

      const btnSave = document.getElementById('btn-modal-save-skill');
      if (btnSave) {
        btnSave.disabled = true;
        btnSave.textContent = 'Saving...';
      }

      try {
        const res = await callBridge('save_skill_content', {
          path: skillPath,
          name,
          description,
          content
        });

        if (res && res.success) {
          showToast(`Skill '${name}' saved successfully.`, 'success');
          if (modalSkill) modalSkill.style.display = 'none';
          loadMcpAndSkills();
        } else {
          showToast('Failed to save skill: ' + (res?.message || 'Unknown error'), 'error');
        }
      } catch (err) {
        showToast('Save skill error: ' + (err?.message || 'Error'), 'error');
      } finally {
        if (btnSave) {
          btnSave.disabled = false;
          btnSave.textContent = 'Save Changes';
        }
      }
    });

    document.getElementById('btn-modal-fork-skill')?.addEventListener('click', async () => {
      const skillPath = document.getElementById('modal-skill-path')?.value;
      if (skillPath) {
        await forkSkill(skillPath);
      }
    });

    document.getElementById('btn-modal-delete-skill')?.addEventListener('click', async () => {
      const skillPath = document.getElementById('modal-skill-path')?.value;
      const name = document.getElementById('modal-skill-name')?.value || 'this skill';
      if (!skillPath) return;

      if (confirm(`Are you sure you want to delete skill '${name}'?`)) {
        try {
          const res = await callBridge('delete_project_skill', { path: skillPath });
          if (res && res.success) {
            showToast(`Skill '${name}' deleted.`, 'info');
            if (modalSkill) modalSkill.style.display = 'none';
            loadMcpAndSkills();
          } else {
            showToast('Failed to delete skill.', 'error');
          }
        } catch (err) {
          showToast('Delete error: ' + (err?.message || 'Error'), 'error');
        }
      }
    });

    document.getElementById('btn-skill-reveal-explorer')?.addEventListener('click', async () => {
      const path = document.getElementById('modal-skill-path')?.value;
      if (path) {
        await callBridge('reveal_skill_in_explorer', { path });
      }
    });

    document.getElementById('btn-skill-open-editor')?.addEventListener('click', async () => {
      const path = document.getElementById('modal-skill-path')?.value;
      if (path) {
        await callBridge('open_skill_in_editor', { path });
      }
    });

    // Add New Skill Modal Handlers
    btnAddSkill?.addEventListener('click', () => {
      const activeWs = (state.workspaces || []).find(w => w.id === state.activeWorkspaceId) || state.workspaces?.[0];
      const workDir = activeWs?.workingDirectory || activeWs?.path || 'Current Workspace';
      const workDirEl = document.getElementById('modal-add-skill-workdir');
      if (workDirEl) workDirEl.textContent = workDir;

      const inputName = document.getElementById('modal-add-skill-name');
      const inputDesc = document.getElementById('modal-add-skill-desc');
      const inputContent = document.getElementById('modal-add-skill-content');

      if (inputName) inputName.value = '';
      if (inputDesc) inputDesc.value = '';
      if (inputContent) inputContent.value = '# Custom Skill Directives\n\n## Overview\nDescribe agent capability directives.\n\n## Guidelines\n- Guideline 1\n- Guideline 2';

      if (modalAddSkill) modalAddSkill.style.display = 'flex';
    });

    document.getElementById('btn-cancel-add-skill')?.addEventListener('click', () => {
      if (modalAddSkill) modalAddSkill.style.display = 'none';
    });

    document.getElementById('btn-save-add-skill')?.addEventListener('click', async () => {
      const name = document.getElementById('modal-add-skill-name')?.value.trim();
      const description = document.getElementById('modal-add-skill-desc')?.value.trim();
      const content = document.getElementById('modal-add-skill-content')?.value;

      if (!name) {
        showToast('Skill identifier / slug is required.', 'warning');
        return;
      }

      const btnSave = document.getElementById('btn-save-add-skill');
      if (btnSave) {
        btnSave.disabled = true;
        btnSave.textContent = 'Creating...';
      }

      try {
        const res = await callBridge('create_project_skill', {
          workspaceId: state.activeWorkspaceId,
          name,
          description: description || name,
          content: content || `# ${name}\n\nSkill directives...`
        });

        if (res && res.success) {
          showToast(`Skill '${name}' created successfully.`, 'success');
          if (modalAddSkill) modalAddSkill.style.display = 'none';
          await loadMcpAndSkills();
          if (res.skill?.path) {
            openSkillEditor(res.skill.path);
          }
        } else {
          showToast('Failed to create skill: ' + (res?.message || 'Error'), 'error');
        }
      } catch (err) {
        showToast('Create skill error: ' + (err?.message || 'Error'), 'error');
      } finally {
        if (btnSave) {
          btnSave.disabled = false;
          btnSave.textContent = 'Create Skill';
        }
      }
    });

    btnRefresh?.addEventListener('click', loadMcpAndSkills);

    btnAdd?.addEventListener('click', () => {
      document.getElementById('modal-mcp-id').value = '';
      document.getElementById('modal-mcp-name').value = '';
      document.getElementById('modal-mcp-cmd').value = 'npx';
      document.getElementById('modal-mcp-args').value = '';
      document.getElementById('modal-mcp-env').value = '';
      document.getElementById('modal-mcp-template').value = '';
      if (modalMcp) modalMcp.style.display = 'flex';
    });

    btnCancelMcp?.addEventListener('click', () => {
      if (modalMcp) modalMcp.style.display = 'none';
    });

    btnSaveMcp?.addEventListener('click', async () => {
      const name = document.getElementById('modal-mcp-name').value.trim();
      const cmd = document.getElementById('modal-mcp-cmd').value.trim();
      const argsRaw = document.getElementById('modal-mcp-args').value.trim();
      const scope = document.getElementById('modal-mcp-scope').value;
      const envRaw = document.getElementById('modal-mcp-env').value.trim();

      if (!name || !cmd) {
        showToast('Server name and command are required.', 'warning');
        return;
      }

      const env = {};
      if (envRaw) {
        envRaw.split('\n').forEach(line => {
          const parts = line.split('=');
          if (parts.length >= 2) env[parts[0].trim()] = parts.slice(1).join('=').trim();
        });
      }

      const server = {
        name,
        command: cmd,
        args: argsRaw ? argsRaw.split(' ') : [],
        scope,
        env
      };

      try {
        await callBridge('save_mcp_server', { server });
        if (modalMcp) modalMcp.style.display = 'none';
        loadMcpAndSkills();
        showToast(`MCP server '${name}' saved.`, 'success');
      } catch (err) {
        showToast('Failed to save MCP server: ' + (err?.message || 'Error'), 'error');
      }
    });

    btnToggleKeyVis?.addEventListener('click', () => {
      if (!authKeyInput) return;
      if (authKeyInput.type === 'password') {
        authKeyInput.type = 'text';
        btnToggleKeyVis.textContent = 'Hide';
      } else {
        authKeyInput.type = 'password';
        btnToggleKeyVis.textContent = 'Show';
      }
    });

    btnCloseAuth?.addEventListener('click', () => {
      if (modalAuth) modalAuth.style.display = 'none';
    });

    btnTestAuth?.addEventListener('click', async () => {
      const serverName = inputHiddenServer?.value || '';
      const keyValue = authKeyInput?.value.trim() || '';
      if (!keyValue) {
        if (authFeedback) {
          authFeedback.className = 'mcp-auth-feedback error';
          authFeedback.textContent = 'Please enter a key or token to test.';
        }
        return;
      }

      btnTestAuth.disabled = true;
      btnTestAuth.textContent = 'Testing...';
      try {
        const res = await callBridge('test_mcp_auth', { serverName, keyValue });
        if (authFeedback) {
          authFeedback.className = 'mcp-auth-feedback ' + (res?.valid ? 'success' : 'error');
          authFeedback.textContent = res?.message || (res?.valid ? 'Key is valid.' : 'Invalid key.');
        }
      } catch (e) {
        if (authFeedback) {
          authFeedback.className = 'mcp-auth-feedback error';
          authFeedback.textContent = 'Test failed: ' + (e?.message || 'Error');
        }
      } finally {
        btnTestAuth.disabled = false;
        btnTestAuth.textContent = 'Test Key';
      }
    });

    btnSaveAuth?.addEventListener('click', async () => {
      const serverName = inputHiddenServer?.value || '';
      const keyName = inputHiddenKey?.value || 'API_KEY';
      const keyValue = authKeyInput?.value.trim() || '';

      if (!keyValue) {
        showToast('Key or token cannot be empty.', 'warning');
        return;
      }

      btnSaveAuth.disabled = true;
      btnSaveAuth.textContent = 'Saving...';
      try {
        const res = await callBridge('save_mcp_auth', { serverName, keyName, keyValue });
        if (res?.success) {
          showToast(`Authentication key saved for ${serverName}.`, 'success');
          if (modalAuth) modalAuth.style.display = 'none';
          loadMcpAndSkills();
        } else {
          showToast(`Failed to update auth for ${serverName}.`, 'error');
        }
      } catch (e) {
        showToast('Save failed: ' + (e?.message || 'Error'), 'error');
      } finally {
        btnSaveAuth.disabled = false;
        btnSaveAuth.textContent = 'Save & Apply';
      }
    });

    window.loadMcpAndSkills = loadMcpAndSkills;
    window.loadAgentRules = loadAgentRules;
    window.loadAgentSessions = loadAgentSessions;
    loadMcpAndSkills();
  }

  // Custom AI CLI Engines Manager
window.setupMcpSkillsManager = setupMcpSkillsManager;
