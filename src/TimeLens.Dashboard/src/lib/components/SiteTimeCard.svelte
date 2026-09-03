<script lang="ts">
  let { browserTime }: { browserTime: { domain: string; totalMinutes: number }[] } = $props();

  const filtered = $derived(
    browserTime.filter(bt => bt.totalMinutes > 0 && bt.domain !== '127.0.0.1' && bt.domain !== 'test.example.com')
  );

  function formatDuration(minutes: number): string {
    if (minutes < 60) return `${minutes}m`;
    return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
  }

  function activityKind(domain: string): string {
    const value = domain.toLowerCase();
    if (/(youtube|netflix|primevideo|disneyplus|hotstar|twitch)/.test(value)) return 'Video & media';
    if (/(udemy|coursera|pluralsight|skillshare|edx|khanacademy)/.test(value)) return 'Learning';
    if (/(github|gitlab|stackoverflow|developer\.mozilla|learn\.microsoft)/.test(value)) return 'Development';
    return 'Website';
  }
</script>

{#if filtered.length > 0}
  <div class="card">
    <div class="card-header">
      <i class="ti ti-clock" aria-hidden="true"></i>
      <div class="card-title">Active time on sites</div>
    </div>
    <div class="browser-time-list">
      {#each filtered as bt}
        <div class="bt-row">
          <span class="bt-domain"><strong>{bt.domain.replace(/^www\./, '')}</strong><small>{activityKind(bt.domain)}</small></span>
          <span class="bt-time">{formatDuration(bt.totalMinutes)}</span>
        </div>
      {/each}
    </div>
  </div>
{/if}

<style>
  .browser-time-list {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .bt-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--space-2) 0;
    border-bottom: 1px solid var(--clr-border);
  }

  .bt-row:last-child { border-bottom: none; }

  .bt-domain {
    display: grid;
    gap: 2px;
    font-size: var(--text-sm);
    font-family: var(--font-mono);
    color: var(--clr-text-pri);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    flex: 1;
    margin-right: var(--space-3);
  }

  .bt-domain strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font: inherit; }
  .bt-domain small { color: var(--clr-text-ter); font: 10px var(--font-display); }

  .bt-time {
    font-size: var(--text-xs);
    font-family: var(--font-mono);
    color: var(--clr-text-sec);
    font-feature-settings: 'tnum';
    font-weight: var(--weight-medium);
    flex-shrink: 0;
  }
</style>
