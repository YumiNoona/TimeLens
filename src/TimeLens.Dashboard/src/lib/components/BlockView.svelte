<script lang="ts">
  import { onMount } from 'svelte';

  let items = $state<string[]>([]);
  let newItem = $state('');
  let blockAction = $state('notify');
  let apiOk = $state(true);
  let showAddDropdown = $state(false);
  let runningProcs = $state<string[]>([]);
  let blockStats = $state<{ exe: string; action: string; count: number }[]>([]);
  let lastBlockToast = $state<string | null>(null);

  const API = 'http://127.0.0.1:47821';

  async function load() {
    try {
      const r = await fetch(`${API}/api/settings`);
      const s = await r.json();
      blockAction = s.blockAction || 'notify';
      try { items = JSON.parse(s.focusBlocklist || '[]'); } catch { items = []; }
      apiOk = true;
    } catch { apiOk = false; }
    loadRunning();
    loadStats();
  }

  async function loadRunning() {
    try {
      const r = await fetch(`${API}/api/running-processes`);
      runningProcs = await r.json();
    } catch { runningProcs = []; }
  }

  async function loadStats() {
    try {
      const r = await fetch(`${API}/api/block/stats`);
      blockStats = await r.json();
    } catch { blockStats = []; }
  }

  async function saveAll(list: string[]) {
    try {
      await fetch(`${API}/api/settings`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ focusBlocklist: JSON.stringify(list) }),
      });
    } catch { apiOk = false; }
  }

  async function setAction(action: string) {
    blockAction = action;
    try {
      await fetch(`${API}/api/settings`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ blockAction: action }),
      });
    } catch { apiOk = false; }
  }

  function add() {
    const val = newItem.trim().toLowerCase();
    if (!val || items.includes(val)) return;
    const next = [...items, val];
    items = next;
    newItem = '';
    showAddDropdown = false;
    saveAll(next);
  }

  function remove(i: number) {
    const next = items.filter((_, idx) => idx !== i);
    items = next;
    saveAll(next);
  }

  async function enforceNow(exe: string) {
    try {
      await fetch(`${API}/api/block/enforce`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ exe }),
      });
      lastBlockToast = exe;
      setTimeout(() => lastBlockToast = null, 2000);
    } catch { }
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Enter') add();
    if (e.key === 'Escape') showAddDropdown = false;
  }

  let filteredProcs = $derived.by(() => {
    const q = newItem.trim().toLowerCase();
    if (!q) return runningProcs.filter(p => !items.some(i => i === p));
    return runningProcs.filter(p => p.toLowerCase().includes(q) && !items.some(i => i === p));
  });

  function selectProc(exe: string) {
    if (!items.includes(exe)) {
      const next = [...items, exe];
      items = next;
      saveAll(next);
    }
    newItem = '';
    showAddDropdown = false;
  }

  function isBlocked(exe: string): boolean {
    return items.some(i => exe.toLowerCase().includes(i.replace('.exe', '')));
  }

  function typeIcon(item: string): string {
    return item.includes('.exe') ? 'ti-apps' : 'ti-world';
  }

  function typeLabel(item: string): string {
    return item.includes('.exe') ? 'app' : 'site';
  }

  const modeOptions = [
    { id: 'notify', icon: 'ti-bell', label: 'Notify', desc: 'Show a reminder toast when a blocked app is opened — no enforcement' },
    { id: 'hide', icon: 'ti-eye-off', label: 'Hide', desc: 'Automatically minimize blocked app windows when detected' },
    { id: 'kill', icon: 'ti-x', label: 'Kill', desc: 'Terminate blocked processes immediately' },
    { id: 'strict', icon: 'ti-shield', label: 'Strict', desc: 'Kill + minimize + re-check every 5s — no escape' },
  ];

  onMount(() => { load(); const t = setInterval(loadRunning, 5000); return () => clearInterval(t); });
</script>

