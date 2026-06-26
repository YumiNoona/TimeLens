<script lang="ts">
  import { timeFormat as timeFormatStore } from '../stores/settings';
  let trackAudio = $state(true);
  let trackBrowser = $state(true);
  let trackInput = $state(true);
  let idleMinutes = $state(3);
  let theme = $state('default');
  let timelineGrouped = $state(false);
  let autoStart = $state(false);
  let retentionDays = $state(90);
  let dbSizeBytes = $state(0);
  let showTitles = $state(false);
  let breakReminder = $state(false);
  let breakInterval = $state(50);
  let focusMode = $state(false);
  let timeFormat = $state('12h');
  let pollInterval = $state(30);
  let apiReachable = $state(true);
  let goals: { id: number; goalType: string; target: string; thresholdMinutes: number; notifyAt: number }[] = $state([]);
  let goalTarget = $state('');
  let goalType = $state('max_time');
  let goalMinutes = $state(60);

  let { ontheme }: { ontheme?: (t: string) => void } = $props();

  const themes = [
    { id: 'default', label: 'Acid', color: '#C8E86A' },
    { id: 'terminal', label: 'Terminal', color: '#39FF14' },
    { id: 'copper', label: 'Copper', color: '#B87333' },
    { id: 'arctic', label: 'Arctic', color: '#7EC8C8' },
  ];

  const retentionOpts = [30, 60, 90, 180, 365];

  const API = '/api/settings';

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
      theme = s.theme ?? 'default';
      timelineGrouped = s.timelineGrouped ?? true;
      autoStart = s.autoStart ?? false;
      retentionDays = s.retentionDays ?? 90;
      showTitles = s.showTitles ?? false;
      breakReminder = s.breakReminder ?? false;
      breakInterval = s.breakIntervalMinutes ?? 50;
      focusMode = s.focusMode ?? false;
      timeFormat = s.timeFormat ?? '12h';
      timeFormatStore.set(s.timeFormat === '24h' ? '24h' : '12h');
      pollInterval = s.pollIntervalSeconds ?? 30;
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
      const sz = await fetch('/api/db-size');
      const j = await sz.json();
      dbSizeBytes = j.sizeBytes ?? 0;
    } catch { }
    // Load goals
    try {
      const gr = await fetch('/api/goals');
      goals = await gr.json();
    } catch { }
  }

  async function save(key: string, value: boolean | number) {
    try {
      await fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ [key]: value }),
      });} catch {
      apiReachable = false;
    }
  }

  function exportCsv(range: string = 'today') {
    window.open(`/api/export?format=csv&range=${range}`, '_blank');
  }

  async function addGoal() {
    const t = goalTarget.trim();
    if (!t) return;
    try {
      await fetch('/api/goals', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ goalType, target: t, thresholdMinutes: goalMinutes, notifyAt: 80 }),
      });
      await load();
      goalTarget = '';
    } catch { apiReachable = false; }
  }

  async function removeGoal(id: number) {
    try {
      await fetch(`/api/goals/${id}`, { method: 'DELETE' });
      await load();
    } catch { apiReachable = false; }
  }

  $effect(() => { load(); });
</script>

