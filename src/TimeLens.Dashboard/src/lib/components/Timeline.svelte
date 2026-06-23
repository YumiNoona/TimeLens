<script lang="ts">
  import type { TimelineBlock } from '../types';
  import { colorForCategory } from '../colors';

  let { blocks }: { blocks: TimelineBlock[] } = $props();

  const hours = [0, 3, 6, 9, 12, 15, 18, 21];

  function fmtHour(h: number): string {
    if (h === 0) return '12a';
    if (h < 12) return h + 'a';
    if (h === 12) return '12p';
    return (h - 12) + 'p';
  }

  const legendTypes = ['dev', 'work', 'browse', 'social', 'idle'];
</script>

<div class="card">
  <div class="card-title">
    <i class="ti ti-timeline" aria-hidden="true"></i>
    24-hour timeline
  </div>

  <div class="tl-hour-row" aria-hidden="true">
    {#each hours as h}
      <div class="tl-hour">{fmtHour(h)}</div>
    {/each}
  </div>

  <div class="tl-track" role="img" aria-label="Activity timeline">
    {#each blocks as block}
      <div
        class="tl-block"
        style="flex: {block.endHour - block.startHour} 0 0; background: {colorForCategory(block.type)}"
      ></div>
    {/each}
  </div>

  <div class="tl-legend" role="list">
    {#each legendTypes as type}
      <div class="leg-item" role="listitem">
        <div class="leg-dot" style="background: {colorForCategory(type)}"></div>
        {type === 'dev' ? 'Dev' : type === 'browse' ? 'Browse' : type.charAt(0).toUpperCase() + type.slice(1)}
      </div>
    {/each}
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

  .tl-hour-row {
    display: flex;
    margin-bottom: var(--sp-1);
  }

  .tl-hour {
    flex: 3 0 0;
    font-size: 10px;
    font-family: var(--font-mono);
    color: var(--md-on-surf-dim);
  }

  .tl-track {
    display: flex;
    height: 28px;
    border-radius: var(--shape-sm);
    overflow: hidden;
    gap: 1px;
    background: var(--md-surface);
  }

  .tl-block { height: 100%; }

  .tl-legend {
    display: flex;
    flex-wrap: wrap;
    gap: var(--sp-4);
    margin-top: var(--sp-3);
  }

  .leg-item {
    display: flex;
    align-items: center;
    gap: var(--sp-2);
    font-size: 11px;
    color: var(--md-on-surf-var);
  }

  .leg-dot { width: 8px; height: 8px; border-radius: 2px; }
</style>
