<script lang="ts">
  import type { AppEntry } from '../types';
  import { colorForApp } from '../colors';

  let { apps }: { apps: AppEntry[] } = $props();

  const maxMins = $derived(apps.length > 0 ? apps[0].minutes : 1);

  function fmtTime(mins: number): string {
    const h = Math.floor(mins / 60);
    const m = mins % 60;
    return (h > 0 ? h + 'h ' : '') + m + 'm';
  }
</script>

<div class="card">
  <div class="card-title">
    <i class="ti ti-apps" aria-hidden="true"></i>
    Top apps
  </div>

  <div role="list">
    {#each apps as app, i}
      <div class="bar-row" role="listitem">
        <div class="bar-app">{app.name}</div>
        <div class="bar-track">
          <div
            class="bar-fill"
            style="width: {Math.round(app.minutes / maxMins * 100)}%; background: {colorForApp(i)}"
          ></div>
        </div>
        <div class="bar-time">{fmtTime(app.minutes)}</div>
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

  .bar-row {
    display: flex;
    align-items: center;
    gap: var(--sp-3);
    margin-bottom: var(--sp-3);
  }

  .bar-app {
    width: 88px;
    font-size: 11px;
    font-family: var(--font-mono);
    color: var(--md-on-surf-var);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .bar-track {
    flex: 1;
    height: 16px;
    background: var(--md-surface);
    border-radius: var(--shape-full);
    overflow: hidden;
  }

  .bar-fill {
    height: 100%;
    border-radius: var(--shape-full);
  }

  .bar-time {
    width: 40px;
    text-align: right;
    font-size: 11px;
    font-family: var(--font-mono);
    color: var(--md-on-surf-dim);
  }
</style>
