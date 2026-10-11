<script lang="ts">
  import { onMount } from 'svelte';
  import { fetchJson } from '../api';
  let ignoredApps = $state(''), hiddenTitleApps = $state(''), ignoredDomains = $state(''), titleRedactionPatterns = $state('');
  let pauseUntil = $state(''), busy = $state(false), message = $state('');
  let now = $state(Date.now());
  $effect(() => {
    const remaining = Date.parse(pauseUntil) - Date.now();
    now = Date.now();
    if (remaining > 0) {
      const timer = setTimeout(() => { now = Date.now(); }, remaining + 100);
      return () => clearTimeout(timer);
    }
  });
  async function save(values: Record<string, string>) {
    busy = true; message = '';
    try {
      const response = await fetch('/api/settings', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(values) });
      if (!response.ok) throw new Error(JSON.stringify(await response.json()));
      if ('pauseUntil' in values) pauseUntil = values.pauseUntil;
      message = 'Saved. Rules apply to new observations; existing history is unchanged.';
    } catch (error) { message = String(error); }
    finally { busy = false; }
  }
  onMount(() => { void fetchJson<Record<string, string>>('/api/settings').then(s => {
    ignoredApps = s.ignoredApps || ''; hiddenTitleApps = s.hiddenTitleApps || '';
    ignoredDomains = s.ignoredDomains || ''; titleRedactionPatterns = s.titleRedactionPatterns || ''; pauseUntil = s.pauseUntil || '';
  }).catch(error => { message = String(error); }); });
</script>

<section class="card privacy-rules">
  <h2>Exclude sensitive activity</h2>
  <p>Applied before storage. One rule per line. Apps use exact executable names; domains include subdomains.</p>
  <div class="fields">
    <label>Never record these apps<textarea bind:value={ignoredApps} placeholder="passwordmanager.exe" maxlength="8192"></textarea></label>
    <label>Hide titles and project names for these apps<textarea bind:value={hiddenTitleApps} placeholder="firefox.exe" maxlength="8192"></textarea></label>
    <label>Never store these websites<textarea bind:value={ignoredDomains} placeholder="bank.example" maxlength="8192"></textarea></label>
    <label>Redact matching titles (regular expressions)<textarea bind:value={titleRedactionPatterns} placeholder="confidential|private" maxlength="8192"></textarea></label>
  </div>
  <p>When website exclusions are set, native browser titles are hidden too. Browser-app time can still be counted without website details.</p>
  <button type="button" disabled={busy} onclick={() => save({ ignoredApps, hiddenTitleApps, ignoredDomains, titleRedactionPatterns })}>Save privacy rules</button>
  <h3>Pause all recording</h3>
  <p>{pauseUntil && Date.parse(pauseUntil) > now ? `Paused until ${new Date(pauseUntil).toLocaleTimeString()}` : 'Recording is enabled according to your collection settings.'} Focus blocking is unaffected.</p>
  <div class="actions">
    <button type="button" disabled={busy} onclick={() => save({ pauseUntil: new Date(Date.now() + 15 * 60000).toISOString() })}>Pause 15 minutes</button>
    <button type="button" disabled={busy} onclick={() => save({ pauseUntil: new Date(Date.now() + 60 * 60000).toISOString() })}>Pause 1 hour</button>
    <button type="button" disabled={busy} onclick={() => save({ pauseUntil: '' })}>Resume</button>
  </div>
  {#if message}<p role="status">{message}</p>{/if}
</section>

<style>
  .privacy-rules { grid-column: 1 / -1; padding: 20px; }
  h2, h3 { color: var(--clr-text); margin: 0 0 12px; } h3 { margin-top: 24px; }
  p { color: var(--clr-text-sec); line-height: 1.6; font-size: 12px; margin: 12px 0; }
  .fields { display: grid; grid-template-columns: repeat(auto-fit,minmax(240px,1fr)); gap: 16px; }
  label { display: grid; gap: 8px; color: var(--clr-text); font-size: 12px; }
  textarea { min-height: 80px; resize: vertical; background: var(--clr-bg); color: var(--clr-text); border: 1px solid var(--clr-border); border-radius: 8px; padding: 10px; }
  button { background: var(--clr-bg); color: var(--clr-text); border: 1px solid var(--clr-border); border-radius: 8px; padding: 8px 12px; cursor: pointer; }
  .actions { display: flex; flex-wrap: wrap; gap: 8px; } button:disabled { opacity: .5; cursor: default; }
</style>
