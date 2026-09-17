export const categoryColors: Record<string, string> = {
  development: 'var(--cat-development)',
  dev: 'var(--cat-development)',
  work: 'var(--cat-work)',
  browsing: 'var(--cat-browsing)',
  browse: 'var(--cat-browsing)',
  communication: 'var(--cat-communication)',
  documents: 'var(--cat-documents)',
  media: 'var(--cat-media)',
  entertainment: 'var(--cat-entertainment)',
  design: 'var(--cat-design)',
  social: 'var(--cat-social)',
  gaming: 'var(--cat-gaming)',
  news: 'var(--cat-news)',
  finance: 'var(--cat-finance)',
  education: 'var(--cat-education)',
  health: 'var(--cat-health)',
  utilities: 'var(--cat-utilities)',
  system: 'var(--cat-system)',
  other: 'var(--cat-other)',
  idle: 'var(--cat-idle)',
  away: 'var(--cat-away)',
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
