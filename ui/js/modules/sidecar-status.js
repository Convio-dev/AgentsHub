/**
 * Sidecar Runtime Status & Health Indicator Module
 * Manages pure icon indicator states, interactive tooltip data binding,
 * and resilient background reconnection with exponential backoff.
 */

(function () {
  'use strict';

  let currentStatus = {
    status: 'Starting',
    port: 0,
    isOnline: false,
    pid: 0,
    latencyMs: -1,
    activeSessions: 0,
    version: 'v2.0.0'
  };

  let reconnectAttempt = 0;
  let reconnectTimer = null;
  let pollTimer = null;

  function initSidecarStatus() {
    const btn = document.getElementById('sidecar-status-btn');
    if (btn) {
      btn.addEventListener('click', () => {
        // Force refresh / reconnection attempt on user click
        fetchStatus(true);
      });
    }

    // Listen to real-time status events from C# bridge
    window.addEventListener('sidecar_status_changed', (evt) => {
      if (evt && evt.detail) {
        updateState(evt.detail);
      }
    });

    // Initial query
    fetchStatus();

    // Regular health poll every 6 seconds
    pollTimer = setInterval(() => {
      fetchStatus(false);
    }, 6000);
  }

  async function fetchStatus(isManual = false) {
    if (!window.bridge) return;

    try {
      const data = await window.bridge.call('get_sidecar_status');
      if (data) {
        updateState(data);
        reconnectAttempt = 0;
        if (reconnectTimer) {
          clearTimeout(reconnectTimer);
          reconnectTimer = null;
        }
      }
    } catch (err) {
      if (!currentStatus.isOnline) {
        scheduleReconnect();
      }
    }
  }

  function scheduleReconnect() {
    if (reconnectTimer) return;
    reconnectAttempt++;
    const delay = Math.min(30000, Math.round(1500 * Math.pow(1.5, reconnectAttempt)));
    
    updateTooltipReconnect(Math.round(delay / 1000));

    reconnectTimer = setTimeout(() => {
      reconnectTimer = null;
      fetchStatus(false);
    }, delay);
  }

  function updateTooltipReconnect(seconds) {
    const statusElem = document.getElementById('sidecar-tooltip-status');
    if (statusElem && !currentStatus.isOnline) {
      statusElem.innerHTML = `<span class="w-1.5 h-1.5 rounded-full bg-rose-500 animate-pulse"></span> Reconnecting in ${seconds}s...`;
    }
  }

  function updateState(data) {
    if (!data) return;

    currentStatus = {
      ...currentStatus,
      ...data,
      isOnline: data.isOnline === true || (typeof data.status === 'string' && data.status.toLowerCase() === 'online')
    };

    renderIndicator();
    renderTooltip();
  }

  function renderIndicator() {
    const slot = document.getElementById('sidecar-icon-slot');
    if (!slot) return;

    if (currentStatus.isOnline) {
      // Connected / Healthy: Emerald pulse dot badge
      slot.innerHTML = `
        <span class="absolute inline-flex h-3 w-3 rounded-full bg-emerald-400 opacity-60 animate-ping"></span>
        <span class="relative inline-flex rounded-full h-2 w-2 bg-emerald-500 shadow-sm shadow-emerald-500/50"></span>
      `;
    } else if (currentStatus.status === 'Starting' || currentStatus.status === 'Connecting') {
      // Connecting / Initializing: Spinning amber ring
      slot.innerHTML = `
        <i class="fa-solid fa-circle-notch fa-spin text-amber-400 text-[11px]"></i>
      `;
    } else {
      // Disconnected / Error: Severed link icon in rose
      slot.innerHTML = `
        <i class="fa-solid fa-link-slash text-rose-500 text-[11px]"></i>
      `;
    }
  }

  function renderTooltip() {
    const badge = document.getElementById('sidecar-tooltip-badge');
    const statusElem = document.getElementById('sidecar-tooltip-status');
    const endpointElem = document.getElementById('sidecar-tooltip-endpoint');
    const healthElem = document.getElementById('sidecar-tooltip-health');
    const sessionsElem = document.getElementById('sidecar-tooltip-sessions');

    const version = currentStatus.version || 'v2.0.0';

    if (badge) {
      if (currentStatus.isOnline) {
        badge.className = 'px-1.5 py-0.5 text-[9px] font-mono rounded bg-emerald-500/15 text-emerald-400 border border-emerald-500/30';
        badge.textContent = `Active (${version})`;
      } else if (currentStatus.status === 'Starting' || currentStatus.status === 'Connecting') {
        badge.className = 'px-1.5 py-0.5 text-[9px] font-mono rounded bg-amber-500/15 text-amber-400 border border-amber-500/30';
        badge.textContent = 'Initializing';
      } else {
        badge.className = 'px-1.5 py-0.5 text-[9px] font-mono rounded bg-rose-500/15 text-rose-400 border border-rose-500/30';
        badge.textContent = 'Offline';
      }
    }

    if (statusElem) {
      if (currentStatus.isOnline) {
        statusElem.className = 'font-medium text-emerald-400 flex items-center gap-1';
        statusElem.innerHTML = `<span class="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse"></span> Connected / Healthy`;
      } else if (currentStatus.status === 'Starting' || currentStatus.status === 'Connecting') {
        statusElem.className = 'font-medium text-amber-400 flex items-center gap-1';
        statusElem.innerHTML = `<span class="w-1.5 h-1.5 rounded-full bg-amber-400 animate-ping"></span> Connecting...`;
      } else {
        statusElem.className = 'font-medium text-rose-400 flex items-center gap-1';
        statusElem.innerHTML = `<span class="w-1.5 h-1.5 rounded-full bg-rose-500"></span> Disconnected`;
      }
    }

    if (endpointElem) {
      if (currentStatus.port > 0) {
        endpointElem.textContent = `127.0.0.1:${currentStatus.port}`;
        endpointElem.className = currentStatus.isOnline ? 'font-mono text-sky-400' : 'font-mono text-content-muted';
      } else {
        endpointElem.textContent = 'Negotiating Port...';
        endpointElem.className = 'font-mono text-content-muted';
      }
    }

    if (healthElem) {
      const pidText = currentStatus.pid > 0 ? `PID ${currentStatus.pid}` : 'PID --';
      const latText = currentStatus.latencyMs >= 0 ? `${currentStatus.latencyMs}ms` : '<1ms';
      healthElem.textContent = currentStatus.isOnline ? `${pidText} • ${latText}` : 'Unreachable';
    }

    if (sessionsElem) {
      const activeCount = typeof currentStatus.activeSessions === 'number' ? currentStatus.activeSessions : 0;
      sessionsElem.textContent = activeCount.toString();
    }
  }

  // Auto-boot on DOMContentLoaded
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSidecarStatus);
  } else {
    initSidecarStatus();
  }

  window.sidecarStatus = {
    getStatus: () => ({ ...currentStatus }),
    refresh: () => fetchStatus(true)
  };
})();
