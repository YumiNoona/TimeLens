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

  let browserSites = $state<BrowserEntry[]>([]);
  let browserTime = $state<{domain: string; totalMinutes: number}[]>([]);
  let audioSessions = $state<AudioEntry[]>([]);
  let timelineGrouped = $state(false);

  let view = $state('today');
  let currentTheme = $state('moss');

  const today = new Date();
  const dateStr = today.toLocaleDateString('en-US', {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
  });

  function applyTheme(t: string) {
    currentTheme = t;
    document.documentElement.className = '';
    if (t !== 'default') {
      document.documentElement.classList.add('theme-' + t);
    }
  }

  let pollTimer: ReturnType<typeof setInterval> | null = null;

  function startPoll() {
    if (pollTimer) return;
    pollTimer = setInterval(async () => {
      await refresh();
      try {
        const br = await fetch('http://127.0.0.1:47821/api/browser-summary');
        browserSites = await br.json();
      } catch { }
      try {
        const bt = await fetch('http://127.0.0.1:47821/api/browser-time-summary');
        browserTime = await bt.json();
      } catch { }
      try {
        const ar = await fetch('http://127.0.0.1:47821/api/audio-summary');
        audioSessions = await ar.json();
      } catch { }
    }, 30_000);
  }

  function stopPoll() {
    if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
  }

  onMount(async () => {
    refresh();
    try {
      const r = await fetch('http://127.0.0.1:47821/api/settings');
      const s = await r.json();
      if (s.theme) applyTheme(s.theme);
      if (s.timelineGrouped !== undefined) timelineGrouped = s.timelineGrouped;
    } catch { /* tray app not running, stay with default */ }
    // Fetch browser and audio data
    try {
      const br = await fetch('http://127.0.0.1:47821/api/browser-summary');
      browserSites = await br.json();
    } catch { browserSites = []; }
    try {
      const ar = await fetch('http://127.0.0.1:47821/api/audio-summary');
      audioSessions = await ar.json();
    } catch { audioSessions = []; }

    document.addEventListener('visibilitychange', onVisibility);
    if (!document.hidden) startPoll();

    return () => {
      stopPoll();
      document.removeEventListener('visibilitychange', onVisibility);
    };
  });

  function onVisibility() {
    document.hidden ? stopPoll() : startPoll();
  }
</script>

