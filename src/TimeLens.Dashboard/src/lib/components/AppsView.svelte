<script lang="ts">
  import { onMount } from 'svelte';
  import type { DashboardData, InputEntry } from '../types';
  import { appIcon } from '../appIcons';
  let { data }: { data: DashboardData } = $props();

  function hashColor(s: string): string {
    let h = 0;
    for (let i = 0; i < s.length; i++) h = ((h << 5) - h + s.charCodeAt(i)) | 0;
    const hue = ((h & 0x7fffffff) % 360);
    return `hsl(${hue}, 45%, 55%)`;
  }

  type SortKey = 'name' | 'time';
  let sortKey = $state<SortKey>('time');
  let search = $state('');
  let inputData = $state<InputEntry[]>([]);
  let uncategorized = $state<{ exe: string; seconds: number }[]>([]);
  let assigningFor = $state<string | null>(null);
  let saving = $state<string | null>(null);
  let saveError = $state<string | null>(null);

  const CATEGORIES = [
    'development', 'work', 'documents', 'communication', 'design',
    'entertainment', 'gaming', 'social', 'news', 'finance',
    'health', 'education', 'utilities', 'browsing', 'other'
  ];

  async function loadUncategorized() {
    try {
      const r = await fetch('http://127.0.0.1:47821/api/uncategorized');
      uncategorized = await r.json();
    } catch { uncategorized = []; }
  }

  async function assignCategory(exe: string, category: string) {
    saving = exe;
    saveError = null;
    try {
      const r = await fetch('http://127.0.0.1:47821/api/rules', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ pattern: exe, category, ruleType: 'substring', target: 'exe', priority: 0 })
      });
      if (!r.ok) throw new Error(`Server returned ${r.status}`);
      assigningFor = null;
      await loadUncategorized();
    } catch (e) {
      saveError = e instanceof Error ? e.message : 'Failed to save';
    } finally {
      saving = null;
    }
  }

  onMount(async () => {
    try {
      const r = await fetch('http://127.0.0.1:47821/api/input-summary');
      inputData = await r.json();
    } catch { inputData = []; }
    loadUncategorized();
  });

  let allApps = $derived(
    data.topApps
      .filter(a => a.name.toLowerCase().includes(search.toLowerCase()))
      .toSorted((a, b) => sortKey === 'time' ? b.minutes - a.minutes : a.name.localeCompare(b.name))
  );
</script>

