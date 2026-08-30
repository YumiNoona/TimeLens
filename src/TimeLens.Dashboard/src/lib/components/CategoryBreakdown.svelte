<script lang="ts">
  import type { CategoryEntry } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtTime } from '../utils';

  let { categories, periodLabel = 'today' }: { categories: CategoryEntry[]; periodLabel?: string } = $props();

  const total = $derived(categories.reduce((sum, category) => sum + category.minutes, 0) || 1);
  const sorted = $derived([...categories].sort((a, b) => b.minutes - a.minutes));
  const RADIUS = 72;
  const STROKE = 18;
  const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
  const GAP = 4;

  const slices = $derived.by(() => {
    let offset = 0;
    return sorted.map(category => {
      const ratio = category.minutes / total;
      const fullDash = CIRCUMFERENCE * ratio;
      const result = {
        name: category.name,
        color: colorForCategory(category.name),
        dashArray: Math.max(0, fullDash - GAP),
        dashOffset: -offset,
        ratio,
        minutes: category.minutes,
        percentage: category.percentage,
      };
      offset += fullDash;
      return result;
    });
  });

  let hovered = $state<string | null>(null);
  const activeSlice = $derived(slices.find(slice => slice.name === hovered) ?? slices[0] ?? null);
  const visibleSlices = $derived(slices.slice(0, 5));
</script>

