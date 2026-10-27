import React from 'react';
import { AddProfileModal } from './AddProfileModal';
import { GitDiffModal } from './GitDiffModal';
import { McpSkillsModal } from './McpSkillsModal';
import { WorkspaceSettingsModal } from './WorkspaceSettingsModal';

export type ModalType = 'add_profile' | 'git_diff' | 'mcp_skills' | 'settings' | 'workspace' | null;

interface ModalRootProps {
  activeModal: ModalType;
  onClose: () => void;
}

export const ModalRoot: React.FC<ModalRootProps> = ({ activeModal, onClose }) => {
  return (
    <>
      <AddProfileModal isOpen={activeModal === 'add_profile'} onClose={onClose} />
      <GitDiffModal isOpen={activeModal === 'git_diff'} onClose={onClose} />
      <McpSkillsModal isOpen={activeModal === 'mcp_skills'} onClose={onClose} />
      <WorkspaceSettingsModal
        isOpen={activeModal === 'workspace' || activeModal === 'settings'}
        onClose={onClose}
      />
    </>
  );
};
