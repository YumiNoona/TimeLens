<script lang="ts">
  import { onMount } from 'svelte';

  let focusMode = $state(false);
  let items = $state<string[]>([]);
  let newItem = $state('');
  let newType = $state<'exe' | 'domain'>('domain');
  let apiOk = $state(true);

  const API = 'http://127.0.0.1:47821/api/settings';

  async function load() {
    try {
      const r = await fetch(API);
      const s = await r.json();
      focusMode = s.focusMode ?? false;
      try { items = JSON.parse(s.focusBlocklist || '[]'); } catch { items = []; }
      apiOk = true;
    } catch { apiOk = false; }
  }

  async function saveAll(list: string[]) {
    try {
      await fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ focusBlocklist: JSON.stringify(list) }),
      });
      apiOk = true;
    } catch { apiOk = false; }
  }

  async function toggleMode() {
    focusMode = !focusMode;
    try {
      await fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ focusMode }),
      });
    } catch { apiOk = false; }
  }

  function add() {
    const val = newItem.trim().toLowerCase();
    if (!val || items.includes(val)) return;
    const next = [...items, val];
    items = next;
    newItem = '';
    saveAll(next);
  }

  function remove(i: number) {
    const next = items.filter((_, idx) => idx !== i);
    items = next;
    saveAll(next);
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Enter') add();
  }

  onMount(() => load());
</script>

<div class="block">
  <div class="topbar">
    <h1 class="headline-small">Block</h1>
    {#if !apiOk}<span class="warning">Tray app not running</span>{/if}
  </div>

  <div class="card">
    <div class="card-header">
      <h2 class="title-small">Focus Mode</h2>
    </div>
    <label class="setting-row">
      <div class="setting-info">
        <span class="setting-label">{focusMode ? 'Focus mode is ON' : 'Focus mode is OFF'}</span>
        <span class="setting-desc">{focusMode ? 'Blocked apps will trigger a reminder toast' : 'Enable to start blocking distractions'}</span>
      </div>
      <div class="control">
        <button class="toggle-btn" class:on={focusMode} onclick={toggleMode}>
          <span class="toggle-knob"></span>
        </button>
      </div>
    </label>
  </div>

  <div class="card">
    <div class="card-header">
      <h2 class="title-small">Blocklist</h2>
    </div>

    <div class="add-row">
      <input class="add-input" placeholder="Add exe or domain, e.g. youtube.com or discord.exe"
        bind:value={newItem} onkeydown={onKeydown} />
      <button class="add-btn" onclick={add} disabled={!newItem.trim()}>
        <i class="ti ti-plus"></i> Block
      </button>
    </div>

    {#if items.length === 0}
      <div class="empty">
        <i class="ti ti-shield-off"></i>
        <span>Nothing blocked yet</span>
        <span class="empty-hint">Add domains like youtube.com or exe names like discord.exe</span>
      </div>
    {:else}
      <div class="bl-list">
        {#each items as item, i}
          <div class="bl-row">
            <div class="bl-icon">
              <i class="ti ti-{item.includes('.exe') ? 'apps' : 'world'}"></i>
            </div>
            <code class="bl-name">{item}</code>
            <span class="bl-tag">{item.includes('.exe') ? 'app' : 'site'}</span>
            <button class="bl-remove" onclick={() => remove(i)} aria-label="Remove {item}">
              <i class="ti ti-trash"></i>
            </button>
          </div>
        {/each}
      </div>
    {/if}
  </div>
</div>

<style>
  .block { display: flex; flex-direction: column; gap: var(--sp-4); }
  .topbar { display: flex; align-items: center; justify-content: space-between; }
  .warning {
    font-size: 12px; color: var(--md-error); font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
  }
  .card {
    background: var(--md-surface-1); border: 1px solid var(--md-outline);
    border-radius: var(--shape-lg); overflow: hidden;
  }
  .card-header {
    padding: var(--sp-3) var(--sp-4); border-bottom: 1px solid var(--md-outline);
    font-size: 13px; font-weight: 500; color: var(--md-on-surf);
  }
  .setting-row {
    display: flex; align-items: center; justify-content: space-between;
    padding: var(--sp-4);
  }
  .setting-info { display: flex; flex-direction: column; gap: 2px; }
  .setting-label { font-size: 14px; font-weight: 500; color: var(--md-on-surf); }
  .setting-desc { font-size: 12px; color: var(--md-on-surf-var); }
  .control { flex-shrink: 0; }

  .toggle-btn {
    width: 48px; height: 26px; border-radius: 99px; border: none;
    background: var(--md-outline); cursor: pointer; position: relative;
    transition: background 0.2s;
  }
  .toggle-btn.on { background: var(--md-primary); }
  .toggle-knob {
    position: absolute; top: 3px; left: 3px;
    width: 20px; height: 20px; border-radius: 50%;
    background: #fff; box-shadow: 0 1px 3px rgba(0,0,0,0.3);
    transition: transform 0.2s;
  }
  .toggle-btn.on .toggle-knob { transform: translateX(22px); }

  .add-row { display: flex; gap: var(--sp-2); padding: var(--sp-3) var(--sp-4); }
  .add-input {
    flex: 1; background: var(--md-surface-2); border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm); padding: var(--sp-2);
    color: var(--md-on-surf); font-family: var(--font-mono); font-size: 13px;
    outline: none; height: 38px;
  }
  .add-input:focus { border-color: var(--md-primary); }
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
  .bl-remove {
    background: none; border: none; color: var(--md-on-surf-dim);
    cursor: pointer; padding: var(--sp-1); border-radius: var(--shape-sm);
    font-size: 15px; transition: all 0.15s;
  }
  .bl-remove:hover { color: var(--md-error); background: var(--md-err-cont); }
</style>
