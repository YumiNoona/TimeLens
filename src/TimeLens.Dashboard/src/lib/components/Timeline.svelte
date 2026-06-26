<script lang="ts">
  import type { TimelineBlock } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtDuration, fmtHourFull } from '../utils';
  import { timeFormat } from '../stores/settings';

  let { blocks }: { blocks: TimelineBlock[] } = $props();

  // Dynamic window — zoom to activity range with 30 min padding
  const minHour = $derived(blocks.length > 0 ? Math.max(0, Math.min(...blocks.map(b => b.startHour)) - 0.5) : 0);
  const maxHour = $derived(blocks.length > 0 ? Math.min(24, Math.max(...blocks.map(b => b.endHour)) + 0.5) : 24);
  const range = $derived(Math.max(maxHour - minHour, 0.5));

  function pctLeft(h: number): string { return `${((h - minHour) / range) * 100}%`; }
  function pctWidth(s: number, e: number): string { return `${((e - s) / range) * 100}%`; }

  // Current time needle
  const now = new Date();
  const nowHour = $derived(now.getHours() + now.getMinutes() / 60 + now.getSeconds() / 3600);
  const showNow = $derived(blocks.length > 0 && nowHour >= minHour && nowHour <= maxHour);

  // Hour labels (adaptive step)
  const hourLabels = $derived.by(() => {
    const labels: number[] = [];
    const step = range <= 4 ? 1 : range <= 8 ? 2 : 3;
    let h = Math.ceil(minHour / step) * step;
    while (h <= maxHour) {
      labels.push(h);
      h += step;
    }
    return labels;
  });

  function fmtHourLabel(h: number): string {
    const hr = h % 24;
    return `${hr === 0 ? 12 : hr > 12 ? hr - 12 : hr}${hr >= 12 ? 'p' : 'a'}`;
  }

  // Hover tooltip
  let tooltip = $state<{
    block: TimelineBlock;
    x: number;
  } | null>(null);

  const isActiveType = (t: string) => t !== 'idle' && t !== 'away' && t !== 'gap';

  let trackEl: HTMLDivElement;

  // Tooltip left position — clamped to card bounds
  function tooltipLeft(cx: number): string {
    if (!trackEl) return '50%';
    const tr = trackEl.getBoundingClientRect();
    const relX = cx - tr.left;
    const max = tr.width - 8;
    return `${Math.max(8, Math.min(max, relX))}px`;
  }
</script>

