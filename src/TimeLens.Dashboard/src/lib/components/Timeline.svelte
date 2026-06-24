<script lang="ts">
  import type { TimelineBlock } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtHourShort, fmtDuration } from '../utils';

  let { blocks }: { blocks: TimelineBlock[] } = $props();

  const HOURS = [0, 3, 6, 9, 12, 15, 18, 21, 24];

  function fmtHour(h: number): string { return fmtHourShort(h); }

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

  const legendTypes = ['dev', 'work', 'browse', 'social', 'idle', 'away'];
</script>

<div class="card">
  <div class="card-title">
    <i class="ti ti-timeline" aria-hidden="true"></i>
    24-hour timeline
  </div>

  {#if blocks.length === 0}
    <div class="tl-empty">
      <i class="ti ti-clock-hour-4" aria-hidden="true"></i>
      <span>No activity recorded yet</span>
      <span class="tl-empty-hint">TimeLens tracks your foreground apps automatically</span>
    </div>
  {:else}
    <div class="tl-container" role="img" aria-label="Activity timeline">
      {#each HOURS as h}
        <div
          class="tl-hour-mark"
          style="left: {h / 24 * 100}%"
          aria-hidden="true"
        >
          {fmtHour(h)}
        </div>
      {/each}

      <div class="tl-track">
        {#each filled as block}
          <div
            class="tl-block"
            class:gap={block.type === 'gap'}
            data-tooltip={block.type === 'gap' ? undefined : `${block.type} · ${block.exeName} · ${fmtDuration(block.durationSeconds)}`}
            style="left: {block.startHour / 24 * 100}%; width: {(block.endHour - block.startHour) / 24 * 100}%; background: {block.type === 'gap' ? 'transparent' : colorForCategory(block.type)}"
          ></div>
        {/each}
        {#if showNow}
          <div class="tl-now" style="left: {nowHour / 24 * 100}%" aria-label="Current time">
            <div class="tl-now-dot"></div>
            <div class="tl-now-line"></div>
          </div>
        {/if}
      </div>
    </div>
  {/if}

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

  .tl-container {
    position: relative;
    padding-top: var(--sp-4);
    margin-bottom: var(--sp-3);
  }

  .tl-hour-mark {
    position: absolute;
    top: 0;
    transform: translateX(-50%);
    font-size: 10px;
    font-family: var(--font-mono);
    color: var(--md-on-surf-dim);
  }

  .tl-track {
    position: relative;
    height: 28px;
    border-radius: var(--shape-sm);
    background: var(--md-surface);
    overflow: visible;
  }

  .tl-block {
    position: absolute;
    top: 0;
    height: 100%;
    min-width: 2px;
    border-radius: var(--shape-full);
    overflow: hidden;
  }

  .tl-block.gap { background: transparent !important; }

  .tl-block[data-tooltip]:hover::after {
    content: attr(data-tooltip);
    position: absolute;
    bottom: calc(100% + 6px);
    left: 50%;
    transform: translateX(-50%);
    background: var(--md-surface-3);
    border: 1px solid var(--md-outline-var);
    color: var(--md-on-surf);
    font-size: 11px;
    font-family: var(--font-mono);
    padding: 4px 8px;
    border-radius: var(--shape-sm);
    white-space: nowrap;
    pointer-events: none;
    z-index: 10;
  }

  .tl-now {
    position: absolute;
    top: -4px;
    bottom: 0;
    width: 0;
    z-index: 2;
  }
  .tl-now-dot {
    width: 8px; height: 8px;
    border-radius: 50%;
    background: var(--md-primary);
    margin-left: -4px;
    box-shadow: 0 0 6px var(--md-primary);
  }
  .tl-now-line {
    position: absolute;
    top: 10px;
    left: -0.5px;
    width: 1px;
    bottom: 0;
    background: var(--md-primary);
    opacity: 0.4;
  }

  .tl-empty {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding: var(--sp-6) 0;
    gap: var(--sp-2);
    color: var(--md-on-surf-dim);
  }
  .tl-empty i { font-size: 32px; color: var(--md-on-surf-dim); }
  .tl-empty span { font-size: 13px; }
  .tl-empty-hint { font-size: 11px !important; color: var(--md-on-surf-dim); opacity: 0.6; }

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
