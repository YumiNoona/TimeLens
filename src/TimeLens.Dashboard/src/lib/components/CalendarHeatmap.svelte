<script lang="ts">
  import type { HeatmapEntry } from '../types';
  import { fmtPrecise } from '../utils';
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

  let range = $state(0);
  let inspected = $state<HeatmapEntry | null>(null);
  const availableDays = $derived.by(() => {
    const first = entries.findIndex(e => e.value > 0);
    return first < 0 ? 28 : entries.length - first;
  });
  const days = $derived(range || Math.min($heatmapDays, availableDays <= 28 ? 28 : availableDays <= 91 ? 91 : $heatmapDays));
  const visibleEntries = $derived(entries.slice(-days));
  const activeDays = $derived(visibleEntries.filter(e => e.value > 0));
  const totalMinutes = $derived(visibleEntries.reduce((sum, e) => sum + e.value, 0));
  const peak = $derived([...visibleEntries].sort((a,b) => b.value-a.value)[0]);
  const detail = $derived(inspected ?? visibleEntries.find(e => e.date === selectedDate) ?? visibleEntries.at(-1));
  const maxVal = $derived(Math.max(...visibleEntries.map(e => e.value), 1));
  const rangeLabel = $derived(
    days === 28 ? 'Last 4 weeks' :
    days === 91 ? 'Last 3 months' :
    days === 273 ? 'Last 9 months' : 'Last 12 months'
  );

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
    return minutes <= 0 ? 'No recorded activity' : `${fmtPrecise(minutes * 60)} active`;
  }
</script>

