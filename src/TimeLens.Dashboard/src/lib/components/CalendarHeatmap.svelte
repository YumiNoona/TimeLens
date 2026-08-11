<script lang="ts">
  import type { HeatmapEntry } from '../types';
  import { heatmapDays } from '../stores/settings';

  let {
    entries,
    selectedDate = '',
    onselect
  }: {
    entries: HeatmapEntry[];
    selectedDate?: string;
    onselect?: (date: string) => void;
  } = $props();

  // Buckets: 0, 1-25%, 26-50%, 51-75%, 76%+
  function intensity(v: number, max: number): string {
    if (v === 0) return 'var(--heat-0)';
    const pct = v / max;
    if (pct <= 0.25) return 'var(--heat-1)';
    if (pct <= 0.50) return 'var(--heat-2)';
    if (pct <= 0.75) return 'var(--heat-3)';
    return 'var(--heat-4)';
  }

  const visibleEntries = $derived(entries.slice(-$heatmapDays));
  const maxVal = $derived(Math.max(...visibleEntries.map(e => e.value), 1));
  const rangeLabel = $derived($heatmapDays === 28 ? 'Last 4 weeks' : $heatmapDays === 91 ? 'Last 3 months' : 'Last 6 months');

  // Build week-based grid
  const weeks = $derived.by((): (HeatmapEntry | null)[][] => {
    if (visibleEntries.length === 0) return [];
    const first = new Date(visibleEntries[0].date + 'T00:00:00');
    const startDay = first.getDay(); // 0=Sun, 6=Sat

    const result: (HeatmapEntry | null)[][] = [];
    let week: (HeatmapEntry | null)[] = [];

    // Pad first week
    for (let i = 0; i < startDay; i++) week.push(null);

    for (const e of visibleEntries) {
      week.push(e);
      if (week.length === 7) {
        result.push(week);
        week = [];
      }
    }
    // Pad last week
    if (week.length > 0) {
      while (week.length < 7) week.push(null);
      result.push(week);
    }
    return result;
  });

  // Month labels on columns
  const monthLabels = $derived.by((): { text: string; col: number }[] => {
    if (visibleEntries.length === 0) return [];
    const labels: { text: string; col: number }[] = [];
    const firstDate = new Date(visibleEntries[0].date + 'T00:00:00');
    labels.push({ text: firstDate.toLocaleString('en-US', { month: 'short' }), col: 0 });

    for (let i = 1; i < visibleEntries.length; i++) {
      const d = new Date(visibleEntries[i].date + 'T00:00:00');
      if (d.getDate() === 1 || (i === 1 && d.getMonth() !== firstDate.getMonth())) {
        const startDayOfWeek = new Date(visibleEntries[0].date + 'T00:00:00').getDay();
        const col = Math.floor((startDayOfWeek + i) / 7);
        labels.push({ text: d.toLocaleString('en-US', { month: 'short' }), col });
      }
    }
    // Deduplicate adjacent same-month labels
    return labels.filter((l, i, a) => i === 0 || l.text !== a[i - 1].text);
  });

  function fmtDate(d: string): string {
    const date = new Date(d + 'T00:00:00');
    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  }

  function fmtActivity(minutes: number): string {
    if (minutes <= 0) return 'No activity';
    if (minutes < 60) return `${minutes}m active`;
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return `${hours}h${mins ? ` ${mins}m` : ''} active`;
  }
</script>

