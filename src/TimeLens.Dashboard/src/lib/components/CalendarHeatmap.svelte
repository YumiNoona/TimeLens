<script lang="ts">
  import type { HeatmapEntry } from '../types';

  let { entries }: { entries: HeatmapEntry[] } = $props();

  const maxVal = $derived(Math.max(...entries.map(e => e.value), 1));

  function opacity(v: number): number {
    if (v === 0) return 0.06;
    return 0.12 + 0.76 * (v / maxVal);
  }
</script>

<div class="card">
  <div class="card-title">
    <i class="ti ti-calendar" aria-hidden="true"></i>
    Last 28 days
  </div>

  <div class="hm-grid" role="img" aria-label="Activity heatmap for the last 28 days">
    {#each entries as entry}
      <div
        class="hm-cell"
        style="background: rgba(200, 232, 106, {opacity(entry.value).toFixed(2)})"
        title="{entry.date}: {entry.value}h active"
      ></div>
    {/each}
  </div>

  <div class="hm-days" aria-hidden="true">
    <span class="hm-day">M</span>
    <span class="hm-day">T</span>
    <span class="hm-day">W</span>
    <span class="hm-day">T</span>
    <span class="hm-day">F</span>
    <span class="hm-day">S</span>
    <span class="hm-day">S</span>
  </div>
</div>

<style>
  .card {
    background: var(--md-surface-1);
    border-radius: var(--shape-lg);
    border: 1px solid var(--md-outline);
    padding: var(--sp-5);
    max-width: 280px;
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

  .hm-grid {
    display: grid;
    grid-template-columns: repeat(7, 1fr);
    gap: 3px;
  }

  .hm-cell {
    aspect-ratio: 1;
    border-radius: 3px;
  }

  .hm-days {
    display: flex;
    justify-content: space-around;
    margin-top: var(--sp-2);
  }

  .hm-day {
    font-size: 9px;
    color: var(--md-on-surf-dim);
    font-family: var(--font-mono);
  }
</style>
