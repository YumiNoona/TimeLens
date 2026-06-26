<script lang="ts">
  let { browserTime }: { browserTime: { domain: string; totalMinutes: number }[] } = $props();

  const filtered = $derived(
    browserTime.filter(bt => bt.totalMinutes > 0 && bt.domain !== '127.0.0.1' && bt.domain !== 'test.example.com')
  );
</script>

{#if filtered.length > 0}
  <div class="card">
    <div class="card-header">
      <i class="ti ti-clock" aria-hidden="true"></i>
      <div class="card-title">Time on sites</div>
    </div>
    <div class="browser-time-list">
      {#each filtered as bt}
        <div class="bt-row">
          <span class="bt-domain">{bt.domain.replace(/^www\./, '')}</span>
          <span class="bt-time">{bt.totalMinutes}m</span>
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
    font-size: var(--text-sm);
    font-family: var(--font-mono);
    color: var(--clr-text-pri);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    flex: 1;
    margin-right: var(--space-3);
  }

  .bt-time {
    font-size: var(--text-xs);
    font-family: var(--font-mono);
    color: var(--clr-text-sec);
    font-feature-settings: 'tnum';
    font-weight: var(--weight-medium);
    flex-shrink: 0;
  }
</style>
