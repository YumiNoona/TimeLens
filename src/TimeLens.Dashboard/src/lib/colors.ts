export const categoryColors: Record<string, string> = {
  development: '#C8E86A',
  dev: '#C8E86A',
  work: '#7ECFA8',
  browsing: '#E8A23A',
  browse: '#E8A23A',
  communication: '#60A5FA',
  documents: '#C084FC',
  media: '#A78BFA',
  entertainment: '#F59E0B',
  design: '#F472B6',
  social: '#E07070',
  gaming: '#E879F9',
  news: '#94A3B8',
  finance: '#34D399',
  education: '#38BDF8',
  health: '#2DD4BF',
  utilities: '#6B7280',
  system: '#6B7280',
  other: '#4A5145',
  idle: '#1C2118',
  away: '#0C0E08',
  gap: 'var(--md-surface)',
};

export function colorForCategory(name: string): string {
  return categoryColors[name.toLowerCase()] ?? categoryColors.other;
}

export const appColors = [
  '#C8E86A',
  '#E8A23A',
  '#60A5FA',
  '#E07070',
  '#C084FC',
  '#A78BFA',
  '#F59E0B',
  '#F472B6',
];

export function colorForApp(index: number): string {
  return appColors[index % appColors.length];
}
