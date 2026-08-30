<script lang="ts">
  import { onMount } from 'svelte';
  import { appIcon } from '../appIcons';

  type Action = 'notify' | 'hide' | 'kill' | 'strict';
  type TargetKind = 'app' | 'website';
  type BlockEntry = { i: string; m: 'u' | 't'; e?: string; a?: Action };
  type RetryAction = () => Promise<void>;

  const DEFAULT_TITLE = 'Focus Mode';
  const DEFAULT_MESSAGE = "'{target}' is blocked — get back to work!";
  const APP_ACTIONS: { id: Action; label: string; icon: string; description: string }[] = [
    { id: 'notify', label: 'Notify', icon: 'ti-bell', description: 'Show a bottom-left reminder and leave the app usable.' },
    { id: 'hide', label: 'Hide', icon: 'ti-eye-off', description: 'Minimize the app whenever its window comes forward.' },
    { id: 'kill', label: 'Kill', icon: 'ti-x', description: 'Close the app process when it is detected.' },
    { id: 'strict', label: 'Strict', icon: 'ti-shield-lock', description: 'Close the app and keep checking for relaunches.' },
  ];
  const WEB_ACTIONS = APP_ACTIONS.filter((item) => item.id === 'notify' || item.id === 'strict');
  const DURATIONS = [
    { value: 0, label: 'No limit' }, { value: 15, label: '15 min' },
    { value: 30, label: '30 min' }, { value: 60, label: '1 hour' },
    { value: 120, label: '2 hours' }, { value: 240, label: '4 hours' },
  ];
  const NOTIFY_INTERVALS = [
    { value: 5, label: 'Every 5 sec' },
    { value: 300, label: 'Every 5 min' },
    { value: 1800, label: 'Every 30 min' },
    { value: 3600, label: 'Every hour' },
    { value: -1, label: 'Custom' },
  ];

  let items = $state<BlockEntry[]>([]);
  let legacyAction = $state<Action>('hide');
  let targetKind = $state<TargetKind>('app');
  let newItem = $state('');
  let newAction = $state<Action>('hide');
  let addingDuration = $state(0);
  let focusMode = $state(false);
  let blockTitle = $state(DEFAULT_TITLE);
  let blockMessage = $state(DEFAULT_MESSAGE);
  let blockImageVersion = $state('');
  let blockMediaType = $state('');
  let notifyIntervalPreset = $state(300);
  let customIntervalAmount = $state(10);
  let customIntervalUnit = $state(60);
  let blockNotifyPosition = $state<'left' | 'right'>('left');
  let notificationDirty = $state(false);
  let notificationSaving = $state(false);
  let imageUploading = $state(false);
  let notificationStatus = $state('');
  let runningProcs = $state<string[]>([]);
  let showSuggestions = $state(false);
  let extensionConnected = $state(false);
  let extensionBrowser = $state('');
  let saving = $state(false);
  let errorMessage = $state('');
  let lastTested = $state('');
  let blockProtectionEnabled = $state(false);
  let unlockToken = $state('');
  let showUnlock = $state(false);
  let unlockPassword = $state('');
  let unlockError = $state('');
  let unlocking = $state(false);
  let pendingAction: RetryAction | null = null;

  function isApp(entry: BlockEntry): boolean { return entry.i.endsWith('.exe'); }
  function isWindowsShell(identifier: string): boolean { return identifier.trim().toLowerCase() === 'explorer.exe'; }
  function effectiveAction(entry: BlockEntry): Action {
    if (entry.a) return entry.a;
    if (!isApp(entry)) return legacyAction === 'notify' ? 'notify' : 'strict';
    return legacyAction;
  }
  function actionsFor(kind: TargetKind, identifier = '') {
    if (kind === 'website') return WEB_ACTIONS;
    return isWindowsShell(identifier) ? APP_ACTIONS.filter(action => action.id === 'notify' || action.id === 'hide') : APP_ACTIONS;
  }
  function setTargetKind(kind: TargetKind) {
    targetKind = kind;
    newItem = '';
    showSuggestions = false;
    if (kind === 'website' && newAction !== 'notify' && newAction !== 'strict') newAction = 'strict';
    if (kind === 'app' && (newAction === 'strict' || newAction === 'notify')) newAction = 'hide';
  }

  async function load() {
    try {
      const response = await fetch('/api/settings');
      if (!response.ok) throw new Error(`Settings request failed (${response.status})`);
      const settings = await response.json();
      legacyAction = (settings.blockAction || 'hide') as Action;
      blockTitle = settings.blockTitle || DEFAULT_TITLE;
      blockMessage = settings.blockMessage || DEFAULT_MESSAGE;
      blockImageVersion = settings.blockImageVersion || '';
      blockMediaType = settings.blockMediaType || (blockImageVersion ? 'image/png' : '');
      const savedInterval = Math.max(5, Number(settings.blockNotifyIntervalSeconds) || 300);
      if (NOTIFY_INTERVALS.some((option) => option.value === savedInterval)) notifyIntervalPreset = savedInterval;
      else { notifyIntervalPreset = -1; customIntervalAmount = savedInterval; customIntervalUnit = 1; }
      blockNotifyPosition = settings.blockNotifyPosition === 'right' ? 'right' : 'left';
      focusMode = settings.focusMode === true;
      blockProtectionEnabled = settings.blockProtectionEnabled === true;
      const parsed = JSON.parse(settings.focusBlocklist || '[]');
      items = Array.isArray(parsed) ? parsed.filter((entry) => entry && typeof entry.i === 'string') : [];
      errorMessage = '';
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : 'TimeLens is not responding';
    }
    await Promise.all([loadRunning(), loadExtensionStatus()]);
  }

  async function loadRunning() {
    try {
      const response = await fetch('/api/running-processes');
      runningProcs = response.ok ? await response.json() : [];
    } catch { runningProcs = []; }
  }

  async function loadExtensionStatus() {
    try {
      const response = await fetch('/api/extension-status');
      const status = response.ok ? await response.json() : {};
      extensionConnected = status.connected === true;
      extensionBrowser = status.browser && status.browser !== 'unknown' ? status.browser : '';
    } catch { extensionConnected = false; extensionBrowser = ''; }
  }

  async function postProtectedSetting(payload: Record<string, unknown>, retry: RetryAction | null): Promise<Response | null> {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (unlockToken) headers['X-TimeLens-Unlock'] = unlockToken;
    const response = await fetch('/api/settings', { method: 'POST', headers, body: JSON.stringify(payload) });
    if (response.status === 423) {
      unlockToken = '';
      pendingAction = retry;
      unlockPassword = '';
      unlockError = '';
      showUnlock = true;
      return null;
    }
    return response;
  }

  async function unlockProtection() {
    if (!unlockPassword || unlocking) return;
    unlocking = true; unlockError = '';
    try {
      const response = await fetch('/api/block/protection/unlock', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ password: unlockPassword }) });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || 'Password is incorrect');
      unlockToken = result.token || '';
      showUnlock = false;
      const action = pendingAction;
      pendingAction = null;
      if (action) await action();
    } catch (error) { unlockError = error instanceof Error ? error.message : 'Could not unlock'; }
    finally { unlocking = false; }
  }

  async function saveAll(next: BlockEntry[], retry: RetryAction | null = null): Promise<boolean> {
    saving = true;
    try {
      const response = await postProtectedSetting({ focusBlocklist: JSON.stringify(next) }, retry);
      if (response === null) return false;
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || `Save failed (${response.status})`);
      errorMessage = '';
      return true;
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : 'Could not save targets';
      return false;
    } finally { saving = false; }
  }

  async function saveFocus(value: boolean) {
    const previous = focusMode;
    focusMode = value;
    try {
      const response = await postProtectedSetting({ focusMode: value }, () => saveFocus(value));
      if (response === null) { focusMode = previous; return; }
      if (!response.ok) throw new Error();
    } catch { focusMode = previous; errorMessage = 'Protection could not be updated'; }
  }

  function sanitizeApp(raw: string) {
    let value = raw.trim().replace(/^['"]|['"]$/g, '').split(/[\\/]/).pop() || '';
    if (value && !value.toLowerCase().endsWith('.exe')) value += '.exe';
    return value.toLowerCase();
  }
  function sanitizeWebsite(raw: string) {
    try { return new URL(raw.includes('://') ? raw : `https://${raw}`).hostname.replace(/^www\./, '').toLowerCase(); }
    catch { return raw.trim().replace(/^https?:\/\//, '').split('/')[0].replace(/^www\./, '').toLowerCase(); }
  }
  function keepShellModeSafe(raw: string) {
    if (targetKind === 'app' && isWindowsShell(sanitizeApp(raw)) && (newAction === 'kill' || newAction === 'strict')) newAction = 'hide';
  }

  async function addTarget(raw = newItem) {
    const value = targetKind === 'app' ? sanitizeApp(raw) : sanitizeWebsite(raw);
    const valid = targetKind === 'app'
      ? /^[a-z0-9][a-z0-9._ -]*\.exe$/.test(value)
      : /^[a-z0-9][a-z0-9.-]*\.[a-z0-9-]+$/.test(value);
    if (!valid) { errorMessage = targetKind === 'app' ? 'Enter an app such as discord.exe' : 'Enter a website such as youtube.com'; return; }
    if (items.some((entry) => entry.i.toLowerCase() === value)) { newItem = ''; return; }
    const targetAction = isWindowsShell(value) && (newAction === 'kill' || newAction === 'strict') ? 'hide' : newAction;
    const entry: BlockEntry = addingDuration > 0
      ? { i: value, m: 't', e: new Date(Date.now() + addingDuration * 60_000).toISOString(), a: targetAction }
      : { i: value, m: 'u', a: targetAction };
    const previous = items;
    const next = [...items, entry];
    items = next; newItem = ''; showSuggestions = false;
    if (!await saveAll(next)) items = previous;
  }

  async function removeTarget(index: number) {
    const previous = items;
    const next = items.filter((_, itemIndex) => itemIndex !== index);
    items = next;
    if (!await saveAll(next, () => removeTarget(index))) items = previous;
  }

  async function changeAction(index: number, action: Action) {
    const previous = items;
    const next = items.map((entry, itemIndex) => itemIndex === index ? { ...entry, a: action } : entry);
    items = next;
    if (!await saveAll(next, () => changeAction(index, action))) items = previous;
  }

  async function saveNotification() {
    if (notificationSaving) return;
    notificationSaving = true;
    try {
      const customSeconds = Math.max(5, Math.min(86400, Math.round(customIntervalAmount * customIntervalUnit)));
      const blockNotifyIntervalSeconds = notifyIntervalPreset === -1 ? customSeconds : notifyIntervalPreset;
      const response = await fetch('/api/settings', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ blockTitle, blockMessage, blockNotifyIntervalSeconds, blockNotifyPosition }) });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || 'Could not save message');
      blockTitle = blockTitle.replace(/\s+/g, ' ').trim() || DEFAULT_TITLE;
      blockMessage = blockMessage.replace(/\s+/g, ' ').trim() || DEFAULT_MESSAGE;
      notificationDirty = false; notificationStatus = 'Saved';
      setTimeout(() => notificationStatus = '', 2200);
    } catch (error) { errorMessage = error instanceof Error ? error.message : 'Could not save message'; }
    finally { notificationSaving = false; }
  }

  function readFile(file: File): Promise<string> {
    return new Promise((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsDataURL(file); });
  }
  function createVideoPoster(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
      const url = URL.createObjectURL(file);
      const video = document.createElement('video');
      const finish = () => URL.revokeObjectURL(url);
      const timeout = window.setTimeout(() => { finish(); reject(new Error('Could not read a preview frame from this video')); }, 12000);
      video.muted = true; video.preload = 'auto'; video.playsInline = true;
      video.onerror = () => { clearTimeout(timeout); finish(); reject(new Error('This video could not be read')); };
      video.onloadeddata = () => {
        try {
          const scale = Math.min(1, 640 / Math.max(video.videoWidth, video.videoHeight));
          const canvas = document.createElement('canvas');
          canvas.width = Math.max(1, Math.round(video.videoWidth * scale));
          canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
          canvas.getContext('2d')?.drawImage(video, 0, 0, canvas.width, canvas.height);
          clearTimeout(timeout); finish(); resolve(canvas.toDataURL('image/jpeg', .84));
        } catch (error) { clearTimeout(timeout); finish(); reject(error); }
      };
      video.src = url;
    });
  }
  async function uploadImage(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0]; input.value = '';
    if (!file) return;
    const supported = ['image/png', 'image/jpeg', 'image/gif', 'video/mp4', 'video/webm'];
    const isVideo = file.type.startsWith('video/');
    const limit = isVideo ? 8 * 1024 * 1024 : 4 * 1024 * 1024;
    if (!supported.includes(file.type) || file.size > limit) { errorMessage = 'Use PNG, JPEG, GIF (up to 4 MB), MP4, or WebM (up to 8 MB)'; return; }
    imageUploading = true;
    try {
      const dataUrl = await readFile(file);
      const posterDataUrl = isVideo ? await createVideoPoster(file) : undefined;
      const response = await fetch('/api/block/media', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ dataUrl, posterDataUrl }) });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || 'Upload failed');
      blockImageVersion = result.version || String(Date.now()); blockMediaType = result.mediaType || file.type; notificationStatus = 'Banner added';
    } catch (error) { errorMessage = error instanceof Error ? error.message : 'Could not add image'; }
    finally { imageUploading = false; }
  }
  async function removeImage() {
    imageUploading = true;
    try { const response = await fetch('/api/block/media', { method: 'DELETE' }); if (!response.ok) throw new Error(); blockImageVersion = ''; blockMediaType = ''; }
    catch { errorMessage = 'Could not remove image'; }
    finally { imageUploading = false; }
  }

  async function testApp(entry: BlockEntry) {
    try {
      const action = effectiveAction(entry);
      const response = await fetch(action === 'notify' ? '/api/block/preview' : '/api/block/enforce', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(action === 'notify' ? { target: entry.i } : { exe: entry.i }),
      });
      if (!response.ok) throw new Error((await response.json().catch(() => ({}))).error || 'Test failed');
      lastTested = entry.i; setTimeout(() => lastTested = '', 1800);
    } catch (error) { errorMessage = error instanceof Error ? error.message : 'Test failed'; }
  }

  function formatPreview(template: string, target: string) {
    return (template || DEFAULT_MESSAGE).replace(/\{target\}/gi, target).replace(/\{mode\}/gi, newAction);
  }
  function timeLabel(entry: BlockEntry) {
    if (entry.m !== 't' || !entry.e) return 'No limit';
    const minutes = Math.ceil((new Date(entry.e).getTime() - Date.now()) / 60000);
    if (minutes <= 0) return 'Expired';
    return minutes >= 60 ? `${Math.ceil(minutes / 60)}h left` : `${minutes}m left`;
  }
  function iconFor(entry: BlockEntry) { return appIcon(entry.i) || (isApp(entry) ? 'ti-apps' : 'ti-world'); }

  let availableActions = $derived(actionsFor(targetKind, targetKind === 'app' ? sanitizeApp(newItem) : newItem));
  let previewTarget = $derived(targetKind === 'website' ? 'youtube.com' : 'example.exe');
  let suggestions = $derived.by(() => {
    if (targetKind !== 'app') return [];
    const query = newItem.toLowerCase().trim();
    const used = new Set(items.map((entry) => entry.i.toLowerCase()));
    return runningProcs.filter((name) => !used.has(name.toLowerCase()) && (!query || name.toLowerCase().includes(query)));
  });

  onMount(() => {
    load();
    const processTimer = setInterval(loadRunning, 5000);
    const extensionTimer = setInterval(loadExtensionStatus, 15000);
    return () => { clearInterval(processTimer); clearInterval(extensionTimer); };
  });
