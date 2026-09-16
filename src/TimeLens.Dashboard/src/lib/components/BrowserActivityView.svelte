<script lang="ts">
  import { onMount } from 'svelte';
  import type { AudioEntry, BrowserEntry } from '../types';
  import { fmtPrecise } from '../utils';
  import { showSeconds } from '../stores/settings';
  import TopSites from './TopSites.svelte';
  import SiteTimeCard from './SiteTimeCard.svelte';
  import BrowserHourlyCard from './BrowserHourlyCard.svelte';
  import MediaCard from './MediaCard.svelte';

  type Visit = { domain:string; url:string; title:string; browser:string; startedAt:string; endedAt:string; activeSeconds:number };
  const localDate = (date = new Date()) => `${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}-${String(date.getDate()).padStart(2,'0')}`;
  let { sites, browserTime, browserHourly, audioSessions }: {
    sites: BrowserEntry[]; browserTime: {domain:string; totalMinutes:number; totalSeconds?:number}[];
    browserHourly: {hour:number; totalSeconds:number}[]; audioSessions: AudioEntry[];
  } = $props();
  let tab = $state<'overview'|'visits'|'pages'>('overview');
  let visits = $state<Visit[]>([]);
  let selectedDate = $state(localDate());
  let shownSites = $state<BrowserEntry[]>([]);
  let shownTime = $state<{domain:string; totalMinutes:number; totalSeconds?:number}[]>([]);
  let shownHourly = $state<{hour:number; totalSeconds:number}[]>([]);
  let loading = $state(false);
  let query = $state('');
  let domain = $state('all');
  let sort = $state<'recent'|'time'>('recent');
  const domains = $derived([...new Set(visits.map(v => v.domain))].sort());
  const filteredVisits = $derived(visits.filter(v => domain === 'all' || v.domain === domain)
    .filter(v => `${v.domain} ${v.title} ${v.url}`.toLowerCase().includes(query.toLowerCase()))
    .toSorted((a,b) => sort === 'time' ? b.activeSeconds-a.activeSeconds : b.startedAt.localeCompare(a.startedAt)));
  const pages = $derived(shownSites.flatMap(site => site.pages.map(page => ({...page, domain:site.domain})))
    .filter(p => domain === 'all' || p.domain === domain)
    .filter(p => `${p.domain} ${p.title} ${p.url}`.toLowerCase().includes(query.toLowerCase()))
    .toSorted((a,b) => sort === 'time' ? b.totalSeconds-a.totalSeconds : b.lastSeen.localeCompare(a.lastSeen)));
  const clock = (value:string) => new Date(value).toLocaleTimeString([], {hour:'2-digit', minute:'2-digit', second:$showSeconds ? '2-digit' : undefined});
  async function loadDate() {
    loading = true;
    const query = `?date=${encodeURIComponent(selectedDate)}`;
    try {
      const [visitResponse, siteResponse, timeResponse, hourlyResponse] = await Promise.all([
        fetch('/api/browser-visits'+query), fetch('/api/browser-summary'+query),
        fetch('/api/browser-time-summary'+query), fetch('/api/browser-hourly'+query)
      ]);
      visits = visitResponse.ok ? await visitResponse.json() : [];
      shownSites = siteResponse.ok ? await siteResponse.json() : [];
      shownTime = timeResponse.ok ? await timeResponse.json() : [];
      shownHourly = hourlyResponse.ok ? await hourlyResponse.json() : [];
    }
    finally { loading = false; }
  }
  onMount(() => { shownSites=sites; shownTime=browserTime; shownHourly=browserHourly; void loadDate(); });
</script>

