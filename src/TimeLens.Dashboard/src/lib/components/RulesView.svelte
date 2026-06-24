<script lang="ts">
  import { colorForCategory } from '../colors';

  let rules: { pattern: string; category: string }[] = $state([]);
  let newExe = $state('');
  let newCat = $state('Other');
  let apiOk = $state(true);

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

  async function addRule() {
    const pattern = newExe.trim().toLowerCase();
    if (!pattern) return;
    try {
      await fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ pattern, category: newCat }),
      });
      rules = [...rules, { pattern, category: newCat }];
      newExe = '';
    } catch { apiOk = false; }
  }

  async function removeRule(pattern: string) {
    try {
      await fetch(`${API}/${encodeURIComponent(pattern)}`, { method: 'DELETE' });
      rules = rules.filter(r => r.pattern !== pattern);
    } catch { apiOk = false; }
  }

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
    <input class="input" placeholder="exe name, e.g. code.exe" bind:value={newExe} onkeydown={(e) => { if (e.key === 'Enter') addRule(); }} />
    <select class="select" bind:value={newCat}>
      {#each categories as c}
        <option value={c}>{c}</option>
      {/each}
    </select>
    <button class="add-btn" onclick={addRule} disabled={!newExe.trim()}>
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
  .rules {
    display: flex;
    flex-direction: column;
    gap: var(--sp-4);
    max-width: 640px;
  }

  .topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .desc {
    color: var(--md-on-surf-var);
    margin-top: calc(-1 * var(--sp-2));
  }

  .warning {
    font-size: 12px;
    color: var(--md-error);
    font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
  }

  .add-bar {
    display: flex;
    gap: var(--sp-2);
    align-items: center;
  }

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
    box-sizing: border-box;
    height: 36px;
  }

  .input:focus { border-color: var(--md-primary); }

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
    display: flex;
    align-items: center;
    gap: var(--sp-0);
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

  .empty-state {
    display: flex;
    align-items: center;
    justify-content: center;
    padding: var(--sp-6);
  }

  .empty-text {
    font-size: 13px;
    color: var(--md-on-surf-dim);
  }

  .rule-row {
    display: flex;
    align-items: center;
    gap: var(--sp-2);
    padding: var(--sp-3) var(--sp-4);
    border-bottom: 1px solid var(--md-outline);
    font-size: 13px;
  }

  .rule-row.last { border-bottom: none; }

  .color-dot {
    width: 10px;
    height: 10px;
    border-radius: 50%;
    flex-shrink: 0;
  }

  .rule-pattern {
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--md-on-surf);
    min-width: 140px;
  }

  .rule-arrow {
    color: var(--md-primary);
    font-size: 12px;
    flex-shrink: 0;
  }

  .rule-cat {
    color: var(--md-on-surf-var);
    font-weight: 500;
    flex: 1;
  }

  .del-btn {
    background: none;
    border: none;
    color: var(--md-on-surf-dim);
    cursor: pointer;
    padding: var(--sp-1);
    border-radius: var(--shape-sm);
    font-size: 14px;
  }

  .del-btn:hover {
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    color: var(--md-error);
  }
</style>
