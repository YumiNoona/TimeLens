<script lang="ts">
  import type { HeatmapEntry } from '../types';

  let { entries }: { entries: HeatmapEntry[] } = $props();

  const maxVal = $derived(Math.max(...entries.map(e => e.value), 1));

  function opacity(v: number): number {
    if (v === 0) return 0.06;
    return 0.12 + 0.76 * (v / maxVal);
  }

  const monthLabels = $derived.by(() => {
    if (entries.length === 0) return [];
    const labels: { text: string; index: number }[] = [];
    const colsPerDay = 12;
    const firstDate = new Date(entries[0].date + 'T00:00:00');
    labels.push({ text: firstDate.toLocaleString('en-US', { month: 'short' }), index: 0 });
    for (let i = 1; i < entries.length; i++) {
      const d = new Date(entries[i].date + 'T00:00:00');
      if (d.getDate() <= 7) {
        labels.push({ text: d.toLocaleString('en-US', { month: 'short' }), index: i });
      }
    }
    return labels;
  });
</script>

<div class="card r1h">
  <div class="card-title">
    <i class="ti ti-calendar" aria-hidden="true"></i>
    Last 28 days
  </div>

  <div class="hm-month-labels">
    {#each monthLabels as ml}
      <span class="hm-month" style="grid-column: {ml.index + 1}">{ml.text}</span>
    {/each}
  </div>

  <div class="hm-grid" role="img" aria-label="Activity heatmap for the last 28 days">
    {#each entries as cell, i}
      <div
        class="hm-cell"
        style="background: rgba(200, 232, 106, {opacity(cell.value).toFixed(2)})"
        title="{cell.date}: {cell.value}h active"
      ></div>
    {/each}
  </div>
</div>

<style>
  .card { background: var(--clr-bg-sec); border-radius: var(--shape-md); padding: 16px 18px; }

  .card-title {
    font-size: 12px; font-weight: 500; color: var(--clr-text-pri);
    margin-bottom: 12px; display: flex; align-items: center; gap: 6px;
  }
  .card-title i { font-size: 14px; color: var(--clr-text-sec); }

  .hm-month-labels {
    display: grid;
    grid-template-columns: repeat(28, 1fr);
    gap: 3px;
    margin-bottom: 2px;
  }

  .hm-month {
    font-size: 8px;
    color: var(--clr-text-ter);
    text-transform: uppercase;
    letter-spacing: 0.04em;
    font-weight: 500;
  }

  .hm-grid {
    display: grid;
    grid-template-columns: repeat(28, 1fr);
    gap: 3px;
  }

  .hm-cell {
    aspect-ratio: 1;
    border-radius: 2px;
  }
</style>
