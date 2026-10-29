import React, { useEffect, useState } from 'react';
import { Minus, Plus, X } from 'lucide-react';

interface ImageLightboxProps {
  src: string;
  alt?: string;
  onClose: () => void;
}

/**
 * Full-size preview for an attached image.
 *
 * Clicking an attachment used to navigate to its data URL, which opened a blank window showing a
 * wall of base64. A modal keeps the chat in place and lets the image be inspected.
 */
export const ImageLightbox: React.FC<ImageLightboxProps> = ({ src, alt, onClose }) => {
  const [zoom, setZoom] = useState(1);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
      if (event.key === '+' || event.key === '=') setZoom(z => Math.min(4, z + 0.25));
      if (event.key === '-') setZoom(z => Math.max(0.25, z - 0.25));
    };

    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-[120] flex flex-col bg-black/80 backdrop-blur-sm"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-label={alt || 'Image preview'}
    >
      <div className="flex items-center justify-end gap-1 p-3" onClick={e => e.stopPropagation()}>
        <button
          type="button"
          onClick={() => setZoom(z => Math.max(0.25, z - 0.25))}
          className="rounded-lg border border-white/20 p-1.5 text-white/80 transition-colors hover:bg-white/10"
          aria-label="Zoom out"
        >
          <Minus className="h-4 w-4" />
        </button>
        <span className="w-12 text-center font-mono text-[11px] text-white/70">{Math.round(zoom * 100)}%</span>
        <button
          type="button"
          onClick={() => setZoom(z => Math.min(4, z + 0.25))}
          className="rounded-lg border border-white/20 p-1.5 text-white/80 transition-colors hover:bg-white/10"
          aria-label="Zoom in"
        >
          <Plus className="h-4 w-4" />
        </button>
        <button
          type="button"
          onClick={onClose}
          className="ml-2 rounded-lg border border-white/20 p-1.5 text-white/80 transition-colors hover:bg-white/10"
          aria-label="Close preview"
        >
          <X className="h-4 w-4" />
        </button>
      </div>

      <div className="flex flex-1 items-center justify-center overflow-auto p-4">
        <img
          src={src}
          alt={alt || 'attachment'}
          onClick={e => e.stopPropagation()}
          style={{ transform: `scale(${zoom})` }}
          className="max-h-full max-w-full origin-center rounded-lg shadow-2xl transition-transform"
          onWheel={event => {
            event.preventDefault();
            setZoom(z => Math.max(0.25, Math.min(4, z + (event.deltaY < 0 ? 0.15 : -0.15))));
          }}
        />
      </div>
    </div>
  );
};
