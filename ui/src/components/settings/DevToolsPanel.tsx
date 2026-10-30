import React, { useState, useEffect } from 'react';
import {
  Wrench,
  Play,
  Pencil,
  ChevronRight,
  ChevronDown,
  Plus,
  RefreshCw,
  Search,
  CheckCircle2,
  AlertTriangle,
  FolderOpen,
  Trash2,
  Save,
  X,
} from 'lucide-react';
import { useDevToolsStore, DevToolItem } from '../store/useDevToolsStore';
import { Button } from '../components/common/Button';

export const DevToolsView: React.FC = () => {
  const {
    tools,
    isLoading,
    expandedToolId,
    feedbackMessage,
    loadTools,
    launchTool,
    toggleTool,
    recheckPaths,
    findInstalledTools,
    browseToolTarget,
    updateToolTarget,
    addTool,
    removeTool,
    setExpandedToolId,
  } = useDevToolsStore();

  const [editTargetPath, setEditTargetPath] = useState('');
  const [showAddModal, setShowAddModal] = useState(false);
  const [newToolName, setNewToolName] = useState('');
  const [newToolTarget, setNewToolTarget] = useState('');
  const [newToolIsIde, setNewToolIsIde] = useState(false);

  useEffect(() => {
    loadTools();
  }, [loadTools]);

  const readyCount = tools.filter(t => t.state === 'ready').length;

  const handleToggleExpand = (tool: DevToolItem) => {
    if (expandedToolId === tool.id) {
      setExpandedToolId(null);
    } else {
      setExpandedToolId(tool.id);
      setEditTargetPath(tool.target);
    }
  };

  const handleBrowseTarget = async (id: string, name: string) => {
    const selected = await browseToolTarget(id, name);
    if (selected) {
      setEditTargetPath(selected);
    }
  };

  const handleSaveEdit = async (id: string) => {
    if (editTargetPath.trim()) {
      await updateToolTarget(id, editTargetPath.trim());
      setExpandedToolId(null);
    }
  };

  const handleCreateTool = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newToolName.trim() || !newToolTarget.trim()) return;
    await addTool({
      name: newToolName.trim(),
      target: newToolTarget.trim(),
      isIde: newToolIsIde,
    });
    setNewToolName('');
    setNewToolTarget('');
    setNewToolIsIde(false);
    setShowAddModal(false);
  };

  return (
    <div className="flex-1 overflow-y-auto p-6 bg-[var(--bg-app,#090D16)] text-[var(--text-main,#F1F5F9)] select-none font-sans">
      <div className="max-w-6xl mx-auto space-y-5 w-full">
        {/* Feedback message banner */}
        {feedbackMessage && (
          <div className="px-4 py-2.5 rounded-xl bg-sky-500/15 border border-sky-500/30 text-accent text-xs font-mono flex items-center justify-between">
            <span>{feedbackMessage}</span>
          </div>
        )}

        {/* Main Card */}
        <div className="rounded-2xl border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-card,#0F172A)] p-6 shadow-sm space-y-5">
          {/* Header */}
          <div className="flex items-center justify-between pb-4 border-b border-[var(--border-subtle,#1E293B)] flex-wrap gap-3">
            <div className="flex items-center space-x-3">
              <div className="w-10 h-10 rounded-xl bg-sky-500/15 border border-sky-500/30 flex items-center justify-center text-accent">
                <Wrench className="w-5 h-5" />
              </div>
              <div>
                <h2 className="text-sm font-bold tracking-wide uppercase text-[var(--text-main,#F1F5F9)]">
                  AI Dev Tools & External Applications
                </h2>
                <p className="text-xs text-[var(--text-dim,#64748B)]">
                  Launch a desktop tool from here, and repair any whose executable moved
                </p>
              </div>
            </div>

            <div className="flex items-center space-x-2">
              <span className="px-2.5 py-1 rounded-lg text-xs font-mono font-semibold bg-sky-500/10 text-accent border border-sky-500/30">
                {readyCount} of {tools.length} ready
              </span>

              <button
                type="button"
                onClick={recheckPaths}
                disabled={isLoading}
                className="px-3 py-1.5 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] hover:border-sky-500 text-[var(--text-dim,#64748B)] hover:text-[var(--text-main,#F1F5F9)] text-xs font-medium transition flex items-center gap-1.5"
              >
                <RefreshCw className={`w-3.5 h-3.5 ${isLoading ? 'animate-spin' : ''}`} />
                <span>Recheck paths</span>
              </button>

              <button
                type="button"
                onClick={findInstalledTools}
                disabled={isLoading}
                className="px-3 py-1.5 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] hover:border-sky-500 text-[var(--text-dim,#64748B)] hover:text-[var(--text-main,#F1F5F9)] text-xs font-medium transition flex items-center gap-1.5"
                title="Look through usual install folders for desktop AI clients"
              >
                <Search className="w-3.5 h-3.5" />
                <span>Find installed tools</span>
              </button>

              <Button variant="primary" size="xs" onClick={() => setShowAddModal(true)}>
                <Plus className="w-3.5 h-3.5 mr-1" />
                <span>Add Tool</span>
              </Button>
            </div>
          </div>

          {/* Tools List */}
          <div className="space-y-2.5">
            {tools.map(tool => {
              const isExpanded = expandedToolId === tool.id;
              return (
                <div
                  key={tool.id}
                  className="rounded-xl border border-[var(--border-subtle,#1E293B)] bg-[var(--bg-input,#0D1322)] hover:border-slate-600 transition shadow-xs overflow-hidden"
                >
                  <div className="flex items-center justify-between p-3.5 flex-wrap gap-3">
                    {/* Left info */}
                    <div
                      className="flex items-center space-x-3 flex-1 min-w-0 pr-4 cursor-pointer"
                      onClick={() => handleToggleExpand(tool)}
                    >
                      {isExpanded ? (
                        <ChevronDown className="w-4 h-4 text-accent flex-shrink-0" />
                      ) : (
                        <ChevronRight className="w-4 h-4 text-[var(--text-dim,#64748B)] flex-shrink-0" />
                      )}
                      <span
                        className={`w-2.5 h-2.5 rounded-full flex-shrink-0 ${
                          tool.state === 'ready' ? 'bg-sky-400' : 'bg-amber-400'
                        }`}
                      />

                      <div className="min-w-0">
                        <div className="flex items-center space-x-2">
                          <span className="font-semibold text-xs text-[var(--text-main,#F1F5F9)] font-mono">
                            {tool.name}
                          </span>
                          {tool.isIde && (
                            <span className="px-1.5 py-0.2 rounded text-[9px] font-mono font-bold bg-sky-500/15 text-accent border border-sky-500/30">
                              IDE
                            </span>
                          )}
                        </div>
                        <div className="text-[11px] text-[var(--text-dim,#64748B)] font-mono truncate" title={tool.target}>
                          {tool.subtitle}
                        </div>
                      </div>
                    </div>

                    {/* Right actions */}
                    <div className="flex items-center space-x-3 flex-shrink-0">
                      <span
                        className={`text-xs font-medium font-mono ${
                          tool.state === 'ready' ? 'text-accent' : 'text-state-warning'
                        }`}
                      >
                        {tool.state === 'ready' ? 'Ready' : 'Needs fixing'}
                      </span>

                      {/* Toggle switch */}
                      <button
                        type="button"
                        onClick={() => toggleTool(tool.id, !tool.enabled)}
                        className={`w-9 h-5 rounded-full transition-colors relative cursor-pointer ${
                          tool.enabled ? 'bg-sky-600' : 'bg-slate-700'
                        }`}
                      >
                        <span
                          className={`absolute top-0.5 w-4 h-4 rounded-full bg-white transition-transform ${
                            tool.enabled ? 'left-4.5' : 'left-0.5'
                          }`}
                        />
                      </button>

                      {/* Launch button */}
                      <Button variant="primary" size="xs" onClick={() => launchTool(tool.id)}>
                        <Play className="w-3.5 h-3.5 mr-1 fill-current" />
                        <span>Launch</span>
                      </Button>

                      {/* Edit button */}
                      <Button variant="secondary" size="xs" onClick={() => handleToggleExpand(tool)}>
                        <Pencil className="w-3 h-3 mr-1" />
                        <span>{isExpanded ? 'Close' : 'Edit'}</span>
                      </Button>
                    </div>
                  </div>

                  {/* Inline Edit Drawer */}
                  {isExpanded && (
                    <div className="p-4 bg-[var(--bg-card,#0F172A)] border-t border-[var(--border-subtle,#1E293B)] space-y-3">
                      <div className="text-xs font-bold text-[var(--text-main,#F1F5F9)]">Edit Executable Target Path</div>
                      <div className="flex items-center gap-2">
                        <input
                          type="text"
                          value={editTargetPath}
                          onChange={e => setEditTargetPath(e.target.value)}
                          placeholder="C:\Path\to\application.exe"
                          className="flex-1 px-3 py-1.5 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-xs font-mono text-[var(--text-main,#F1F5F9)] focus:outline-none focus:border-sky-500"
                        />
                        <Button
                          variant="secondary"
                          size="xs"
                          onClick={() => handleBrowseTarget(tool.id, tool.name)}
                          title="Open native Windows File Dialog"
                        >
                          <FolderOpen className="w-3.5 h-3.5 mr-1" />
                          Browse
                        </Button>
                        <Button
                          variant="primary"
                          size="xs"
                          onClick={() => handleSaveEdit(tool.id)}
                        >
                          <Save className="w-3.5 h-3.5 mr-1" />
                          Save
                        </Button>
                        <Button
                          variant="danger"
                          size="xs"
                          onClick={() => removeTool(tool.id)}
                        >
                          <Trash2 className="w-3.5 h-3.5" />
                        </Button>
                      </div>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>

        {/* Add Tool Modal */}
        {showAddModal && (
          <div className="fixed inset-0 z-50 bg-surface-sunken backdrop-blur-xs flex items-center justify-center p-4">
            <div className="w-full max-w-md bg-[var(--bg-card,#0F172A)] border border-[var(--border-subtle,#1E293B)] rounded-2xl p-6 shadow-2xl space-y-4 text-[var(--text-main,#F1F5F9)]">
              <div className="flex items-center justify-between pb-3 border-b border-[var(--border-subtle,#1E293B)]">
                <h3 className="text-sm font-bold">Add AI Dev Tool</h3>
                <button onClick={() => setShowAddModal(false)} className="text-[var(--text-dim,#64748B)] hover:text-white">
                  <X className="w-4 h-4" />
                </button>
              </div>

              <form onSubmit={handleCreateTool} className="space-y-4 text-xs">
                <div>
                  <label className="block text-[var(--text-dim,#64748B)] mb-1">Tool Name:</label>
                  <input
                    type="text"
                    required
                    value={newToolName}
                    onChange={e => setNewToolName(e.target.value)}
                    placeholder="e.g. Cursor, Roo Code, Aider"
                    className="w-full px-3 py-2 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-[var(--text-main,#F1F5F9)] focus:outline-none focus:border-sky-500"
                  />
                </div>

                <div>
                  <label className="block text-[var(--text-dim,#64748B)] mb-1">Executable Target Path:</label>
                  <div className="flex gap-2">
                    <input
                      type="text"
                      required
                      value={newToolTarget}
                      onChange={e => setNewToolTarget(e.target.value)}
                      placeholder="C:\Path\to\exe or alias"
                      className="flex-1 px-3 py-2 rounded-lg bg-[var(--bg-input,#0D1322)] border border-[var(--border-subtle,#1E293B)] text-[var(--text-main,#F1F5F9)] font-mono focus:outline-none focus:border-sky-500"
                    />
                    <Button
                      type="button"
                      variant="secondary"
                      size="sm"
                      onClick={async () => {
                        const path = await browseToolTarget('new', newToolName || 'Tool');
                        if (path) setNewToolTarget(path);
                      }}
                    >
                      <FolderOpen className="w-4 h-4" />
                    </Button>
                  </div>
                </div>

                <label className="flex items-center gap-2 cursor-pointer pt-1">
                  <input
                    type="checkbox"
                    checked={newToolIsIde}
                    onChange={e => setNewToolIsIde(e.target.checked)}
                    className="w-4 h-4 rounded accent-sky-500"
                  />
                  <span>Treat as Code Editor / IDE</span>
                </label>

                <div className="flex justify-end gap-2 pt-3 border-t border-[var(--border-subtle,#1E293B)]">
                  <Button variant="secondary" size="xs" onClick={() => setShowAddModal(false)}>
                    Cancel
                  </Button>
                  <Button variant="primary" size="xs" type="submit">
                    Add Tool
                  </Button>
                </div>
              </form>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