<div class="card category-card">
  <div class="card-header category-header">
    <div class="card-title-wrap">
      <i class="ti ti-chart-donut-4" aria-hidden="true"></i>
      <div><div class="card-title">Categories</div><span>Where your active time went</span></div>
    </div>
    <span class="tracked-pill"><i class="ti ti-clock-hour-4" aria-hidden="true"></i>{fmtTime(total)} tracked</span>
  </div>

  <div class="category-layout">
    <div class="donut-panel">
      <div class="donut-halo" aria-hidden="true"></div>
      <div class="cat-donut">
        <svg viewBox="0 0 190 190" aria-label="Category breakdown donut chart" role="img">
          <circle cx="95" cy="95" r={RADIUS} fill="none" stroke="var(--clr-bg-ter)" stroke-width={STROKE} />
          {#each slices as slice}
            <circle
              cx="95" cy="95" r={RADIUS}
              fill="none"
              stroke={slice.color}
              stroke-width={STROKE}
              stroke-dasharray="{slice.dashArray} {CIRCUMFERENCE - slice.dashArray}"
              stroke-dashoffset={slice.dashOffset}
              transform="rotate(-90 95 95)"
              stroke-linecap="butt"
              opacity={hovered ? (hovered === slice.name ? 1 : 0.2) : 1}
              style="filter:{hovered === slice.name ? `drop-shadow(0 0 5px ${slice.color})` : 'none'}"
            />
          {/each}
        </svg>
        <div class="cat-donut-center">
          {#if activeSlice}
            <span class="center-dot" style="background:{activeSlice.color}"></span>
            <span class="cat-pct-main">{activeSlice.percentage}%</span>
            <span class="cat-label">{activeSlice.name}</span>
            <span class="cat-time-main">{fmtTime(activeSlice.minutes)}</span>
          {:else}
            <span class="cat-pct-main">0%</span>
            <span class="cat-label">No activity</span>
          {/if}
        </div>
      </div>
      <div class="donut-caption"><span>Top category</span><strong>{slices[0]?.name ?? '—'}</strong><small>{periodLabel}</small></div>
    </div>

    <div class="category-list" role="list" aria-label="Category details">
      {#each visibleSlices as slice, index}
        <button
          type="button"
          class="category-row"
          class:active={hovered === slice.name || (!hovered && index === 0)}
          style="--rank-color:{slice.color}"
          onmouseenter={() => hovered = slice.name}
          onmouseleave={() => hovered = null}
          onfocus={() => hovered = slice.name}
          onblur={() => hovered = null}
        >
          <span class="rank">{index + 1}</span>
          <span class="category-copy"><strong>{slice.name}</strong><span class="mini-track"><span style="width:{slice.percentage}%;background:{slice.color}"></span></span></span>
          <span class="category-metric"><strong>{slice.percentage}%</strong><small>{fmtTime(slice.minutes)}</small></span>
        </button>
      {/each}
    </div>
  </div>
</div>

<style>
  .category-card { min-height: 100%; }
  .category-header { display: flex; align-items: center; justify-content: space-between; gap: 14px; }
  .card-title-wrap { display: flex; align-items: center; gap: 9px; }
  .card-title-wrap > i { color: var(--md-primary); font-size: 16px; }
  .card-title-wrap > div { display: flex; flex-direction: column; gap: 1px; }
  .card-title-wrap span { color: var(--clr-text-ter); font-size: 10px; }
  .tracked-pill { display: inline-flex; align-items: center; gap: 5px; padding: 5px 8px; color: var(--clr-text-sec); background: var(--clr-bg-ter); border: 1px solid var(--clr-border); border-radius: var(--shape-full); font: 10px var(--font-mono); white-space: nowrap; }
  .tracked-pill i { color: var(--md-primary); }
  .category-layout { display: grid; grid-template-columns: minmax(210px, .8fr) minmax(280px, 1.2fr); gap: 24px; padding: 14px 20px 20px; align-items: center; }
  .donut-panel { min-height: 222px; position: relative; display: flex; flex-direction: column; align-items: center; justify-content: center; overflow: visible; }
  .donut-halo { position: absolute; width: 168px; height: 168px; top: 24px; border-radius: 50%; background: color-mix(in srgb, var(--md-primary) 5%, transparent); filter: blur(18px); }
  .cat-donut { position: relative; width: 182px; height: 182px; z-index: 1; }
  .cat-donut svg { width: 100%; height: 100%; overflow: visible; }
  .cat-donut circle { transition: opacity 180ms var(--ease-out), filter 180ms var(--ease-out), stroke-width 180ms var(--ease-out); }
  .cat-donut-center { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; pointer-events: none; }
  .center-dot { width: 6px; height: 6px; margin-bottom: 7px; border-radius: 50%; box-shadow: 0 0 10px currentColor; }
  .cat-pct-main { color: var(--clr-text-pri); font: var(--weight-bold) 28px/1 var(--font-mono); letter-spacing: -.04em; }
  .cat-label { max-width: 88px; margin-top: 5px; overflow: hidden; color: var(--clr-text-sec); font-size: 11px; font-weight: 600; text-transform: capitalize; text-overflow: ellipsis; white-space: nowrap; }
  .cat-time-main { margin-top: 2px; color: var(--clr-text-ter); font: 9px var(--font-mono); }
  .donut-caption { z-index: 1; display: grid; grid-template-columns: auto auto; align-items: center; gap: 2px 6px; margin-top: -4px; }
  .donut-caption span { color: var(--clr-text-ter); font-size: 9px; text-transform: uppercase; letter-spacing: .06em; }
  .donut-caption strong { color: var(--clr-text-pri); font-size: 10px; text-transform: capitalize; }
  .donut-caption small { grid-column: 1 / -1; color: var(--clr-text-ter); font-size: 9px; text-align: center; }
  .category-list { display: flex; flex-direction: column; gap: 6px; }
  .category-row { min-height: 52px; display: grid; grid-template-columns: 34px minmax(0, 1fr) auto; align-items: center; gap: 11px; padding: 7px 10px; color: var(--clr-text-sec); background: color-mix(in srgb, var(--clr-bg-ter) 54%, transparent); border: 1px solid transparent; border-radius: 11px; font-family: inherit; text-align: left; cursor: pointer; transition: background 150ms var(--ease-out), border-color 150ms var(--ease-out); }
  .category-row:hover, .category-row.active { background: var(--clr-bg-ter); border-color: color-mix(in srgb, var(--rank-color, var(--md-primary)) 34%, var(--clr-border)); }
  .rank { width: 28px; height: 28px; display: grid; place-items: center; border-radius: 8px; color: var(--rank-color); background: color-mix(in srgb, var(--rank-color) 14%, var(--clr-bg-sec)); border: 1px solid color-mix(in srgb, var(--rank-color) 28%, transparent); font: 600 10px var(--font-mono); }
  .category-copy { min-width: 0; display: flex; flex-direction: column; gap: 8px; }
  .category-copy strong { overflow: hidden; color: var(--clr-text-pri); font-size: 11px; font-weight: 600; text-transform: capitalize; text-overflow: ellipsis; white-space: nowrap; }
  .mini-track { height: 4px; overflow: hidden; border-radius: 99px; background: var(--clr-bg-sec); }
  .mini-track span { display: block; height: 100%; min-width: 2px; border-radius: inherit; transition: width 300ms var(--ease-out); }
  .category-metric { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; }
  .category-metric strong { min-width: 42px; padding: 3px 6px; border-radius: 7px; color: var(--clr-text-pri); background: var(--clr-bg-sec); text-align: center; font: 600 11px var(--font-mono); }
  .category-metric small { color: var(--clr-text-ter); font: 9px var(--font-mono); }
  @media (max-width: 900px) {
    .category-layout { grid-template-columns: 1fr; }
    .donut-panel { min-height: 224px; }
  }
  @media (max-width: 520px) {
    .category-layout { padding: 8px 12px 14px; }
    .tracked-pill { display: none; }
  }
</style>
