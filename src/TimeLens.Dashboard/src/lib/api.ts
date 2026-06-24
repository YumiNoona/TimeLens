import type { DashboardData } from './types';

const API = 'http://127.0.0.1:47821';

async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`API ${res.status}: ${res.statusText}`);
  return res.json();
}

export async function getDashboardData(date?: string): Promise<DashboardData> {
  const url = date ? `${API}/api/summary?date=${date}` : `${API}/api/summary`;
  return await fetchJson<DashboardData>(url);
}