<div class="browser-nav">
 <div class="browser-tabs" role="tablist" aria-label="Browser activity views">
  <button class:active={tab==='overview'} onclick={() => tab='overview'}><i class="ti ti-chart-donut"></i>Overview</button>
  <button class:active={tab==='visits'} onclick={() => tab='visits'}><i class="ti ti-clock-record"></i>Visits <span>{visits.length}</span></button>
  <button class:active={tab==='pages'} onclick={() => tab='pages'}><i class="ti ti-files"></i>Pages <span>{shownSites.reduce((n,s)=>n+s.pages.length,0)}</span></button>
 </div>
 <label class="date-picker"><i class="ti ti-calendar"></i><input type="date" bind:value={selectedDate} max={localDate()} onchange={loadDate} aria-label="Browser activity date" /></label>
</div>
<div class="browser-summary">
  <div><span>Unique sites</span><strong>{shownSites.length}</strong></div>
  <div><span>Recorded sessions</span><strong>{shownSites.reduce((sum,site)=>sum+site.visits,0).toLocaleString()}</strong></div>
  <div><span>Active browsing</span><strong>{fmtPrecise(shownTime.reduce((sum,item)=>sum+(item.totalSeconds ?? item.totalMinutes*60),0), $showSeconds)}</strong></div>
</div>