<div class="apps">
  <div class="app-toolbar">
    <input class="search" type="search" placeholder="Search apps…" bind:value={search} />
    <div class="sort-controls">
      <button class="sort-btn chip-button" class:active={sortKey === 'time'} onclick={() => sortKey = 'time'}>
        <i class="ti ti-clock" aria-hidden="true"></i> Time
      </button>
      <button class="sort-btn chip-button" class:active={sortKey === 'name'} onclick={() => sortKey = 'name'}>
        <i class="ti ti-sort-alpha" aria-hidden="true"></i> Name
      </button>
    </div>
    <span class="count">{allApps.length} apps</span>
  </div>

  <div class="table" role="table">
    <div class="th" role="row">
      <span role="columnheader">App</span>
      <span role="columnheader">Time</span>
    </div>
    {#each allApps as app, i}
      {@const icon = appIcon(app.name)}
      <div class="tr" role="row" class:alt={i % 2 === 0}>
        <span class="td-name" role="cell">
          {#if icon}
            <i class="ti {icon} app-icon-tabler" aria-hidden="true"></i>
          {:else}
            <span class="app-letter" style="background:{hashColor(app.name)}">{app.name.charAt(0).toUpperCase()}</span>
          {/if}
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
        <div class="th input-th" role="row">
          <span role="columnheader">App</span>
          <span role="columnheader">Keystrokes</span>
          <span role="columnheader">Clicks</span>
        </div>
        {#each inputData as row, i}
          {@const icon = appIcon(row.exeName || '')}
          <div class="tr input-tr" role="row" class:alt={i % 2 === 0}>
            <span class="td-name" role="cell">
              {#if icon}
                <i class="ti {icon} app-icon-tabler" aria-hidden="true"></i>
              {:else}
                <span class="app-letter" style="background:{hashColor(row.exeName || '')}">{(row.exeName || '?').charAt(0).toUpperCase()}</span>
              {/if}
              {row.exeName || 'System / Unknown'}
            </span>
            <span class="td-num" role="cell">{row.keystrokes.toLocaleString()}</span>
            <span class="td-num" role="cell">{row.clicks.toLocaleString()}</span>
          </div>
        {/each}
      </div>
    </div>
  {/if}

  {#if uncategorized.length > 0}
    <div class="section">
      <h2 class="section-title">
        <i class="ti ti-tag-off" aria-hidden="true"></i>
        Uncategorized ({uncategorized.length})
        <span class="section-hint">Click Assign to add a category rule</span>
      </h2>

      {#if saveError}
        <div class="save-error">{saveError}</div>
      {/if}

      <div class="uncat-list" role="list">
        {#each uncategorized as item}
          <div class="uncat-row" role="listitem">
            <span class="uncat-exe">{item.exe}</span>
            <span class="uncat-time">{Math.floor(item.seconds / 60)}m</span>

            {#if saving === item.exe}
              <span class="uncat-saving">Saving…</span>
            {:else if assigningFor === item.exe}
              <div class="uncat-picker">
                {#each CATEGORIES as cat}
                  <button class="cat-pill" onclick={() => assignCategory(item.exe, cat)}>{cat}</button>
                {/each}
                <button class="cat-cancel" onclick={() => assigningFor = null} aria-label="Cancel">x</button>
              </div>
            {:else}
              <button class="uncat-assign" onclick={() => assigningFor = item.exe}>Assign</button>
            {/if}
          </div>
        {/each}
      </div>
    </div>
  {/if}
</div>

<style>
  .apps { display: flex; flex-direction: column; gap: var(--sp-4); }
  .app-toolbar {
    display: flex;
    align-items: center;
    gap: var(--sp-3);
    flex-wrap: wrap;
  }
  .search {
    background: var(--clr-bg-sec);
    border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm);
    padding: var(--sp-1) var(--sp-2);
    color: var(--clr-text-pri);
    font-family: inherit;
    font-size: 13px;
    width: 200px;
    outline: none;
  }
  .search:focus { border-color: var(--md-primary); }
  .sort-controls {
    display: flex;
    gap: var(--sp-2);
  }
  .sort-btn i { font-size: 14px; }
  .count { font-size: 12px; color: var(--clr-text-ter); margin-left: auto; }
  .table { display: flex; flex-direction: column; border: 1px solid var(--clr-border); border-radius: var(--shape-md); overflow: hidden; }
  .th {
    display: grid; grid-template-columns: 2fr 1fr; padding: var(--sp-2) var(--sp-3);
    background: var(--clr-bg-sec);
    font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;
    color: var(--clr-text-sec);
  }
  .th.input-th { grid-template-columns: 2fr 1fr 1fr; }
  .tr {
    display: grid; grid-template-columns: 2fr 1fr;
    align-items: center;
    padding: var(--sp-2) var(--sp-3);
    font-size: 13px;
    color: var(--clr-text-pri);
    border-top: 1px solid var(--clr-border);
  }
  .tr.input-tr { grid-template-columns: 2fr 1fr 1fr; }
  .tr.alt { background: var(--clr-bg-sec); }
  .td-name { min-width: 0; display: flex; align-items: center; gap: var(--sp-2); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

  .app-icon-tabler { font-size: 18px; color: var(--md-on-surf-var); flex-shrink: 0; width: 20px; }

  .app-letter {
		display: inline-flex; align-items: center; justify-content: center;
		width: 20px; height: 20px; border-radius: 4px;
		font-size: 11px; font-weight: 600; color: #fff;
		flex-shrink: 0;
	}
	.app-letter.hidden { display: none; }
	.td-name img { flex-shrink: 0; }
	.td-time { font-family: var(--font-mono); text-align: right; color: var(--clr-text-sec); font-size: 12px; }
  .td-num { font-family: var(--font-mono); text-align: right; color: var(--clr-text-sec); font-size: 12px; }

  .section { margin-top: var(--sp-4); }
  .section-title {
    font-size: 14px;
    font-weight: 500;
    color: var(--clr-text-pri);
    margin-bottom: var(--sp-3);
    display: flex;
    align-items: center;
    gap: var(--sp-2);
  }
  .section-title i { color: var(--clr-text-sec); font-size: 16px; }
  .th span:nth-child(2),
  .th span:nth-child(3) { width: 100px; flex: none; text-align: right; }
  .td-num { width: 100px; flex: none; font-family: var(--font-mono); text-align: right; color: var(--clr-text-sec); font-size: 12px; margin-left: var(--sp-3); }

  .section-hint { font-size: 11px; color: var(--clr-text-ter); font-weight: 400; margin-left: var(--sp-2); }
  .uncat-list { display: flex; flex-direction: column; gap: 2px; margin-top: var(--sp-2); }
  .uncat-row {
    display: flex; align-items: center; gap: var(--sp-3);
    padding: var(--sp-2) var(--sp-3);
    background: var(--clr-bg-sec);
    border-radius: var(--shape-sm);
    border: 1px solid var(--clr-border);
    flex-wrap: wrap;
  }
  .uncat-exe { font-family: var(--font-mono); font-size: 12px; color: var(--clr-text-pri); flex: 1; }
  .uncat-time { font-family: var(--font-mono); font-size: 11px; color: var(--clr-text-ter); width: 36px; text-align: right; flex-shrink: 0; }
  .uncat-assign {
    font-size: 11px; padding: 3px 10px;
    border: 1px solid var(--md-primary); border-radius: var(--shape-sm);
    background: none; color: var(--md-primary); cursor: pointer; font-family: inherit; flex-shrink: 0;
  }
  .uncat-assign:hover { background: var(--md-primary-cont); }
  .uncat-picker { display: flex; flex-wrap: wrap; gap: 4px; width: 100%; padding-top: var(--sp-2); }
  .cat-pill {
    font-size: 10px; padding: 2px 8px;
    border: 1px solid var(--clr-border); border-radius: var(--shape-full);
    background: var(--clr-bg-ter); color: var(--clr-text-sec);
    cursor: pointer; font-family: inherit; text-transform: capitalize;
  }
  .cat-pill:hover { border-color: var(--md-primary); color: var(--md-primary); }
  .cat-cancel {
    font-size: 11px; padding: 2px 8px;
    border: 1px solid var(--clr-border); border-radius: var(--shape-full);
    background: none; color: var(--clr-text-ter); cursor: pointer; font-family: inherit;
  }
  .cat-cancel:hover { color: var(--md-error); border-color: var(--md-error); }

  .uncat-saving {
    font-size: 11px; color: var(--md-primary); font-weight: 500; flex-shrink: 0;
    animation: saving-pulse 1.2s ease-in-out infinite;
  }

  @keyframes saving-pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.3; }
  }

  .save-error {
    background: var(--md-err-cont);
    color: var(--md-error);
    padding: var(--sp-2) var(--sp-3);
    border-radius: var(--shape-sm);
    font-size: 12px;
    margin-bottom: var(--sp-2);
    border: 1px solid rgba(224,112,112,0.2);
  }
</style>
