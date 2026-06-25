<script lang="ts">
  import type { AppEntry } from '../types';
  import { colorForApp } from '../colors';
  import { fmtTime } from '../utils';

  let { apps }: { apps: AppEntry[] } = $props();

  const maxMins = $derived(apps.length > 0 ? apps[0].minutes : 1);
</script>

<div class="card">
  <div class="card-title">
    <i class="ti ti-apps" aria-hidden="true"></i>
    Top apps
  </div>

  <div class="app-list">
    {#each apps as app, i}
      <div class="app-row">
        <div class="app-icon" style="background: {colorForApp(i)}">{app.name.charAt(0).toUpperCase()}</div>
        <span class="app-name">{app.name}</span>
        <div class="app-bar">
          <div class="app-fill" style="width: {Math.round(app.minutes / maxMins * 100)}%"></div>
        </div>
        <span class="app-time">{fmtTime(app.minutes)}</span>
      </div>
    {/each}
  </div>
</div>

<style>
  .app-list { display: flex; flex-direction: column; gap: 6px; }

  .app-row {
    display: flex; align-items: center; gap: 8px;
    font-size: 13px;
  }

  .app-icon {
    width: 16px; height: 16px; border-radius: 3px;
    display: flex; align-items: center; justify-content: center;
    font-size: 9px; color: #0D0F0A; font-weight: 700; flex-shrink: 0;
  }

  .app-name {
    width: 100px; flex-shrink: 0;
    color: var(--clr-text-pri);
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }

  .app-bar {
    flex: 1; height: 8px;
    background: var(--clr-bg-ter);
    border-radius: 999px; overflow: hidden;
  }

  .app-fill {
    height: 100%; border-radius: 999px;
    background: var(--md-primary); opacity: 0.6;
  }

  .app-time {
    width: 44px; text-align: right;
    font-size: 11px; font-family: var(--font-mono); color: var(--clr-text-sec);
  }
</style>
