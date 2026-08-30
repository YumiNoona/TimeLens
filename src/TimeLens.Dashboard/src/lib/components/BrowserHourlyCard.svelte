<script lang="ts">
  import { timeFormat as timeFormatStore } from '../stores/settings';

  let { browserHourly }: { browserHourly: { hour: number; visits: number }[] } = $props();

  const maxVisits = $derived(Math.max(...browserHourly.map(entry => entry.visits), 1));
  const totalVisits = $derived(browserHourly.reduce((sum, entry) => sum + entry.visits, 0));
  const peak = $derived([...browserHourly].sort((a, b) => b.visits - a.visits)[0] ?? { hour: 0, visits: 0 });

  function hourLabel(hour: number, compact = false): string {
    if ($timeFormatStore === '24h') return `${String(hour).padStart(2, '0')}${compact ? '' : ':00'}`;
    const suffix = hour >= 12 ? 'pm' : 'am';
    const value = hour % 12 || 12;
    return `${value}${compact ? suffix[0] : ` ${suffix}`}`;
  }
</script>

{#if browserHourly.length > 0}
  <section class="card hourly-card" aria-label="Browser visits by hour">
    <div class="card-header hourly-header">
      <div class="title-wrap">
        <i class="ti ti-chart-bar" aria-hidden="true"></i>
        <div><div class="card-title">Browser visits by hour</div><span>When browsing was most active</span></div>
      </div>
      <div class="hourly-summary">
        <span><strong>{totalVisits.toLocaleString()}</strong> visits</span>
        <span><strong>{hourLabel(peak.hour)}</strong> peak</span>
      </div>
    </div>

    <div class="browser-hourly-body">
      <div class="chart-shell">
        <div class="chart-gridlines" aria-hidden="true"><span></span><span></span><span></span><span></span></div>
        <div class="browser-hourly-chart">
          {#each browserHourly as entry}
            <div class="hour-column">
              <button
                type="button"
                class="bh-bar"
                class:zero={entry.visits === 0}
                class:peak={entry.visits === peak.visits && peak.visits > 0}
                style="height:{entry.visits > 0 ? Math.max(6, entry.visits / maxVisits * 100) : 2}%"
                aria-label={`${hourLabel(entry.hour)}: ${entry.visits} visits`}
              >
                <span>{entry.visits}<small>{hourLabel(entry.hour)}</small></span>
              </button>
            </div>
          {/each}
        </div>
        <div class="bh-labels" aria-hidden="true">
          {#each [0, 6, 12, 18, 23] as hour}
            <span style="grid-column:{hour + 1}">{hourLabel(hour, true)}</span>
          {/each}
        </div>
      </div>
    </div>
  </section>
{/if}

<style>
  .hourly-card { min-height: 100%; display: flex; flex-direction: column; }
  .hourly-header { display: flex; align-items: center; justify-content: space-between; gap: 14px; }
  .title-wrap { display: flex; align-items: center; gap: 9px; }
  .title-wrap > i { color: var(--md-primary); font-size: 16px; }
  .title-wrap > div { display: flex; flex-direction: column; gap: 1px; }
  .title-wrap span { color: var(--clr-text-ter); font-size: 10px; }
  .hourly-summary { display: flex; align-items: center; gap: 6px; }
  .hourly-summary span { display: inline-flex; align-items: baseline; gap: 4px; padding: 5px 8px; border: 1px solid var(--clr-border); border-radius: var(--shape-full); background: var(--clr-bg-ter); color: var(--clr-text-ter); font-size: 9px; }
  .hourly-summary strong { color: var(--clr-text-pri); font: 600 10px var(--font-mono); }

  .browser-hourly-body { min-height: 248px; flex: 1; display: flex; padding: 14px 20px 18px; }
  .chart-shell { min-height: 220px; flex: 1; position: relative; padding-bottom: 28px; }
  .chart-gridlines { position: absolute; inset: 0 0 28px; display: grid; grid-template-rows: repeat(4, 1fr); pointer-events: none; }
  .chart-gridlines span { border-top: 1px dashed color-mix(in srgb, var(--clr-border) 72%, transparent); }
  .chart-gridlines span:last-child { border-bottom: 1px solid var(--clr-border); }
  .browser-hourly-chart { position: absolute; inset: 0 0 28px; display: grid; grid-template-columns: repeat(24, minmax(4px, 1fr)); align-items: stretch; gap: clamp(2px, .35vw, 6px); padding: 0 4px; }
  .hour-column { min-width: 0; height: 100%; display: flex; align-items: flex-end; justify-content: center; }
  .bh-bar { width: min(100%, 22px); min-height: 3px; position: relative; border: 0; border-radius: 5px 5px 2px 2px; background: color-mix(in srgb, var(--md-primary) 62%, var(--clr-bg-ter)); box-shadow: inset 0 1px rgba(255,255,255,.12); cursor: default; transition: opacity var(--duration-fast) var(--ease-out), filter var(--duration-fast) var(--ease-out), transform var(--duration-fast) var(--ease-out); }
  .bh-bar.peak { background: var(--md-primary); box-shadow: 0 0 18px color-mix(in srgb, var(--md-primary) 24%, transparent); }
  .bh-bar.zero { opacity: .12; border-radius: 2px; }
  .bh-bar:hover, .bh-bar:focus-visible { z-index: 2; filter: brightness(1.18); transform: scaleX(1.08); outline: none; }
  .bh-bar > span { display: none; position: absolute; left: 50%; bottom: calc(100% + 8px); transform: translateX(-50%); min-width: 68px; padding: 7px 8px; border: 1px solid var(--clr-border-strong); border-radius: 8px; background: var(--clr-bg-pri); color: var(--clr-text-pri); box-shadow: var(--shadow-md); font: 600 11px/1 var(--font-mono); white-space: nowrap; }
  .bh-bar > span small { display: block; margin-top: 4px; color: var(--clr-text-ter); font: 9px/1.2 var(--font-display); }
  .bh-bar:hover > span, .bh-bar:focus-visible > span { display: block; }
  .bh-labels { position: absolute; inset: auto 0 0; height: 22px; display: grid; grid-template-columns: repeat(24, 1fr); align-items: end; color: var(--clr-text-ter); font: 9px var(--font-mono); }
  .bh-labels span { justify-self: center; }
  .bh-labels span:first-child { justify-self: start; }
  .bh-labels span:last-child { justify-self: end; }

  @media (max-width: 560px) {
    .hourly-summary span:last-child { display: none; }
    .browser-hourly-body { min-height: 210px; padding-inline: 12px; }
    .chart-shell { min-height: 180px; }
  }
</style>
