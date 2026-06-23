import { writable, derived } from 'svelte/store';
import type { DashboardData } from '../types';
import { getDashboardData } from '../api';

export const data = writable<DashboardData | null>(null);
export const loading = writable(true);
export const error = writable<string | null>(null);

export const live = derived(data, ($data) => $data?.live ?? null);

function pad(n: number): string {
  return n.toString().padStart(2, '0');
}

export const timeStr = derived(data, ($data) => {
  if (!$data) return '';
  const s = $data.summary.activeSeconds;
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return `${h}h ${pad(m)}m`;
});

export async function refresh(): Promise<void> {
  loading.set(true);
  error.set(null);
  try {
    const d = await getDashboardData();
    data.set(d);
  } catch (e) {
    error.set(e instanceof Error ? e.message : 'Unknown error');
  } finally {
    loading.set(false);
  }
}
