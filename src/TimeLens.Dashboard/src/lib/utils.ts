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

export function fmtHourShort(h: number): string {
  if (h === 0 || h === 24) return '12a';
  if (h < 12) return h + 'a';
  if (h === 12) return '12p';
  return (h - 12) + 'p';
}

export function fmtHourFull(n: number): string {
  const h = Math.floor(n);
  const m = Math.floor((n % 1) * 60);
  const mm = String(Math.min(m, 59)).padStart(2, '0');
  if (h === 0) return `12:${mm}am`;
  if (h < 12) return `${h}:${mm}am`;
  if (h === 12) return `12:${mm}pm`;
  return `${h - 12}:${mm}pm`;
}
