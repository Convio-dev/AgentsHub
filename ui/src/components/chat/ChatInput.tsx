import React, { useState, useRef, useEffect, useMemo } from 'react';
import { ArrowUp, Paperclip, Terminal, GitBranch, Loader2, X, FileText } from 'lucide-react';
import { ChatAttachment, SlashCommand } from '../../types/agent';
import { useSlashCommandStore } from '../../store/useSlashCommandStore';
import { rankByQuery } from '../../lib/fuzzy';
import { SlashCommandPalette } from './SlashCommandPalette';

interface ChatInputProps {
  onSendMessage: (prompt: string, attachments?: ChatAttachment[]) => void;
  isExecuting?: boolean;
  activeModel?: string;
  totalTokens?: number;
  onOpenGitModal?: () => void;
  onOpenTerminal?: () => void;
  /** Active CLI id, so command discovery can return engine-specific commands. */
  activeCli?: string;
  /** Workspace root, used to discover project-level skills and command manifests. */
  workingDirectory?: string;
}

/**
 * Finds the slash token the caret currently sits in. Only a token at the start of the input or
 * after whitespace opens the palette, so `src/foo` never triggers it.
 */
function findSlashToken(value: string, caret: number): { start: number; query: string } | null {
  const upto = value.slice(0, caret);
  const match = /(?:^|\s)\/([^\s/]*)$/.exec(upto);
  if (!match) return null;
  return { start: upto.lastIndexOf('/'), query: match[1] };
}