{#if tab === 'overview'}
  {#if shownSites.length || shownTime.length}
    <div class="two-col"><TopSites sites={shownSites} /><SiteTimeCard browserTime={shownTime} /></div>
    <div class="detail-grid"><BrowserHourlyCard browserHourly={shownHourly} /><MediaCard {audioSessions} /></div>
  {:else}<div class="empty overview-empty"><i class="ti ti-world-off"></i><strong>No browsing data on this date</strong><span>Choose another date, or connect the browser extension in Settings.</span></div>{/if}
{:else}
  <div class="browser-tools">
    <label><i class="ti ti-search"></i><input type="search" placeholder="Search sites, titles or URLs…" bind:value={query} /></label>
    <select bind:value={domain} aria-label="Filter by website"><option value="all">All websites</option>{#each domains as item}<option value={item}>{item}</option>{/each}</select>
    <select bind:value={sort} aria-label="Sort browser activity"><option value="recent">Most recent</option><option value="time">Longest time</option></select>
  </div>
  <div class="activity-card">
    {#if tab === 'visits'}
      <div class="activity-head"><span>Visit and page</span><span>When</span><span>Active time</span></div>
      {#if loading}<div class="empty">Loading visits…</div>
      {:else}
        {#each filteredVisits as visit}
          <div class="activity-row">
            <div class="page-copy"><strong>{visit.title || visit.domain}</strong><span>{visit.domain} · {visit.browser}</span><small title={visit.url}>{visit.url || 'Address hidden by privacy settings'}</small></div>
            <div class="when"><strong>{clock(visit.startedAt)}–{clock(visit.endedAt)}</strong><span>{new Date(visit.startedAt).toLocaleDateString([], {month:'short',day:'numeric'})}</span></div>
            <strong class="duration">{fmtPrecise(visit.activeSeconds, $showSeconds)}</strong>
          </div>
        {:else}<div class="empty">No visits match these filters.</div>{/each}
      {/if}
    {:else}
      <div class="activity-head pages-head"><span>Page</span><span>Interactions</span><span>Active time</span></div>
      {#each pages as page}
        <div class="activity-row">
          <div class="page-copy"><strong>{page.title || page.domain}</strong><span>{page.domain} · {page.browser} · {page.sessions} session{page.sessions===1?'':'s'}</span><small title={page.url}>{page.url || 'Address hidden by privacy settings'}</small></div>
          <div class="when"><strong>{page.keystrokes?.toLocaleString() ?? '—'} keys</strong><span>{page.clicks?.toLocaleString() ?? '—'} clicks · last {clock(page.lastSeen)}</span></div>
          <strong class="duration">{fmtPrecise(page.totalSeconds, $showSeconds)}</strong>
        </div>
      {:else}<div class="empty">No pages match these filters.</div>{/each}
    {/if}
  </div>
{/if}

<style>
  .browser-nav{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}.browser-tabs{display:flex;align-items:center;gap:6px;padding:5px;width:max-content;max-width:100%;overflow:auto;background:var(--clr-bg-sec);border:1px solid var(--clr-border);border-radius:12px}
  .browser-tabs button{height:34px;display:flex;align-items:center;gap:7px;padding:0 13px;border:0;border-radius:8px;background:transparent;color:var(--clr-text-sec);font:12px inherit;cursor:pointer;white-space:nowrap}.browser-tabs button.active{background:var(--md-primary-cont);color:var(--md-primary)}.browser-tabs span{font:10px var(--font-mono);opacity:.65}
  .browser-summary{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));overflow:hidden;background:var(--clr-bg-sec);border:1px solid var(--clr-border);border-radius:12px}.browser-summary>div{display:grid;gap:3px;padding:14px 18px}.browser-summary>div+div{border-left:1px solid var(--clr-border)}.browser-summary span{color:var(--clr-text-sec);font-size:10px}.browser-summary strong{font:18px var(--font-mono)}
  .two-col,.detail-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:var(--space-4)}.detail-grid{grid-template-columns:1fr;margin-top:var(--space-4)}
  .browser-tools{display:flex;gap:8px;align-items:center}.browser-tools label{height:38px;flex:1;display:flex;align-items:center;gap:8px;padding:0 12px;background:var(--clr-bg-sec);border:1px solid var(--clr-border);border-radius:9px;color:var(--clr-text-ter)}.browser-tools input{width:100%;border:0;outline:0;background:transparent;color:var(--clr-text-pri);font:12px inherit}.browser-tools select{height:38px;padding:0 30px 0 11px;background:var(--clr-bg-sec);border:1px solid var(--clr-border);border-radius:9px;color:var(--clr-text-pri);font:12px inherit}
  .date-picker{height:42px;display:flex;align-items:center;gap:8px;padding:0 11px;border:1px solid var(--clr-border);border-radius:10px;background:var(--clr-bg-sec);color:var(--clr-text-sec)}.date-picker input{border:0;outline:0;background:transparent;color:var(--clr-text-pri);font:11px var(--font-mono)}
  .activity-card{overflow:hidden;background:var(--clr-bg-sec);border:1px solid var(--clr-border);border-radius:12px}.activity-head,.activity-row{display:grid;grid-template-columns:minmax(280px,1fr) 190px 105px;gap:16px;align-items:center}.activity-head{padding:11px 16px;color:var(--clr-text-ter);font-size:10px;text-transform:uppercase;letter-spacing:.07em}.activity-row{min-height:76px;padding:12px 16px;border-top:1px solid var(--clr-border)}.activity-row:hover{background:var(--clr-bg-ter)}.page-copy{min-width:0;display:grid;gap:2px}.page-copy strong{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:13px}.page-copy span,.page-copy small,.when span{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:var(--clr-text-sec);font-size:10px}.page-copy small{font-family:var(--font-mono);color:var(--clr-text-ter)}.when{display:grid;gap:3px}.when strong{font:11px var(--font-mono)}.duration{text-align:right;color:var(--md-primary);font:12px var(--font-mono)}.empty{padding:30px;text-align:center;color:var(--clr-text-sec);font-size:12px}
  .overview-empty{min-height:220px;display:grid;place-content:center;gap:7px;border:1px dashed var(--clr-border);border-radius:12px}.overview-empty i{font-size:28px;color:var(--md-primary)}.overview-empty strong{color:var(--clr-text-pri)}
  @media(max-width:780px){.two-col{grid-template-columns:1fr}.browser-summary{grid-template-columns:1fr}.browser-summary>div+div{border-left:0;border-top:1px solid var(--clr-border)}.browser-tools{align-items:stretch;flex-direction:column}.browser-tools label,.browser-tools select{width:100%}.activity-head{display:none}.activity-row{grid-template-columns:1fr auto}.activity-row .when{grid-column:1}.duration{grid-row:1;grid-column:2}}
</style>
