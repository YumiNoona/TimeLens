<script lang="ts">
  import type { HeatmapEntry } from '../types';

  let { entries }: { entries: HeatmapEntry[] } = $props();

  const DAY_LABELS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

  // Buckets: 0, 1-25%, 26-50%, 51-75%, 76%+
  function intensity(v: number, max: number): string {
    if (v === 0) return 'var(--heat-0)';
    const pct = v / max;
    if (pct <= 0.25) return 'var(--heat-1)';
    if (pct <= 0.50) return 'var(--heat-2)';
    if (pct <= 0.75) return 'var(--heat-3)';
    return 'var(--heat-4)';
  }

  const maxVal = $derived(Math.max(...entries.map(e => e.value), 1));

  // Build week-based grid
  const weeks = $derived.by((): (HeatmapEntry | null)[][] => {
    if (entries.length === 0) return [];
    const first = new Date(entries[0].date + 'T00:00:00');
    const startDay = first.getDay(); // 0=Sun, 6=Sat

    const result: (HeatmapEntry | null)[][] = [];
    let week: (HeatmapEntry | null)[] = [];

    // Pad first week
    for (let i = 0; i < startDay; i++) week.push(null);

    for (const e of entries) {
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
    if (entries.length === 0) return [];
    const labels: { text: string; col: number }[] = [];
    const firstDate = new Date(entries[0].date + 'T00:00:00');
    labels.push({ text: firstDate.toLocaleString('en-US', { month: 'short' }), col: 0 });

    for (let i = 1; i < entries.length; i++) {
      const d = new Date(entries[i].date + 'T00:00:00');
      if (d.getDate() === 1 || (i === 1 && d.getMonth() !== firstDate.getMonth())) {
        const startDayOfWeek = new Date(entries[0].date + 'T00:00:00').getDay();
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
</script>

<div class="heatmap-card">
  <div class="hm-header">
    <i class="ti ti-calendar" aria-hidden="true"></i>
    Last {entries.length} days
  </div>

  <div class="hm-body">
    <!-- Day labels -->
    <div class="hm-day-labels">
      <span>Mon</span>
      <span></span>
      <span>Wed</span>
      <span></span>
      <span>Fri</span>
      <span></span>
      <span></span>
    </div>

    <div class="hm-scroll">
      <!-- Month labels -->
      <div class="hm-month-row">
        {#each monthLabels as ml}
          <span class="hm-month" style="grid-column: {ml.col + 1} / span 2">{ml.text}</span>
        {/each}
      </div>

      <!-- Grid -->
      <div class="hm-grid" role="img" aria-label="Activity heatmap">
        {#each weeks as week}
          {#each week as cell}
            {#if cell}
              <div
                class="hm-cell"
                style="background: {intensity(cell.value, maxVal)}"
                title="{fmtDate(cell.date)}: {cell.value}h active"
              ></div>
            {:else}
              <div class="hm-cell empty"></div>
            {/if}
          {/each}
        {/each}
      </div>
    </div>

    <!-- Legend -->
    <div class="hm-legend">
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

<style>
  :root {
    --heat-0: rgba(200, 232, 106, 0.05);
    --heat-1: rgba(200, 232, 106, 0.20);
    --heat-2: rgba(200, 232, 106, 0.45);
    --heat-3: rgba(200, 232, 106, 0.70);
    --heat-4: rgba(200, 232, 106, 0.95);
  }

  .heatmap-card {
    background: var(--md-surface-1);
    border-radius: var(--shape-lg);
    border: 1px solid var(--md-outline);
    padding: var(--sp-4) var(--sp-5);
    overflow: hidden;
  }

  .hm-header {
    font-size: 13px;
    font-weight: 500;
    color: var(--md-on-surf);
    margin-bottom: var(--sp-3);
    display: flex;
    align-items: center;
    gap: var(--sp-2);
  }
  .hm-header i { color: var(--md-on-surf-var); font-size: 15px; }

  .hm-body {
    display: flex;
    gap: var(--sp-2);
    align-items: flex-start;
  }

  .hm-day-labels {
    display: grid;
    grid-template-rows: repeat(7, 12px);
    gap: 3px;
    padding-top: 16px;
    flex-shrink: 0;
  }
  .hm-day-labels span {
    font-size: 9px;
    color: var(--md-on-surf-dim);
    line-height: 12px;
    text-align: right;
  }

  .hm-scroll {
    overflow: hidden;
    flex: 1;
    min-width: 0;
  }

  .hm-month-row {
    display: grid;
    grid-auto-flow: column;
    grid-auto-columns: 12px;
    gap: 3px;
    margin-bottom: 3px;
    height: 12px;
  }
  .hm-month-row   .hm-month {
    font-size: 9px;
    color: var(--md-on-surf-dim);
    text-transform: uppercase;
    letter-spacing: 0.03em;
    font-weight: 500;
    white-space: nowrap;
    align-self: end;
  }

  .hm-grid {
    display: grid;
    grid-auto-flow: column;
    grid-template-rows: repeat(7, 12px);
    gap: 3px;
    grid-auto-columns: 12px;
  }

  .hm-cell {
    border-radius: 2px;
    width: 12px;
    height: 12px;
  }
  .hm-cell.empty {
    background: transparent !important;
  }

  .hm-legend {
    display: flex;
    align-items: center;
    gap: 3px;
    margin-top: var(--sp-2);
  }
  .hm-leg-label {
    font-size: 9px;
    color: var(--md-on-surf-dim);
  }
  .hm-legend .hm-cell {
    width: 11px;
    height: 11px;
  }
</style>
