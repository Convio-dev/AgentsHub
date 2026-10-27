import React from 'react';

export interface BadgeProps {
  children: React.ReactNode;
  variant?: 'sky' | 'emerald' | 'amber' | 'rose' | 'slate' | 'purple';
  size?: 'xs' | 'sm';
  className?: string;
  dot?: boolean;
}

export const Badge: React.FC<BadgeProps> = ({
  children,
  variant = 'slate',
  size = 'xs',
  className = '',
  dot = false,
}) => {
  const variantStyles = {
    sky: 'bg-sky-500/10 text-accent border-sky-500/25',
    emerald: 'bg-emerald-500/10 text-state-success border-emerald-500/25',
    amber: 'bg-amber-500/10 text-state-warning border-amber-500/25',
    rose: 'bg-rose-500/10 text-state-danger border-rose-500/25',
    slate: 'bg-surface-strong text-content-muted border-surface-border/60',
    purple: 'bg-purple-500/10 text-purple-400 border-purple-500/25',
  }[variant];

  const dotColors = {
    sky: 'bg-sky-400',
    emerald: 'bg-emerald-400',
    amber: 'bg-amber-400',
    rose: 'bg-rose-400',
    slate: 'bg-content-dim',
    purple: 'bg-purple-400',
  }[variant];

  const sizeStyles = size === 'xs' ? 'px-2 py-0.5 text-[11px]' : 'px-2.5 py-1 text-xs';

  return (
    <span
      className={`inline-flex items-center gap-1.5 font-medium border rounded-md select-none ${sizeStyles} ${variantStyles} ${className}`}
    >
      {dot && <span className={`w-1.5 h-1.5 rounded-full ${dotColors}`} />}
      {children}
    </span>
  );
};
