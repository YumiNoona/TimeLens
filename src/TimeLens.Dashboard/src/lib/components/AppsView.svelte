<script lang="ts">
  import { onMount } from 'svelte';
  import type { DashboardData, InputEntry } from '../types';
  let { data }: { data: DashboardData } = $props();

  type SortKey = 'name' | 'time';
  let sortKey = $state<SortKey>('time');
  let search = $state('');
  let inputData = $state<InputEntry[]>([]);

  onMount(async () => {
    try {
      const r = await fetch('http://127.0.0.1:47821/api/input-summary');
      inputData = await r.json();
    } catch { inputData = []; }
  });

  let allApps = $derived(
    data.topApps
      .filter(a => a.name.toLowerCase().includes(search.toLowerCase()))
      .toSorted((a, b) => sortKey === 'time' ? b.minutes - a.minutes : a.name.localeCompare(b.name))
  );
</script>

<div class="apps">
  <div class="topbar">
    <h1 class="headline-small">Apps</h1>
    <input class="search" type="search" placeholder="Search apps…" bind:value={search} />
  </div>

  <div class="toolbar">
    <button class="sort-btn" class:active={sortKey === 'time'} onclick={() => sortKey = 'time'}>
      <i class="ti ti-clock" aria-hidden="true"></i> Time
    </button>
    <button class="sort-btn" class:active={sortKey === 'name'} onclick={() => sortKey = 'name'}>
      <i class="ti ti-sort-alpha" aria-hidden="true"></i> Name
    </button>
    <span class="count">{allApps.length} apps</span>
  </div>

  <div class="table" role="table">
    <div class="th" role="row">
      <span role="columnheader">App</span>
      <span role="columnheader">Time</span>
    </div>
    {#each allApps as app, i}
      <div class="tr" role="row" class:alt={i % 2 === 0}>
        <span class="td-name" role="cell">
          <i class="ti ti-apps" aria-hidden="true"></i>
          {app.name}
        </span>
        <span class="td-time" role="cell">
          {Math.floor(app.minutes / 60)}h {app.minutes % 60}m
        </span>
      </div>
    {/each}
  </div>

  {#if inputData.length > 0}
    <div class="section">
      <h2 class="section-title">
        <i class="ti ti-keyboard" aria-hidden="true"></i>
        Input activity
      </h2>
      <div class="table" role="table">
        <div class="th" role="row">
          <span role="columnheader">App</span>
          <span role="columnheader">Keystrokes</span>
          <span role="columnheader">Clicks</span>
        </div>
        {#each inputData as row, i}
          <div class="tr" role="row" class:alt={i % 2 === 0}>
            <span class="td-name" role="cell">
              <i class="ti ti-apps" aria-hidden="true"></i>
              {row.exeName || 'System / Unknown'}
            </span>
            <span class="td-num" role="cell">{row.keystrokes.toLocaleString()}</span>
            <span class="td-num" role="cell">{row.clicks.toLocaleString()}</span>
          </div>
        {/each}
      </div>
    </div>
  {/if}
</div>

<style>
  .apps { display: flex; flex-direction: column; gap: var(--sp-3); }
  .topbar { display: flex; align-items: center; justify-content: space-between; gap: var(--sp-3); }
  .search {
    background: var(--md-surface-1);
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    padding: var(--sp-1) var(--sp-2);
    color: var(--md-on-surf);
    font-family: inherit;
    font-size: 13px;
    width: 200px;
    outline: none;
  }
  .search:focus { border-color: var(--md-primary); }
  .toolbar { display: flex; align-items: center; gap: var(--sp-2); }
  .sort-btn {
    display: flex; align-items: center; gap: var(--sp-1);
    padding: var(--sp-1) var(--sp-2);
    border-radius: var(--shape-sm);
    border: 1px solid var(--md-outline);
    background: transparent;
    color: var(--md-on-surf-var);
    font-family: inherit;
    font-size: 12px;
    cursor: pointer;
  }
  .sort-btn.active { background: var(--md-primary-cont); color: var(--md-on-pri-cont); border-color: var(--md-primary); }
  .sort-btn i { font-size: 14px; }
  .count { font-size: 12px; color: var(--md-on-surf-dim); margin-left: auto; }
  .table { display: flex; flex-direction: column; border: 1px solid var(--md-outline); border-radius: var(--shape-md); overflow: hidden; }
  .th {
    display: flex; padding: var(--sp-2) var(--sp-3);
    background: var(--md-surface-1);
    font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;
    color: var(--md-on-surf-var);
  }
  .th span { flex: 1; }
  .th span:last-child { width: 80px; flex: none; text-align: right; }
  .tr {
    display: flex; align-items: center;
    padding: var(--sp-2) var(--sp-3);
    font-size: 13px;
    color: var(--md-on-surf);
    border-top: 1px solid var(--md-outline);
  }
  .tr.alt { background: var(--md-surface-1); }
  .td-name { flex: 1; display: flex; align-items: center; gap: var(--sp-2); }
  .td-name i { color: var(--md-primary); font-size: 16px; }
  .td-time { width: 80px; flex: none; font-family: var(--font-mono); text-align: right; color: var(--md-on-surf-var); font-size: 12px; }

  .section { margin-top: var(--sp-4); }
  .section-title {
    font-size: 14px;
    font-weight: 500;
    color: var(--md-on-surf);
    margin-bottom: var(--sp-3);
    display: flex;
    align-items: center;
    gap: var(--sp-2);
  }
  .section-title i { color: var(--md-on-surf-var); font-size: 16px; }
  .th span:nth-child(2),
  .th span:nth-child(3) { width: 100px; flex: none; text-align: right; }
  .td-num { width: 100px; flex: none; font-family: var(--font-mono); text-align: right; color: var(--md-on-surf-var); font-size: 12px; }
</style>
