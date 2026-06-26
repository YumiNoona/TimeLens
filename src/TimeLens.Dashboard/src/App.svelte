<script lang="ts">
  import { onMount } from 'svelte';
  import NavRail from './lib/components/NavRail.svelte';
  import LiveChip from './lib/components/LiveChip.svelte';
  import StatCard from './lib/components/StatCard.svelte';
  import Timeline from './lib/components/Timeline.svelte';
  import TopApps from './lib/components/TopApps.svelte';
  import CategoryBreakdown from './lib/components/CategoryBreakdown.svelte';

  import AppsView from './lib/components/AppsView.svelte';
  import TimelineView from './lib/components/TimelineView.svelte';
  import RulesView from './lib/components/RulesView.svelte';
  import SettingsView from './lib/components/SettingsView.svelte';
  import BlockView from './lib/components/BlockView.svelte';
  import TopSites from './lib/components/TopSites.svelte';
  import CalendarHeatmap from './lib/components/CalendarHeatmap.svelte';
  import type { BrowserEntry, AudioEntry } from './lib/types';
  import { data, loading, error, live, refresh } from './lib/stores/activity';
  import { timeFormat as timeFormatStore } from './lib/stores/settings';

  let browserSites = $state<BrowserEntry[]>([]);
  let browserTime = $state<{domain: string; totalMinutes: number}[]>([]);
  let audioSessions = $state<AudioEntry[]>([]);
  let browserHourlyRaw = $state<{hour: number; visits: number}[]>([]);
  let browserHourly = $derived.by(() => {
    const map = new Map(browserHourlyRaw.map(h => [h.hour, h.visits]));
    return Array.from({ length: 24 }, (_, i) => ({ hour: i, visits: map.get(i) ?? 0 }));
  });
  let timelineGrouped = $state(true);

  let view = $state('today');
  let currentTheme = $state('default');
  let pollInterval = $state(30);

  const today = new Date();
  const dateStr = today.toLocaleDateString('en-US', {
    weekday: 'long', month: 'long', day: 'numeric',
  });

  const greeting = $derived.by(() => {
    const h = today.getHours();
    if (h < 5) return 'Good night';
    if (h < 12) return 'Good morning';
    if (h < 17) return 'Good afternoon';
    return 'Good evening';
  });

  function applyTheme(t: string) {
    currentTheme = t;
    document.documentElement.className = '';
    if (t !== 'default') document.documentElement.classList.add('theme-' + t);
  }

  let pollTimer: ReturnType<typeof setInterval> | null = null;

  function startPoll() {
    if (pollTimer) return;
    const interval = Math.max(5, pollInterval) * 1000;
    pollTimer = setInterval(async () => {
      await refresh(true);
      try { const br = await fetch('http://127.0.0.1:47821/api/browser-summary'); browserSites = await br.json(); } catch { }
      try { const bt = await fetch('http://127.0.0.1:47821/api/browser-time-summary'); browserTime = await bt.json(); } catch { }
      try { const ar = await fetch('http://127.0.0.1:47821/api/audio-summary'); audioSessions = await ar.json(); } catch { }
      try { const hr = await fetch('http://127.0.0.1:47821/api/browser-hourly'); browserHourlyRaw = await hr.json(); } catch { }
    }, interval);
  }

  function stopPoll() { if (pollTimer) { clearInterval(pollTimer); pollTimer = null; } }

  onMount(async () => {
    refresh();
    try {
      const r = await fetch('http://127.0.0.1:47821/api/settings');
      const s = await r.json();
      if (s.theme) applyTheme(s.theme);
      timelineGrouped = s.timelineGrouped ?? true;
      if (s.timeFormat) timeFormatStore.set(s.timeFormat === '24h' ? '24h' : '12h');
      if (s.pollIntervalSeconds) pollInterval = s.pollIntervalSeconds;
    } catch { }
    try { const br = await fetch('http://127.0.0.1:47821/api/browser-summary'); browserSites = await br.json(); } catch { browserSites = []; }
    try { const ar = await fetch('http://127.0.0.1:47821/api/audio-summary'); audioSessions = await ar.json(); } catch { audioSessions = []; }
    try { const hr = await fetch('http://127.0.0.1:47821/api/browser-hourly'); browserHourlyRaw = await hr.json(); } catch { browserHourlyRaw = []; }
    try { const bt = await fetch('http://127.0.0.1:47821/api/browser-time-summary'); browserTime = await bt.json(); } catch { browserTime = []; }

    document.addEventListener('visibilitychange', onVisibility);
    if (!document.hidden) startPoll();
    return () => { stopPoll(); document.removeEventListener('visibilitychange', onVisibility); };
  });

  function onVisibility() { document.hidden ? stopPoll() : startPoll(); }
</script>

