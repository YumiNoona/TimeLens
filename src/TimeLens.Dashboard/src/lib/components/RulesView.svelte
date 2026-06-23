<script lang="ts">
  import { onMount } from 'svelte';

  let rules = $state<{ id: number; pattern: string; category: string }[]>([]);
  let newExe = $state('');
  let newCat = $state('Other');

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
    { value: 'other', label: 'Other' },
  ];

  function catLabel(value: string): string {
    return categories.find(c => c.value === value)?.label ?? value;
  }

  async function loadRules() {
    try {
      const res = await fetch('/api/rules');
      if (res.ok) {
        const data: { id: number; pattern: string; category: string }[] = await res.json();
        rules = data.map(r => ({ id: r.id, pattern: r.pattern, category: r.category }));
      }
    } catch { /* offline */ }
  }

  onMount(loadRules);

  async function addRule() {
    if (!newExe.trim()) return;
    try {
      await fetch('/api/rules', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ pattern: newExe.trim().toLowerCase(), category: newCat }),
      });
      newExe = '';
      await loadRules();
    } catch { /* offline */ }
  }

  async function removeRule(id: number) {
    try {
      await fetch(`/api/rules/${id}`, { method: 'DELETE' });
      await loadRules();
    } catch { /* offline */ }
  }
</script>

<div class="rules">
  <div class="topbar">
    <h1 class="headline-small">Rules</h1>
  </div>
  <p class="title-small desc">Map executable names to activity categories. Changes apply on next foreground switch.</p>

  <div class="add-row">
    <input class="input" placeholder="exe name (e.g. code.exe)" bind:value={newExe} onkeydown={(e) => { if (e.key === 'Enter') addRule(); }} />
    <select class="select" bind:value={newCat}>
      {#each categories as c}
        <option value={c.value}>{c.label}</option>
      {/each}
    </select>
    <button class="add-btn" onclick={addRule} disabled={!newExe.trim()}>Add Rule</button>
  </div>

  <div class="rule-list">
    {#each rules as rule}
      <div class="rule-row">
        <code class="rule-pattern">{rule.pattern}</code>
        <span class="rule-arrow">→</span>
        <span class="rule-cat">{catLabel(rule.category)}</span>
        <button class="del-btn" onclick={() => removeRule(rule.id)} aria-label="Remove rule">
          <i class="ti ti-x" aria-hidden="true"></i>
        </button>
      </div>
    {/each}
  </div>
</div>

<style>
  .rules { display: flex; flex-direction: column; gap: var(--sp-4); max-width: 640px; }
  .topbar { display: flex; align-items: center; justify-content: space-between; }
  .desc { color: var(--md-on-surf-var); }
  .add-row { display: flex; gap: var(--sp-2); align-items: center; }
  .input {
    flex: 1;
    background: var(--md-surface-1);
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    padding: var(--sp-2);
    color: var(--md-on-surf);
    font-family: var(--font-mono);
    font-size: 13px;
    outline: none;
  }
  .input:focus { border-color: var(--md-primary); }
  .select {
    background: var(--md-surface-1);
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    padding: var(--sp-1) var(--sp-2);
    color: var(--md-on-surf);
    font-family: inherit;
    font-size: 13px;
    outline: none;
  }
  .add-btn {
    padding: var(--sp-1) var(--sp-3);
    background: var(--md-primary);
    color: var(--md-on-pri-cont);
    border: none;
    border-radius: var(--shape-sm);
    font-family: inherit;
    font-size: 13px;
    font-weight: 500;
    cursor: pointer;
  }
  .add-btn:disabled { opacity: 0.4; cursor: default; }
  .rule-list { display: flex; flex-direction: column; border: 1px solid var(--md-outline); border-radius: var(--shape-md); overflow: hidden; }
  .rule-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-2) var(--sp-3);
    border-bottom: 1px solid var(--md-outline);
    font-size: 13px;
  }
  .rule-row:last-child { border-bottom: none; }
  .rule-pattern { font-family: var(--font-mono); font-size: 12px; color: var(--md-on-surf); width: 160px; }
  .rule-arrow { color: var(--md-primary); font-size: 12px; }
  .rule-cat { color: var(--md-on-surf-var); font-weight: 500; flex: 1; }
  .del-btn {
    background: none; border: none;
    color: var(--md-on-surf-dim);
    cursor: pointer;
    padding: var(--sp-1);
    border-radius: var(--shape-sm);
  }
  .del-btn:hover { background: rgba(224,112,112,0.1); color: var(--md-error); }
</style>
