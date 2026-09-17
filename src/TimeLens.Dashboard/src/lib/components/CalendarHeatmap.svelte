<script lang="ts">
  import { onMount } from 'svelte';
  import type { HeatmapEntry } from '../types';
  import { fmtPrecise } from '../utils';
  import { heatmapDays, showSeconds } from '../stores/settings';

  let { entries, selectedDate = '', onselect }: { entries: HeatmapEntry[]; selectedDate?: string; onselect?: (date: string) => void } = $props();
  let range = $state(0);
  let chartHost: HTMLDivElement | undefined = $state();
  let cellSize = $state(12);
  let cellGap = $state(3);
  const days = $derived(range || $heatmapDays);
  const visibleEntries = $derived(entries.slice(-days));
  const totalMinutes = $derived(visibleEntries.reduce((sum, entry) => sum + entry.value, 0));
  const activeDays = $derived(visibleEntries.filter(entry => entry.value > 0).length);
  const thresholds = $derived.by(() => {
    const values = visibleEntries.map(entry => entry.value).filter(Boolean).sort((a, b) => a - b);
    if (!values.length) return [1, 1, 1];
    const at = (fraction: number) => values[Math.min(values.length - 1, Math.floor((values.length - 1) * fraction))];
    return [at(.25), at(.5), at(.75)];
  });
  const weeks = $derived.by(() => {
    if (!visibleEntries.length) return [] as (HeatmapEntry | null)[][];
    const result: (HeatmapEntry | null)[][] = [];
    let week: (HeatmapEntry | null)[] = Array(new Date(`${visibleEntries[0].date}T00:00:00`).getDay()).fill(null);
    for (const entry of visibleEntries) {
      week.push(entry);
      if (week.length === 7) { result.push(week); week = []; }
    }
    if (week.length) { while (week.length < 7) week.push(null); result.push(week); }
    return result;
  });
  const monthLabels = $derived.by(() => {
    const labels: { text: string; col: number }[] = [];
    let lastColumn = -5;
    if (!visibleEntries.length) return labels;
    const offset = new Date(`${visibleEntries[0].date}T00:00:00`).getDay();
    visibleEntries.forEach((entry, index) => {
      const date = new Date(`${entry.date}T00:00:00`);
      const previous = index ? new Date(`${visibleEntries[index - 1].date}T00:00:00`) : null;
      const column = Math.floor((offset + index) / 7);
      if ((index === 0 || date.getMonth() !== previous?.getMonth()) && column - lastColumn >= 3) {
        labels.push({ text: date.toLocaleString('en-US', { month: 'short' }), col: column });
        lastColumn = column;
      }
    });
    return labels;
  });

  function level(value: number): number {
    if (value <= 0) return 0;
    if (value <= thresholds[0]) return 1;
    if (value <= thresholds[1]) return 2;
    if (value <= thresholds[2]) return 3;
    return 4;
  }
  function dateLabel(value: string) { return new Date(`${value}T00:00:00`).toLocaleDateString('en-US', { weekday:'short', month:'short', day:'numeric', year:'numeric' }); }
  function activityLabel(value: number) { return value <= 0 ? 'No recorded activity' : `${fmtPrecise(value * 60, $showSeconds)} active`; }
  function fitChart() {
    if (!chartHost || !weeks.length) return;
    const gap = chartHost.clientWidth < 620 ? 2 : 3;
    const available = chartHost.clientWidth - 34 - Math.max(0, weeks.length - 1) * gap;
    cellGap = gap;
    cellSize = Math.max(4, Math.min(12, Math.floor(available / weeks.length)));
  }

  onMount(() => {
    if (!chartHost) return;
    const observer = new ResizeObserver(fitChart);
    observer.observe(chartHost);
    fitChart();
    return () => observer.disconnect();
  });
</script>

