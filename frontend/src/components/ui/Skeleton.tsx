import type { HTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('animate-skeleton rounded-[var(--radius-sm)] bg-[var(--bg-inset)]', className)}
      aria-hidden="true"
      {...props}
    />
  );
}
