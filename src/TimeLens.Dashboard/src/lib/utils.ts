export function fmtDuration(secs: number): string {
  const m = Math.floor(secs / 60);
  if (m < 60) return m + 'm';
  const h = Math.floor(m / 60);
  return h + 'h ' + (m % 60) + 'm';
}

export function fmtTime(mins: number): string {
  const h = Math.floor(mins / 60);
  const m = mins % 60;
  return (h > 0 ? h + 'h ' : '') + m + 'm';
}

export function fmtHourShort(h: number, fmt?: '12h' | '24h'): string {
  const hour = Math.floor(h);
  if (fmt === '24h') return String(hour).padStart(2, '0') + ':00';
  if (hour === 0 || hour === 24) return '12a';
  if (hour < 12) return hour + 'a';
  if (hour === 12) return '12p';
  return (hour - 12) + 'p';
}

export function fmtHourFull(n: number, fmt?: '12h' | '24h'): string {
  const h = Math.floor(n);
  const m = Math.floor((n % 1) * 60);
  const mm = String(Math.min(m, 59)).padStart(2, '0');
  if (fmt === '24h') return `${String(h).padStart(2, '0')}:${mm}`;
  if (h === 0) return `12:${mm}am`;
  if (h < 12) return `${h}:${mm}am`;
  if (h === 12) return `12:${mm}pm`;
  return `${h - 12}:${mm}pm`;
}
