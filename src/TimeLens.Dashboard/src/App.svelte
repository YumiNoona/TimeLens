<script lang="ts">
  import { onMount } from 'svelte';
  import NavRail from './lib/components/NavRail.svelte';
  import LiveChip from './lib/components/LiveChip.svelte';
  import StatCard from './lib/components/StatCard.svelte';
  import Timeline from './lib/components/Timeline.svelte';
  import TopApps from './lib/components/TopApps.svelte';
  import CalendarHeatmap from './lib/components/CalendarHeatmap.svelte';
  import CategoryBreakdown from './lib/components/CategoryBreakdown.svelte';
  import HistoryView from './lib/components/HistoryView.svelte';
  import AppsView from './lib/components/AppsView.svelte';
  import TimelineView from './lib/components/TimelineView.svelte';
  import RulesView from './lib/components/RulesView.svelte';
  import SettingsView from './lib/components/SettingsView.svelte';
  import BlockView from './lib/components/BlockView.svelte';
  import TopSites from './lib/components/TopSites.svelte';
  import type { BrowserEntry, AudioEntry } from './lib/types';
  import { data, loading, error, live, refresh } from './lib/stores/activity';
  import { timeFormat as timeFormatStore } from './lib/stores/settings';

  let browserSites = $state<BrowserEntry[]>([]);
  let browserTime = $state<{domain: string; totalMinutes: number}[]>([]);
  let audioSessions = $state<AudioEntry[]>([]);
  let browserHourly = $state<{hour: number; visits: number}[]>([]);
  let timelineGrouped = $state(false);

  let view = $state('today');
  let currentTheme = $state('default');
  let pollInterval = $state(30);

  const today = new Date();
  const dateStr = today.toLocaleDateString('en-US', {
    weekday: 'long', month: 'long', day: 'numeric',
  });

  function applyTheme(t: string) {
    currentTheme = t;
    document.documentElement.className = '';
    if (t !== 'default') document.documentElement.classList.add('theme-' + t);
  }

  let pollTimer: ReturnType<typeof setInterval> | null = null;

  function startPoll() {
    if (pollTimer) return;
    const interval = Math.max(5000, pollInterval) * 1000;
    pollTimer = setInterval(async () => {
      await refresh(true);
      try { const br = await fetch('http://127.0.0.1:47821/api/browser-summary'); browserSites = await br.json(); } catch { }
      try { const bt = await fetch('http://127.0.0.1:47821/api/browser-time-summary'); browserTime = await bt.json(); } catch { }
      try { const ar = await fetch('http://127.0.0.1:47821/api/audio-summary'); audioSessions = await ar.json(); } catch { }
      try { const hr = await fetch('http://127.0.0.1:47821/api/browser-hourly'); browserHourly = await hr.json(); } catch { }
    }, interval);
  }

  function stopPoll() { if (pollTimer) { clearInterval(pollTimer); pollTimer = null; } }

  onMount(async () => {
    refresh();
    try {
      const r = await fetch('http://127.0.0.1:47821/api/settings');
      const s = await r.json();
      if (s.theme) applyTheme(s.theme);
      if (s.timelineGrouped !== undefined) timelineGrouped = s.timelineGrouped;
      if (s.timeFormat) timeFormatStore.set(s.timeFormat === '24h' ? '24h' : '12h');
      if (s.pollIntervalSeconds) pollInterval = s.pollIntervalSeconds;
    } catch { }
    try { const br = await fetch('http://127.0.0.1:47821/api/browser-summary'); browserSites = await br.json(); } catch { browserSites = []; }
    try { const ar = await fetch('http://127.0.0.1:47821/api/audio-summary'); audioSessions = await ar.json(); } catch { audioSessions = []; }
    try { const hr = await fetch('http://127.0.0.1:47821/api/browser-hourly'); browserHourly = await hr.json(); } catch { browserHourly = []; }

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
      <div class="topbar">
        <div class="topbar-left">
          {#if $loading}
            <div class="page-sub">Loading…</div>
            <h1 class="page-title">Today's activity</h1>
          {:else if $data}
            <div class="page-sub">{dateStr}</div>
            <h1 class="page-title">Today's activity</h1>
          {/if}
        </div>
        {#if $live}
          <LiveChip status={$live} />
        {/if}
      </div>

      {#if $data}
        <div class="content">
          <div class="stat-row">
            <StatCard label="Active time" value={$data.summary.activeTime}
              chip={$data.summary.vsYesterday !== null ? `${$data.summary.vsYesterday >= 0 ? '↑' : '↓'} ${Math.abs($data.summary.vsYesterday)}m vs yesterday` : ''}
              chipClass={$data.summary.vsYesterday !== null && $data.summary.vsYesterday >= 0 ? 'chip-up' : 'chip-down'} />
            <StatCard label="Idle time" value={$data.summary.idleTime}
              chip={$data.summary.idleSeconds > 0 ? Math.round($data.summary.idleSeconds / ($data.summary.idleSeconds + $data.summary.activeSeconds) * 100) + '% of session' : ''}
              chipClass="chip-down" amber />
            <StatCard label="Focus score" value={String($data.summary.focusScore)}
              chip={$data.summary.focusScore >= 70 ? '↑ productive' : $data.summary.focusScore >= 40 ? '~ mixed' : '↓ distracting'}
              chipClass={$data.summary.focusScore >= 70 ? 'chip-up' : $data.summary.focusScore >= 40 ? 'chip-neu' : 'chip-down'}
              accent={$data.summary.focusScore >= 40} />
            <StatCard label="Top category" value={$data.summary.topCategory} chip={$data.summary.topCategoryTime + ' total'} chipClass="chip-neu" />
            <StatCard label="Keystrokes" value={$data.summary.totalKeystrokes.toLocaleString()} chip="today" chipClass="chip-neu" />
            <StatCard label="Clicks" value={$data.summary.totalClicks.toLocaleString()} chip="today" chipClass="chip-neu" />
          </div>

          <div class="timeline-track">
            <div class="section-label">Activity timeline</div>
            <Timeline blocks={$data.timeline} />
          </div>

          <div class="three-col">
            <TopApps apps={$data.topApps} />
            <CategoryBreakdown categories={$data.categories} />
            <CalendarHeatmap entries={$data.heatmap} />
          </div>

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
                  <div class="h-bar" style="height:{Math.max(h.visits / Math.max(...browserHourly.map(x => x.visits)) * 60, 3)}px"></div>
                {/each}
              </div>
              <div class="tl-labels">
                <span class="tl-label">8am</span><span class="tl-label">10am</span><span class="tl-label">12pm</span><span class="tl-label">2pm</span><span class="tl-label">4pm</span><span class="tl-label">6pm</span>
              </div>
            </div>
          {/if}

          {#if audioSessions.length > 0}
            <div class="card audio-card">
              <div class="card-title"><i class="ti ti-volume-2" aria-hidden="true"></i>Media</div>
              {#each audioSessions as a}
                <div class="site-row">
                  <span class="site-name">{a.exeName}</span>
                  <span class="site-visits">active {a.sessions} time{a.sessions > 1 ? 's' : ''}</span>
                </div>
              {/each}
            </div>
          {/if}
        </div>
      {/if}

    {:else if view === 'history' && $data}
      <div class="content"><HistoryView data={$data} /></div>
    {:else if view === 'browser'}
      <div class="browser-view">
        <div class="topbar"><h1 class="page-title">Browser</h1></div>
        <div class="content">
          <div class="stat-row">
            <div class="stat-card"><div class="stat-label">Unique sites</div><div class="stat-val">{browserSites.length}</div></div>
            <div class="stat-card"><div class="stat-label">Total visits</div><div class="stat-val">{browserSites.reduce((a, b) => a + b.visits, 0)}</div></div>
            <div class="stat-card"><div class="stat-label">Browse time</div><div class="stat-val">{browserTime.reduce((a, b) => a + b.totalMinutes, 0)}m</div></div>
          </div>
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
                  <div class="h-bar" style="height:{Math.max(h.visits / Math.max(...browserHourly.map(x => x.visits)) * 60, 3)}px"></div>
                {/each}
              </div>
              <div class="tl-labels"><span class="tl-label">8am</span><span class="tl-label">10am</span><span class="tl-label">12pm</span><span class="tl-label">2pm</span><span class="tl-label">4pm</span><span class="tl-label">6pm</span></div>
            </div>
          {/if}
        </div>
      </div>
    {:else if view === 'apps' && $data}
      <div class="content"><AppsView data={$data} browserSites={browserSites} {browserTime} /></div>
    {:else if view === 'timeline' && $data}
      <div class="content"><TimelineView data={$data} timelineGrouped={timelineGrouped} /></div>
    {:else if view === 'rules'}
      <div class="content"><RulesView /></div>
    {:else if view === 'block'}
      <div class="content"><BlockView /></div>
    {:else if view === 'settings'}
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
  }

  .topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 16px 20px 12px;
    border-bottom: 0.5px solid var(--clr-border);
    flex-shrink: 0;
  }

  .page-sub {
    font-size: 11px;
    color: var(--clr-text-ter);
    margin-bottom: 2px;
  }

  .page-title {
    font-size: 18px;
    font-weight: 500;
    color: var(--clr-text-pri);
  }

  .content {
    padding: 16px 20px;
    display: flex;
    flex-direction: column;
    gap: 18px;
  }

  .stat-row {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 14px;
  }

  .section-label {
    font-size: 11px;
    font-weight: 500;
    color: var(--clr-text-sec);
    letter-spacing: 0.04em;
    text-transform: uppercase;
    margin-bottom: 8px;
  }

  .timeline-track {
    background: var(--clr-bg-sec);
    border-radius: var(--shape-md);
    padding: 16px 18px;
  }

  .three-col {
    display: grid;
    grid-template-columns: 1fr 1fr 1fr;
    gap: 14px;
  }

  .two-col {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 14px;
  }

  .card {
    background: var(--clr-bg-sec);
    border-radius: var(--shape-md);
    padding: 16px 18px;
  }

  .card-title {
    font-size: 12px;
    font-weight: 500;
    color: var(--clr-text-pri);
    margin-bottom: 12px;
    display: flex;
    align-items: center;
    gap: 6px;
  }

  .card-title i { font-size: 14px; color: var(--clr-text-sec); }

  .site-row {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 5px 0;
    border-bottom: 0.5px solid var(--clr-border);
  }
  .site-row:last-child { border-bottom: none; }

  .site-icon {
    width: 16px; height: 16px; border-radius: 3px;
    background: var(--clr-bg-ter);
    display: flex; align-items: center; justify-content: center;
    font-size: 9px; color: var(--clr-text-sec); flex-shrink: 0;
  }

  .site-name {
    font-size: 11px; color: var(--clr-text-pri); flex: 1;
  }

  .site-visits {
    font-size: 10px; color: var(--clr-text-sec);
  }

  .hourly {
    display: flex;
    align-items: flex-end;
    gap: 2px;
    height: 60px;
    margin-top: 8px;
  }

  .h-bar {
    flex: 1;
    border-radius: 2px 2px 0 0;
    background: var(--md-primary);
    opacity: 0.7;
    min-height: 3px;
  }

  .tl-labels {
    display: flex;
    justify-content: space-between;
    margin-top: 4px;
  }

  .tl-label {
    font-size: 9px;
    color: var(--clr-text-ter);
  }

  .error-banner {
    background: var(--md-err-cont);
    color: var(--md-error);
    padding: var(--sp-3) var(--sp-4);
    margin: 16px 20px 0;
    border-radius: var(--shape-sm);
    font-size: 13px;
    border: 1px solid rgba(224, 112, 112, 0.2);
  }

  .placeholder-view {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    flex: 1;
    color: var(--clr-text-sec);
  }
  .placeholder-view i { font-size: 48px; }

  .audio-card { background: var(--clr-bg-sec); border-radius: var(--shape-md); padding: 16px 18px; }
  .browser-view { display: flex; flex-direction: column; flex: 1; }

  .stat-card {
    background: var(--clr-bg-sec);
    border-radius: var(--shape-md);
    padding: 16px 18px;
  }
  .stat-val {
    font-size: 24px; font-weight: 500; color: var(--clr-text-pri);
    line-height: 1; font-family: var(--font-mono);
  }
  .stat-label {
    font-size: 11px; font-weight: 500; color: var(--clr-text-sec);
    margin-bottom: 4px;
  }
</style>
