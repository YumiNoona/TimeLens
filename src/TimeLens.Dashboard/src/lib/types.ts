export interface TodaySummary {
  activeTime: string;
  activeSeconds: number;
  idleTime: string;
  idleSeconds: number;
  focusScore: number;
  topCategory: string;
  topCategoryTime: string;
  vsYesterday: number | null;
}

export interface TimelineBlock {
  startHour: number;
  endHour: number;
  type: string;
}

export interface AppEntry {
  name: string;
  minutes: number;
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

export interface DashboardData {
  summary: TodaySummary;
  timeline: TimelineBlock[];
  topApps: AppEntry[];
  heatmap: HeatmapEntry[];
  categories: CategoryEntry[];
  live: LiveStatus;
}