export const ChatInput: React.FC<ChatInputProps> = ({
  onSendMessage,
  isExecuting = false,
  activeModel,
  totalTokens = 0,
  onOpenGitModal,
  onOpenTerminal,
  activeCli,
  workingDirectory,
}) => {
  const [prompt, setPrompt] = useState('');
  const [attachments, setAttachments] = useState<ChatAttachment[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const { commands, isLoading: isDiscovering, error: discoveryError, load: loadCommands } = useSlashCommandStore();

  const [isPaletteOpen, setIsPaletteOpen] = useState(false);
  const [paletteQuery, setPaletteQuery] = useState('');
  const [activeCommandIndex, setActiveCommandIndex] = useState(0);

  const visibleCommands = useMemo(
    () =>
      rankByQuery(commands, paletteQuery, command => command.name, command => command.description).map(
        entry => entry.item
      ),
    [commands, paletteQuery]
  );

  // Discovery is lazy: it runs the first time the palette opens for a given engine and workspace.
  useEffect(() => {
    if (!isPaletteOpen) return;
    void loadCommands({ cli: activeCli, cwd: workingDirectory });
  }, [isPaletteOpen, activeCli, workingDirectory, loadCommands]);

  useEffect(() => {
    if (activeCommandIndex >= visibleCommands.length) setActiveCommandIndex(0);
  }, [activeCommandIndex, visibleCommands.length]);

  const syncPalette = (value: string, caret: number) => {
    const token = findSlashToken(value, caret);
    if (!token) {
      setIsPaletteOpen(false);
      return;
    }
    setPaletteQuery(token.query);
    setIsPaletteOpen(true);
  };

  // Listen for file reference inserts from file tree
  useEffect(() => {
    const handleInsert = (e: any) => {
      const path = e.detail?.path;
      if (!path) return;
      setPrompt(prev => {
        const sep = prev.endsWith(' ') || prev.length === 0 ? '' : ' ';
        return `${prev}${sep}@${path} `;
      });
      textareaRef.current?.focus();
    };

    window.addEventListener('insert-prompt-file', handleInsert);
    return () => window.removeEventListener('insert-prompt-file', handleInsert);
  }, []);

  // Auto-resize textarea height
  useEffect(() => {
    if (textareaRef.current) {
      textareaRef.current.style.height = 'auto';
      textareaRef.current.style.height = `${Math.min(textareaRef.current.scrollHeight, 180)}px`;
    }
  }, [prompt]);

  const processFiles = (files: FileList | File[]) => {
    const fileArray = Array.from(files);
    fileArray.forEach(file => {
      const isImg = file.type.startsWith('image/');
      const reader = new FileReader();

      if (isImg) {
        reader.onload = e => {
          const dataUrl = e.target?.result as string;
          const newAtt: ChatAttachment = {
            id: `att_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
            name: file.name,
            size: file.size,
            type: file.type,
            isImage: true,
            dataUrl,
          };
          setAttachments(prev => [...prev, newAtt]);
        };
        reader.readAsDataURL(file);
      } else {
        // Read text preview for code/text files if under 2MB
        if (file.size < 2 * 1024 * 1024) {
          reader.onload = e => {
            const text = e.target?.result as string;
            const newAtt: ChatAttachment = {
              id: `att_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
              name: file.name,
              size: file.size,
              type: file.type || 'text/plain',
              isImage: false,
              dataUrl: text ? `data:text/plain;base64,${btoa(unescape(encodeURIComponent(text)))}` : undefined,
            };
            setAttachments(prev => [...prev, newAtt]);
          };
          reader.readAsText(file);
        } else {
          const newAtt: ChatAttachment = {
            id: `att_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
            name: file.name,
            size: file.size,
            type: file.type || 'application/octet-stream',
            isImage: false,
          };
          setAttachments(prev => [...prev, newAtt]);
        }
      }
    });
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      processFiles(e.target.files);
      e.target.value = '';
    }
  };

  const handleRemoveAttachment = (id: string) => {
    setAttachments(prev => prev.filter(a => a.id !== id));
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      processFiles(e.dataTransfer.files);
    }
  };

  const handlePaste = (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    if (e.clipboardData.files && e.clipboardData.files.length > 0) {
      e.preventDefault();
      processFiles(e.clipboardData.files);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (isPaletteOpen) {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setActiveCommandIndex(prev => (visibleCommands.length ? (prev + 1) % visibleCommands.length : 0));
        return;
      }
      if (e.key === 'ArrowUp') {
        e.preventDefault();
        setActiveCommandIndex(prev =>
          visibleCommands.length ? (prev - 1 + visibleCommands.length) % visibleCommands.length : 0
        );
        return;
      }
      if (e.key === 'Escape') {
        e.preventDefault();
        setIsPaletteOpen(false);
        return;
      }
      if ((e.key === 'Tab' || e.key === 'Enter') && !e.shiftKey) {
        const command = visibleCommands[activeCommandIndex];
        if (command) {
          e.preventDefault();
          applyCommand(command);
          return;
        }
      }
    }

    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  /**
   * Replaces the `/token` under the caret with the chosen command. A command that takes no argument
   * runs immediately, because selecting it from the palette is the same intent as pressing Enter.
   */
  const applyCommand = (command: SlashCommand) => {
    const caret = textareaRef.current?.selectionStart ?? prompt.length;
    const token = findSlashToken(prompt, caret);
    const start = token ? token.start : prompt.length;
    const inserted = `/${command.name}${command.takesArgument ? ' ' : ''}`;
    const nextPrompt = `${prompt.slice(0, start)}${inserted}${prompt.slice(caret)}`;

    setPrompt(nextPrompt);
    setIsPaletteOpen(false);

    if (!command.takesArgument) {
      submitPrompt(nextPrompt);
      return;
    }

    requestAnimationFrame(() => {
      const position = start + inserted.length;
      textareaRef.current?.focus();
      textareaRef.current?.setSelectionRange(position, position);
    });
  };

  const submitPrompt = (raw: string) => {
    const text = raw.trim();
    if ((!text && attachments.length === 0) || isExecuting) return;

    let enrichedPrompt = text;
    if (attachments.length > 0) {
      const fileHeaders = attachments.map(a => `[Attached: ${a.name} (${formatFileSize(a.size)})]`).join('\n');
      enrichedPrompt = enrichedPrompt ? `${enrichedPrompt}\n\n${fileHeaders}` : `Please analyze the attached files:\n${fileHeaders}`;
    }

    onSendMessage(enrichedPrompt, attachments);
    setPrompt('');
    setAttachments([]);
    setIsPaletteOpen(false);
    if (textareaRef.current) {
      textareaRef.current.style.height = 'auto';
    }
  };

  const handleSend = () => {
    submitPrompt(prompt);
  };

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const formatTokens = (tok: number) => {
    if (tok >= 1000) {
      return `~${(tok / 1000).toFixed(1)}k`;
    }
    return `~${tok}`;
  };

  const canSend = (prompt.trim().length > 0 || attachments.length > 0) && !isExecuting;

  return (
    <div
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      className="px-6 py-4 bg-transparent select-none max-w-4xl w-full mx-auto flex flex-col items-center"
    >
      {/* Hidden file input */}
      <input
        ref={fileInputRef}
        type="file"
        multiple
        onChange={handleFileChange}
        className="hidden"
        accept="image/*,.txt,.md,.json,.js,.jsx,.ts,.tsx,.py,.cs,.html,.css,.yml,.yaml,.sh,.cmd,.bat,.log,*"
      />

      <div className="relative w-full">
        {isPaletteOpen ? (
          <SlashCommandPalette
            commands={visibleCommands}
            query={paletteQuery}
            activeIndex={activeCommandIndex}
            isLoading={isDiscovering}
            error={discoveryError}
            engineLabel={activeCli || activeModel}
            onSelect={applyCommand}
            onHover={setActiveCommandIndex}
            onRefresh={() => void loadCommands({ cli: activeCli, cwd: workingDirectory }, { force: true })}
            onDismiss={() => setIsPaletteOpen(false)}
          />
        ) : null}

        <div
          className={`w-full rounded-2xl border transition-all shadow-2xl overflow-hidden bg-surface-card/95 backdrop-blur-md ${
            isDragging
              ? 'border-sky-400 ring-2 ring-sky-500/30 bg-sky-500/5'
              : 'border-surface-border focus-within:border-sky-500/70 focus-within:ring-1 focus-within:ring-sky-500/30'
          }`}
        >
        {/* Attached Files Preview Bar */}
        {attachments.length > 0 && (
          <div className="flex flex-wrap items-center gap-2 px-3.5 pt-3 pb-1 border-b border-surface-border/40 max-h-36 overflow-y-auto">
            {attachments.map(att => (
              <div
                key={att.id}
                className="flex items-center gap-2 p-1.5 pr-2 rounded-lg bg-surface-deep/80 border border-surface-border text-xs text-content-main shadow-xs"
              >
                {att.isImage && att.dataUrl ? (
                  <img
                    src={att.dataUrl}
                    alt={att.name}
                    className="w-7 h-7 rounded object-cover flex-shrink-0 border border-surface-border"
                  />
                ) : (
                  <div className="w-7 h-7 rounded bg-sky-500/10 border border-sky-500/20 flex items-center justify-center flex-shrink-0 text-sky-400">
                    <FileText className="w-4 h-4" />
                  </div>
                )}
                <div className="min-w-0 max-w-[140px]">
                  <div className="text-[11px] font-medium truncate" title={att.name}>
                    {att.name}
                  </div>
                  <div className="text-[9.5px] font-mono text-content-dim">
                    {formatFileSize(att.size)}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => handleRemoveAttachment(att.id)}
                  className="p-1 rounded text-content-dim hover:text-rose-400 hover:bg-rose-500/10 transition ml-0.5"
                  title="Remove attachment"
                >
                  <X className="w-3 h-3" />
                </button>
              </div>
            ))}
          </div>
        )}

        {/* Input Textarea */}
        <textarea
          ref={textareaRef}
          value={prompt}
          onChange={e => {
            setPrompt(e.target.value);
            syncPalette(e.target.value, e.target.selectionStart ?? e.target.value.length);
          }}
          onKeyDown={handleKeyDown}
          onKeyUp={e => {
            if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(e.key)) {
              syncPalette(e.currentTarget.value, e.currentTarget.selectionStart ?? 0);
            }
          }}
          onClick={e => syncPalette(e.currentTarget.value, e.currentTarget.selectionStart ?? 0)}
          onPaste={handlePaste}
          placeholder={
            attachments.length > 0
              ? 'Add instructions for attached files, or press Enter to send...'
              : "Send instructions to active agent (e.g. 'Build an express route', '/diff', '/switch-profile')..."
          }
          rows={1}
          disabled={isExecuting}
          className="w-full px-4 pt-3.5 pb-2 bg-transparent text-xs text-content-main placeholder:text-content-dim focus:outline-none resize-none leading-relaxed font-sans max-h-48"
        />

        {/* Action bar inside input */}
        <div className="flex items-center justify-between px-3.5 py-2 border-t border-surface-border/50 text-xs">
          {/* Left tools: Attach, Exec, Git */}
          <div className="flex items-center space-x-2">
            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              className="p-1 rounded text-content-dim hover:text-sky-400 hover:bg-surface-deep transition flex items-center gap-1"
              title="Attach Images or Files (or drag & drop)"
            >
              <Paperclip className="w-3.5 h-3.5" />
              {attachments.length > 0 && (
                <span className="text-[10px] font-mono font-bold text-sky-400 px-1 py-0.2 rounded-full bg-sky-500/15">
                  {attachments.length}
                </span>
              )}
            </button>

            <span className="opacity-30">|</span>

            <button
              type="button"
              onClick={onOpenTerminal}
              className="flex items-center gap-1 px-1.5 py-0.5 rounded text-[11px] font-mono text-content-muted hover:text-content-main hover:bg-surface-deep transition"
              title="Quick Exec in ConPTY Terminal"
            >
              <Terminal className="w-3 h-3 text-sky-400" />
              <span>Exec</span>
            </button>

            <button
              type="button"
              onClick={onOpenGitModal}
              className="flex items-center gap-1 px-1.5 py-0.5 rounded text-[11px] font-mono text-content-muted hover:text-content-main hover:bg-surface-deep transition"
              title="Inspect Git Changes"
            >
              <GitBranch className="w-3 h-3 text-emerald-400" />
              <span>Git</span>
            </button>
          </div>

          {/* Right: Tokens counter & Circular Send Button */}
          <div className="flex items-center space-x-3">
            {totalTokens > 0 ? (
              <span className="text-[11px] font-mono text-content-dim tabular-nums">
                Tokens: {formatTokens(totalTokens)}
              </span>
            ) : null}

            <button
              type="button"
              onClick={handleSend}
              disabled={!canSend}
              className={`w-7 h-7 rounded-full flex items-center justify-center transition shadow-md ${
                !canSend
                  ? 'bg-sky-600/40 text-white/50 cursor-not-allowed'
                  : 'bg-sky-500 hover:bg-sky-400 text-white active:scale-95 shadow-sky-500/20'
              }`}
            >
              {isExecuting ? (
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
              ) : (
                <ArrowUp className="w-3.5 h-3.5 stroke-[2.5]" />
              )}
            </button>
          </div>
        </div>
        </div>
      </div>

      {/* Subtle Platform Status Footer */}
      <p className="mt-2 text-[10px] text-content-dim text-center font-mono">
        Agents Hub v2.0 • Autonomous desktop engine running via Bun Sidecar with multi-account isolation.
      </p>
    </div>
  );
};
