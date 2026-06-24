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
  </div>
  <p class="title-small desc">Map executable names to activity categories. Changes apply immediately.</p>

  {#if !apiOk}
    <div class="error-banner">Tray app not running — rules are read-only.</div>
  {/if}

  <div class="add-row">
    <input class="input" placeholder="exe name (e.g. code.exe)" bind:value={newExe} onkeydown={(e) => { if (e.key === 'Enter') addRule(); }} />
    <select class="select" bind:value={newCat}>
      {#each categories as c}
        <option value={c}>{c}</option>
      {/each}
    </select>
    <button class="add-btn" onclick={addRule} disabled={!newExe.trim()}>Add Rule</button>
  </div>

  <div class="rule-list">
    {#if rules.length === 0}
      <div class="empty-state">
        <span class="empty-text">No rules yet — add one above.</span>
      </div>
    {:else}
      {#each rules as rule}
        <div class="rule-row">
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
  .desc { color: var(--md-on-surf-var); }
  .error-banner {
    background: var(--md-err-cont);
    color: var(--md-error);
    padding: var(--sp-3) var(--sp-4);
    border-radius: var(--shape-sm);
    font-size: 13px;
    border: 1px solid rgba(224, 112, 112, 0.2);
  }
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
    color: #1a1a1a;
    border: none;
    border-radius: var(--shape-sm);
    font-family: inherit;
    font-size: 13px;
    font-weight: 500;
    cursor: pointer;
  }
  .add-btn:disabled { opacity: 0.4; cursor: default; }
  .rule-list { display: flex; flex-direction: column; border: 1px solid var(--md-outline); border-radius: var(--shape-md); overflow: hidden; min-height: 60px; }
  .empty-state {
    display: flex; align-items: center; justify-content: center;
    padding: var(--sp-6);
    flex: 1;
  }
  .empty-text { font-size: 13px; color: var(--md-on-surf-dim); }
  .rule-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-2) var(--sp-3);
    border-bottom: 1px solid var(--md-outline);
    font-size: 13px;
  }
  .rule-row:last-child { border-bottom: none; }
  .color-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
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
