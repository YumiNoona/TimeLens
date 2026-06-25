<script lang="ts">
  import type { HeatmapEntry } from '../types';

  let { entries }: { entries: HeatmapEntry[] } = $props();

  const maxVal = $derived(Math.max(...entries.map(e => e.value), 1));

  function opacity(v: number): number {
    if (v === 0) return 0.06;
    return 0.12 + 0.76 * (v / maxVal);
  }

  const gridCells = $derived.by(() => {
    if (entries.length === 0) return [];
    const firstDate = new Date(entries[0].date + 'T00:00:00');
    const startDow = (firstDate.getDay() + 6) % 7;

    const cells: (HeatmapEntry | null)[] = [];
    for (let i = 0; i < startDow; i++) cells.push(null);
    for (const e of entries) cells.push(e);
    return cells;
  });
</script>

<div class="card">
  <div class="card-title">
    <i class="ti ti-calendar" aria-hidden="true"></i>
    Last 28 days
  </div>

  <div class="hm-layout">
    <div class="hm-labels" aria-hidden="true">
      <span>M</span>
      <span></span>
      <span>W</span>
      <span></span>
      <span>F</span>
    </div>
    <div class="hm-grid" role="img" aria-label="Activity heatmap for the last 28 days">
      {#each gridCells as cell}
        {#if cell}
          <div
            class="hm-cell"
            style="background: rgba(200, 232, 106, {opacity(cell.value).toFixed(2)})"
            title="{cell.date}: {cell.value}h active"
          ></div>
        {:else}
          <div class="hm-cell hm-empty"></div>
        {/if}
      {/each}
    </div>
  </div>
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

  .hm-layout {
    display: flex;
    gap: 6px;
  }

  .hm-labels {
    display: grid;
    grid-template-rows: repeat(5, 22px);
    gap: 3px;
    padding-top: 0;
    font-size: 10px;
    color: var(--md-on-surf-dim);
    font-family: var(--font-mono);
    line-height: 22px;
  }

  .hm-grid {
    display: grid;
    grid-auto-flow: column;
    grid-template-rows: repeat(7, 22px);
    gap: 3px;
  }

  .hm-cell {
    width: 22px;
    height: 22px;
    border-radius: 3px;
  }

  .hm-cell.hm-empty {
    visibility: hidden;
  }
</style>