<div class="shell">
  <NavRail active={view} onselect={(id) => view = id} />

  <main class="main">
    {#if $error}
      <div class="error-banner">{$error}</div>
    {/if}

    {#if view === 'today'}
      {#if $loading}
        <div class="today-loading">
          <div class="today-loading-pulse"></div>
          <p>Loading your activity…</p>
        </div>
      {:else if $data}
        <div class="today-header">
          <div class="today-header-left">
            <p class="today-greeting">{greeting}</p>
            <h1 class="today-date">{dateStr}</h1>
          </div>
          <div class="today-header-right">
            {#if $live}
              <LiveChip status={$live} />
            {/if}
          </div>
        </div>

        <div class="today-content">
          <section class="today-hero">
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

          <section class="today-timeline card">
            <div class="card-header">
              <i class="ti ti-timeline-event" aria-hidden="true"></i>
              <div class="card-title">Activity timeline</div>
            </div>
            <Timeline blocks={$data.timeline} />
          </section>

          <div class="today-grid">
            <TopApps apps={$data.topApps} />
            <CategoryBreakdown categories={$data.categories} />
          </div>

          {#if browserSites.length > 0 || browserTime.length > 0}
            <div class="today-grid">
              <TopSites sites={browserSites} />
              {#if browserTime.length > 0}
                <div class="card">
                  <div class="card-header">
                    <i class="ti ti-clock" aria-hidden="true"></i>
                    <div class="card-title">Time on sites</div>
                  </div>
                  <div class="browser-time-list">
                    {#each browserTime as bt}
                      <div class="bt-row">
                        <span class="bt-domain">{bt.domain}</span>
                        <span class="bt-time">{bt.totalMinutes}m</span>
                      </div>
                    {/each}
                  </div>
                </div>
              {/if}
            </div>
          {/if}

          {#if browserHourly.length > 0}
            <div class="card">
              <div class="card-header">
                <i class="ti ti-chart-bar" aria-hidden="true"></i>
                <div class="card-title">Browser visits by hour</div>
              </div>
              <div class="browser-hourly-chart">
                {#each browserHourly as h}
                  <div
                    class="bh-bar"
                    class:zero={h.visits === 0}
                    style="height:{h.visits > 0 ? Math.max(3, h.visits / Math.max(...browserHourly.map(x => x.visits), 1) * 64) : 2}px"
                    title="{h.hour}:00 — {h.visits} visits"
                  ></div>
                {/each}
              </div>
              <div class="bh-labels">
                {#each [0, 6, 12, 18] as hr}
                  <span>{$timeFormatStore === '24h'
                    ? String(hr).padStart(2, '0') + ':00'
                    : (hr === 12 ? '12p' : hr === 0 ? '12a' : hr > 12 ? (hr - 12) + 'p' : hr + 'a')}</span>
                {/each}
              </div>
            </div>
          {/if}

          {#if audioSessions.length > 0}
            <div class="card">
              <div class="card-header">
                <i class="ti ti-volume-2" aria-hidden="true"></i>
                <div class="card-title">Media sessions</div>
              </div>
              <div class="media-list">
                {#each audioSessions as a}
                  <div class="media-row">
                    <span class="media-name">{a.exeName}</span>
                    <span class="media-count">{a.sessions} session{a.sessions !== 1 ? 's' : ''}</span>
                  </div>
                {/each}
              </div>
            </div>
          {/if}
        </div>
      {/if}

    {:else if view === 'browser'}
      <div class="topbar">
        <div class="topbar-left">
          <h1 class="page-title">Browser</h1>
        </div>
      </div>
      <div class="content">
        <div class="stat-row">
          <StatCard label="Unique sites" value={browserSites.length} />
          <StatCard label="Total visits" value={browserSites.reduce((a, b) => a + b.visits, 0)} />
          <StatCard label="Browse time" value={`${browserTime.reduce((a, b) => a + b.totalMinutes, 0)}m`} />
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
            {#if browserTime.length > 0}
              <div class="card">
                <div class="card-title"><i class="ti ti-clock" aria-hidden="true"></i>Time on sites</div>
                {#each browserTime as bt}
                  <div class="site-row">
                    <div class="site-icon">{bt.domain.charAt(0).toUpperCase()}</div>
                    <span class="site-name">{bt.domain}</span>
                    <span class="site-visits">{bt.totalMinutes}m</span>
                  </div>
                {/each}
              </div>
            {/if}
          </div>
          {#if browserHourly.length > 0}
            <div class="card">
              <div class="card-title"><i class="ti ti-clock-hour-3" aria-hidden="true"></i>Visits by hour</div>
              <div class="hourly">
                {#each browserHourly as h}
                  <div class="h-bar" class:zero={h.visits === 0} style="height:{h.visits > 0 ? h.visits / Math.max(...browserHourly.map(x => x.visits), 1) * 60 : 3}px; min-width: 4px;" title="{h.hour}:00 - {h.visits} visits"></div>
                {/each}
              </div>
              <div class="tl-labels">
                {#each [8, 10, 12, 14, 16, 18, 20, 22] as hr}
                <span class="tl-label">{$timeFormatStore === '24h' ? String(hr).padStart(2, '0') + ':00' : (hr > 12 ? hr - 12 : hr) + (hr >= 12 ? 'p' : 'a')}</span>
                {/each}
              </div>
            </div>
          {/if}
        {/if}
      </div>
    {:else if view === 'apps' && $data}
      <div class="topbar">
        <div class="topbar-left">
          <h1 class="page-title">Apps</h1>
        </div>
      </div>
      <div class="content"><AppsView data={$data} browserSites={browserSites} {browserTime} /></div>
    {:else if view === 'timeline' && $data}
      <div class="topbar">
        <div class="topbar-left">
          <h1 class="page-title">Timeline</h1>
        </div>
      </div>
      <div class="content"><TimelineView data={$data} timelineGrouped={timelineGrouped} /></div>
    {:else if view === 'rules'}
      <div class="topbar">
        <div class="topbar-left">
          <h1 class="page-title">Rules</h1>
        </div>
      </div>
      <div class="content"><RulesView /></div>
    {:else if view === 'block'}
      <div class="topbar">
        <div class="topbar-left">
          <h1 class="page-title">Block</h1>
        </div>
      </div>
      <div class="content"><BlockView /></div>
    {:else if view === 'settings'}
      <div class="topbar">
        <div class="topbar-left">
          <h1 class="page-title">Settings</h1>
        </div>
      </div>
      <div class="content"><SettingsView ontheme={applyTheme} /></div>
    {:else if !$data}
      <div class="placeholder-view">
        <i class="ti ti-loader" aria-hidden="true"></i>
        <p class="title-small" style="margin-top: var(--sp-2)">Loading…</p>
      </div>
    {/if}
  </main>
</div>

<style>
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
    padding: 0 var(--space-8);
  }

  /* ── Today: Loading ── */
  .today-loading {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    flex: 1;
    gap: var(--space-4);
    color: var(--clr-text-sec);
  }

  .today-loading-pulse {
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

  .today-loading p {
    font-size: var(--text-sm);
    font-weight: var(--weight-medium);
    color: var(--clr-text-ter);
  }

  /* ── Today: Header ── */
  .today-header {
    display: flex;
    align-items: flex-end;
    justify-content: space-between;
    padding: var(--space-4) 0 var(--space-6);
    flex-shrink: 0;
  }

  .today-greeting {
    font-size: var(--text-base);
    font-weight: var(--weight-normal);
    color: var(--clr-text-sec);
    margin-bottom: var(--space-1);
  }

  .today-date {
    font-size: var(--text-2xl);
    font-weight: var(--weight-semibold);
    color: var(--clr-text-pri);
    letter-spacing: -0.03em;
    line-height: 1.1;
  }

  .today-header-right {
    display: flex;
    align-items: center;
    gap: var(--space-3);
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

  /* ── Timeline section ── */
  .today-timeline {
    padding: var(--space-5) var(--space-6);
  }

  /* ── Two-column grid ── */
  .today-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-4);
  }

  /* ── Browser time on sites ── */
  .browser-time-list {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .bt-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--space-2) 0;
    border-bottom: 1px solid var(--clr-border);
  }

  .bt-row:last-child { border-bottom: none; }

  .bt-domain {
    font-size: var(--text-sm);
    font-family: var(--font-mono);
    color: var(--clr-text-pri);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    flex: 1;
    margin-right: var(--space-3);
  }

  .bt-time {
    font-size: var(--text-xs);
    font-family: var(--font-mono);
    color: var(--clr-text-sec);
    font-feature-settings: 'tnum';
    font-weight: var(--weight-medium);
    flex-shrink: 0;
  }

  /* ── Browser hourly chart ── */
  .browser-hourly-chart {
    display: flex;
    align-items: flex-end;
    gap: 2px;
    height: 64px;
    margin-bottom: var(--space-2);
  }

  .bh-bar {
    flex: 1;
    border-radius: 2px 2px 0 0;
    background: var(--md-primary);
    opacity: 0.45;
    min-height: 2px;
    cursor: pointer;
    transition: opacity var(--duration-fast) var(--ease-out);
  }

  .bh-bar:hover { opacity: 0.7; }
  .bh-bar.zero { opacity: 0.06; }

  .bh-labels {
    display: flex;
    justify-content: space-between;
    font-size: var(--text-2xs);
    color: var(--clr-text-ter);
    font-family: var(--font-mono);
  }

  /* ── Media sessions ── */
  .media-list {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .media-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--space-2) 0;
    border-bottom: 1px solid var(--clr-border);
  }

  .media-row:last-child { border-bottom: none; }

  .media-name {
    font-size: var(--text-sm);
    color: var(--clr-text-pri);
    font-weight: var(--weight-medium);
  }

  .media-count {
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
    padding: var(--space-4) 0 var(--space-3);
    border-bottom: 1px solid var(--clr-border);
    flex-shrink: 0;
    margin-bottom: var(--space-3);
  }

  .page-title {
    font-size: var(--text-lg);
    font-weight: var(--weight-semibold);
    color: var(--clr-text-pri);
    letter-spacing: -0.02em;
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
</style>