<div class="settings">
    {#if !apiReachable}
      <span class="warning">Tray app not running</span>
    {/if}
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
          <input type="checkbox" class="toggle" checked={trackAudio} onchange={(e) => save('trackAudio', (e.currentTarget as HTMLInputElement).checked)} />
                  </div>
      </label>
      <label class="setting-row">
        <div class="setting-info">
          <span class="setting-label">Browser Tracking</span>
          <span class="setting-desc">Record browsing via the browser extension</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={trackBrowser} onchange={(e) => save('trackBrowser', (e.currentTarget as HTMLInputElement).checked)} />
        </div>
      </label>
      <label class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Input Tracking</span>
          <span class="setting-desc">Track keyboard and mouse activity</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={trackInput} onchange={() => save('trackInput', trackInput)} />
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
        </div>
      </div>
    </div>

    <div class="card card-theme">
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
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Timeline</h2>
      </div>
      <label class="setting-row">
        <div class="setting-info">
          <span class="setting-label">{timelineGrouped ? 'Grouped' : 'Flat'}</span>
          <span class="setting-desc">Collapse same-category runs. Turn off to show every event flat.</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={timelineGrouped}
            onchange={(e) => save('timelineGrouped', (e.currentTarget as HTMLInputElement).checked)} />
        </div>
      </label>
      <label class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Window Titles</span>
          <span class="setting-desc">Show active tab/window title per event in Timeline View</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={showTitles}
            onchange={() => save('showTitles', showTitles)} />
        </div>
      </label>
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Format</h2>
      </div>
      <div class="setting-row">
        <div class="setting-info">
          <span class="setting-label">Time Format</span>
          <span class="setting-desc">Show timestamps in 12-hour or 24-hour notation</span>
        </div>
        <div class="control">
          <select class="select" style="width:90px" bind:value={timeFormat} onchange={() => { save('timeFormat', timeFormat); timeFormatStore.set(timeFormat === '24h' ? '24h' : '12h'); }}>
            <option value="12h">12h</option>
            <option value="24h">24h</option>
          </select>
        </div>
      </div>
      <div class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Poll Interval</span>
          <span class="setting-desc">How often the dashboard refreshes data</span>
        </div>
        <div class="control">
          <select class="select" style="width:110px" bind:value={pollInterval} onchange={() => save('pollIntervalSeconds', pollInterval)}>
            {#each [5, 10, 30, 60] as n}
              <option value={n}>{n} seconds</option>
            {/each}
          </select>
        </div>
      </div>
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Break Reminder</h2>
      </div>
      <label class="setting-row">
        <div class="setting-info">
          <span class="setting-label">Remind me to take breaks</span>
          <span class="setting-desc">Show a notification after continuous active time</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={breakReminder}
            onchange={() => save('breakReminder', breakReminder)} />
        </div>
      </label>
      <div class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Break interval</span>
          <span class="setting-desc">Minutes of activity before a reminder</span>
        </div>
        <div class="control">
          <select class="select" style="width:110px" bind:value={breakInterval} onchange={() => save('breakIntervalMinutes', breakInterval)}>
            {#each [25, 30, 45, 50, 60, 90] as n}
              <option value={n}>{n} min</option>
            {/each}
          </select>
        </div>
      </div>
    </div>

    <div class="card">
      <div class="card-header">
        <h2 class="title-small">Focus Mode</h2>
      </div>
      <label class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Block distracting apps</span>
          <span class="setting-desc">Manage your blocklist in the Block tab</span>
        </div>
        <div class="control">
          <input type="checkbox" class="toggle" checked={focusMode}
            onchange={() => save('focusMode', focusMode)} />
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
        </div>
      </div>
      <div class="setting-row last">
        <div class="setting-info">
          <span class="setting-label">Export</span>
          <span class="setting-desc">Download activity data as CSV</span>
        </div>
        <div class="control" style="gap:var(--sp-2)">
          <button class="export-btn" onclick={() => exportCsv('today')}>
            <i class="ti ti-download" aria-hidden="true"></i> Today
          </button>
          <button class="export-btn" onclick={() => exportCsv('30days')}>
            <i class="ti ti-download" aria-hidden="true"></i> 30 days
          </button>
          <button class="export-btn" onclick={() => { let d = prompt('Enter date (YYYY-MM-DD):'); if (d) exportCsv(d); }}>
            <i class="ti ti-calendar" aria-hidden="true"></i> Pick
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
        <code class="path"></code>
      </div>
    </div>

    <div class="card card-goals">
      <div class="card-header">
        <h2 class="title-small">Goals</h2>
      </div>
      <div class="card-body">
        {#if goals.length > 0}
          {#each goals as g, i}
            <div class="setting-row" class:last={i === goals.length - 1 && !goals[i+1]}>
              <div class="setting-info">
                <span class="setting-label">
                  {g.goalType === 'max_time' ? 'Limit' : 'Minimum'} · {g.target}
                </span>
                <span class="setting-desc">{g.thresholdMinutes} min — alert at {g.notifyAt}%</span>
              </div>
              <div class="control">
                <button class="del-btn" onclick={() => removeGoal(g.id)} aria-label="Remove">
                  <i class="ti ti-trash"></i>
                </button>
              </div>
            </div>
          {/each}
        {:else}
          <div class="section-row">
            <div class="empty-state"><span class="empty-text">No goals yet. Set a time limit for any app or category.</span></div>
          </div>
        {/if}
        <div class="section-row">
          <div class="setting-info">
            <span class="setting-label">New goal</span>
            <div class="goal-fields">
              <input class="mini-input" placeholder="app or category" bind:value={goalTarget} />
              <select class="select mini" bind:value={goalType}>
                <option value="max_time">max</option>
                <option value="min_time">min</option>
              </select>
              <select class="select mini" bind:value={goalMinutes}>
                {#each [15, 30, 60, 90, 120, 180, 240] as n}
                  <option value={n}>{n}m</option>
                {/each}
              </select>
            </div>
          </div>
          <div class="control">
            <button class="export-btn" onclick={addGoal} disabled={!goalTarget.trim()}>
              <i class="ti ti-plus"></i> Add
            </button>
          </div>
        </div>
      </div>
    </div>

    <div class="card card-about">
      <div class="card-header">
        <h2 class="title-small">About</h2>
      </div>
      <div class="about-body">
        <p class="about-text">TimeLens is a privacy-first PC activity tracker. All data is stored locally in a SQLite database. No data is sent to external servers. The built-in dashboard runs on a local Kestrel server bound to 127.0.0.1.</p>
      </div>
    </div>

</div>

<style>
  .settings {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--sp-4);
    align-items: start;
  }

  .card-theme,
  .card-about { grid-column: 1 / -1; }

  .warning {
    font-size: 12px;
    color: var(--md-error);
    font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
    width: fit-content;
    grid-column: 1 / -1;
  }

  .card {
    border: 1px solid var(--clr-border);
    border-radius: var(--shape-md);
    background: var(--clr-bg-sec);
    overflow: hidden;
  }

  .card-header {
    padding: 16px;
    border-bottom: 1px solid var(--clr-border);
  }

  .card-header h2 { margin: 0; }

  .setting-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 16px;
    border-bottom: 1px solid var(--clr-border);
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
    color: var(--clr-text-pri);
  }

  .setting-desc {
    font-size: 12px;
    color: var(--clr-text-sec);
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

  .toggle {
    appearance: none;
    width: 40px;
    height: 22px;
    background: var(--clr-border);
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
    background: var(--clr-bg-sec);
    border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm);
    padding: var(--sp-1) var(--sp-2);
    color: var(--clr-text-pri);
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
    color: var(--clr-text-sec);
    background: var(--clr-bg-sec);
    padding: var(--sp-1) var(--sp-2);
    border-radius: var(--shape-sm);
    white-space: nowrap;
  }

  .about-body { padding: var(--sp-3) var(--sp-4); }

  .about-text {
    font-size: 13px;
    color: var(--clr-text-sec);
    line-height: 1.6;
    margin: 0;
  }

  .theme-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(72px, 1fr));
    gap: var(--sp-2);
    padding: var(--sp-3) var(--sp-4);
  }

  .theme-swatch {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: var(--sp-1);
    padding: var(--sp-2);
    background: var(--clr-bg-sec);
    border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm);
    cursor: pointer;
    font-family: inherit;
    color: var(--clr-text-sec);
    transition: border-color 0.15s, background 0.15s;
    min-width: 64px;
  }

  .theme-swatch:hover { background: var(--clr-bg-ter); }

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
    .settings { grid-template-columns: 1fr; }
  }

  .card-goals {
    grid-column: 1 / -1;
    display: flex;
    flex-direction: column;
    min-height: 0;
    overflow: visible;
  }

  .card-body {
    display: flex;
    flex-direction: column;
  }

  .section-row {
    border-top: 1px solid var(--clr-border);
    padding: var(--sp-3) var(--sp-4);
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .empty-state {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 100%;
  }

  .goal-fields {
    display: flex; gap: var(--sp-2); align-items: center; margin-top: var(--sp-2);
  }

  .mini-input {
    background: var(--clr-bg-sec); border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm); padding: 6px 8px; color: var(--clr-text-pri);
    font-family: var(--font-mono); font-size: 12px; outline: none;
    width: 150px; box-sizing: border-box;
  }
  .mini-input:focus { border-color: var(--md-primary); }

  .select.mini { height: 32px; font-size: 12px; padding: 4px 6px; }
</style>
