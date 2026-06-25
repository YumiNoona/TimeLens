<script lang="ts">
  import type { BrowserEntry } from '../types';
  import { colorForApp } from '../colors';

  let { sites }: { sites: BrowserEntry[] } = $props();

  const maxVisits = $derived(sites.length > 0 ? sites[0].visits : 1);
</script>

<div class="card r1h">
  <div class="card-title">
    <i class="ti ti-world" aria-hidden="true"></i>
    Top sites
  </div>

  {#if sites.length === 0}
    <p class="empty">No browsing activity today.</p>
  {:else}
    <div class="site-list">
      {#each sites as site, i}
        <div class="site-row">
          <div class="site-icon" style="background: {colorForApp(i)}">{site.domain.charAt(0).toUpperCase()}</div>
          <span class="site-name" title={site.domain}>{site.domain}</span>
          <div class="site-bar">
            <div class="site-fill" style="width: {Math.round(site.visits / maxVisits * 100)}%"></div>
          </div>
          <span class="site-count">{site.visits}</span>
        </div>
      {/each}
    </div>
  {/if}
</div>

<style>
  .card { background: var(--clr-bg-sec); border-radius: var(--shape-md); padding: 16px 18px; }
  .card-title {
    font-size: 12px; font-weight: 500; color: var(--clr-text-pri);
    margin-bottom: 12px; display: flex; align-items: center; gap: 6px;
  }
  .card-title i { font-size: 14px; color: var(--clr-text-sec); }

  .site-list { display: flex; flex-direction: column; gap: 6px; }
  .site-row { display: flex; align-items: center; gap: 8px; }

  .site-icon {
    width: 16px; height: 16px; border-radius: 3px;
    display: flex; align-items: center; justify-content: center;
    font-size: 9px; color: #0D0F0A; font-weight: 700; flex-shrink: 0;
  }

  .site-name {
    width: 120px; flex-shrink: 0;
    font-size: 11px; color: var(--clr-text-pri);
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }

  .site-bar {
    flex: 1; height: 6px;
    background: var(--clr-bg-ter);
    border-radius: 999px; overflow: hidden;
  }

  .site-fill {
    height: 100%; border-radius: 999px;
    background: var(--md-primary); opacity: 0.5;
  }

  .site-count {
    width: 28px; text-align: right;
    font-size: 10px; font-family: var(--font-mono); color: var(--clr-text-sec);
  }

  .empty { font-size: 11px; color: var(--clr-text-ter); padding: 8px 0; }
</style>