<div class="tl">
  <NavRail active={view} onselect={(id) => view = id} />

  <main class="main">
    {#if $error}
      <div class="error-banner">{$error}</div>
    {/if}

    {#if view === 'today'}
      <div class="topbar">
        <div class="topbar-left">
          {#if $loading}
            <p class="title-small">Loading…</p>
            <h1 class="headline-small">Today's activity</h1>
          {:else if $data}
            <p class="title-small">{dateStr}</p>
            <h1 class="headline-small">Today's activity</h1>
          {/if}
        </div>
        {#if $live}
          <LiveChip status={$live} />
        {/if}
      </div>

      {#if $data}
        <div class="stat-grid" role="region" aria-label="Summary statistics">
          <StatCard
            label="Active time"
            value={$data.summary.activeTime}
            chip={$data.summary.vsYesterday !== null ? `↑ ${$data.summary.vsYesterday}m vs yesterday` : ''}
            chipClass="chip-up"
          />
          <StatCard
            label="Idle time"
            value={$data.summary.idleTime}
            chip={$data.summary.idleSeconds > 0
              ? Math.round($data.summary.idleSeconds / ($data.summary.idleSeconds + $data.summary.activeSeconds) * 100) + '% of session'
              : ''}
            chipClass="chip-down"
            amber
          />
          <StatCard
            label="Focus score"
            value={String($data.summary.focusScore)}
            chip={$data.summary.focusScore >= 70 ? '↑ productive' : $data.summary.focusScore >= 40 ? '~ mixed' : '↓ distracting'}
            chipClass={$data.summary.focusScore >= 70 ? 'chip-up' : $data.summary.focusScore >= 40 ? 'chip-neu' : 'chip-down'}
            accent={$data.summary.focusScore >= 40}
          />
          <StatCard
            label="Top category"
            value={$data.summary.topCategory}
            chip={$data.summary.topCategoryTime + ' total'}
            chipClass="chip-neu"
          />
          <StatCard
            label="Keystrokes"
            value={$data.summary.totalKeystrokes.toLocaleString()}
            chip="today"
            chipClass="chip-neu"
          />
          <StatCard
            label="Clicks"
            value={$data.summary.totalClicks.toLocaleString()}
            chip="today"
            chipClass="chip-neu"
          />
        </div>

        <Timeline blocks={$data.timeline} />

        <div class="bottom-grid">
          <TopApps apps={$data.topApps} />
          <CategoryBreakdown categories={$data.categories} />
          <CalendarHeatmap entries={$data.heatmap} />
        </div>

        <div class="bottom-grid">
          <TopSites sites={browserSites} />
          {#if audioSessions.length > 0}
            <div class="card audio-card">
              <div class="card-title">
                <i class="ti ti-volume-2" aria-hidden="true"></i>
                Media
              </div>
              <div role="list" class="audio-list">
                {#each audioSessions as a}
                  <div class="audio-row" role="listitem">
                    <span class="audio-exe">{a.exeName}</span>
                    <span class="audio-count">active {a.sessions} time{a.sessions > 1 ? 's' : ''}</span>
                  </div>
                {/each}
              </div>
            </div>
          {:else}
            <div></div>
          {/if}
        </div>
      {/if}

    {:else if view === 'history' && $data}
      <HistoryView data={$data} />
    {:else if view === 'browser'}
      <div class="browser-view">
        <div class="topbar">
          <h1 class="headline-small">Browser</h1>
        </div>
        <div class="bottom-grid">
          <TopSites sites={browserSites} />
          {#if browserTime.length > 0}
            <div class="card audio-card">
              <div class="card-title">
                <i class="ti ti-clock" aria-hidden="true"></i>
                Time on sites
              </div>
              <div role="list" class="audio-list">
                {#each browserTime as bt}
                  <div class="audio-row" role="listitem">
                    <span class="audio-exe">{bt.domain}</span>
                    <span class="audio-count">{bt.totalMinutes}m</span>
                  </div>
                {/each}
              </div>
            </div>
          {:else}
            <div></div>
          {/if}
        </div>
      </div>
    {:else if view === 'apps' && $data}
      <AppsView data={$data} />
    {:else if view === 'timeline' && $data}
      <TimelineView data={$data} timelineGrouped={timelineGrouped} />
    {:else if view === 'rules'}
      <RulesView />
    {:else if view === 'block'}
      <BlockView />
    {:else if view === 'settings'}
      <SettingsView ontheme={applyTheme} />
    {:else if !$data}
      <div class="placeholder-view">
        <i class="ti ti-loader" aria-hidden="true"></i>
        <p class="title-small" style="margin-top: var(--sp-2)">Loading…</p>
      </div>
    {/if}
  </main>
</div>

<style>
  .tl {
    background: var(--md-surface);
    color: var(--md-on-surf);
    font-family: var(--font-display);
    display: flex;
    height: 100vh;
    overflow: hidden;
    font-size: 14px;
    line-height: 1.5;
  }

  .main {
    flex: 1;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: var(--sp-4);
    padding: var(--sp-6);
  }

  .topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .title-small {
    font-size: 14px;
    font-weight: 500;
    color: var(--md-on-surf-var);
    letter-spacing: 0.01em;
  }

  .headline-small {
    font-size: 24px;
    font-weight: 600;
    color: var(--md-on-surf);
    letter-spacing: -0.01em;
    line-height: 1.2;
  }

  .stat-grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
    gap: var(--sp-3);
  }

  .bottom-grid {
    display: grid;
    grid-template-columns: 1fr 1fr 1fr;
    gap: var(--sp-4);
  }

  .error-banner {
    background: var(--md-err-cont);
    color: var(--md-error);
    padding: var(--sp-3) var(--sp-4);
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
    color: var(--md-on-surf-dim);
  }

  .placeholder-view i {
    font-size: 48px;
  }

  .audio-card {
    background: var(--md-surface-1);
    border-radius: var(--shape-lg);
    border: 1px solid var(--md-outline);
    padding: var(--sp-5);
  }

  .card-title {
    font-size: 14px;
    font-weight: 500;
    color: var(--md-on-surf);
    margin-bottom: var(--sp-4);
    display: flex;
    align-items: center;
    gap: var(--sp-2);
  }

  .card-title i { color: var(--md-on-surf-var); font-size: 16px; }

  .audio-list {
    display: flex;
    flex-direction: column;
    gap: var(--sp-2);
  }

  .audio-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--sp-2) 0;
    border-bottom: 1px solid var(--md-outline);
    font-size: 12px;
  }

  .audio-row:last-child { border-bottom: none; }

  .audio-exe {
    font-family: var(--font-mono);
    color: var(--md-on-surf);
  }

  .audio-count {
    color: var(--md-on-surf-dim);
    font-size: 11px;
  }
</style>
