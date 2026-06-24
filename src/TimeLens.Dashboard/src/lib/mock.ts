import type { DashboardData } from './types';

const tlBlocks = [
  { s: 0, e: 8, type: 'idle' },
  { s: 8, e: 9.5, type: 'work' },
  { s: 9.5, e: 11, type: 'development' },
  { s: 11, e: 11.5, type: 'browsing' },
  { s: 11.5, e: 13, type: 'work' },
  { s: 13, e: 13.75, type: 'idle' },
  { s: 13.75, e: 15.5, type: 'development' },
  { s: 15.5, e: 16, type: 'browsing' },
  { s: 16, e: 17.5, type: 'work' },
  { s: 17.5, e: 18, type: 'social' },
  { s: 18, e: 24, type: 'idle' },
];

const hmValues = [3, 5, 7, 4, 2, 1, 0, 6, 8, 9, 7, 6, 2, 1, 4, 7, 9, 8, 7, 3, 0, 7, 9, 8, 9, 8, 4, 2];

const today = new Date();
const hmDates = Array.from({ length: 28 }, (_, i) => {
  const d = new Date(today);
  d.setDate(d.getDate() - (27 - i));
  return d.toISOString().slice(0, 10);
});

export const mockData: DashboardData = {
  summary: {
    activeTime: '6h 42m',
    activeSeconds: 24120,
    idleTime: '1h 08m',
    idleSeconds: 4080,
    focusScore: 82,
    topCategory: 'Work',
    topCategoryTime: '4h 22m',
    vsYesterday: 34,
    totalKeystrokes: 2847,
    totalClicks: 512,
  },
  timeline: tlBlocks.map(b => ({ startHour: b.s, endHour: b.e, type: b.type, exeName: 'mock.exe', windowTitle: null, durationSeconds: Math.round((b.e - b.s) * 3600) })),
  topApps: [
    { name: 'code.exe', minutes: 142 },
    { name: 'chrome.exe', minutes: 98 },
    { name: 'slack.exe', minutes: 76 },
    { name: 'figma.exe', minutes: 54 },
    { name: 'notion.exe', minutes: 32 },
  ],
  heatmap: hmDates.map((d, i) => ({ date: d, value: hmValues[i] })),
  categories: [
    { name: 'Work', percentage: 65, minutes: 262 },
    { name: 'Development', percentage: 15, minutes: 60 },
    { name: 'Browsing', percentage: 12, minutes: 48 },
    { name: 'Social', percentage: 8, minutes: 32 },
  ],
  live: {
    currentApp: 'code.exe',
    idleMinutes: 2,
    isIdle: false,
    audibleTab: null,
    audioActive: false,
    systemState: 'active',
    pendingIdleReturn: false,
  },
};
