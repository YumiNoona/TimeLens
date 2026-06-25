<script lang="ts">
  import { onMount } from 'svelte';
  import { colorForCategory } from '../colors';
  import { live } from '../stores/activity';

  type Rule = { pattern: string; category: string; ruleType: string; target: string; priority: number; id: number };

  let rules: Rule[] = $state([]);
  let builtinExe: Record<string, string> = $state({});
  let builtinDomains: Record<string, string> = $state({});
  let runningProcs: string[] = $state([]);
  let newPattern = $state('');
  let newCat = $state('other');
  let newRuleType = $state('substring');
  let newTarget = $state('exe');
  let apiOk = $state(true);
  let suggestions = $state<string[]>([]);
  let showDropdown = $state(false);
  let highlightIdx = $state(-1);
  let dragIdx: number | null = $state(null);

  const categories = [
    { value: 'work', label: 'Work' },
    { value: 'development', label: 'Development' },
    { value: 'browsing', label: 'Browsing' },
    { value: 'communication', label: 'Communication' },
    { value: 'entertainment', label: 'Entertainment' },
    { value: 'design', label: 'Design' },
    { value: 'social', label: 'Social' },
    { value: 'documents', label: 'Documents' },
    { value: 'media', label: 'Media' },
    { value: 'system', label: 'System' },
    { value: 'other', label: 'Other' },
  ];

  const ruleTypes = [
    { value: 'substring', label: 'Contains' },
    { value: 'glob', label: 'Glob (*)' },
    { value: 'regex', label: 'Regex' },
  ];

  const targets = [
    { value: 'exe', label: 'Exe name' },
    { value: 'title', label: 'Window title' },
    { value: 'domain', label: 'Domain' },
  ];

  const API = '/api/rules';

  function catLabel(value: string): string {
    return categories.find(c => c.value === value)?.label ?? value;
  }

  async function load() {
    try {
      const r = await fetch(API);
      rules = await r.json();
      apiOk = true;
    } catch { apiOk = false; }
    try {
      const b = await fetch('http://127.0.0.1:47821/api/builtin-rules');
      const j = await b.json();
      builtinExe = j.exeRules || {};
      builtinDomains = j.domainRules || {};
    } catch { }
    try {
      const p = await fetch('http://127.0.0.1:47821/api/running-processes');
      runningProcs = await p.json();
    } catch { }
  }

  async function addRule(pattern: string = '') {
    const p = (pattern || newPattern.trim().toLowerCase());
    if (!p) return;
    try {
      const body = { pattern: p, category: newCat, ruleType: newRuleType, target: newTarget, priority: rules.length };
      await fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      await load();
      newPattern = '';
      showDropdown = false;
    } catch { apiOk = false; }
  }

  async function removeRule(pattern: string) {
    try {
      await fetch(`${API}/${encodeURIComponent(pattern)}`, { method: 'DELETE' });
      await load();
    } catch { apiOk = false; }
  }

  async function reorder() {
    const ids = rules.map(r => r.id);
    try {
      await fetch('/api/rules/reorder', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ids }),
      });
    } catch { }
  }

  function handleDragStart(e: DragEvent, idx: number) {
    dragIdx = idx;
    if (e.dataTransfer) e.dataTransfer.effectAllowed = 'move';
  }

  function handleDragOver(e: DragEvent, idx: number) {
    e.preventDefault();
    if (dragIdx === null || dragIdx === idx) return;
    const item = rules.splice(dragIdx, 1)[0];
    rules.splice(idx, 0, item);
    dragIdx = idx;
  }

  function handleDragEnd() {
    dragIdx = null;
    reorder();
  }

  async function loadSuggestions() {
    try {
      const r = await fetch('/api/running-processes');
      suggestions = await r.json();
    } catch { suggestions = []; }
  }

  function onFocus() {
    if (suggestions.length === 0) loadSuggestions();
    showDropdown = true;
    highlightIdx = -1;
  }

  function onInput() { showDropdown = true; highlightIdx = -1; }

  let filtered = $derived.by(() => {
    const q = newPattern.trim().toLowerCase();
    if (!q) {
      const list = [...suggestions];
      if ($live?.currentApp && $live.currentApp !== '—' && !list.some(r => r.toLowerCase() === $live.currentApp!.toLowerCase()))
        list.unshift($live.currentApp);
      return list;
    }
    const results = suggestions.filter(s => s.toLowerCase().includes(q));
    if ($live?.currentApp && $live.currentApp !== '—' && $live.currentApp.toLowerCase().includes(q)) {
      if (!results.some(r => r.toLowerCase() === $live.currentApp!.toLowerCase()))
        results.unshift($live.currentApp);
    }
    return results;
  });

  function selectSuggestion(exe: string) { newPattern = exe; showDropdown = false; highlightIdx = -1; }

  function onKeydown(e: KeyboardEvent) {
    if (!showDropdown || filtered.length === 0) {
      if (e.key === 'Enter') { addRule(); e.preventDefault(); }
      return;
    }
    if (e.key === 'ArrowDown') { e.preventDefault(); highlightIdx = Math.min(highlightIdx + 1, filtered.length - 1); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); highlightIdx = Math.max(highlightIdx - 1, -1); }
    else if (e.key === 'Enter') { e.preventDefault(); if (highlightIdx >= 0) selectSuggestion(filtered[highlightIdx]); else addRule(); }
    else if (e.key === 'Escape') { showDropdown = false; highlightIdx = -1; }
  }

  let focusTimeout: ReturnType<typeof setTimeout>;
  function onBlur() { focusTimeout = setTimeout(() => { showDropdown = false; highlightIdx = -1; }, 150); }
  function clearFocusTimeout() { clearTimeout(focusTimeout); }

  onMount(() => { load(); });
