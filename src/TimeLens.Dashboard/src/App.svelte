<script lang="ts">
  import { onMount } from 'svelte';
  import NavRail from './lib/components/NavRail.svelte';
  import StatCard from './lib/components/StatCard.svelte';

  import TopApps from './lib/components/TopApps.svelte';
  import CategoryBreakdown from './lib/components/CategoryBreakdown.svelte';

  import AppsView from './lib/components/AppsView.svelte';
  import TimelineView from './lib/components/TimelineView.svelte';
  import RulesView from './lib/components/RulesView.svelte';
  import SettingsView from './lib/components/SettingsView.svelte';
  import BlockView from './lib/components/BlockView.svelte';
  import HistoryView from './lib/components/HistoryView.svelte';
  import TopSites from './lib/components/TopSites.svelte';
  import SiteTimeCard from './lib/components/SiteTimeCard.svelte';
  import BrowserHourlyCard from './lib/components/BrowserHourlyCard.svelte';
  import MediaCard from './lib/components/MediaCard.svelte';
  import type { BrowserEntry, AudioEntry } from './lib/types';
  import { fetchJson, getBrowserHourly } from './lib/api';
  import { data, loading, error, refresh } from './lib/stores/activity';
  import { timeFormat as timeFormatStore, timelineMinSegmentSeconds, heatmapDays } from './lib/stores/settings';
  import { reorderable } from './lib/actions/reorderable';

  let browserSites = $state<BrowserEntry[]>([]);
  let browserTime = $state<{domain: string; totalMinutes: number}[]>([]);
  let audioSessions = $state<AudioEntry[]>([]);
  let browserHourlyRaw = $state<{hour: number; visits: number}[]>([]);
  let browserHourly = $derived.by(() => {
    const map = new Map(browserHourlyRaw.map(h => [h.hour, h.visits]));
    return Array.from({ length: 24 }, (_, i) => ({ hour: i, visits: map.get(i) ?? 0 }));
  });
  let timelineGrouped = $state(true);
  let showTitles = $state(false);

  let view = $state('today');
  let pollInterval = $state(30);
  let now = $state(new Date());
  let activeTheme = 'default';
  let density = 'comfortable';
  let motionEnabled = true;

  function goTo(id: string) { view = id; }

  const dateStr = $derived(now.toLocaleDateString('en-US', {
    weekday: 'long', month: 'long', day: 'numeric',
  }));

  const greeting = $derived.by(() => {
    const h = now.getHours();
    if (h < 5) return 'Good night';
    if (h < 12) return 'Good morning';
    if (h < 17) return 'Good afternoon';
    return 'Good evening';
  });

  function siteMinutes(domain: string): number {
    return browserTime.find((entry) => entry.domain === domain)?.totalMinutes ?? 0;
  }

  function compactDuration(minutes: number): string {
    if (minutes < 60) return `${minutes}m`;
    return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
  }

  function applyTheme(t: string) {
    activeTheme = t;
    applyInterfacePreferences();
  }

  function applyDensity(value: string) {
    density = value === 'compact' ? 'compact' : 'comfortable';
    applyInterfacePreferences();
  }

  function applyMotion(value: boolean) {
    motionEnabled = value;
    applyInterfacePreferences();
  }

  function applyPollInterval(seconds: number) {
    pollInterval = Math.max(5, seconds);
    stopPoll();
    if (!document.hidden) startPoll();
  }

  function applyInterfacePreferences() {
    const root = document.documentElement;
    for (const name of [...root.classList]) {
      if (name.startsWith('theme-')) root.classList.remove(name);
    }
    if (activeTheme !== 'default') root.classList.add('theme-' + activeTheme);
    root.classList.toggle('density-compact', density === 'compact');
    root.classList.toggle('motion-off', !motionEnabled);
  }

  let pollTimer: ReturnType<typeof setInterval> | null = null;

  async function loadCompanionData(): Promise<void> {
    const [sites, time, audio, hours] = await Promise.allSettled([
      fetchJson<BrowserEntry[]>('/api/browser-summary'),
      fetchJson<{domain: string; totalMinutes: number}[]>('/api/browser-time-summary'),
      fetchJson<AudioEntry[]>('/api/audio-summary'),
      getBrowserHourly()
    ]);

    if (sites.status === 'fulfilled') browserSites = sites.value;
    if (time.status === 'fulfilled') browserTime = time.value;
    if (audio.status === 'fulfilled') audioSessions = audio.value;
    if (hours.status === 'fulfilled') browserHourlyRaw = hours.value;
  }

  async function loadSettings(): Promise<void> {
    try {
      const s = await fetchJson<Record<string, unknown>>('/api/settings');
      if (typeof s.theme === 'string') applyTheme(s.theme);
      timelineGrouped = typeof s.timelineGrouped === 'boolean' ? s.timelineGrouped : true;
      showTitles = typeof s.showTitles === 'boolean' ? s.showTitles : false;
      if (s.timeFormat === '24h' || s.timeFormat === '12h') timeFormatStore.set(s.timeFormat);
      if (typeof s.pollIntervalSeconds === 'number') pollInterval = s.pollIntervalSeconds;
      if (s.density === 'compact' || s.density === 'comfortable') applyDensity(s.density);
      if (typeof s.motionEnabled === 'boolean') applyMotion(s.motionEnabled);
      if (typeof s.timelineMinSegmentSeconds === 'number') timelineMinSegmentSeconds.set(s.timelineMinSegmentSeconds);
      if (typeof s.heatmapDays === 'number') heatmapDays.set(s.heatmapDays);
      const allowedViews = ['today', 'history', 'apps', 'browser', 'timeline', 'block', 'rules', 'settings'];
      if (typeof s.defaultView === 'string' && allowedViews.includes(s.defaultView)) view = s.defaultView;
    } catch { }
  }

  function startPoll() {
    if (pollTimer) return;
    const interval = Math.max(5, pollInterval) * 1000;
    pollTimer = setInterval(async () => {
      now = new Date();
      await Promise.all([refresh(true), loadCompanionData()]);
    }, interval);
  }

  function stopPoll() { if (pollTimer) { clearInterval(pollTimer); pollTimer = null; } }

  onMount(async () => {
    await Promise.all([refresh(), loadSettings(), loadCompanionData()]);

    document.addEventListener('visibilitychange', onVisibility);
    if (!document.hidden) startPoll();
    return () => { stopPoll(); document.removeEventListener('visibilitychange', onVisibility); };
  });

  function onVisibility() {
    if (document.hidden) {
      stopPoll();
      return;
    }
    now = new Date();
    void Promise.all([refresh(true), loadCompanionData()]);
    startPoll();
  }
