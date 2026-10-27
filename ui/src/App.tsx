import React, { useEffect, useState } from 'react';
import { Header } from './components/header/Header';
import { PrimaryRail } from './components/layout/PrimaryRail';
import { SecondarySidebar } from './components/layout/SecondarySidebar';
import { AgentChatView } from './views/AgentChatView';
import { DashboardView } from './views/DashboardView';
import { TerminalsView } from './views/TerminalsView';
import { RepositoriesView } from './views/RepositoriesView';
import { ProfilesView } from './views/ProfilesView';
import { McpSkillsView } from './views/McpSkillsView';
import { DevToolsView } from './views/DevToolsView';
import { LanView } from './views/LanView';
import { SettingsView } from './views/SettingsView';
import { ModalRoot, ModalType } from './components/modals/ModalRoot';
import { useSidecarStore } from './store/useSidecarStore';
import { useProfileStore } from './store/useProfileStore';
import { useWorkspaceStore } from './store/useWorkspaceStore';
import { useThemeStore } from './store/useThemeStore';

export const App: React.FC = () => {
  const { activeView, isSidebarCollapsed, initBridgeListeners } = useSidecarStore();
  const { loadProfiles } = useProfileStore();
  const { loadWorkspaces, loadWorkspaceFiles, loadBackendSessions } = useWorkspaceStore();
  const { initTheme } = useThemeStore();

  const [activeModal, setActiveModal] = useState<ModalType>(null);

  useEffect(() => {
    // 0. Initialize theme variables and contrast
    initTheme();
    // 1. Initialize bi-directional backend event subscriptions
    const cleanup = initBridgeListeners();

    // 2. Hydrate profiles, workspaces, and past session history from C# host
    loadProfiles();
    loadWorkspaces().then(() => {
      loadWorkspaceFiles();
      loadBackendSessions();
    });

    return () => {
      cleanup();
    };
  }, [initBridgeListeners, loadProfiles, loadWorkspaces, loadWorkspaceFiles, loadBackendSessions]);

  const renderActiveView = () => {
    switch (activeView) {
      case 'chat':
        return <AgentChatView onOpenGitModal={() => setActiveModal('git_diff')} />;
      case 'dashboard':
        return <DashboardView />;
      case 'terminals':
        return <TerminalsView />;
      case 'repositories':
        return <RepositoriesView />;
      case 'profiles':
        return <ProfilesView onOpenAddProfile={() => setActiveModal('add_profile')} />;
      case 'mcpskills':
        return <McpSkillsView />;
      case 'devtools':
        return <DevToolsView />;
      case 'lan':
        return <LanView />;
      case 'settings':
        return <SettingsView />;
      default:
        return <AgentChatView onOpenGitModal={() => setActiveModal('git_diff')} />;
    }
  };

  return (
    <div className="flex flex-col h-screen w-screen overflow-hidden bg-surface-deep text-content-main font-sans antialiased select-none">
      {/* 1. TOP HEADER (Workspace context, branch badge, telemetry, sidecar status, HUD toggle) */}
      <Header
        onOpenSettings={() => setActiveModal('settings')}
        onOpenWorkspaceModal={() => setActiveModal('workspace')}
      />

      {/* 2. DUAL-SIDEBAR WORKSPACE CONTAINER */}
      <div id="app-container" className="flex-1 flex overflow-hidden relative">
        {/* Left-most Primary Rail (56px) */}
        <PrimaryRail />

        {/* Secondary Collapsible Sidebar (260px) - ONLY visible in Chat view */}
        {activeView === 'chat' && !isSidebarCollapsed && (
          <SecondarySidebar
            onOpenWorkspaceModal={() => setActiveModal('workspace')}
          />
        )}

        {/* Main Workspace Content Area */}
        <main id="main-area" className="flex-1 flex flex-col bg-surface-deep overflow-hidden relative min-w-0">
          {renderActiveView()}
        </main>
      </div>

      {/* 3. MODAL REGISTRY (Workspace Settings, Add Profile, Git Diff, MCP Skills) */}
      <ModalRoot activeModal={activeModal} onClose={() => setActiveModal(null)} />
    </div>
  );
};

export default App;
