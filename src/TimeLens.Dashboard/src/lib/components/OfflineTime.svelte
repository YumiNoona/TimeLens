<script lang="ts">
  import { onMount } from 'svelte';
  import { fetchJson } from '../api';
  import { fmtPrecise } from '../utils';
  let { date = '', review = false }: { date?: string; review?: boolean } = $props();
  type Span = { id: number; startTime: string; endTime: string };
  type Entry = Span & { kind: string; project: string; notes: string };
  let pending = $state<Span[]>([]), entries = $state<Entry[]>([]), kind = $state('meeting'), project = $state(''), notes = $state('');
  let startTime = $state(''), endTime = $state(''), busy = $state(false), message = $state(''), manual = $state(false);
  let loadSequence = 0;
  const total = $derived(entries.reduce((sum, e) => {
    const local = date ? new Date(`${date}T00:00:00`) : new Date(new Date().setHours(0,0,0,0));
    const next = new Date(local); next.setDate(next.getDate()+1);
    return sum + Math.max(0, (Math.min(Date.parse(e.endTime),next.getTime()) - Math.max(Date.parse(e.startTime),local.getTime())) / 1000);
  }, 0));
  async function load() {
    const sequence = ++loadSequence;
    try {
      const rows = await fetchJson<Entry[]>(`/api/offline${date ? `?date=${encodeURIComponent(date)}` : ''}`);
      if (sequence !== loadSequence) return;
      entries = rows;
      if (review) {
        const spans = await fetchJson<Span[]>('/api/offline/pending');
        if (sequence === loadSequence) pending = spans;
      }
    } catch (error) { message = String(error); }
  }
  async function save(span?: Span) {
    busy = true; message = '';
    try {
      if (!span && (!startTime || !endTime)) throw new Error('Choose a start and end time.');
      const response = await fetch('/api/offline', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
        idleSpanId: span?.id, startTime: span ? undefined : new Date(startTime).toISOString(),
        endTime: span ? undefined : new Date(endTime).toISOString(), kind, project, notes
      }) });
      if (!response.ok) throw new Error(JSON.stringify(await response.json()));
      project = ''; notes = ''; manual = false; await load();
    } catch (error) { message = String(error); }
    finally { busy = false; }
  }
  async function remove(id: number) {
    if (!confirm('Remove this offline entry?')) return;
    busy = true;
    try { const response = await fetch(`/api/offline/${id}`, { method: 'DELETE' }); if (!response.ok) throw new Error('Could not remove entry'); await load(); }
    catch (error) { message = String(error); } finally { busy = false; }
  }
  function range(span: Span) { return `${new Date(span.startTime).toLocaleString()} – ${new Date(span.endTime).toLocaleTimeString()}`; }
  $effect(() => { date; void load(); });
  onMount(() => {
    if (!review) return;
    const timer = setInterval(() => { if (!document.hidden && !busy) void load(); }, 30000);
    return () => clearInterval(timer);
  });
</script>

<section class="card offline">
  <h2>Offline work <span>{fmtPrecise(total, false)}</span></h2>
  <p>Meetings, calls and work away from the keyboard. Reported separately from automatic active time and media playback.</p>
  {#if pending.length || manual}
    <div class="fields">
      <label>Activity<select bind:value={kind}><option value="meeting">Meeting</option><option value="call">Call</option><option value="brainstorming">Brainstorming</option><option value="work">Other offline work</option><option value="break">Break (not work)</option><option value="personal">Personal (not work)</option></select></label>
      <label>Project (optional)<input bind:value={project} maxlength="120" /></label>
      <label>Notes (optional)<input bind:value={notes} maxlength="500" /></label>
    </div>
  {/if}
  {#each pending as span (span.id)}
    <div class="row"><span>Review time away: {range(span)}</span><button type="button" disabled={busy} onclick={() => save(span)}>Confirm activity</button></div>
  {/each}
  {#each entries as entry (entry.id)}
    <div class="row"><span>{entry.kind} · {range(entry)}{entry.project ? ` · ${entry.project}` : ''}{entry.notes ? ` · ${entry.notes}` : ''}</span><button type="button" disabled={busy} onclick={() => remove(entry.id)}>Remove</button></div>
  {/each}
  {#if manual}
    <div class="fields"><label>Start<input type="datetime-local" bind:value={startTime} /></label><label>End<input type="datetime-local" bind:value={endTime} /></label></div>
    <button type="button" disabled={busy} onclick={() => save()}>Save offline time</button>
  {/if}
  <button type="button" disabled={busy} onclick={() => { manual = !manual; }}>{manual ? 'Cancel' : 'Add untracked time'}</button>
  {#if message}<p role="alert">{message}</p>{/if}
</section>

<style>
  .offline { padding: 20px; margin: 16px 0; } h2 { display: flex; justify-content: space-between; color: var(--clr-text); font-size: 16px; }
  p { color: var(--clr-text-sec); font-size: 12px; line-height: 1.6; } .fields { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0; }
  label { display: grid; gap: 6px; font-size: 12px; color: var(--clr-text); } input, select, button { background: var(--clr-bg); color: var(--clr-text); border: 1px solid var(--clr-border); padding: 8px; border-radius: 8px; }
  .row { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 0; color: var(--clr-text-sec); font-size: 12px; border-top: 1px solid var(--clr-border); }
  button { cursor: pointer; margin: 4px; } button:disabled { opacity: .5; cursor: default; }
</style>