<section class="heatmap-card" aria-labelledby="contributions-title" style="--cell:{cellSize}px;--gap:{cellGap}px">
  <header>
    <div><h2 id="contributions-title">Activity contributions</h2><p>{activeDays} active days · {fmtPrecise(totalMinutes * 60, $showSeconds)} recorded</p></div>
    <div class="ranges" aria-label="Contribution range">
      {#each [{v:91,l:'3m'},{v:182,l:'6m'},{v:365,l:'1y'}] as option}
        <button type="button" class:chosen={days === option.v} onclick={() => { range = option.v; requestAnimationFrame(fitChart); }}>{option.l}</button>
      {/each}
    </div>
  </header>
  <div class="chart-scroll" bind:this={chartHost}><div class="chart">
    <div class="day-labels" aria-hidden="true"><span></span><span>Mon</span><span></span><span>Wed</span><span></span><span>Fri</span><span></span></div>
    <div class="calendar">
      <div class="months" style="grid-template-columns:repeat({weeks.length},var(--cell))">{#each monthLabels as label}<span style="grid-column:{label.col + 1}">{label.text}</span>{/each}</div>
      <div class="grid" role="grid" aria-label="Daily activity contributions">
        {#each weeks as week}{#each week as cell}
          {#if cell}<button type="button" class="cell level-{level(cell.value)}" class:selected={cell.date === selectedDate} title="{dateLabel(cell.date)}: {activityLabel(cell.value)}" aria-label="{dateLabel(cell.date)}: {activityLabel(cell.value)}" aria-pressed={cell.date === selectedDate} onclick={() => onselect?.(cell.date)}></button>
          {:else}<span class="cell blank" aria-hidden="true"></span>{/if}
        {/each}{/each}
      </div>
    </div>
  </div></div>
  <footer><span>{days === 365 ? 'Last 12 months' : days === 182 ? 'Last 6 months' : 'Last 3 months'}</span><div class="legend" aria-label="Less to more activity"><span>Less</span>{#each [0,1,2,3,4] as n}<i class="cell level-{n}"></i>{/each}<span>More</span></div></footer>
</section>

<style>
  .heatmap-card { --heat-0:color-mix(in srgb,var(--clr-border) 56%,var(--clr-bg-ter)); --heat-1:color-mix(in srgb,var(--md-primary) 24%,var(--clr-bg-ter)); --heat-2:color-mix(in srgb,var(--md-primary) 46%,var(--clr-bg-ter)); --heat-3:color-mix(in srgb,var(--md-primary) 70%,var(--clr-bg-ter)); --heat-4:var(--md-primary); width:100%; box-sizing:border-box; overflow:hidden; padding:16px 18px 12px; background:var(--md-surface-1); border:1px solid var(--md-outline); border-radius:var(--shape-lg); }
  header, footer { display:flex; align-items:center; justify-content:space-between; gap:12px; }
  h2 { margin:0; color:var(--md-on-surf); font-size:13px; font-weight:650; }
  header p { margin:3px 0 0; color:var(--md-on-surf-var); font-size:10px; }
  .ranges { display:flex; padding:3px; gap:2px; background:var(--clr-bg-sec); border:1px solid var(--clr-border); border-radius:8px; }
  .ranges button { height:25px; min-width:31px; padding:0 7px; border:0; border-radius:5px; color:var(--md-on-surf-var); background:transparent; font:10px var(--font-mono); cursor:pointer; }
  .ranges button.chosen { color:var(--md-primary); background:var(--clr-bg-ter); }
  .chart-scroll { width:100%; margin-top:14px; overflow:hidden; }
  .chart { width:max-content; max-width:100%; display:flex; justify-content:flex-start; gap:8px; padding:0 1px 3px; }
  .day-labels { width:23px; flex:none; display:grid; grid-template-rows:repeat(7,var(--cell)); gap:var(--gap); padding-top:18px; }
  .day-labels span { color:var(--md-on-surf-var); font-size:8px; line-height:var(--cell); text-align:right; }
  .calendar { width:max-content; }.months { height:14px; display:grid; grid-auto-columns:var(--cell); gap:var(--gap); margin-bottom:4px; }.months span { overflow:visible; white-space:nowrap; color:var(--md-on-surf-var); font-size:9px; }
  .grid { display:grid; grid-auto-flow:column; grid-template-rows:repeat(7,var(--cell)); grid-auto-columns:var(--cell); gap:var(--gap); }
  .cell { width:var(--cell); height:var(--cell); box-sizing:border-box; display:block; padding:0; border:1px solid color-mix(in srgb,var(--clr-border) 55%,transparent); border-radius:2px; background:var(--heat-0); }
  button.cell { cursor:pointer; transition:transform 90ms ease,box-shadow 90ms ease; }button.cell:hover { transform:scale(1.35); box-shadow:0 0 0 1px var(--md-on-surf); z-index:2; }button.cell:focus-visible,button.cell.selected{outline:none;box-shadow:0 0 0 1px var(--clr-bg-pri),0 0 0 2px var(--md-primary);z-index:2}
  .level-1{background:var(--heat-1)}.level-2{background:var(--heat-2)}.level-3{background:var(--heat-3)}.level-4{background:var(--heat-4)}.blank{visibility:hidden}
  footer { margin-top:9px; color:var(--md-on-surf-var); font-size:9px; }.legend{display:flex;align-items:center;gap:3px}.legend .cell{width:10px;height:10px}
  @media(max-width:600px){.heatmap-card{padding:14px 10px 11px}.chart-scroll{margin-top:12px}.months span{font-size:8px}.day-labels{width:20px}.ranges button{min-width:28px;padding:0 5px}}
</style>
