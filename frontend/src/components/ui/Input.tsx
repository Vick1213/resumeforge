import { forwardRef } from 'react';
import type { InputHTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(({ className, invalid, ...props }, ref) => {
  return (
    <input
      ref={ref}
      className={cn(
        'h-9 w-full rounded-[var(--radius-sm)] border bg-[var(--bg-elevated)] px-3 text-sm text-[var(--text)]',
        'placeholder:text-[var(--text-faint)] transition-colors',
        'focus-visible:outline focus-visible:outline-2 focus-visible:outline-[var(--accent)] focus-visible:outline-offset-1',
        invalid ? 'border-[var(--danger)]' : 'border-[var(--border-strong)]',
        className,
      )}
      aria-invalid={invalid}
      {...props}
    />
  );
});
Input.displayName = 'Input';
