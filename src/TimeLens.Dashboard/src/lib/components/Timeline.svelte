<script lang="ts">
  import type { TimelineBlock } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtHourShort } from '../utils';
  import { timeFormat } from '../stores/settings';

  let { blocks }: { blocks: TimelineBlock[] } = $props();

  const HOURS = [0, 3, 6, 9, 12, 15, 18, 21, 24];

  function fmtHour(h: number): string { return fmtHourShort(h, $timeFormat); }

  const filled = $derived.by(() => {
    if (blocks.length === 0) return [];
    const sorted = [...blocks].sort((a, b) => a.startHour - b.startHour);
    const result: TimelineBlock[] = [];
    let cursor = 0;

    for (const b of sorted) {
      if (b.startHour > cursor) {
        result.push({ startHour: cursor, endHour: b.startHour, type: 'gap', exeName: '', windowTitle: null, durationSeconds: 0 });
      }
      result.push(b);
      cursor = b.endHour;
    }
    if (cursor < 24) {
      result.push({ startHour: cursor, endHour: 24, type: 'gap', exeName: '', windowTitle: null, durationSeconds: 0 });
    }
    return result;
  });

  const nowHour = new Date().getHours() + new Date().getMinutes() / 60;
  const showNow = blocks.length > 0 && nowHour > 0 && nowHour < 24;

  const legendTypes = $derived([...new Set(blocks.map(b => b.type.toLowerCase()).filter(t => t !== 'gap'))]);
</script>

<div class="tl-track">
  <div class="tl-track-inner">
    {#each filled as block}
      <div
        class="tl-block"
        class:gap={block.type === 'gap'}
        title={block.type === 'gap' ? '' : `${block.type}`}
        style="left: {block.startHour / 24 * 100}%; width: {(block.endHour - block.startHour) / 24 * 100}%; background: {block.type === 'gap' ? 'var(--clr-bg-sec)' : colorForCategory(block.type)}"
      ></div>
    {/each}
    {#if showNow}
      <div class="tl-now" style="left: {nowHour / 24 * 100}%" aria-label="Current time">
        <div class="tl-now-dot"></div>
        <div class="tl-now-line"></div>
      </div>
    {/if}
  </div>

  <div class="tl-labels">
    {#each HOURS as h}
      <span class="tl-label" style="left: {h / 24 * 100}%">{fmtHour(h)}</span>
    {/each}
  </div>
</div>

{#if legendTypes.length > 0}
  <div class="tl-legend">
    {#each legendTypes as type}
      <div class="leg-item">
        <div class="leg-dot" style="background: {colorForCategory(type)}"></div>
        {type.charAt(0).toUpperCase() + type.slice(1)}
      </div>
    {/each}
  </div>
{/if}

<style>
  .tl-track {
    position: relative;
  }

  .tl-track-inner {
    position: relative;
    height: 10px;
    background: var(--clr-bg-ter);
    border-radius: 999px;
    overflow: hidden;
  }

  .tl-block {
    position: absolute;
    top: 0; height: 100%;
    min-width: 2px;
    border-radius: 999px;
  }

  .tl-block.gap { background: transparent !important; }

  .tl-now {
    position: absolute;
    top: -3px; bottom: -3px;
    width: 0;
    z-index: 2;
  }
  .tl-now-dot {
    width: 6px; height: 6px;
    border-radius: 50%;
    background: var(--md-primary);
    margin-left: -3px;
    margin-top: -1px;
    box-shadow: 0 0 6px var(--md-primary);
  }
  .tl-now-line {
    position: absolute;
    top: 5px; left: -0.5px;
    width: 1px; bottom: 2px;
    background: var(--md-primary);
    opacity: 0.4;
  }

  .tl-labels {
    position: relative;
    height: 14px;
    margin-top: 4px;
  }

  .tl-label {
    position: absolute;
    font-size: 9px; color: var(--clr-text-ter);
    font-family: var(--font-mono);
    transform: translateX(-50%);
  }
  .tl-label:first-child { transform: translateX(0); }
  .tl-label:last-child { transform: translateX(-100%); }

  .tl-legend {
    display: flex; flex-wrap: wrap; gap: 12px; margin-top: 8px;
  }

  .leg-item {
    display: flex; align-items: center; gap: 5px;
    font-size: 10px; color: var(--clr-text-sec);
  }

  .leg-dot { width: 6px; height: 6px; border-radius: 2px; }
</style>
