<script lang="ts">
  let trackAudio = $state(true);
  let trackBrowser = $state(true);
  let trackInput = $state(true);
  let idleMinutes = $state(3);
  let apiReachable = $state(true);
  let savedKey = $state<string | null>(null);

  const API = 'http://127.0.0.1:47821/api/settings';

  async function load() {
    try {
      const r = await fetch(API);
      const s = await r.json();
      trackAudio = s.trackAudio ?? true;
      trackBrowser = s.trackBrowser ?? true;
      trackInput = s.trackInput ?? true;
      idleMinutes = Math.round((s.idleThresholdSeconds ?? 180) / 60);
      apiReachable = true;
    } catch {
      apiReachable = false;
    }
  }

  async function save(key: string, value: boolean | number) {
    try {
      await fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ [key]: value }),
      });
      savedKey = key;
      setTimeout(() => { savedKey = null; }, 1500);
    } catch {
      apiReachable = false;
    }
  }

  $effect(() => { load(); });
</script>

<div class="settings">
  <div class="topbar">
    <h1 class="headline-small">Settings</h1>
    {#if !apiReachable}
      <span class="warning">Tray app not running</span>
    {/if}
  </div>

  <div class="section">
    <h2 class="title-large">Tracking Toggles</h2>

    <label class="setting-row">
      <div class="setting-info">
        <span class="setting-label">Audio Tracking</span>
        <span class="setting-desc">Monitor audio output to prevent idle while playing media</span>
      </div>
      <div class="control">
        <input type="checkbox" class="toggle" checked={trackAudio}
          onchange={() => save('trackAudio', trackAudio)} />
        {#if savedKey === 'trackAudio'}<span class="saved">Saved</span>{/if}
      </div>
    </label>

    <label class="setting-row">
      <div class="setting-info">
        <span class="setting-label">Browser Tracking</span>
        <span class="setting-desc">Record browsing history via the browser extension</span>
      </div>
      <div class="control">
        <input type="checkbox" class="toggle" checked={trackBrowser}
          onchange={() => save('trackBrowser', trackBrowser)} />
        {#if savedKey === 'trackBrowser'}<span class="saved">Saved</span>{/if}
      </div>
    </label>

    <label class="setting-row">
      <div class="setting-info">
        <span class="setting-label">Input / Keystroke Tracking</span>
        <span class="setting-desc">Track keyboard and mouse activity</span>
      </div>
      <div class="control">
        <input type="checkbox" class="toggle" checked={trackInput}
          onchange={() => save('trackInput', trackInput)} />
        {#if savedKey === 'trackInput'}<span class="saved">Saved</span>{/if}
      </div>
    </label>

    <div class="setting-row">
      <div class="setting-info">
        <span class="setting-label">Idle Threshold</span>
        <span class="setting-desc">Minutes of inactivity before considering you idle</span>
      </div>
      <div class="control">
        <select class="select" bind:value={idleMinutes}
          onchange={() => save('idleThresholdSeconds', idleMinutes * 60)}>
          {#each [1, 2, 3, 5, 10, 15] as n}
            <option value={n}>{n} min</option>
          {/each}
        </select>
        {#if savedKey === 'idleThresholdSeconds'}<span class="saved">Saved</span>{/if}
      </div>
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
  .warning { font-size: 12px; color: var(--md-error); font-weight: 500; }
  .section { display: flex; flex-direction: column; gap: var(--sp-2); }
  .setting-row {
    display: flex; align-items: center; justify-content: space-between;
    padding: var(--sp-2) 0;
    border-bottom: 1px solid var(--md-surface-2);
  }
  .setting-info { display: flex; flex-direction: column; gap: var(--sp-0); }
  .setting-label { font-size: 13px; font-weight: 500; color: var(--md-on-surf); }
  .setting-desc { font-size: 12px; color: var(--md-on-surf-var); }
  .control { display: flex; align-items: center; gap: var(--sp-1); flex-shrink: 0; }
  .saved { font-size: 11px; color: var(--md-primary); font-weight: 500; }
  .toggle {
    appearance: none;
    width: 36px; height: 20px;
    background: var(--md-outline);
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
    background: #fff;
    border-radius: 50%;
    top: 2px; left: 2px;
    transition: transform 0.2s;
  }
  .toggle:checked { background: var(--md-primary); }
  .toggle:checked::after { transform: translateX(16px); }
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
