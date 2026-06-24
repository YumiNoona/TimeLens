<script lang="ts">
  import { getDashboardData } from '../api';
  import { onMount } from 'svelte';
  import type { DashboardData, BrowserEntry, InputEntry } from '../types';

  let { data: initial }: { data: DashboardData } = $props();

  const today = new Date();
  let selectedDate = $state(today.toLocaleDateString('en-CA'));
  let historyData = $state<DashboardData>(initial);
  let browserSites = $state<BrowserEntry[]>([]);
  let inputActivity = $state<InputEntry[]>([]);
  let loadingHistory = $state(false);
  let historyError = $state<string | null>(null);
  let historyEmpty = $state(false);

  onMount(() => {
    selectDate(selectedDate);
  });

  let weekDays = $derived.by(() => {
    const heatmapLookup = new Map(initial.heatmap.map(h => [h.date, h.value]));
    const maxVal = Math.max(...initial.heatmap.map(h => h.value), 1);
    const days = [];
    for (let i = 6; i >= 0; i--) {
      const d = new Date(today);
      d.setDate(d.getDate() - i);
      const dateStr = d.toLocaleDateString('en-CA');
      const val = heatmapLookup.get(dateStr) ?? 0;
      days.push({
        date: dateStr,
        label: d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' }),
        isToday: i === 0,
        level: val > 0 ? Math.ceil(val / maxVal * 3) : 0,
      });
    }
    return days;
  });

  async function selectDate(date: string) {
    selectedDate = date;
    loadingHistory = true;
    historyError = null;
    historyEmpty = false;
    try {
      const d = await getDashboardData(date);
      historyData = d;
      if (d.summary.activeSeconds === 0 && d.topApps.length === 0)
        historyEmpty = true;
    } catch {
      historyError = 'Could not load data for this date.';
    }
    // Fetch browser and input data for this date
    try {
      const br = await fetch(`http://127.0.0.1:47821/api/browser-summary?date=${date}`);
      browserSites = await br.json();
    } catch { browserSites = []; }
    try {
      const ir = await fetch(`http://127.0.0.1:47821/api/input-summary?date=${date}`);
      inputActivity = await ir.json();
    } catch { inputActivity = []; }
    loadingHistory = false;
  }

  let displayDate = $derived(new Date(selectedDate + 'T00:00:00').toLocaleDateString('en-US', {
    weekday: 'long', month: 'long', day: 'numeric', year: 'numeric',
  }));
</script>

