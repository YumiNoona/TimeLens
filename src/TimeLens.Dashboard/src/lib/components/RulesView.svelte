<script lang="ts">
  import { colorForCategory } from '../colors';
  import { live } from '../stores/activity';

  let rules: { pattern: string; category: string }[] = $state([]);
  let newExe = $state('');
  let newCat = $state('Other');
  let apiOk = $state(true);
  let suggestions = $state<string[]>([]);
  let showDropdown = $state(false);
  let highlightIdx = $state(-1);

  const categories = ['Work', 'Development', 'Browsing', 'Communication', 'Entertainment', 'Design', 'Social', 'Other'];
  const API = '/api/rules';

  async function load() {
    try {
      const r = await fetch(API);
      rules = await r.json();
      apiOk = true;
    } catch {
      apiOk = false;
    }
  }

  async function addRule(pattern: string = '') {
    const p = (pattern || newExe.trim().toLowerCase());
    if (!p) return;
    try {
      await fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ pattern: p, category: newCat }),
      });
      rules = [...rules, { pattern: p, category: newCat }];
      newExe = '';
      showDropdown = false;
    } catch { apiOk = false; }
  }

  async function removeRule(pattern: string) {
    try {
      await fetch(`${API}/${encodeURIComponent(pattern)}`, { method: 'DELETE' });
      rules = rules.filter(r => r.pattern !== pattern);
    } catch { apiOk = false; }
  }

  async function loadSuggestions() {
    try {
      const r = await fetch('/api/running-processes');
      suggestions = await r.json();
    } catch {
      suggestions = [];
    }
  }

  function onFocus() {
    if (suggestions.length === 0) loadSuggestions();
    showDropdown = true;
    highlightIdx = -1;
  }

  function onInput() {
    showDropdown = true;
    highlightIdx = -1;
  }

  let filtered = $derived.by(() => {
    const q = newExe.trim().toLowerCase();
    if (!q) {
      const list = [...suggestions];
      if ($live?.currentApp && $live.currentApp !== '—') list.unshift($live.currentApp);
      return list;
    }
    const results = suggestions.filter(s => s.toLowerCase().includes(q));
    if ($live?.currentApp && $live.currentApp !== '—' && $live.currentApp.toLowerCase().includes(q)) {
      if (!results.some(r => r.toLowerCase() === $live.currentApp!.toLowerCase()))
        results.unshift($live.currentApp);
    }
    return results;
  });

  function selectSuggestion(exe: string) {
    newExe = exe;
    showDropdown = false;
    highlightIdx = -1;
  }

  function onKeydown(e: KeyboardEvent) {
    if (!showDropdown || filtered.length === 0) {
      if (e.key === 'Enter') { addRule(); e.preventDefault(); }
      return;
    }
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      highlightIdx = Math.min(highlightIdx + 1, filtered.length - 1);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      highlightIdx = Math.max(highlightIdx - 1, -1);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (highlightIdx >= 0) selectSuggestion(filtered[highlightIdx]);
      else addRule();
    } else if (e.key === 'Escape') {
      showDropdown = false;
      highlightIdx = -1;
    }
  }

  let focusTimeout: ReturnType<typeof setTimeout>;
  function onBlur() {
    focusTimeout = setTimeout(() => { showDropdown = false; highlightIdx = -1; }, 150);
  }
  function clearFocusTimeout() { clearTimeout(focusTimeout); }

  $effect(() => { load(); });
</script>