<div class="heatmap-card">
  <div class="hm-header">
    <span class="hm-title"><i class="ti ti-calendar" aria-hidden="true"></i>Activity</span>
    <span class="hm-range">{rangeLabel}</span>
  </div>

  <div class="hm-overflow">
    <div class="hm-content">
      <div class="hm-body">
        <div class="hm-day-labels" aria-hidden="true">
          <span></span>
          <span>Mon</span>
          <span></span>
          <span>Wed</span>
          <span></span>
          <span>Fri</span>
          <span></span>
        </div>

        <div class="hm-scroll">
          <div class="hm-month-row" style="grid-template-columns: repeat({weeks.length}, var(--hm-cell))">
            {#each monthLabels as ml}
              <span class="hm-month" style="grid-column: {ml.col + 1} / span 2">{ml.text}</span>
            {/each}
          </div>

          <div class="hm-grid" role="group" aria-label="Activity heatmap">
            {#each weeks as week}
              {#each week as cell}
                {#if cell}
                  <button
                    type="button"
                    class="hm-cell"
                    class:selected={cell.date === selectedDate}
                    style="background: {intensity(cell.value, maxVal)}"
                    title="{fmtDate(cell.date)}: {fmtActivity(cell.value)}"
                    aria-label="{fmtDate(cell.date)}: {fmtActivity(cell.value)}"
                    aria-pressed={cell.date === selectedDate}
                    onclick={() => onselect?.(cell.date)}
                  ></button>
                {:else}
                  <div class="hm-cell empty"></div>
                {/if}
              {/each}
            {/each}
          </div>
        </div>
      </div>

      <div class="hm-legend" aria-label="Activity intensity from less to more">
        <span class="hm-leg-label">Less</span>
        <div class="hm-cell" style="background:var(--heat-0)"></div>
        <div class="hm-cell" style="background:var(--heat-1)"></div>
        <div class="hm-cell" style="background:var(--heat-2)"></div>
        <div class="hm-cell" style="background:var(--heat-3)"></div>
        <div class="hm-cell" style="background:var(--heat-4)"></div>
        <span class="hm-leg-label">More</span>
      </div>
    </div>
  </div>
</div>

<style>
  :root {
    --heat-0: color-mix(in srgb, var(--md-primary) 4%, var(--clr-bg-ter));
    --heat-1: color-mix(in srgb, var(--md-primary) 22%, var(--clr-bg-ter));
    --heat-2: color-mix(in srgb, var(--md-primary) 45%, var(--clr-bg-ter));
    --heat-3: color-mix(in srgb, var(--md-primary) 70%, var(--clr-bg-ter));
    --heat-4: color-mix(in srgb, var(--md-primary) 94%, var(--clr-bg-ter));
  }

  .heatmap-card {
    --hm-cell: 13px;
    width: max-content;
    max-width: 100%;
    background: var(--md-surface-1);
    border-radius: var(--shape-lg);
    border: 1px solid var(--md-outline);
    padding: 16px 20px 14px;
    overflow: hidden;
  }

  .hm-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    margin-bottom: 14px;
  }
  .hm-title { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; font-weight: 600; color: var(--md-on-surf); }
  .hm-header i { color: var(--md-on-surf-var); font-size: 15px; }
  .hm-range { font-size: 11px; color: var(--md-on-surf-dim); background: var(--clr-bg-ter); border-radius: var(--shape-full); padding: 4px 8px; }

  .hm-overflow { overflow-x: auto; padding: 2px 0; }
  .hm-content { width: max-content; margin: 0; }

  .hm-body {
    display: flex;
    gap: 8px;
    align-items: flex-start;
  }
  .hm-day-labels {
    display: grid;
    grid-template-rows: repeat(7, var(--hm-cell));
    gap: 3px;
    padding-top: 17px;
    width: 24px;
    flex-shrink: 0;
  }
  .hm-day-labels span {
    font-size: 9px;
    color: var(--md-on-surf-dim);
    line-height: var(--hm-cell);
    text-align: right;
  }

  .hm-scroll { min-width: 0; }

  .hm-month-row {
    display: grid;
    grid-auto-columns: var(--hm-cell);
    gap: 3px;
    margin-bottom: 4px;
    height: 13px;
  }

  .hm-month-row .hm-month {
    font-size: 9px;
    color: var(--md-on-surf-dim);
    font-weight: 500;
    white-space: nowrap;
    align-self: end;
  }

  .hm-grid {
    display: grid;
    grid-auto-flow: column;
    grid-template-rows: repeat(7, var(--hm-cell));
    gap: 3px;
    grid-auto-columns: var(--hm-cell);
  }

  .hm-cell {
    border-radius: 2px;
    width: var(--hm-cell);
    height: var(--hm-cell);
  }
  button.hm-cell {
    border: 0;
    padding: 0;
    cursor: pointer;
    transition: transform var(--duration-fast), box-shadow var(--duration-fast);
  }
  button.hm-cell:hover { transform: scale(1.2); box-shadow: 0 0 0 1px var(--md-on-surf); z-index: 2; }
  button.hm-cell.selected {
    box-shadow: 0 0 0 1px var(--clr-bg-pri), 0 0 0 2px var(--md-primary);
    z-index: 1;
  }
  .hm-cell.empty {
    background: transparent !important;
  }

  .hm-legend {
    display: flex;
    align-items: center;
    gap: 3px;
    margin-top: 12px;
    padding-right: 2px;
    justify-content: flex-end;
  }
  .hm-leg-label {
    font-size: 9px;
    color: var(--md-on-surf-dim);
  }
  .hm-legend .hm-cell {
    width: 10px;
    height: 10px;
  }
</style>
