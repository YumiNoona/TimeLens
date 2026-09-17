<script lang="ts">
  import { onMount } from 'svelte';

  type Notice = { pending: boolean; version: string; fromVersion: string; kind: 'install' | 'update' };
  let dialog: HTMLDialogElement | undefined = $state();
  let notice: Notice | null = $state(null);
  let notes = $state<string[]>([]);
  let dismissing = $state(false);

  function cleanNotes(value: string): string[] {
    return value.split(/\r?\n/)
      .map(line => line.replace(/^#{1,6}\s*/, '').replace(/^[-*]\s+/, '').trim())
      .filter(line => line && !/^TimeLens \d/i.test(line) && !/^Highlights$/i.test(line))
      .slice(0, 6);
  }

  async function load() {
    try {
      const response = await fetch('/api/release-notice', { cache: 'no-store' });
      if (!response.ok) return;
      const result: Notice = await response.json();
      if (!result.pending) return;
      notice = result;
      requestAnimationFrame(() => dialog?.showModal());
      try {
        const status = await fetch('/api/update/status', { cache: 'no-store' });
        if (status.ok) notes = cleanNotes((await status.json()).releaseNotes ?? '');
      } catch { }
    } catch { }
  }

  async function dismiss() {
    if (dismissing) return;
    dismissing = true;
    try {
      const response = await fetch('/api/release-notice/dismiss', { method: 'POST' });
      if (!response.ok) throw new Error();
      dialog?.close();
      notice = null;
    } finally { dismissing = false; }
  }

  onMount(() => { void load(); });
</script>

<dialog bind:this={dialog} class="release-dialog" aria-labelledby="release-title" oncancel={(event) => event.preventDefault()}>
  {#if notice}
    <div class="release-head">
      <div class="release-mark"><i class="ti {notice.kind === 'install' ? 'ti-sparkles' : 'ti-circle-check'}" aria-hidden="true"></i></div>
      <div class="release-heading">
        <span>{notice.kind === 'install' ? 'WELCOME TO TIMELENS' : 'UPDATE COMPLETE'}</span>
        <h2 id="release-title">TimeLens {notice.version} is ready</h2>
        <p>{notice.kind === 'install'
          ? 'Your private activity dashboard is installed and ready to use.'
          : notice.fromVersion ? `Updated successfully from ${notice.fromVersion}.` : 'The update installed successfully.'}</p>
      </div>
      <button class="close" type="button" aria-label="Dismiss release details" onclick={dismiss} disabled={dismissing}><i class="ti ti-x"></i></button>
    </div>
    <div class="release-body">
      <h3>What’s new</h3>
      <ul>
        {#if notes.length}
          {#each notes as note}<li>{note}</li>{/each}
        {:else}
          <li>Reliable multi-hour YouTube and background-media duration tracking.</li>
          <li>Accurate, quieter browser-coverage diagnostics.</li>
          <li>Refresh-safe navigation and a more compact contribution calendar.</li>
        {/if}
      </ul>
    </div>
    <div class="release-actions">
      <small>This notice stays available after a restart until you continue.</small>
      <button type="button" onclick={dismiss} disabled={dismissing}>{dismissing ? 'Finishing…' : 'Continue'}</button>
    </div>
  {/if}
</dialog>

<style>
  .release-dialog { position:fixed; inset:50% auto auto 50%; transform:translate(-50%,-50%); width:min(560px,calc(100vw - 32px)); max-height:calc(100dvh - 32px); margin:0; padding:0; overflow:hidden; color:var(--clr-text-pri); background:var(--clr-bg-sec); border:1px solid var(--clr-border-strong); border-radius:18px; box-shadow:0 28px 90px rgba(0,0,0,.55); }
  .release-dialog[open] { display:flex; flex-direction:column; }
  .release-dialog::backdrop { background:rgba(3,8,10,.72); backdrop-filter:blur(5px); }
  .release-head { display:grid; grid-template-columns:44px minmax(0,1fr) 32px; gap:14px; align-items:start; padding:22px 22px 18px; border-bottom:1px solid var(--clr-border); }
  .release-mark { width:44px; height:44px; display:grid; place-items:center; color:var(--md-primary); background:var(--md-primary-cont); border:1px solid color-mix(in srgb,var(--md-primary) 28%,transparent); border-radius:13px; font-size:22px; }
  .release-heading span { color:var(--md-primary); font-size:10px; font-weight:700; letter-spacing:.11em; }
  .release-heading h2 { margin:4px 0 5px; font-size:20px; line-height:1.2; }
  .release-heading p { margin:0; color:var(--clr-text-sec); font-size:12px; line-height:1.5; }
  .close { width:32px; height:32px; display:grid; place-items:center; border:0; border-radius:8px; color:var(--clr-text-sec); background:transparent; cursor:pointer; }
  .close:hover { color:var(--clr-text-pri); background:var(--clr-bg-ter); }
  .release-body { min-height:0; overflow:auto; padding:18px 24px; }
  .release-body h3 { margin:0 0 10px; color:var(--clr-text-sec); font-size:11px; letter-spacing:.08em; text-transform:uppercase; }
  .release-body ul { display:grid; gap:9px; margin:0; padding:0; list-style:none; }
  .release-body li { position:relative; padding-left:18px; color:var(--clr-text-sec); font-size:12px; line-height:1.55; }
  .release-body li::before { content:'✓'; position:absolute; left:0; color:var(--md-primary); font-weight:700; }
  .release-actions { display:flex; justify-content:space-between; align-items:center; gap:16px; padding:14px 20px; border-top:1px solid var(--clr-border); background:color-mix(in srgb,var(--clr-bg-ter) 45%,transparent); }
  .release-actions small { color:var(--clr-text-sec); font-size:10px; line-height:1.4; }
  .release-actions button { min-width:96px; height:36px; border:0; border-radius:9px; color:var(--md-on-primary); background:var(--md-primary); font-weight:650; cursor:pointer; }
  @media(max-width:520px){ .release-head{grid-template-columns:38px minmax(0,1fr) 30px;padding:18px 16px 15px}.release-mark{width:38px;height:38px}.release-body{padding:16px 18px}.release-actions{align-items:flex-end}.release-actions small{max-width:190px} }
</style>
