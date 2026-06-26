<script lang="ts">
  import { timeFormat as timeFormatStore } from '../stores/settings';

  let { browserHourly }: { browserHourly: { hour: number; visits: number }[] } = $props();
</script>

{#if browserHourly.length > 0}
  <div class="card">
    <div class="card-header">
      <i class="ti ti-chart-bar" aria-hidden="true"></i>
      <div class="card-title">Browser visits by hour</div>
    </div>
    <div class="browser-hourly-chart">
      {#each browserHourly as h}
        <div
          class="bh-bar"
          class:zero={h.visits === 0}
          style="height:{h.visits > 0 ? Math.max(3, h.visits / Math.max(...browserHourly.map(x => x.visits), 1) * 64) : 2}px"
          title="{h.hour}:00 — {h.visits} visits"
        ></div>
      {/each}
    </div>
    <div class="bh-labels">
      {#each [0, 6, 12, 18] as hr}
        <span>{$timeFormatStore === '24h'
          ? String(hr).padStart(2, '0') + ':00'
          : (hr === 12 ? '12p' : hr === 0 ? '12a' : hr > 12 ? (hr - 12) + 'p' : hr + 'a')}</span>
      {/each}
    </div>
  </div>
{/if}

<style>
  .browser-hourly-chart {
    display: flex;
    align-items: flex-end;
    gap: 2px;
    height: 64px;
    margin-bottom: var(--space-2);
  }

  .bh-bar {
    flex: 1;
    border-radius: 2px 2px 0 0;
    background: var(--md-primary);
    opacity: 0.45;
    min-height: 2px;
    cursor: pointer;
    transition: opacity var(--duration-fast) var(--ease-out);
  }

  .bh-bar:hover { opacity: 0.7; }
  .bh-bar.zero { opacity: 0.06; }

  .bh-labels {
    display: flex;
    justify-content: space-between;
    font-size: var(--text-2xs);
    color: var(--clr-text-ter);
    font-family: var(--font-mono);
  }
</style>
