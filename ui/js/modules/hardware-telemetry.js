  // Real-Time Hardware & System Telemetry Engine
  const TELEMETRY_HISTORY_LENGTH = 20;
  const telemetryState = {
    ramHistory: [],
    pollTimer: null,
    isPaused: false
  };

  function renderSmoothSparkline(lineEl, areaEl, dotEl, dataPoints, width = 240, height = 56, maxVal = 100) {
    if (!lineEl || !areaEl || !dataPoints || dataPoints.length === 0) return;

    const len = dataPoints.length;
    const paddingY = 4;
    const usableHeight = height - paddingY * 2;

    if (len === 1) {
      const val = Math.max(0, Math.min(maxVal, dataPoints[0]));
      const y = (height - paddingY) - (val / maxVal) * usableHeight;
      lineEl.setAttribute('d', `M 0,${y.toFixed(1)} L ${width},${y.toFixed(1)}`);
      areaEl.setAttribute('d', `M 0,${y.toFixed(1)} L ${width},${y.toFixed(1)} L ${width},${height} L 0,${height} Z`);
      if (dotEl) {
        dotEl.setAttribute('cx', width);
        dotEl.setAttribute('cy', y.toFixed(1));
        dotEl.style.display = 'block';
      }
      return;
    }

    const stepX = width / (len - 1);
    const pts = dataPoints.map((val, idx) => {
      const clamped = Math.max(0, Math.min(maxVal, val));
      const y = (height - paddingY) - (clamped / maxVal) * usableHeight;
      return { x: idx * stepX, y };
    });

    let lineD = `M ${pts[0].x.toFixed(1)},${pts[0].y.toFixed(1)}`;
    for (let i = 0; i < pts.length - 1; i++) {
      const p0 = pts[Math.max(0, i - 1)];
      const p1 = pts[i];
      const p2 = pts[i + 1];
      const p3 = pts[Math.min(pts.length - 1, i + 2)];

      const cp1x = p1.x + (p2.x - p0.x) / 6;
      const cp1y = p1.y + (p2.y - p0.y) / 6;
      const cp2x = p2.x - (p3.x - p1.x) / 6;
      const cp2y = p2.y - (p3.y - p1.y) / 6;

      lineD += ` C ${cp1x.toFixed(1)},${cp1y.toFixed(1)} ${cp2x.toFixed(1)},${cp2y.toFixed(1)} ${p2.x.toFixed(1)},${p2.y.toFixed(1)}`;
    }

    const lastPt = pts[pts.length - 1];
    const areaD = `${lineD} L ${width},${height} L 0,${height} Z`;

    lineEl.setAttribute('d', lineD);
    areaEl.setAttribute('d', areaD);

    if (dotEl) {
      dotEl.setAttribute('cx', lastPt.x.toFixed(1));
      dotEl.setAttribute('cy', lastPt.y.toFixed(1));
      dotEl.style.display = 'block';
    }
  }

  function formatNetworkRate(kbps) {
    const value = Number.isFinite(kbps) && kbps > 0 ? kbps : 0;
    if (value >= 1024) return `${(value / 1024).toFixed(1)} MB/s`;
    return `${Math.round(value)} KB/s`;
  }

  function handleHardwareMetrics(payload) {
    if (!payload) return;

    const netKbps = typeof payload.netKbps === 'number' && Number.isFinite(payload.netKbps)
      ? Math.max(0, payload.netKbps)
      : 0;

    const ramMb = typeof payload.ramMb === 'number' ? payload.ramMb : 0;
    const totalRamMb = typeof payload.totalRamMb === 'number' ? payload.totalRamMb : 16384;
    const hermesMemMb = typeof payload.hermesMemMb === 'number' ? payload.hermesMemMb : 0;

    // Header bar badges
    const netEl = document.getElementById('metric-net');
    const ramEl = document.getElementById('metric-ram');
    if (netEl) netEl.textContent = `NET ${formatNetworkRate(netKbps)}`;
    if (ramEl) ramEl.textContent = `RAM ${ramMb} MB`;

    // Ring buffer (last 20 points)
    const ramPercent = totalRamMb > 0 ? (ramMb / totalRamMb) * 100 : 0;
    telemetryState.ramHistory.push(ramPercent);
    if (telemetryState.ramHistory.length > TELEMETRY_HISTORY_LENGTH) {
      telemetryState.ramHistory.shift();
    }

    // Update telemetry card numeric displays
    const netLoadVal = document.getElementById('net-load-val');
    if (netLoadVal) netLoadVal.textContent = formatNetworkRate(netKbps);

    const ramUsageVal = document.getElementById('ram-usage-val');
    const usedGb = (ramMb / 1024).toFixed(1);
    const totalGb = (totalRamMb / 1024).toFixed(1);
    if (ramUsageVal) ramUsageVal.textContent = `${usedGb} GB (${Math.round(ramPercent)}%)`;

    const ramStatsSub = document.getElementById('ram-stats-sub');
    if (ramStatsSub) ramStatsSub.textContent = `Total: ${totalGb} GB`;

    const availMb = Math.max(0, totalRamMb - ramMb);
    const availGb = (availMb / 1024).toFixed(1);
    const ramAvailSub = document.getElementById('ram-avail-sub');
    if (ramAvailSub) ramAvailSub.textContent = `Avail: ${availGb} GB`;

    const hermesMemVal = document.getElementById('hermes-mem-val');
    if (hermesMemVal) hermesMemVal.textContent = `${hermesMemMb} MB`;

    const sysStatusVal = document.getElementById('sys-status-val');
    if (sysStatusVal) {
      if (ramPercent > 90) {
        sysStatusVal.textContent = 'High Load';
        sysStatusVal.style.color = 'var(--accent-red)';
      } else {
        sysStatusVal.textContent = 'Active';
        sysStatusVal.style.color = 'var(--accent-green)';
      }
    }

    // Smooth Bézier sparkline
    const ramLine = document.getElementById('ram-sparkline-line');
    const ramArea = document.getElementById('ram-sparkline-area');
    const ramDot = document.getElementById('ram-sparkline-dot');
    renderSmoothSparkline(ramLine, ramArea, ramDot, telemetryState.ramHistory, 240, 56, 100);
  }

  function updateHardwareMonitorVisibility() {
    const isEnabled = state.settings.enableHardwareMonitor === true;
    const card = document.getElementById('hardware-monitor-card');
    if (card) {
      card.style.display = isEnabled ? 'flex' : 'none';
    }
    if (!isEnabled) {
      pauseHardwareTelemetry();
    } else if (state.activeView === 'dashboard' && !document.hidden) {
      resumeHardwareTelemetry();
    }
    if (typeof window.updateDashboardGridClasses === 'function') {
      window.updateDashboardGridClasses();
    }
  }

  function pollHardwareTelemetry() {
    if (!state.settings.enableHardwareMonitor || telemetryState.isPaused || state.activeView !== 'dashboard') return;
    callBridge('get_system_telemetry').then(data => {
      if (data) handleHardwareMetrics(data);
    }).catch(() => {});
  }

  function pauseHardwareTelemetry() {
    telemetryState.isPaused = true;
    const badge = document.getElementById('sys-telemetry-refresh-badge');
    if (badge) badge.classList.add('is-paused');
  }

  function resumeHardwareTelemetry() {
    if (!state.settings.enableHardwareMonitor) return;
    telemetryState.isPaused = false;
    const badge = document.getElementById('sys-telemetry-refresh-badge');
    if (badge) badge.classList.remove('is-paused');
    pollHardwareTelemetry();
  }

  function setupHardwareTelemetry() {
    updateHardwareMonitorVisibility();

    if (telemetryState.ramHistory.length === 0) {
      for (let i = 0; i < 6; i++) {
        telemetryState.ramHistory.push(0);
      }
    }

    if (state.settings.enableHardwareMonitor) {
      callBridge('get_system_telemetry').then(data => {
        if (data) handleHardwareMetrics(data);
      }).catch(() => {});
    }

    if (telemetryState.pollTimer) clearInterval(telemetryState.pollTimer);
    telemetryState.pollTimer = setInterval(pollHardwareTelemetry, 2000);

    document.addEventListener('visibilitychange', () => {
      if (document.hidden) {
        pauseHardwareTelemetry();
      } else if (state.activeView === 'dashboard' && state.settings.enableHardwareMonitor) {
        resumeHardwareTelemetry();
      }
    });
  }

  // Navigation Logic & Collapsible Sidebar
  // ---- Command palette (Ctrl+K) ----

  const cpState = { open: false, query: '', items: [], selected: 0 };

  // Views are read from the sidebar rather than a second list, so the palette can never
  // disagree with the nav about what exists.
window.setupHardwareTelemetry = setupHardwareTelemetry;

window.handleHardwareMetrics = handleHardwareMetrics;
var handleHardwareMetrics = window.handleHardwareMetrics;
window.updateHardwareMonitorVisibility = updateHardwareMonitorVisibility;
var updateHardwareMonitorVisibility = window.updateHardwareMonitorVisibility;
window.pauseHardwareTelemetry = pauseHardwareTelemetry;
var pauseHardwareTelemetry = window.pauseHardwareTelemetry;
window.resumeHardwareTelemetry = resumeHardwareTelemetry;
var resumeHardwareTelemetry = window.resumeHardwareTelemetry;
