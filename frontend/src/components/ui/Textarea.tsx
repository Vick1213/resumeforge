import { forwardRef } from 'react';
import type { TextareaHTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  invalid?: boolean;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(({ className, invalid, ...props }, ref) => {
  return (
    <textarea
      ref={ref}
      className={cn(
        'w-full rounded-[var(--radius-sm)] border bg-[var(--bg-elevated)] px-3 py-2 text-sm text-[var(--text)]',
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
Textarea.displayName = 'Textarea';