<div class="heatmap-card" class:short-range={days <= 28}>
  <div class="hm-header">
    <span class="hm-title"><i class="ti ti-calendar" aria-hidden="true"></i>Activity</span>
    <div class="hm-ranges" aria-label="Activity range">{#each [28,91,273,365] as option}<button class:chosen={days === option} onclick={() => range = option}>{option === 28 ? '4w' : option === 91 ? '3m' : option === 273 ? '9m' : '1y'}</button>{/each}</div>
  </div>

  <div class="hm-summary"><span><strong>{fmtPrecise(totalMinutes * 60)}</strong> recorded</span><span><strong>{activeDays.length}</strong> active days</span><span><strong>{fmtPrecise(activeDays.length ? totalMinutes * 60 / activeDays.length : 0)}</strong> / active day</span></div>
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
              <span class="hm-month" style="grid-column: {ml.col + 1}; overflow:visible">{ml.text}</span>
            {/each}
          </div>

          {#if days <= 28}<div class="hm-weekdays" aria-hidden="true">{#each ['Sun','Mon','Tue','Wed','Thu','Fri','Sat'] as day}<span>{day}</span>{/each}</div>{/if}
          <div class="hm-grid" role="group" aria-label="Activity heatmap">
            {#each weeks as week}
              {#each week as cell}
                {#if cell}
                  <button
                    type="button"
                    class="hm-cell"
                    class:selected={cell.date === selectedDate}
                    style="background: {intensity(cell.value, maxVal)}; color: {cell.value / maxVal > .5 ? '#142015' : 'var(--md-on-surf)'}"
                    title="{fmtDate(cell.date)}: {fmtActivity(cell.value)}"
                    aria-label="{fmtDate(cell.date)}: {fmtActivity(cell.value)}"
                    aria-pressed={cell.date === selectedDate}
                    onmouseenter={() => inspected = cell}
                    onfocus={() => inspected = cell}
                    onclick={() => { inspected = cell; onselect?.(cell.date); }}
                  >{#if days <= 28}<span class="hm-date-number">{Number(cell.date.slice(-2))}</span>{/if}</button>
                {:else}
                  <div class="hm-cell empty"></div>
                {/if}
              {/each}
            {/each}
          </div>
        </div>
      </div>

      <div class="hm-legend" aria-label="Activity intensity from less to more">
        <span class="hm-leg-label">0s</span>
        <div class="hm-cell" style="background:var(--heat-0)"></div>
        <div class="hm-cell" style="background:var(--heat-1)"></div>
        <div class="hm-cell" style="background:var(--heat-2)"></div>
        <div class="hm-cell" style="background:var(--heat-3)"></div>
        <div class="hm-cell" style="background:var(--heat-4)"></div>
        <span class="hm-leg-label">{fmtPrecise((peak?.value ?? 0) * 60)}</span>
      </div>
    </div>
  </div>
  <div class="hm-detail" aria-live="polite"><span>{#if detail}<strong>{fmtDate(detail.date)}</strong> · {fmtActivity(detail.value)}{/if}</span><span>{rangeLabel}{#if peak?.value} · Best day {fmtDate(peak.date)}{/if}</span></div>
</div>

<style>
  .heatmap-card {
    --heat-0: color-mix(in srgb, var(--md-primary) 4%, var(--clr-bg-ter));
    --heat-1: color-mix(in srgb, var(--md-primary) 22%, var(--clr-bg-ter));
    --heat-2: color-mix(in srgb, var(--md-primary) 45%, var(--clr-bg-ter));
    --heat-3: color-mix(in srgb, var(--md-primary) 70%, var(--clr-bg-ter));
    --heat-4: color-mix(in srgb, var(--md-primary) 94%, var(--clr-bg-ter));
  }

  .heatmap-card {
    --hm-cell: clamp(13px, 1.5vw, 19px);
    width: 100%;
    min-height: 240px;
    max-width: 100%;
    box-sizing: border-box;
    display: flex;
    flex-direction: column;
    background: var(--md-surface-1);
    border-radius: var(--shape-lg);
    border: 1px solid var(--md-outline);
    padding: 16px 20px 14px;
    overflow: hidden;
  }

  .hm-ranges { display:flex; gap:4px; }
  .hm-ranges button { border:0; background:transparent; color:var(--md-on-surf-var); padding:6px 9px; border-radius:6px; cursor:pointer; }
  .hm-ranges button.chosen { background:var(--clr-bg-ter); color:var(--md-primary); }
  .hm-summary { display:flex; flex-wrap:wrap; gap:10px 24px; font-size:11px; color:var(--md-on-surf-var); margin-bottom:18px; }
  .hm-summary strong { color:var(--md-on-surf); font-variant-numeric:tabular-nums; }
  .hm-detail { display:flex; justify-content:space-between; flex-wrap:wrap; gap:8px; margin-top:14px; font-size:11px; color:var(--md-on-surf-var); }
  button:focus-visible { outline:2px solid var(--md-primary); outline-offset:3px; }
  .hm-weekdays { display:grid; grid-template-columns:repeat(7,1fr); gap:4px; margin-bottom:6px; color:var(--md-on-surf-var); font-size:10px; text-align:center; }
  .short-range .hm-content, .short-range .hm-scroll { width:100%; }
  .short-range .hm-day-labels, .short-range .hm-month-row { display:none; }
  .short-range .hm-grid { grid-auto-flow:row; grid-template-columns:repeat(7,minmax(24px,1fr)); grid-template-rows:none; grid-auto-rows:30px; gap:4px; }
  .short-range .hm-grid .hm-cell { width:100%; height:30px; border-radius:5px; }
  .hm-date-number { font:10px var(--font-mono); color:inherit; }
  .short-range button.hm-cell:hover { transform:none; }
  .hm-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    margin-bottom: 14px;
  }
  .hm-title { display: inline-flex; align-items: center; gap: 8px; font-size: 13px; font-weight: 600; color: var(--md-on-surf); }
  .hm-header i { color: var(--md-on-surf-var); font-size: 15px; }

  .hm-overflow {
    flex: 1;
    display: flex;
    align-items: center;
    overflow-x: auto;
    padding: 2px 8px 2px 2px;
    margin-inline: -2px;
    scrollbar-gutter: stable;
  }
  .hm-content { width: max-content; margin: 0 auto; padding-right: 4px; }

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
    color: var(--md-on-surf-var);
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
    color: var(--md-on-surf-var);
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
    color: var(--md-on-surf-var);
  }
  .hm-legend .hm-cell {
    width: 10px;
    height: 10px;
  }
</style>
