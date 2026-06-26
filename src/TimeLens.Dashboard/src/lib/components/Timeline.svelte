<script lang="ts">
  import type { TimelineBlock } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtHourShort, fmtDuration } from '../utils';
  import { timeFormat } from '../stores/settings';

  let { blocks }: { blocks: TimelineBlock[] } = $props();

  function fmtHour(h: number): string { return fmtHourShort(h, $timeFormat); }

  const hourly = $derived.by(() => {
    const buckets: { hour: number; total: number; segments: { type: string; minutes: number }[] }[] = [];
    for (let h = 0; h < 24; h++) {
      buckets.push({ hour: h, total: 0, segments: [] });
    }
    for (const b of blocks) {
      const startH = Math.floor(b.startHour);
      const endH = Math.min(23, Math.floor(b.endHour - 0.001));
      for (let h = startH; h <= endH; h++) {
        const segStart = Math.max(b.startHour, h);
        const segEnd = Math.min(b.endHour, h + 1);
        const duration = Math.max(0, (segEnd - segStart) * 60);
        if (duration > 0) {
          buckets[h].total += duration;
          const existing = buckets[h].segments.find(s => s.type === b.type);
          if (existing) {
            existing.minutes += duration;
          } else {
            buckets[h].segments.push({ type: b.type, minutes: duration });
          }
        }
      }
    }
    return buckets;
  });

  const maxMinutes = $derived(Math.max(...hourly.map(h => h.total), 1));

  const nowHour = new Date().getHours() + new Date().getMinutes() / 60;
  const showNow = $derived(blocks.length > 0);

  const legendTypes = $derived([...new Set(blocks.map(b => b.type.toLowerCase()).filter(t => t !== 'gap' && t !== 'idle' && t !== 'away'))]);

  let tooltip = $state<{ hour: number; total: number; segments: { type: string; minutes: number }[]; x: number; y: number } | null>(null);
</script>