</script>

<div class="rules">
  {#if !apiOk}<span class="warning">Tray app not running</span>{/if}
  <div class="add-bar">
    <div class="combo-wrapper">
      <input class="input" placeholder="pattern, e.g. notion or *notion*" bind:value={newPattern}
        onfocus={onFocus} oninput={onInput} onkeydown={onKeydown} onblur={onBlur} autocomplete="off" />
      {#if showDropdown && filtered.length > 0}
        <div class="suggestions" onmousedown={clearFocusTimeout}>
          {#each filtered as exe, i}
            <button class="suggestion-item" class:highlight={i === highlightIdx} class:live={$live?.currentApp === exe} onmousedown={() => selectSuggestion(exe)} type="button">
              {#if $live?.currentApp === exe}<span class="live-dot"></span>{/if}
              <span>{exe}</span>
              {#if $live?.currentApp === exe}<span class="live-label">active</span>{/if}
            </button>
          {/each}
        </div>
      {/if}
    </div>
    <select class="select" bind:value={newRuleType}>
      {#each ruleTypes as rt}<option value={rt.value}>{rt.label}</option>{/each}
    </select>
    <select class="select" bind:value={newTarget}>
      {#each targets as t}<option value={t.value}>{t.label}</option>{/each}
    </select>
    <select class="select" bind:value={newCat}>
      {#each categories as c}<option value={c.value}>{c.label}</option>{/each}
    </select>
    <button class="add-btn" onclick={() => addRule()} disabled={!newPattern.trim()}>
      <i class="ti ti-plus"></i> Add
    </button>
  </div>

  <!-- Custom Rules -->
  <div class="card">
    <div class="card-header">Custom Rules (drag to reorder)</div>
    {#if rules.length === 0}
      <div class="empty-state"><span class="empty-text">No custom rules. Add one above.</span></div>
    {:else}
      {#each rules as rule, i}
        <div class="rule-row" class:last={i === rules.length - 1}
          draggable="true"
          ondragstart={(e) => handleDragStart(e, i)}
          ondragover={(e) => handleDragOver(e, i)}
          ondragend={handleDragEnd}
        >
          <i class="ti ti-grip-vertical drag-handle" aria-hidden="true"></i>
          <span class="color-dot" style="background:{colorForCategory(rule.category)}"></span>
          <code class="rule-pattern">{rule.pattern}</code>
          <span class="rule-meta">{ruleTypes.find(r => r.value === rule.ruleType)?.label ?? rule.ruleType}</span>
          <span class="rule-meta target">{targets.find(t => t.value === rule.target)?.label ?? rule.target}</span>
          <span class="rule-arrow">→</span>
          <span class="rule-cat">{catLabel(rule.category)}</span>
          <button class="del-btn" onclick={() => removeRule(rule.pattern)} aria-label="Remove"><i class="ti ti-x"></i></button>
        </div>
      {/each}
    {/if}
  </div>

  <!-- Built-in Rules -->
  <div class="card">
    <div class="card-header">Built-in Rules</div>
    <div class="builtin-section">
      <div class="builtin-title">Executable Rules</div>
      {#each Object.entries(builtinExe) as [exe, cat]}
        <div class="rule-row builtin">
          <span class="color-dot" style="background:{colorForCategory(cat)}"></span>
          <code class="rule-pattern">{exe}</code>
          <span class="rule-arrow">→</span>
          <span class="rule-cat">{catLabel(cat)}</span>
          <button class="override-btn" onclick={() => { newPattern = exe; newCat = cat; newRuleType = 'substring'; newTarget = 'exe'; }} title="Override">
            <i class="ti ti-pencil"></i>
          </button>
        </div>
      {/each}
    </div>
    <div class="builtin-section">
      <div class="builtin-title">Domain Rules</div>
      {#each Object.entries(builtinDomains) as [domain, cat]}
        <div class="rule-row builtin">
          <span class="color-dot" style="background:{colorForCategory(cat)}"></span>
          <code class="rule-pattern">{domain}</code>
          <span class="rule-arrow">→</span>
          <span class="rule-cat">{catLabel(cat)}</span>
          <button class="override-btn" onclick={() => { newPattern = domain; newCat = cat; newRuleType = 'substring'; newTarget = 'domain'; }} title="Override">
            <i class="ti ti-pencil"></i>
          </button>
        </div>
      {/each}
    </div>
  </div>

  <!-- Running Processes -->
  {#if runningProcs.length > 0}
    <div class="card">
      <div class="card-header">Running Apps</div>
      <div class="running-grid">
        {#each runningProcs.filter(p => !rules.some(r => r.pattern === p) && !builtinExe[p]) as proc}
          <button class="running-chip" onclick={() => { newPattern = proc; newCat = 'other'; newRuleType = 'substring'; newTarget = 'exe'; }}>
            <code>{proc}</code>
            <span class="chip-hint">assign</span>
          </button>
        {/each}
      </div>
    </div>
  {/if}
</div>

<style>
  .rules { display: flex; flex-direction: column; gap: var(--sp-4); }
  .warning {
    font-size: 12px; color: var(--md-error); font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
    width: fit-content;
  }
  .add-bar { display: flex; gap: var(--sp-2); align-items: center; flex-wrap: wrap; }
  .combo-wrapper { flex: 1; min-width: 180px; position: relative; }
  .input {
    width: 100%; background: var(--md-surface-1); border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm); padding: var(--sp-2); color: var(--md-on-surf);
    font-family: var(--font-mono); font-size: 13px; outline: none; box-sizing: border-box; height: 36px;
  }
  .input:focus { border-color: var(--md-primary); }
  .suggestions {
    position: absolute; top: 100%; left: 0; right: 0; margin-top: 4px;
    background: var(--md-surface-2); border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm); max-height: 220px; overflow-y: auto; z-index: 100;
    box-shadow: 0 8px 24px rgba(0,0,0,0.4);
  }
  .suggestion-item {
    display: flex; align-items: center; gap: var(--sp-2); width: 100%;
    padding: var(--sp-2); border: none; background: none;
    color: var(--md-on-surf); font-family: var(--font-mono); font-size: 12px;
    cursor: pointer; text-align: left;
  }
  .suggestion-item:hover,.suggestion-item.highlight { background: var(--md-surface-1); }
  .suggestion-item.live { color: var(--md-primary); }
  .live-dot { width: 7px; height: 7px; border-radius: 50%; background: var(--md-primary); flex-shrink: 0; }
  .live-label { margin-left: auto; font-family: var(--font-display); font-size: 10px; color: var(--md-on-surf-dim); font-style: italic; }
  .select {
    background: var(--md-surface-1); border: 1px solid var(--md-outline); border-radius: var(--shape-sm);
    padding: var(--sp-2); color: var(--md-on-surf); font-family: inherit; font-size: 13px;
    outline: none; cursor: pointer; box-sizing: border-box; height: 36px;
  }
  .select:focus { border-color: var(--md-primary); }
  .add-btn {
    display: flex; align-items: center; gap: var(--sp-1); padding: var(--sp-2) var(--sp-3);
    background: var(--md-primary); color: #1a1a1a; border: none; border-radius: var(--shape-sm);
    font-family: inherit; font-size: 13px; font-weight: 500; cursor: pointer; height: 36px;
  }
  .add-btn:disabled { opacity: 0.4; cursor: default; }
  .card { border: 1px solid var(--md-outline); border-radius: var(--shape-lg); background: var(--md-surface-1); overflow: hidden; }
  .card-header {
    padding: var(--sp-3) var(--sp-4); border-bottom: 1px solid var(--md-outline);
    font-size: 13px; font-weight: 500; color: var(--md-on-surf);
  }
  .empty-state { display: flex; align-items: center; justify-content: center; padding: var(--sp-6); }
  .empty-text { font-size: 13px; color: var(--md-on-surf-dim); }
  .rule-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-3) var(--sp-4); border-bottom: 1px solid var(--md-outline); font-size: 13px;
    cursor: default;
  }
  .rule-row:active { background: var(--md-surface-2); }
  .rule-row.last { border-bottom: none; }
  .rule-row.builtin { opacity: 0.75; font-size: 12px; }
  .drag-handle { color: var(--md-on-surf-dim); font-size: 14px; cursor: grab; flex-shrink: 0; }
  .drag-handle:active { cursor: grabbing; }
  .color-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
  .rule-pattern { font-family: var(--font-mono); font-size: 12px; color: var(--md-on-surf); min-width: 100px; }
  .rule-meta {
    font-size: 10px; color: var(--md-on-surf-dim); background: var(--md-surface-2);
    padding: 1px 5px; border-radius: var(--shape-sm); font-family: var(--font-mono);
    white-space: nowrap;
  }
  .rule-meta.target { background: color-mix(in srgb, var(--md-primary) 10%, transparent); color: var(--md-primary); }
  .rule-arrow { color: var(--md-primary); font-size: 12px; flex-shrink: 0; }
  .rule-cat { color: var(--md-on-surf-var); font-weight: 500; flex: 1; }
  .del-btn {
    background: none; border: none; color: var(--md-on-surf-dim);
    cursor: pointer; padding: var(--sp-1); border-radius: var(--shape-sm); font-size: 14px;
  }
  .del-btn:hover { background: color-mix(in srgb, var(--md-error) 10%, transparent); color: var(--md-error); }
  .override-btn {
    background: none; border: none; color: var(--md-on-surf-dim);
    cursor: pointer; padding: var(--sp-1); border-radius: var(--shape-sm); font-size: 13px;
  }
  .override-btn:hover { color: var(--md-primary); background: var(--md-surface-2); }
  .builtin-section { border-bottom: 1px solid var(--md-outline); }
  .builtin-section:last-child { border-bottom: none; }
  .builtin-title {
    padding: var(--sp-2) var(--sp-4); font-size: 11px; font-weight: 600;
    color: var(--md-on-surf-dim); text-transform: uppercase; letter-spacing: 0.05em;
    background: var(--md-surface-1);
  }
  .running-grid { display: flex; flex-wrap: wrap; gap: var(--sp-2); padding: var(--sp-3) var(--sp-4); }
  .running-chip {
    display: flex; align-items: center; gap: var(--sp-1);
    padding: var(--sp-1) var(--sp-2); border-radius: var(--shape-sm);
    border: 1px solid var(--md-outline); background: transparent;
    color: var(--md-on-surf-var); font-family: inherit; font-size: 12px;
    cursor: pointer; transition: all 0.15s;
  }
  .running-chip:hover { background: var(--md-primary-cont); color: var(--md-on-pri-cont); border-color: var(--md-primary); }
  .running-chip code { font-family: var(--font-mono); font-size: 11px; }
  .chip-hint { font-size: 10px; color: var(--md-on-surf-dim); font-style: italic; }
  .running-chip:hover .chip-hint { color: var(--md-on-pri-cont); }
</style>
