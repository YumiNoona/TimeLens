<script lang="ts">
  import { onMount } from 'svelte';
  import { appIcon } from '../appIcons';
  import { reorderable } from '../actions/reorderable';

  type BlockEntry = { i: string; m: 'u' | 't'; e?: string };
  type RetryAction = () => Promise<void>;

  let items = $state<BlockEntry[]>([]);
  let newItem = $state('');
  let blockAction = $state('hide');
  let focusMode = $state(false);
  let apiOk = $state(true);
  let showAddDropdown = $state(false);
  let runningProcs = $state<string[]>([]);
  let blockStats = $state<{ exe: string; action: string; count: number }[]>([]);
  let lastBlockToast = $state<string | null>(null);
  let addingDuration = $state(0);
  let confirmRemove = $state<number | null>(null);
  let errorMessage = $state<string | null>(null);
  let saving = $state(false);
  let blockProtectionEnabled = $state(false);
  let unlockToken = $state('');
  let showUnlock = $state(false);
  let unlockPassword = $state('');
  let unlockError = $state('');
  let unlocking = $state(false);
  let pendingAction: (() => Promise<void>) | null = null;

  const API = '';
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
      if (!r.ok) throw new Error(`Settings request failed (${r.status})`);
      const s = await r.json();
      blockAction = s.blockAction || 'hide';
      focusMode = s.focusMode ?? false;
      blockProtectionEnabled = s.blockProtectionEnabled ?? false;
      const raw = s.focusBlocklist || '[]';
      try {
        const parsed = JSON.parse(raw);
        items = Array.isArray(parsed) ? parsed.filter((entry) => entry && typeof entry.i === 'string') : [];
      } catch { items = []; }
      apiOk = true;
      errorMessage = null;
    } catch (error) {
      apiOk = false;
      errorMessage = error instanceof Error ? error.message : 'Unable to load blocking settings';
    }
    loadRunning();
    loadStats();
  }

  async function loadRunning() {
    try {
      const r = await fetch(`${API}/api/running-processes`);
      if (!r.ok) throw new Error();
      runningProcs = await r.json();
    } catch { runningProcs = []; }
  }

  async function loadStats() {
    try {
      const r = await fetch(`${API}/api/block/stats`);
      if (!r.ok) throw new Error();
      blockStats = await r.json();
    } catch { blockStats = []; }
  }

  async function postProtectedSetting(payload: Record<string, unknown>, retry: RetryAction | null): Promise<Response | null> {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (unlockToken) headers['X-TimeLens-Unlock'] = unlockToken;
    const response = await fetch(`${API}/api/settings`, {
      method: 'POST', headers, body: JSON.stringify(payload),
    });
    if (response.status === 423) {
      unlockToken = '';
      pendingAction = retry ?? null;
      unlockPassword = '';
      unlockError = '';
      showUnlock = true;
      return null;
    }
    return response;
  }

  async function unlockProtection() {
    if (!unlockPassword || unlocking) return;
    unlocking = true;
    unlockError = '';
    try {
      const response = await fetch(`${API}/api/block/protection/unlock`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ password: unlockPassword }),
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || 'Could not unlock protected blocks');
      unlockToken = result.token || '';
      showUnlock = false;
      unlockPassword = '';
      const action = pendingAction;
      pendingAction = null;
      if (action) await action();
    } catch (error) {
      unlockError = error instanceof Error ? error.message : 'Could not unlock protected blocks';
    } finally { unlocking = false; }
  }

  function closeUnlock() {
    showUnlock = false;
    unlockPassword = '';
    unlockError = '';
    pendingAction = null;
  }

  async function saveAll(list: BlockEntry[], retry: RetryAction | null): Promise<boolean> {
    saving = true;
    try {
      const response = await postProtectedSetting({ focusBlocklist: JSON.stringify(list) }, retry);
      if (response === null) return false;
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error || `Save failed (${response.status})`);
      apiOk = true;
      errorMessage = null;
      return true;
    } catch (error) {
      apiOk = false;
      errorMessage = error instanceof Error ? error.message : 'Unable to save blocklist';
      return false;
    } finally { saving = false; }
  }

  async function saveFocus(val: boolean) {
    const previous = focusMode;
    focusMode = val;
    try {
      const response = await postProtectedSetting({ focusMode: val }, () => saveFocus(val));
      if (response === null) { focusMode = previous; return; }
      if (!response.ok) throw new Error(`Save failed (${response.status})`);
      apiOk = true;
      errorMessage = null;
    } catch {
      focusMode = previous;
      apiOk = false;
      errorMessage = 'Focus Mode could not be updated';
    }
  }

  async function setAction(action: string) {
    const previous = blockAction;
    blockAction = action;
    try {
      const response = await postProtectedSetting({ blockAction: action }, () => setAction(action));
      if (response === null) { blockAction = previous; return; }
      if (!response.ok) throw new Error(`Save failed (${response.status})`);
      apiOk = true;
      errorMessage = null;
    } catch {
      blockAction = previous;
      apiOk = false;
      errorMessage = 'Block action could not be updated';
    }
  }

  function sanitizeEntry(raw: string): string {
    // Strip URLs down to just the hostname (e.g. https://learn.microsoft.com/en-us → learn.microsoft.com)
    try {
      const u = new URL(raw);
      return u.hostname || raw;
    } catch {
      // Not a URL — strip common noise
      return raw.replace(/\/.*/, '').replace(/^https?:\/\//, '');
    }
  }

  async function add() {
    let val = newItem.trim().toLowerCase();
    if (!val) return;
    val = sanitizeEntry(val);
    if (!/^[a-z0-9][a-z0-9.-]*$/.test(val) || (!val.endsWith('.exe') && !val.includes('.'))) {
      errorMessage = 'Enter an executable such as discord.exe or a domain such as youtube.com';
      return;
    }
    if (items.some(e => e.i === val)) { newItem = ''; return; }
    let entry: BlockEntry;
    if (addingDuration > 0) {
      const exp = new Date(Date.now() + addingDuration * 60_000).toISOString();
      entry = { i: val, m: 't', e: exp };
    } else {
      entry = { i: val, m: 'u' };
    }
    const previous = items;
    const next = [...items, entry];
    items = next;
    newItem = '';
    addingDuration = 0;
    showAddDropdown = false;
    if (!await saveAll(next, null)) items = previous;
  }

  async function remove(i: number) {
    const previous = items;
    const next = items.filter((_, idx) => idx !== i);
    items = next;
    confirmRemove = null;
    if (!await saveAll(next, async () => {
      items = next;
      if (!await saveAll(next, null)) items = previous;
    })) items = previous;
  }

  function requestRemove(i: number) {
    confirmRemove = i;
  }

  function cancelRemove() {
    confirmRemove = null;
  }

  async function enforceNow(exe: string) {
    try {
      const response = await fetch(`${API}/api/block/enforce`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ exe }),
      });
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error || `Enforcement failed (${response.status})`);
      lastBlockToast = exe;
      errorMessage = null;
      setTimeout(() => lastBlockToast = null, 2000);
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : 'Unable to enforce this entry';
    }
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

  async function selectProc(exe: string) {
    exe = sanitizeEntry(exe);
    if (items.some(e => e.i === exe)) { newItem = ''; showAddDropdown = false; return; }
    let entry: BlockEntry;
    if (addingDuration > 0) {
      const exp = new Date(Date.now() + addingDuration * 60_000).toISOString();
      entry = { i: exe, m: 't', e: exp };
    } else {
      entry = { i: exe, m: 'u' };
    }
    const previous = items;
    const next = [...items, entry];
    items = next;
    newItem = '';
    addingDuration = 0;
    showAddDropdown = false;
    if (!await saveAll(next, null)) items = previous;
  }

  function isBlocked(exe: string): boolean {
    return items.some(e => e.i.endsWith('.exe') && exe.toLowerCase() === e.i.toLowerCase());
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

<div class="block" use:reorderable={{ key: 'block:cards', draggable: ':scope > .card' }}>
    {#if !apiOk}<span class="warning">Tray app not running</span>{/if}
    {#if errorMessage}<button class="warning error-dismiss" onclick={() => errorMessage = null}>{errorMessage} <span>×</span></button>{/if}
  <div class="card focus-workflow">
    <div class="workflow-header">
      <div class="workflow-title">
        <span class="step-icon"><i class="ti ti-focus-2" aria-hidden="true"></i></span>
        <div><span class="eyebrow">Focus control</span><h2>{focusMode ? 'Protection is active' : 'Protection is paused'}</h2><p>Your list stays editable even while enforcement is off.</p></div>
      </div>
      <div class="workflow-actions">
        {#if blockProtectionEnabled}
          <span class="protection-badge" class:unlocked={!!unlockToken} title="Protected changes require your password">
            <i class="ti {unlockToken ? 'ti-lock-open' : 'ti-lock'}" aria-hidden="true"></i>
            {unlockToken ? 'Unlocked' : 'Password protected'}
          </span>
        {/if}
        <button class="refresh-btn" onclick={() => { loadStats(); loadRunning(); }} title="Refresh block activity"><i class="ti ti-refresh" aria-hidden="true"></i><span>Refresh</span></button>
        <label class="master-switch">
          <span>{focusMode ? 'On' : 'Off'}</span>
          <input type="checkbox" class="toggle" checked={focusMode} onchange={() => saveFocus(!focusMode)} aria-label="Enable Focus Mode" />
        </label>
      </div>
    </div>
    <div class="mode-section">
      <div class="section-heading"><span class="step-number">1</span><div><strong>Choose enforcement</strong><span>What TimeLens should do when a target opens</span></div></div>
      <div class="mode-grid">
        {#each modeOptions as { id, icon, label, desc }}
          <button class="mode-card" class:active={blockAction === id} onclick={() => setAction(id)} aria-pressed={blockAction === id}>
            <span class="mode-icon"><i class="ti {icon}" aria-hidden="true"></i></span>
            <span class="mode-copy"><strong>{label}</strong><small>{desc}</small></span>
            {#if blockAction === id}<i class="ti ti-check mode-check" aria-hidden="true"></i>{/if}
          </button>
        {/each}
      </div>
    </div>
  </div>

  <!-- Blocklist -->
  <div class="card blocklist-card">
    <div class="card-header flex-between">
      <div class="section-heading compact"><span class="step-number">2</span><div><strong>Apps & sites</strong><span>{items.length} target{items.length === 1 ? '' : 's'} configured</span></div></div>
      <button class="scanner-btn" onclick={loadRunning} title="Scan running apps"><i class="ti ti-search"></i> Scan</button>
    </div>

    {#if !focusMode}
      <div class="block-banner">
        <i class="ti ti-alert-circle"></i>
        Ready when you are — enable Focus Mode above to enforce this list.
      </div>
    {/if}

    <div class="add-row">
      <div class="add-field target-field">
        <label for="block-target">Target</label>
        <div class="combo-wrapper">
          <i class="ti ti-search input-icon" aria-hidden="true"></i>
          <input id="block-target" class="add-input" placeholder="discord.exe or youtube.com"
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
      </div>
      <div class="add-field duration-field">
        <span class="field-label">Duration</span>
        <div class="duration-picker">
          {#each DURATIONS as d}
            <button class="dur-btn" class:active={addingDuration === d.value} onclick={() => addingDuration = d.value} type="button" aria-pressed={addingDuration === d.value}>
              {d.label}
            </button>
          {/each}
        </div>
      </div>
      <button class="add-btn" onclick={add} disabled={!newItem.trim() || saving}>
        <i class="ti ti-plus"></i> Add target
      </button>
    </div>

    {#if items.length === 0}
      <div class="empty">
        <span class="empty-icon"><i class="ti ti-shield-plus"></i></span>
        <span>No targets yet</span>
        <span class="empty-hint">Add a running app or a website domain to begin.</span>
      </div>
    {:else}
      <div class="bl-list">
        {#each items as entry, i}
          <div class="bl-row">
            <div class="bl-icon"><i class="ti {typeIcon(entry.i)}"></i></div>
            <code class="bl-name">{entry.i}</code>
            <span class="bl-tag">{typeLabel(entry.i)}</span>
            <span class="bl-mode">{modeLabel(entry)}</span>
            <button class="bl-enforce" onclick={() => enforceNow(entry.i)} title="Enforce now" disabled={blockAction === 'notify' || !focusMode || !entry.i.endsWith('.exe')}>
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
    <div class="card stats-card">
      <div class="card-header flex-between">
        <div><h2 class="title-small">Today's interventions</h2><span class="header-subtitle">Times TimeLens responded to a blocked target</span></div>
        <span class="total-badge">{blockStats.reduce((a, b) => a + b.count, 0)} total</span>
      </div>
      <div class="stats-list">
        {#each blockStats as stat}
          <div class="stat-row">
            <span class="stat-app-icon"><i class="ti {typeIcon(stat.exe)}" aria-hidden="true"></i></span>
            <code class="stat-exe">{stat.exe}</code>
            <span class="stat-action">{stat.action}</span>
            <span class="stat-count">{stat.count} time{stat.count === 1 ? '' : 's'}</span>
          </div>
        {/each}
      </div>
    </div>
  {/if}

  {#if showUnlock}
    <div class="unlock-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) closeUnlock(); }}>
      <div class="unlock-dialog" role="dialog" aria-modal="true" aria-labelledby="unlock-title">
        <button class="dialog-close" type="button" onclick={closeUnlock} aria-label="Close"><i class="ti ti-x"></i></button>
        <span class="unlock-icon"><i class="ti ti-lock-password" aria-hidden="true"></i></span>
        <div class="unlock-copy">
          <span class="eyebrow">Protected change</span>
          <h2 id="unlock-title">Enter your block password</h2>
          <p>This action would disable or weaken an active restriction. Unlocking lasts five minutes.</p>
        </div>
        <label class="password-field">
          <span>Password</span>
          <input type="password" bind:value={unlockPassword} onkeydown={(event) => { if (event.key === 'Enter') unlockProtection(); }} autocomplete="current-password" />
        </label>
        {#if unlockError}<div class="unlock-error" role="alert"><i class="ti ti-alert-circle"></i>{unlockError}</div>{/if}
        <div class="dialog-actions">
          <button class="dialog-secondary" type="button" onclick={closeUnlock}>Cancel</button>
          <button class="dialog-primary" type="button" onclick={unlockProtection} disabled={!unlockPassword || unlocking}>
            <i class="ti ti-lock-open" aria-hidden="true"></i>{unlocking ? 'Checking…' : 'Unlock change'}
          </button>
        </div>
      </div>
    </div>
  {/if}
</div>

<style>
  .block { display: flex; flex-direction: column; gap: 24px; }
  .warning {
    font-size: 12px; color: var(--md-error); font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
    width: fit-content;
  }
  .error-dismiss { border: 1px solid color-mix(in srgb, var(--md-error) 30%, transparent); cursor: pointer; font-family: inherit; }
  .error-dismiss span { margin-left: 8px; }
  .block-toolbar {
    display: flex;
    align-items: center;
    justify-content: flex-start;
  }
  .refresh-btn i { font-size: 14px; }
  .card {
    background: var(--clr-bg-sec); border: 1px solid var(--clr-border);
    border-radius: var(--shape-lg); overflow: hidden; padding: 0;
  }
  .card-header {
    padding: var(--sp-3) var(--sp-4); border-bottom: 1px solid var(--clr-border);
    font-size: 13px; font-weight: 500; color: var(--clr-text-pri); margin-bottom: 0;
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

  .card-disabled { opacity: 0.45; pointer-events: none; }
  .block-banner {
    display: flex; align-items: center; gap: var(--sp-2);
    margin: 0 var(--sp-4) var(--sp-3); padding: var(--sp-2) var(--sp-3);
    background: color-mix(in srgb, var(--md-primary) 12%, transparent);
    border: 1px solid color-mix(in srgb, var(--md-primary) 25%, transparent);
    border-radius: var(--shape-sm); color: var(--md-primary);
    font-size: 12px; font-weight: 500;
  }
  .block-banner i { font-size: 15px; flex-shrink: 0; }

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

  /* Workflow layout */
  .block { gap: 16px; }
  .focus-workflow { overflow: visible; }
  .workflow-header {
    display: flex; align-items: center; justify-content: space-between; gap: 18px;
    padding: 18px 20px;
    background: linear-gradient(105deg, color-mix(in srgb, var(--md-primary) 8%, var(--clr-bg-sec)), var(--clr-bg-sec) 58%);
    border-bottom: 1px solid var(--clr-border);
  }
  .workflow-title, .workflow-actions, .master-switch, .section-heading { display: flex; align-items: center; }
  .workflow-title { gap: 12px; }
  .workflow-title > div, .section-heading > div { display: flex; flex-direction: column; gap: 2px; }
  .workflow-title h2 { margin: 0; color: var(--clr-text-pri); font-size: var(--type-section-title); }
  .workflow-title p { margin: 0; color: var(--clr-text-sec); font-size: var(--type-section-subtitle); line-height: 1.4; }
  .eyebrow { color: var(--md-primary); font-size: var(--type-card-subtitle); font-weight: 700; letter-spacing: .08em; text-transform: uppercase; }
  .step-icon { width: 40px; height: 40px; display: grid; place-items: center; border-radius: 12px; color: var(--md-primary); background: var(--md-primary-cont); border: 1px solid color-mix(in srgb, var(--md-primary) 22%, transparent); font-size: 19px; }
  .workflow-actions { gap: 9px; }
  .protection-badge { height: 34px; display: inline-flex; align-items: center; gap: 6px; padding: 0 10px; color: var(--md-primary); background: var(--md-primary-cont); border: 1px solid color-mix(in srgb, var(--md-primary) 25%, transparent); border-radius: var(--shape-full); font-size: 10px; font-weight: 600; white-space: nowrap; }
  .protection-badge.unlocked { color: var(--md-tertiary); background: color-mix(in srgb, var(--md-tertiary) 10%, var(--clr-bg-ter)); border-color: color-mix(in srgb, var(--md-tertiary) 25%, transparent); }
  .refresh-btn { height: 34px; display: inline-flex; align-items: center; gap: 6px; padding: 0 11px; border-radius: var(--shape-sm); border: 1px solid var(--clr-border); background: var(--clr-bg-ter); color: var(--clr-text-sec); font: 11px inherit; cursor: pointer; }
  .refresh-btn:hover { color: var(--md-primary); border-color: var(--md-primary); }
  .master-switch { height: 34px; gap: 8px; padding: 0 7px 0 11px; border-radius: var(--shape-full); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); color: var(--clr-text-pri); font-size: 11px; font-weight: 600; cursor: pointer; }
  .mode-section { padding: 16px 18px 18px; }
  .section-heading { gap: 9px; }
  .section-heading strong { color: var(--clr-text-pri); font-size: var(--type-section-title); }
  .section-heading span:not(.step-number) { color: var(--clr-text-sec); font-size: var(--type-section-subtitle); }
  .section-heading.compact strong { font-size: var(--type-section-title); }
  .step-number { width: 24px; height: 24px; display: grid; place-items: center; flex: 0 0 auto; border-radius: 8px; color: var(--md-primary); background: var(--md-primary-cont); font: 700 11px var(--font-mono); }
  .mode-grid { grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 8px; padding: 12px 0 0; }
  .mode-card { min-height: 68px; display: grid; grid-template-columns: 34px minmax(0, 1fr) 18px; align-items: center; gap: 9px; padding: 10px; text-align: left; border: 1px solid var(--clr-border); border-radius: var(--shape-md); background: var(--clr-bg-ter); color: var(--clr-text-sec); font-family: inherit; cursor: pointer; transition: transform var(--duration-fast), border-color var(--duration-fast), background var(--duration-fast); }
  .mode-card:hover { transform: translateY(-1px); border-color: var(--clr-border-strong); }
  .mode-card.active { border-color: var(--md-primary); background: color-mix(in srgb, var(--md-primary) 7%, var(--clr-bg-ter)); color: var(--clr-text-pri); }
  .mode-icon { width: 34px; height: 34px; display: grid; place-items: center; border-radius: 9px; background: var(--clr-bg-sec); color: var(--clr-text-sec); }
  .mode-card.active .mode-icon { color: var(--md-primary); background: var(--md-primary-cont); }
  .mode-icon i { font-size: 17px; }
  .mode-copy { min-width: 0; display: flex; flex-direction: column; gap: 2px; }
  .mode-copy strong { color: inherit; font-size: 12px; }
  .mode-copy small { color: var(--clr-text-sec); font-size: 9px; line-height: 1.25; }
  .mode-check { color: var(--md-primary); font-size: 14px; }
  .blocklist-card .card-header { padding: 14px 18px; }
  .block-banner { margin: 12px 18px 0; }
  .add-row { display: grid; grid-template-columns: minmax(250px, 1.2fr) minmax(420px, 2fr) auto; align-items: end; gap: 12px; padding: 16px 18px; }
  .add-field { min-width: 0; display: flex; flex-direction: column; gap: 7px; }
  .add-field label, .field-label { color: var(--clr-text-sec); font-size: 10px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
  .input-icon { position: absolute; z-index: 2; left: 11px; top: 50%; transform: translateY(-50%); color: var(--clr-text-ter); font-size: 14px; }
  .add-input { padding-left: 34px; height: 36px; background: var(--clr-bg-ter); }
  .duration-picker { display: grid; grid-template-columns: repeat(6, minmax(62px, 1fr)); gap: 5px; }
  .dur-btn { min-height: 36px; padding: 0 7px; border: 1px solid var(--clr-border); border-radius: var(--shape-sm); background: var(--clr-bg-ter); color: var(--clr-text-sec); font: 10px inherit; cursor: pointer; white-space: nowrap; }
  .dur-btn:hover { color: var(--clr-text-pri); border-color: var(--clr-border-strong); }
  .dur-btn.active { border-color: var(--md-primary); background: var(--md-primary-cont); color: var(--md-on-pri-cont); }
  .add-btn { height: 36px; padding: 0 14px; }
  .empty { min-height: 112px; gap: 5px; padding: 17px; border-top: 1px solid var(--clr-border); }
  .empty-icon { width: 34px; height: 34px; display: grid; place-items: center; margin-bottom: 2px; border-radius: 10px; background: var(--clr-bg-ter); color: var(--clr-text-ter); }
  .empty-icon i { font-size: 18px; }
  .empty > span:not(.empty-hint):not(.empty-icon) { color: var(--clr-text-sec); font-size: 12px; }
  .bl-row { min-height: 54px; padding: 8px 18px; }
  .bl-icon { width: 32px; height: 32px; }
  .stats-card .card-header > div { display: flex; flex-direction: column; gap: 2px; }
  .header-subtitle { color: var(--clr-text-sec); font-size: var(--type-section-subtitle); line-height: 1.4; }
  .total-badge { padding: 4px 8px; color: var(--md-primary); background: var(--md-primary-cont); border-radius: var(--shape-full); font: 10px var(--font-mono); }
  .stats-list { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 7px; padding: 12px; }
  .stat-row { min-height: 44px; display: grid; grid-template-columns: 28px minmax(80px, 1fr) auto auto; gap: 8px; padding: 7px 9px; border: 1px solid var(--clr-border); border-radius: var(--shape-md); background: var(--clr-bg-ter); }
  .stat-app-icon { width: 28px; height: 28px; display: grid; place-items: center; border-radius: 8px; color: var(--md-error); background: color-mix(in srgb, var(--md-error) 9%, transparent); }
  .stat-exe { width: auto; overflow: hidden; text-overflow: ellipsis; }
  .stat-action { width: auto; padding: 3px 6px; border-radius: var(--shape-full); background: var(--clr-bg-sec); font-size: 9px; }
  .stat-count { width: auto; color: var(--clr-text-sec); }
  .unlock-backdrop { position: fixed; inset: 0; z-index: 1000; display: grid; place-items: center; padding: 20px; background: rgba(4, 6, 3, .74); backdrop-filter: blur(8px); }
  .unlock-dialog { width: min(440px, 100%); position: relative; display: flex; flex-direction: column; gap: 16px; padding: 24px; color: var(--clr-text-pri); background: var(--clr-bg-sec); border: 1px solid var(--clr-border-strong); border-radius: 18px; box-shadow: 0 24px 80px rgba(0,0,0,.55); }
  .dialog-close { position: absolute; top: 14px; right: 14px; width: 30px; height: 30px; display: grid; place-items: center; color: var(--clr-text-sec); background: transparent; border: 0; border-radius: 8px; cursor: pointer; }
  .dialog-close:hover { color: var(--clr-text-pri); background: var(--clr-bg-ter); }
  .unlock-icon { width: 44px; height: 44px; display: grid; place-items: center; color: var(--md-primary); background: var(--md-primary-cont); border: 1px solid color-mix(in srgb, var(--md-primary) 25%, transparent); border-radius: 13px; font-size: 20px; }
  .unlock-copy { display: flex; flex-direction: column; gap: 4px; }
  .unlock-copy h2 { margin: 0; font-size: 18px; }
  .unlock-copy p { margin: 0; color: var(--clr-text-sec); font-size: 12px; line-height: 1.5; }
  .password-field { display: flex; flex-direction: column; gap: 7px; color: var(--clr-text-sec); font-size: 10px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
  .password-field input { height: 40px; padding: 0 12px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-sm); font: 14px var(--font-mono); outline: none; }
  .password-field input:focus { border-color: var(--md-primary); box-shadow: 0 0 0 3px color-mix(in srgb, var(--md-primary) 10%, transparent); }
  .unlock-error { display: flex; align-items: center; gap: 7px; padding: 9px 10px; color: var(--md-error); background: color-mix(in srgb, var(--md-error) 9%, transparent); border-radius: var(--shape-sm); font-size: 11px; }
  .dialog-actions { display: flex; justify-content: flex-end; gap: 8px; }
  .dialog-secondary, .dialog-primary { height: 36px; display: inline-flex; align-items: center; justify-content: center; gap: 6px; padding: 0 13px; border-radius: var(--shape-sm); font: 12px inherit; cursor: pointer; }
  .dialog-secondary { color: var(--clr-text-sec); background: transparent; border: 1px solid var(--clr-border); }
  .dialog-primary { color: var(--md-on-primary); background: var(--md-primary); border: 1px solid var(--md-primary); font-weight: 600; }
  .dialog-primary:disabled { opacity: .4; cursor: not-allowed; }
  @media (max-width: 1180px) {
    .mode-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .add-row { grid-template-columns: 1fr auto; }
    .duration-field { grid-column: 1 / -1; grid-row: 2; }
  }
  @media (max-width: 720px) {
    .workflow-header { align-items: flex-start; flex-direction: column; }
    .workflow-actions { width: 100%; justify-content: space-between; }
    .mode-grid, .add-row { grid-template-columns: 1fr; }
    .duration-field { grid-column: auto; grid-row: auto; }
    .duration-picker { grid-template-columns: repeat(3, 1fr); }
    .add-btn { width: 100%; justify-content: center; }
    .mode-copy small { white-space: normal; }
  }
</style>
