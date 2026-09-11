<script lang="ts">
  import { onMount } from 'svelte';
  import {
    heatmapDays as heatmapDaysStore,
    showSeconds as showSecondsStore,
    timeFormat as timeFormatStore
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
  let browserUrlMode = $state<'domain' | 'path' | 'full'>('full');
  let browserStoreTitles = $state(true);
  let pairCode = $state('');
  let pairMessage = $state('');
  let pairCopied = $state(false);
  let privacyBusy = $state(false);
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
  let showSeconds = $state(false);
  let pollInterval = $state(30);
  let defaultView = $state('today');
  let density = $state('comfortable');
  let motionEnabled = $state(true);
  let heatmapDays = $state(273);
  let blockProtectionEnabled = $state(false);
  let blockProtectionScope = $state<'strict' | 'all'>('strict');
  let blockExitProtection = $state(true);
  let protectionCurrentPassword = $state('');
  let protectionNewPassword = $state('');
  let protectionConfirmPassword = $state('');
  let protectionMessage = $state('');
  let protectionError = $state('');
  let protectionBusy = $state(false);
  let apiReachable = $state(true);
  let savingKey = $state('');
  let saveMessage = $state('');
  const localDate = (date = new Date()) => `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
  let exportFormat = $state<'csv'|'json'>('csv');
  let exportRange = $state('month');
  let exportMonth = $state(localDate().slice(0,7));
  let exportYear = $state(new Date().getFullYear());
  let exportStart = $state(localDate(new Date(new Date().getFullYear(), new Date().getMonth(), 1)));
  let exportEnd = $state(localDate());
  type UpdateStatus = {
    currentVersion: string;
    latestVersion?: string;
    updateAvailable: boolean;
    restarting: boolean;
    message: string;
    error?: string;
    releaseNotes?: string;
  };
  let updateStatus: UpdateStatus = $state({
    currentVersion: __APP_VERSION__,
    updateAvailable: false,
    restarting: false,
    message: 'Check for a newer production release.'
  });
  let updateBusy = $state(false);
  let releaseDialog: HTMLDialogElement | undefined = $state();
  let releaseDialogVersion = $state('');
  let releaseDialogNotes = $state('');

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
    { id: 'paper', label: 'Paper', color: '#F4F1E8' },
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
      browserUrlMode = s.browserUrlMode === 'domain' || s.browserUrlMode === 'path' ? s.browserUrlMode : 'full';
      browserStoreTitles = s.browserStoreTitles ?? true;
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
      showSeconds = s.showSeconds ?? false;
      pollInterval = s.pollIntervalSeconds ?? 30;
      defaultView = s.defaultView ?? 'today';
      density = s.density ?? 'comfortable';
      motionEnabled = s.motionEnabled ?? true;
      heatmapDays = s.heatmapDays ?? 273;
      blockProtectionEnabled = s.blockProtectionEnabled ?? false;
      blockProtectionScope = s.blockProtectionScope === 'all' ? 'all' : 'strict';
      blockExitProtection = s.blockExitProtection ?? true;
      timeFormatStore.set(timeFormat === '24h' ? '24h' : '12h');
      showSecondsStore.set(showSeconds);
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

  function exportActivity() {
    const params = new URLSearchParams({ format: exportFormat, range: exportRange });
    if (exportRange === 'custom') { params.set('start', exportStart); params.set('end', exportEnd); }
    if (exportRange === 'month') params.set('month', exportMonth);
    if (exportRange === 'year') params.set('year', String(exportYear));
    window.open(`/api/export?${params}`, '_blank');
  }

  async function copyPairCode() {
    if (!pairCode) return;
    try { await navigator.clipboard.writeText(pairCode); pairCopied = true; setTimeout(() => pairCopied = false, 1500); }
    catch { pairMessage = 'Copy is unavailable. Select the code and enter it in the extension.'; }
  }

  async function createPairCode() {
    privacyBusy = true;
    pairMessage = '';
    try {
      const response = await fetch('/api/pair/code', { method: 'POST' });
      if (!response.ok) throw new Error();
      const result = await response.json();
      pairCode = result.code;
      try { await navigator.clipboard.writeText(pairCode); pairCopied = true; setTimeout(() => pairCopied = false, 1500); }
      catch { }
      pairMessage = 'Code ready and copied when browser permissions allow. Open the TimeLens extension, choose Pair, and paste it within two minutes.';
    } catch { pairMessage = 'Could not create a pairing code.'; }
    finally { privacyBusy = false; }
  }

  async function revokeBrowsers() {
    privacyBusy = true;
    try {
      const response = await fetch('/api/pair/revoke', { method: 'POST' });
      if (!response.ok) throw new Error();
      pairCode = '';
      pairMessage = 'All paired browser access revoked. Pair again to resume browser tracking.';
    } catch { pairMessage = 'Could not revoke browser extension access.'; }
    finally { privacyBusy = false; }
  }

  async function deleteAllActivity() {
    if (!confirm('Permanently delete all recorded activity? Settings and focus rules will be kept.')) return;
    privacyBusy = true;
    try {
      const response = await fetch('/api/privacy/delete-all', { method: 'POST' });
      if (!response.ok) throw new Error();
      pairMessage = 'Recorded activity deleted. Tracking continues with a fresh session.';
      await load();
    } catch { pairMessage = 'Could not delete activity.'; }
    finally { privacyBusy = false; }
  }

  async function checkUpdates() {
    updateBusy = true;
    try {
      const response = await fetch('/api/update/status', { cache: 'no-store' });
      if (!response.ok) throw new Error();
      updateStatus = await response.json();
      showCompletedUpdate(updateStatus);
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
      if (result.restarting) {
        sessionStorage.setItem('timelens.updatedTo', result.latestVersion ?? '');
        sessionStorage.setItem('timelens.updatedFrom', result.currentVersion ?? updateStatus.currentVersion);
        void waitForUpdatedDashboard(result.latestVersion);
      }
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

  async function waitForUpdatedDashboard(expectedVersion: string | undefined) {
    const deadline = Date.now() + 75_000;
    while (Date.now() < deadline) {
      await new Promise((resolve) => window.setTimeout(resolve, 1_000));
      try {
        const response = await fetch('/api/update/status', { cache: 'no-store' });
        if (!response.ok) continue;
        const status: UpdateStatus = await response.json();
        if (status.currentVersion === expectedVersion) {
          const next = new URL(window.location.href);
          next.searchParams.set('v', String(Date.now()));
          window.location.replace(next.toString());
          return;
        }
      } catch {
        // The old local API is expected to disappear while the executable is replaced.
      }
    }
    updateStatus = {
      ...updateStatus,
      restarting: false,
      error: 'TimeLens did not return after the update. Open it again from Start or run the installer.'
    };
    updateBusy = false;
  }

  function showCompletedUpdate(status: UpdateStatus) {
    const updatedTo = sessionStorage.getItem('timelens.updatedTo');
    if (!updatedTo || updatedTo !== status.currentVersion) return;
    const updatedFrom = sessionStorage.getItem('timelens.updatedFrom');
    sessionStorage.removeItem('timelens.updatedTo');
    sessionStorage.removeItem('timelens.updatedFrom');
    releaseDialogVersion = status.currentVersion;
    releaseDialogNotes = status.releaseNotes?.trim() || 'TimeLens has been updated successfully.';
    if (updatedFrom) releaseDialogNotes = `Updated from ${updatedFrom} to ${status.currentVersion}.\n\n${releaseDialogNotes}`;
    releaseDialog?.showModal();
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

  async function saveProtectionOptions() {
    if (!protectionCurrentPassword) return;
    protectionBusy = true;
    protectionMessage = '';
    protectionError = '';
    try {
      const response = await fetch('/api/block/protection/options', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ password: protectionCurrentPassword, scope: blockProtectionScope, protectExit: blockExitProtection })
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || 'Could not save protection options');
      protectionCurrentPassword = '';
      protectionMessage = 'Protection options saved';
      await load();
    } catch (error) {
      protectionError = error instanceof Error ? error.message : 'Could not save protection options';
    } finally { protectionBusy = false; }
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
        <div class="update-versions">
          <span class="update-badge">Installed {updateStatus.currentVersion}</span>
          {#if updateStatus.updateAvailable && updateStatus.latestVersion}
            <span class="update-badge available">New update {updateStatus.latestVersion} available</span>
          {/if}
        </div>
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
        {#each [1, 2, 3, 5, 10, 15, 20, 30, 60] as minutes}<option value={minutes}>{minutes} min</option>{/each}
      </select>
    </div>
  </section>

  <section class="card card-wide privacy-card">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-shield-lock" aria-hidden="true"></i></span>
      <div><h2>Privacy center</h2><p>Control collection, browser extension access, and deletion.</p></div>
    </div>
    <div class="settings-columns">
      <div class="setting-row">
        <div class="setting-info"><span class="setting-label">Stored browser address</span><span class="setting-desc">Reduce URL detail before it reaches the database</span></div>
        <select class="select wide" bind:value={browserUrlMode} onchange={() => save('browserUrlMode', browserUrlMode)}>
          <option value="domain">Domain only</option><option value="path">Domain + path</option><option value="full">Full URL</option>
        </select>
      </div>
      <label class="setting-row">
        <div class="setting-info"><span class="setting-label">Store page titles</span><span class="setting-desc">Disable to save browser records without page titles</span></div>
        <input type="checkbox" class="toggle" checked={browserStoreTitles} onchange={(e) => setToggle('browserStoreTitles', e, value => browserStoreTitles = value)} />
      </label>
      <div class="setting-row pair-row">
        <div class="setting-info"><span class="setting-label">Connect browser extension</span><span class="setting-desc">Pairing lets the extension send the active site and interaction counts only to this PC. It prevents other local apps from writing fake browser history.</span></div>
        <div class="browser-actions">
          <div class="button-group download-actions">
            <button class="secondary-btn" type="button" title="Download the Firefox extension package" onclick={() => window.open('https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-Firefox-Extension.zip','_blank')}><i class="ti ti-brand-firefox"></i>Get Firefox ZIP</button>
            <button class="secondary-btn" type="button" title="Download the Chrome extension package" onclick={() => window.open('https://github.com/YumiNoona/TimeLens/releases/latest/download/TimeLens-Chrome-Extension.zip','_blank')}><i class="ti ti-brand-chrome"></i>Get Chrome ZIP</button>
          </div>
          <div class="button-group connection-actions">
            {#if pairCode}<button class="pair-code" type="button" title="Copy pairing code" onclick={copyPairCode}>{pairCode}<i class="ti {pairCopied ? 'ti-check' : 'ti-copy'}"></i></button>{/if}
            <button class="primary-btn" type="button" onclick={createPairCode} disabled={privacyBusy}>{pairCode ? 'New code' : 'Connect'}</button>
            <button class="secondary-btn" type="button" onclick={revokeBrowsers} disabled={privacyBusy}>Disconnect all</button>
          </div>
        </div>
      </div>
      <div class="setting-row delete-row">
        <div class="setting-info"><span class="setting-label">Delete activity</span><span class="setting-desc">Permanently erase activity while keeping settings and rules</span></div>
        <button class="danger-btn" type="button" onclick={deleteAllActivity} disabled={privacyBusy}>Delete all activity</button>
      </div>
    </div>
    {#if pairMessage}<div class="privacy-message" role="status">{pairMessage}</div>{/if}
  </section>

  <section class="card card-wide">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-palette" aria-hidden="true"></i></span>
      <div><h2>Appearance</h2><p>Choose a dark accent or the light Paper theme.</p></div>
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

  <section class="card card-wide timeline-history-card">
    <div class="card-header">
      <span class="section-icon"><i class="ti ti-timeline-event" aria-hidden="true"></i></span>
      <div><h2>Timeline & history</h2><p>Decide how much detail appears in daily activity views.</p></div>
    </div>
    <div class="history-settings-grid">
      <div class="setting-group">
        <div class="setting-group-title"><i class="ti ti-timeline" aria-hidden="true"></i><span>Timeline</span></div>
        <div class="setting-row">
          <div class="setting-info"><span class="setting-label">Default layout</span><span class="setting-desc">Group activity into expandable categories</span></div>
          <select class="select wide" bind:value={timelineGrouped} onchange={() => { save('timelineGrouped', timelineGrouped); ontimelinegrouped?.(timelineGrouped); }}>
            <option value={true}>Grouped</option><option value={false}>Flat</option>
          </select>
        </div>
        <label class="setting-row">
          <div class="setting-info"><span class="setting-label">Window titles</span><span class="setting-desc">Show titles in expanded timeline rows</span></div>
          <input type="checkbox" class="toggle" checked={showTitles} onchange={(e) => setToggle('showTitles', e, value => { showTitles = value; onshowtitles?.(value); })} />
        </label>
      </div>

      <div class="setting-group">
        <div class="setting-group-title"><i class="ti ti-calendar-stats" aria-hidden="true"></i><span>History display</span></div>
        <div class="setting-row">
          <div class="setting-info"><span class="setting-label">Heatmap range</span><span class="setting-desc">Default period shown in History</span></div>
          <select class="select wide" bind:value={heatmapDays} onchange={() => { save('heatmapDays', heatmapDays); heatmapDaysStore.set(heatmapDays); }}>
            <option value={28}>4 weeks</option><option value={91}>3 months</option><option value={182}>6 months</option><option value={273}>9 months</option><option value={365}>12 months</option>
          </select>
        </div>
        <label class="setting-row">
          <div class="setting-info"><span class="setting-label">Show seconds</span><span class="setting-desc">Add seconds to activity durations</span></div>
          <input type="checkbox" class="toggle" checked={showSeconds} onchange={(e) => setToggle('showSeconds', e, value => { showSeconds = value; showSecondsStore.set(value); })} />
        </label>
      </div>

      <div class="setting-group">
        <div class="setting-group-title"><i class="ti ti-clock-cog" aria-hidden="true"></i><span>Clock & updates</span></div>
        <div class="setting-row">
          <div class="setting-info"><span class="setting-label">Time format</span><span class="setting-desc">Timestamp notation across the dashboard</span></div>
          <select class="select" bind:value={timeFormat} onchange={() => { save('timeFormat', timeFormat); timeFormatStore.set(timeFormat === '24h' ? '24h' : '12h'); }}>
            <option value="12h">12 hour</option><option value="24h">24 hour</option>
          </select>
        </div>
        <div class="setting-row">
          <div class="setting-info"><span class="setting-label">Dashboard refresh</span><span class="setting-desc">How often live data is requested</span></div>
          <select class="select wide" bind:value={pollInterval} onchange={() => { save('pollIntervalSeconds', pollInterval); onpollinterval?.(pollInterval); }}>
            {#each [5, 10, 15, 30, 60, 120, 300] as seconds}<option value={seconds}>{seconds} seconds</option>{/each}
          </select>
        </div>
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
        {#each [15, 20, 25, 30, 45, 50, 60, 90, 120, 180, 240] as minutes}<option value={minutes}>{minutes} min</option>{/each}
      </select>
    </div>
  </section>

  <section class="card card-wide protection-card">
    <div class="card-header protection-header">
      <span class="section-icon"><i class="ti ti-lock-password" aria-hidden="true"></i></span>
      <div><h2>Block password</h2><p>Choose which blocks need a password to weaken or remove, and whether tray exit is protected.</p></div>
      <span class="protection-state" class:enabled={blockProtectionEnabled}><i class="ti {blockProtectionEnabled ? 'ti-shield-lock' : 'ti-shield-off'}" aria-hidden="true"></i>{blockProtectionEnabled ? 'Protected' : 'Optional'}</span>
    </div>
    <div class="protection-body">
      <div class="protection-form">
        {#if blockProtectionEnabled}
          <label><span>Current password</span><input type="password" bind:value={protectionCurrentPassword} autocomplete="current-password" placeholder="Required to make changes" /></label>
          <label><span>Protect changes</span><select bind:value={blockProtectionScope}><option value="strict">Strict targets only</option><option value="all">All focus targets</option></select></label>
          <label class="setting-row protection-toggle"><span class="setting-info"><strong class="setting-label">Protect tray exit</strong><small class="setting-desc">Prevent closing while active apps or websites remain blocked. Empty lists and expired timers always allow exit.</small></span><input type="checkbox" class="toggle" bind:checked={blockExitProtection} aria-label="Protect tray exit" /></label>
        {/if}
        <label><span>{blockProtectionEnabled ? 'New password' : 'Password'}</span><input type="password" bind:value={protectionNewPassword} autocomplete="new-password" placeholder="6–128 characters" /></label>
        <label><span>Confirm password</span><input type="password" bind:value={protectionConfirmPassword} autocomplete="new-password" placeholder="Type it again" /></label>
        {#if protectionError}<div class="protection-feedback error" role="alert"><i class="ti ti-alert-circle"></i>{protectionError}</div>{/if}
        {#if protectionMessage}<div class="protection-feedback success"><i class="ti ti-circle-check"></i>{protectionMessage}</div>{/if}
        <div class="protection-actions">
          {#if blockProtectionEnabled}
            <button class="danger-btn" type="button" onclick={disableProtection} disabled={!protectionCurrentPassword || protectionBusy}>Disable protection</button>
            <button class="secondary-btn" type="button" onclick={saveProtectionOptions} disabled={!protectionCurrentPassword || protectionBusy}>Save protection options</button>
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
        {#each [30, 60, 90, 180, 365, 730] as days}<option value={days}>{days} days</option>{/each}
      </select>
    </div>
    <div class="setting-row">
      <div class="setting-info"><span class="setting-label">Local storage used</span><span class="setting-desc">Database, write-ahead log, reminder media, and diagnostics</span></div>
      <code class="path">{fmtSize(dbSizeBytes)}</code>
    </div>
    <div class="export-builder">
      <div class="setting-info"><span class="setting-label">Export activity</span><span class="setting-desc">Choose a ready-made period or an exact date range, up to ten years.</span></div>
      <div class="export-controls">
        <select class="select" bind:value={exportRange} aria-label="Export period"><option value="today">Today</option><option value="30days">Last 30 days</option><option value="month">Specific month</option><option value="year">Whole year</option><option value="custom">Custom dates</option></select>
        {#if exportRange === 'month'}<input type="month" bind:value={exportMonth} max={localDate().slice(0,7)} aria-label="Export month" />{/if}
        {#if exportRange === 'year'}<select class="select" bind:value={exportYear} aria-label="Export year">{#each Array.from({length:10},(_,i)=>new Date().getFullYear()-i) as year}<option value={year}>{year}</option>{/each}</select>{/if}
        {#if exportRange === 'custom'}<input type="date" bind:value={exportStart} max={exportEnd} aria-label="Export start date" /><span>to</span><input type="date" bind:value={exportEnd} min={exportStart} max={new Date().toISOString().slice(0,10)} aria-label="Export end date" />{/if}
        <select class="select" bind:value={exportFormat} aria-label="Export format"><option value="csv">CSV</option><option value="json">JSON</option></select>
        <button class="primary-btn" onclick={exportActivity} disabled={exportRange === 'custom' && (!exportStart || !exportEnd || exportEnd < exportStart)}><i class="ti ti-download"></i>Export</button>
      </div>
    </div>
  </section>

</div>

<dialog class="release-dialog" bind:this={releaseDialog} aria-labelledby="release-dialog-title">
  <div class="release-dialog-head">
    <div><span class="release-kicker">UPDATE COMPLETE</span><h2 id="release-dialog-title">TimeLens {releaseDialogVersion} is ready</h2></div>
    <button class="icon-btn" type="button" aria-label="Close update details" onclick={() => releaseDialog?.close()}><i class="ti ti-x" aria-hidden="true"></i></button>
  </div>
  <p class="release-dialog-copy">{releaseDialogNotes}</p>
  <div class="release-dialog-actions"><button class="primary-btn" type="button" onclick={() => releaseDialog?.close()}>Continue</button></div>
</dialog>

<style>
  .settings { display: grid; grid-template-columns: 1fr; gap: 12px; align-items: start; }
  .card, .card-wide, .warning { grid-column: 1; }
  .card-header, .button-group { display: flex; align-items: center; }
  .card-header div { display: flex; flex-direction: column; gap: 2px; }
  .section-icon { display: grid; place-items: center; flex: 0 0 auto; color: var(--md-primary); background: var(--md-primary-cont); border: 1px solid color-mix(in srgb, var(--md-primary) 20%, transparent); }
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
  .update-versions { display: inline-flex; flex-wrap: wrap; align-items: center; gap: 6px; }
  .update-copy .error { color: var(--md-error); }
  .update-badge { flex: 0 0 auto; padding: 5px 9px; color: var(--clr-text-sec); background: var(--clr-bg-ter); border-radius: var(--shape-full); font: 10px var(--font-mono); }
  .update-badge.available { color: var(--md-primary); background: var(--md-primary-cont); }
  .update-actions { display: flex; align-items: center; gap: 7px; flex: 0 0 auto; }
  .setting-row.muted { opacity: .48; }
  .setting-info { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .setting-label { color: var(--clr-text-pri); font-size: 13px; font-weight: 500; }
  .setting-desc { color: var(--clr-text-sec); font-size: 11px; line-height: 1.35; }
  .timeline-history-card .card-header { padding-bottom: 15px; border-bottom: 1px solid var(--clr-border); }
  .timeline-history-card .card-header p { display: block; }
  .history-settings-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; padding: 14px; background: color-mix(in srgb, var(--clr-bg-ter) 35%, transparent); }
  .setting-group { min-width: 0; overflow: hidden; background: var(--clr-bg-sec); border: 1px solid var(--clr-border); border-radius: var(--shape-md); }
  .setting-group-title { height: 38px; display: flex; align-items: center; gap: 7px; padding: 0 13px; color: var(--clr-text-sec); background: color-mix(in srgb, var(--md-primary) 4%, var(--clr-bg-ter)); border-bottom: 1px solid var(--clr-border); font-size: 11px; font-weight: 650; letter-spacing: .02em; }
  .setting-group-title i { color: var(--md-primary); font-size: 14px; }
  .setting-group .setting-row { min-height: 64px; padding: 10px 13px; border-top: 0; }
  .setting-group .setting-row + .setting-row { border-top: 1px solid var(--clr-border); }
  .toggle { appearance: none; width: 40px; height: 22px; flex: 0 0 auto; margin: 0; border-radius: 99px; background: var(--clr-border-strong); position: relative; cursor: pointer; transition: background var(--duration-base) var(--ease-out); }
  .toggle::after { content: ''; position: absolute; width: 18px; height: 18px; left: 2px; top: 2px; border-radius: 50%; background: white; box-shadow: var(--shadow-xs); transition: transform var(--duration-base) var(--ease-out); }
  .toggle:checked { background: var(--md-primary); }
  .toggle:checked::after { transform: translateX(18px); }
  .select { height: 34px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-sm); font: 12px inherit; outline: none; }
  .select { min-width: 96px; padding: 0 28px 0 10px; cursor: pointer; }
  .select.wide { min-width: 132px; }
  .select:focus { border-color: var(--md-primary); box-shadow: 0 0 0 2px color-mix(in srgb, var(--md-primary) 12%, transparent); }
  .select:disabled { cursor: not-allowed; }
  .theme-grid { display: grid; grid-template-columns: repeat(4, minmax(110px, 1fr)); gap: 10px; padding: 2px 18px 18px; }
  .theme-swatch { height: 46px; display: flex; align-items: center; gap: 9px; padding: 0 12px; color: var(--clr-text-sec); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-md); font: 12px inherit; cursor: pointer; transition: transform var(--duration-fast), border-color var(--duration-fast), background var(--duration-fast); }
  .theme-swatch:hover { transform: translateY(-1px); border-color: var(--clr-border-strong); }
  .theme-swatch.selected { color: var(--clr-text-pri); border-color: var(--md-primary); background: color-mix(in srgb, var(--md-primary) 7%, var(--clr-bg-ter)); }
  .theme-swatch i { margin-left: auto; color: var(--md-primary); }
  .swatch-dot { width: 18px; height: 18px; border-radius: 50%; box-shadow: inset 0 0 0 2px rgba(255,255,255,.12); }
  .primary-btn, .secondary-btn, .icon-btn { display: inline-flex; align-items: center; justify-content: center; gap: 6px; border-radius: var(--shape-sm); font: 12px inherit; cursor: pointer; }
  .primary-btn { height: 34px; padding: 0 13px; border: 1px solid var(--md-primary); background: var(--md-primary); color: var(--md-on-primary); font-weight: 600; }
  .secondary-btn { height: 32px; padding: 0 10px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); }
  .secondary-btn:hover { border-color: var(--md-primary); color: var(--md-primary); }
  button:disabled { opacity: .4; cursor: not-allowed; }
  .path { color: var(--clr-text-sec); font: 11px var(--font-mono); white-space: nowrap; }
  .pair-code { height:34px;display:flex;align-items:center;gap:8px;padding: 5px 9px; color: var(--md-primary); background: var(--md-primary-cont); border:1px solid color-mix(in srgb,var(--md-primary) 30%,transparent); border-radius: var(--shape-sm); font: 700 15px var(--font-mono); letter-spacing: .1em;cursor:pointer }
  .browser-actions{display:flex;align-items:center;gap:18px;flex-wrap:wrap;justify-content:flex-end}.button-group{gap:6px}.connection-actions{padding-left:18px;border-left:1px solid var(--clr-border)}
  .privacy-message { padding: 9px 16px 13px; border-top: 1px solid var(--clr-border); color: var(--clr-text-sec); font-size: 11px; }
  .icon-btn { width: 30px; height: 30px; border: 0; color: var(--clr-text-sec); background: transparent; }
  .protection-header { border-bottom: 1px solid var(--clr-border); }
  .protection-header > div { flex: 1; }
  .protection-state { display: inline-flex; align-items: center; gap: 6px; padding: 5px 9px; color: var(--clr-text-sec); background: var(--clr-bg-ter); border-radius: var(--shape-full); font-size: 10px; font-weight: 600; }
  .protection-state.enabled { color: var(--md-primary); background: var(--md-primary-cont); }
  .protection-body { padding: 14px 16px 16px; }
  .protection-form { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; align-items: end; }
  .protection-form label { display: flex; flex-direction: column; gap: 6px; color: var(--clr-text-sec); font-size: 10px; font-weight: 600; }
  .protection-form select { height: 38px; padding: 0 11px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-sm); font-size: 12px; outline: none; }
  .protection-toggle { grid-column: 1 / -1; margin:0;padding:10px 12px !important;border:1px solid var(--clr-border) !important;border-radius:var(--shape-md);background:var(--clr-bg-ter);flex-direction:row !important }
  .protection-toggle input.toggle { appearance:none;width:40px;height:22px;flex:0 0 40px;padding:0;border:0;border-radius:999px;background:var(--clr-border-strong);overflow:visible }
  .protection-toggle input.toggle:checked { background:var(--md-primary) }
  .protection-form input { height: 38px; padding: 0 11px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-sm); font: 12px var(--font-mono); outline: none; }
  .protection-form input:focus { border-color: var(--md-primary); box-shadow: 0 0 0 3px color-mix(in srgb, var(--md-primary) 10%, transparent); }
  .protection-feedback { grid-column: 1 / -1; display: flex; align-items: center; gap: 6px; padding: 8px 9px; border-radius: var(--shape-sm); font-size: 10px; }
  .protection-feedback.error { color: var(--md-error); background: color-mix(in srgb, var(--md-error) 9%, transparent); }
  .protection-feedback.success { color: var(--md-tertiary); background: color-mix(in srgb, var(--md-tertiary) 9%, transparent); }
  .protection-actions { grid-column: 1 / -1; display: flex; justify-content: flex-end; gap: 8px; padding-top: 2px; }
  .danger-btn { height: 34px; padding: 0 12px; color: var(--md-error); background: transparent; border: 1px solid color-mix(in srgb, var(--md-error) 40%, var(--clr-border)); border-radius: var(--shape-sm); font: 12px inherit; cursor: pointer; }
  .danger-btn:hover { background: var(--md-err-cont); }
  .export-builder{display:grid;grid-template-columns:minmax(230px,1fr) auto;align-items:center;gap:24px;padding:14px 16px;border-top:1px solid var(--clr-border)}.export-controls{display:flex;align-items:center;justify-content:flex-end;gap:8px;flex-wrap:wrap}.export-controls input{height:34px;padding:0 9px;border:1px solid var(--clr-border);border-radius:var(--shape-sm);background:var(--clr-bg-ter);color:var(--clr-text-pri);font:11px var(--font-mono)}.export-controls>span{color:var(--clr-text-ter);font-size:10px}
  .release-dialog { width: min(510px, calc(100vw - 32px)); padding: 0; overflow: hidden; border: 1px solid var(--clr-border-strong); border-radius: var(--shape-lg); color: var(--clr-text-pri); background: var(--clr-bg-sec); box-shadow: var(--shadow-lg); }
  .release-dialog::backdrop { background: rgba(0, 0, 0, .62); backdrop-filter: blur(3px); }
  .release-dialog-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 18px; padding: 20px 20px 15px; border-bottom: 1px solid var(--clr-border); }
  .release-kicker { display: block; margin-bottom: 6px; color: var(--md-primary); font: 10px var(--font-mono); letter-spacing: .09em; }
  .release-dialog h2 { margin: 0; font-size: 18px; }
  .release-dialog-copy { max-height: 280px; margin: 0; padding: 18px 20px; overflow: auto; color: var(--clr-text-sec); font-size: 12px; line-height: 1.65; white-space: pre-wrap; }
  .release-dialog-actions { display: flex; justify-content: flex-end; padding: 14px 20px 20px; border-top: 1px solid var(--clr-border); }
  @media (max-width: 960px) {
    .compact-card { grid-template-columns: 1fr; }
    .compact-card .setting-row:nth-child(odd) { border-left: 0; }
    .history-settings-grid { grid-template-columns: 1fr 1fr; }
    .setting-group:last-child { grid-column: 1 / -1; display: grid; grid-template-columns: 1fr 1fr; }
    .setting-group:last-child .setting-group-title { grid-column: 1 / -1; }
    .setting-group:last-child .setting-row + .setting-row { border-top: 0; border-left: 1px solid var(--clr-border); }
    .theme-grid { grid-template-columns: repeat(2, 1fr); }
  }
  @media (max-width: 620px) {
    .protection-form { grid-template-columns: 1fr; }
    .export-builder { grid-template-columns:1fr; }
    .export-controls { align-items: stretch; flex-direction: column; }
    .export-controls > * { width: 100%; }
    .browser-actions { justify-content:flex-start;gap:10px; }
    .connection-actions { width:100%;padding:10px 0 0;border-left:0;border-top:1px solid var(--clr-border); }
    .theme-grid { grid-template-columns: 1fr; }
    .history-settings-grid { grid-template-columns: 1fr; padding: 10px; }
    .setting-group:last-child { grid-column: auto; display: block; }
    .setting-group:last-child .setting-row + .setting-row { border-top: 1px solid var(--clr-border); border-left: 0; }
    .select, .select.wide { min-width: 112px; }
    .update-row { align-items: stretch; flex-direction: column; }
    .update-copy { align-items: flex-start; flex-direction: column; }
    .update-actions { justify-content: flex-end; }
  }
</style>
