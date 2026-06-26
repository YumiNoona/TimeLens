<script lang="ts">
  import type { CategoryEntry } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtTime } from '../utils';

  let { categories }: { categories: CategoryEntry[] } = $props();

  const total = $derived(categories.reduce((a, c) => a + c.minutes, 0) || 1);
  const sorted = $derived([...categories].sort((a, b) => b.minutes - a.minutes));
  const topCat = $derived(sorted[0] ?? null);

  const R = 70;
  const STROKE = 22;
  const CIR = 2 * Math.PI * R;

  const slices = $derived.by(() => {
    let off = 0;
    return sorted.map(cat => {
      const pct = cat.minutes / total;
      const dash = CIR * pct;
      return {
        name: cat.name,
        color: colorForCategory(cat.name),
        dashArray: dash,
        dashOffset: -off,
        pct,
        minutes: cat.minutes,
        percentage: cat.percentage,
      };
      off += dash;
    });
  });

  let hovered = $state<string | null>(null);
</script>

<div class="card">
  <div class="card-header">
    <i class="ti ti-chart-pie" aria-hidden="true"></i>
    <div class="card-title">Categories</div>
  </div>

  <div class="cat-body">
    <div class="cat-donut">
      <svg viewBox="0 0 180 180" aria-label="Category breakdown donut chart" role="img">
        <circle cx="90" cy="90" r={R} fill="none" stroke="var(--clr-bg-ter)" stroke-width={STROKE} />
        {#each slices as slice}
          <circle
            cx="90" cy="90" r={R}
            fill="none"
            stroke={slice.color}
            stroke-width={STROKE}
            stroke-dasharray="{slice.dashArray} {CIR - slice.dashArray}"
            stroke-dashoffset={slice.dashOffset}
            transform="rotate(-90 90 90)"
            stroke-linecap="butt"
            opacity={hovered ? (hovered === slice.name ? 1 : 0.3) : 1}
            style="transition: opacity 0.15s var(--ease-out)"
          />
        {/each}
      </svg>
      <div class="cat-donut-center">
        {#if topCat}
          <span class="cat-label">{topCat.name}</span>
          <span class="cat-pct-main">{topCat.percentage}%</span>
        {:else}
          <span class="cat-total">{fmtTime(total)}</span>
          <span class="cat-total-label">today</span>
        {/if}
      </div>
    </div>

    <div class="cat-legend">
      {#each slices as slice}
        <div
          class="cat-item"
          class:cat-hovered={hovered === slice.name}
          onmouseenter={() => hovered = slice.name}
          onmouseleave={() => hovered = null}
          role="listitem"
        >
          <span class="cat-dot" style="background: {slice.color}"></span>
          <span class="cat-name">{slice.name}</span>
          <span class="cat-pct">{slice.percentage}%</span>
          <span class="cat-time">{fmtTime(slice.minutes)}</span>
        </div>
      {/each}
    </div>
  </div>
</div>

<style>
  .cat-body {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: var(--space-4);
  }

  /* Donut */
  .cat-donut {
    position: relative;
    width: 160px;
    height: 160px;
    flex-shrink: 0;
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
    pointer-events: none;
  }

  .cat-label {
    font-size: var(--text-xs);
    color: var(--clr-text-sec);
    text-transform: capitalize;
    font-weight: var(--weight-medium);
    margin-bottom: var(--space-1);
    max-width: 80px;
    text-align: center;
    line-height: 1.2;
  }

  .cat-pct-main {
    font-size: var(--text-xl);
    font-weight: var(--weight-bold);
    color: var(--clr-text-pri);
    font-family: var(--font-mono);
    font-feature-settings: 'tnum';
    line-height: 1;
  }

  .cat-total {
    font-size: var(--text-xl);
    font-weight: var(--weight-bold);
    color: var(--clr-text-pri);
    font-family: var(--font-mono);
    line-height: 1;
  }

  .cat-total-label {
    font-size: 10px;
    color: var(--clr-text-ter);
    text-transform: uppercase;
    letter-spacing: 0.05em;
    font-weight: var(--weight-medium);
    margin-top: 4px;
  }

  /* Legend */
  .cat-legend {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-2) var(--space-5);
    width: 100%;
    padding: 0 var(--space-1);
  }

  .cat-item {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-1) 0;
    border-radius: var(--radius-xs);
    cursor: pointer;
    transition: background 0.1s var(--ease-out);
  }

  .cat-item:hover, .cat-hovered {
    background: rgba(255,255,255,0.04);
  }

  .cat-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    flex-shrink: 0;
  }

  .cat-name {
    font-size: var(--text-xs);
    color: var(--clr-text-pri);
    text-transform: capitalize;
    font-weight: var(--weight-medium);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .cat-pct {
    font-size: var(--text-xs);
    font-weight: var(--weight-semibold);
    color: var(--clr-text-pri);
    font-family: var(--font-mono);
    font-feature-settings: 'tnum';
    margin-left: auto;
    min-width: 28px;
    text-align: right;
  }

  .cat-time {
    font-size: 11px;
    color: var(--clr-text-sec);
    font-family: var(--font-mono);
    font-feature-settings: 'tnum';
    min-width: 36px;
    text-align: right;
  }
</style>
