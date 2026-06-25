export interface TodaySummary {
  activeTime: string;
  activeSeconds: number;
  idleTime: string;
  idleSeconds: number;
  focusScore: number;
  topCategory: string;
  topCategoryTime: string;
  vsYesterday: number | null;
  totalKeystrokes: number;
  totalClicks: number;
}

export interface TimelineBlock {
  startHour: number;
  endHour: number;
  type: string;
  exeName: string;
  windowTitle: string | null;
  durationSeconds: number;
  project?: string | null;
}

export interface AppEntry {
  name: string;
  minutes: number;
}

export interface InputEntry {
  exeName: string;
  keystrokes: number;
  clicks: number;
}

export interface BrowserEntry {
  domain: string;
  visits: number;
  lastVisit: string;
}

export interface AudioEntry {
  exeName: string;
  sessions: number;
  firstSeen: string;
}

export interface HeatmapEntry {
  date: string;
  value: number;
}

export interface CategoryEntry {
  name: string;
  percentage: number;
  minutes: number;
}

export interface LiveStatus {
  currentApp: string;
  idleMinutes: number;
  isIdle: boolean;
  audibleTab: string | null;
  audioActive: boolean;
  systemState: string;
  pendingIdleReturn: boolean;
}

export interface BrowserHourEntry {
  hour: number;
  visits: number;
}

export interface DashboardData {
  summary: TodaySummary;
  timeline: TimelineBlock[];
  topApps: AppEntry[];
  heatmap: HeatmapEntry[];
  categories: CategoryEntry[];
  live: LiveStatus;
}
