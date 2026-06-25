<script lang="ts">
  import { onMount } from 'svelte';
  import { appIcon } from '../appIcons';

  type BlockEntry = { i: string; m: 'u' | 't'; e?: string };

  let items = $state<BlockEntry[]>([]);
  let newItem = $state('');
  let blockAction = $state('notify');
  let focusMode = $state(false);
  let apiOk = $state(true);
  let showAddDropdown = $state(false);
  let runningProcs = $state<string[]>([]);
  let blockStats = $state<{ exe: string; action: string; count: number }[]>([]);
  let lastBlockToast = $state<string | null>(null);
  let addingDuration = $state(0);
  let confirmRemove = $state<number | null>(null);

  const API = 'http://127.0.0.1:47821';
  const DURATIONS = [
    { value: 0, label: 'Until unblocked' },
    { value: 15, label: '15 min' },
    { value: 30, label: '30 min' },
    { value: 60, label: '1 hour' },
    { value: 120, label: '2 hours' },
    { value: 240, label: '4 hours' },
  ];

  async function load() {
    try {
      const r = await fetch(`${API}/api/settings`);
      const s = await r.json();
      blockAction = s.blockAction || 'notify';
      focusMode = s.focusMode ?? false;
      const raw = s.focusBlocklist || '[]';
      try { items = JSON.parse(raw); } catch { items = []; }
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

  async function saveAll(list: BlockEntry[]) {
    try {
      await fetch(`${API}/api/settings`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ focusBlocklist: JSON.stringify(list) }),
      });
    } catch { apiOk = false; }
  }

  async function saveFocus(val: boolean) {
    focusMode = val;
    try {
      await fetch(`${API}/api/settings`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ focusMode: val }),
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
    if (!val || items.some(e => e.i === val)) return;
    let entry: BlockEntry;
    if (addingDuration > 0) {
      const exp = new Date(Date.now() + addingDuration * 60_000).toISOString();
      entry = { i: val, m: 't', e: exp };
    } else {
      entry = { i: val, m: 'u' };
    }
    items = [...items, entry];
    newItem = '';
    addingDuration = 0;
    showAddDropdown = false;
    saveAll(items);
  }

  function remove(i: number) {
    items = items.filter((_, idx) => idx !== i);
    confirmRemove = null;
    saveAll(items);
  }

  function requestRemove(i: number) {
    confirmRemove = i;
  }

  function cancelRemove() {
    confirmRemove = null;
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
    if (e.key === 'Escape') { showAddDropdown = false; confirmRemove = null; }
  }

  let filteredProcs = $derived.by(() => {
    const q = newItem.trim().toLowerCase();
    const ids = new Set(items.map(e => e.i));
    if (!q) return runningProcs.filter(p => !ids.has(p));
    return runningProcs.filter(p => p.toLowerCase().includes(q) && !ids.has(p));
  });

  function selectProc(exe: string) {
    if (items.some(e => e.i === exe)) { newItem = ''; showAddDropdown = false; return; }
    let entry: BlockEntry;
    if (addingDuration > 0) {
      const exp = new Date(Date.now() + addingDuration * 60_000).toISOString();
      entry = { i: exe, m: 't', e: exp };
    } else {
      entry = { i: exe, m: 'u' };
    }
    items = [...items, entry];
    newItem = '';
    addingDuration = 0;
    showAddDropdown = false;
    saveAll(items);
  }

  function isBlocked(exe: string): boolean {
    return items.some(e => exe.toLowerCase().includes(e.i.replace('.exe', '')));
  }

  function typeIcon(id: string): string {
    if (id.includes('.exe')) return appIcon(id) ?? 'ti-apps';
    return appIcon(id) ?? 'ti-world';
  }

  function typeLabel(id: string): string {
    return id.includes('.exe') ? 'app' : 'site';
  }

  function modeLabel(entry: BlockEntry): string {
    if (entry.m === 't' && entry.e) {
      const rem = new Date(entry.e).getTime() - Date.now();
      if (rem <= 0) return 'expired';
      const m = Math.ceil(rem / 60_000);
      if (m >= 60) return `${Math.round(m / 60)}h left`;
      return `${m}m left`;
    }
    return 'always';
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
        <button class="mode-card chip-button" class:active={blockAction === id} onclick={() => setAction(id)}>
          <div class="mode-icon"><i class="ti {icon}"></i></div>
          <div class="mode-label">{label}</div>
          <div class="mode-desc">{desc}</div>
        </button>
      {/each}
    </div>
  </div>

  <!-- Focus Mode -->
  <div class="card">
    <div class="card-header">
      <h2 class="title-small">Focus Mode</h2>
    </div>
    <label class="focus-row">
      <div class="focus-info">
        <span class="focus-label">Enable Focus Mode</span>
        <span class="focus-desc">Blocked apps are enforced — minimize, terminate, or strict lockdown</span>
      </div>
      <input type="checkbox" class="toggle" checked={focusMode} onchange={() => saveFocus(!focusMode)} />
    </label>
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
          onkeydown={onKeydown} onblur={() => showAddDropdown = false} autocomplete="off" />
        {#if showAddDropdown && filteredProcs.length > 0}
          <div class="suggestions">
            {#each filteredProcs as proc}
              <button class="suggestion-item" onmousedown={(e) => { e.preventDefault(); selectProc(proc); }} type="button">
                <span class="live-dot" class:blocked={isBlocked(proc)}></span>
                <code>{proc}</code>
                {#if isBlocked(proc)}<span class="bl-tag-sm">blocked</span>{/if}
              </button>
            {/each}
          </div>
        {/if}
      </div>
      <div class="duration-picker">
        {#each DURATIONS as d}
          <button class="dur-btn chip-button" class:active={addingDuration === d.value} onclick={() => addingDuration = d.value} type="button">
            {d.label}
          </button>
        {/each}
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
        {#each items as entry, i}
          <div class="bl-row">
            <div class="bl-icon"><i class="ti {typeIcon(entry.i)}"></i></div>
            <code class="bl-name">{entry.i}</code>
            <span class="bl-tag">{typeLabel(entry.i)}</span>
            <span class="bl-mode">{modeLabel(entry)}</span>
            <button class="bl-enforce" onclick={() => enforceNow(entry.i)} title="Enforce now" disabled={blockAction === 'notify'}>
              <i class="ti ti-player-play"></i>
            </button>
            {#if confirmRemove === i}
              <div class="confirm-group">
                <button class="bl-confirm-yes" onclick={() => remove(i)} aria-label="Confirm remove {entry.i}">
                  <i class="ti ti-check"></i>
                </button>
                <button class="bl-confirm-no" onclick={cancelRemove} aria-label="Cancel">
                  <i class="ti ti-x"></i>
                </button>
              </div>
            {:else}
              <button class="bl-remove" onclick={() => requestRemove(i)} aria-label="Remove {entry.i}">
                <i class="ti ti-trash"></i>
              </button>
            {/if}
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
  .block { display: flex; flex-direction: column; gap: 24px; }
  .topbar { display: flex; align-items: center; gap: var(--sp-2); }
  .topbar h1 { flex: 1; }
  .warning {
    font-size: 12px; color: var(--md-error); font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
  }
  .refresh-btn {
    background: none; border: 1px solid var(--clr-border); border-radius: var(--shape-sm);
    color: var(--clr-text-sec); cursor: pointer; padding: var(--sp-1) var(--sp-2);
    font-size: 16px; transition: all 0.15s;
  }
  .refresh-btn:hover { color: var(--md-primary); border-color: var(--md-primary); }
  .card {
    background: var(--clr-bg-sec); border: 1px solid var(--clr-border);
    border-radius: var(--shape-lg); overflow: hidden;
  }
  .card-header {
    padding: var(--sp-3) var(--sp-4); border-bottom: 1px solid var(--clr-border);
    font-size: 13px; font-weight: 500; color: var(--clr-text-pri);
  }
  .flex-between { display: flex; align-items: center; justify-content: space-between; }
  .scanner-btn {
    display: flex; align-items: center; gap: 4px;
    background: var(--clr-bg-ter); border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm); padding: var(--sp-1) var(--sp-2);
    color: var(--clr-text-sec); font-family: inherit; font-size: 11px;
    cursor: pointer; transition: all 0.15s;
  }
  .scanner-btn:hover { color: var(--md-primary); border-color: var(--md-primary); }

  .focus-row {
    display: flex; align-items: center; justify-content: space-between;
    padding: var(--sp-3) var(--sp-4);
  }
  .focus-info { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .focus-label { font-size: 13px; font-weight: 500; color: var(--clr-text-pri); }
  .focus-desc { font-size: 12px; color: var(--clr-text-sec); }
  .toggle {
    appearance: none; width: 40px; height: 22px;
    background: var(--clr-border); border-radius: 99px;
    position: relative; cursor: pointer; flex-shrink: 0; margin: 0;
    transition: background 0.2s ease;
  }
  .toggle::after {
    content: ''; position: absolute; width: 18px; height: 18px;
    background: #fff; border-radius: 50%; top: 2px; left: 2px;
    transition: transform 0.2s ease, box-shadow 0.2s ease;
    box-shadow: 0 1px 3px rgba(0,0,0,0.3);
  }
  .toggle:checked { background: var(--md-primary); }
  .toggle:checked::after { transform: translateX(18px); }

  .mode-grid {
    display: grid; grid-template-columns: repeat(4, 1fr);
    gap: var(--sp-2); padding: var(--sp-3);
  }
  .mode-card {
    flex-direction: column;
    padding: var(--sp-3);
    border-radius: var(--shape-md);
  }
  .mode-icon i { font-size: 22px; }
  .mode-label { font-size: 13px; font-weight: 600; }
  .mode-desc { font-size: 10px; text-align: center; line-height: 1.3; opacity: 0.7; }

  .add-row { display: flex; gap: var(--sp-2); padding: var(--sp-3) var(--sp-4); flex-wrap: wrap; }
  .duration-picker { display: flex; gap: 4px; flex-wrap: wrap; flex: 1; }
  .dur-btn { font-size: 11px; padding: 2px 8px; }
  .dur-btn.active { border-color: var(--md-primary); background: var(--md-primary-cont); color: var(--md-on-pri-cont); }
  .combo-wrapper { flex: 1; position: relative; }
  .add-input {
    width: 100%; background: var(--clr-bg-ter); border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm); padding: var(--sp-2);
    color: var(--clr-text-pri); font-family: var(--font-mono); font-size: 13px;
    outline: none; height: 38px; box-sizing: border-box;
  }
  .add-input:focus { border-color: var(--md-primary); }
  .suggestions {
    position: absolute; top: 100%; left: 0; right: 0; margin-top: 4px; z-index: 100;
    background: var(--clr-bg-ter); border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm); max-height: 200px; overflow-y: auto;
    box-shadow: 0 8px 24px rgba(0,0,0,0.4);
  }
  .suggestion-item {
    display: flex; align-items: center; gap: var(--sp-2); width: 100%;
    padding: var(--sp-2); border: none; background: none;
    color: var(--clr-text-pri); font-family: var(--font-mono); font-size: 12px;
    cursor: pointer; text-align: left;
  }
  .suggestion-item:hover { background: var(--clr-bg-sec); }
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
    padding: var(--sp-8) var(--sp-4); color: var(--clr-text-ter);
  }
  .empty i { font-size: 36px; }
  .empty span { font-size: 14px; }
  .empty-hint { font-size: 12px !important; opacity: 0.6; }

  .bl-list { display: flex; flex-direction: column; }
  .bl-row {
    display: flex; align-items: center; gap: var(--sp-3);
    padding: var(--sp-3) var(--sp-4); border-top: 1px solid var(--clr-border);
  }
  .bl-icon {
    width: 36px; height: 36px; border-radius: var(--shape-sm);
    background: var(--clr-bg-ter); display: flex; align-items: center; justify-content: center;
    flex-shrink: 0;
  }
  .bl-icon i { font-size: 16px; color: var(--md-primary); }
  .bl-name {
    font-family: var(--font-mono); font-size: 13px; color: var(--clr-text-pri); flex: 1;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .bl-tag {
    font-size: 11px; font-weight: 500; color: var(--clr-text-ter);
    background: var(--clr-bg-ter); padding: 2px 8px; border-radius: var(--shape-sm);
    text-transform: uppercase; letter-spacing: 0.05em;
  }
  .bl-tag-sm { font-size: 10px; margin-left: auto; color: var(--md-error); font-style: italic; }
  .bl-mode { font-size: 11px; color: var(--clr-text-ter); font-family: var(--font-mono); flex-shrink: 0; min-width: 48px; text-align: right; }
  .confirm-group { display: flex; gap: 4px; }
  .bl-confirm-yes, .bl-confirm-no {
    background: none; border: 1px solid var(--clr-border); border-radius: var(--shape-sm);
    cursor: pointer; padding: var(--sp-1); font-size: 14px; transition: all 0.15s;
    display: flex; align-items: center; justify-content: center;
  }
  .bl-confirm-yes { color: var(--md-error); border-color: var(--md-error); }
  .bl-confirm-yes:hover { background: var(--md-err-cont); }
  .bl-confirm-no { color: var(--clr-text-sec); }
  .bl-confirm-no:hover { background: var(--clr-bg-ter); }
  .bl-enforce {
    background: none; border: 1px solid var(--clr-border); color: var(--clr-text-ter);
    cursor: pointer; padding: var(--sp-1); border-radius: var(--shape-sm);
    font-size: 14px; transition: all 0.15s;
  }
  .bl-enforce:hover:not(:disabled) { color: var(--md-primary); border-color: var(--md-primary); }
  .bl-enforce:disabled { opacity: 0.3; cursor: default; }
  .bl-remove {
    background: none; border: none; color: var(--clr-text-ter);
    cursor: pointer; padding: var(--sp-1); border-radius: var(--shape-sm);
    font-size: 15px; transition: all 0.15s;
  }
  .bl-remove:hover { color: var(--md-error); background: var(--md-err-cont); }

  .stats-list { display: flex; flex-direction: column; }
  .stat-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-2) var(--sp-4); border-top: 1px solid var(--clr-border);
    font-size: 12px;
  }
  .stat-exe { font-family: var(--font-mono); font-size: 12px; width: 120px; color: var(--clr-text-pri); }
  .stat-action {
    font-size: 11px; font-weight: 500; text-transform: uppercase; color: var(--clr-text-sec);
    width: 50px;
  }
  .stat-bar-track { flex: 1; height: 4px; background: var(--md-surface); border-radius: var(--shape-full); overflow: hidden; }
  .stat-bar-fill { height: 100%; background: var(--md-error); border-radius: var(--shape-full); }
  .stat-count { font-family: var(--font-mono); font-size: 11px; color: var(--clr-text-ter); width: 36px; text-align: right; }
</style>
