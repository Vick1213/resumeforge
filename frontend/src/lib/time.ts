const TIME_SPAN_PATTERN = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d+))?$/;

/**
 * Parses a .NET TimeSpan constant-format string (e.g. "00:00:01.2345678" or
 * "1.04:00:00") into milliseconds. Returns 0 for unparseable input rather
 * than throwing, since this only ever feeds a visualization.
 */
export function parseTimeSpanMs(value: string): number {
  const match = TIME_SPAN_PATTERN.exec(value.trim());
  if (!match) return 0;

  const sign = match[1] === '-' ? -1 : 1;
  const days = Number(match[2] ?? 0);
  const hours = Number(match[3] ?? 0);
  const minutes = Number(match[4] ?? 0);
  const seconds = Number(match[5] ?? 0);
  const fraction = (match[6] ?? '').padEnd(3, '0').slice(0, 3);
  const millis = fraction ? Number(fraction) : 0;

  const totalMs = ((days * 24 + hours) * 60 + minutes) * 60_000 + seconds * 1000 + millis;
  return sign * totalMs;
}

export function formatDurationMs(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`;
  const seconds = ms / 1000;
  if (seconds < 60) return `${seconds.toFixed(seconds < 10 ? 2 : 1)}s`;
  const minutes = Math.floor(seconds / 60);
  const remainder = Math.round(seconds % 60);
  return `${minutes}m ${remainder}s`;
}

export function formatRelativeTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  const diffMs = date.getTime() - Date.now();
  const diffMinutes = Math.round(diffMs / 60_000);
  const formatter = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

  const abs = Math.abs(diffMinutes);
  if (abs < 60) return formatter.format(diffMinutes, 'minute');
  const diffHours = Math.round(diffMinutes / 60);
  if (Math.abs(diffHours) < 24) return formatter.format(diffHours, 'hour');
  const diffDays = Math.round(diffHours / 24);
  if (Math.abs(diffDays) < 30) return formatter.format(diffDays, 'day');
  const diffMonths = Math.round(diffDays / 30);
  return formatter.format(diffMonths, 'month');
}