<div class="history">
  <div class="topbar">
    <h1 class="headline-small">History</h1>
  </div>

  <div class="week-nav" role="tablist">
    {#each weekDays as day}
      <button
        class="day-chip"
        class:active={selectedDate === day.date}
        class:today={day.isToday && selectedDate !== day.date}
        onclick={() => selectDate(day.date)}
        role="tab"
        aria-selected={selectedDate === day.date}
      >
        {day.label}
        <span class="day-dot" class:l1={day.level >= 1} class:l2={day.level >= 2} class:l3={day.level >= 3} class:hidden={day.level === 0}></span>
      </button>
    {/each}
  </div>

  {#if historyError}
    <div class="error-banner">{historyError}</div>
  {/if}

  {#if loadingHistory}
    <p class="title-small" style="color:var(--md-on-surf-var)">Loading…</p>
  {:else}
    <p class="title-small" style="color:var(--md-on-surf-var);margin-bottom:calc(-1 * var(--sp-2))">{displayDate}</p>

    {#if historyEmpty}
      <div class="empty-notice">No activity recorded for this day.</div>
    {:else}
      <div class="summary-row">
        <div class="stat-box">
          <span class="stat-val">{historyData.summary.activeTime}</span>
          <span class="stat-lbl">Active</span>
        </div>
        <div class="stat-box">
          <span class="stat-val">{historyData.summary.idleTime}</span>
          <span class="stat-lbl">Idle</span>
        </div>
        <div class="stat-box">
          <span class="stat-val">{historyData.summary.focusScore}%</span>
          <span class="stat-lbl">Focus</span>
        </div>
        {#if historyData.summary.vsYesterday !== null}
          <div class="stat-box">
            <span class="stat-val" class:up={historyData.summary.vsYesterday > 0}>{historyData.summary.vsYesterday > 0 ? '+' : ''}{historyData.summary.vsYesterday}m</span>
            <span class="stat-lbl">vs yesterday</span>
          </div>
        {/if}
      </div>

      <div class="section">
        <h2 class="title-large">Top Apps</h2>
        <div class="app-list">
          {#each historyData.topApps as app}
            <div class="app-row">
              <div class="app-icon-box">
                <i class="ti ti-apps" aria-hidden="true"></i>
              </div>
              <span class="app-name">{app.name}</span>
              <span class="app-time">{Math.floor(app.minutes / 60)}h {app.minutes % 60}m</span>
            </div>
          {:else}
            <p class="empty">No apps tracked this day.</p>
          {/each}
        </div>
      </div>

      <div class="section">
        <h2 class="title-large">Categories</h2>
        <div class="cat-list">
          {#each historyData.categories as cat}
            <div class="cat-row">
              <span class="cat-name">{cat.name}</span>
              <div class="cat-bar-bg">
                <div class="cat-bar" style="width: {cat.percentage}%"></div>
              </div>
              <span class="cat-pct">{cat.percentage}%</span>
              <span class="cat-time">{Math.floor(cat.minutes / 60)}h {cat.minutes % 60}m</span>
            </div>
          {:else}
            <p class="empty">No categories this day.</p>
          {/each}
        </div>
      </div>
    {/if}

      {#if browserSites.length > 0}
        <div class="section">
          <h2 class="title-large">Top sites</h2>
          <div class="app-list">
            {#each browserSites as site}
              <div class="app-row">
                <div class="app-icon-box">
                  <i class="ti ti-world" aria-hidden="true"></i>
                </div>
                <span class="app-name">{site.domain}</span>
                <span class="app-time">{site.visits} visit{site.visits > 1 ? 's' : ''}</span>
              </div>
            {/each}
          </div>
        </div>
      {/if}

      {#if inputActivity.length > 0}
        <div class="section">
          <h2 class="title-large">Input activity</h2>
          <div class="app-list">
            {#each inputActivity as row}
              <div class="app-row">
                <div class="app-icon-box">
                  <i class="ti ti-keyboard" aria-hidden="true"></i>
                </div>
                <span class="app-name">{row.exeName}</span>
                <span class="app-time">{row.keystrokes.toLocaleString()} keys · {row.clicks.toLocaleString()} clicks</span>
              </div>
            {/each}
          </div>
        </div>
      {/if}
    {/if}
</div>

<style>
  .history { display: flex; flex-direction: column; gap: var(--sp-4); }
  .topbar { display: flex; align-items: center; justify-content: space-between; }
  .week-nav { display: flex; gap: var(--sp-2); overflow-x: auto; padding-bottom: var(--sp-1); }
  .day-chip {
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: var(--sp-2) var(--sp-3) var(--sp-1);
    border-radius: 999px;
    border: 1px solid var(--md-outline);
    background: var(--md-surface-2);
    color: var(--md-on-surf-var);
    font-family: inherit;
    font-size: 12px;
    font-weight: 500;
    cursor: pointer;
    white-space: nowrap;
    transition: all 0.15s;
  }
  .day-chip:hover { background: var(--md-surface-3); color: var(--md-on-surf); }
  .day-chip.today { border-color: var(--md-primary); color: var(--md-primary); }
  .day-chip.active {
    background: var(--md-primary);
    color: #1a1a1a;
    border-color: var(--md-primary);
    font-weight: 600;
    box-shadow: 0 0 0 2px rgba(200, 232, 106, 0.3);
  }
  .day-dot {
    display: block;
    width: 6px; height: 6px;
    border-radius: 50%;
    background: var(--md-outline);
    margin-top: 4px;
    transition: background 0.15s;
  }
  .day-dot.l1 { background: var(--md-surface-3); }
  .day-dot.l2 { background: var(--md-secondary); }
  .day-dot.l3 { background: var(--md-primary); }
  .day-dot.hidden { visibility: hidden; }
  .summary-row { display: flex; gap: var(--sp-3); }
  .stat-box {
    flex: 1;
    background: var(--md-surface-1);
    border-radius: var(--shape-md);
    padding: var(--sp-3);
    display: flex;
    flex-direction: column;
    gap: var(--sp-1);
  }
  .stat-val { font-family: var(--font-mono); font-size: 20px; font-weight: 600; color: var(--md-on-surf); }
  .stat-val.up { color: var(--md-primary); }
  .stat-lbl { font-size: 12px; color: var(--md-on-surf-var); }
  .section { display: flex; flex-direction: column; gap: var(--sp-2); }
  .app-list { display: flex; flex-direction: column; gap: var(--sp-1); }
  .app-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-2) var(--sp-2);
    border-radius: var(--shape-sm);
    background: var(--md-surface-1);
  }
  .app-icon-box {
    width: 28px; height: 28px;
    background: var(--md-surface-2);
    border-radius: var(--shape-sm);
    display: flex; align-items: center; justify-content: center;
  }
  .app-icon-box i { font-size: 16px; color: var(--md-primary); }
  .app-name { flex: 1; font-size: 13px; font-weight: 500; color: var(--md-on-surf); }
  .app-time { font-family: var(--font-mono); font-size: 12px; color: var(--md-on-surf-var); }
  .cat-list { display: flex; flex-direction: column; gap: var(--sp-2); }
  .cat-row { display: flex; align-items: center; gap: var(--sp-2); font-size: 13px; }
  .cat-name { width: 100px; font-weight: 500; color: var(--md-on-surf); }
  .cat-bar-bg { flex: 1; height: 8px; background: var(--md-surface-2); border-radius: 99px; overflow: hidden; }
  .cat-bar { height: 100%; background: var(--md-primary); border-radius: 99px; transition: width 0.3s; }
  .cat-pct { width: 36px; text-align: right; font-family: var(--font-mono); color: var(--md-on-surf-var); font-size: 12px; }
  .cat-time { width: 60px; text-align: right; font-family: var(--font-mono); color: var(--md-on-surf-var); font-size: 12px; }
  .empty { font-size: 13px; color: var(--md-on-surf-dim); padding: var(--sp-2) 0; }
  .empty-notice {
    background: var(--md-surface-2);
    color: var(--md-on-surf-dim);
    padding: var(--sp-6);
    border-radius: var(--shape-md);
    text-align: center;
    font-size: 13px;
  }
  .error-banner {
    background: var(--md-err-cont);
    color: var(--md-error);
    padding: var(--sp-3) var(--sp-4);
    border-radius: var(--shape-sm);
    font-size: 13px;
    border: 1px solid rgba(224, 112, 112, 0.2);
  }
</style>
