<script lang="ts">
  import { getDashboardData } from '../api';
  import type { DashboardData } from '../types';

  let { data: initial }: { data: DashboardData } = $props();

  const today = new Date();
  let selectedDate = $state(today.toISOString().slice(0, 10));
  let historyData = $state<DashboardData>(initial);
  let loadingHistory = $state(false);
  let historyError = $state<string | null>(null);

  let weekDays = $derived.by(() => {
    const days = [];
    for (let i = 6; i >= 0; i--) {
      const d = new Date(today);
      d.setDate(d.getDate() - i);
      days.push({
        date: d.toISOString().slice(0, 10),
        label: d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' }),
        isToday: i === 0,
      });
    }
    return days;
  });

  async function selectDate(date: string) {
    selectedDate = date;
    loadingHistory = true;
    historyError = null;
    try {
      const d = await getDashboardData(date);
      historyData = d;
    } catch {
      historyError = 'Could not load data for this date.';
    }
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
      <div class="stat-box">
        <span class="stat-val" class:up={historyData.summary.vsYesterday > 0}>{historyData.summary.vsYesterday > 0 ? '+' : ''}{historyData.summary.vsYesterday}m</span>
        <span class="stat-lbl">vs yesterday</span>
      </div>
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
</div>

<style>
  .history { display: flex; flex-direction: column; gap: var(--sp-4); }
  .topbar { display: flex; align-items: center; justify-content: space-between; }
  .week-nav { display: flex; gap: var(--sp-2); overflow-x: auto; padding-bottom: var(--sp-1); }
  .day-chip {
    padding: var(--sp-2) var(--sp-3);
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
    color: var(--md-on-pri-cont);
    border-color: var(--md-primary);
    font-weight: 600;
    box-shadow: 0 0 0 2px rgba(200, 232, 106, 0.3);
  }
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
  .error-banner {
    background: var(--md-err-cont);
    color: var(--md-error);
    padding: var(--sp-3) var(--sp-4);
    border-radius: var(--shape-sm);
    font-size: 13px;
    border: 1px solid rgba(224, 112, 112, 0.2);
  }
</style>
