import type { BrowserHourEntry, DashboardData } from './types';

const API = '';

export async function fetchJson<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`API ${res.status}: ${res.statusText}`);
  return res.json();
}

export async function getDashboardData(date?: string): Promise<DashboardData> {
  const url = date ? `${API}/api/summary?date=${date}` : `${API}/api/summary`;
  return await fetchJson<DashboardData>(url);
}

export async function getBrowserHourly(date?: string): Promise<BrowserHourEntry[]> {
  const query = date ? `?date=${encodeURIComponent(date)}` : '';
  return fetchJson<BrowserHourEntry[]>(`${API}/api/browser-hourly${query}`);
}
