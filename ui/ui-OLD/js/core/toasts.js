// Modern Dynamic Toast System
  const toastHistoryState = { items: [] };
  let lastToastSignature = '';
  let lastToastTime = 0;

  function showToast(message, type = 'info', durationMs = 4500) {
    let cleanMessage = message;
    if (cleanMessage && typeof cleanMessage === 'object') {
      cleanMessage = cleanMessage.message || cleanMessage.error || JSON.stringify(cleanMessage);
    } else if (cleanMessage !== undefined && cleanMessage !== null) {
      cleanMessage = String(cleanMessage);
    } else {
      cleanMessage = '';
    }

    if (!cleanMessage.trim()) return;

    // Suppress duplicate toasts fired in quick succession (< 1.5s)
    const now = Date.now();
    const signature = `${type}:${cleanMessage}`;
    if (signature === lastToastSignature && now - lastToastTime < 1500) {
      return;
    }
    lastToastSignature = signature;
    lastToastTime = now;

    let container = document.getElementById('toast-container');
    if (!container) {
      container = document.createElement('div');
      container.id = 'toast-container';
      document.body.appendChild(container);
    }

    const toastId = 't_' + now + '_' + Math.random().toString(36).slice(2, 7);

    // Record into history initially (persisted if auto-dismissed)
    recordToast(cleanMessage, type, toastId);

    // Play subtle synthesized audio chime based on notification severity
    if (type === 'success') {
      playAcousticChime('success');
    } else if (type === 'error') {
      playAcousticChime('error');
    } else if (type === 'warning' || type === 'warn') {
      playAcousticChime('prompt');
    }

    const toast = document.createElement('div');
    toast.className = `toast-item toast-${type}`;
    toast.dataset.toastId = toastId;

    let iconSvg = '';
    if (type === 'success') {
      iconSvg = `<svg viewBox="0 0 24 24" width="16" height="16" stroke="#10b981" stroke-width="2.5" fill="none"><path d="M20 6L9 17l-5-5"/></svg>`;
    } else if (type === 'error') {
      iconSvg = `<svg viewBox="0 0 24 24" width="16" height="16" stroke="#ef4444" stroke-width="2.5" fill="none"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>`;
    } else if (type === 'warning') {
      iconSvg = `<svg viewBox="0 0 24 24" width="16" height="16" stroke="#f59e0b" stroke-width="2.5" fill="none"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>`;
    } else {
      iconSvg = `<svg viewBox="0 0 24 24" width="16" height="16" stroke="#06b6d4" stroke-width="2" fill="none"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>`;
    }

    toast.innerHTML = `
      <span class="toast-icon">${iconSvg}</span>
      <span class="toast-message">${escapeHtml(cleanMessage)}</span>
      <button class="toast-close-btn" type="button" title="Dismiss">
        <svg viewBox="0 0 24 24" width="13" height="13" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" fill="none"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
      </button>
    `;

    container.appendChild(toast);

    let dismissTimer = null;
    let isDismissed = false;

    function dismiss(userClosed) {
      if (isDismissed) return;
      isDismissed = true;
      if (dismissTimer) clearTimeout(dismissTimer);

      if (userClosed) {
        // User explicitly closed via X: remove from notifications history!
        removeToastFromHistory(toastId);
      }

      toast.classList.add('toast-out');
      setTimeout(() => {
        if (toast.parentNode) {
          toast.parentNode.removeChild(toast);
        }
      }, 220);
    }

    const closeBtn = toast.querySelector('.toast-close-btn');
    if (closeBtn) {
      closeBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        dismiss(true /* userClosed */);
      });
    }

    dismissTimer = setTimeout(() => {
      dismiss(false /* userClosed - keep in history */);
    }, durationMs);

    toast.addEventListener('mouseenter', () => {
      if (dismissTimer) clearTimeout(dismissTimer);
    });
    toast.addEventListener('mouseleave', () => {
      if (!isDismissed) {
        dismissTimer = setTimeout(() => dismiss(false), 1500);
      }
    });
  }

  function recordToast(message, type, id) {
    const item = {
      id: id || ('t_' + Date.now() + '_' + Math.random().toString(36).slice(2, 7)),
      message,
      type: type || 'info',
      at: new Date().toISOString()
    };
    toastHistoryState.items.unshift(item);
    if (toastHistoryState.items.length > 50) toastHistoryState.items.length = 50;
    updateToastBadge();
    return item;
  }

  function removeToastFromHistory(toastId) {
    if (!toastId) return;
    const idx = toastHistoryState.items.findIndex(it => it.id === toastId);
    if (idx !== -1) {
      toastHistoryState.items.splice(idx, 1);
      updateToastBadge();
      const panel = document.getElementById('toast-history-panel');
      if (panel && panel.classList.contains('open')) {
        renderToastHistory();
      }
    }
  }

  function updateToastBadge() {
    const badge = document.getElementById('toast-history-count');
    if (badge) {
      const n = toastHistoryState.items.length;
      badge.textContent = String(n);
      badge.style.display = n > 0 ? '' : 'none';
    }
  }

  function toggleToastHistory(forceOpen) {
    const panel = document.getElementById('toast-history-panel');
    const badge = document.getElementById('toast-history-count');
    if (!panel) return;

    const willOpen = typeof forceOpen === 'boolean' ? forceOpen : panel.classList.contains('open') === false;
    panel.classList.toggle('open', willOpen);
    if (willOpen) {
      renderToastHistory();
      if (badge) badge.style.display = 'none';
    }
  }

  function renderToastHistory() {
    const list = document.getElementById('toast-history-list');
    if (!list) return;

    list.innerHTML = '';
    if (toastHistoryState.items.length === 0) {
      const empty = document.createElement('div');
      empty.className = 'toast-history-empty';
      empty.textContent = 'No notifications yet.';
      list.appendChild(empty);
      return;
    }

    toastHistoryState.items.forEach(item => {
      const row = document.createElement('div');
      row.className = 'toast-history-item is-' + item.type;

      const when = document.createElement('span');
      when.className = 'toast-history-time';
      const d = new Date(item.at);
      when.textContent = isNaN(d.getTime()) ? '' : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });

      const body = document.createElement('span');
      body.className = 'toast-history-message';
      body.textContent = item.message;

      const delBtn = document.createElement('button');
      delBtn.className = 'toast-history-del-btn';
      delBtn.type = 'button';
      delBtn.title = 'Remove notification';
      delBtn.innerHTML = `<svg viewBox="0 0 24 24" width="12" height="12" stroke="currentColor" stroke-width="2" fill="none"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>`;
      delBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        removeToastFromHistory(item.id);
      });

      row.appendChild(when);
      row.appendChild(body);
      row.appendChild(delBtn);
      list.appendChild(row);
    });
  }

  function clearToastHistory() {
    toastHistoryState.items = [];
    renderToastHistory();
    updateToastBadge();
  }

  // AI Providers & Models Setup View
window.showToast = showToast;
var showToast = window.showToast;
window.toggleToastHistory = toggleToastHistory;
window.clearToastHistory = clearToastHistory;
