// Centralized Structured Logging System & Execution Inspector
  function updateLogPillCounts() {
    const total = state.logs.length;
    const errors = state.logs.filter(l => l.level === 'error').length;
    const warns = state.logs.filter(l => l.level === 'warn').length;
    const sys = state.logs.filter(l => (l.tag || '').toLowerCase() === 'system').length;
    const cli = state.logs.filter(l => ['cli', 'hermes', 'claude', 'antigravity'].includes((l.tag || '').toLowerCase())).length;
    const opencode = state.logs.filter(l => (l.tag || '').toLowerCase() === 'opencode').length;
    const warp = state.logs.filter(l => ['warp', 'vpn', 'quotareset'].includes((l.tag || '').toLowerCase())).length;

    const elTotal = document.getElementById('logs-total-count');
    if (elTotal) elTotal.textContent = `${total} ${total === 1 ? 'entry' : 'entries'}`;

    const setBadge = (id, count) => {
      const el = document.getElementById(id);
      if (el) el.textContent = count;
    };
    setBadge('count-pill-all', total);
    setBadge('count-pill-error', errors);
    setBadge('count-pill-warn', warns);
    setBadge('count-pill-system', sys);
    setBadge('count-pill-cli', cli);
    setBadge('count-pill-opencode', opencode);
    setBadge('count-pill-warp', warp);

    const pillErr = document.querySelector('.log-filter-pill.pill-error');
    if (pillErr) pillErr.classList.toggle('has-errors', errors > 0);

    const pillWarn = document.querySelector('.log-filter-pill.pill-warn');
    if (pillWarn) pillWarn.classList.toggle('has-warns', warns > 0);
  }

  function appendLog(text, tag = null, timestamp = null, level = null, extraData = null) {
    if (!text && !extraData) return;
    let rawText = text || '';
    let jsonPayload = extraData || null;

    if (typeof rawText === 'object') {
      jsonPayload = jsonPayload || rawText;
      rawText = rawText.message || rawText.error || JSON.stringify(rawText);
    } else {
      rawText = String(rawText);
    }

    if (!jsonPayload) {
      const jsonStart = rawText.indexOf('{');
      if (jsonStart !== -1 && rawText.endsWith('}')) {
        try {
          const parsed = JSON.parse(rawText.substring(jsonStart));
          jsonPayload = parsed;
          rawText = rawText.substring(0, jsonStart).trim() || 'Data payload';
        } catch { }
      }
    }

    const time = timestamp || new Date().toLocaleTimeString();

    // Deduce tag if not explicitly provided
    let inferredTag = tag;
    if (!inferredTag) {
      if (rawText.startsWith('[System]') || rawText.startsWith('[Config]') || rawText.startsWith('[Metrics]')) {
        inferredTag = 'System';
      } else if (rawText.startsWith('[OpenCode]') || rawText.startsWith('[Sidecar]')) {
        inferredTag = 'OpenCode';
      } else if (rawText.startsWith('[WARP]') || rawText.startsWith('[QuotaReset]') || rawText.startsWith('[ProtonVPN]')) {
        inferredTag = 'WARP';
      } else if (rawText.startsWith('[CLI]') || rawText.startsWith('[Shortcuts]') || rawText.startsWith('[Claude]') || rawText.startsWith('[Hermes]') || rawText.startsWith('[Antigravity]')) {
        inferredTag = 'CLI';
      } else {
        inferredTag = 'System';
      }
    }

    let cleanText = rawText;
    if (cleanText.startsWith(`[${inferredTag}]`)) {
      cleanText = cleanText.substring(inferredTag.length + 2).trim();
    }

    let inferredLevel = level;
    if (!inferredLevel) {
      const lower = cleanText.toLowerCase();
      if (lower.includes('error') || lower.includes('exception') || lower.includes('failed') || lower.includes('fail:') || lower.includes('fatal') || lower.includes('critical') || lower.includes('rejected')) {
        inferredLevel = 'error';
      } else if (lower.includes('warn') || lower.includes('warning') || lower.includes('timeout') || lower.includes('timed out') || lower.includes('offline') || lower.includes('could not bind')) {
        inferredLevel = 'warn';
      } else if (lower.includes('success') || lower.includes('installed') || lower.includes('ready') || lower.includes('connected') || lower.includes('saved')) {
        inferredLevel = 'success';
      } else {
        inferredLevel = 'info';
      }
    }

    const logEntry = {
      id: 'log_' + (++logIdCounter),
      timestamp: time,
      tag: inferredTag,
      level: inferredLevel,
      text: cleanText,
      jsonDetails: jsonPayload
    };

    state.logs.push(logEntry);
    if (state.logs.length > 1000) {
      state.logs.shift();
    }

    updateLogPillCounts();

    if (shouldShowLogEntry(logEntry, state.currentLogFilter, state.logSearchQuery)) {
      renderLogEntryDom(logEntry);
    }
  }

  function shouldShowLogEntry(entry, filter, searchQuery) {
    if (filter && filter.toLowerCase() !== 'all') {
      const f = filter.toLowerCase();
      if (f === 'error') {
        if (entry.level !== 'error') return false;
      } else if (f === 'warn') {
        if (entry.level !== 'warn') return false;
      } else if (f === 'system') {
        if ((entry.tag || '').toLowerCase() !== 'system') return false;
      } else if (f === 'cli') {
        if (!['cli', 'hermes', 'claude', 'antigravity'].includes((entry.tag || '').toLowerCase())) return false;
      } else if (f === 'opencode') {
        if ((entry.tag || '').toLowerCase() !== 'opencode') return false;
      } else if (f === 'warp') {
        if (!['warp', 'vpn', 'quotareset'].includes((entry.tag || '').toLowerCase())) return false;
      }
    }

    if (searchQuery && searchQuery.trim()) {
      const q = searchQuery.trim().toLowerCase();
      const inText = (entry.text || '').toLowerCase().includes(q);
      const inTag = (entry.tag || '').toLowerCase().includes(q);
      const inLevel = (entry.level || '').toLowerCase().includes(q);
      const inTime = (entry.timestamp || '').toLowerCase().includes(q);
      let inJson = false;
      if (entry.jsonDetails) {
        try { inJson = JSON.stringify(entry.jsonDetails).toLowerCase().includes(q); } catch { }
      }
      if (!inText && !inTag && !inLevel && !inTime && !inJson) return false;
    }

    return true;
  }

  function highlightSearchText(text, query) {
    if (!query || !query.trim()) return escapeHtml(text);
    const escapedText = escapeHtml(text);
    const escapedQuery = escapeHtml(query.trim());
    try {
      const re = new RegExp(`(${escapedQuery.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')})`, 'gi');
      return escapedText.replace(re, '<mark class="log-search-match">$1</mark>');
    } catch {
      return escapedText;
    }
  }

  function renderLogEntryDom(entry) {
    const logsEl = document.getElementById('logs-container');
    if (!logsEl) return;

    const emptyEl = logsEl.querySelector('.logs-empty-state');
    if (emptyEl) emptyEl.remove();

    const t = (entry.tag || '').toLowerCase();
    let tagClass = 'log-tag-default';
    let tagLabel = entry.tag || 'System';
    if (t === 'system') {
      tagClass = 'log-tag-system';
      tagLabel = 'System';
    } else if (t === 'opencode') {
      tagClass = 'log-tag-opencode';
      tagLabel = 'OpenCode';
    } else if (t === 'warp' || t === 'quotareset' || t === 'vpn') {
      tagClass = 'log-tag-warp';
      tagLabel = 'WARP';
    } else if (t === 'cli' || t === 'hermes' || t === 'claude' || t === 'antigravity') {
      tagClass = 'log-tag-cli';
      tagLabel = 'CLI';
    } else if (t === 'config') {
      tagClass = 'log-tag-config';
      tagLabel = 'Config';
    }

    const levelClass = entry.level ? `log-level-${entry.level}` : 'log-level-info';
    const badgeLevel = (entry.level || 'info').toUpperCase();
    const badgeClass = `level-${entry.level || 'info'}`;

    const line = document.createElement('div');
    line.className = `log-line ${levelClass}`;
    line.setAttribute('data-id', entry.id);

    const hasJson = entry.jsonDetails && typeof entry.jsonDetails === 'object';
    const jsonToggleHtml = hasJson
      ? `<button type="button" class="log-json-toggle" title="Toggle JSON payload">{ JSON }</button>`
      : '';

    const jsonBoxHtml = hasJson
      ? `<pre class="log-json-box" style="display: none;"><code>${escapeHtml(JSON.stringify(entry.jsonDetails, null, 2))}</code></pre>`
      : '';

    line.innerHTML = `
      <span class="log-time">[${escapeHtml(entry.timestamp)}]</span>
      <span class="log-level-badge ${badgeClass}">${escapeHtml(badgeLevel)}</span>
      <span class="log-tag ${tagClass}" title="${escapeHtml(tagLabel)}">${escapeHtml(tagLabel)}</span>
      <div class="log-msg-wrap">
        <span class="log-text">${highlightSearchText(entry.text, state.logSearchQuery)}</span>
        ${jsonToggleHtml}
        ${jsonBoxHtml}
      </div>
    `;

    if (hasJson) {
      const btn = line.querySelector('.log-json-toggle');
      const box = line.querySelector('.log-json-box');
      if (btn && box) {
        btn.addEventListener('click', (e) => {
          e.stopPropagation();
          const isHidden = box.style.display === 'none';
          box.style.display = isHidden ? 'block' : 'none';
          btn.textContent = isHidden ? '{ Close }' : '{ JSON }';
        });
      }
    }

    logsEl.appendChild(line);

    if (state.logAutoScroll) {
      logsEl.scrollTop = logsEl.scrollHeight;
    }
  }

  function refreshLogsDisplay() {
    const logsEl = document.getElementById('logs-container');
    if (!logsEl) return;
    logsEl.innerHTML = '';
    const visibleEntries = state.logs.filter(entry => shouldShowLogEntry(entry, state.currentLogFilter, state.logSearchQuery));

    if (visibleEntries.length === 0) {
      logsEl.innerHTML = `
        <div class="logs-empty-state">
          <svg class="icon-svg" viewBox="0 0 24 24"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
          <div>No logs match the current filter or search criteria.</div>
        </div>
      `;
      return;
    }

    visibleEntries.forEach(entry => renderLogEntryDom(entry));
    if (state.logAutoScroll) {
      logsEl.scrollTop = logsEl.scrollHeight;
    }
  }

  function setupLogFilterListeners() {
    updateLogPillCounts();
    document.querySelectorAll('.log-filter-bar .log-filter-pill').forEach(pill => {
      pill.addEventListener('click', () => {
        document.querySelectorAll('.log-filter-bar .log-filter-pill').forEach(p => p.classList.remove('active'));
        pill.classList.add('active');
        state.currentLogFilter = pill.getAttribute('data-filter') || 'all';
        refreshLogsDisplay();
      });
    });

    document.getElementById('btn-clear-logs')?.addEventListener('click', () => {
      state.logs = [];
      const logsEl = document.getElementById('logs-container');
      if (logsEl) {
        logsEl.innerHTML = `
          <div class="logs-empty-state">
            <svg class="icon-svg" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
            <div>Logs cleared. Waiting for new events...</div>
          </div>
        `;
      }
      updateLogPillCounts();
      showToast('Logs cleared', 'info', 1500);
    });

    document.getElementById('btn-copy-logs')?.addEventListener('click', async () => {
      const visibleEntries = state.logs.filter(entry => shouldShowLogEntry(entry, state.currentLogFilter, state.logSearchQuery));
      if (visibleEntries.length === 0) {
        showToast('No logs to copy', 'info', 2000);
        return;
      }
      const textToCopy = visibleEntries.map(e => {
        let line = `[${e.timestamp}] [${(e.level || 'info').toUpperCase()}] [${e.tag || 'System'}] ${e.text}`;
        if (e.jsonDetails) {
          line += `\n${JSON.stringify(e.jsonDetails, null, 2)}`;
        }
        return line;
      }).join('\n');

      try {
        await navigator.clipboard.writeText(textToCopy);
        showToast(`Copied ${visibleEntries.length} log lines to clipboard`, 'success', 2500);
      } catch {
        const ta = document.createElement('textarea');
        ta.value = textToCopy;
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
        showToast(`Copied ${visibleEntries.length} log lines to clipboard`, 'success', 2500);
      }
    });

    const btnAutoScroll = document.getElementById('btn-toggle-autoscroll');
    btnAutoScroll?.addEventListener('click', () => {
      state.logAutoScroll = !state.logAutoScroll;
      btnAutoScroll.classList.toggle('active', state.logAutoScroll);
      const lbl = document.getElementById('lbl-autoscroll');
      if (lbl) lbl.textContent = state.logAutoScroll ? 'Auto-Scroll' : 'Scroll: Paused';
      if (state.logAutoScroll) {
        const logsEl = document.getElementById('logs-container');
        if (logsEl) logsEl.scrollTop = logsEl.scrollHeight;
      }
    });

    const searchInput = document.getElementById('logs-search-input');
    const clearSearchBtn = document.getElementById('btn-clear-log-search');

    searchInput?.addEventListener('input', () => {
      state.logSearchQuery = searchInput.value;
      if (clearSearchBtn) {
        clearSearchBtn.style.display = searchInput.value ? 'block' : 'none';
      }
      refreshLogsDisplay();
    });

    clearSearchBtn?.addEventListener('click', () => {
      if (searchInput) searchInput.value = '';
      state.logSearchQuery = '';
      clearSearchBtn.style.display = 'none';
      refreshLogsDisplay();
    });

    const logsContainer = document.getElementById('logs-container');
    logsContainer?.addEventListener('scroll', () => {
      const isAtBottom = logsContainer.scrollHeight - logsContainer.scrollTop - logsContainer.clientHeight <= 40;
      if (!isAtBottom && state.logAutoScroll) {
        state.logAutoScroll = false;
        btnAutoScroll?.classList.remove('active');
        const lbl = document.getElementById('lbl-autoscroll');
        if (lbl) lbl.textContent = 'Scroll: Paused';
      } else if (isAtBottom && !state.logAutoScroll) {
        state.logAutoScroll = true;
        btnAutoScroll?.classList.add('active');
        const lbl = document.getElementById('lbl-autoscroll');
        if (lbl) lbl.textContent = 'Auto-Scroll';
      }
    });
  }

window.appendLog = appendLog;
window.setupLogFilterListeners = setupLogFilterListeners;