{#if blocks.length === 0}
  <div class="tl-empty">No activity recorded yet</div>
{:else}
  <div class="tl-wrap">
    <div
      class="tl-track"
      bind:this={trackEl}
      onmouseleave={() => tooltip = null}
      role="img"
      aria-label="Activity timeline from {minHour.toFixed(1)}h to {maxHour.toFixed(1)}h"
    >
      {#each blocks as block}
        {@const active = isActiveType(block.type)}
        <div
          class="tl-block"
          class:tl-active={active}
          class:tl-idle={!active}
          style="
            left: {pctLeft(block.startHour)};
            width: {pctWidth(block.startHour, block.endHour)};
            background: {active ? colorForCategory(block.type) : 'transparent'};
          "
          onmouseenter={(e) => {
            tooltip = { block, x: e.clientX };
          }}
          onmousemove={(e) => {
            if (tooltip) tooltip = { ...tooltip, x: e.clientX };
          }}
        >
        </div>
      {/each}

      {#if showNow}
        <div class="tl-now" style="left: {pctLeft(nowHour)}">
          <div class="tl-now-line"></div>
          <div class="tl-now-dot"></div>
        </div>
      {/if}

      {#if tooltip}
        <div
          class="tl-tooltip"
          style="left: {tooltipLeft(tooltip.x)}"
        >
          <div class="tl-tt-name">{tooltip.block.exeName || tooltip.block.type}</div>
          <div class="tl-tt-time">{fmtHourFull(tooltip.block.startHour, $timeFormat)} – {fmtHourFull(tooltip.block.endHour, $timeFormat)}</div>
          <div class="tl-tt-row">
            <span class="tl-tt-dot" style="background: {colorForCategory(tooltip.block.type)}"></span>
            <span class="tl-tt-type">{tooltip.block.type}</span>
            <span class="tl-tt-dur">{fmtDuration(tooltip.block.durationSeconds)}</span>
          </div>
          {#if tooltip.block.project}
            <div class="tl-tt-proj">{tooltip.block.project}</div>
          {/if}
        </div>
      {/if}
    </div>

    <div class="tl-labels">
      {#each hourLabels as h}
        <span
          class="tl-label"
          class:tl-label-exact={h === Math.round(h)}
          style="left: {pctLeft(h)}"
        >{fmtHourLabel(h)}</span>
      {/each}
    </div>
  </div>
{/if}

<style>
  .tl-wrap { width: 100%; }

  .tl-track {
    position: relative;
    height: 52px;
    background: var(--md-surface-2);
    border-radius: var(--radius-md);
    overflow: hidden;
    cursor: crosshair;
  }

  .tl-block {
    position: absolute;
    top: 0;
    bottom: 0;
    min-width: 1px;
    border-radius: 0;
    z-index: 1;
    box-sizing: border-box;
    user-select: none;
  }

  .tl-active {
    border-right: 1.5px solid rgba(0,0,0,0.25);
  }

  .tl-block:hover {
    filter: brightness(1.25);
    z-index: 3;
  }

  .tl-idle {
    border: none;
    background: none !important;
    z-index: 0;
  }

  /* Current time needle */
  .tl-now {
    position: absolute;
    top: 0;
    bottom: 0;
    width: 0;
    z-index: 5;
    pointer-events: none;
  }

  .tl-now-line {
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    width: 2px;
    background: rgba(255,255,255,0.65);
    margin-left: -1px;
  }

  .tl-now-dot {
    position: absolute;
    left: 0;
    top: 4px;
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: #fff;
    margin-left: -4px;
    box-shadow: 0 0 10px var(--md-primary), 0 0 4px var(--md-primary);
    animation: now-pulse 2s var(--ease-in-out) infinite;
  }

  @keyframes now-pulse {
    0%, 100% { box-shadow: 0 0 6px var(--md-primary), 0 0 2px var(--md-primary); }
    50% { box-shadow: 0 0 14px var(--md-primary), 0 0 6px var(--md-primary); }
  }

  /* Tooltip */
  .tl-tooltip {
    position: absolute;
    top: -8px;
    transform: translate(-50%, -100%);
    background: var(--clr-bg-ter);
    border: 1px solid var(--clr-border-strong);
    border-radius: var(--radius-md);
    padding: var(--space-2) var(--space-3);
    min-width: 150px;
    max-width: 260px;
    z-index: 20;
    pointer-events: none;
    box-shadow: var(--shadow-md);
    font-size: var(--text-xs);
  }

  .tl-tt-name {
    font-weight: var(--weight-semibold);
    color: var(--clr-text-pri);
    margin-bottom: 1px;
    text-transform: capitalize;
  }

  .tl-tt-time {
    font-size: var(--text-2xs);
    color: var(--clr-text-ter);
    font-family: var(--font-mono);
    margin-bottom: var(--space-1);
  }

  .tl-tt-row {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: 1px 0;
  }

  .tl-tt-dot {
    width: 7px;
    height: 7px;
    border-radius: 2px;
    flex-shrink: 0;
  }

  .tl-tt-type {
    color: var(--clr-text-sec);
    text-transform: capitalize;
    flex: 1;
    font-size: var(--text-2xs);
  }

  .tl-tt-dur {
    font-family: var(--font-mono);
    color: var(--clr-text-pri);
    font-weight: var(--weight-semibold);
    font-size: var(--text-2xs);
  }

  .tl-tt-proj {
    font-size: var(--text-2xs);
    color: var(--md-primary);
    margin-top: var(--space-1);
  }

  /* X-axis labels */
  .tl-labels {
    position: relative;
    height: 18px;
    margin-top: var(--space-1);
  }

  .tl-label {
    position: absolute;
    top: 0;
    font-size: 10px;
    color: var(--clr-text-ter);
    font-family: var(--font-mono);
    transform: translateX(-50%);
    font-weight: var(--weight-medium);
    user-select: none;
  }

  .tl-label-exact { color: var(--clr-text-sec); }

  /* Empty state */
  .tl-empty {
    display: flex;
    align-items: center;
    justify-content: center;
    height: 52px;
    font-size: var(--text-sm);
    color: var(--clr-text-ter);
    background: var(--md-surface-2);
    border-radius: var(--radius-md);
  }
</style>
