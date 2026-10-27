  let refreshSnapshots = async function() {
    const selSnapshot = document.getElementById('checkpoint-select');
    if (!selSnapshot) return;
    try {
      const wsId = state.activeWorkspace?.id || null;
      const res = await callBridge('get_workspace_snapshots', { workspaceId: wsId });
      const snapshots = res?.snapshots || [];
      selSnapshot.innerHTML = '';
      if (snapshots.length === 0) {
        selSnapshot.innerHTML = '<option value="">No checkpoints recorded</option>';
        return;
      }
      snapshots.forEach(s => {
        const opt = document.createElement('option');
        opt.value = s.id;
        const time = s.timestamp ? new Date(s.timestamp).toLocaleTimeString() : '';
        opt.textContent = `[${time}] ${s.title} (${s.fileCount} files)`;
        selSnapshot.appendChild(opt);
      });
    } catch (err) {
      console.error('Failed to load snapshots:', err);
    }
  };

  function setupWorkspaceSnapshots() {
    const selSnapshot = document.getElementById('checkpoint-select');
    const btnCreate = document.getElementById('btn-create-checkpoint');
    const btnRollback = document.getElementById('btn-rollback-checkpoint');
    const btnDelete = document.getElementById('btn-delete-checkpoint');

    btnCreate?.addEventListener('click', async () => {
      const title = prompt('Enter checkpoint label:', 'Manual Checkpoint');
      if (title === null) return;
      try {
        const wsId = state.activeWorkspace?.id || null;
        const res = await callBridge('create_workspace_snapshot', {
          workspaceId: wsId,
          title: title.trim() || 'Manual Checkpoint',
          trigger: 'Manual'
        });
        if (res?.success) {
          showToast('Checkpoint created successfully', 'success');
          refreshSnapshots();
        } else {
          showToast('Failed to create checkpoint', 'error');
        }
      } catch (err) {
        showToast(`Checkpoint error: ${err.message}`, 'error');
      }
    });

    btnRollback?.addEventListener('click', async () => {
      const snapId = selSnapshot?.value;
      if (!snapId) {
        showToast('Please select a checkpoint to rollback', 'warning');
        return;
      }
      if (!confirm('Are you sure you want to rollback this workspace? Changed files will be replaced with snapshot contents.')) {
        return;
      }
      try {
        const wsId = state.activeWorkspace?.id || null;
        const res = await callBridge('rollback_workspace_snapshot', {
          snapshotId: snapId,
          workspaceId: wsId
        });
        if (res?.success) {
          showToast(`Workspace restored! (${res.restoredCount} files replaced)`, 'success');
        } else {
          showToast('Rollback failed', 'error');
        }
      } catch (err) {
        showToast(`Rollback error: ${err.message}`, 'error');
      }
    });

    btnDelete?.addEventListener('click', async () => {
      const snapId = selSnapshot?.value;
      if (!snapId) return;
      if (!confirm('Delete selected checkpoint?')) return;
      try {
        const wsId = state.activeWorkspace?.id || null;
        const res = await callBridge('delete_workspace_snapshot', {
          snapshotId: snapId,
          workspaceId: wsId
        });
        if (res?.success) {
          showToast('Checkpoint deleted', 'info');
          refreshSnapshots();
        }
      } catch (err) {
        showToast(`Delete error: ${err.message}`, 'error');
      }
    });

    refreshSnapshots();
    window.addEventListener('workspace-changed', refreshSnapshots);
  }

  // Feature 3: Acoustic Soundpack Engine & Audio Chimes (Unified Gateway)
window.setupWorkspaceSnapshots = setupWorkspaceSnapshots;
window.refreshSnapshots = refreshSnapshots;