</script>

<div class="shell">
  <NavRail active={view} onselect={(id) => view = id} />

  <main class="main">
    {#if $error}
      <div class="error-banner" role="alert">
        <span><i class="ti ti-alert-circle" aria-hidden="true"></i> Could not refresh activity: {$error}</span>
        <button type="button" onclick={() => refresh()}>Retry</button>
      </div>
    {/if}

    <div class="view-pane">
    {#if $loading && !$data && (view === 'today' || view === 'apps' || view === 'timeline')}
      <div class="view-loading">
        <div class="view-loading-pulse"></div>
        <p>Loading…</p>
      </div>
    {:else if view === 'today'}
      {#if $data}
        <div class="today-header">
          <div class="today-header-left">
            <p class="today-greeting">{greeting}</p>
            <h1 class="today-date">{dateStr}</h1>
            <p class="today-purpose">Your current-day overview: time, focus, input, apps, and categories.</p>
          </div>
        </div>

        <div class="today-content">
          <section class="today-hero" use:reorderable={{ key: 'today:stats' }}>
            <StatCard
              label="Active time"
              value={$data.summary.activeTime}
              variant="hero"
              accent={true}
              icon="ti-clock-hour-4"
              chip={$data.summary.vsYesterday !== null
                ? ($data.summary.vsYesterday === 0
                  ? '= yesterday'
                  : `${$data.summary.vsYesterday > 0 ? '↑' : '↓'} ${Math.abs($data.summary.vsYesterday)}m vs yesterday`)
                : ''}
              chipClass={$data.summary.vsYesterday === null
                ? ''
                : ($data.summary.vsYesterday === 0
                  ? 'chip-neu'
                  : ($data.summary.vsYesterday > 0 ? 'chip-up' : 'chip-down'))}
            />
            <StatCard
              label="Focus score"
              value={String($data.summary.focusScore)}
              variant="hero"
              accent={$data.summary.focusScore >= 40}
              icon="ti-target-arrow"
              chip={$data.summary.focusScore >= 70
                ? 'Productive day'
                : $data.summary.focusScore >= 40
                  ? 'Mixed activity'
                  : 'Distracted day'}
              chipClass={$data.summary.focusScore >= 70
                ? 'chip-up'
                : $data.summary.focusScore >= 40
                  ? 'chip-neu'
                  : 'chip-down'}
            />
            <StatCard
              label="Keystrokes"
              value={$data.summary.totalKeystrokes.toLocaleString()}
              variant="hero"
              icon="ti-keyboard"
              chip={$data.summary.totalClicks.toLocaleString() + ' clicks'}
              chipClass="chip-neu"
            />
            <StatCard
              label="Idle time"
              value={$data.summary.idleTime}
              variant="hero"
              icon="ti-coffee"
              chip={$data.summary.idleSeconds > 0
                ? Math.round($data.summary.idleSeconds / ($data.summary.idleSeconds + $data.summary.activeSeconds) * 100) + '% of session'
                : 'No idle time'}
              chipClass="chip-down"
            />
          </section>

          <div class="today-grid" use:reorderable={{ key: 'today:insights' }}>
            <TopApps apps={$data.topApps} />
            <CategoryBreakdown categories={$data.categories} />
          </div>

          {#if browserSites.length > 0}
            <div class="card">
              <div class="card-header">
                <i class="ti ti-world" aria-hidden="true"></i>
                <div class="card-title">Top sites</div>
                <button class="view-all-link" onclick={() => goTo('browser')}>View all <i class="ti ti-arrow-right"></i></button>
              </div>
              <div class="teaser-list">
                {#each browserSites.slice(0, 3) as site}
                  <div class="teaser-row">
                    <span class="teaser-domain">{site.domain.replace(/^www\./, '')}</span>
                    <span class="teaser-count">{compactDuration(siteMinutes(site.domain))} · {site.visits} visit{site.visits !== 1 ? 's' : ''}</span>
                  </div>
                {/each}
              </div>
            </div>
          {/if}

          {#if audioSessions.length > 0}
            <div class="card">
              <div class="card-header">
                <i class="ti ti-volume-2" aria-hidden="true"></i>
                <div class="card-title">Media active</div>
              </div>
              <div class="teaser-list">
                {#each audioSessions as a}
                  <div class="teaser-row">
                    <span class="teaser-domain">{a.exeName}</span>
                    <span class="teaser-count">{a.sessions} session{a.sessions !== 1 ? 's' : ''}</span>
                  </div>
                {/each}
              </div>
            </div>
          {/if}
        </div>
      {/if}

    {:else if view === 'history'}
      <HistoryView {timelineGrouped} {showTitles} />
    {:else if view === 'browser'}
      <div class="topbar">
        <div class="page-heading">
          <p class="page-eyebrow">Web activity</p>
          <h1 class="page-title">Browser</h1>
          <p class="page-purpose">Domains, visits, browsing time, hourly patterns, and audible media.</p>
        </div>
      </div>
      <div class="content">
        <div class="stat-row">
          <StatCard label="Unique sites" value={browserSites.length} />
          <StatCard label="Total visits" value={browserSites.reduce((a, b) => a + b.visits, 0)} />
          <StatCard label="Browse time" value={`${browserTime.filter(bt => bt.domain !== '127.0.0.1' && bt.domain !== 'test.example.com').reduce((a, b) => a + b.totalMinutes, 0)}m`} />
        </div>
        {#if browserSites.length === 0 && browserTime.length === 0}
          <div class="empty-view">
            <i class="ti ti-world-off" aria-hidden="true"></i>
            <span>No browsing data yet</span>
            <span class="empty-hint">Install the browser extension to start tracking</span>
          </div>
        {:else}
          <div class="two-col">
           <TopSites sites={browserSites} />
             <SiteTimeCard {browserTime} />
           </div>
            <div class="browser-detail-grid">
              <BrowserHourlyCard {browserHourly} />
              <MediaCard {audioSessions} />
            </div>
        {/if}
      </div>
    {:else if view === 'apps' && $data}
      <div class="topbar">
        <div class="page-heading">
          <p class="page-eyebrow">Desktop activity</p>
          <h1 class="page-title">Apps</h1>
          <p class="page-purpose">Compare desktop app usage, keyboard activity, clicks, and uncategorized apps.</p>
        </div>
      </div>
      <div class="content"><AppsView data={$data} browserSites={browserSites} {browserTime} /></div>
    {:else if view === 'timeline' && $data}
      <div class="topbar">
        <div class="page-heading">
          <p class="page-eyebrow">Activity timeline</p>
          <h1 class="page-title">Timeline</h1>
          <p class="page-purpose">Follow the day chronologically and inspect meaningful activity segments.</p>
        </div>
      </div>
      <div class="content"><TimelineView data={$data} timelineGrouped={timelineGrouped} {showTitles} /></div>
    {:else if view === 'rules'}
      <div class="topbar">
        <div class="page-heading">
          <p class="page-eyebrow">Organization</p>
          <h1 class="page-title">Rules</h1>
          <p class="page-purpose">Teach TimeLens how apps, window titles, and domains should be categorized.</p>
        </div>
      </div>
      <div class="content"><RulesView /></div>
    {:else if view === 'block'}
      <div class="topbar">
        <div class="page-heading">
          <p class="page-eyebrow">Focus controls</p>
          <h1 class="page-title">Block</h1>
          <p class="page-purpose">Shape focus sessions with schedules, limits, and distraction controls.</p>
        </div>
      </div>
      <div class="content"><BlockView /></div>
    {:else if view === 'settings'}
      <div class="topbar">
        <div class="page-heading">
          <p class="page-eyebrow">Preferences</p>
          <h1 class="page-title">Settings</h1>
          <p class="page-purpose">Configure tracking, privacy, appearance, reminders, storage, and goals.</p>
        </div>
      </div>
      <div class="content"><SettingsView
        ontheme={applyTheme}
        ondensity={applyDensity}
        onmotion={applyMotion}
        ontimelinegrouped={(value) => timelineGrouped = value}
        onshowtitles={(value) => showTitles = value}
        onpollinterval={applyPollInterval}
      /></div>
    {:else if !$data}
      <div class="placeholder-view">
        <i class="ti ti-loader" aria-hidden="true"></i>
        <p class="title-small" style="margin-top: var(--sp-2)">Loading…</p>
      </div>
    {/if}
    </div>
  </main>
</div>

<style>
  .view-pane { width: 100%; min-width: 0; }
  .shell {
    display: flex;
    height: 100vh;
    overflow: hidden;
    background: var(--clr-bg-pri);
  }

  .main {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    padding: 0 clamp(14px, 1.35vw, 24px);
  }

  /* ── View Loading Skeleton ── */
  .view-loading {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    flex: 1;
    gap: var(--space-4);
    color: var(--clr-text-sec);
    min-height: 200px;
  }

  .view-loading-pulse {
    width: 32px;
    height: 32px;
    border-radius: var(--radius-full);
    background: var(--md-primary);
    opacity: 0.3;
    animation: loading-pulse 1.5s var(--ease-in-out) infinite;
  }

  @keyframes loading-pulse {
    0%, 100% { transform: scale(0.8); opacity: 0.2; }
    50% { transform: scale(1.2); opacity: 0.5; }
  }

  .view-loading p {
    font-size: var(--text-sm);
    font-weight: var(--weight-medium);
    color: var(--clr-text-ter);
  }

  /* ── Today: Header ── */
  .today-header {
    display: flex;
    align-items: flex-end;
    padding: var(--space-4) 0 var(--space-5);
    flex-shrink: 0;
  }

  .today-greeting {
    font-size: var(--type-page-eyebrow);
    font-weight: var(--weight-semibold);
    color: var(--md-primary);
    letter-spacing: 0.08em;
    text-transform: uppercase;
    margin-bottom: var(--space-1);
  }

  .today-date {
    font-size: var(--type-page-title);
    font-weight: var(--weight-semibold);
    color: var(--clr-text-pri);
    letter-spacing: -0.03em;
    line-height: 1.15;
  }

  .today-purpose, .page-purpose {
    color: var(--clr-text-sec);
    font-size: var(--type-page-subtitle);
    line-height: 1.45;
    margin-top: var(--space-2);
  }

  /* ── Today: Content ── */
  .today-content {
    display: flex;
    flex-direction: column;
    gap: var(--space-5);
    padding-bottom: var(--space-10);
  }

  /* ── Hero stats ── */
  .today-hero {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: var(--space-4);
  }

  /* ── Two-column grid ── */
  .today-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-4);
  }

  /* ── Today teaser cards ── */
  .view-all-link {
    font-size: var(--text-xs);
    font-weight: var(--weight-medium);
    color: var(--md-primary);
    background: none;
    border: none;
    cursor: pointer;
    font-family: inherit;
    display: flex;
    align-items: center;
    gap: 4px;
    margin-left: auto;
    padding: 2px 0;
    transition: opacity 0.15s;
  }
  .view-all-link:hover { opacity: 0.7; }
  .view-all-link i { font-size: 12px; }

  .teaser-list {
    display: flex;
    flex-direction: column;
  }

  .teaser-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--space-2) var(--space-4);
    border-top: 1px solid var(--clr-border);
  }

  .teaser-domain {
    font-size: var(--text-sm);
    font-family: var(--font-mono);
    color: var(--clr-text-pri);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    flex: 1;
    margin-right: var(--space-3);
  }

  .teaser-count {
    font-size: var(--text-xs);
    font-family: var(--font-mono);
    color: var(--clr-text-sec);
    font-feature-settings: 'tnum';
    flex-shrink: 0;
  }

  /* ── Topbar (for non-Today pages) ── */
  .topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--space-5) 0 var(--space-4);
    flex-shrink: 0;
    margin-bottom: var(--space-2);
  }

  .page-title {
    font-size: var(--type-page-title);
    font-weight: var(--weight-semibold);
    color: var(--clr-text-pri);
    letter-spacing: -0.03em;
    line-height: 1.15;
  }

  .page-heading { min-width: 0; }
  .page-eyebrow {
    color: var(--md-primary);
    font-size: var(--type-page-eyebrow);
    font-weight: var(--weight-semibold);
    letter-spacing: 0.08em;
    text-transform: uppercase;
    margin-bottom: var(--space-1);
  }

  /* ── Content (non-Today pages) ── */
  .content {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
    padding-bottom: var(--space-10);
  }

  .stat-row {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: var(--space-3);
  }

  .two-col {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-4);
  }

  /* ── Error banner ── */
  .error-banner {
    background: var(--md-err-cont);
    color: var(--md-error);
    padding: var(--space-3) var(--space-4);
    margin: var(--space-4) var(--space-6) 0;
    border-radius: var(--radius-md);
    font-size: var(--text-sm);
    font-weight: var(--weight-medium);
    border: 1px solid rgba(224, 112, 112, 0.2);
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-5);
  }

  .browser-detail-grid { display: grid; grid-template-columns: 1fr; gap: var(--space-4); }

  .error-banner span { display: flex; align-items: center; gap: var(--space-2); }
  .error-banner button {
    border: 0;
    background: transparent;
    color: inherit;
    font: inherit;
    font-weight: var(--weight-semibold);
    cursor: pointer;
  }

  /* ── Placeholder / empty ── */
  .placeholder-view {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    flex: 1;
    color: var(--clr-text-sec);
  }

  .placeholder-view i {
    font-size: 48px;
    opacity: 0.4;
  }

  .empty-view {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: 48px 0;
    gap: var(--space-2);
    color: var(--clr-text-ter);
  }

  .empty-view i {
    font-size: 36px;
    color: var(--clr-text-ter);
    opacity: 0.5;
  }

  .empty-view span {
    font-size: var(--text-base);
  }

  .empty-hint {
    font-size: var(--text-xs) !important;
    opacity: 0.5;
  }

  @media (max-width: 1050px) {
    .today-hero { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  }

  @media (max-width: 760px) {
    .shell { flex-direction: column; }
    .main {
      padding: 0 12px;
      padding-bottom: 76px;
    }
    .today-header { align-items: flex-start; gap: var(--space-4); }
    .today-date { font-size: var(--text-xl); }
    .today-grid, .two-col { grid-template-columns: 1fr; }
    .stat-row { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .error-banner { margin-inline: 0; }
  }

  @media (max-width: 520px) {
    .today-header { flex-direction: column; }
    .today-hero, .stat-row { grid-template-columns: 1fr; }
    .main { padding-inline: 10px; }
    .card { padding-inline: var(--space-4); }
  }
</style>
