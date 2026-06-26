<script lang="ts">
  import type { BrowserEntry } from '../types';
  import { colorForApp } from '../colors';
  import { appIcon } from '../appIcons';

  let { sites }: { sites: BrowserEntry[] } = $props();

  const strippedDomains = $derived(
    sites.map(s => ({ ...s, displayDomain: s.domain.replace(/^www\./, '') }))
  );
  const maxVisits = $derived(sites.length > 0 ? sites[0].visits : 1);
</script>

<div class="card">
  <div class="card-header">
    <i class="ti ti-world" aria-hidden="true"></i>
    <div class="card-title">Top sites</div>
  </div>

  {#if sites.length === 0}
    <p class="empty-msg">No browsing activity today.</p>
  {:else}
    <div class="site-list">
      {#each strippedDomains as site, i}
        {@const icon = appIcon(site.domain)}
        <div class="site-row">
          {#if icon}
            <i class="ti {icon} site-icon-tabler" aria-hidden="true"></i>
          {:else}
            <span class="site-icon" style="background: {colorForApp(i)}">{site.domain.charAt(0).toUpperCase()}</span>
          {/if}
          <span class="site-name" title={site.domain}>{site.displayDomain}</span>
          <div class="site-bar-track">
            <div class="site-bar-fill" style="width: {Math.round(site.visits / maxVisits * 100)}%"></div>
          </div>
          <span class="site-count">{site.visits}</span>
        </div>
      {/each}
    </div>
  {/if}
</div>

<style>
  .site-list {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
  }

  .site-row {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-1) 0;
  }

  .site-icon {
    width: 22px;
    height: 22px;
    border-radius: var(--radius-sm);
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 10px;
    color: #0D0F0A;
    font-weight: var(--weight-bold);
    flex-shrink: 0;
  }

  .site-icon-tabler {
    font-size: var(--text-lg);
    color: var(--clr-text-sec);
    flex-shrink: 0;
    width: 22px;
    text-align: center;
  }

  .site-name {
    flex: 1;
    min-width: 0;
    font-size: var(--text-sm);
    color: var(--clr-text-pri);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    font-family: var(--font-mono);
  }

  .site-bar-track {
    flex: 1;
    height: 5px;
    background: var(--clr-bg-ter);
    border-radius: var(--radius-full);
    overflow: hidden;
  }

  .site-bar-fill {
    height: 100%;
    border-radius: var(--radius-full);
    background: var(--md-primary);
    opacity: 0.5;
    min-width: 2px;
    transition: width var(--duration-slow) var(--ease-out);
  }

  .site-count {
    width: 32px;
    text-align: right;
    font-size: var(--text-xs);
    font-family: var(--font-mono);
    color: var(--clr-text-sec);
    font-feature-settings: 'tnum';
    font-weight: var(--weight-medium);
  }

  .empty-msg {
    font-size: var(--text-sm);
    color: var(--clr-text-ter);
    padding: var(--space-4) 0;
    text-align: center;
  }
</style>
