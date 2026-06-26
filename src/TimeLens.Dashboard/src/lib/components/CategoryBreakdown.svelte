<script lang="ts">
  import type { CategoryEntry } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtTime } from '../utils';

  let { categories }: { categories: CategoryEntry[] } = $props();

  const total = $derived(categories.reduce((a, c) => a + c.minutes, 0) || 1);
  const sorted = $derived([...categories].sort((a, b) => b.minutes - a.minutes));
  const top5 = $derived(sorted.slice(0, 5));

  const R = 42;
  const CIR = 2 * Math.PI * R;
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
  <div class="card-header">
    <i class="ti ti-chart-pie" aria-hidden="true"></i>
    <div class="card-title">Categories</div>
  </div>

  <div class="cat-body">
    <div class="cat-left">
      {#each top5 as cat}
        <div class="cat-tag">
          <span class="cat-dot" style="background: {colorForCategory(cat.name)}"></span>
          <span class="cat-tag-name">{cat.name}</span>
        </div>
      {/each}
    </div>

    <div class="cat-center">
      <div class="cat-donut">
        <svg viewBox="0 0 100 100" aria-label="Category breakdown donut chart">
          <circle cx="50" cy="50" r={R} fill="none" stroke="var(--clr-bg-ter)" stroke-width="14" />
          {#each slices as slice}
            <circle
              cx="50" cy="50" r={R}
              fill="none"
              stroke={slice.color}
              stroke-width="14"
              stroke-dasharray="{slice.dashArray} {CIR - slice.dashArray}"
              stroke-dashoffset={slice.dashOffset}
              transform="rotate(-90 50 50)"
              stroke-linecap="butt"
            />
          {/each}
        </svg>
        <div class="cat-donut-center">
          <span class="cat-total">{sorted.length}</span>
          <span class="cat-total-label">categories</span>
        </div>
      </div>
    </div>

    <div class="cat-right">
      {#each top5 as cat}
        <div class="cat-stat">
          <span class="cat-pct">{cat.percentage}%</span>
          <span class="cat-time">{fmtTime(cat.minutes)}</span>
        </div>
      {/each}
    </div>
  </div>
</div>

<style>
  .cat-body {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: var(--space-5);
  }

  .cat-left {
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
    align-items: flex-end;
    flex: 1;
  }

  .cat-tag {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-1) 0;
  }

  .cat-dot {
    width: 10px;
    height: 10px;
    border-radius: var(--radius-xs);
    flex-shrink: 0;
  }

  .cat-tag-name {
    font-size: var(--text-sm);
    color: var(--clr-text-pri);
    font-weight: var(--weight-medium);
    text-transform: capitalize;
  }

  .cat-center {
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .cat-donut {
    position: relative;
    width: 160px;
    height: 160px;
  }

  .cat-donut svg {
    width: 100%;
    height: 100%;
  }

  .cat-donut-center {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
  }

  .cat-total {
    font-size: var(--text-2xl);
    font-weight: var(--weight-bold);
    color: var(--clr-text-pri);
    line-height: 1;
    font-family: var(--font-mono);
  }

  .cat-total-label {
    font-size: 10px;
    color: var(--clr-text-ter);
    text-transform: uppercase;
    letter-spacing: 0.05em;
    font-weight: var(--weight-medium);
    margin-top: 4px;
  }

  .cat-right {
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
    flex: 1;
  }

  .cat-stat {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-1) 0;
  }

  .cat-pct {
    font-size: var(--text-sm);
    font-weight: var(--weight-semibold);
    font-family: var(--font-mono);
    font-feature-settings: 'tnum';
    min-width: 32px;
    text-align: right;
  }

  .cat-time {
    font-size: var(--text-xs);
    color: var(--clr-text-sec);
    font-family: var(--font-mono);
    font-feature-settings: 'tnum';
    min-width: 48px;
    text-align: right;
  }
</style>