<div class="chart-wrap">
  <div class="chart-area"
    onmouseleave={() => tooltip = null}
  >
    <!-- Grid lines -->
    <div class="chart-grid">
      {#each [0, 6, 12, 18, 24] as h}
        <div class="grid-line" style="left: {h / 24 * 100}%"></div>
      {/each}
      {#each [0, 25, 50, 75, 100] as pct}
        <div class="grid-h-line" style="bottom: {pct}%"></div>
      {/each}
      <div class="grid-h-label-wrap" style="bottom: 0%"><span class="grid-h-label">0</span></div>
      <div class="grid-h-label-wrap" style="bottom: 50%"><span class="grid-h-label">{Math.round(maxMinutes / 2)}m</span></div>
      <div class="grid-h-label-wrap" style="bottom: 100%"><span class="grid-h-label">{Math.round(maxMinutes)}m</span></div>
    </div>

    <!-- Bars -->
    <div class="chart-bars">
      {#each hourly as h, i}
        <div
          class="bar-stack"
          style="left: {i / 24 * 100}%; width: {100 / 24 - 0.6}%"
          onmousemove={(e) => {
            const rect = (e.currentTarget as HTMLElement).closest('.chart-area')!.getBoundingClientRect();
            tooltip = { hour: h.hour, total: h.total, segments: h.segments, x: e.clientX - rect.left, y: e.clientY - rect.top };
          }}
        >
          {#each h.segments.filter(s => s.type !== 'gap').toSorted((a, b) => b.minutes - a.minutes) as seg}
            <div
              class="bar-segment"
              style="height: {Math.max(1, seg.minutes / maxMinutes * 100)}%; background: {colorForCategory(seg.type)}"
              title={`${seg.type}: ${Math.round(seg.minutes)}m`}
            ></div>
          {/each}
        </div>
      {/each}
    </div>

    <!-- Now line -->
    {#if showNow}
      <div class="now-line" style="left: {Math.min(99.7, nowHour / 24 * 100)}%">
        <div class="now-line-inner"></div>
        <div class="now-dot"></div>
      </div>
    {/if}

    <!-- Tooltip -->
    {#if tooltip && tooltip.segments.length > 0}
      <div class="chart-tooltip" style="left: {tooltip.x}px; top: {tooltip.y - 10}px">
        <div class="tooltip-header">
          {fmtHour(tooltip.hour)} — {fmtHour((tooltip.hour + 1) % 24)}
          <span class="tooltip-total">{Math.round(tooltip.total)}m</span>
        </div>
        {#each tooltip.segments.filter(s => s.type !== 'gap').toSorted((a, b) => b.minutes - a.minutes) as seg}
          <div class="tooltip-row">
            <span class="tooltip-dot" style="background: {colorForCategory(seg.type)}"></span>
            <span class="tooltip-type">{seg.type}</span>
            <span class="tooltip-val">{Math.round(seg.minutes)}m</span>
          </div>
        {/each}
      </div>
    {/if}
  </div>

  <!-- X-axis labels -->
  <div class="chart-x-labels">
    {#each [0, 3, 6, 9, 12, 15, 18, 21] as h}
      <span class="x-label" style="left: {h / 24 * 100}%">{fmtHour(h)}</span>
    {/each}
  </div>

  <!-- Legend -->
  {#if legendTypes.length > 0}
    <div class="chart-legend">
      {#each legendTypes as type}
        <div class="leg-item">
          <span class="leg-dot" style="background: {colorForCategory(type)}"></span>
          <span class="leg-name">{type}</span>
        </div>
      {/each}
    </div>
  {/if}
</div>

<style>
  .chart-wrap {
    width: 100%;
  }

  .chart-area {
    position: relative;
    height: 140px;
    margin-bottom: var(--space-2);
    cursor: crosshair;
  }

  /* Grid */
  .chart-grid {
    position: absolute;
    inset: 0;
    pointer-events: none;
  }

  .grid-line {
    position: absolute;
    top: 0;
    bottom: 0;
    width: 1px;
    background: rgba(255,255,255,0.06);
    margin-left: -0.5px;
  }

  .grid-h-line {
    position: absolute;
    left: 0;
    right: 0;
    height: 1px;
    background: rgba(255,255,255,0.04);
    margin-bottom: -0.5px;
  }

  .grid-h-label-wrap {
    position: absolute;
    left: -4px;
    transform: translateY(50%);
  }

  .grid-h-label {
    font-size: 9px;
    color: var(--clr-text-ter);
    font-family: var(--font-mono);
    font-weight: var(--weight-medium);
  }

  /* Bars */
  .chart-bars {
    position: absolute;
    inset: 0 0 20px 0;
    display: flex;
    align-items: flex-end;
    padding: 0 2px;
  }

  .bar-stack {
    position: absolute;
    bottom: 0;
    top: 0;
    display: flex;
    flex-direction: column;
    justify-content: flex-end;
    cursor: pointer;
  }

  .bar-segment {
    width: 100%;
    min-height: 2px;
    border-radius: 1px;
    transition: opacity 0.12s var(--ease-out), filter 0.12s var(--ease-out);
    flex-shrink: 0;
  }

  .bar-stack:hover .bar-segment {
    filter: brightness(1.25);
  }

  /* Now line */
  .now-line {
    position: absolute;
    top: -4px;
    bottom: 20px;
    width: 0;
    z-index: 3;
    pointer-events: none;
  }

  .now-line-inner {
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    width: 1.5px;
    background: rgba(255,255,255,0.5);
    margin-left: -0.75px;
  }

  .now-dot {
    position: absolute;
    left: 0;
    top: 50%;
    width: 10px;
    height: 10px;
    border-radius: 50%;
    background: #fff;
    margin-left: -5px;
    margin-top: -5px;
    box-shadow: 0 0 14px var(--md-primary), 0 0 4px var(--md-primary);
    animation: now-pulse 2s ease-in-out infinite;
  }

  @keyframes now-pulse {
    0%, 100% { box-shadow: 0 0 8px var(--md-primary), 0 0 2px var(--md-primary); }
    50% { box-shadow: 0 0 18px var(--md-primary), 0 0 8px var(--md-primary); }
  }

  /* Tooltip */
  .chart-tooltip {
    position: absolute;
    transform: translate(-50%, -100%);
    background: var(--clr-bg-ter);
    border: 1px solid var(--clr-border-strong);
    border-radius: var(--radius-md);
    padding: var(--space-2) var(--space-3);
    min-width: 120px;
    z-index: 10;
    pointer-events: none;
    box-shadow: var(--shadow-md);
  }

  .tooltip-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    font-size: var(--text-xs);
    font-weight: var(--weight-semibold);
    color: var(--clr-text-pri);
    margin-bottom: var(--space-1);
    padding-bottom: var(--space-1);
    border-bottom: 1px solid var(--clr-border);
  }

  .tooltip-total {
    font-family: var(--font-mono);
    font-size: var(--text-2xs);
    color: var(--clr-text-sec);
    font-weight: var(--weight-medium);
  }

  .tooltip-row {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: 2px 0;
  }

  .tooltip-dot {
    width: 7px;
    height: 7px;
    border-radius: 2px;
    flex-shrink: 0;
  }

  .tooltip-type {
    font-size: var(--text-2xs);
    color: var(--clr-text-sec);
    text-transform: capitalize;
    flex: 1;
  }

  .tooltip-val {
    font-size: var(--text-2xs);
    font-family: var(--font-mono);
    color: var(--clr-text-sec);
    font-weight: var(--weight-medium);
  }

  /* X-axis */
  .chart-x-labels {
    position: relative;
    height: 18px;
    margin-bottom: var(--space-2);
  }

  .x-label {
    position: absolute;
    top: 0;
    font-size: var(--text-2xs);
    color: var(--clr-text-ter);
    font-family: var(--font-mono);
    transform: translateX(-50%);
    font-weight: var(--weight-medium);
  }

  .x-label:first-child { transform: translateX(0); }

  /* Legend */
  .chart-legend {
    display: flex;
    flex-wrap: wrap;
    gap: var(--space-4);
  }

  .leg-item {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    font-size: var(--text-xs);
    font-weight: var(--weight-medium);
    color: var(--clr-text-sec);
    text-transform: capitalize;
  }

  .leg-dot {
    width: 8px;
    height: 8px;
    border-radius: 3px;
    flex-shrink: 0;
  }

  .leg-name {
    color: var(--clr-text-sec);
  }
</style>