<div class="block">
  <div class="topbar">
    <h1 class="headline-small">Block</h1>
    {#if !apiOk}<span class="warning">Tray app not running</span>{/if}
    <button class="refresh-btn" onclick={loadStats} title="Refresh stats"><i class="ti ti-refresh"></i></button>
  </div>

  <!-- Action Mode -->
  <div class="card">
    <div class="card-header">
      <h2 class="title-small">Block Action</h2>
    </div>
    <div class="mode-grid">
      {#each modeOptions as { id, icon, label, desc }}
        <button class="mode-card" class:active={blockAction === id} onclick={() => setAction(id)}>
          <div class="mode-icon"><i class="ti {icon}"></i></div>
          <div class="mode-label">{label}</div>
          <div class="mode-desc">{desc}</div>
        </button>
      {/each}
    </div>
  </div>

  <!-- Blocklist -->
  <div class="card">
    <div class="card-header flex-between">
      <h2 class="title-small">Blocklist ({items.length})</h2>
      <button class="scanner-btn" onclick={loadRunning} title="Scan running apps"><i class="ti ti-search"></i> Scan</button>
    </div>

    <div class="add-row">
      <div class="combo-wrapper">
        <input class="add-input" placeholder="exe name or domain, e.g. discord.exe or youtube.com"
          bind:value={newItem} onfocus={() => { loadRunning(); showAddDropdown = true; }} oninput={() => showAddDropdown = true}
          onkeydown={onKeydown} onblur={() => setTimeout(() => showAddDropdown = false, 150)} autocomplete="off" />
        {#if showAddDropdown && filteredProcs.length > 0}
          <div class="suggestions">
            {#each filteredProcs as proc}
              <button class="suggestion-item" onmousedown={() => selectProc(proc)} type="button">
                <span class="live-dot" class:blocked={isBlocked(proc)}></span>
                <code>{proc}</code>
                {#if isBlocked(proc)}<span class="bl-tag-sm">blocked</span>{/if}
              </button>
            {/each}
          </div>
        {/if}
      </div>
      <button class="add-btn" onclick={add} disabled={!newItem.trim()}>
        <i class="ti ti-plus"></i> Block
      </button>
    </div>

    {#if items.length === 0}
      <div class="empty">
        <i class="ti ti-shield-off"></i>
        <span>Nothing blocked yet</span>
        <span class="empty-hint">Add executables like discord.exe or domains like youtube.com</span>
      </div>
    {:else}
      <div class="bl-list">
        {#each items as item, i}
          <div class="bl-row">
            <div class="bl-icon"><i class="ti {typeIcon(item)}"></i></div>
            <code class="bl-name">{item}</code>
            <span class="bl-tag">{typeLabel(item)}</span>
            <button class="bl-enforce" onclick={() => enforceNow(item)} title="Enforce now" disabled={blockAction === 'notify'}>
              <i class="ti ti-player-play"></i>
            </button>
            <button class="bl-remove" onclick={() => remove(i)} aria-label="Remove {item}">
              <i class="ti ti-trash"></i>
            </button>
          </div>
        {/each}
      </div>
    {/if}
  </div>

  <!-- Block Stats -->
  {#if blockStats.length > 0}
    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Today's Blocks ({blockStats.reduce((a, b) => a + b.count, 0)})</h2>
      </div>
      <div class="stats-list">
        {#each blockStats as stat}
          <div class="stat-row">
            <code class="stat-exe">{stat.exe}</code>
            <span class="stat-action">{stat.action}</span>
            <div class="stat-bar-track">
              <div class="stat-bar-fill" style="width: {Math.min(stat.count / 5 * 100, 100)}%"></div>
            </div>
            <span class="stat-count">×{stat.count}</span>
          </div>
        {/each}
      </div>
    </div>
  {/if}
</div>

<style>
  .block { display: flex; flex-direction: column; gap: var(--sp-4); }
  .topbar { display: flex; align-items: center; gap: var(--sp-2); }
  .topbar h1 { flex: 1; }
  .warning {
    font-size: 12px; color: var(--md-error); font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
  }
  .refresh-btn {
    background: none; border: 1px solid var(--md-outline); border-radius: var(--shape-sm);
    color: var(--md-on-surf-var); cursor: pointer; padding: var(--sp-1) var(--sp-2);
    font-size: 16px; transition: all 0.15s;
  }
  .refresh-btn:hover { color: var(--md-primary); border-color: var(--md-primary); }
  .card {
    background: var(--md-surface-1); border: 1px solid var(--md-outline);
    border-radius: var(--shape-lg); overflow: hidden;
  }
  .card-header {
    padding: var(--sp-3) var(--sp-4); border-bottom: 1px solid var(--md-outline);
    font-size: 13px; font-weight: 500; color: var(--md-on-surf);
  }
  .flex-between { display: flex; align-items: center; justify-content: space-between; }
  .scanner-btn {
    display: flex; align-items: center; gap: 4px;
    background: var(--md-surface-2); border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm); padding: var(--sp-1) var(--sp-2);
    color: var(--md-on-surf-var); font-family: inherit; font-size: 11px;
    cursor: pointer; transition: all 0.15s;
  }
  .scanner-btn:hover { color: var(--md-primary); border-color: var(--md-primary); }

  .mode-grid {
    display: grid; grid-template-columns: repeat(4, 1fr);
    gap: var(--sp-2); padding: var(--sp-3);
  }
  .mode-card {
    display: flex; flex-direction: column; align-items: center; gap: var(--sp-1);
    padding: var(--sp-3); border-radius: var(--shape-md);
    border: 1px solid var(--md-outline); background: transparent;
    color: var(--md-on-surf-var); cursor: pointer;
    transition: all 0.15s; font-family: inherit;
  }
  .mode-card:hover { background: var(--md-surface-2); }
  .mode-card.active { border-color: var(--md-primary); background: var(--md-primary-cont); color: var(--md-on-pri-cont); }
  .mode-icon i { font-size: 22px; }
  .mode-label { font-size: 13px; font-weight: 600; }
  .mode-desc { font-size: 10px; text-align: center; line-height: 1.3; opacity: 0.7; }

  .add-row { display: flex; gap: var(--sp-2); padding: var(--sp-3) var(--sp-4); }
  .combo-wrapper { flex: 1; position: relative; }
  .add-input {
    width: 100%; background: var(--md-surface-2); border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm); padding: var(--sp-2);
    color: var(--md-on-surf); font-family: var(--font-mono); font-size: 13px;
    outline: none; height: 38px; box-sizing: border-box;
  }
  .add-input:focus { border-color: var(--md-primary); }
  .suggestions {
    position: absolute; top: 100%; left: 0; right: 0; margin-top: 4px; z-index: 100;
    background: var(--md-surface-2); border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm); max-height: 200px; overflow-y: auto;
    box-shadow: 0 8px 24px rgba(0,0,0,0.4);
  }
  .suggestion-item {
    display: flex; align-items: center; gap: var(--sp-2); width: 100%;
    padding: var(--sp-2); border: none; background: none;
    color: var(--md-on-surf); font-family: var(--font-mono); font-size: 12px;
    cursor: pointer; text-align: left;
  }
  .suggestion-item:hover { background: var(--md-surface-1); }
  .live-dot { width: 6px; height: 6px; border-radius: 50%; background: var(--md-tertiary); flex-shrink: 0; }
  .live-dot.blocked { background: var(--md-error); }

  .add-btn {
    display: flex; align-items: center; gap: var(--sp-1);
    padding: var(--sp-2) var(--sp-4); border-radius: var(--shape-sm);
    border: none; background: var(--md-primary); color: #1a1a1a;
    font-family: inherit; font-size: 13px; font-weight: 600; cursor: pointer;
    white-space: nowrap; height: 38px;
  }
  .add-btn:disabled { opacity: 0.4; cursor: default; }

  .empty {
    display: flex; flex-direction: column; align-items: center; gap: var(--sp-2);
    padding: var(--sp-8) var(--sp-4); color: var(--md-on-surf-dim);
  }
  .empty i { font-size: 36px; }
  .empty span { font-size: 14px; }
  .empty-hint { font-size: 12px !important; opacity: 0.6; }

  .bl-list { display: flex; flex-direction: column; }
  .bl-row {
    display: flex; align-items: center; gap: var(--sp-3);
    padding: var(--sp-3) var(--sp-4); border-top: 1px solid var(--md-outline);
  }
  .bl-icon {
    width: 36px; height: 36px; border-radius: var(--shape-sm);
    background: var(--md-surface-2); display: flex; align-items: center; justify-content: center;
    flex-shrink: 0;
  }
  .bl-icon i { font-size: 16px; color: var(--md-primary); }
  .bl-name {
    font-family: var(--font-mono); font-size: 13px; color: var(--md-on-surf); flex: 1;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .bl-tag {
    font-size: 11px; font-weight: 500; color: var(--md-on-surf-dim);
    background: var(--md-surface-2); padding: 2px 8px; border-radius: var(--shape-sm);
    text-transform: uppercase; letter-spacing: 0.05em;
  }
  .bl-tag-sm { font-size: 10px; margin-left: auto; color: var(--md-error); font-style: italic; }
  .bl-enforce {
    background: none; border: 1px solid var(--md-outline); color: var(--md-on-surf-dim);
    cursor: pointer; padding: var(--sp-1); border-radius: var(--shape-sm);
    font-size: 14px; transition: all 0.15s;
  }
  .bl-enforce:hover:not(:disabled) { color: var(--md-primary); border-color: var(--md-primary); }
  .bl-enforce:disabled { opacity: 0.3; cursor: default; }
  .bl-remove {
    background: none; border: none; color: var(--md-on-surf-dim);
    cursor: pointer; padding: var(--sp-1); border-radius: var(--shape-sm);
    font-size: 15px; transition: all 0.15s;
  }
  .bl-remove:hover { color: var(--md-error); background: var(--md-err-cont); }

  .stats-list { display: flex; flex-direction: column; }
  .stat-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-2) var(--sp-4); border-top: 1px solid var(--md-outline);
    font-size: 12px;
  }
  .stat-exe { font-family: var(--font-mono); font-size: 12px; width: 120px; color: var(--md-on-surf); }
  .stat-action {
    font-size: 11px; font-weight: 500; text-transform: uppercase; color: var(--md-on-surf-var);
    width: 50px;
  }
  .stat-bar-track { flex: 1; height: 4px; background: var(--md-surface); border-radius: var(--shape-full); overflow: hidden; }
  .stat-bar-fill { height: 100%; background: var(--md-error); border-radius: var(--shape-full); }
  .stat-count { font-family: var(--font-mono); font-size: 11px; color: var(--md-on-surf-dim); width: 36px; text-align: right; }
</style>
