<script lang="ts">
  let autoStart = $state(true);
  let idleThreshold = $state(3);
</script>

<div class="settings">
  <div class="topbar">
    <h1 class="headline-small">Settings</h1>
  </div>

  <div class="section">
    <h2 class="title-large">General</h2>

    <label class="setting-row">
      <div class="setting-info">
        <span class="setting-label">Launch on startup</span>
        <span class="setting-desc">Automatically start TimeLens when you log in</span>
      </div>
      <input type="checkbox" bind:checked={autoStart} class="toggle" />
    </label>

    <div class="setting-row">
      <div class="setting-info">
        <span class="setting-label">Idle threshold</span>
        <span class="setting-desc">Minutes of inactivity before considering you idle</span>
      </div>
      <select class="select" bind:value={idleThreshold}>
        {#each [1, 2, 3, 5, 10, 15] as n}
          <option value={n}>{n} min</option>
        {/each}
      </select>
    </div>
  </div>

  <div class="section">
    <h2 class="title-large">Data</h2>

    <div class="setting-row">
      <div class="setting-info">
        <span class="setting-label">Storage location</span>
        <span class="setting-desc">%LOCALAPPDATA%\TimeLens\activity.db</span>
      </div>
      <span class="badge">SQLite</span>
    </div>

    <div class="setting-row">
      <div class="setting-info">
        <span class="setting-label">Dashboard</span>
        <span class="setting-desc">http://127.0.0.1:47821/</span>
      </div>
      <span class="badge">Active</span>
    </div>

    <div class="setting-row">
      <div class="setting-info">
        <span class="setting-label">Exe size</span>
        <span class="setting-desc">TimeLens.TrayApp.exe — Native AOT</span>
      </div>
      <span class="badge">~10.5 MB</span>
    </div>
  </div>

  <div class="section">
    <h2 class="title-large">About</h2>
    <p class="about-text">
      TimeLens is a privacy-first PC activity tracker. All data is stored locally in a SQLite database.
      No data is sent to external servers. The built-in dashboard runs on a local Kestrel server bound to 127.0.0.1.
    </p>
  </div>
</div>

<style>
  .settings { display: flex; flex-direction: column; gap: var(--sp-5); max-width: 640px; }
  .topbar { display: flex; align-items: center; justify-content: space-between; }
  .section { display: flex; flex-direction: column; gap: var(--sp-2); }
  .setting-row {
    display: flex; align-items: center; justify-content: space-between;
    padding: var(--sp-2) 0;
    border-bottom: 1px solid var(--md-surface-2);
  }
  .setting-info { display: flex; flex-direction: column; gap: var(--sp-0); }
  .setting-label { font-size: 13px; font-weight: 500; color: var(--md-on-surf); }
  .setting-desc { font-size: 12px; color: var(--md-on-surf-var); }
  .toggle {
    appearance: none;
    width: 36px; height: 20px;
    background: var(--md-surface-2);
    border-radius: 99px;
    position: relative;
    cursor: pointer;
    transition: background 0.2s;
    flex-shrink: 0;
  }
  .toggle::after {
    content: '';
    position: absolute;
    width: 16px; height: 16px;
    background: var(--md-on-surf);
    border-radius: 50%;
    top: 2px; left: 2px;
    transition: transform 0.2s;
  }
  .toggle:checked { background: var(--md-primary); }
  .toggle:checked::after { transform: translateX(16px); background: var(--md-on-pri-cont); }
  .select {
    background: var(--md-surface-1);
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    padding: var(--sp-1) var(--sp-2);
    color: var(--md-on-surf);
    font-family: inherit;
    font-size: 13px;
    width: 90px;
  }
  .badge {
    padding: var(--sp-0) var(--sp-2);
    background: var(--md-primary-cont);
    color: var(--md-on-pri-cont);
    border-radius: var(--shape-sm);
    font-size: 11px;
    font-weight: 500;
  }
  .about-text { font-size: 13px; color: var(--md-on-surf-var); line-height: 1.6; }
</style>
