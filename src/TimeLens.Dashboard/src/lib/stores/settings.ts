import { writable } from 'svelte/store';

export const timeFormat = writable<'12h' | '24h'>('12h');
export const settingsLoaded = writable(false);
export const timelineMinSegmentSeconds = writable(60);
export const heatmapDays = writable(182);