</script>

<div class="block-page">
  {#if errorMessage}<div class="notice error" role="alert"><i class="ti ti-alert-circle"></i><span>{errorMessage}</span><button onclick={() => errorMessage = ''} aria-label="Dismiss">×</button></div>{/if}

  <section class="focus-bar">
    <div class="focus-state"><span class:active={focusMode}><i class="ti ti-shield-check"></i></span><div><strong>{focusMode ? 'Protection on' : 'Protection off'}</strong><small>{items.length} {items.length === 1 ? 'target' : 'targets'}</small></div></div>
    <div class="focus-actions">
      {#if blockProtectionEnabled}<span class="locked"><i class="ti ti-lock"></i>Protected</span>{/if}
      <label class="switch-label"><span>{focusMode ? 'On' : 'Off'}</span><input class="toggle" type="checkbox" checked={focusMode} onchange={(event) => saveFocus((event.currentTarget as HTMLInputElement).checked)} /></label>
    </div>
  </section>

  <div class="workspace">
    <section class="panel message-panel">
      <header><div><span class="kicker">MESSAGE</span><h2>Block screen & reminder</h2></div><button class="save" onclick={saveNotification} disabled={!notificationDirty || notificationSaving}>{notificationSaving ? 'Saving…' : notificationStatus || 'Save'}</button></header>
      <div class="preview-shell" class:browser={targetKind === 'website'}>
        <div class="preview" class:has-image={!!blockImageVersion}>
          {#if blockImageVersion && blockMediaType.startsWith('video/')}<video src={`/api/block/media?v=${encodeURIComponent(blockImageVersion)}`} autoplay muted loop playsinline aria-label="Custom reminder video"></video>{:else if blockImageVersion}<img src={`/api/block/media?v=${encodeURIComponent(blockImageVersion)}`} alt="Custom reminder" />{:else}<span class="preview-icon"><i class="ti {targetKind === 'website' ? 'ti-world' : 'ti-bell'}"></i></span>{/if}
          <div><span>{targetKind === 'website' ? 'Browser' : 'Windows'} · {newAction}</span><strong>{formatPreview(blockTitle || DEFAULT_TITLE, previewTarget)}</strong><p>{formatPreview(blockMessage, previewTarget)}</p></div>
        </div>
      </div>
      <div class="message-fields">
        <label><span>Title</span><input maxlength="60" bind:value={blockTitle} oninput={() => notificationDirty = true} /></label>
        <label class="message-field"><span>Message</span><input maxlength="240" bind:value={blockMessage} oninput={() => notificationDirty = true} /></label>
      </div>
      <div class="notify-settings">
        <label><span>Repeat reminder</span><select bind:value={notifyIntervalPreset} onchange={() => notificationDirty = true}>{#each NOTIFY_INTERVALS as option}<option value={option.value}>{option.label}</option>{/each}</select></label>
        {#if notifyIntervalPreset === -1}<label class="custom-interval"><span>Custom interval</span><div><input type="number" min="1" max="86400" bind:value={customIntervalAmount} oninput={() => notificationDirty = true} /><select bind:value={customIntervalUnit} onchange={() => notificationDirty = true}><option value={1}>seconds</option><option value={60}>minutes</option><option value={3600}>hours</option></select></div></label>{/if}
        <label><span>Toast position</span><select bind:value={blockNotifyPosition} onchange={() => notificationDirty = true}><option value="left">Bottom left</option><option value="right">Bottom right</option></select></label>
      </div>
      <div class="image-actions"><label class="file-button"><i class="ti ti-photo-plus"></i>{blockImageVersion ? 'Replace banner' : 'Add banner'}<input type="file" accept="image/png,image/jpeg,image/gif,video/mp4,video/webm" onchange={uploadImage} disabled={imageUploading} /></label>{#if blockImageVersion}<button class="plain danger" onclick={removeImage} disabled={imageUploading}><i class="ti ti-trash"></i>Remove</button>{/if}<small>PNG/JPEG/GIF · 4 MB &nbsp; MP4/WebM · 8 MB</small></div>
    </section>

    <section class="panel add-panel">
      <header><div><span class="kicker">NEW TARGET</span><h2>Add an app or website</h2></div></header>
      <div class="kind-tabs" role="tablist"><button class:active={targetKind === 'app'} onclick={() => setTargetKind('app')}><i class="ti ti-device-desktop"></i>App</button><button class:active={targetKind === 'website'} onclick={() => setTargetKind('website')}><i class="ti ti-world"></i>Website</button></div>
      <div class="field-label">Mode</div>
      <div class="mode-row" style={`grid-template-columns:repeat(${availableActions.length},minmax(0,1fr))`}>{#each availableActions as action}<button class="tooltip-button" class:active={newAction === action.id} data-tooltip={action.description} aria-label={`${action.label}: ${action.description}`} onclick={() => newAction = action.id}><i class="ti {action.icon}"></i>{action.label}</button>{/each}</div>
      <div class="target-picker">
        <label class="target-input"><span>{targetKind === 'app' ? 'App name' : 'Domain'}</span><div><i class="ti ti-search"></i><input bind:value={newItem} placeholder={targetKind === 'app' ? 'Search running apps' : 'youtube.com'} oninput={(event) => keepShellModeSafe(event.currentTarget.value)} onfocus={() => showSuggestions = true} onkeydown={(event) => { if (event.key === 'Enter') addTarget(); if (event.key === 'Escape') showSuggestions = false; }} /><button onclick={() => addTarget()} disabled={!newItem.trim() || saving}><i class="ti ti-plus"></i>Add</button></div></label>
        {#if targetKind === 'app' && showSuggestions}
          <div class="suggestions">
            <div class="suggestions-head"><span>Open apps</span><button type="button" onclick={loadRunning} aria-label="Refresh open apps"><i class="ti ti-refresh"></i>Refresh</button></div>
            {#if suggestions.length}
              {#each suggestions as process}<button type="button" onclick={() => addTarget(process)}><i class="ti {appIcon(process) || 'ti-apps'}"></i><span>{process}</span>{#if isWindowsShell(process)}<small>Notify or Hide</small>{/if}</button>{/each}
            {:else}<div class="suggestions-empty">No matching open apps</div>{/if}
          </div>
        {/if}
      </div>
      <label class="duration"><span>Duration</span><select bind:value={addingDuration}>{#each DURATIONS as duration}<option value={duration.value}>{duration.label}</option>{/each}</select></label>
      <div class="mode-note"><i class="ti {targetKind === 'website' ? (newAction === 'notify' ? 'ti-bell' : 'ti-shield-lock') : 'ti-device-desktop'}"></i><span>{targetKind === 'website' ? (newAction === 'notify' ? 'Shows a repeating toast on the left and leaves the site usable.' : 'Shows your custom full-page screen until the target is removed.') : 'Desktop apps support Notify, Hide, Kill, and Strict.'}</span></div>
    </section>
  </div>

  <section class="panel targets-panel">
    <header><div><span class="kicker">TARGETS</span><h2>Your focus list</h2></div><span class="extension" class:ready={extensionConnected}><i class="ti ti-point-filled"></i>{extensionConnected ? `Web ready${extensionBrowser ? ` · ${extensionBrowser}` : ''}` : 'Extension offline'}</span></header>
    {#if items.length}
      <div class="target-list">
        {#each items as entry, index}
          <div class="target-row">
            <span class="target-icon"><i class="ti {iconFor(entry)}"></i></span>
            <div class="target-name"><strong>{entry.i}</strong><span>{isApp(entry) ? 'Desktop app' : 'Website'} · {timeLabel(entry)}</span></div>
            <div class="action-pills">{#each actionsFor(isApp(entry) ? 'app' : 'website', entry.i) as action}<button class="tooltip-button" class:active={effectiveAction(entry) === action.id} data-tooltip={action.description} onclick={() => changeAction(index, action.id)} aria-label={`${action.label} ${entry.i}: ${action.description}`}><i class="ti {action.icon}"></i><span>{action.label}</span></button>{/each}</div>
            {#if isApp(entry)}<button class="icon-button" onclick={() => testApp(entry)} disabled={!focusMode} title="Test now" aria-label={`Test ${entry.i}`}><i class="ti {lastTested === entry.i ? 'ti-check' : 'ti-player-play'}"></i></button>{/if}
            <button class="icon-button remove" onclick={() => removeTarget(index)} title="Remove and unblock" aria-label={`Remove ${entry.i}`} disabled={saving}><i class="ti ti-trash"></i></button>
          </div>
        {/each}
      </div>
    {:else}<div class="empty"><i class="ti ti-shield-plus"></i><strong>No targets yet</strong><span>Add an app or website above.</span></div>{/if}
  </section>
</div>

{#if showUnlock}
  <div class="modal-backdrop" role="presentation" onclick={(event) => { if (event.currentTarget === event.target) showUnlock = false; }}>
    <div class="unlock-modal" role="dialog" aria-modal="true" aria-labelledby="unlock-title"><button class="modal-close" onclick={() => showUnlock = false}>×</button><span class="modal-icon"><i class="ti ti-lock"></i></span><h2 id="unlock-title">Unlock protected blocks</h2><p>Enter your password to make this change.</p><input type="password" bind:value={unlockPassword} placeholder="Password" onkeydown={(event) => { if (event.key === 'Enter') unlockProtection(); }} />{#if unlockError}<div class="unlock-error">{unlockError}</div>{/if}<button class="unlock-button" onclick={unlockProtection} disabled={!unlockPassword || unlocking}>{unlocking ? 'Unlocking…' : 'Unlock'}</button></div>
  </div>
{/if}

<style>
  .block-page{display:grid;gap:14px}.notice{display:flex;align-items:center;gap:8px;padding:10px 12px;border-radius:10px;font-size:12px}.notice.error{color:var(--md-error);background:var(--md-err-cont);border:1px solid color-mix(in srgb,var(--md-error) 28%,transparent)}.notice span{flex:1}.notice button{border:0;background:transparent;color:inherit;font-size:18px;cursor:pointer}
  .focus-bar,.panel{background:var(--clr-bg-sec);border:1px solid var(--clr-border);border-radius:var(--shape-lg)}.focus-bar{min-height:66px;display:flex;align-items:center;justify-content:space-between;padding:12px 16px}.focus-state,.focus-actions,.switch-label,.panel header,.image-actions,.target-row{display:flex;align-items:center}.focus-state{gap:11px}.focus-state>span{width:38px;height:38px;display:grid;place-items:center;border-radius:11px;color:var(--clr-text-sec);background:var(--clr-bg-ter)}.focus-state>span.active{color:var(--md-primary);background:var(--md-primary-cont)}.focus-state div{display:grid;gap:2px}.focus-state strong{font-size:14px;color:var(--clr-text-pri)}.focus-state small{font-size:11px;color:var(--clr-text-sec)}.focus-actions{gap:10px}.locked,.extension{display:inline-flex;align-items:center;gap:5px;padding:5px 8px;border-radius:999px;background:var(--clr-bg-ter);color:var(--clr-text-sec);font-size:10px}.switch-label{gap:8px;color:var(--clr-text-pri);font-size:11px;font-weight:600}.toggle{appearance:none;width:40px;height:22px;margin:0;position:relative;border:0;border-radius:99px;background:var(--clr-border-strong);cursor:pointer}.toggle:after{content:'';position:absolute;width:18px;height:18px;top:2px;left:2px;border-radius:50%;background:white;transition:transform .18s}.toggle:checked{background:var(--md-primary)}.toggle:checked:after{transform:translateX(18px)}
  .workspace{display:grid;grid-template-columns:minmax(0,1.15fr) minmax(360px,.85fr);gap:14px}.panel{overflow:hidden}.panel header{min-height:62px;justify-content:space-between;gap:12px;padding:14px 16px;border-bottom:1px solid var(--clr-border)}.panel header>div{display:grid;gap:2px}.kicker{color:var(--md-primary);font-size:9px;font-weight:700;letter-spacing:.12em}.panel h2{margin:0;color:var(--clr-text-pri);font-size:14px}.save{height:31px;padding:0 12px;border:1px solid var(--md-primary);border-radius:8px;background:var(--md-primary);color:var(--md-on-primary);font:600 11px inherit;cursor:pointer}.save:disabled{opacity:.4;cursor:not-allowed}
  .preview-shell{padding:16px 16px 8px}.preview-shell.browser{display:flex;justify-content:flex-start}.preview{min-height:78px;display:flex;align-items:center;gap:12px;padding:12px;border-left:3px solid var(--md-primary);border-radius:12px;background:#151b1d;border-top:1px solid #2a3639;border-right:1px solid #2a3639;border-bottom:1px solid #2a3639;box-shadow:0 10px 26px rgba(0,0,0,.2)}.browser .preview{width:min(390px,100%)}.preview img,.preview video,.preview-icon{width:52px;height:52px;flex:0 0 52px;border-radius:9px}.preview img,.preview video{object-fit:cover;background:#0b1113}.preview-icon{display:grid;place-items:center;color:var(--md-primary);background:var(--md-primary-cont);font-size:20px}.preview>div{min-width:0;display:grid;gap:2px}.preview span{color:var(--md-primary);font-size:9px;text-transform:uppercase;letter-spacing:.08em}.preview strong{color:#f3f7f6;font-size:13px}.preview p{margin:0;color:#b3c2bf;font-size:11px;line-height:1.35;white-space:normal;overflow-wrap:anywhere}
  .message-fields{display:grid;grid-template-columns:minmax(150px,.5fr) minmax(260px,1fr);gap:10px;padding:8px 16px}.message-fields label,.target-input,.duration,.notify-settings label{display:grid;gap:5px}.message-fields label>span,.target-input>span,.duration>span,.notify-settings label>span,.field-label{color:var(--clr-text-sec);font-size:10px;font-weight:600}.message-fields input,.target-input input,.duration select,.notify-settings input,.notify-settings select,.unlock-modal input{height:36px;min-width:0;padding:0 10px;color:var(--clr-text-pri);background:var(--clr-bg-ter);border:1px solid var(--clr-border);border-radius:8px;font:12px inherit;outline:none}.message-fields input:focus,.target-input input:focus,.duration select:focus,.notify-settings input:focus,.notify-settings select:focus,.unlock-modal input:focus{border-color:var(--md-primary)}.notify-settings{display:grid;grid-template-columns:repeat(2,minmax(140px,1fr));gap:10px;padding:8px 16px}.notify-settings:has(.custom-interval){grid-template-columns:repeat(3,minmax(120px,1fr))}.custom-interval>div{display:grid;grid-template-columns:minmax(72px,.7fr) 1fr;gap:6px}.image-actions{gap:8px;padding:8px 16px 16px}.file-button,.plain{height:30px;display:inline-flex;align-items:center;gap:6px;padding:0 9px;border:1px solid var(--clr-border);border-radius:8px;background:var(--clr-bg-ter);color:var(--clr-text-sec);font:11px inherit;cursor:pointer}.file-button input{display:none}.plain.danger:hover{color:var(--md-error);border-color:var(--md-error)}.image-actions small{margin-left:auto;color:var(--clr-text-ter);font-size:9px}
  .add-panel{position:relative;padding-bottom:14px;overflow:visible}.add-panel header{margin-bottom:12px}.kind-tabs{display:grid;grid-template-columns:1fr 1fr;gap:4px;margin:0 16px 12px;padding:4px;background:var(--clr-bg-ter);border-radius:10px}.kind-tabs button,.mode-row button,.action-pills button{border:1px solid transparent;background:transparent;color:var(--clr-text-sec);font:11px inherit;cursor:pointer}.kind-tabs button{height:32px;border-radius:7px}.kind-tabs button.active{color:var(--clr-text-pri);background:var(--clr-bg-sec);border-color:var(--clr-border)}.kind-tabs i,.mode-row i{margin-right:5px}.field-label{margin:0 16px 6px}.mode-row{display:grid;grid-template-columns:repeat(4,1fr);gap:6px;margin:0 16px 12px;overflow:visible}.mode-row button{height:34px;border-color:var(--clr-border);border-radius:8px;background:var(--clr-bg-ter)}.mode-row button.active{color:var(--md-primary);border-color:var(--md-primary);background:var(--md-primary-cont)}.target-picker{margin:0 16px 10px}.target-input{margin:0}.target-input>div{height:38px;display:flex;align-items:center;padding-left:10px;border:1px solid var(--clr-border);border-radius:9px;background:var(--clr-bg-ter)}.target-input>div:focus-within{border-color:var(--md-primary)}.target-input>div>i{color:var(--clr-text-ter)}.target-input input{height:36px;flex:1;border:0;background:transparent}.target-input button{height:30px;margin-right:3px;padding:0 10px;border:0;border-radius:7px;background:var(--md-primary);color:var(--md-on-primary);font:600 11px inherit;cursor:pointer}.target-input button i{margin-right:4px}.suggestions{max-height:220px;overflow:auto;margin-top:6px;padding:5px;border:1px solid var(--clr-border);border-radius:10px;background:var(--clr-bg-sec);box-shadow:var(--shadow-md)}.suggestions>button{width:100%;height:36px;display:flex;align-items:center;gap:8px;padding:0 8px;border:0;border-radius:7px;background:transparent;color:var(--clr-text-pri);font:11px inherit;cursor:pointer}.suggestions>button:hover{background:var(--clr-bg-ter)}.suggestions>button span{min-width:0;flex:1;overflow:hidden;text-overflow:ellipsis;text-align:left}.suggestions>button small{color:var(--clr-text-ter);font-size:9px}.suggestions-head{height:28px;display:flex;align-items:center;justify-content:space-between;padding:0 7px;color:var(--clr-text-ter);font-size:9px;font-weight:700;letter-spacing:.08em;text-transform:uppercase}.suggestions-head button{display:inline-flex;align-items:center;gap:4px;border:0;background:transparent;color:var(--md-primary);font:9px inherit;cursor:pointer}.suggestions-empty{padding:14px 8px;color:var(--clr-text-ter);font-size:10px;text-align:center}.duration{margin:0 16px 10px}.duration select{width:100%}.mode-note{display:flex;align-items:flex-start;gap:8px;margin:0 16px;padding:9px 10px;border-radius:9px;background:color-mix(in srgb,var(--md-primary) 6%,var(--clr-bg-ter));color:var(--clr-text-sec);font-size:10px;line-height:1.4}.mode-note i{color:var(--md-primary);font-size:14px}
  .targets-panel{overflow:visible}.targets-panel header{min-height:58px}.extension.ready{color:var(--md-primary);background:var(--md-primary-cont)}.target-list{display:grid}.target-row{min-height:64px;gap:10px;padding:9px 12px;border-top:1px solid var(--clr-border)}.target-row:first-child{border-top:0}.target-icon{width:36px;height:36px;display:grid;place-items:center;flex:0 0 36px;border-radius:10px;color:var(--md-primary);background:var(--md-primary-cont)}.target-name{min-width:150px;flex:1;display:grid;gap:2px}.target-name strong{color:var(--clr-text-pri);font:12px var(--font-mono);overflow:hidden;text-overflow:ellipsis}.target-name span{color:var(--clr-text-sec);font-size:10px}.action-pills{display:flex;gap:4px}.action-pills button{height:30px;display:flex;align-items:center;gap:4px;padding:0 8px;border-color:var(--clr-border);border-radius:7px;background:var(--clr-bg-ter)}.action-pills button.active{color:var(--md-primary);border-color:var(--md-primary);background:var(--md-primary-cont)}.icon-button{width:32px;height:32px;display:grid;place-items:center;border:1px solid var(--clr-border);border-radius:8px;background:var(--clr-bg-ter);color:var(--clr-text-sec);cursor:pointer}.icon-button:hover{color:var(--md-primary);border-color:var(--md-primary)}.icon-button.remove:hover{color:var(--md-error);border-color:var(--md-error)}.icon-button:disabled{opacity:.35;cursor:not-allowed}.empty{min-height:150px;display:grid;place-items:center;align-content:center;gap:4px;color:var(--clr-text-sec)}.empty i{font-size:26px;color:var(--md-primary)}.empty strong{font-size:13px;color:var(--clr-text-pri)}.empty span{font-size:11px}
  .tooltip-button{position:relative}.tooltip-button::after{content:attr(data-tooltip);position:absolute;z-index:40;left:50%;top:calc(100% + 8px);width:max-content;max-width:220px;padding:7px 9px;border:1px solid var(--clr-border-strong);border-radius:8px;background:var(--clr-bg-pri);color:var(--clr-text-pri);box-shadow:var(--shadow-md);font:10px/1.35 var(--font-display);text-align:left;white-space:normal;opacity:0;pointer-events:none;transform:translate(-50%,-3px);transition:opacity .14s,transform .14s}.tooltip-button:hover::after,.tooltip-button:focus-visible::after{opacity:1;transform:translate(-50%,0)}
  .modal-backdrop{position:fixed;inset:0;z-index:1000;display:grid;place-items:center;padding:20px;background:rgba(0,0,0,.62);backdrop-filter:blur(5px)}.unlock-modal{width:min(380px,100%);position:relative;display:grid;gap:11px;padding:24px;border:1px solid var(--clr-border);border-radius:16px;background:var(--clr-bg-sec);box-shadow:var(--shadow-lg)}.unlock-modal h2,.unlock-modal p{margin:0}.unlock-modal h2{font-size:17px}.unlock-modal p{color:var(--clr-text-sec);font-size:12px}.modal-icon{width:40px;height:40px;display:grid;place-items:center;border-radius:11px;color:var(--md-primary);background:var(--md-primary-cont)}.modal-close{position:absolute;right:12px;top:10px;border:0;background:transparent;color:var(--clr-text-sec);font-size:20px;cursor:pointer}.unlock-button{height:36px;border:0;border-radius:8px;background:var(--md-primary);color:var(--md-on-primary);font:600 12px inherit;cursor:pointer}.unlock-error{color:var(--md-error);font-size:11px}
  @media(max-width:1000px){.workspace{grid-template-columns:1fr}}
  @media(max-width:760px){.message-fields,.notify-settings,.notify-settings:has(.custom-interval){grid-template-columns:1fr}.target-row{flex-wrap:wrap}.action-pills{order:3;width:100%;overflow:auto}.action-pills button{flex:1;justify-content:center}.action-pills span{display:none}.focus-bar{align-items:flex-start}.locked{display:none}}
  @media(max-width:500px){.mode-row{grid-template-columns:repeat(2,1fr)}.focus-state small{display:none}.preview-shell{padding:12px 12px 6px}.message-fields,.notify-settings,.image-actions{padding-left:12px;padding-right:12px}.image-actions{flex-wrap:wrap}.image-actions small{width:100%;margin-left:0}}
</style>
