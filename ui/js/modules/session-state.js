  function setupSessionStateAutoSave() {
    setInterval(() => {
      const activeWs = state.activeWorkspaceId || '';
      const activeView = state.activeView || 'dashboard';
      const activeTab = state.activeTerminalCli || 'hermes';
      callBridge('save_session_state', {
        activeWorkspaceId: activeWs,
        activeView,
        activeCliTab: activeTab
      }).catch(() => {});
    }, 25000);
  }

  // Terminal (xterm.js) Management.
  // One xterm per CLI session, kept alive across tab switches. Rebuilding a single terminal on
  // every switch threw the scrollback away and left the new tab blank until fresh output arrived,
  // which is why a running Claude Code session showed an empty pane. Hermes is a web dashboard,
  // so its tab hosts the Hermes page instead of a terminal.
  const terminalSessions = {};
  const TERMINAL_TAB_LABELS = {
    hermes: 'Hermes Web',
    claude_code: 'Claude Code',
    antigravity_cli: 'Antigravity CLI'
  };

window.setupSessionStateAutoSave = setupSessionStateAutoSave;
