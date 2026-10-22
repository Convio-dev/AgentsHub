/**
 * Architecture & Blast-Radius Graph View Module
 * High-performance 2D Canvas force-directed graph renderer with caller blast-zone analysis.
 */
(function() {
  let graphData = { nodes: [], edges: [], stats: {} };
  let selectedNode = null;
  let blastReport = null;
  let isPhysicsRunning = true;
  let animFrameId = null;
  let canvas = null;
  let ctx = null;

  // Viewport transformation
  let panX = 0;
  let panY = 0;
  let zoom = 1.0;
  let isDraggingCanvas = false;
  let dragStartX = 0;
  let dragStartY = 0;
  let draggedNode = null;
  let hoveredNode = null;

  // Search & Filter state
  let searchQuery = '';
  let activeExtFilter = 'all';

  function initArchitectureView() {
    canvas = document.getElementById('arch-graph-canvas');
    if (!canvas) return;

    ctx = canvas.getContext('2d');
    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);

    setupCanvasInteractions();
    setupToolbarControls();
    setupInspectorControls();

    // Auto-load if view becomes active
    window.addEventListener('view-switched', (e) => {
      const activeV = (e.detail && (e.detail.activeView || e.detail.view)) || '';
      if (activeV === 'architecture') {
        resizeCanvas();
        if (graphData.nodes.length === 0) {
          fetchGraphData();
        } else {
          startPhysicsLoop();
        }
      } else {
        stopPhysicsLoop();
      }
    });

    // Check if initial view is architecture
    const initialView = localStorage.getItem('agents_hub_active_view');
    if (initialView === 'architecture') {
      setTimeout(() => {
        resizeCanvas();
        fetchGraphData();
      }, 200);
    }
  }

  function resizeCanvas() {
    if (!canvas) return;
    const container = canvas.parentElement;
    if (!container) return;
    const rect = container.getBoundingClientRect();
    if (rect.width > 0 && rect.height > 0) {
      canvas.width = rect.width * (window.devicePixelRatio || 1);
      canvas.height = rect.height * (window.devicePixelRatio || 1);
      canvas.style.width = rect.width + 'px';
      canvas.style.height = rect.height + 'px';
      if (ctx) {
        ctx.scale(window.devicePixelRatio || 1, window.devicePixelRatio || 1);
      }
    }
  }

  async function fetchGraphData(forceRefresh = false) {
    const loading = document.getElementById('arch-loading-overlay');
    if (loading) loading.style.display = 'flex';

    try {
      const res = await callBridge('get_codebase_graph', { forceRefresh: !!forceRefresh });
      if (res && res.graph) {
        processGraphResult(res.graph);
      }
    } catch (err) {
      console.error('Failed to load codebase graph:', err);
      showToast('Failed to load architecture graph: ' + (err.message || err), 'error');
    } finally {
      if (loading) loading.style.display = 'none';
    }
  }

  function processGraphResult(result) {
    const rawNodes = result.nodes || [];
    const rawEdges = result.edges || [];
    const stats = result.stats || {};

    const container = canvas.parentElement;
    const width = container ? container.clientWidth : 800;
    const height = container ? container.clientHeight : 600;

    // Initialize physical node positions
    const nodeMap = new Map();
    rawNodes.forEach((n, idx) => {
      const angle = (idx / Math.max(1, rawNodes.length)) * 2 * Math.PI;
      const radius = 100 + Math.random() * 250;
      const node = {
        ...n,
        x: (width / 2) + Math.cos(angle) * radius,
        y: (height / 2) + Math.sin(angle) * radius,
        vx: 0,
        vy: 0,
        radius: Math.max(6, Math.min(22, 6 + (n.inDegree || 0) * 2.5))
      };
      nodeMap.set(node.id, node);
    });

    // Map edges to node references
    const edges = [];
    rawEdges.forEach(e => {
      const sNode = nodeMap.get(e.source);
      const tNode = nodeMap.get(e.target);
      if (sNode && tNode) {
        edges.push({
          source: sNode,
          target: tNode,
          kind: e.kind,
          weight: e.weight || 1
        });
      }
    });

    graphData = {
      nodes: Array.from(nodeMap.values()),
      edges: edges,
      stats: stats
    };

    // Update Header Badges
    const filesBadge = document.getElementById('arch-files-count-badge');
    if (filesBadge) filesBadge.textContent = `${graphData.nodes.length} Files`;

    const connBadge = document.getElementById('arch-connections-badge');
    if (connBadge) connBadge.textContent = `${graphData.edges.length} Connections`;

    centerAndFit();
    startPhysicsLoop();
  }

  function centerAndFit() {
    if (!canvas) return;
    const container = canvas.parentElement;
    const width = container ? container.clientWidth : 800;
    const height = container ? container.clientHeight : 600;

    if (graphData.nodes.length === 0) {
      panX = width / 2;
      panY = height / 2;
      zoom = 1.0;
      return;
    }

    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    graphData.nodes.forEach(n => {
      if (n.x < minX) minX = n.x;
      if (n.x > maxX) maxX = n.x;
      if (n.y < minY) minY = n.y;
      if (n.y > maxY) maxY = n.y;
    });

    const graphWidth = Math.max(100, maxX - minX);
    const graphHeight = Math.max(100, maxY - minY);
    const centerX = (minX + maxX) / 2;
    const centerY = (minY + maxY) / 2;

    const scaleX = (width * 0.85) / graphWidth;
    const scaleY = (height * 0.85) / graphHeight;
    zoom = Math.max(0.3, Math.min(1.5, Math.min(scaleX, scaleY)));

    panX = (width / 2) - (centerX * zoom);
    panY = (height / 2) - (centerY * zoom);
  }

  function setupCanvasInteractions() {
    if (!canvas) return;

    canvas.addEventListener('mousedown', (e) => {
      const pos = getCanvasMousePos(e);
      const clicked = findNodeAt(pos.x, pos.y);

      if (clicked) {
        draggedNode = clicked;
        selectNode(clicked);
      } else {
        isDraggingCanvas = true;
        dragStartX = e.clientX - panX;
        dragStartY = e.clientY - panY;
      }
    });

    window.addEventListener('mousemove', (e) => {
      if (isDraggingCanvas) {
        panX = e.clientX - dragStartX;
        panY = e.clientY - dragStartY;
      } else if (draggedNode) {
        const pos = getCanvasMousePos(e);
        draggedNode.x = pos.x;
        draggedNode.y = pos.y;
        draggedNode.vx = 0;
        draggedNode.vy = 0;
      } else if (canvas && canvas.offsetParent !== null) {
        const pos = getCanvasMousePos(e);
        const node = findNodeAt(pos.x, pos.y);
        if (node !== hoveredNode) {
          hoveredNode = node;
          updateTooltip(e, node);
        }
      }
    });

    window.addEventListener('mouseup', () => {
      isDraggingCanvas = false;
      draggedNode = null;
    });

    canvas.addEventListener('wheel', (e) => {
      e.preventDefault();
      const zoomFactor = e.deltaY < 0 ? 1.12 : 0.89;
      const newZoom = Math.max(0.2, Math.min(4.0, zoom * zoomFactor));

      const rect = canvas.getBoundingClientRect();
      const mouseX = e.clientX - rect.left;
      const mouseY = e.clientY - rect.top;

      panX = mouseX - (mouseX - panX) * (newZoom / zoom);
      panY = mouseY - (mouseY - panY) * (newZoom / zoom);
      zoom = newZoom;
    }, { passive: false });
  }

  function getCanvasMousePos(e) {
    const rect = canvas.getBoundingClientRect();
    const rawX = e.clientX - rect.left;
    const rawY = e.clientY - rect.top;
    return {
      x: (rawX - panX) / zoom,
      y: (rawY - panY) / zoom
    };
  }

  function findNodeAt(worldX, worldY) {
    for (let i = graphData.nodes.length - 1; i >= 0; i--) {
      const n = graphData.nodes[i];
      if (!isNodeVisible(n)) continue;
      const dx = n.x - worldX;
      const dy = n.y - worldY;
      if (Math.sqrt(dx * dx + dy * dy) <= n.radius + 4) {
        return n;
      }
    }
    return null;
  }

  function isNodeVisible(n) {
    if (activeExtFilter !== 'all') {
      const allowed = activeExtFilter.split(',');
      if (!allowed.includes(n.extension)) return false;
    }
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      const matchName = n.label.toLowerCase().includes(q);
      const matchPath = n.id.toLowerCase().includes(q);
      const matchSymbol = n.symbols && n.symbols.some(s => s.toLowerCase().includes(q));
      if (!matchName && !matchPath && !matchSymbol) return false;
    }
    return true;
  }

  function updateTooltip(e, node) {
    const tip = document.getElementById('arch-tooltip');
    if (!tip) return;
    if (!node) {
      tip.style.display = 'none';
      return;
    }

    const rect = canvas.getBoundingClientRect();
    tip.style.display = 'block';
    tip.style.left = (e.clientX - rect.left + 14) + 'px';
    tip.style.top = (e.clientY - rect.top + 14) + 'px';

    tip.innerHTML = `
      <div style="font-weight:700; color:var(--text-main);">${escapeHtml(node.label)}</div>
      <div style="color:var(--text-dim); font-size:10px; font-family:var(--font-mono); margin-bottom:4px;">${escapeHtml(node.id)}</div>
      <div>Callers: <b style="color:#ef4444;">${node.inDegree || 0}</b> | Depends on: <b style="color:#3b82f6;">${node.outDegree || 0}</b></div>
      <div>Lines: <b>${node.lineCount || 0}</b> | Size: <b>${formatBytes(node.sizeBytes)}</b></div>
      ${node.isOrphan ? '<div style="color:#eab308; font-weight:600; margin-top:2px;">&#9888; Unused file (0 callers)</div>' : ''}
    `;
  }

  async function selectNode(node) {
    selectedNode = node;
    updateSelectedNodeDetails(node);

    try {
      const res = await callBridge('calculate_blast_radius', { fileId: node.id });
      if (res && res.report) {
        blastReport = res.report;
        updateBlastRadiusUi(res.report);
      }
    } catch (err) {
      console.error('Failed to calculate blast radius:', err);
    }
  }

  function updateSelectedNodeDetails(node) {
    const lbl = document.getElementById('arch-selected-file-label');
    const path = document.getElementById('arch-selected-file-path');
    const inDeg = document.getElementById('arch-node-in-degree');
    const outDeg = document.getElementById('arch-node-out-degree');
    const lines = document.getElementById('arch-node-lines');
    const size = document.getElementById('arch-node-size');
    const orphanBadge = document.getElementById('arch-node-orphan-badge');

    if (lbl) lbl.textContent = node.label;
    if (path) path.textContent = node.id;
    if (inDeg) inDeg.textContent = node.inDegree || 0;
    if (outDeg) outDeg.textContent = node.outDegree || 0;
    if (lines) lines.textContent = node.lineCount || 0;
    if (size) size.textContent = formatBytes(node.sizeBytes);
    if (orphanBadge) orphanBadge.style.display = node.isOrphan ? '' : 'none';
  }

  function updateBlastRadiusUi(report) {
    const directEl = document.getElementById('arch-direct-count');
    const secEl = document.getElementById('arch-secondary-count');
    const safeEl = document.getElementById('arch-safe-count');
    const riskBadge = document.getElementById('arch-risk-badge');
    const list = document.getElementById('arch-impacted-list');
    const shieldArea = document.getElementById('arch-prompt-shield-text');

    if (directEl) directEl.textContent = report.directCallers ? report.directCallers.length : 0;
    if (secEl) secEl.textContent = report.secondaryCallers ? report.secondaryCallers.length : 0;
    if (safeEl) safeEl.textContent = report.safeFilesCount || 0;

    if (riskBadge) {
      riskBadge.textContent = `${report.riskLevel} Risk`;
      riskBadge.className = 'badge';
      if (report.riskLevel === 'Critical' || report.riskLevel === 'High') {
        riskBadge.classList.add('arch-badge-red');
      } else if (report.riskLevel === 'Medium') {
        riskBadge.classList.add('arch-badge-orange');
      } else {
        riskBadge.classList.add('arch-badge-green');
      }
    }

    if (shieldArea) {
      shieldArea.value = report.promptShield || '';
    }

    if (list) {
      list.innerHTML = '';
      const directs = report.directCallers || [];
      const secs = report.secondaryCallers || [];

      if (directs.length === 0 && secs.length === 0) {
        list.innerHTML = '<div style="font-size: 11px; color: var(--text-muted); font-style: italic; padding: 6px;">Zero callers impacted. Safe to edit.</div>';
        return;
      }

      directs.forEach(d => {
        const item = document.createElement('div');
        item.className = 'arch-impacted-item is-direct';
        item.title = 'Direct caller (High risk)';
        item.innerHTML = `<span>${escapeHtml(d)}</span><span style="color:#ef4444; font-size:10px; font-weight:700;">DIRECT</span>`;
        item.addEventListener('click', () => focusFileNode(d));
        list.appendChild(item);
      });

      secs.forEach(s => {
        const item = document.createElement('div');
        item.className = 'arch-impacted-item is-secondary';
        item.title = 'Secondary caller (Indirect risk)';
        item.innerHTML = `<span>${escapeHtml(s)}</span><span style="color:#f97316; font-size:10px; font-weight:700;">SECONDARY</span>`;
        item.addEventListener('click', () => focusFileNode(s));
        list.appendChild(item);
      });
    }
  }

  function focusFileNode(fileId) {
    const target = graphData.nodes.find(n =>
      n.id.toLowerCase() === fileId.toLowerCase() ||
      n.label.toLowerCase() === fileId.toLowerCase());
    if (target) {
      const container = canvas ? canvas.parentElement : null;
      const width = container ? container.clientWidth : 800;
      const height = container ? container.clientHeight : 600;
      panX = (width / 2) - (target.x * zoom);
      panY = (height / 2) - (target.y * zoom);
      selectNode(target);
    }
  }

  function setupToolbarControls() {
    const searchInput = document.getElementById('arch-search-input');
    const clearSearch = document.getElementById('btn-clear-arch-search');
    if (searchInput) {
      searchInput.addEventListener('input', (e) => {
        searchQuery = e.target.value.trim();
        if (clearSearch) clearSearch.style.display = searchQuery ? '' : 'none';
      });
    }
    if (clearSearch) {
      clearSearch.addEventListener('click', () => {
        if (searchInput) searchInput.value = '';
        searchQuery = '';
        clearSearch.style.display = 'none';
      });
    }

    const filterExt = document.getElementById('arch-filter-ext');
    if (filterExt) {
      filterExt.addEventListener('change', (e) => {
        activeExtFilter = e.target.value;
      });
    }

    const btnFit = document.getElementById('btn-fit-arch-graph');
    if (btnFit) {
      btnFit.addEventListener('click', centerAndFit);
    }

    const btnPhysics = document.getElementById('btn-toggle-physics');
    if (btnPhysics) {
      btnPhysics.addEventListener('click', () => {
        isPhysicsRunning = !isPhysicsRunning;
        const lbl = document.getElementById('lbl-physics-toggle');
        if (lbl) lbl.textContent = isPhysicsRunning ? 'Pause' : 'Resume';
        if (isPhysicsRunning) startPhysicsLoop();
      });
    }

    const btnRescan = document.getElementById('btn-rescan-arch-graph');
    if (btnRescan) {
      btnRescan.addEventListener('click', () => fetchGraphData(true));
    }
  }

  function setupInspectorControls() {
    const btnCopy = document.getElementById('btn-copy-shield');
    if (btnCopy) {
      btnCopy.addEventListener('click', () => {
        const shieldText = document.getElementById('arch-prompt-shield-text')?.value;
        if (shieldText) {
          navigator.clipboard.writeText(shieldText);
          showToast('Agent Prompt Shield copied to clipboard.', 'success');
        } else {
          showToast('Select a file node first.', 'info');
        }
      });
    }

    const btnInject = document.getElementById('btn-inject-shield');
    if (btnInject) {
      btnInject.addEventListener('click', async () => {
        const shieldText = document.getElementById('arch-prompt-shield-text')?.value;
        if (!shieldText) {
          showToast('Select a file node first.', 'info');
          return;
        }

        // Copy to clipboard & populate prompt enhancer if available
        navigator.clipboard.writeText(shieldText);
        const promptInput = document.getElementById('input-prompt-enhance');
        if (promptInput) {
          promptInput.value = shieldText + '\n\n' + (promptInput.value || '');
          const navPrompt = document.querySelector('.nav-item[data-view="prompt-enhancer"]');
          if (navPrompt) navPrompt.click();
          showToast('Prompt Shield injected into Prompt Enhancer.', 'success');
        } else {
          showToast('Prompt Shield copied to clipboard. Paste into CLI agent.', 'success');
        }
      });
    }
  }

  // Physics Simulation Step
  function stepPhysics() {
    const nodes = graphData.nodes;
    const edges = graphData.edges;
    if (nodes.length === 0) return;

    // Node-Node Repulsion (Coulomb)
    for (let i = 0; i < nodes.length; i++) {
      const n1 = nodes[i];
      for (let j = i + 1; j < nodes.length; j++) {
        const n2 = nodes[j];
        const dx = n2.x - n1.x;
        const dy = n2.y - n1.y;
        const distSq = dx * dx + dy * dy + 1;
        if (distSq < 45000) {
          const force = 350 / distSq;
          const fx = (dx / Math.sqrt(distSq)) * force;
          const fy = (dy / Math.sqrt(distSq)) * force;
          n1.vx -= fx;
          n1.vy -= fy;
          n2.vx += fx;
          n2.vy += fy;
        }
      }
    }

    // Edge Attraction (Hooke)
    for (let i = 0; i < edges.length; i++) {
      const e = edges[i];
      const dx = e.target.x - e.source.x;
      const dy = e.target.y - e.source.y;
      const dist = Math.sqrt(dx * dx + dy * dy) || 1;
      const desiredDist = 110;
      const force = (dist - desiredDist) * 0.04;
      const fx = (dx / dist) * force;
      const fy = (dy / dist) * force;
      e.source.vx += fx;
      e.source.vy += fy;
      e.target.vx -= fx;
      e.target.vy -= fy;
    }

    // Apply velocities with damping
    for (let i = 0; i < nodes.length; i++) {
      const n = nodes[i];
      if (n === draggedNode) continue;
      n.vx *= 0.88;
      n.vy *= 0.88;
      n.x += n.vx;
      n.y += n.vy;
    }
  }

  function renderGraph() {
    if (!ctx || !canvas) return;
    const width = canvas.width / (window.devicePixelRatio || 1);
    const height = canvas.height / (window.devicePixelRatio || 1);

    ctx.clearRect(0, 0, width, height);

    ctx.save();
    ctx.translate(panX, panY);
    ctx.scale(zoom, zoom);

    const isLight = document.documentElement.getAttribute('data-theme') === 'light';

    // Determine Blast-Radius Sets for quick lookup
    const targetId = selectedNode?.id;
    const directSet = new Set(blastReport?.directCallers || []);
    const secSet = new Set(blastReport?.secondaryCallers || []);

    // 1. Draw Edges
    for (let i = 0; i < graphData.edges.length; i++) {
      const e = graphData.edges[i];
      if (!isNodeVisible(e.source) || !isNodeVisible(e.target)) continue;

      const isTargetEdge = targetId && (e.target.id === targetId || e.source.id === targetId);
      const isDirectEdge = directSet.has(e.source.id) && e.target.id === targetId;

      ctx.beginPath();
      ctx.moveTo(e.source.x, e.source.y);
      ctx.lineTo(e.target.x, e.target.y);

      if (isDirectEdge) {
        ctx.strokeStyle = 'rgba(239, 68, 68, 0.85)';
        ctx.lineWidth = 2.2;
      } else if (isTargetEdge) {
        ctx.strokeStyle = isLight ? 'rgba(37, 99, 235, 0.8)' : 'rgba(59, 130, 246, 0.75)';
        ctx.lineWidth = 1.8;
      } else if (targetId) {
        ctx.strokeStyle = isLight ? 'rgba(15, 23, 42, 0.05)' : 'rgba(255, 255, 255, 0.04)';
        ctx.lineWidth = 0.8;
      } else {
        ctx.strokeStyle = isLight ? 'rgba(15, 23, 42, 0.18)' : 'rgba(255, 255, 255, 0.14)';
        ctx.lineWidth = 1.0;
      }
      ctx.stroke();

      // Arrow head towards target
      if (isDirectEdge || isTargetEdge || !targetId) {
        drawArrowHead(ctx, e.source.x, e.source.y, e.target.x, e.target.y, e.target.radius);
      }
    }

    // 2. Draw Nodes
    for (let i = 0; i < graphData.nodes.length; i++) {
      const n = graphData.nodes[i];
      if (!isNodeVisible(n)) continue;

      const isTarget = targetId && n.id === targetId;
      const isDirect = directSet.has(n.id);
      const isSecondary = secSet.has(n.id);

      // Node Color Calculation
      let fillCol = isLight ? '#64748b' : '#64748b';
      let strokeCol = isLight ? 'rgba(15, 23, 42, 0.2)' : 'rgba(255, 255, 255, 0.2)';
      let shadowCol = null;

      if (isTarget) {
        fillCol = isLight ? '#2563eb' : '#3b82f6';
        strokeCol = isLight ? '#93c5fd' : '#bfdbfe';
        shadowCol = 'rgba(59, 130, 246, 0.6)';
      } else if (isDirect) {
        fillCol = '#ef4444';
        strokeCol = '#fca5a5';
        shadowCol = 'rgba(239, 68, 68, 0.6)';
      } else if (isSecondary) {
        fillCol = '#f97316';
        strokeCol = '#fdba74';
      } else if (n.isOrphan) {
        fillCol = isLight ? '#ca8a04' : '#854d0e';
        strokeCol = '#eab308';
      } else if (targetId) {
        fillCol = isLight ? '#cbd5e1' : '#334155';
        strokeCol = isLight ? 'rgba(15, 23, 42, 0.08)' : 'rgba(255, 255, 255, 0.05)';
      }

      ctx.save();
      if (shadowCol) {
        ctx.shadowColor = shadowCol;
        ctx.shadowBlur = 14;
      }

      ctx.beginPath();
      ctx.arc(n.x, n.y, n.radius, 0, 2 * Math.PI);
      ctx.fillStyle = fillCol;
      ctx.fill();
      ctx.lineWidth = (isTarget || isDirect) ? 2.5 : 1.2;
      ctx.strokeStyle = strokeCol;
      ctx.stroke();
      ctx.restore();

      // Node Labels (Render when zoomed in or on key nodes)
      if (zoom >= 0.75 || isTarget || isDirect || isSecondary || n === hoveredNode) {
        ctx.font = (isTarget || isDirect) ? 'bold 11px system-ui, sans-serif' : '10px system-ui, sans-serif';
        ctx.textAlign = 'center';

        const labelColor = isLight
          ? ((targetId && !isTarget && !isDirect && !isSecondary) ? 'rgba(15, 23, 42, 0.35)' : '#0f172a')
          : ((targetId && !isTarget && !isDirect && !isSecondary) ? 'rgba(255, 255, 255, 0.35)' : '#ffffff');

        // Text halo for high-contrast legibility over background and edges
        ctx.lineWidth = 2.5;
        ctx.strokeStyle = isLight ? 'rgba(248, 250, 252, 0.9)' : 'rgba(11, 15, 25, 0.9)';
        ctx.strokeText(n.label, n.x, n.y + n.radius + 12);

        ctx.fillStyle = labelColor;
        ctx.fillText(n.label, n.x, n.y + n.radius + 12);
      }
    }

    ctx.restore();
  }

  function drawArrowHead(ctx, fromX, fromY, toX, toY, targetRadius) {
    const dx = toX - fromX;
    const dy = toY - fromY;
    const angle = Math.atan2(dy, dx);
    const arrowLen = 6;
    const arrowX = toX - Math.cos(angle) * (targetRadius + 2);
    const arrowY = toY - Math.sin(angle) * (targetRadius + 2);

    ctx.save();
    ctx.beginPath();
    ctx.moveTo(arrowX, arrowY);
    ctx.lineTo(arrowX - arrowLen * Math.cos(angle - Math.PI / 6), arrowY - arrowLen * Math.sin(angle - Math.PI / 6));
    ctx.lineTo(arrowX - arrowLen * Math.cos(angle + Math.PI / 6), arrowY - arrowLen * Math.sin(angle + Math.PI / 6));
    ctx.closePath();
    ctx.fillStyle = ctx.strokeStyle;
    ctx.fill();
    ctx.restore();
  }

  function loop() {
    if (isPhysicsRunning) {
      stepPhysics();
    }
    renderGraph();
    animFrameId = requestAnimationFrame(loop);
  }

  function startPhysicsLoop() {
    if (!animFrameId) {
      animFrameId = requestAnimationFrame(loop);
    }
  }

  function stopPhysicsLoop() {
    if (animFrameId) {
      cancelAnimationFrame(animFrameId);
      animFrameId = null;
    }
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>"']/g, m => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[m]));
  }

  function formatBytes(bytes) {
    if (!bytes || bytes <= 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return (bytes / Math.pow(k, i)).toFixed(1) + ' ' + sizes[i];
  }

  // Export functions to window
  window.initArchitectureView = initArchitectureView;
  window.fetchArchitectureGraphData = fetchGraphData;

  // Auto-init on DOMContentLoaded or immediate if DOM is already ready
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initArchitectureView);
  } else {
    setTimeout(initArchitectureView, 100);
  }
})();
