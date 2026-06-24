import type { DashboardData } from './types';

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`API ${res.status}: ${res.statusText}`);
  return res.json();
}

export async function getDashboardData(date?: string): Promise<DashboardData> {
  const url = date ? `/api/summary?date=${date}` : '/api/summary';
  return await fetchJson<DashboardData>(url);
}
