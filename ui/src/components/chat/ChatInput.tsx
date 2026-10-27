import React, { useState, useRef, useEffect } from 'react';
import { ArrowUp, Paperclip, Terminal, GitBranch, Loader2, X, FileText, Image as ImageIcon } from 'lucide-react';
import { ChatAttachment } from '../../types/agent';

interface ChatInputProps {
  onSendMessage: (prompt: string, attachments?: ChatAttachment[]) => void;
  isExecuting?: boolean;
  activeModel?: string;
  totalTokens?: number;
  onOpenGitModal?: () => void;
  onOpenTerminal?: () => void;
}

export const ChatInput: React.FC<ChatInputProps> = ({
  onSendMessage,
  isExecuting = false,
  totalTokens = 0,
  onOpenGitModal,
  onOpenTerminal,
}) => {
  const [prompt, setPrompt] = useState('');
  const [attachments, setAttachments] = useState<ChatAttachment[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

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
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  const handleSend = () => {
    const text = prompt.trim();
    if ((!text && attachments.length === 0) || isExecuting) return;

    let enrichedPrompt = text;
    if (attachments.length > 0) {
      const fileHeaders = attachments.map(a => `[Attached: ${a.name} (${formatFileSize(a.size)})]`).join('\n');
      if (!enrichedPrompt) {
        enrichedPrompt = `Please analyze the attached files:\n${fileHeaders}`;
      } else {
        enrichedPrompt = `${enrichedPrompt}\n\n${fileHeaders}`;
      }
    }

    onSendMessage(enrichedPrompt, attachments);
    setPrompt('');
    setAttachments([]);
    if (textareaRef.current) {
      textareaRef.current.style.height = 'auto';
    }
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
          onChange={e => setPrompt(e.target.value)}
          onKeyDown={handleKeyDown}
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
            <span className="text-[11px] font-mono text-content-dim">
              Tokens: {totalTokens > 0 ? formatTokens(totalTokens) : '~1.2k'}
            </span>

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

      {/* Subtle Platform Status Footer */}
      <p className="mt-2 text-[10px] text-content-dim text-center font-mono">
        Agents Hub v2.0 • Autonomous desktop engine running via Bun Sidecar with multi-account isolation.
      </p>
    </div>
  );
};
