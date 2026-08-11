import type { TimelineBlock } from './types';

export function fmtDuration(secs: number): string {
  if (secs < 60) return '<1m';
  const m = Math.floor(secs / 60);
  if (m < 60) return m + 'm';
  const h = Math.floor(m / 60);
  return h + 'h ' + (m % 60) + 'm';
}

/**
 * Produces a human-readable activity timeline without mutating the recorded data.
 * Adjacent samples from the same app/category are joined, then sub-minute window
 * switches are omitted so they do not render as duplicate timestamps or 0m rows.
 */
export function normalizeTimeline(blocks: TimelineBlock[], minimumSeconds = 60): TimelineBlock[] {
  const ordered = blocks
    .filter(block => block.endHour > block.startHour && block.durationSeconds > 0)
    .toSorted((a, b) => a.startHour - b.startHour);
  const merged: TimelineBlock[] = [];

  for (const block of ordered) {
    const current = { ...block };
    const previous = merged.at(-1);
    const gapSeconds = previous
      ? Math.max(0, Math.round((current.startHour - previous.endHour) * 3600))
      : Number.POSITIVE_INFINITY;
    const sameContext = previous &&
      previous.type.toLowerCase() === current.type.toLowerCase() &&
      previous.exeName.toLowerCase() === current.exeName.toLowerCase();

    if (previous && sameContext && gapSeconds <= 20) {
      previous.endHour = Math.max(previous.endHour, current.endHour);
      previous.durationSeconds += current.durationSeconds + gapSeconds;
      previous.windowTitle = current.windowTitle || previous.windowTitle;
      previous.project = current.project || previous.project;
      continue;
    }
    merged.push(current);
  }

  return merged.filter(block => block.durationSeconds >= minimumSeconds);
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
