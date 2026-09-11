<script lang="ts">
  import { onMount } from 'svelte';
  import { fmtPrecise } from '../utils';
  import type { DashboardData } from '../types';
  import { appIcon } from '../appIcons';
  import { showSeconds } from '../stores/settings';
  let { data }: { data: DashboardData } = $props();

  function hashColor(s: string): string {
    let h = 0;
    for (let i = 0; i < s.length; i++) h = ((h << 5) - h + s.charCodeAt(i)) | 0;
    const hue = ((h & 0x7fffffff) % 360);
    return `hsl(${hue}, 45%, 55%)`;
  }

  type SortKey = 'name' | 'time' | 'keys' | 'clicks';
  let sortKey = $state<SortKey>('time');
  let search = $state('');
  const inputData = $derived(data.topApps.filter(app => app.keystrokes > 0 || app.clicks > 0)
    .filter(app => app.name.toLowerCase().includes(search.toLowerCase()))
    .toSorted((a,b) => (b.keystrokes+b.clicks)-(a.keystrokes+a.clicks)));
  const totalKeys = $derived(inputData.reduce((sum,row) => sum+row.keystrokes, 0));
  const totalClicks = $derived(inputData.reduce((sum,row) => sum+row.clicks, 0));
  const maxInput = $derived(Math.max(...inputData.map(row => row.keystrokes+row.clicks), 1));
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
      const r = await fetch('/api/uncategorized');
      if (!r.ok) throw new Error(`Server returned ${r.status}`);
      uncategorized = await r.json();
    } catch { uncategorized = []; }
  }

  async function assignCategory(exe: string, category: string) {
    saving = exe;
    saveError = null;
    try {
      const r = await fetch('/api/rules', {
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
    loadUncategorized();
  });

  let allApps = $derived(
    data.topApps
      .filter(a => a.name.toLowerCase().includes(search.toLowerCase()))
      .toSorted((a, b) => sortKey === 'time' ? b.minutes - a.minutes : sortKey === 'keys' ? b.keystrokes-a.keystrokes : sortKey === 'clicks' ? b.clicks-a.clicks : a.name.localeCompare(b.name))
  );

  const formatAppTime = (minutes: number) => fmtPrecise(minutes * 60, $showSeconds);

</script>

<div class="apps">
  <div class="app-toolbar">
    <input class="search" type="search" placeholder="Search apps…" bind:value={search} />
    <div class="sort-controls">
      <button class="sort-btn chip-button" class:active={sortKey === 'keys'} onclick={() => sortKey = 'keys'}>Keystrokes</button>
      <button class="sort-btn chip-button" class:active={sortKey === 'clicks'} onclick={() => sortKey = 'clicks'}>Clicks</button>
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
      <span role="columnheader">Time / share</span>
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
          {formatAppTime(app.minutes)}<small class="app-share">{data.summary.activeSeconds ? Math.round(app.minutes * 6000 / data.summary.activeSeconds) : 0}% of active time</small>
        </span>
      </div>
    {:else}<p class="empty">No applications match your search.</p>{/each}
  </div>

  {#if inputData.length > 0}
    <section class="section input-card">
      <div class="input-header"><div><h2 class="section-title"><i class="ti ti-keyboard" aria-hidden="true"></i>Input activity</h2><p>Interaction counts only—TimeLens never records what you type.</p></div><span>{inputData.length} active app{inputData.length === 1 ? '' : 's'}</span></div>
      <div class="input-summary">
        <div><i class="ti ti-keyboard"></i><span>Keystrokes<strong>{totalKeys.toLocaleString()}</strong></span></div>
        <div><i class="ti ti-pointer"></i><span>Clicks<strong>{totalClicks.toLocaleString()}</strong></span></div>
        <div><i class="ti ti-activity"></i><span>Total interactions<strong>{(totalKeys+totalClicks).toLocaleString()}</strong></span></div>
      </div>
      <div class="input-list" role="table">
        <div class="input-labels" role="row"><span>Application</span><span>Activity mix</span><span>Keys</span><span>Clicks</span></div>
        {#each inputData as row}
          {@const icon = appIcon(row.name || '')}
          <div class="input-row" role="row">
            <span class="td-name" role="cell">
              {#if icon}
                <i class="ti {icon} app-icon-tabler" aria-hidden="true"></i>
              {:else}
                <span class="app-letter" style="background:{hashColor(row.name || '')}">{(row.name || '?').charAt(0).toUpperCase()}</span>
              {/if}
              <span class="input-app"><strong>{row.name || 'System / Unknown'}</strong><small>{Math.round((row.keystrokes+row.clicks)*100/Math.max(totalKeys+totalClicks,1))}% of interactions</small></span>
            </span>
            <span class="input-bars"><i style="width:{(row.keystrokes+row.clicks)*100/maxInput}%"><b style="width:{row.keystrokes*100/Math.max(row.keystrokes+row.clicks,1)}%"></b></i></span>
            <span class="td-num" role="cell">{row.keystrokes.toLocaleString()}</span>
            <span class="td-num" role="cell">{row.clicks.toLocaleString()}</span>
          </div>
        {/each}
      </div>
    </section>
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
  .app-share { display:block; font-size:9px; color:var(--clr-text-ter); margin-top:4px; }
  .empty { padding:16px; color:var(--clr-text-sec); }
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
    height: 36px;
    padding: 0 12px;
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
  .table { display: flex; flex-direction: column; gap: 2px; padding: 4px; background: var(--clr-bg-sec); border: 1px solid var(--clr-border); border-radius: var(--shape-md); overflow: hidden; }
  .th {
    display: grid; grid-template-columns: minmax(0, 1fr) 110px; padding: var(--sp-2) var(--sp-3);
    background: transparent;
    font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;
    color: var(--clr-text-sec);
  }
  .th.input-th { grid-template-columns: 2fr 1fr 1fr; }
  .tr {
    display: grid; grid-template-columns: minmax(0, 1fr) 110px;
    align-items: center;
    min-height: 38px;
    padding: 8px 12px;
    font-size: 13px;
    color: var(--clr-text-pri);
    border: 0;
    border-radius: 7px;
  }
  .tr.input-tr { grid-template-columns: 2fr 1fr 1fr; }
  .tr.alt { background: transparent; }
  .tr:hover { background: var(--clr-bg-ter); }
  .td-name { min-width: 0; display: flex; align-items: center; gap: var(--sp-2); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

  .app-icon-tabler { font-size: 18px; color: var(--md-on-surf-var); flex-shrink: 0; width: 20px; }

  .app-letter {
		display: inline-flex; align-items: center; justify-content: center;
		width: 20px; height: 20px; border-radius: 4px;
		font-size: 11px; font-weight: 600; color: #fff;
		flex-shrink: 0;
	}
	.app-letter.hidden { display: none; }
	.td-time { font-family: var(--font-mono); text-align: right; color: var(--clr-text-pri); font-size: 12px; font-weight: 500; }
  .td-num { font-family: var(--font-mono); text-align: right; color: var(--clr-text-sec); font-size: 12px; }

  .section { margin-top: var(--sp-4); }
  .input-card{overflow:hidden;background:var(--clr-bg-sec);border:1px solid var(--clr-border);border-radius:var(--shape-md)}
  .input-header{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:16px 18px;border-bottom:1px solid var(--clr-border)}
  .input-header .section-title{margin:0}.input-header p{margin-top:4px;color:var(--clr-text-sec);font-size:11px}.input-header>span{padding:5px 9px;border-radius:99px;background:var(--clr-bg-ter);color:var(--clr-text-sec);font-size:10px}
  .input-summary{display:grid;grid-template-columns:repeat(3,1fr);gap:1px;background:var(--clr-border);border-bottom:1px solid var(--clr-border)}
  .input-summary>div{display:flex;align-items:center;gap:10px;padding:13px 18px;background:var(--clr-bg-sec)}.input-summary i{display:grid;place-items:center;width:30px;height:30px;border-radius:8px;color:var(--md-primary);background:var(--md-primary-cont)}.input-summary span{display:grid;color:var(--clr-text-sec);font-size:10px}.input-summary strong{color:var(--clr-text-pri);font:16px var(--font-mono)}
  .input-list{padding:5px}.input-labels,.input-row{display:grid;grid-template-columns:minmax(180px,1.2fr) minmax(140px,1fr) 90px 90px;gap:16px;align-items:center}.input-labels{padding:7px 12px;color:var(--clr-text-ter);font-size:9px;text-transform:uppercase;letter-spacing:.07em}.input-labels span:nth-last-child(-n+2){text-align:right}.input-row{min-height:52px;padding:8px 12px;border-radius:8px}.input-row:hover{background:var(--clr-bg-ter)}
  .input-app{min-width:0;display:grid}.input-app strong{overflow:hidden;text-overflow:ellipsis;font-size:12px}.input-app small{color:var(--clr-text-ter);font-size:9px}.input-bars i{display:block;width:100%;height:7px;overflow:hidden;border-radius:99px;background:var(--md-secondary)}.input-bars b{display:block;height:100%;background:var(--md-primary)}
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
  @media(max-width:700px){.input-summary{grid-template-columns:1fr}.input-labels{display:none}.input-row{grid-template-columns:minmax(150px,1fr) 70px 70px}.input-bars{display:none}.td-num{width:auto;margin:0}.input-header{align-items:flex-start}}

  .section-hint { font-size: 11px; color: var(--clr-text-ter); font-weight: 400; margin-left: var(--sp-2); }
  .uncat-list { display: flex; flex-direction: column; gap: 8px; margin-top: 0; }
  .uncat-row {
    display: flex; align-items: center; gap: var(--sp-3);
    min-height: 44px;
    padding: 9px 12px;
    background: var(--clr-bg-sec);
    border-radius: var(--shape-sm);
    border: 1px solid transparent;
    flex-wrap: wrap;
  }
  .uncat-row:hover { border-color: var(--clr-border); background: var(--clr-bg-ter); }
  .uncat-exe { font-family: var(--font-mono); font-size: 12px; color: var(--clr-text-pri); flex: 1; }
  .uncat-time { font-family: var(--font-mono); font-size: 11px; color: var(--clr-text-ter); width: 36px; text-align: right; flex-shrink: 0; }
  .uncat-assign {
    min-height: 30px; font-size: 11px; padding: 0 12px;
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
