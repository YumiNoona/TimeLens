<script lang="ts">
  import { onMount } from 'svelte';
  import { resetCardLayouts } from '../actions/reorderable';
  import {
    heatmapDays as heatmapDaysStore,
    timeFormat as timeFormatStore,
    timelineMinSegmentSeconds as timelineMinSegmentSecondsStore
  } from '../stores/settings';

  let {
    ontheme,
    ondensity,
    onmotion,
    ontimelinegrouped,
    onshowtitles,
    onpollinterval
  }: {
    ontheme?: (theme: string) => void;
    ondensity?: (density: string) => void;
    onmotion?: (enabled: boolean) => void;
    ontimelinegrouped?: (grouped: boolean) => void;
    onshowtitles?: (enabled: boolean) => void;
    onpollinterval?: (seconds: number) => void;
  } = $props();

  let trackAudio = $state(true);
  let trackBrowser = $state(true);
  let trackInput = $state(true);
  let idleMinutes = $state(3);
  let theme = $state('default');
  let timelineGrouped = $state(true);
  let autoStart = $state(false);
  let retentionDays = $state(90);
  let dbSizeBytes = $state(0);
  let showTitles = $state(false);
  let breakReminder = $state(false);
  let breakInterval = $state(50);
  let focusMode = $state(false);
  let timeFormat = $state('12h');
  let pollInterval = $state(30);
  let defaultView = $state('today');
  let density = $state('comfortable');
  let motionEnabled = $state(true);
  let timelineMinSegmentSeconds = $state(60);
  let heatmapDays = $state(273);
  let blockProtectionEnabled = $state(false);
  let protectionCurrentPassword = $state('');
  let protectionNewPassword = $state('');
  let protectionConfirmPassword = $state('');
  let protectionMessage = $state('');
  let protectionError = $state('');
  let protectionBusy = $state(false);
  let apiReachable = $state(true);
  let savingKey = $state('');
  let saveMessage = $state('');
  let goals: { id: number; goalType: string; target: string; thresholdMinutes: number; notifyAt: number }[] = $state([]);
  let goalTarget = $state('');
  let goalType = $state('max_time');
  let goalMinutes = $state(60);
  let layoutTarget = $state('today');
  type UpdateStatus = {
    currentVersion: string;
    latestVersion?: string;
    updateAvailable: boolean;
    restarting: boolean;
    message: string;
    error?: string;
  };
  let updateStatus: UpdateStatus = $state({
    currentVersion: __APP_VERSION__,
    updateAvailable: false,
    restarting: false,
    message: 'Check for a newer production release.'
  });
  let updateBusy = $state(false);

  const API = '/api/settings';
  const themes = [
    { id: 'default', label: 'Acid', color: '#C8E86A' },
    { id: 'terminal', label: 'Terminal', color: '#39FF14' },
    { id: 'copper', label: 'Copper', color: '#B87333' },
    { id: 'arctic', label: 'Arctic', color: '#7EC8C8' },
    { id: 'moss', label: 'Moss', color: '#8CB84A' },
    { id: 'crimson', label: 'Crimson', color: '#E07070' },
    { id: 'gold', label: 'Gold', color: '#F0C040' },
    { id: 'ember', label: 'Ember', color: '#F08050' },
    { id: 'rose', label: 'Rose', color: '#F080A0' },
    { id: 'clay', label: 'Clay', color: '#C8A080' },
    { id: 'sunset', label: 'Sunset', color: '#F0A060' },
  ];
  const views = [
    { id: 'today', label: 'Today' },
    { id: 'history', label: 'History' },
    { id: 'apps', label: 'Apps' },
    { id: 'browser', label: 'Browser' },
    { id: 'timeline', label: 'Timeline' },
    { id: 'block', label: 'Block' },
    { id: 'rules', label: 'Rules' },
    { id: 'settings', label: 'Settings' },
  ];
  const layoutTargets = [
    { id: 'today', label: 'Today' },
    { id: 'history', label: 'History' },
  ];

  function fmtSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
  }

  async function load(attempt = 1) {
    try {
      const response = await fetch(API);
      if (!response.ok) throw new Error();
      const s = await response.json();
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
      pollInterval = s.pollIntervalSeconds ?? 30;
      defaultView = s.defaultView ?? 'today';
      density = s.density ?? 'comfortable';
      motionEnabled = s.motionEnabled ?? true;
      timelineMinSegmentSeconds = s.timelineMinSegmentSeconds ?? 60;
      heatmapDays = s.heatmapDays ?? 273;
      blockProtectionEnabled = s.blockProtectionEnabled ?? false;
      timeFormatStore.set(timeFormat === '24h' ? '24h' : '12h');
      timelineMinSegmentSecondsStore.set(timelineMinSegmentSeconds);
      heatmapDaysStore.set(heatmapDays);
      apiReachable = true;
    } catch {
      if (attempt < 3) {
        await new Promise(resolve => setTimeout(resolve, 1000));
        return load(attempt + 1);
      }
      apiReachable = false;
    }

    try {
      const response = await fetch('/api/db-size');
      dbSizeBytes = (await response.json()).sizeBytes ?? 0;
    } catch { }
    try {
      const response = await fetch('/api/goals');
      goals = await response.json();
    } catch { }
  }

  async function save(key: string, value: boolean | number | string) {
    savingKey = key;
    saveMessage = '';
    try {
      const response = await fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ [key]: value }),
      });
      if (!response.ok) throw new Error();
      apiReachable = true;
      saveMessage = 'Saved';
      setTimeout(() => { if (saveMessage === 'Saved') saveMessage = ''; }, 1200);
    } catch {
      apiReachable = false;
      saveMessage = 'Could not save';
    } finally {
      savingKey = '';
    }
  }

  function setToggle(key: string, event: Event, update: (value: boolean) => void) {
    const value = (event.currentTarget as HTMLInputElement).checked;
    update(value);
    void save(key, value);
  }

  function exportCsv(range: string) {
    window.open(`/api/export?format=csv&range=${range}`, '_blank');
  }

  async function addGoal() {
    const target = goalTarget.trim();
    if (!target) return;
    try {
      const response = await fetch('/api/goals', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ goalType, target, thresholdMinutes: goalMinutes, notifyAt: 80 }),
      });
      if (!response.ok) throw new Error();
      goalTarget = '';
      await load();
    } catch { apiReachable = false; }
  }

  async function removeGoal(id: number) {
    try {
      const response = await fetch(`/api/goals/${id}`, { method: 'DELETE' });
      if (!response.ok) throw new Error();
      await load();
    } catch { apiReachable = false; }
  }

  async function checkUpdates() {
    updateBusy = true;
    try {
      const response = await fetch('/api/update/status', { cache: 'no-store' });
      if (!response.ok) throw new Error();
      updateStatus = await response.json();
    } catch {
      updateStatus = {
        currentVersion: __APP_VERSION__,
        updateAvailable: false,
        restarting: false,
        message: 'TimeLens could not check for updates.',
        error: 'The update service is unavailable. Try again later.'
      };
    } finally {
      updateBusy = false;
    }
  }

  async function installUpdate() {
    updateBusy = true;
    try {
      const response = await fetch('/api/update/install', {
        method: 'POST',
        headers: { 'X-TimeLens-Update': 'install' }
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || 'Update failed');
      updateStatus = result;
    } catch (error) {
      updateStatus = {
        ...updateStatus,
        restarting: false,
        error: error instanceof Error ? error.message : 'The update could not be installed.'
      };
    } finally {
      if (!updateStatus.restarting) updateBusy = false;
    }
  }

  function clearProtectionFields() {
    protectionCurrentPassword = '';
    protectionNewPassword = '';
    protectionConfirmPassword = '';
  }

  async function protectionRequest(path: string, body: Record<string, string>) {
    protectionBusy = true;
    protectionMessage = '';
    protectionError = '';
    try {
      const response = await fetch(`/api/block/protection/${path}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || 'Could not update block protection');
      clearProtectionFields();
      await load();
      return true;
    } catch (error) {
      protectionError = error instanceof Error ? error.message : 'Could not update block protection';
      return false;
    } finally { protectionBusy = false; }
  }

  async function enableProtection() {
    if (protectionNewPassword !== protectionConfirmPassword) {
      protectionError = 'Passwords do not match';
      return;
    }
    if (await protectionRequest('setup', { password: protectionNewPassword }))
      protectionMessage = 'Password protection enabled';
  }

  async function changeProtectionPassword() {
    if (protectionNewPassword !== protectionConfirmPassword) {
      protectionError = 'New passwords do not match';
      return;
    }
    if (await protectionRequest('change', { currentPassword: protectionCurrentPassword, newPassword: protectionNewPassword }))
      protectionMessage = 'Block password changed';
  }

  async function disableProtection() {
    if (await protectionRequest('disable', { password: protectionCurrentPassword }))
      protectionMessage = 'Password protection disabled';
  }

  onMount(() => { void load(); void checkUpdates(); });
</script>

<div class="settings">
  {#if !apiReachable}
    <div class="warning" role="alert"><i class="ti ti-plug-off" aria-hidden="true"></i> Tray app is unavailable. Changes will not persist until it reconnects.</div>
  {/if}

  <section class="card card-wide update-card">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-refresh" aria-hidden="true"></i></span>
      <div><h2>Software updates</h2><p>Check, verify, and install the latest desktop release.</p></div>
    </div>
    <div class="update-row">
      <div class="update-copy">
        <span class="update-badge" class:available={updateStatus.updateAvailable}>
          {updateStatus.updateAvailable ? `Version ${updateStatus.latestVersion} available` : `Installed ${updateStatus.currentVersion}`}
        </span>
        <span class:error={Boolean(updateStatus.error)}>{updateStatus.error ?? updateStatus.message}</span>
      </div>
      <div class="update-actions">
        <button class="secondary-btn" type="button" onclick={checkUpdates} disabled={updateBusy || updateStatus.restarting}>
          <i class="ti ti-refresh" aria-hidden="true"></i>{updateBusy ? 'Checking…' : 'Check again'}
        </button>
        {#if updateStatus.updateAvailable}
          <button class="primary-btn" type="button" onclick={installUpdate} disabled={updateBusy || updateStatus.restarting}>
            <i class="ti ti-download" aria-hidden="true"></i>{updateStatus.restarting ? 'Restarting…' : 'Update now'}
          </button>
        {/if}
      </div>
    </div>
  </section>

  <section class="card compact-card">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-layout-dashboard" aria-hidden="true"></i></span>
      <div><h2>General</h2><p>Choose how TimeLens opens and feels.</p></div>
    </div>
    <div class="setting-row">
      <div class="setting-info"><span class="setting-label">Default tab</span><span class="setting-desc">Page shown when the dashboard opens</span></div>
      <select class="select wide" bind:value={defaultView} onchange={() => save('defaultView', defaultView)}>
        {#each views as option}<option value={option.id}>{option.label}</option>{/each}
      </select>
    </div>
    <div class="setting-row">
      <div class="setting-info"><span class="setting-label">Interface density</span><span class="setting-desc">Use roomier or tighter cards and rows</span></div>
      <select class="select wide" bind:value={density} onchange={() => { save('density', density); ondensity?.(density); }}>
        <option value="comfortable">Comfortable</option><option value="compact">Compact</option>
      </select>
    </div>
    <label class="setting-row">
      <div class="setting-info"><span class="setting-label">Smooth motion</span><span class="setting-desc">Animate navigation, controls, and state changes</span></div>
      <input type="checkbox" class="toggle" checked={motionEnabled} onchange={(e) => setToggle('motionEnabled', e, value => { motionEnabled = value; onmotion?.(value); })} />
    </label>
    <label class="setting-row">
      <div class="setting-info"><span class="setting-label">Launch at login</span><span class="setting-desc">Start TimeLens after Windows sign-in</span></div>
      <input type="checkbox" class="toggle" checked={autoStart} onchange={(e) => setToggle('autoStart', e, value => autoStart = value)} />
    </label>
  </section>

  <section class="card compact-card">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-activity-heartbeat" aria-hidden="true"></i></span>
      <div><h2>Tracking</h2><p>Control which signals contribute to activity.</p></div>
    </div>
    <label class="setting-row">
      <div class="setting-info"><span class="setting-label">Audio activity</span><span class="setting-desc">Treat audible media as active time</span></div>
      <input type="checkbox" class="toggle" checked={trackAudio} onchange={(e) => setToggle('trackAudio', e, value => trackAudio = value)} />
    </label>
    <label class="setting-row">
      <div class="setting-info"><span class="setting-label">Browser activity</span><span class="setting-desc">Accept domains and tabs from the extension</span></div>
      <input type="checkbox" class="toggle" checked={trackBrowser} onchange={(e) => setToggle('trackBrowser', e, value => trackBrowser = value)} />
    </label>
    <label class="setting-row">
      <div class="setting-info"><span class="setting-label">Keyboard and mouse</span><span class="setting-desc">Measure interaction without recording content</span></div>
      <input type="checkbox" class="toggle" checked={trackInput} onchange={(e) => setToggle('trackInput', e, value => trackInput = value)} />
    </label>
    <div class="setting-row">
      <div class="setting-info"><span class="setting-label">Idle threshold</span><span class="setting-desc">Inactivity required before time becomes idle</span></div>
      <select class="select" bind:value={idleMinutes} onchange={() => save('idleThresholdSeconds', idleMinutes * 60)}>
        {#each [1, 2, 3, 5, 10, 15] as minutes}<option value={minutes}>{minutes} min</option>{/each}
      </select>
    </div>
  </section>

  <section class="card card-wide">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-palette" aria-hidden="true"></i></span>
      <div><h2>Appearance</h2><p>Pick an accent while keeping the same accessible dark surfaces.</p></div>
    </div>
    <div class="theme-grid">
      {#each themes as option}
        <button type="button" class="theme-swatch" class:selected={theme === option.id} onclick={() => { theme = option.id; save('theme', option.id); ontheme?.(option.id); }}>
          <span class="swatch-dot" style="background:{option.color}"></span>
          <span>{option.label}</span>
          {#if theme === option.id}<i class="ti ti-check" aria-hidden="true"></i>{/if}
        </button>
      {/each}
    </div>
  </section>

  <section class="card card-wide">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-timeline-event" aria-hidden="true"></i></span>
      <div><h2>Timeline & history</h2><p>Decide how much detail appears in daily activity views.</p></div>
    </div>
    <div class="settings-columns">
      <div class="setting-row">
        <div class="setting-info"><span class="setting-label">Default timeline layout</span><span class="setting-desc">Group activity into expandable categories</span></div>
        <select class="select wide" bind:value={timelineGrouped} onchange={() => { save('timelineGrouped', timelineGrouped); ontimelinegrouped?.(timelineGrouped); }}>
          <option value={true}>Grouped</option><option value={false}>Flat</option>
        </select>
      </div>
      <label class="setting-row">
        <div class="setting-info"><span class="setting-label">Window titles</span><span class="setting-desc">Include titles in expanded timeline rows</span></div>
        <input type="checkbox" class="toggle" checked={showTitles} onchange={(e) => setToggle('showTitles', e, value => { showTitles = value; onshowtitles?.(value); })} />
      </label>
      <div class="setting-row">
        <div class="setting-info"><span class="setting-label">Hide quick switches</span><span class="setting-desc">Omit segments shorter than this duration</span></div>
        <select class="select wide" bind:value={timelineMinSegmentSeconds} onchange={() => { save('timelineMinSegmentSeconds', timelineMinSegmentSeconds); timelineMinSegmentSecondsStore.set(timelineMinSegmentSeconds); }}>
          <option value={30}>30 seconds</option><option value={60}>1 minute</option><option value={120}>2 minutes</option><option value={300}>5 minutes</option>
        </select>
      </div>
      <div class="setting-row">
        <div class="setting-info"><span class="setting-label">Activity heatmap</span><span class="setting-desc">Range shown in History</span></div>
        <select class="select wide" bind:value={heatmapDays} onchange={() => { save('heatmapDays', heatmapDays); heatmapDaysStore.set(heatmapDays); }}>
          <option value={28}>4 weeks</option><option value={91}>3 months</option><option value={273}>9 months</option><option value={365}>12 months</option>
        </select>
      </div>
      <div class="setting-row">
        <div class="setting-info"><span class="setting-label">Time format</span><span class="setting-desc">Timestamp notation throughout the dashboard</span></div>
        <select class="select" bind:value={timeFormat} onchange={() => { save('timeFormat', timeFormat); timeFormatStore.set(timeFormat === '24h' ? '24h' : '12h'); }}>
          <option value="12h">12 hour</option><option value="24h">24 hour</option>
        </select>
      </div>
      <div class="setting-row">
        <div class="setting-info"><span class="setting-label">Dashboard refresh</span><span class="setting-desc">How often live data is requested</span></div>
        <select class="select wide" bind:value={pollInterval} onchange={() => { save('pollIntervalSeconds', pollInterval); onpollinterval?.(pollInterval); }}>
          {#each [5, 10, 30, 60] as seconds}<option value={seconds}>{seconds} seconds</option>{/each}
        </select>
      </div>
    </div>
  </section>

  <section class="card compact-card">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-bell-ringing" aria-hidden="true"></i></span>
      <div><h2>Focus & reminders</h2><p>Support healthy sessions and distraction controls.</p></div>
    </div>
    <label class="setting-row">
      <div class="setting-info"><span class="setting-label">Break reminders</span><span class="setting-desc">Notify after continuous active time</span></div>
      <input type="checkbox" class="toggle" checked={breakReminder} onchange={(e) => setToggle('breakReminder', e, value => breakReminder = value)} />
    </label>
    <div class="setting-row" class:muted={!breakReminder}>
      <div class="setting-info"><span class="setting-label">Reminder interval</span><span class="setting-desc">Active time between reminders</span></div>
      <select class="select" bind:value={breakInterval} disabled={!breakReminder} onchange={() => save('breakIntervalMinutes', breakInterval)}>
        {#each [25, 30, 45, 50, 60, 90] as minutes}<option value={minutes}>{minutes} min</option>{/each}
      </select>
    </div>
  </section>

  <section class="card card-wide protection-card">
    <div class="card-header protection-header">
      <span class="section-icon"><i class="ti ti-lock-password" aria-hidden="true"></i></span>
      <div><h2>Block password</h2><p>Require a password before restrictions can be disabled, downgraded, or removed.</p></div>
      <span class="protection-state" class:enabled={blockProtectionEnabled}><i class="ti {blockProtectionEnabled ? 'ti-shield-lock' : 'ti-shield-off'}" aria-hidden="true"></i>{blockProtectionEnabled ? 'Protected' : 'Optional'}</span>
    </div>
    <div class="protection-body">
      <div class="protection-form">
        {#if blockProtectionEnabled}
          <label><span>Current password</span><input type="password" bind:value={protectionCurrentPassword} autocomplete="current-password" placeholder="Required to make changes" /></label>
        {/if}
        <label><span>{blockProtectionEnabled ? 'New password' : 'Password'}</span><input type="password" bind:value={protectionNewPassword} autocomplete="new-password" placeholder="6–128 characters" /></label>
        <label><span>Confirm password</span><input type="password" bind:value={protectionConfirmPassword} autocomplete="new-password" placeholder="Type it again" /></label>
        {#if protectionError}<div class="protection-feedback error" role="alert"><i class="ti ti-alert-circle"></i>{protectionError}</div>{/if}
        {#if protectionMessage}<div class="protection-feedback success"><i class="ti ti-circle-check"></i>{protectionMessage}</div>{/if}
        <div class="protection-actions">
          {#if blockProtectionEnabled}
            <button class="danger-btn" type="button" onclick={disableProtection} disabled={!protectionCurrentPassword || protectionBusy}>Disable protection</button>
            <button class="primary-btn" type="button" onclick={changeProtectionPassword} disabled={!protectionCurrentPassword || protectionNewPassword.length < 6 || !protectionConfirmPassword || protectionBusy}>Change password</button>
          {:else}
            <button class="primary-btn" type="button" onclick={enableProtection} disabled={protectionNewPassword.length < 6 || !protectionConfirmPassword || protectionBusy}><i class="ti ti-lock"></i>Enable protection</button>
          {/if}
        </div>
      </div>
    </div>
  </section>

  <section class="card">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-database" aria-hidden="true"></i></span>
      <div><h2>Data & storage</h2><p>Retention, exports, and local database details.</p></div>
    </div>
    <div class="setting-row">
      <div class="setting-info"><span class="setting-label">Keep activity for</span><span class="setting-desc">Older raw events are automatically removed</span></div>
      <select class="select" bind:value={retentionDays} onchange={() => save('retentionDays', retentionDays)}>
        {#each [30, 60, 90, 180, 365] as days}<option value={days}>{days} days</option>{/each}
      </select>
    </div>
    <div class="setting-row">
      <div class="setting-info"><span class="setting-label">Database size</span><span class="setting-desc">%LOCALAPPDATA%\TimeLens\activity.db</span></div>
      <code class="path">{fmtSize(dbSizeBytes)}</code>
    </div>
    <div class="setting-row export-row">
      <div class="setting-info"><span class="setting-label">Export CSV</span><span class="setting-desc">Download a portable copy of activity</span></div>
      <div class="button-group">
        <button class="secondary-btn" onclick={() => exportCsv('today')}><i class="ti ti-download" aria-hidden="true"></i> Today</button>
        <button class="secondary-btn" onclick={() => exportCsv('30days')}><i class="ti ti-calendar-stats" aria-hidden="true"></i> 30 days</button>
      </div>
    </div>
  </section>

  <section class="card card-wide">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-target-arrow" aria-hidden="true"></i></span>
      <div><h2>Goals</h2><p>Set a maximum or minimum daily target for an app or category.</p></div>
    </div>
    <div class="goal-layout">
      <div class="goal-list">
        {#if goals.length > 0}
          {#each goals as goal}
            <div class="goal-row">
              <span class="goal-icon"><i class="ti ti-target" aria-hidden="true"></i></span>
              <div class="setting-info"><span class="setting-label">{goal.target}</span><span class="setting-desc">{goal.goalType === 'max_time' ? 'Maximum' : 'Minimum'} {goal.thresholdMinutes} min · alert at {goal.notifyAt}%</span></div>
              <button class="icon-btn danger" onclick={() => removeGoal(goal.id)} aria-label="Remove {goal.target}"><i class="ti ti-trash" aria-hidden="true"></i></button>
            </div>
          {/each}
        {:else}
          <div class="goal-empty"><i class="ti ti-target-off" aria-hidden="true"></i><span>No goals yet</span></div>
        {/if}
      </div>
      <div class="goal-form">
        <label for="goal-target">New goal</label>
        <div class="goal-fields">
          <input id="goal-target" class="text-input" placeholder="App or category" bind:value={goalTarget} />
          <select class="select" bind:value={goalType}><option value="max_time">Maximum</option><option value="min_time">Minimum</option></select>
          <select class="select" bind:value={goalMinutes}>{#each [15, 30, 60, 90, 120, 180, 240] as minutes}<option value={minutes}>{minutes} min</option>{/each}</select>
          <button class="primary-btn" onclick={addGoal} disabled={!goalTarget.trim()}><i class="ti ti-plus" aria-hidden="true"></i> Add goal</button>
        </div>
      </div>
    </div>
  </section>

  <section class="card card-wide layout-settings-card">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-layout-dashboard"></i></span>
      <div>
        <h2>Card layout</h2>
        <p>Restore the original card order for one page or the entire dashboard.</p>
      </div>
    </div>
    <div class="layout-reset-row">
      <label>
        <span>Dashboard page</span>
        <select class="select wide" bind:value={layoutTarget} aria-label="Dashboard page to reset">
          {#each layoutTargets as page}
            <option value={page.id}>{page.label}</option>
          {/each}
        </select>
      </label>
      <div class="button-group">
        <button class="secondary-btn" type="button" onclick={() => resetCardLayouts(layoutTarget)}>Reset selected page</button>
        <button class="secondary-btn" type="button" onclick={() => resetCardLayouts('all')}>Reset all layouts</button>
      </div>
    </div>
  </section>

</div>

<style>
  .settings { display: grid; grid-template-columns: 1fr; gap: 12px; align-items: start; }
  .card, .card-wide, .warning { grid-column: 1; }
  .card-header, .button-group, .goal-fields { display: flex; align-items: center; }
  .card-header div { display: flex; flex-direction: column; gap: 2px; }
  .section-icon, .goal-icon { display: grid; place-items: center; flex: 0 0 auto; color: var(--md-primary); background: var(--md-primary-cont); border: 1px solid color-mix(in srgb, var(--md-primary) 20%, transparent); }
  .warning { display: flex; align-items: center; gap: 8px; padding: 10px 12px; color: var(--md-error); background: color-mix(in srgb, var(--md-error) 10%, transparent); border: 1px solid color-mix(in srgb, var(--md-error) 22%, transparent); border-radius: var(--shape-md); font-size: 12px; }
  .card { background: var(--clr-bg-sec); border: 1px solid var(--clr-border); border-radius: var(--shape-lg); overflow: hidden; padding: 0; }
  .compact-card { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .compact-card .card-header { grid-column: 1 / -1; }
  .compact-card .setting-row:nth-child(odd) { border-left: 1px solid var(--clr-border); }
  .card-header { gap: 10px; padding: 13px 16px; }
  .section-icon { width: 32px; height: 32px; border-radius: 9px; font-size: 16px; }
  .card-header h2 { margin: 0; font-size: var(--type-section-title); color: var(--clr-text-pri); }
  .card-header p { display: none; }
  .setting-row { min-height: 55px; display: flex; align-items: center; justify-content: space-between; gap: 16px; padding: 10px 16px; border-top: 1px solid var(--clr-border); }
  .update-row { min-height: 68px; display: flex; align-items: center; justify-content: space-between; gap: 18px; padding: 12px 18px 16px; border-top: 1px solid var(--clr-border); }
  .update-copy { min-width: 0; display: flex; align-items: center; gap: 10px; color: var(--clr-text-sec); font-size: 11px; }
  .update-copy .error { color: var(--md-error); }
  .update-badge { flex: 0 0 auto; padding: 5px 9px; color: var(--clr-text-sec); background: var(--clr-bg-ter); border-radius: var(--shape-full); font: 10px var(--font-mono); }
  .update-badge.available { color: var(--md-primary); background: var(--md-primary-cont); }
  .update-actions { display: flex; align-items: center; gap: 7px; flex: 0 0 auto; }
  .setting-row.muted { opacity: .48; }
  .setting-info { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .setting-label { color: var(--clr-text-pri); font-size: 13px; font-weight: 500; }
  .setting-desc { color: var(--clr-text-sec); font-size: 11px; line-height: 1.35; }
  .settings-columns { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .settings-columns .setting-row:nth-child(even) { border-left: 1px solid var(--clr-border); }
  .toggle { appearance: none; width: 40px; height: 22px; flex: 0 0 auto; margin: 0; border-radius: 99px; background: var(--clr-border-strong); position: relative; cursor: pointer; transition: background var(--duration-base) var(--ease-out); }
  .toggle::after { content: ''; position: absolute; width: 18px; height: 18px; left: 2px; top: 2px; border-radius: 50%; background: white; box-shadow: var(--shadow-xs); transition: transform var(--duration-base) var(--ease-out); }
  .toggle:checked { background: var(--md-primary); }
  .toggle:checked::after { transform: translateX(18px); }
  .select, .text-input { height: 34px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-sm); font: 12px inherit; outline: none; }
  .select { min-width: 96px; padding: 0 28px 0 10px; cursor: pointer; }
  .select.wide { min-width: 132px; }
  .select:focus, .text-input:focus { border-color: var(--md-primary); box-shadow: 0 0 0 2px color-mix(in srgb, var(--md-primary) 12%, transparent); }
  .select:disabled { cursor: not-allowed; }
  .theme-grid { display: grid; grid-template-columns: repeat(4, minmax(110px, 1fr)); gap: 10px; padding: 2px 18px 18px; }
  .theme-swatch { height: 46px; display: flex; align-items: center; gap: 9px; padding: 0 12px; color: var(--clr-text-sec); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-md); font: 12px inherit; cursor: pointer; transition: transform var(--duration-fast), border-color var(--duration-fast), background var(--duration-fast); }
  .theme-swatch:hover { transform: translateY(-1px); border-color: var(--clr-border-strong); }
  .theme-swatch.selected { color: var(--clr-text-pri); border-color: var(--md-primary); background: color-mix(in srgb, var(--md-primary) 7%, var(--clr-bg-ter)); }
  .theme-swatch i { margin-left: auto; color: var(--md-primary); }
  .swatch-dot { width: 18px; height: 18px; border-radius: 50%; box-shadow: inset 0 0 0 2px rgba(255,255,255,.12); }
  .button-group { gap: 6px; }
  .layout-reset-row { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; padding: 4px 18px 18px; }
  .layout-reset-row label { min-width: 210px; display: flex; flex-direction: column; gap: 7px; color: var(--clr-text-sec); font-size: 11px; font-weight: 600; }
  .primary-btn, .secondary-btn, .icon-btn { display: inline-flex; align-items: center; justify-content: center; gap: 6px; border-radius: var(--shape-sm); font: 12px inherit; cursor: pointer; }
  .primary-btn { height: 34px; padding: 0 13px; border: 1px solid var(--md-primary); background: var(--md-primary); color: var(--md-on-primary); font-weight: 600; }
  .secondary-btn { height: 32px; padding: 0 10px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); }
  .secondary-btn:hover { border-color: var(--md-primary); color: var(--md-primary); }
  button:disabled { opacity: .4; cursor: not-allowed; }
  .path { color: var(--clr-text-sec); font: 11px var(--font-mono); white-space: nowrap; }
  .goal-layout { padding: 0 18px 18px; display: grid; gap: 12px; }
  .goal-list { display: grid; gap: 7px; }
  .goal-row { min-height: 50px; display: flex; align-items: center; gap: 10px; padding: 8px 10px; background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-md); }
  .goal-icon { width: 30px; height: 30px; border-radius: 8px; }
  .goal-row .setting-info { flex: 1; }
  .icon-btn { width: 30px; height: 30px; border: 0; color: var(--clr-text-sec); background: transparent; }
  .icon-btn.danger:hover { color: var(--md-error); background: var(--md-err-cont); }
  .goal-empty { min-height: 54px; display: flex; align-items: center; justify-content: center; gap: 8px; color: var(--clr-text-ter); border: 1px dashed var(--clr-border); border-radius: var(--shape-md); font-size: 12px; }
  .goal-form { padding: 12px; background: color-mix(in srgb, var(--clr-bg-ter) 72%, transparent); border-radius: var(--shape-md); }
  .goal-form > label { display: block; margin-bottom: 8px; color: var(--clr-text-sec); font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: .05em; }
  .goal-fields { gap: 8px; }
  .text-input { flex: 1; min-width: 180px; padding: 0 10px; font-family: var(--font-mono); }
  .protection-header { border-bottom: 1px solid var(--clr-border); }
  .protection-header > div { flex: 1; }
  .protection-state { display: inline-flex; align-items: center; gap: 6px; padding: 5px 9px; color: var(--clr-text-sec); background: var(--clr-bg-ter); border-radius: var(--shape-full); font-size: 10px; font-weight: 600; }
  .protection-state.enabled { color: var(--md-primary); background: var(--md-primary-cont); }
  .protection-body { padding: 14px 16px 16px; }
  .protection-form { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; align-items: end; }
  .protection-form label { display: flex; flex-direction: column; gap: 6px; color: var(--clr-text-sec); font-size: 10px; font-weight: 600; }
  .protection-form label:first-child:nth-last-child(3) { grid-column: auto; }
  .protection-form input { height: 38px; padding: 0 11px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-sm); font: 12px var(--font-mono); outline: none; }
  .protection-form input:focus { border-color: var(--md-primary); box-shadow: 0 0 0 3px color-mix(in srgb, var(--md-primary) 10%, transparent); }
  .protection-feedback { grid-column: 1 / -1; display: flex; align-items: center; gap: 6px; padding: 8px 9px; border-radius: var(--shape-sm); font-size: 10px; }
  .protection-feedback.error { color: var(--md-error); background: color-mix(in srgb, var(--md-error) 9%, transparent); }
  .protection-feedback.success { color: var(--md-tertiary); background: color-mix(in srgb, var(--md-tertiary) 9%, transparent); }
  .protection-actions { grid-column: 1 / -1; display: flex; justify-content: flex-end; gap: 8px; padding-top: 2px; }
  .danger-btn { height: 34px; padding: 0 12px; color: var(--md-error); background: transparent; border: 1px solid color-mix(in srgb, var(--md-error) 40%, var(--clr-border)); border-radius: var(--shape-sm); font: 12px inherit; cursor: pointer; }
  .danger-btn:hover { background: var(--md-err-cont); }
  @media (max-width: 960px) {
    .compact-card { grid-template-columns: 1fr; }
    .compact-card .setting-row:nth-child(odd) { border-left: 0; }
    .settings-columns { grid-template-columns: 1fr; }
    .settings-columns .setting-row:nth-child(even) { border-left: 0; }
    .theme-grid { grid-template-columns: repeat(2, 1fr); }
    .protection-form { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  }
  @media (max-width: 620px) {
    .layout-reset-row { align-items: stretch; flex-direction: column; }
    .layout-reset-row label { min-width: 0; }
    .layout-reset-row .button-group { flex-wrap: wrap; }
    .protection-form { grid-template-columns: 1fr; }
    .setting-row.export-row, .goal-fields { align-items: stretch; flex-direction: column; }
    .theme-grid { grid-template-columns: 1fr; }
    .select, .select.wide { min-width: 112px; }
    .goal-fields .select, .goal-fields .primary-btn { width: 100%; }
    .update-row { align-items: stretch; flex-direction: column; }
    .update-copy { align-items: flex-start; flex-direction: column; }
    .update-actions { justify-content: flex-end; }
  }
</style>