<div class="rules">
  <div class="topbar">
    <h1 class="headline-small">Rules</h1>
    {#if !apiOk}
      <span class="warning">Tray app not running</span>
    {/if}
  </div>
  <p class="title-small desc">Map executable names to categories. Changes apply immediately.</p>

  <div class="add-bar">
    <div class="combo-wrapper">
      <input
        class="input"
        placeholder="exe name, e.g. code.exe"
        bind:value={newExe}
        onfocus={onFocus}
        oninput={onInput}
        onkeydown={onKeydown}
        onblur={onBlur}
        autocomplete="off"
      />
      {#if showDropdown && filtered.length > 0}
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div class="suggestions" onmousedown={clearFocusTimeout}>
          {#each filtered as exe, i}
            <!-- svelte-ignore a11y_no_static_element_interactions -->
            <button
              class="suggestion-item"
              class:highlight={i === highlightIdx}
              class:live={$live?.currentApp === exe}
              onmousedown={() => selectSuggestion(exe)}
              type="button"
            >
              {#if $live?.currentApp === exe}
                <span class="live-dot" aria-hidden="true"></span>
              {/if}
              <span>{exe}</span>
              {#if $live?.currentApp === exe}
                <span class="live-label">currently active</span>
              {/if}
            </button>
          {/each}
        </div>
      {/if}
    </div>
    <select class="select" bind:value={newCat}>
      {#each categories as c}
        <option value={c}>{c}</option>
      {/each}
    </select>
    <button class="add-btn" onclick={() => addRule()} disabled={!newExe.trim()}>
      <i class="ti ti-plus" aria-hidden="true"></i> Add
    </button>
  </div>

  <div class="card">
    {#if rules.length === 0}
      <div class="empty-state">
        <span class="empty-text">No rules yet — add one above.</span>
      </div>
    {:else}
      {#each rules as rule, i}
        <div class="rule-row" class:last={i === rules.length - 1}>
          <span class="color-dot" style="background: {colorForCategory(rule.category)}"></span>
          <code class="rule-pattern">{rule.pattern}</code>
          <span class="rule-arrow">→</span>
          <span class="rule-cat">{rule.category}</span>
          <button class="del-btn" onclick={() => removeRule(rule.pattern)} aria-label="Remove rule">
            <i class="ti ti-x" aria-hidden="true"></i>
          </button>
        </div>
      {/each}
    {/if}
  </div>
</div>

<style>
  .rules { display: flex; flex-direction: column; gap: var(--sp-4); max-width: 640px; }
  .topbar { display: flex; align-items: center; justify-content: space-between; }
  .desc { color: var(--md-on-surf-var); margin-top: calc(-1 * var(--sp-2)); }
  .warning {
    font-size: 12px; color: var(--md-error); font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
  }
  .add-bar { display: flex; gap: var(--sp-2); align-items: center; }

  .combo-wrapper { flex: 1; position: relative; }

  .input {
    width: 100%;
    background: var(--md-surface-1);
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    padding: var(--sp-2);
    color: var(--md-on-surf);
    font-family: var(--font-mono);
    font-size: 13px;
    outline: none;
    box-sizing: border-box;
    height: 36px;
  }
  .input:focus { border-color: var(--md-primary); }

  .suggestions {
    position: absolute;
    top: 100%;
    left: 0;
    right: 0;
    margin-top: 4px;
    background: var(--md-surface-2);
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    max-height: 220px;
    overflow-y: auto;
    z-index: 100;
    box-shadow: 0 8px 24px rgba(0,0,0,0.4);
  }

  .suggestion-item {
    display: flex;
    align-items: center;
    gap: var(--sp-2);
    width: 100%;
    padding: var(--sp-2);
    border: none;
    background: none;
    color: var(--md-on-surf);
    font-family: var(--font-mono);
    font-size: 12px;
    cursor: pointer;
    text-align: left;
  }

  .suggestion-item:hover,
  .suggestion-item.highlight { background: var(--md-surface-1); }

  .suggestion-item.live { color: var(--md-primary); }

  .live-dot {
    width: 7px; height: 7px;
    border-radius: 50%;
    background: var(--md-primary);
    flex-shrink: 0;
  }

  .live-label {
    margin-left: auto;
    font-family: var(--font-display);
    font-size: 10px;
    color: var(--md-on-surf-dim);
    font-style: italic;
  }

  .select {
    background: var(--md-surface-1);
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    padding: var(--sp-2);
    color: var(--md-on-surf);
    font-family: inherit;
    font-size: 13px;
    outline: none;
    cursor: pointer;
    box-sizing: border-box;
    height: 36px;
  }
  .select:focus { border-color: var(--md-primary); }

  .add-btn {
    display: flex; align-items: center; gap: var(--sp-0);
    padding: var(--sp-2) var(--sp-3);
    background: var(--md-primary);
    color: #1a1a1a;
    border: none;
    border-radius: var(--shape-sm);
    font-family: inherit;
    font-size: 13px;
    font-weight: 500;
    cursor: pointer;
    white-space: nowrap;
    box-sizing: border-box;
    height: 36px;
  }
  .add-btn:disabled { opacity: 0.4; cursor: default; }

  .card {
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-md);
    overflow: hidden;
    min-height: 60px;
  }
  .empty-state { display: flex; align-items: center; justify-content: center; padding: var(--sp-6); }
  .empty-text { font-size: 13px; color: var(--md-on-surf-dim); }

  .rule-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-3) var(--sp-4);
    border-bottom: 1px solid var(--md-outline);
    font-size: 13px;
  }
  .rule-row.last { border-bottom: none; }
  .color-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
  .rule-pattern { font-family: var(--font-mono); font-size: 12px; color: var(--md-on-surf); min-width: 140px; }
  .rule-arrow { color: var(--md-primary); font-size: 12px; flex-shrink: 0; }
  .rule-cat { color: var(--md-on-surf-var); font-weight: 500; flex: 1; }

  .del-btn {
    background: none; border: none;
    color: var(--md-on-surf-dim);
    cursor: pointer;
    padding: var(--sp-1);
    border-radius: var(--shape-sm);
    font-size: 14px;
  }
  .del-btn:hover { background: color-mix(in srgb, var(--md-error) 10%, transparent); color: var(--md-error); }
</style>
