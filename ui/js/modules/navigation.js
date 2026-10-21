  function setupNavigation() {
    const navItems = document.querySelectorAll('.nav-item');
    navItems.forEach(item => {
      item.addEventListener('click', () => {
        const targetView = item.dataset.view;
        switchView(targetView);
      });
    });

    // Closeable / Collapsible Sidebar
    const btnToggleSidebar = document.getElementById('btn-toggle-sidebar');
    const sidebar = document.getElementById('sidebar');
    
    // Restore sidebar preference
    const savedCollapsed = localStorage.getItem('agents_hub_sidebar_collapsed') === 'true';
    if (savedCollapsed) sidebar.classList.add('collapsed');

    btnToggleSidebar?.addEventListener('click', () => {
      sidebar.classList.toggle('collapsed');
      localStorage.setItem('agents_hub_sidebar_collapsed', sidebar.classList.contains('collapsed'));
      if (state.activeView === 'terminals') {
        setTimeout(() => fitTerminalSession(terminalSessions[normalizeCliKey(state.activeTerminalCli)]), 220);
      }
    });

    // Restore active view (persists across Ctrl+R / F5 / page reloads)
    try {
      const savedView = localStorage.getItem('agents_hub_active_view');
      if (savedView) {
        switchView(savedView);
      }
    } catch (e) {
      console.warn('Failed to restore active view:', e);
    }
  }

  function switchView(viewName) {
    if (viewName === 'providers') {
      switchView('prompt-enhancer');
      switchPromptSubTab('providers');
      return;
    }
    if (viewName === 'logs') {
      switchView('settings');
      if (typeof window.switchSettingsTab === 'function') {
        window.switchSettingsTab('logs');
      }
      return;
    }

    const targetPane = document.getElementById(`view-${viewName}`);
    if (!targetPane && viewName !== 'dashboard') {
      viewName = 'dashboard';
    }

    state.activeView = viewName;
    try {
      localStorage.setItem('agents_hub_active_view', viewName);
    } catch (e) {}

    document.querySelectorAll('.nav-item').forEach(i => {
      i.classList.toggle('active', i.dataset.view === viewName);
    });
    document.querySelectorAll('.view-pane').forEach(p => {
      p.classList.toggle('active', p.id === `view-${viewName}`);
    });

    window.dispatchEvent(new CustomEvent('view-switched', { detail: { activeView: viewName } }));

    if (viewName === 'dashboard') {
      resumeHardwareTelemetry();
    } else {
      pauseHardwareTelemetry();
    }

    if (viewName === 'devtools') {
      // The health check is local reads only, so it is re-run whenever the view opens.
      loadDevTools();
    }

    if (viewName === 'terminals') {
      activateTerminalTab(state.activeTerminalCli, false);
    }

    // Dependency detection spawns version probes, so only scan the first time Settings opens.
    if (viewName === 'settings' && typeof dependencyState !== 'undefined' && !dependencyState.loaded && !dependencyState.loading) {
      loadDependencies(false);
    }

    if (viewName === 'profiles') {
      if (typeof window.renderWorkspacesDropdown === 'function') window.renderWorkspacesDropdown(state.workspaces, state.activeWorkspaceId);
      if (typeof window.renderProfiles === 'function') window.renderProfiles();
      if (typeof window.updateProfileEmailUi === 'function') window.updateProfileEmailUi();
    }

    // The firewall probe runs in the background, so the LAN page renders its focal state
    // immediately and loads fresh state without blocking.
    if (viewName === 'lan') {
      if (typeof window.renderLanPage === 'function') window.renderLanPage();
      if (typeof lanLoading === 'undefined' || !lanLoading) {
        loadLanState(false);
      }
    }

    if (viewName === 'repositories') {
      if (typeof window.refreshRepositoriesView === 'function') {
        window.refreshRepositoriesView();
      }
    }
  }

  // Discord Adaptive Theme & Color Engine
  function hexToHsl(hex) {
    hex = (hex || '').replace(/^#/, '').trim();
    if (hex.length === 3) {
      hex = hex.split('').map(c => c + c).join('');
    }
    if (hex.length !== 6 || !/^[0-9a-fA-F]{6}$/.test(hex)) {
      return { h: 160, s: 84, l: 39 }; // Fallback Emerald
    }
    const r = parseInt(hex.substring(0, 2), 16) / 255;
    const g = parseInt(hex.substring(2, 4), 16) / 255;
    const b = parseInt(hex.substring(4, 6), 16) / 255;

    const max = Math.max(r, g, b);
    const min = Math.min(r, g, b);
    let h = 0;
    let s = 0;
    let l = (max + min) / 2;

    if (max !== min) {
      const d = max - min;
      s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
      switch (max) {
        case r: h = (g - b) / d + (g < b ? 6 : 0); break;
        case g: h = (b - r) / d + 2; break;
        case b: h = (r - g) / d + 4; break;
      }
      h = Math.round(h * 60);
    }
    s = Math.round(s * 100);
    l = Math.round(l * 100);
    return { h, s, l };
  }

  function getPerceivedLuminance(hex) {
    hex = (hex || '').replace(/^#/, '').trim();
    if (hex.length === 3) hex = hex.split('').map(c => c + c).join('');
    if (hex.length !== 6 || !/^[0-9a-fA-F]{6}$/.test(hex)) return 0.5;
    const r = parseInt(hex.substring(0, 2), 16);
    const g = parseInt(hex.substring(2, 4), 16);
    const b = parseInt(hex.substring(4, 6), 16);
    return (0.299 * r + 0.587 * g + 0.114 * b) / 255;
  }

  function applyAdaptiveTheme(themeName, hexColor, intensityVal = null) {
    const activeTheme = themeName === 'light' ? 'light' : 'dark';
    
    let hex = (hexColor || '').trim();
    if (!hex.startsWith('#')) hex = '#' + hex;
    if (hex.length === 4) {
      hex = '#' + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3];
    }
    if (!/^#[0-9a-fA-F]{6}$/.test(hex)) {
      hex = '#4049B9';
    }
    hex = hex.toUpperCase();

    // Color intensity support (Discord theme customizer: default 74%)
    let intensity = intensityVal !== null ? parseInt(intensityVal, 10) : parseInt(localStorage.getItem('agents_hub_theme_intensity') || '74', 10);
    if (isNaN(intensity) || intensity < 10 || intensity > 100) intensity = 74;
    const intMult = intensity / 74;

    const { h, s, l } = hexToHsl(hex);
    const rootStyle = document.documentElement.style;

    document.documentElement.setAttribute('data-theme', activeTheme);

    if (activeTheme === 'dark') {
      // Discord-style Dark Surface Hierarchy modulated by intensity
      const sTint = s < 12 
        ? Math.max(2, Math.round(s * intMult)) 
        : Math.min(32, Math.max(6, Math.round(s * 0.28 * intMult)));
      
      rootStyle.setProperty('--bg-app', `hsl(${h}, ${sTint}%, 5.5%)`);
      rootStyle.setProperty('--bg-sidebar', `hsl(${h}, ${sTint + 2}%, 7.5%)`);
      rootStyle.setProperty('--bg-header', `hsl(${h}, ${sTint + 2}%, 9%)`);
      rootStyle.setProperty('--bg-card', `hsl(${h}, ${sTint + 4}%, 12.5%)`);
      rootStyle.setProperty('--bg-card-hover', `hsl(${h}, ${sTint + 5}%, 16%)`);
      rootStyle.setProperty('--bg-input', `hsl(${h}, ${sTint}%, 7%)`);
      rootStyle.setProperty('--border-subtle', `hsl(${h}, ${sTint + 2}%, 19%)`);
      rootStyle.setProperty('--border-hover', `hsl(${h}, ${Math.min(s, 75)}%, 48%)`);
      rootStyle.setProperty('--bg-terminal', `hsl(${h}, ${sTint}%, 5%)`);
      
      rootStyle.setProperty('--text-main', '#f8fafc');
      rootStyle.setProperty('--text-muted', `hsl(${h}, 12%, 68%)`);
      rootStyle.setProperty('--text-dim', `hsl(${h}, 10%, 48%)`);

      const darkAccent = l < 42 ? `hsl(${h}, ${Math.max(s, 50)}%, 52%)` : hex;
      const lum = getPerceivedLuminance(darkAccent);
      rootStyle.setProperty('--accent-green', darkAccent);
      rootStyle.setProperty('--accent-green-hover', `hsl(${h}, ${s}%, ${Math.max(12, l - 8)}%)`);
      rootStyle.setProperty('--accent-green-bg', `hsla(${h}, ${s}%, ${l}%, 0.16)`);
      rootStyle.setProperty('--accent-text', lum > 0.62 ? '#0a0f14' : '#ffffff');
      rootStyle.setProperty('--border-focus', darkAccent);
    } else {
      // Discord-style Light Surface Hierarchy matching Discord app in screenshot
      const sTintLight = s < 12 
        ? Math.max(2, Math.round(s * 0.4 * intMult)) 
        : Math.min(26, Math.max(4, Math.round(s * 0.22 * intMult)));

      rootStyle.setProperty('--bg-app', `hsl(${h}, ${sTintLight}%, 95.5%)`);
      rootStyle.setProperty('--bg-sidebar', `hsl(${h}, ${Math.round(sTintLight * 1.35)}%, 92.5%)`);
      rootStyle.setProperty('--bg-header', `hsl(${h}, ${Math.round(sTintLight * 1.5)}%, 88%)`);
      rootStyle.setProperty('--bg-card', '#ffffff');
      rootStyle.setProperty('--bg-card-hover', `hsl(${h}, ${sTintLight}%, 96.5%)`);
      rootStyle.setProperty('--bg-input', `hsl(${h}, ${sTintLight}%, 96.8%)`);
      rootStyle.setProperty('--border-subtle', `hsl(${h}, ${Math.round(sTintLight * 1.25)}%, 83%)`);
      rootStyle.setProperty('--border-hover', `hsl(${h}, ${Math.min(s, 75)}%, 45%)`);
      rootStyle.setProperty('--bg-terminal', `hsl(${h}, 15%, 8%)`);

      rootStyle.setProperty('--text-main', `hsl(${h}, 28%, 11%)`);
      rootStyle.setProperty('--text-muted', `hsl(${h}, 16%, 36%)`);
      rootStyle.setProperty('--text-dim', `hsl(${h}, 10%, 52%)`);

      const lightAccent = l > 48 ? `hsl(${h}, ${Math.max(s, 60)}%, 42%)` : hex;
      const lum = getPerceivedLuminance(lightAccent);
      rootStyle.setProperty('--accent-green', lightAccent);
      rootStyle.setProperty('--accent-green-hover', `hsl(${h}, ${s}%, 34%)`);
      rootStyle.setProperty('--accent-green-bg', `hsla(${h}, ${s}%, 45%, 0.12)`);
      rootStyle.setProperty('--accent-text', lum > 0.62 ? '#0a0f14' : '#ffffff');
      rootStyle.setProperty('--border-focus', lightAccent);
    }

    // Retheme every session, not only the visible one.
    Object.values(terminalSessions).forEach(session => {
      session.term.options.theme = {
        background: activeTheme === 'dark' ? `hsl(${h}, ${s < 12 ? 6 : 14}%, 5%)` : '#0d1117',
        foreground: '#f8fafc',
        cursor: hex,
        selectionBackground: `hsla(${h}, ${s}%, 50%, 0.3)`
      };
    });

    document.querySelectorAll('.btn-theme-toggle').forEach(b => {
      b.classList.toggle('active', b.dataset.theme === activeTheme);
    });

    document.querySelectorAll('.color-swatch').forEach(sw => {
      sw.classList.toggle('active', sw.dataset.color.toUpperCase() === hex);
    });

    const hexInput = document.getElementById('accent-hex-input');
    const picker = document.getElementById('accent-color-picker');
    const slider = document.getElementById('theme-intensity-slider');
    const sliderVal = document.getElementById('theme-intensity-val');
    if (hexInput && document.activeElement !== hexInput) hexInput.value = hex;
    if (picker) picker.value = hex;
    if (slider) slider.value = intensity;
    if (sliderVal) sliderVal.textContent = intensity + '%';

    localStorage.setItem('agents_hub_theme', activeTheme);
    localStorage.setItem('agents_hub_accent_color', hex);
    localStorage.setItem('agents_hub_theme_intensity', intensity);
  }

  function applyTheme(themeName) {
    const currentColor = localStorage.getItem('agents_hub_accent_color') || '#4049B9';
    applyAdaptiveTheme(themeName, currentColor);
  }

  function applyAccentColor(hex) {
    const currentTheme = localStorage.getItem('agents_hub_theme') || 'dark';
    applyAdaptiveTheme(currentTheme, hex);
  }

  // CLI Dependencies & Runtimes (Settings view)
  const dependencyState = {
    items: [],
    busy: {},
    logs: {},
    loaded: false,
    loading: false
  };

  const DEPENDENCY_ICONS = {
    node: '<path d="M12 2 3 7v10l9 5 9-5V7Z"/><path d="M12 8v8"/>',
    git: '<circle cx="6" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M18 9a9 9 0 0 1-9 9"/><circle cx="18" cy="6" r="3"/>',
    python: '<polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/>',
    claude: '<polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/>',
    antigravity: '<path d="m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3Z"/>',
    hermes: '<path d="m12 2 9 5v10l-9 5-9-5V7Z"/><path d="m3 7 9 5 9-5"/><path d="M12 22V12"/>',
    warp: '<path d="M17.5 19a4.5 4.5 0 0 0 .5-8.97A6 6 0 0 0 6.2 9.4 4.5 4.5 0 0 0 7 19Z"/>',
    proton: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z"/>'
  };

window.setupNavigation = setupNavigation;
window.switchView = switchView;
window.applyAdaptiveTheme = applyAdaptiveTheme;
window.applyTheme = applyTheme;
window.applyAccentColor = applyAccentColor;
