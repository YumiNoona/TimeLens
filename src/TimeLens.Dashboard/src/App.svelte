<script lang="ts">
  import { onMount } from 'svelte';
  import NavRail from './lib/components/NavRail.svelte';
  import LiveChip from './lib/components/LiveChip.svelte';
  import StatCard from './lib/components/StatCard.svelte';
  import Timeline from './lib/components/Timeline.svelte';
  import TopApps from './lib/components/TopApps.svelte';
  import CalendarHeatmap from './lib/components/CalendarHeatmap.svelte';
  import CategoryBreakdown from './lib/components/CategoryBreakdown.svelte';
  import HistoryView from './lib/components/HistoryView.svelte';
  import AppsView from './lib/components/AppsView.svelte';
  import TimelineView from './lib/components/TimelineView.svelte';
  import RulesView from './lib/components/RulesView.svelte';
  import SettingsView from './lib/components/SettingsView.svelte';
  import { data, loading, error, live, refresh } from './lib/stores/activity';

  let view = $state('today');

  const today = new Date();
  const dateStr = today.toLocaleDateString('en-US', {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
  });

  onMount(() => {
    refresh();
  });
</script>

<div class="tl">
  <NavRail active={view} onselect={(id) => view = id} />

  <main class="main">
    {#if $error}
      <div class="error-banner">{$error}</div>
    {/if}

    {#if view === 'today'}
      <div class="topbar">
        <div class="topbar-left">
          {#if $loading}
            <p class="title-small">Loading…</p>
            <h1 class="headline-small">Today's activity</h1>
          {:else if $data}
            <p class="title-small">{dateStr}</p>
            <h1 class="headline-small">Today's activity</h1>
          {/if}
        </div>
        {#if $live}
          <LiveChip status={$live} />
        {/if}
      </div>

      {#if $data}
        <div class="stat-grid" role="region" aria-label="Summary statistics">
          <StatCard
            label="Active time"
            value={$data.summary.activeTime}
            chip="↑ {$data.summary.vsYesterday}m vs yesterday"
            chipClass="chip-up"
          />
          <StatCard
            label="Idle time"
            value={$data.summary.idleTime}
            chip={$data.summary.idleSeconds > 0
              ? Math.round($data.summary.idleSeconds / ($data.summary.idleSeconds + $data.summary.activeSeconds) * 100) + '% of session'
              : ''}
            chipClass="chip-down"
            amber
          />
          <StatCard
            label="Focus score"
            value={String($data.summary.focusScore)}
            chip="↑ focused"
            chipClass="chip-up"
            accent
          />
          <StatCard
            label="Top category"
            value={$data.summary.topCategory}
            chip={$data.summary.topCategoryTime + ' total'}
            chipClass="chip-neu"
          />
        </div>

        <Timeline blocks={$data.timeline} />

        <div class="bottom-grid">
          <TopApps apps={$data.topApps} />
          <CalendarHeatmap entries={$data.heatmap} />
        </div>

        <CategoryBreakdown categories={$data.categories} />
      {/if}

    {:else if view === 'history' && $data}
      <HistoryView data={$data} />
    {:else if view === 'apps' && $data}
      <AppsView data={$data} />
    {:else if view === 'timeline' && $data}
      <TimelineView data={$data} />
    {:else if view === 'rules'}
      <RulesView />
    {:else if view === 'settings'}
      <SettingsView />
    {:else if !$data}
      <div class="placeholder-view">
        <i class="ti ti-loader" aria-hidden="true"></i>
        <p class="title-small" style="margin-top: var(--sp-2)">Loading…</p>
      </div>
    {/if}
  </main>
</div>

<style>
  .tl {
    background: var(--md-surface);
    color: var(--md-on-surf);
    font-family: var(--font-display);
    display: flex;
    height: 100vh;
    overflow: hidden;
    font-size: 14px;
    line-height: 1.5;
  }

  .main {
    flex: 1;
    min-height: 0;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: var(--sp-4);
    padding: var(--sp-6);
  }

  .topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .title-small {
    font-size: 14px;
    font-weight: 500;
    color: var(--md-on-surf-var);
    letter-spacing: 0.01em;
  }

  .headline-small {
    font-size: 24px;
    font-weight: 600;
    color: var(--md-on-surf);
    letter-spacing: -0.01em;
    line-height: 1.2;
  }

  .stat-grid {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: var(--sp-3);
  }

  .bottom-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--sp-4);
  }

  .error-banner {
    background: var(--md-err-cont);
    color: var(--md-error);
    padding: var(--sp-3) var(--sp-4);
    border-radius: var(--shape-sm);
    font-size: 13px;
    border: 1px solid rgba(224, 112, 112, 0.2);
  }

  .placeholder-view {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    flex: 1;
    color: var(--md-on-surf-dim);
  }

  .placeholder-view i {
    font-size: 48px;
  }
</style>
