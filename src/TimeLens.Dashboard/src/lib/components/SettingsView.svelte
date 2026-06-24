<script lang="ts">
  let trackAudio = $state(true);
  let trackBrowser = $state(true);
  let trackInput = $state(true);
  let idleMinutes = $state(3);
  let theme = $state('moss');
  let timelineGrouped = $state(false);
  let autoStart = $state(false);
  let retentionDays = $state(90);
  let dbSizeBytes = $state(0);
  let apiReachable = $state(true);
  let savedKey = $state<string | null>(null);

  let { ontheme }: { ontheme?: (t: string) => void } = $props();

  const themes = [
    { id: 'default', label: 'Acid', color: '#C8E86A' },
    { id: 'terminal', label: 'Terminal', color: '#39FF14' },
    { id: 'moss', label: 'Moss', color: '#81C784' },
    { id: 'copper', label: 'Copper', color: '#B87333' },
    { id: 'arctic', label: 'Arctic', color: '#7EC8C8' },
    { id: 'crimson', label: 'Crimson', color: '#DC143C' },
    { id: 'gold', label: 'Gold', color: '#FFB000' },
    { id: 'ember', label: 'Ember', color: '#FF8A65' },
    { id: 'rose', label: 'Rose', color: '#F48FB1' },
    { id: 'clay', label: 'Clay', color: '#BCAAA4' },
    { id: 'sunset', label: 'Sunset', color: '#FFD54F' },
  ];

  const retentionOpts = [30, 60, 90, 180, 365];

  const API = 'http://127.0.0.1:47821/api/settings';

  function fmtSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }

  async function load(attempt = 1) {
    try {
      const r = await fetch(API);
      const s = await r.json();
      trackAudio = s.trackAudio ?? true;
      trackBrowser = s.trackBrowser ?? true;
      trackInput = s.trackInput ?? true;
      idleMinutes = Math.round((s.idleThresholdSeconds ?? 180) / 60);
      theme = s.theme ?? 'moss';
      timelineGrouped = s.timelineGrouped ?? false;
      autoStart = s.autoStart ?? false;
      retentionDays = s.retentionDays ?? 90;
      apiReachable = true;
    } catch {
      if (attempt < 3) {
        await new Promise(r => setTimeout(r, 2000));
        return load(attempt + 1);
      }
      apiReachable = false;
    }
    // Fetch DB size
    try {
      const sz = await fetch('http://127.0.0.1:47821/api/db-size');
      const j = await sz.json();
      dbSizeBytes = j.sizeBytes ?? 0;
    } catch { }
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

  function exportCsv() {
    window.open('http://127.0.0.1:47821/api/export?format=csv', '_blank');
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

  <div class="settings-grid">

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Tracking</h2>
      </div>
      <label class="setting-row">
        <div class="setting-info">
          <span class="setting-label">Audio Tracking</span>
          <span class="setting-desc">Prevent idle while playing media</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={trackAudio} onchange={() => save('trackAudio', trackAudio)} />
          {#if savedKey === 'trackAudio'}<i class="ti ti-check saved-icon"></i>{/if}
        </div>
      </label>
      <label class="setting-row">
        <div class="setting-info">
          <span class="setting-label">Browser Tracking</span>
          <span class="setting-desc">Record browsing via the browser extension</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={trackBrowser} onchange={() => save('trackBrowser', trackBrowser)} />
          {#if savedKey === 'trackBrowser'}<i class="ti ti-check saved-icon"></i>{/if}
        </div>
      </label>
      <label class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Input Tracking</span>
          <span class="setting-desc">Track keyboard and mouse activity</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={trackInput} onchange={() => save('trackInput', trackInput)} />
          {#if savedKey === 'trackInput'}<i class="ti ti-check saved-icon"></i>{/if}
        </div>
      </label>
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Startup & Idle</h2>
      </div>
      <label class="setting-row">
        <div class="setting-info">
          <span class="setting-label">Launch at login</span>
          <span class="setting-desc">Start TimeLens automatically when you sign in</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={autoStart} onchange={() => save('autoStart', autoStart)} />
          {#if savedKey === 'autoStart'}<i class="ti ti-check saved-icon"></i>{/if}
        </div>
      </label>
      <div class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Idle Threshold</span>
          <span class="setting-desc">Minutes of inactivity before considering you idle</span>
        </div>
        <div class="control">
          <select class="select" bind:value={idleMinutes} onchange={() => save('idleThresholdSeconds', idleMinutes * 60)}>
            {#each [1, 2, 3, 5, 10, 15] as n}
              <option value={n}>{n} min</option>
            {/each}
          </select>
          {#if savedKey === 'idleThresholdSeconds'}<i class="ti ti-check saved-icon"></i>{/if}
        </div>
      </div>
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Theme</h2>
      </div>
      <div class="theme-grid">
        {#each themes as t}
          <button
            class="theme-swatch"
            class:active={theme === t.id}
            onclick={() => { theme = t.id; save('theme', t.id); ontheme?.(t.id); }}
            aria-label={t.label}
          >
            <div class="swatch-bar" style="background: {t.color}"></div>
            <span class="swatch-label">{t.label}</span>
          </button>
        {/each}
      </div>
      {#if savedKey === 'theme'}<i class="ti ti-check saved-icon" style="padding: var(--sp-3) var(--sp-4)"></i>{/if}
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Timeline</h2>
      </div>
      <label class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Grouped Timeline</span>
          <span class="setting-desc">Collapse same-category runs. Turn off to show every event flat.</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={timelineGrouped}
            onchange={() => save('timelineGrouped', timelineGrouped)} />
          {#if savedKey === 'timelineGrouped'}<i class="ti ti-check saved-icon"></i>{/if}
        </div>
      </label>
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Data</h2>
      </div>
      <div class="setting-row">
        <div class="setting-info">
          <span class="setting-label">Retention</span>
          <span class="setting-desc">Auto-delete events older than</span>
        </div>
        <div class="control">
          <select class="select" style="width:110px" bind:value={retentionDays} onchange={() => save('retentionDays', retentionDays)}>
            {#each retentionOpts as n}
              <option value={n}>{n} days</option>
            {/each}
          </select>
          {#if savedKey === 'retentionDays'}<i class="ti ti-check saved-icon"></i>{/if}
        </div>
      </div>
      <div class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Export</span>
          <span class="setting-desc">Download today's activity as CSV</span>
        </div>
        <div class="control">
          <button class="export-btn" onclick={exportCsv}>
            <i class="ti ti-download" aria-hidden="true"></i> CSV
          </button>
        </div>
      </div>
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Storage</h2>
      </div>
      <div class="setting-row">
        <div class="setting-info">
          <span class="setting-label">Database</span>
          <span class="setting-desc">%LOCALAPPDATA%\TimeLens\activity.db</span>
        </div>
        <code class="path">{fmtSize(dbSizeBytes)}</code>
      </div>
      <div class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Dashboard</span>
          <span class="setting-desc">Local server, no data leaves your machine</span>
        </div>
        <code class="path">http://127.0.0.1:47821</code>
      </div>
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">About</h2>
      </div>
      <div class="about-body">
        <p class="about-text">TimeLens is a privacy-first PC activity tracker. All data is stored locally in a SQLite database. No data is sent to external servers. The built-in dashboard runs on a local Kestrel server bound to 127.0.0.1.</p>
      </div>
    </div>

  </div>
</div>

<style>
  .settings {
    display: flex;
    flex-direction: column;
    gap: var(--sp-4);
    max-width: 860px;
  }

  .settings-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--sp-4);
  }

  .card:last-child { grid-column: 1 / -1; }

  .topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .warning {
    font-size: 12px;
    color: var(--md-error);
    font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
  }

  .card {
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-md);
    overflow: hidden;
  }

  .card-header {
    padding: var(--sp-3) var(--sp-4);
    border-bottom: 1px solid var(--md-outline);
  }

  .card-header h2 { margin: 0; }

  .setting-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--sp-3) var(--sp-4);
    border-bottom: 1px solid var(--md-outline);
  }

  .setting-row.last { border-bottom: none; }

  .setting-info {
    display: flex;
    flex-direction: column;
    gap: 2px;
    min-width: 0;
  }

  .setting-label {
    font-size: 13px;
    font-weight: 500;
    color: var(--md-on-surf);
  }

  .setting-desc {
    font-size: 12px;
    color: var(--md-on-surf-var);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .control {
    display: flex;
    align-items: center;
    gap: var(--sp-1);
    flex-shrink: 0;
  }

  .saved-icon {
    font-size: 14px;
    color: var(--md-primary);
  }

  .toggle {
    appearance: none;
    width: 40px;
    height: 22px;
    background: var(--md-outline);
    border-radius: 99px;
    position: relative;
    cursor: pointer;
    transition: background 0.2s ease;
    flex-shrink: 0;
    margin: 0;
  }

  .toggle::after {
    content: '';
    position: absolute;
    width: 18px;
    height: 18px;
    background: #fff;
    border-radius: 50%;
    top: 2px;
    left: 2px;
    transition: transform 0.2s ease, box-shadow 0.2s ease;
    box-shadow: 0 1px 3px rgba(0,0,0,0.3);
  }

  .toggle:checked { background: var(--md-primary); }

  .toggle:checked::after { transform: translateX(18px); }

  .select {
    background: var(--md-surface-1);
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    padding: var(--sp-1) var(--sp-2);
    color: var(--md-on-surf);
    font-family: inherit;
    font-size: 13px;
    width: 90px;
    outline: none;
    cursor: pointer;
  }

  .select:focus { border-color: var(--md-primary); }

  .export-btn {
    display: flex;
    align-items: center;
    gap: var(--sp-1);
    padding: var(--sp-1) var(--sp-2);
    background: var(--md-primary-cont);
    color: var(--md-on-pri-cont);
    border: 1px solid var(--md-primary);
    border-radius: var(--shape-sm);
    font-family: inherit;
    font-size: 12px;
    font-weight: 500;
    cursor: pointer;
  }

  .export-btn:hover { filter: brightness(1.1); }

  .export-btn i { font-size: 14px; }

  .path {
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--md-on-surf-var);
    background: var(--md-surface-1);
    padding: var(--sp-1) var(--sp-2);
    border-radius: var(--shape-sm);
    white-space: nowrap;
  }

  .about-body { padding: var(--sp-3) var(--sp-4); }

  .about-text {
    font-size: 13px;
    color: var(--md-on-surf-var);
    line-height: 1.6;
    margin: 0;
  }

  .theme-grid {
    display: flex;
    gap: var(--sp-2);
    padding: var(--sp-3) var(--sp-4);
    flex-wrap: wrap;
  }

  .theme-swatch {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: var(--sp-1);
    padding: var(--sp-2);
    background: var(--md-surface-1);
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    cursor: pointer;
    font-family: inherit;
    color: var(--md-on-surf-var);
    transition: border-color 0.15s, background 0.15s;
    min-width: 64px;
  }

  .theme-swatch:hover { background: var(--md-surface-2); }

  .theme-swatch.active {
    border-color: var(--md-primary);
    background: var(--md-primary-cont);
    color: var(--md-on-pri-cont);
  }

  .swatch-bar {
    width: 32px;
    height: 32px;
    border-radius: 50%;
    flex-shrink: 0;
  }

  .swatch-label {
    font-size: 10px;
    font-weight: 500;
    letter-spacing: 0.03em;
  }

  @media (max-width: 700px) {
    .settings-grid { grid-template-columns: 1fr; }
  }
</style>
