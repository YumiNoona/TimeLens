<script lang="ts">
  const defaultRules: { pattern: string; category: string }[] = [
    { pattern: 'code.exe', category: 'Development' },
    { pattern: 'cursor.exe', category: 'Development' },
    { pattern: 'windsurf.exe', category: 'Development' },
    { pattern: 'chrome.exe', category: 'Browsing' },
    { pattern: 'firefox.exe', category: 'Browsing' },
    { pattern: 'msedge.exe', category: 'Browsing' },
    { pattern: 'slack.exe', category: 'Communication' },
    { pattern: 'discord.exe', category: 'Communication' },
    { pattern: 'teams.exe', category: 'Communication' },
    { pattern: 'spotify.exe', category: 'Entertainment' },
    { pattern: 'wmplayer.exe', category: 'Entertainment' },
    { pattern: 'notion.exe', category: 'Work' },
    { pattern: 'obsidian.exe', category: 'Work' },
    { pattern: 'figma.exe', category: 'Design' },
    { pattern: 'photoshop.exe', category: 'Design' },
  ];

  let rules = $state(defaultRules.map(r => ({ ...r })));
  let newExe = $state('');
  let newCat = $state('Other');
  let editingIdx = $state(-1);

  const categories = ['Work', 'Development', 'Browsing', 'Communication', 'Entertainment', 'Design', 'Social', 'Other'];

  function addRule() {
    if (!newExe.trim()) return;
    rules = [...rules, { pattern: newExe.trim().toLowerCase(), category: newCat }];
    newExe = '';
  }

  function removeRule(idx: number) {
    rules = rules.filter((_, i) => i !== idx);
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
        <option value={c}>{c}</option>
      {/each}
    </select>
    <button class="add-btn" onclick={addRule} disabled={!newExe.trim()}>Add Rule</button>
  </div>

  <div class="rule-list">
    {#each rules as rule, i}
      <div class="rule-row">
        <code class="rule-pattern">{rule.pattern}</code>
        <span class="rule-arrow">→</span>
        <span class="rule-cat">{rule.category}</span>
        <button class="del-btn" onclick={() => removeRule(i)} aria-label="Remove rule">
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
