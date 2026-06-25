<script lang="ts">
  import type { CategoryEntry } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtTime } from '../utils';

  let { categories }: { categories: CategoryEntry[] } = $props();

  const total = $derived(categories.reduce((a, c) => a + c.minutes, 0) || 1);
  const sorted = $derived([...categories].sort((a, b) => b.minutes - a.minutes));
  const top = $derived(sorted.slice(0, 5));

  const CIR = 2 * Math.PI * 28;
  const slices = $derived.by(() => {
    let off = 0;
    return sorted.map(cat => {
      const pct = cat.minutes / total;
      const dash = CIR * pct;
      const slice = { name: cat.name, color: colorForCategory(cat.name), dashArray: dash, dashOffset: -off };
      off += dash;
      return slice;
    });
  });
</script>

<div class="card">
  <div class="card-title">
    <i class="ti ti-chart-bar" aria-hidden="true"></i>
    Categories
  </div>

  <div class="donut-row">
    <svg viewBox="0 0 80 80" class="donut-svg" aria-label="Category breakdown donut">
      <circle cx="40" cy="40" r="28" fill="none" stroke="var(--clr-bg-ter)" stroke-width="12" />
      {#each slices as slice}
        <circle
          cx="40" cy="40" r="28"
          fill="none"
          stroke={slice.color}
          stroke-width="12"
          stroke-dasharray="{slice.dashArray} {CIR - slice.dashArray}"
          stroke-dashoffset={slice.dashOffset}
          transform="rotate(-90 40 40)"
        />
      {/each}
    </svg>

    <div class="cat-list">
      {#each top as cat}
        <div class="cat-dot-row">
          <div class="cat-dot" style="background: {colorForCategory(cat.name)}"></div>
          <span class="cat-dot-name">{cat.name}</span>
          <span class="cat-dot-pct" style="color: {colorForCategory(cat.name)}">{cat.percentage}%</span>
          <span class="cat-dot-time">{fmtTime(cat.minutes)}</span>
        </div>
      {/each}
    </div>
  </div>
</div>

<style>
  .donut-row {
    display: flex; align-items: flex-start; gap: 12px;
    font-size: 13px;
  }

  .donut-svg {
    width: 60px; height: 60px; flex-shrink: 0;
  }

  .cat-list { flex: 1; display: flex; flex-direction: column; gap: 4px; }

  .cat-dot-row {
    display: flex; align-items: center; gap: 6px;
  }

  .cat-dot {
    width: 8px; height: 8px; border-radius: 2px; flex-shrink: 0;
  }

  .cat-dot-name {
    width: 64px; font-size: 13px; color: var(--clr-text-pri);
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis; flex-shrink: 0;
  }

  .cat-dot-pct {
    width: 32px; text-align: right; font-weight: 500;
    font-family: var(--font-mono); flex-shrink: 0;
  }

  .cat-dot-time {
    font-size: 11px; color: var(--clr-text-sec);
    font-family: var(--font-mono); text-align: right; flex: 1;
  }
</style>
