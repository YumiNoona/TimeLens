import type { DashboardData } from './types';

declare const __DEV__: boolean;

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`API ${res.status}: ${res.statusText}`);
  return res.json();
}

export async function getDashboardData(date?: string): Promise<DashboardData> {
  const url = date ? `/api/summary?date=${date}` : '/api/summary';
  try {
    return await fetchJson<DashboardData>(url);
  } catch (e) {
    if (typeof __DEV__ !== 'undefined' && __DEV__) {
      const { mockData } = await import('./mock');
      console.warn('API unreachable, showing mock data (dev only)');
      return {
        ...mockData,
        live: { currentApp: '—', idleMinutes: 0, isIdle: false, audibleTab: null, audioActive: false, systemState: 'active', pendingIdleReturn: false },
      };
    }
    throw e;
  }
}
