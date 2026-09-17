<script lang="ts">
  import type { AppEntry, BrowserEntry, CategoryEntry, LiveStatus, TodaySummary, WebMediaSummary } from '../types';
  import { fmtPrecise } from '../utils';
  import { showSeconds } from '../stores/settings';

  let {
    summary,
    live,
    topApps,
    categories,
    browserSites,
    webMedia
  }: {
    summary: TodaySummary;
    live: LiveStatus;
    topApps: AppEntry[];
    categories: CategoryEntry[];
    browserSites: BrowserEntry[];
    webMedia: WebMediaSummary;
  } = $props();

  const topApp = $derived(topApps[0]);
  const topCategory = $derived(categories[0]);
  const visits = $derived(browserSites.reduce((total, site) => total + site.visits, 0));
  const currentLabel = $derived(live.isIdle ? 'Idle' : (live.currentApp || 'Waiting for activity'));
</script>

<section class="snapshot-card" aria-labelledby="snapshot-title">
  <header>
    <div class="heading"><i class="ti ti-sparkles" aria-hidden="true"></i><div><h2 id="snapshot-title">Day snapshot</h2><p>The useful signals behind today</p></div></div>
    <span class:idle={live.isIdle} class="status"><i></i>{live.isIdle ? 'Idle' : 'Active'}</span>
  </header>

  <div class="snapshot-grid">
    <div class="snapshot-item current">
      <span><i class="ti ti-device-desktop" aria-hidden="true"></i>Right now</span>
      <strong title={currentLabel}>{currentLabel}</strong>
      <small>{live.audioActive ? 'Media is also playing' : 'Foreground activity'}</small>
    </div>
    <div class="snapshot-item">
      <span><i class="ti ti-apps" aria-hidden="true"></i>Most used</span>
      <strong title={topApp?.name ?? 'No activity yet'}>{topApp?.name ?? 'No activity yet'}</strong>
      <small>{topApp ? fmtPrecise(topApp.minutes * 60, $showSeconds) : '—'}</small>
    </div>
    <div class="snapshot-item">
      <span><i class="ti ti-category" aria-hidden="true"></i>Top category</span>
      <strong class="capitalize">{topCategory?.name ?? summary.topCategory ?? '—'}</strong>
      <small>{topCategory ? fmtPrecise(topCategory.minutes * 60, $showSeconds) : '—'}</small>
    </div>
    <div class="snapshot-item">
      <span><i class="ti ti-player-play" aria-hidden="true"></i>Web media</span>
      <strong>{fmtPrecise(webMedia.playbackSeconds, $showSeconds)}</strong>
      <small>{visits.toLocaleString()} browser visit{visits === 1 ? '' : 's'}</small>
    </div>
  </div>
</section>

<style>
  .snapshot-card{min-width:0;min-height:225px;box-sizing:border-box;padding:14px 16px 12px;border:1px solid var(--md-outline);border-radius:var(--shape-lg);background:var(--md-surface-1)}
  header,.heading,.heading>div,.status,.snapshot-item>span{display:flex;align-items:center}.heading{min-width:0;gap:9px}.heading>i{color:var(--md-primary);font-size:17px}.heading>div{min-width:0;align-items:flex-start;flex-direction:column;gap:2px}h2{color:var(--clr-text-pri);font-size:13px;font-weight:650}.heading p{color:var(--clr-text-ter);font-size:10px}
  .status{margin-left:auto;gap:6px;padding:4px 8px;border:1px solid color-mix(in srgb,var(--md-primary) 18%,var(--clr-border));border-radius:99px;color:var(--md-primary);background:color-mix(in srgb,var(--md-primary) 7%,transparent);font:600 9px var(--font-mono)}.status i{width:6px;height:6px;border-radius:50%;background:currentColor;box-shadow:0 0 8px color-mix(in srgb,currentColor 48%,transparent)}.status.idle{color:var(--clr-text-sec)}
  .snapshot-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px;margin-top:14px}.snapshot-item{min-width:0;min-height:72px;display:flex;flex-direction:column;justify-content:center;padding:9px 10px;border:1px solid color-mix(in srgb,var(--md-primary) 7%,var(--clr-border));border-radius:9px;background:color-mix(in srgb,var(--md-primary) 2.5%,var(--clr-bg-sec))}.snapshot-item>span{gap:5px;color:var(--clr-text-ter);font-size:9px;text-transform:uppercase;letter-spacing:.05em}.snapshot-item>span i{color:var(--md-primary);font-size:12px}.snapshot-item strong{margin-top:4px;overflow:hidden;color:var(--clr-text-pri);font:600 12px var(--font-mono);text-overflow:ellipsis;white-space:nowrap}.snapshot-item strong.capitalize{text-transform:capitalize}.snapshot-item small{margin-top:2px;color:var(--clr-text-sec);font-size:9px}
  @media(max-width:520px){.snapshot-card{min-height:0}.snapshot-grid{grid-template-columns:1fr}.snapshot-item{min-height:62px}}
</style>
