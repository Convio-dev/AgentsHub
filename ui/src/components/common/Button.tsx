import React from 'react';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger' | 'outline' | 'success';
  size?: 'xs' | 'sm' | 'md' | 'lg';
  isLoading?: boolean;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
}

export const Button: React.FC<ButtonProps> = ({
  children,
  variant = 'secondary',
  size = 'sm',
  isLoading = false,
  leftIcon,
  rightIcon,
  className = '',
  disabled,
  ...props
}) => {
  const baseStyles = 'inline-flex items-center justify-center font-medium rounded-lg transition-colors select-none focus:outline-none focus:ring-1 focus:ring-sky-500/50 disabled:opacity-45 disabled:cursor-not-allowed';

  const sizeStyles = {
    xs: 'px-2 py-0.5 text-xs gap-1',
    sm: 'px-2.5 py-1 text-xs gap-1.5',
    md: 'px-3.5 py-1.5 text-sm gap-2',
    lg: 'px-4 py-2 text-base gap-2',
  }[size];

  const variantStyles = {
    primary: 'bg-sky-500 hover:bg-sky-400 text-slate-950 font-semibold shadow-sm shadow-sky-500/20 active:bg-sky-600',
    secondary: 'bg-surface-strong hover:bg-surface-strong text-content-main border border-surface-border/60 active:bg-surface-strong',
    ghost: 'bg-transparent hover:bg-surface-hover text-content-muted active:bg-surface-strong',
    danger: 'bg-rose-500/15 hover:bg-rose-500/25 text-state-danger border border-rose-500/30 active:bg-rose-500/35',
    outline: 'bg-transparent border border-surface-border hover:border-surface-borderLight text-content-main active:bg-surface-hover',
    success: 'bg-emerald-500/15 hover:bg-emerald-500/25 text-state-success border border-emerald-500/30 active:bg-emerald-500/35',
  }[variant];

  return (
    <button
      className={`${baseStyles} ${sizeStyles} ${variantStyles} ${className}`}
      disabled={disabled || isLoading}
      {...props}
    >
      {isLoading ? (
        <span className="w-3.5 h-3.5 border-2 border-current border-t-transparent rounded-full animate-spin" />
      ) : (
        leftIcon
      )}
      {children}
      {!isLoading && rightIcon}
    </button>
  );
};
