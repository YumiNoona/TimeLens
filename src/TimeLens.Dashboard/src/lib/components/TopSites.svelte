<script lang="ts">
  import type { BrowserEntry } from '../types';
  import { colorForApp } from '../colors';

  let { sites }: { sites: BrowserEntry[] } = $props();

  const maxVisits = $derived(sites.length > 0 ? sites[0].visits : 1);
</script>

<div class="card">
  <div class="card-title">
    <i class="ti ti-world" aria-hidden="true"></i>
    Top sites
  </div>

  {#if sites.length === 0}
    <p class="empty">No browsing activity today.</p>
  {:else}
    <div role="list">
      {#each sites as site, i}
        <div class="bar-row" role="listitem">
          <div class="bar-domain" title={site.domain}>{site.domain}</div>
          <div class="bar-track">
            <div
              class="bar-fill"
              style="width: {Math.round(site.visits / maxVisits * 100)}%; background: {colorForApp(i)}"
            ></div>
          </div>
          <div class="bar-count">{site.visits}</div>
        </div>
      {/each}
    </div>
  {/if}
</div>

<style>
  .card {
    background: var(--md-surface-1);
    border-radius: var(--shape-lg);
    border: 1px solid var(--md-outline);
    padding: var(--sp-5);
  }

  .card-title {
    font-size: 14px;
    font-weight: 500;
    color: var(--md-on-surf);
    margin-bottom: var(--sp-4);
    display: flex;
    align-items: center;
    gap: var(--sp-2);
  }

  .card-title i { color: var(--md-on-surf-var); font-size: 16px; }

  .bar-row {
    display: flex;
    align-items: center;
    gap: var(--sp-3);
    margin-bottom: var(--sp-3);
  }

  .bar-row:last-child { margin-bottom: 0; }

  .bar-domain {
    width: 120px; flex-shrink: 0;
    font-size: 11px;
    font-family: var(--font-mono);
    color: var(--md-on-surf-var);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .bar-track {
    flex: 1; min-width: 60px;
    height: 16px;
    background: var(--md-surface);
    border-radius: var(--shape-full);
    overflow: hidden;
  }

  .bar-fill {
    height: 100%;
    border-radius: var(--shape-full);
  }

  .bar-count {
    width: 32px;
    text-align: right;
    font-size: 11px;
    font-family: var(--font-mono);
    color: var(--md-on-surf-dim);
  }

  .empty {
    font-size: 12px;
    color: var(--md-on-surf-dim);
    padding: var(--sp-3) 0;
  }
</style>
