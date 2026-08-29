<script lang="ts">
  import { onMount } from 'svelte';
  import { appIcon } from '../appIcons';

  type BlockEntry = { i: string; m: 'u' | 't'; e?: string };
  type RetryAction = () => Promise<void>;
  type TargetKind = 'app' | 'website';
  const DEFAULT_BLOCK_MESSAGE = "'{target}' is blocked — get back to work!";
  const TARGET_PLACEHOLDER = '{target}';
  const MODE_PLACEHOLDER = '{mode}';

  let items = $state<BlockEntry[]>([]);
  let newItem = $state('');
  let targetKind = $state<TargetKind>('app');
  let blockAction = $state('hide');
  let blockTitle = $state('Focus Mode');
  let blockMessage = $state(DEFAULT_BLOCK_MESSAGE);
  let blockImageVersion = $state('');
  let notificationDirty = $state(false);
  let notificationSaving = $state(false);
  let imageUploading = $state(false);
  let notificationStatus = $state<string | null>(null);
  let focusMode = $state(false);
  let apiOk = $state(true);
  let showAddDropdown = $state(false);
  let runningProcs = $state<string[]>([]);
  let extensionConnected = $state(false);
  let extensionBrowser = $state('');
  let blockStats = $state<{ exe: string; action: string; count: number }[]>([]);
  let lastBlockToast = $state<string | null>(null);
  let addingDuration = $state(0);
  let recentlyRemoved = $state<{ entry: BlockEntry; index: number } | null>(null);
  let errorMessage = $state<string | null>(null);
  let saving = $state(false);
  let blockProtectionEnabled = $state(false);
  let unlockToken = $state('');
  let showUnlock = $state(false);
  let unlockPassword = $state('');
  let unlockError = $state('');
  let unlocking = $state(false);
  let pendingAction: (() => Promise<void>) | null = null;
  let undoTimer: ReturnType<typeof setTimeout> | undefined;

  const API = '';
  const DURATIONS = [
    { value: 0, label: 'Until unblocked' },
    { value: 15, label: '15 min' },
    { value: 30, label: '30 min' },
    { value: 60, label: '1 hour' },
    { value: 120, label: '2 hours' },
    { value: 240, label: '4 hours' },
  ];

  async function load() {
    try {
      const r = await fetch(`${API}/api/settings`);
      if (!r.ok) throw new Error(`Settings request failed (${r.status})`);
      const s = await r.json();
      blockAction = s.blockAction || 'hide';
      blockTitle = s.blockTitle || 'Focus Mode';
      blockMessage = s.blockMessage || DEFAULT_BLOCK_MESSAGE;
      blockImageVersion = s.blockImageVersion || '';
      notificationDirty = false;
      focusMode = s.focusMode ?? false;
      blockProtectionEnabled = s.blockProtectionEnabled ?? false;
      const raw = s.focusBlocklist || '[]';
      try {
        const parsed = JSON.parse(raw);
        items = Array.isArray(parsed) ? parsed.filter((entry) => entry && typeof entry.i === 'string') : [];
      } catch { items = []; }
      apiOk = true;
      errorMessage = null;
    } catch (error) {
      apiOk = false;
      errorMessage = error instanceof Error ? error.message : 'Unable to load blocking settings';
    }
    loadRunning();
    loadStats();
    loadExtensionStatus();
  }

  async function loadRunning() {
    try {
      const r = await fetch(`${API}/api/running-processes`);
      if (!r.ok) throw new Error();
      runningProcs = await r.json();
    } catch { runningProcs = []; }
  }

  async function loadStats() {
    try {
      const r = await fetch(`${API}/api/block/stats`);
      if (!r.ok) throw new Error();
      blockStats = await r.json();
    } catch { blockStats = []; }
  }

  async function postProtectedSetting(payload: Record<string, unknown>, retry: RetryAction | null): Promise<Response | null> {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (unlockToken) headers['X-TimeLens-Unlock'] = unlockToken;
    const response = await fetch(`${API}/api/settings`, {
      method: 'POST', headers, body: JSON.stringify(payload),
    });
    if (response.status === 423) {
      unlockToken = '';
      pendingAction = retry ?? null;
      unlockPassword = '';
      unlockError = '';
      showUnlock = true;
      return null;
    }
    return response;
  }

  async function unlockProtection() {
    if (!unlockPassword || unlocking) return;
    unlocking = true;
    unlockError = '';
    try {
      const response = await fetch(`${API}/api/block/protection/unlock`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ password: unlockPassword }),
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || 'Could not unlock protected blocks');
      unlockToken = result.token || '';
      showUnlock = false;
      unlockPassword = '';
      const action = pendingAction;
      pendingAction = null;
      if (action) await action();
    } catch (error) {
      unlockError = error instanceof Error ? error.message : 'Could not unlock protected blocks';
    } finally { unlocking = false; }
  }

  function closeUnlock() {
    showUnlock = false;
    unlockPassword = '';
    unlockError = '';
    pendingAction = null;
  }

  async function saveAll(list: BlockEntry[], retry: RetryAction | null): Promise<boolean> {
    saving = true;
    try {
      const response = await postProtectedSetting({ focusBlocklist: JSON.stringify(list) }, retry);
      if (response === null) return false;
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error || `Save failed (${response.status})`);
      apiOk = true;
      errorMessage = null;
      return true;
    } catch (error) {
      apiOk = false;
      errorMessage = error instanceof Error ? error.message : 'Unable to save blocklist';
      return false;
    } finally { saving = false; }
  }

  async function saveFocus(val: boolean) {
    const previous = focusMode;
    focusMode = val;
    try {
      const response = await postProtectedSetting({ focusMode: val }, () => saveFocus(val));
      if (response === null) { focusMode = previous; return; }
      if (!response.ok) throw new Error(`Save failed (${response.status})`);
      apiOk = true;
      errorMessage = null;
    } catch {
      focusMode = previous;
      apiOk = false;
      errorMessage = 'Focus Mode could not be updated';
    }
  }

  async function setAction(action: string) {
    const previous = blockAction;
    blockAction = action;
    try {
      const response = await postProtectedSetting({ blockAction: action }, () => setAction(action));
      if (response === null) { blockAction = previous; return; }
      if (!response.ok) throw new Error(`Save failed (${response.status})`);
      apiOk = true;
      errorMessage = null;
    } catch {
      blockAction = previous;
      apiOk = false;
      errorMessage = 'Block action could not be updated';
    }
  }

  async function loadExtensionStatus() {
    try {
      const r = await fetch(`${API}/api/extension-status`);
      if (!r.ok) throw new Error();
      const status = await r.json();
      extensionConnected = status.connected === true;
      extensionBrowser = typeof status.browser === 'string' && status.browser !== 'unknown' ? status.browser : '';
    } catch {
      extensionConnected = false;
      extensionBrowser = '';
    }
  }

  async function saveNotification(): Promise<boolean> {
    if (notificationSaving) return false;
    notificationSaving = true;
    notificationStatus = null;
    try {
      const response = await fetch(`${API}/api/settings`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ blockTitle, blockMessage }),
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || `Save failed (${response.status})`);
      blockTitle = blockTitle.replace(/\s+/g, ' ').trim() || 'Focus Mode';
      blockMessage = blockMessage.replace(/\s+/g, ' ').trim() || DEFAULT_BLOCK_MESSAGE;
      notificationDirty = false;
      notificationStatus = 'Saved';
      return true;
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : 'Could not save the reminder';
      return false;
    } finally { notificationSaving = false; }
  }

  async function previewNotification() {
    if (notificationDirty && !await saveNotification()) return;
    try {
      const response = await fetch(`${API}/api/block/preview`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ target: 'example.exe' }),
      });
      if (!response.ok) throw new Error(`Preview failed (${response.status})`);
      notificationStatus = 'Preview sent to your desktop';
      setTimeout(() => notificationStatus = null, 3000);
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : 'Could not show the preview';
    }
  }

  function readFileAsDataUrl(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => typeof reader.result === 'string' ? resolve(reader.result) : reject(new Error('Could not read image'));
      reader.onerror = () => reject(new Error('Could not read image'));
      reader.readAsDataURL(file);
    });
  }

  async function uploadNotificationImage(event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (!['image/png', 'image/jpeg'].includes(file.type)) {
      errorMessage = 'Choose a PNG or JPEG image';
      return;
    }
    if (file.size > 2 * 1024 * 1024) {
      errorMessage = 'Image must be 2 MB or smaller';
      return;
    }

    imageUploading = true;
    notificationStatus = null;
    try {
      const dataUrl = await readFileAsDataUrl(file);
      const response = await fetch(`${API}/api/block/image`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ dataUrl }),
      });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error || `Upload failed (${response.status})`);
      blockImageVersion = result.version || Date.now().toString();
      notificationStatus = 'Image added';
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : 'Could not upload the image';
    } finally { imageUploading = false; }
  }

  async function removeNotificationImage() {
    imageUploading = true;
    try {
      const response = await fetch(`${API}/api/block/image`, { method: 'DELETE' });
      if (!response.ok) throw new Error(`Remove failed (${response.status})`);
      blockImageVersion = '';
      notificationStatus = 'Image removed';
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : 'Could not remove the image';
    } finally { imageUploading = false; }
  }

  function sanitizeWebsite(raw: string): string {
    // Strip URLs down to just the hostname (e.g. https://learn.microsoft.com/en-us → learn.microsoft.com)
    try {
      const u = new URL(raw);
      return u.hostname || raw;
    } catch {
      // Not a URL — strip common noise
      return raw.replace(/\/.*/, '').replace(/^https?:\/\//, '');
    }
  }

  function sanitizeApp(raw: string): string {
    let value = raw.trim().replace(/^['"]|['"]$/g, '').split(/[\\/]/).pop() ?? '';
    if (value && !value.toLowerCase().endsWith('.exe')) value += '.exe';
    return value.toLowerCase();
  }

  function normalizeTarget(raw: string): string {
    return targetKind === 'app' ? sanitizeApp(raw) : sanitizeWebsite(raw).toLowerCase();
  }

  async function add() {
    let val = newItem.trim();
    if (!val) return;
    val = normalizeTarget(val);
    const validApp = targetKind === 'app' && /^[a-z0-9][a-z0-9._ -]*\.exe$/.test(val);
    const validWebsite = targetKind === 'website' && /^[a-z0-9][a-z0-9.-]*\.[a-z0-9-]+$/.test(val);
    if (!validApp && !validWebsite) {
      errorMessage = targetKind === 'app'
        ? 'Enter an app such as discord.exe'
        : 'Enter a website such as youtube.com or paste its URL';
      return;
    }
    if (items.some(e => e.i === val)) { newItem = ''; return; }
    let entry: BlockEntry;
    if (addingDuration > 0) {
      const exp = new Date(Date.now() + addingDuration * 60_000).toISOString();
      entry = { i: val, m: 't', e: exp };
    } else {
      entry = { i: val, m: 'u' };
    }
    const previous = items;
    const next = [...items, entry];
    items = next;
    newItem = '';
    addingDuration = 0;
    showAddDropdown = false;
    if (!await saveAll(next, null)) items = previous;
  }

  function showUndo(entry: BlockEntry, index: number) {
    recentlyRemoved = { entry, index };
    if (undoTimer) clearTimeout(undoTimer);
    undoTimer = setTimeout(() => recentlyRemoved = null, 6000);
  }

  async function remove(i: number) {
    const previous = [...items];
    const removed = items[i];
    if (!removed) return;
    const next = items.filter((_, idx) => idx !== i);
    items = next;
    if (!await saveAll(next, async () => {
      items = next;
      if (await saveAll(next, null)) showUndo(removed, i);
      else items = previous;
    })) {
      items = previous;
      return;
    }
    showUndo(removed, i);
  }

  async function undoRemove() {
    const removed = recentlyRemoved;
    if (!removed) return;
    recentlyRemoved = null;
    if (undoTimer) clearTimeout(undoTimer);
    const previous = [...items];
    const next = [...items];
    next.splice(Math.min(removed.index, next.length), 0, removed.entry);
    items = next;
    if (!await saveAll(next, null)) {
      items = previous;
      showUndo(removed.entry, removed.index);
    }
  }

  async function enforceNow(exe: string) {
    try {
      const endpoint = blockAction === 'notify' ? '/api/block/preview' : '/api/block/enforce';
      const response = await fetch(`${API}${endpoint}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(blockAction === 'notify' ? { target: exe } : { exe }),
      });
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error || `Enforcement failed (${response.status})`);
      lastBlockToast = exe;
      errorMessage = null;
      setTimeout(() => lastBlockToast = null, 2000);
    } catch (error) {
      errorMessage = error instanceof Error ? error.message : 'Unable to enforce this entry';
    }
  }

  function onKeydown(e: KeyboardEvent) {
    if (e.key === 'Enter') add();
    if (e.key === 'Escape') showAddDropdown = false;
  }

  let filteredProcs = $derived.by(() => {
    if (targetKind !== 'app') return [];
    const q = newItem.trim().toLowerCase();
    const ids = new Set(items.map(e => e.i));
    if (!q) return runningProcs.filter(p => !ids.has(p));
    return runningProcs.filter(p => p.toLowerCase().includes(q) && !ids.has(p));
  });

  async function selectProc(exe: string) {
    targetKind = 'app';
    exe = sanitizeApp(exe);
    if (items.some(e => e.i === exe)) { newItem = ''; showAddDropdown = false; return; }
    let entry: BlockEntry;
    if (addingDuration > 0) {
      const exp = new Date(Date.now() + addingDuration * 60_000).toISOString();
      entry = { i: exe, m: 't', e: exp };
    } else {
      entry = { i: exe, m: 'u' };
    }
    const previous = items;
    const next = [...items, entry];
    items = next;
    newItem = '';
    addingDuration = 0;
    showAddDropdown = false;
    if (!await saveAll(next, null)) items = previous;
  }

  function isBlocked(exe: string): boolean {
    return items.some(e => e.i.endsWith('.exe') && exe.toLowerCase() === e.i.toLowerCase());
  }

  function typeIcon(id: string): string {
    if (id.includes('.exe')) return appIcon(id) ?? 'ti-apps';
    return appIcon(id) ?? 'ti-world';
  }

  function typeLabel(id: string): string {
    return id.includes('.exe') ? 'app' : 'site';
  }

  function modeLabel(entry: BlockEntry): string {
    if (entry.m === 't' && entry.e) {
      const rem = new Date(entry.e).getTime() - Date.now();
      if (rem <= 0) return 'expired';
      const m = Math.ceil(rem / 60_000);
      if (m >= 60) return `${Math.round(m / 60)}h left`;
      return `${m}m left`;
    }
    return 'always';
  }

  const modeOptions = [
    { id: 'notify', icon: 'ti-bell', label: 'Notify', desc: 'Remind without interrupting', app: 'Show the custom desktop toast and leave the app open.', web: 'Show the same reminder and allow the website to stay open.' },
    { id: 'hide', icon: 'ti-eye-off', label: 'Hide', desc: 'Move distractions out of sight', app: 'Minimize every visible window owned by the blocked app.', web: 'Replace the matching page with the extension block screen.' },
    { id: 'kill', icon: 'ti-x', label: 'Kill', desc: 'Stop the distraction now', app: 'Terminate the blocked process and its child processes immediately.', web: 'Block the matching page while leaving the browser itself running.' },
    { id: 'strict', icon: 'ti-shield', label: 'Strict', desc: 'Keep checking for relaunches', app: 'Minimize, terminate, then check every 5 seconds and stop relaunches.', web: 'Block every matching visit while Focus Mode remains active.' },
  ];
  let activeMode = $derived(modeOptions.find(option => option.id === blockAction) ?? modeOptions[1]);
  let hasWebsiteTargets = $derived(items.some(entry => !entry.i.endsWith('.exe')));

  onMount(() => {
    load();
    const processTimer = setInterval(loadRunning, 5000);
    const extensionTimer = setInterval(loadExtensionStatus, 15000);
    return () => {
      clearInterval(processTimer);
      clearInterval(extensionTimer);
      if (undoTimer) clearTimeout(undoTimer);
    };
  });
</script>

<div class="block">
    {#if !apiOk}<span class="warning">Tray app not running</span>{/if}
    {#if errorMessage}<button class="warning error-dismiss" onclick={() => errorMessage = null}>{errorMessage} <span>×</span></button>{/if}
  <div class="card focus-workflow">
    <div class="workflow-header">
      <div class="workflow-title">
        <span class="step-icon"><i class="ti ti-focus-2" aria-hidden="true"></i></span>
        <div><span class="eyebrow">Focus control</span><h2>{focusMode ? 'Protection is active' : 'Protection is paused'}</h2><p>Your list stays editable even while enforcement is off.</p></div>
      </div>
      <div class="workflow-actions">
        {#if blockProtectionEnabled}
          <span class="protection-badge" class:unlocked={!!unlockToken} title="Protected changes require your password">
            <i class="ti {unlockToken ? 'ti-lock-open' : 'ti-lock'}" aria-hidden="true"></i>
            {unlockToken ? 'Unlocked' : 'Password protected'}
          </span>
        {/if}
        <button class="refresh-btn" onclick={() => { loadStats(); loadRunning(); }} title="Refresh block activity"><i class="ti ti-refresh" aria-hidden="true"></i><span>Refresh</span></button>
        <label class="master-switch">
          <span>{focusMode ? 'On' : 'Off'}</span>
          <input type="checkbox" class="toggle" checked={focusMode} onchange={() => saveFocus(!focusMode)} aria-label="Enable Focus Mode" />
        </label>
      </div>
    </div>
    <div class="mode-section">
      <div class="section-heading"><span class="step-number">1</span><div><strong>Choose enforcement</strong><span>What TimeLens should do when a target opens</span></div></div>
      <div class="mode-grid">
        {#each modeOptions as { id, icon, label, desc }}
          <button class="mode-card" class:active={blockAction === id} onclick={() => setAction(id)} aria-pressed={blockAction === id}>
            <span class="mode-icon"><i class="ti {icon}" aria-hidden="true"></i></span>
            <span class="mode-copy"><strong>{label}</strong><small>{desc}</small></span>
            {#if blockAction === id}<i class="ti ti-check mode-check" aria-hidden="true"></i>{/if}
          </button>
        {/each}
      </div>
      <div class="mode-outcome" aria-live="polite">
        <div class="outcome-title">
          <span class="mode-icon"><i class="ti {activeMode.icon}" aria-hidden="true"></i></span>
          <div><span>Selected behavior</span><strong>{activeMode.label}</strong></div>
        </div>
        <div class="outcome-item">
          <span class="outcome-kind"><i class="ti ti-app-window" aria-hidden="true"></i>Desktop apps</span>
          <p>{activeMode.app}</p>
        </div>
        <div class="outcome-item">
          <span class="outcome-kind"><i class="ti ti-world" aria-hidden="true"></i>Websites</span>
          <p>{activeMode.web}</p>
        </div>
      </div>
    </div>
  </div>

  <div class="card reminder-card">
    <div class="card-header flex-between">
      <div class="section-heading compact"><span class="step-number">2</span><div><strong>Customize reminder</strong><span>Notify uses this toast alone; other modes show it before enforcement</span></div></div>
      {#if notificationStatus}<span class="save-status"><i class="ti ti-check" aria-hidden="true"></i>{notificationStatus}</span>{/if}
    </div>
    <div class="reminder-layout">
      <div class="preview-column">
        <span class="preview-label"><i class="ti ti-layout-sidebar-left" aria-hidden="true"></i>Desktop toast preview</span>
        <div class="reminder-preview" class:with-image={!!blockImageVersion}>
          {#if blockImageVersion}
            <img src={`${API}/api/block/image?v=${encodeURIComponent(blockImageVersion)}`} alt="Custom reminder" />
          {:else}
            <span class="preview-icon"><i class="ti ti-focus-2" aria-hidden="true"></i></span>
          {/if}
          <div><strong>{(blockTitle || 'Focus Mode').replace(/\{target\}/gi, 'example.exe').replace(/\{mode\}/gi, blockAction)}</strong><p>{(blockMessage || DEFAULT_BLOCK_MESSAGE).replace(/\{target\}/gi, 'example.exe').replace(/\{mode\}/gi, blockAction)}</p></div>
        </div>
      </div>
      <div class="reminder-fields">
        <label class="reminder-field" for="block-title">
          <span>Title</span>
          <input id="block-title" maxlength="60" bind:value={blockTitle} oninput={() => { notificationDirty = true; notificationStatus = null; }} placeholder="Focus Mode" />
        </label>
        <label class="reminder-field" for="block-message">
          <span>Message</span>
          <textarea id="block-message" maxlength="240" rows="3" bind:value={blockMessage} oninput={() => { notificationDirty = true; notificationStatus = null; }} placeholder={DEFAULT_BLOCK_MESSAGE}></textarea>
        </label>
        <span class="placeholder-hint">Use <code>{TARGET_PLACEHOLDER}</code> for the app or site and <code>{MODE_PLACEHOLDER}</code> for the active mode.</span>
      </div>
    </div>
    <div class="reminder-actions">
      <label class="image-button" class:disabled={imageUploading}>
        <i class="ti ti-photo-plus" aria-hidden="true"></i>{blockImageVersion ? 'Replace image' : 'Add image'}
        <input type="file" accept="image/png,image/jpeg" onchange={uploadNotificationImage} disabled={imageUploading} />
      </label>
      {#if blockImageVersion}<button class="remove-image" onclick={removeNotificationImage} disabled={imageUploading}><i class="ti ti-trash" aria-hidden="true"></i>Remove image</button>{/if}
      <span class="image-note">PNG or JPEG, up to 2 MB</span>
      <button class="save-reminder" onclick={saveNotification} disabled={!notificationDirty || notificationSaving}><i class="ti ti-device-floppy" aria-hidden="true"></i>{notificationSaving ? 'Saving…' : 'Save'}</button>
      <button class="preview-button" onclick={previewNotification} disabled={notificationSaving || imageUploading}><i class="ti ti-bell" aria-hidden="true"></i>Save & preview</button>
    </div>
  </div>

  <!-- Blocklist -->
  <div class="card blocklist-card">
    <div class="card-header flex-between">
      <div class="section-heading compact"><span class="step-number">3</span><div><strong>Apps & sites</strong><span>{items.length} target{items.length === 1 ? '' : 's'} configured</span></div></div>
      <div class="target-header-actions">
        <span class="extension-status" class:connected={extensionConnected} title={extensionConnected ? `Browser extension connected${extensionBrowser ? ` in ${extensionBrowser}` : ''}` : 'Website blocking needs the TimeLens browser extension'}>
          <span></span>{extensionConnected ? `Web ready${extensionBrowser ? ` · ${extensionBrowser}` : ''}` : 'Web extension offline'}
        </span>
        <button class="scanner-btn" onclick={loadRunning} title="Refresh running apps"><i class="ti ti-refresh"></i> Running apps</button>
      </div>
    </div>

    {#if !focusMode}
      <div class="block-banner">
        <i class="ti ti-alert-circle"></i>
        Ready when you are — enable Focus Mode above to enforce this list.
      </div>
    {/if}

    {#if hasWebsiteTargets && !extensionConnected}
      <div class="extension-banner">
        <i class="ti ti-plug-off" aria-hidden="true"></i>
        <div><strong>Website targets need the browser extension</strong><span>App blocking still works. Connect the TimeLens extension to enforce website entries.</span></div>
      </div>
    {/if}

    <div class="add-row">
      <div class="add-field target-field">
        <div class="target-kind-row">
          <span class="field-label">Add</span>
          <div class="target-kind" role="group" aria-label="Target type">
            <button type="button" class:active={targetKind === 'app'} onclick={() => { targetKind = 'app'; newItem = ''; showAddDropdown = false; }}><i class="ti ti-app-window" aria-hidden="true"></i>App</button>
            <button type="button" class:active={targetKind === 'website'} onclick={() => { targetKind = 'website'; newItem = ''; showAddDropdown = false; }}><i class="ti ti-world" aria-hidden="true"></i>Website</button>
          </div>
        </div>
        <div class="combo-wrapper">
          <i class="ti {targetKind === 'app' ? 'ti-search' : 'ti-link'} input-icon" aria-hidden="true"></i>
          <input id="block-target" class="add-input" aria-label={targetKind === 'app' ? 'App name' : 'Website address'} placeholder={targetKind === 'app' ? 'Search running apps or type discord.exe' : 'Paste youtube.com or a full URL'}
            bind:value={newItem} onfocus={() => { if (targetKind === 'app') { loadRunning(); showAddDropdown = true; } }} oninput={() => showAddDropdown = targetKind === 'app'}
            onkeydown={onKeydown} onblur={() => showAddDropdown = false} autocomplete="off" />
          {#if showAddDropdown && filteredProcs.length > 0}
            <div class="suggestions">
              {#each filteredProcs as proc}
                <button class="suggestion-item" onmousedown={(e) => { e.preventDefault(); selectProc(proc); }} type="button">
                  <span class="live-dot" class:blocked={isBlocked(proc)}></span>
                  <code>{proc}</code>
                  {#if isBlocked(proc)}<span class="bl-tag-sm">blocked</span>{/if}
                </button>
              {/each}
            </div>
          {/if}
        </div>
      </div>
      <div class="add-field duration-field">
        <span class="field-label">Duration</span>
        <div class="duration-picker">
          {#each DURATIONS as d}
            <button class="dur-btn" class:active={addingDuration === d.value} onclick={() => addingDuration = d.value} type="button" aria-pressed={addingDuration === d.value}>
              {d.label}
            </button>
          {/each}
        </div>
      </div>
      <button class="add-btn" onclick={add} disabled={!newItem.trim() || saving}>
        <i class="ti ti-plus"></i> Add {targetKind === 'app' ? 'app' : 'website'}
      </button>
    </div>

    {#if items.length === 0}
      <div class="empty">
        <span class="empty-icon"><i class="ti ti-shield-plus"></i></span>
        <span>No targets yet</span>
        <span class="empty-hint">Add a running app or a website domain to begin.</span>
      </div>
    {:else}
      <div class="bl-list">
        {#each items as entry, i}
          <div class="bl-row">
            <div class="bl-icon"><i class="ti {typeIcon(entry.i)}"></i></div>
            <code class="bl-name">{entry.i}</code>
            <span class="bl-tag">{typeLabel(entry.i)}</span>
            <span class="bl-mode">{modeLabel(entry)}</span>
            <button class="bl-enforce" onclick={() => enforceNow(entry.i)} title={blockAction === 'notify' ? 'Send reminder now' : 'Enforce now'} disabled={!focusMode || !entry.i.endsWith('.exe')}>
              <i class="ti ti-player-play"></i>
            </button>
            <button class="bl-remove" onclick={() => remove(i)} aria-label="Remove {entry.i}" title="Remove target">
              <i class="ti ti-trash"></i>
            </button>
          </div>
        {/each}
      </div>
    {/if}
    {#if recentlyRemoved}
      <div class="undo-bar" role="status">
        <span><i class="ti ti-trash" aria-hidden="true"></i>Removed <code>{recentlyRemoved.entry.i}</code></span>
        <button type="button" onclick={undoRemove}>Undo</button>
      </div>
    {/if}
  </div>

  <!-- Block Stats -->
  {#if blockStats.length > 0}
    <div class="card stats-card">
      <div class="card-header flex-between">
        <div><h2 class="title-small">Today's interventions</h2><span class="header-subtitle">Times TimeLens responded to a blocked target</span></div>
        <span class="total-badge">{blockStats.reduce((a, b) => a + b.count, 0)} total</span>
      </div>
      <div class="stats-list">
        {#each blockStats as stat}
          <div class="stat-row">
            <span class="stat-app-icon"><i class="ti {typeIcon(stat.exe)}" aria-hidden="true"></i></span>
            <code class="stat-exe">{stat.exe}</code>
            <span class="stat-action">{stat.action}</span>
            <span class="stat-count">{stat.count} time{stat.count === 1 ? '' : 's'}</span>
          </div>
        {/each}
      </div>
    </div>
  {/if}

  {#if showUnlock}
    <div class="unlock-backdrop" role="presentation" onclick={(event) => { if (event.target === event.currentTarget) closeUnlock(); }}>
      <div class="unlock-dialog" role="dialog" aria-modal="true" aria-labelledby="unlock-title">
        <button class="dialog-close" type="button" onclick={closeUnlock} aria-label="Close"><i class="ti ti-x"></i></button>
        <span class="unlock-icon"><i class="ti ti-lock-password" aria-hidden="true"></i></span>
        <div class="unlock-copy">
          <span class="eyebrow">Protected change</span>
          <h2 id="unlock-title">Enter your block password</h2>
          <p>This action would disable or weaken an active restriction. Unlocking lasts five minutes.</p>
        </div>
        <label class="password-field">
          <span>Password</span>
          <input type="password" bind:value={unlockPassword} onkeydown={(event) => { if (event.key === 'Enter') unlockProtection(); }} autocomplete="current-password" />
        </label>
        {#if unlockError}<div class="unlock-error" role="alert"><i class="ti ti-alert-circle"></i>{unlockError}</div>{/if}
        <div class="dialog-actions">
          <button class="dialog-secondary" type="button" onclick={closeUnlock}>Cancel</button>
          <button class="dialog-primary" type="button" onclick={unlockProtection} disabled={!unlockPassword || unlocking}>
            <i class="ti ti-lock-open" aria-hidden="true"></i>{unlocking ? 'Checking…' : 'Unlock change'}
          </button>
        </div>
      </div>
    </div>
  {/if}
</div>

<style>
  .block { display: flex; flex-direction: column; gap: 24px; }
  .warning {
    font-size: 12px; color: var(--md-error); font-weight: 500;
    padding: var(--sp-1) var(--sp-2);
    background: color-mix(in srgb, var(--md-error) 10%, transparent);
    border-radius: var(--shape-sm);
    width: fit-content;
  }
  .error-dismiss { border: 1px solid color-mix(in srgb, var(--md-error) 30%, transparent); cursor: pointer; font-family: inherit; }
  .error-dismiss span { margin-left: 8px; }
  .block-toolbar {
    display: flex;
    align-items: center;
    justify-content: flex-start;
  }
  .refresh-btn i { font-size: 14px; }
  .card {
    background: var(--clr-bg-sec); border: 1px solid var(--clr-border);
    border-radius: var(--shape-lg); overflow: hidden; padding: 0;
  }
  .card-header {
    padding: var(--sp-3) var(--sp-4); border-bottom: 1px solid var(--clr-border);
    font-size: 13px; font-weight: 500; color: var(--clr-text-pri); margin-bottom: 0;
  }
  .flex-between { display: flex; align-items: center; justify-content: space-between; }
  .scanner-btn {
    display: flex; align-items: center; gap: 4px;
    background: var(--clr-bg-ter); border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm); padding: var(--sp-1) var(--sp-2);
    color: var(--clr-text-sec); font-family: inherit; font-size: 11px;
    cursor: pointer; transition: all 0.15s;
  }
  .scanner-btn:hover { color: var(--md-primary); border-color: var(--md-primary); }

  .focus-row {
    display: flex; align-items: center; justify-content: space-between;
    padding: var(--sp-3) var(--sp-4);
  }
  .focus-info { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
  .focus-label { font-size: 13px; font-weight: 500; color: var(--clr-text-pri); }
  .focus-desc { font-size: 12px; color: var(--clr-text-sec); }
  .toggle {
    appearance: none; width: 40px; height: 22px;
    background: var(--clr-border); border-radius: 99px;
    position: relative; cursor: pointer; flex-shrink: 0; margin: 0;
    transition: background 0.2s ease;
  }
  .toggle::after {
    content: ''; position: absolute; width: 18px; height: 18px;
    background: #fff; border-radius: 50%; top: 2px; left: 2px;
    transition: transform 0.2s ease, box-shadow 0.2s ease;
    box-shadow: 0 1px 3px rgba(0,0,0,0.3);
  }
  .toggle:checked { background: var(--md-primary); }
  .toggle:checked::after { transform: translateX(18px); }

  .mode-grid {
    display: grid; grid-template-columns: repeat(4, 1fr);
    gap: var(--sp-2); padding: var(--sp-3);
  }
  .mode-card {
    flex-direction: column;
    padding: var(--sp-3);
    border-radius: var(--shape-md);
  }
  .mode-icon i { font-size: 22px; }
  .mode-label { font-size: 13px; font-weight: 600; }
  .mode-desc { font-size: 10px; text-align: center; line-height: 1.3; opacity: 0.7; }

  .add-row { display: flex; gap: var(--sp-2); padding: var(--sp-3) var(--sp-4); flex-wrap: wrap; }
  .duration-picker { display: flex; gap: 4px; flex-wrap: wrap; flex: 1; }
  .dur-btn { font-size: 11px; padding: 2px 8px; }
  .dur-btn.active { border-color: var(--md-primary); background: var(--md-primary-cont); color: var(--md-on-pri-cont); }
  .combo-wrapper { flex: 1; position: relative; }
  .add-input {
    width: 100%; background: var(--clr-bg-ter); border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm); padding: var(--sp-2);
    color: var(--clr-text-pri); font-family: var(--font-mono); font-size: 13px;
    outline: none; height: 38px; box-sizing: border-box;
  }
  .add-input:focus { border-color: var(--md-primary); }
  .suggestions {
    position: absolute; top: 100%; left: 0; right: 0; margin-top: 4px; z-index: 100;
    background: var(--clr-bg-ter); border: 1px solid var(--clr-border);
    border-radius: var(--shape-sm); max-height: 200px; overflow-y: auto;
    box-shadow: 0 8px 24px rgba(0,0,0,0.4);
  }
  .suggestion-item {
    display: flex; align-items: center; gap: var(--sp-2); width: 100%;
    padding: var(--sp-2); border: none; background: none;
    color: var(--clr-text-pri); font-family: var(--font-mono); font-size: 12px;
    cursor: pointer; text-align: left;
  }
  .suggestion-item:hover { background: var(--clr-bg-sec); }
  .live-dot { width: 6px; height: 6px; border-radius: 50%; background: var(--md-tertiary); flex-shrink: 0; }
  .live-dot.blocked { background: var(--md-error); }

  .add-btn {
    display: flex; align-items: center; gap: var(--sp-1);
    padding: var(--sp-2) var(--sp-4); border-radius: var(--shape-sm);
    border: none; background: var(--md-primary); color: #1a1a1a;
    font-family: inherit; font-size: 13px; font-weight: 600; cursor: pointer;
    white-space: nowrap; height: 38px;
  }
  .add-btn:disabled { opacity: 0.4; cursor: default; }

  .empty {
    display: flex; flex-direction: column; align-items: center; gap: var(--sp-2);
    padding: var(--sp-8) var(--sp-4); color: var(--clr-text-ter);
  }
  .empty i { font-size: 36px; }
  .empty span { font-size: 14px; }
  .empty-hint { font-size: 12px !important; opacity: 0.6; }

  .bl-list { display: flex; flex-direction: column; }
  .bl-row {
    display: flex; align-items: center; gap: var(--sp-3);
    padding: var(--sp-3) var(--sp-4); border-top: 1px solid var(--clr-border);
  }
  .bl-icon {
    width: 36px; height: 36px; border-radius: var(--shape-sm);
    background: var(--clr-bg-ter); display: flex; align-items: center; justify-content: center;
    flex-shrink: 0;
  }
  .bl-icon i { font-size: 16px; color: var(--md-primary); }
  .bl-name {
    font-family: var(--font-mono); font-size: 13px; color: var(--clr-text-pri); flex: 1;
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .bl-tag {
    font-size: 11px; font-weight: 500; color: var(--clr-text-ter);
    background: var(--clr-bg-ter); padding: 2px 8px; border-radius: var(--shape-sm);
    text-transform: uppercase; letter-spacing: 0.05em;
  }
  .bl-tag-sm { font-size: 10px; margin-left: auto; color: var(--md-error); font-style: italic; }
  .bl-mode { font-size: 11px; color: var(--clr-text-ter); font-family: var(--font-mono); flex-shrink: 0; min-width: 48px; text-align: right; }
  .bl-enforce {
    background: none; border: 1px solid var(--clr-border); color: var(--clr-text-ter);
    cursor: pointer; padding: var(--sp-1); border-radius: var(--shape-sm);
    font-size: 14px; transition: all 0.15s;
  }
  .bl-enforce:hover:not(:disabled) { color: var(--md-primary); border-color: var(--md-primary); }
  .bl-enforce:disabled { opacity: 0.3; cursor: default; }
  .bl-remove {
    background: none; border: none; color: var(--clr-text-ter);
    cursor: pointer; padding: var(--sp-1); border-radius: var(--shape-sm);
    font-size: 15px; transition: all 0.15s;
  }
  .bl-remove:hover { color: var(--md-error); background: var(--md-err-cont); }

  .card-disabled { opacity: 0.45; pointer-events: none; }
  .block-banner {
    display: flex; align-items: center; gap: var(--sp-2);
    margin: 0 var(--sp-4) var(--sp-3); padding: var(--sp-2) var(--sp-3);
    background: color-mix(in srgb, var(--md-primary) 12%, transparent);
    border: 1px solid color-mix(in srgb, var(--md-primary) 25%, transparent);
    border-radius: var(--shape-sm); color: var(--md-primary);
    font-size: 12px; font-weight: 500;
  }
  .block-banner i { font-size: 15px; flex-shrink: 0; }

  .stats-list { display: flex; flex-direction: column; }
  .stat-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-2) var(--sp-4); border-top: 1px solid var(--clr-border);
    font-size: 12px;
  }
  .stat-exe { font-family: var(--font-mono); font-size: 12px; width: 120px; color: var(--clr-text-pri); }
  .stat-action {
    font-size: 11px; font-weight: 500; text-transform: uppercase; color: var(--clr-text-sec);
    width: 50px;
  }
  .stat-bar-track { flex: 1; height: 4px; background: var(--md-surface); border-radius: var(--shape-full); overflow: hidden; }
  .stat-bar-fill { height: 100%; background: var(--md-error); border-radius: var(--shape-full); }
  .stat-count { font-family: var(--font-mono); font-size: 11px; color: var(--clr-text-ter); width: 36px; text-align: right; }

  /* Workflow layout */
  .block { gap: 16px; }
  .focus-workflow { overflow: visible; }
  .workflow-header {
    display: flex; align-items: center; justify-content: space-between; gap: 18px;
    padding: 18px 20px;
    background: linear-gradient(105deg, color-mix(in srgb, var(--md-primary) 8%, var(--clr-bg-sec)), var(--clr-bg-sec) 58%);
    border-bottom: 1px solid var(--clr-border);
  }
  .workflow-title, .workflow-actions, .master-switch, .section-heading { display: flex; align-items: center; }
  .workflow-title { gap: 12px; }
  .workflow-title > div, .section-heading > div { display: flex; flex-direction: column; gap: 2px; }
  .workflow-title h2 { margin: 0; color: var(--clr-text-pri); font-size: var(--type-section-title); }
  .workflow-title p { margin: 0; color: var(--clr-text-sec); font-size: var(--type-section-subtitle); line-height: 1.4; }
  .eyebrow { color: var(--md-primary); font-size: var(--type-card-subtitle); font-weight: 700; letter-spacing: .08em; text-transform: uppercase; }
  .step-icon { width: 40px; height: 40px; display: grid; place-items: center; border-radius: 12px; color: var(--md-primary); background: var(--md-primary-cont); border: 1px solid color-mix(in srgb, var(--md-primary) 22%, transparent); font-size: 19px; }
  .workflow-actions { gap: 9px; }
  .protection-badge { height: 34px; display: inline-flex; align-items: center; gap: 6px; padding: 0 10px; color: var(--md-primary); background: var(--md-primary-cont); border: 1px solid color-mix(in srgb, var(--md-primary) 25%, transparent); border-radius: var(--shape-full); font-size: 10px; font-weight: 600; white-space: nowrap; }
  .protection-badge.unlocked { color: var(--md-tertiary); background: color-mix(in srgb, var(--md-tertiary) 10%, var(--clr-bg-ter)); border-color: color-mix(in srgb, var(--md-tertiary) 25%, transparent); }
  .refresh-btn { height: 34px; display: inline-flex; align-items: center; gap: 6px; padding: 0 11px; border-radius: var(--shape-sm); border: 1px solid var(--clr-border); background: var(--clr-bg-ter); color: var(--clr-text-sec); font: 11px inherit; cursor: pointer; }
  .refresh-btn:hover { color: var(--md-primary); border-color: var(--md-primary); }
  .master-switch { height: 34px; gap: 8px; padding: 0 7px 0 11px; border-radius: var(--shape-full); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); color: var(--clr-text-pri); font-size: 11px; font-weight: 600; cursor: pointer; }
  .mode-section { padding: 16px 18px 18px; }
  .section-heading { gap: 9px; }
  .section-heading strong { color: var(--clr-text-pri); font-size: var(--type-section-title); }
  .section-heading span:not(.step-number) { color: var(--clr-text-sec); font-size: var(--type-section-subtitle); }
  .section-heading.compact strong { font-size: var(--type-section-title); }
  .step-number { width: 24px; height: 24px; display: grid; place-items: center; flex: 0 0 auto; border-radius: 8px; color: var(--md-primary); background: var(--md-primary-cont); font: 700 11px var(--font-mono); }
  .mode-grid { grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 8px; padding: 12px 0 0; }
  .mode-card { min-height: 68px; display: grid; grid-template-columns: 34px minmax(0, 1fr) 18px; align-items: center; gap: 9px; padding: 10px; text-align: left; border: 1px solid var(--clr-border); border-radius: var(--shape-md); background: var(--clr-bg-ter); color: var(--clr-text-sec); font-family: inherit; cursor: pointer; transition: transform var(--duration-fast), border-color var(--duration-fast), background var(--duration-fast); }
  .mode-card:hover { transform: translateY(-1px); border-color: var(--clr-border-strong); }
  .mode-card.active { border-color: var(--md-primary); background: color-mix(in srgb, var(--md-primary) 7%, var(--clr-bg-ter)); color: var(--clr-text-pri); }
  .mode-icon { width: 34px; height: 34px; display: grid; place-items: center; border-radius: 9px; background: var(--clr-bg-sec); color: var(--clr-text-sec); }
  .mode-card.active .mode-icon { color: var(--md-primary); background: var(--md-primary-cont); }
  .mode-icon i { font-size: 17px; }
  .mode-copy { min-width: 0; display: flex; flex-direction: column; gap: 2px; }
  .mode-copy strong { color: inherit; font-size: 12px; }
  .mode-copy small { color: var(--clr-text-sec); font-size: 9px; line-height: 1.25; }
  .mode-check { color: var(--md-primary); font-size: 14px; }
  .mode-outcome { display: grid; grid-template-columns: minmax(180px, .65fr) repeat(2, minmax(240px, 1fr)); gap: 8px; margin-top: 10px; padding: 8px; border: 1px solid var(--clr-border); border-radius: var(--shape-md); background: color-mix(in srgb, var(--clr-bg-ter) 65%, transparent); }
  .outcome-title, .outcome-item { min-width: 0; display: flex; align-items: center; gap: 10px; padding: 9px 10px; border-radius: var(--shape-sm); background: var(--clr-bg-sec); }
  .outcome-title > div { display: flex; flex-direction: column; gap: 1px; }
  .outcome-title > div span { color: var(--clr-text-ter); font-size: 9px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
  .outcome-title strong { color: var(--clr-text-pri); font-size: 12px; }
  .outcome-item { align-items: flex-start; flex-direction: column; gap: 4px; }
  .outcome-kind { display: inline-flex; align-items: center; gap: 5px; color: var(--md-primary); font-size: 10px; font-weight: 700; }
  .outcome-item p { margin: 0; color: var(--clr-text-sec); font-size: 10px; line-height: 1.45; }
  .reminder-card { overflow: hidden; }
  .reminder-card .card-header { padding: 14px 18px; }
  .save-status { display: inline-flex; align-items: center; gap: 5px; color: var(--md-primary); font-size: 10px; font-weight: 600; }
  .reminder-layout { display: grid; grid-template-columns: minmax(300px, .8fr) minmax(460px, 1.2fr); gap: 18px; padding: 16px 18px; border-top: 1px solid var(--clr-border); }
  .preview-column { min-width: 0; display: flex; flex-direction: column; gap: 7px; }
  .preview-label { display: inline-flex; align-items: center; gap: 5px; color: var(--clr-text-sec); font-size: 10px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
  .reminder-fields { display: grid; grid-template-columns: minmax(180px, .45fr) minmax(280px, 1fr); gap: 10px; align-items: start; }
  .reminder-field { display: flex; flex-direction: column; gap: 7px; }
  .reminder-field > span { color: var(--clr-text-sec); font-size: 10px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
  .reminder-field input, .reminder-field textarea { width: 100%; box-sizing: border-box; padding: 9px 11px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-sm); font: 12px inherit; outline: none; resize: vertical; }
  .reminder-field input { height: 38px; }
  .reminder-field textarea { min-height: 70px; line-height: 1.45; }
  .reminder-field input:focus, .reminder-field textarea:focus { border-color: var(--md-primary); box-shadow: 0 0 0 3px color-mix(in srgb, var(--md-primary) 8%, transparent); }
  .placeholder-hint { grid-column: 1 / -1; color: var(--clr-text-ter); font-size: 10px; }
  .placeholder-hint code { color: var(--md-primary); font-family: var(--font-mono); }
  .reminder-preview { min-height: 90px; display: grid; grid-template-columns: 48px minmax(0, 1fr); align-items: center; gap: 12px; padding: 12px 14px; border: 1px solid color-mix(in srgb, var(--md-primary) 20%, var(--clr-border)); border-left: 4px solid var(--md-primary); border-radius: var(--shape-md); background: #1a1a1a; box-shadow: 0 10px 28px rgba(0,0,0,.24); }
  .reminder-preview img, .preview-icon { width: 48px; height: 48px; border-radius: 10px; }
  .reminder-preview img { object-fit: contain; background: rgba(255,255,255,.04); }
  .preview-icon { display: grid; place-items: center; color: var(--md-primary); background: color-mix(in srgb, var(--md-primary) 12%, #1a1a1a); font-size: 21px; }
  .reminder-preview div { min-width: 0; }
  .reminder-preview strong { display: block; overflow: hidden; color: #fff; font-size: 13px; text-overflow: ellipsis; white-space: nowrap; }
  .reminder-preview p { display: -webkit-box; margin: 5px 0 0; overflow: hidden; color: #aaa; font-size: 11px; line-height: 1.4; -webkit-box-orient: vertical; -webkit-line-clamp: 2; }
  .reminder-actions { min-height: 52px; display: flex; align-items: center; gap: 8px; padding: 8px 18px; border-top: 1px solid var(--clr-border); background: color-mix(in srgb, var(--clr-bg-ter) 45%, transparent); }
  .image-button, .remove-image, .save-reminder, .preview-button { height: 34px; display: inline-flex; align-items: center; justify-content: center; gap: 6px; padding: 0 11px; border-radius: var(--shape-sm); font: 11px inherit; cursor: pointer; }
  .image-button, .remove-image, .save-reminder { color: var(--clr-text-sec); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); }
  .image-button:hover, .remove-image:hover, .save-reminder:hover:not(:disabled) { color: var(--md-primary); border-color: var(--md-primary); }
  .image-button input { display: none; }
  .image-button.disabled, .remove-image:disabled, .save-reminder:disabled, .preview-button:disabled { opacity: .4; cursor: default; }
  .remove-image:hover:not(:disabled) { color: var(--md-error); border-color: var(--md-error); }
  .image-note { flex: 1; color: var(--clr-text-ter); font-size: 10px; }
  .preview-button { color: var(--md-on-primary); background: var(--md-primary); border: 1px solid var(--md-primary); font-weight: 600; }
  .blocklist-card .card-header { padding: 14px 18px; }
  .target-header-actions { display: flex; align-items: center; gap: 8px; }
  .extension-status { height: 28px; display: inline-flex; align-items: center; gap: 6px; padding: 0 9px; color: var(--clr-text-ter); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-full); font-size: 9px; font-weight: 600; white-space: nowrap; }
  .extension-status > span { width: 6px; height: 6px; border-radius: 50%; background: var(--md-error); box-shadow: 0 0 0 3px color-mix(in srgb, var(--md-error) 12%, transparent); }
  .extension-status.connected { color: var(--md-tertiary); }
  .extension-status.connected > span { background: var(--md-tertiary); box-shadow: 0 0 0 3px color-mix(in srgb, var(--md-tertiary) 12%, transparent); }
  .block-banner { margin: 12px 18px 0; }
  .extension-banner { display: flex; align-items: center; gap: 10px; margin: 12px 18px 0; padding: 10px 12px; color: var(--clr-text-sec); background: color-mix(in srgb, var(--md-error) 7%, var(--clr-bg-ter)); border: 1px solid color-mix(in srgb, var(--md-error) 20%, var(--clr-border)); border-radius: var(--shape-sm); }
  .extension-banner > i { color: var(--md-error); font-size: 17px; }
  .extension-banner > div { display: flex; flex-direction: column; gap: 2px; }
  .extension-banner strong { color: var(--clr-text-pri); font-size: 11px; }
  .extension-banner span { font-size: 10px; }
  .add-row { display: grid; grid-template-columns: minmax(250px, 1.2fr) minmax(420px, 2fr) auto; align-items: end; gap: 12px; padding: 16px 18px; }
  .add-field { min-width: 0; display: flex; flex-direction: column; gap: 7px; }
  .field-label { color: var(--clr-text-sec); font-size: 10px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
  .target-kind-row { display: flex; align-items: center; justify-content: space-between; min-height: 24px; }
  .target-kind { display: inline-flex; padding: 2px; border: 1px solid var(--clr-border); border-radius: var(--shape-sm); background: var(--clr-bg-ter); }
  .target-kind button { height: 22px; display: inline-flex; align-items: center; gap: 4px; padding: 0 8px; color: var(--clr-text-ter); background: transparent; border: 0; border-radius: 5px; font: 9px inherit; cursor: pointer; }
  .target-kind button.active { color: var(--md-on-pri-cont); background: var(--md-primary-cont); }
  .input-icon { position: absolute; z-index: 2; left: 11px; top: 50%; transform: translateY(-50%); color: var(--clr-text-ter); font-size: 14px; }
  .add-input { padding-left: 34px; height: 36px; background: var(--clr-bg-ter); }
  .duration-picker { display: grid; grid-template-columns: repeat(6, minmax(62px, 1fr)); gap: 5px; }
  .dur-btn { min-height: 36px; padding: 0 7px; border: 1px solid var(--clr-border); border-radius: var(--shape-sm); background: var(--clr-bg-ter); color: var(--clr-text-sec); font: 10px inherit; cursor: pointer; white-space: nowrap; }
  .dur-btn:hover { color: var(--clr-text-pri); border-color: var(--clr-border-strong); }
  .dur-btn.active { border-color: var(--md-primary); background: var(--md-primary-cont); color: var(--md-on-pri-cont); }
  .add-btn { height: 36px; padding: 0 14px; }
  .empty { min-height: 112px; gap: 5px; padding: 17px; border-top: 1px solid var(--clr-border); }
  .empty-icon { width: 34px; height: 34px; display: grid; place-items: center; margin-bottom: 2px; border-radius: 10px; background: var(--clr-bg-ter); color: var(--clr-text-ter); }
  .empty-icon i { font-size: 18px; }
  .empty > span:not(.empty-hint):not(.empty-icon) { color: var(--clr-text-sec); font-size: 12px; }
  .bl-row { min-height: 54px; padding: 8px 18px; }
  .bl-icon { width: 32px; height: 32px; }
  .undo-bar { min-height: 42px; display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 7px 18px; color: var(--clr-text-sec); background: color-mix(in srgb, var(--md-primary) 7%, var(--clr-bg-ter)); border-top: 1px solid var(--clr-border); font-size: 11px; }
  .undo-bar span { display: inline-flex; align-items: center; gap: 7px; min-width: 0; }
  .undo-bar code { max-width: 260px; overflow: hidden; color: var(--clr-text-pri); font-family: var(--font-mono); text-overflow: ellipsis; white-space: nowrap; }
  .undo-bar button { padding: 5px 10px; color: var(--md-primary); background: transparent; border: 1px solid color-mix(in srgb, var(--md-primary) 35%, transparent); border-radius: var(--shape-sm); font: 10px inherit; font-weight: 700; cursor: pointer; }
  .stats-card .card-header > div { display: flex; flex-direction: column; gap: 2px; }
  .header-subtitle { color: var(--clr-text-sec); font-size: var(--type-section-subtitle); line-height: 1.4; }
  .total-badge { padding: 4px 8px; color: var(--md-primary); background: var(--md-primary-cont); border-radius: var(--shape-full); font: 10px var(--font-mono); }
  .stats-list { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 7px; padding: 12px; }
  .stat-row { min-height: 44px; display: grid; grid-template-columns: 28px minmax(80px, 1fr) auto auto; gap: 8px; padding: 7px 9px; border: 1px solid var(--clr-border); border-radius: var(--shape-md); background: var(--clr-bg-ter); }
  .stat-app-icon { width: 28px; height: 28px; display: grid; place-items: center; border-radius: 8px; color: var(--md-error); background: color-mix(in srgb, var(--md-error) 9%, transparent); }
  .stat-exe { width: auto; overflow: hidden; text-overflow: ellipsis; }
  .stat-action { width: auto; padding: 3px 6px; border-radius: var(--shape-full); background: var(--clr-bg-sec); font-size: 9px; }
  .stat-count { width: auto; color: var(--clr-text-sec); }
  .unlock-backdrop { position: fixed; inset: 0; z-index: 1000; display: grid; place-items: center; padding: 20px; background: rgba(4, 6, 3, .74); backdrop-filter: blur(8px); }
  .unlock-dialog { width: min(440px, 100%); position: relative; display: flex; flex-direction: column; gap: 16px; padding: 24px; color: var(--clr-text-pri); background: var(--clr-bg-sec); border: 1px solid var(--clr-border-strong); border-radius: 18px; box-shadow: 0 24px 80px rgba(0,0,0,.55); }
  .dialog-close { position: absolute; top: 14px; right: 14px; width: 30px; height: 30px; display: grid; place-items: center; color: var(--clr-text-sec); background: transparent; border: 0; border-radius: 8px; cursor: pointer; }
  .dialog-close:hover { color: var(--clr-text-pri); background: var(--clr-bg-ter); }
  .unlock-icon { width: 44px; height: 44px; display: grid; place-items: center; color: var(--md-primary); background: var(--md-primary-cont); border: 1px solid color-mix(in srgb, var(--md-primary) 25%, transparent); border-radius: 13px; font-size: 20px; }
  .unlock-copy { display: flex; flex-direction: column; gap: 4px; }
  .unlock-copy h2 { margin: 0; font-size: 18px; }
  .unlock-copy p { margin: 0; color: var(--clr-text-sec); font-size: 12px; line-height: 1.5; }
  .password-field { display: flex; flex-direction: column; gap: 7px; color: var(--clr-text-sec); font-size: 10px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase; }
  .password-field input { height: 40px; padding: 0 12px; color: var(--clr-text-pri); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-sm); font: 14px var(--font-mono); outline: none; }
  .password-field input:focus { border-color: var(--md-primary); box-shadow: 0 0 0 3px color-mix(in srgb, var(--md-primary) 10%, transparent); }
  .unlock-error { display: flex; align-items: center; gap: 7px; padding: 9px 10px; color: var(--md-error); background: color-mix(in srgb, var(--md-error) 9%, transparent); border-radius: var(--shape-sm); font-size: 11px; }
  .dialog-actions { display: flex; justify-content: flex-end; gap: 8px; }
  .dialog-secondary, .dialog-primary { height: 36px; display: inline-flex; align-items: center; justify-content: center; gap: 6px; padding: 0 13px; border-radius: var(--shape-sm); font: 12px inherit; cursor: pointer; }
  .dialog-secondary { color: var(--clr-text-sec); background: transparent; border: 1px solid var(--clr-border); }
  .dialog-primary { color: var(--md-on-primary); background: var(--md-primary); border: 1px solid var(--md-primary); font-weight: 600; }
  .dialog-primary:disabled { opacity: .4; cursor: not-allowed; }
  @media (max-width: 1180px) {
    .mode-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .mode-outcome { grid-template-columns: 1fr 1fr; }
    .outcome-title { grid-column: 1 / -1; }
    .reminder-layout { grid-template-columns: 1fr; }
    .add-row { grid-template-columns: 1fr auto; }
    .duration-field { grid-column: 1 / -1; grid-row: 2; }
  }
  @media (max-width: 720px) {
    .workflow-header { align-items: flex-start; flex-direction: column; }
    .workflow-actions { width: 100%; justify-content: space-between; }
    .mode-grid, .mode-outcome, .add-row { grid-template-columns: 1fr; }
    .outcome-title { grid-column: auto; }
    .reminder-fields { grid-template-columns: 1fr; }
    .placeholder-hint { grid-column: auto; }
    .reminder-actions { align-items: stretch; flex-wrap: wrap; }
    .image-note { flex-basis: 100%; order: 3; }
    .duration-field { grid-column: auto; grid-row: auto; }
    .duration-picker { grid-template-columns: repeat(3, 1fr); }
    .add-btn { width: 100%; justify-content: center; }
    .mode-copy small { white-space: normal; }
    .target-header-actions { align-items: flex-end; flex-direction: column; }
    .extension-status { display: none; }
    .bl-tag { display: none; }
  }
</style>
