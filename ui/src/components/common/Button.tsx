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
    secondary: 'bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700/60 active:bg-slate-800',
    ghost: 'bg-transparent hover:bg-slate-800/60 text-slate-300 active:bg-slate-800',
    danger: 'bg-rose-500/15 hover:bg-rose-500/25 text-rose-400 border border-rose-500/30 active:bg-rose-500/35',
    outline: 'bg-transparent border border-slate-700 hover:border-slate-600 text-slate-200 active:bg-slate-800/40',
    success: 'bg-emerald-500/15 hover:bg-emerald-500/25 text-emerald-400 border border-emerald-500/30 active:bg-emerald-500/35',
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
