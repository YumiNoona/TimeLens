import type { TimelineBlock } from './types';

export function fmtDuration(secs: number): string {
  if (secs < 60) return Math.max(0, Math.round(secs)) + 's';
  const m = Math.floor(secs / 60);
  if (m < 60) return m + 'm';
  const h = Math.floor(m / 60);
  return h + 'h ' + (m % 60) + 'm';
}

/**
 * Produces a human-readable activity timeline without mutating the recorded data.
 * Only contiguous identical contexts are joined. Gaps never become recorded time.
 */
export function normalizeTimeline(blocks: TimelineBlock[], minimumSeconds = 0): TimelineBlock[] {
  const ordered = blocks
    .filter(block => block.endHour > block.startHour && block.durationSeconds > 0)
    .toSorted((a, b) => a.startHour - b.startHour);
  const merged: TimelineBlock[] = [];

  for (const block of ordered) {
    const current = { ...block };
    const previous = merged.at(-1);
    const sameContext = previous &&
      previous.type.toLowerCase() === current.type.toLowerCase() &&
      previous.exeName.toLowerCase() === current.exeName.toLowerCase() &&
      previous.windowTitle === current.windowTitle && previous.project === current.project;

    if (previous && sameContext && Math.abs(current.startHour - previous.endHour) * 3600 < 0.001) {
      previous.endHour = Math.max(previous.endHour, current.endHour);
      previous.durationSeconds += current.durationSeconds;
      previous.windowTitle = current.windowTitle || previous.windowTitle;
      previous.project = current.project || previous.project;
      continue;
    }
    merged.push(current);
  }

  return merged.filter(block => block.durationSeconds >= minimumSeconds);
}

export function fmtTime(mins: number): string {
  return fmtDuration(mins * 60);
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

export function fmtPrecise(seconds: number): string {
  const total = Math.max(0, Math.round(seconds));
  const h = Math.floor(total / 3600), m = Math.floor(total % 3600 / 60), s = total % 60;
  return `${h ? h + 'h ' : ''}${h || m ? m + 'm ' : ''}${s}s`;
}
