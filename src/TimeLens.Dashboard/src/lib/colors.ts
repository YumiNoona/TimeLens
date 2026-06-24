export const categoryColors: Record<string, string> = {
  development: '#C8E86A',
  dev: '#C8E86A',
  work: '#7ECFA8',
  browsing: '#E8A23A',
  browse: '#E8A23A',
  communication: '#7ECFA8',
  documents: '#7ECFA8',
  media: '#7ECFA8',
  entertainment: '#A78BFA',
  design: '#F472B6',
  social: '#E07070',
  system: '#6B7280',
  other: '#4A5145',
  idle: '#1C2118',
  away: '#0C0E08',
};

export function colorForCategory(name: string): string {
  return categoryColors[name.toLowerCase()] ?? categoryColors.other;
}

export const appColors = [
  '#C8E86A',
  '#E8A23A',
  '#7ECFA8',
  '#E07070',
  '#7ECFA8',
  '#C8E86A',
  '#E8A23A',
  '#E07070',
];

export function colorForApp(index: number): string {
  return appColors[index % appColors.length];
}
