<script lang="ts">
  import type { DashboardData } from '../types';
  import { fmtPrecise } from '../utils';
  let { data, date }: { data: DashboardData; date: string } = $props();
  let mode = $state<'sites' | 'apps'>('sites');
  let query = $state('');
  let sort = $state('time');
  let expanded = $state(new Set<string>());
  const count = (n: number | null | undefined) => n == null ? '—' : n.toLocaleString();
  const seen = (value: string) => new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
  const rows = $derived.by(() => {
    const all = mode === 'sites'
      ? data.browserSites.map(site => ({ id: site.domain, seconds: site.totalSeconds, keys: site.keystrokes, clicks: site.clicks, sessions: site.visits, lastSeen: site.lastVisit }))
      : data.topApps.map(app => ({ id: app.name, seconds: app.minutes * 60, keys: app.keystrokes, clicks: app.clicks,
          sessions: data.timeline.filter(segment => segment.exeName === app.name && segment.type !== 'idle' && segment.type !== 'away').length, lastSeen: '' }));
    return all.filter(row => row.id.toLowerCase().includes(query.toLowerCase())).sort((a, b) =>
      sort === 'name' ? a.id.localeCompare(b.id) : sort === 'clicks' ? (b.clicks ?? -1) - (a.clicks ?? -1) :
      sort === 'keys' ? (b.keys ?? -1) - (a.keys ?? -1) : b.seconds - a.seconds);
  });
  function toggle(id: string) {
    const next = new Set(expanded);
    if (next.has(id)) next.delete(id); else next.add(id);
    expanded = next;
  }
  function exportCsv() {
    const escape = (value: unknown) => '"' + String(value ?? '').replaceAll('"', '""') + '"';
    const csv = [['Date', mode === 'sites' ? 'Website' : 'Application', 'Seconds', 'Recorded keystrokes', 'Recorded clicks', 'Sessions'],
      ...rows.map(row => [date, row.id, Math.round(row.seconds), row.keys, row.clicks, row.sessions])].map(row => row.map(escape).join(',')).join('\r\n');
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
    const link = document.createElement('a'); link.href = url; link.download = `TimeLens-${date}-${mode}.csv`; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
</script>

<section class="card usage-details" aria-label="Detailed daily usage">
  <div class="usage-heading"><div><h2>Usage details</h2><p>{date} · Select a row to inspect pages or window titles.</p></div><button onclick={exportCsv} disabled={!rows.length}>Export CSV</button></div>
  <div class="usage-controls">
    <div class="tabs" aria-label="Usage type"><button class:selected={mode === 'sites'} onclick={() => { mode = 'sites'; expanded = new Set(); }}>Websites ({data.browserSites.length})</button><button class:selected={mode === 'apps'} onclick={() => { mode = 'apps'; expanded = new Set(); }}>Applications ({data.topApps.length})</button></div>
    <input aria-label="Search usage" placeholder={mode === 'sites' ? 'Search websites…' : 'Search applications…'} bind:value={query} />
    <select aria-label="Sort usage" bind:value={sort}><option value="time">Most time</option><option value="clicks">Most clicks</option><option value="keys">Most keystrokes</option><option value="name">Name</option></select>
  </div>
  <p class="hint">Website time is part of browser app time, not added on top. Input counts are recorded events, never typed text. “—” means website input was not collected; historical counts cannot be reconstructed. Sessions may include title changes in older history.</p>
  <div class="usage-scroll"><table><thead><tr><th>{mode === 'sites' ? 'Website' : 'Executable / app'}</th><th>Active time</th><th>Keystrokes</th><th>Clicks</th><th>Sessions</th><th>{mode === 'sites' ? 'Last seen' : 'Details'}</th></tr></thead><tbody>
  {#each rows as row}
    <tr><td><button class="row-name" aria-expanded={expanded.has(row.id)} onclick={() => toggle(row.id)}><span>{expanded.has(row.id) ? '−' : '+'}</span>{row.id}</button></td><td class="time">{fmtPrecise(row.seconds)}</td><td>{count(row.keys)}</td><td>{count(row.clicks)}</td><td>{row.sessions}</td><td>{row.lastSeen ? seen(row.lastSeen) : 'Window titles'}</td></tr>
    {#if expanded.has(row.id)}
      <tr class="detail-row"><td colspan="6">
        {#if mode === 'sites'}
          <div class="pages">
          {#each data.browserSites.find(site => site.domain === row.id)?.pages ?? [] as page}
            <div class="page"><div class="page-copy"><strong>{page.title || page.url}</strong><span title={page.url}>{page.url}</span><small>{page.browser} · last seen {seen(page.lastSeen)} · {page.sessions} sessions</small></div><div class="page-metrics"><strong>{fmtPrecise(page.totalSeconds)}</strong><span>{count(page.keystrokes)} keystrokes · {count(page.clicks)} clicks</span></div></div>
          {/each}
          </div>
        {:else}
          <div class="pages">{#each data.timeline.filter(segment => segment.exeName === row.id && segment.type !== 'idle' && segment.type !== 'away') as segment}
            <div class="page"><div class="page-copy"><strong>{segment.windowTitle || '(No window title)'}</strong><small>{String(Math.floor(segment.startHour)).padStart(2, '0')}:{String(Math.floor(segment.startHour * 60) % 60).padStart(2, '0')} · {segment.project || segment.type}</small></div><strong>{fmtPrecise(segment.durationSeconds)}</strong></div>
          {/each}</div>
        {/if}
      </td></tr>
    {/if}
  {:else}<tr><td colspan="6" class="empty">No matching {mode === 'sites' ? 'website' : 'application'} activity on this day.</td></tr>{/each}
  </tbody></table></div>
</section>
<style>
  .usage-details { margin-bottom: var(--space-5); }
  .usage-heading, .usage-controls { display:flex; align-items:center; justify-content:space-between; gap:12px; flex-wrap:wrap; }
  h2 { font-size:18px; margin:0 0 6px; } p { color:var(--clr-text-ter); font-size:12px; margin:0; }
  .usage-controls { margin:20px 0 12px; }
  button, input, select { border:1px solid var(--clr-border); background:var(--clr-bg-ter); color:var(--clr-text-pri); border-radius:8px; padding:9px 12px; font:inherit; font-size:12px; }
  button { cursor:pointer; } button:disabled { opacity:.5; cursor:default; } button:focus-visible, input:focus-visible, select:focus-visible { outline:2px solid var(--md-primary); outline-offset:2px; }
  .tabs { display:flex; gap:6px; } .tabs .selected { border-color:var(--md-primary); color:var(--md-primary); }
  input { flex:1; min-width:170px; } .hint { line-height:1.6; max-width:1000px; margin-bottom:14px; }
  .usage-scroll { overflow-x:auto; } table { width:100%; border-collapse:collapse; font-size:12px; text-align:left; }
  th { font-size:10px; text-transform:uppercase; letter-spacing:.05em; color:var(--clr-text-ter); padding:12px 10px; white-space:nowrap; }
  td { padding:12px 10px; border-top:1px solid var(--clr-border); font-variant-numeric:tabular-nums; white-space:nowrap; }
  .time { color:var(--md-primary); font-weight:600; } .row-name { border:0; background:none; padding:0; display:flex; gap:10px; align-items:center; font-family:var(--font-mono); text-align:left; }
  .row-name span { color:var(--md-primary); width:14px; } .detail-row td { background:var(--clr-bg-ter); padding:4px 18px; }
  .page { display:flex; justify-content:space-between; align-items:center; gap:24px; padding:13px 0; border-bottom:1px solid var(--clr-border); } .page:last-child { border:0; }
  .page-copy { display:grid; gap:4px; min-width:0; } .page-copy strong { white-space:normal; font-weight:500; } .page-copy span { max-width:660px; overflow:hidden; text-overflow:ellipsis; color:var(--clr-text-sec); font-size:11px; }
  small, .page-metrics span { color:var(--clr-text-ter); font-size:10px; } .page-metrics { display:grid; gap:5px; text-align:right; flex-shrink:0; } .empty { padding:28px; text-align:center; color:var(--clr-text-ter); }
</style>
