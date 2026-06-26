<script lang="ts">
  import type { AppEntry } from '../types';
  import { colorForApp } from '../colors';
  import { fmtTime } from '../utils';
  import { appIcon } from '../appIcons';

  let { apps }: { apps: AppEntry[] } = $props();

  const maxMins = $derived(apps.length > 0 ? apps[0].minutes : 1);

  function isPassive(app: AppEntry): boolean {
    return app.keystrokes === 0 && app.clicks === 0;
  }
</script>

<div class="card">
  <div class="card-header">
    <i class="ti ti-apps" aria-hidden="true"></i>
    <div class="card-title">Top apps</div>
  </div>

  <div class="app-list">
    {#each apps as app, i}
      {@const icon = appIcon(app.name)}
      {@const passive = isPassive(app)}
      <div class="app-row" class:app-passive={passive}>
        {#if icon}
          <i class="ti {icon} app-icon-tabler" aria-hidden="true"></i>
        {:else}
          <span class="app-icon" style="background: {colorForApp(i)}">{app.name.charAt(0).toUpperCase()}</span>
        {/if}
        <span class="app-name" title={app.name}>{app.name}</span>
        <div class="app-bar-track">
          <div class="app-bar-fill" class:bar-passive={passive} style="width: {Math.round(app.minutes / maxMins * 100)}%"></div>
        </div>
        <span class="app-time">
          {fmtTime(app.minutes)}
          {#if passive}
            <span class="app-passive-badge">no input</span>
          {/if}
        </span>
      </div>
    {/each}
  </div>
</div>

<style>
  .app-list {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
  }

  .app-row {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-1) 0;
  }

  .app-icon {
    width: 22px;
    height: 22px;
    border-radius: var(--radius-sm);
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 10px;
    color: #0D0F0A;
    font-weight: var(--weight-bold);
    flex-shrink: 0;
  }

  .app-icon-tabler {
    font-size: var(--text-lg);
    color: var(--clr-text-sec);
    flex-shrink: 0;
    width: 22px;
    text-align: center;
  }

  .app-name {
    width: 260px;
    flex-shrink: 0;
    font-size: var(--text-sm);
    color: var(--clr-text-pri);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    font-weight: var(--weight-medium);
  }

  .app-bar-track {
    flex: 1;
    height: 6px;
    background: var(--clr-bg-ter);
    border-radius: var(--radius-full);
    overflow: hidden;
  }

  .app-bar-fill {
    height: 100%;
    border-radius: var(--radius-full);
    background: var(--md-primary);
    opacity: 0.55;
    min-width: 2px;
    transition: width var(--duration-slow) var(--ease-out);
  }

  .app-time {
    width: 50px;
    text-align: right;
    font-size: var(--text-xs);
    font-family: var(--font-mono);
    color: var(--clr-text-sec);
    font-feature-settings: 'tnum';
    font-weight: var(--weight-medium);
  }
  .app-row.app-passive .app-name { color: var(--clr-text-ter); }
  .app-bar-fill.bar-passive { opacity: 0.25; }
  .app-passive-badge {
    display: block;
    font-size: 9px;
    color: var(--clr-text-ter);
    text-transform: uppercase;
    letter-spacing: 0.04em;
    font-weight: var(--weight-normal);
  }

  .app-passive {
    opacity: 0.55;
  }
</style>
