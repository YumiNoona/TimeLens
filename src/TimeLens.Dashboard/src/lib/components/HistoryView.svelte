<script lang="ts">
  import { onMount } from 'svelte';
  import type { BrowserHourEntry, DashboardData } from '../types';
  import { getBrowserHourly, getDashboardData } from '../api';
  import StatCard from './StatCard.svelte';
  import TopApps from './TopApps.svelte';
  import TopSites from './TopSites.svelte';
  import CategoryBreakdown from './CategoryBreakdown.svelte';
  import CalendarHeatmap from './CalendarHeatmap.svelte';
  import BrowserHourlyCard from './BrowserHourlyCard.svelte';
  import MediaCard from './MediaCard.svelte';
  import TimelineView from './TimelineView.svelte';
  import { normalizeTimeline } from '../utils';
  import { timelineMinSegmentSeconds } from '../stores/settings';
  import { reorderable } from '../actions/reorderable';

  let {
    timelineGrouped = true,
    showTitles = false
  }: {
    timelineGrouped?: boolean;
    showTitles?: boolean;
  } = $props();

  function toLocalIso(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  const todayIso = toLocalIso(new Date());
  let selectedDate = $state(todayIso);
  let historyData = $state<DashboardData | null>(null);
  let hourly = $state<BrowserHourEntry[]>([]);
  let isLoading = $state(true);
  let loadError = $state<string | null>(null);
  let requestId = 0;

  const dateLabel = $derived.by(() => {
    const date = new Date(`${selectedDate}T00:00:00`);
    if (selectedDate === todayIso) return 'Today';
    const yesterday = new Date();
    yesterday.setDate(yesterday.getDate() - 1);
    if (selectedDate === toLocalIso(yesterday)) return 'Yesterday';
    return date.toLocaleDateString('en-US', {
      weekday: 'long', month: 'long', day: 'numeric', year: 'numeric'
    });
  });

  const fullHourly = $derived.by(() => {
    const values = new Map(hourly.map(entry => [entry.hour, entry.visits]));
    return Array.from({ length: 24 }, (_, hour) => ({ hour, visits: values.get(hour) ?? 0 }));
  });

  const hasActivity = $derived(
    !!historyData && (
      historyData.summary.activeSeconds > 0 ||
      historyData.summary.idleSeconds > 0 ||
      historyData.browserSites.length > 0 ||
      historyData.audioSessions.length > 0
    )
  );

  const visibleTimeline = $derived(historyData ? normalizeTimeline(historyData.timeline, $timelineMinSegmentSeconds) : []);

  async function loadDate(date: string): Promise<void> {
    const currentRequest = ++requestId;
    isLoading = true;
    loadError = null;

    try {
      const [dashboard, browserHours] = await Promise.all([
        getDashboardData(date),
        getBrowserHourly(date)
      ]);
      if (currentRequest !== requestId) return;
      historyData = dashboard;
      hourly = browserHours;
    } catch (error) {
      if (currentRequest !== requestId) return;
      loadError = error instanceof Error ? error.message : 'Could not load this day';
    } finally {
      if (currentRequest === requestId) isLoading = false;
    }
  }

  function selectDate(date: string): void {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || date > todayIso || date === selectedDate) return;
    selectedDate = date;
    void loadDate(date);
  }

  function moveDay(offset: number): void {
    const date = new Date(`${selectedDate}T12:00:00`);
    date.setDate(date.getDate() + offset);
    selectDate(toLocalIso(date));
  }

  onMount(() => {
    void loadDate(selectedDate);
  });
</script>

<div class="history" aria-busy={isLoading}>
  <header class="history-header">
    <div class="history-heading">
      <p class="eyebrow">Activity history</p>
      <h1>{dateLabel}</h1>
      <p class="history-subtitle">Review your apps, focus, input, browsing, and timeline for any tracked day.</p>
    </div>

    <div class="date-controls" aria-label="History date controls">
      <button class="date-arrow" type="button" onclick={() => moveDay(-1)} aria-label="Previous day" title="Previous day">
        <i class="ti ti-chevron-left" aria-hidden="true"></i>
      </button>
      <label class="date-picker">
        <i class="ti ti-calendar" aria-hidden="true"></i>
        <input
          type="date"
          value={selectedDate}
          max={todayIso}
          aria-label="History date"
          onchange={(event) => selectDate(event.currentTarget.value)}
        />
      </label>
      <button
        class="date-arrow"
        type="button"
        onclick={() => moveDay(1)}
        disabled={selectedDate >= todayIso}
        aria-label="Next day"
        title="Next day"
      >
        <i class="ti ti-chevron-right" aria-hidden="true"></i>
      </button>
      {#if selectedDate !== todayIso}
        <button class="today-button" type="button" onclick={() => selectDate(todayIso)}>Today</button>
      {/if}
    </div>
  </header>

  {#if loadError}
    <div class="history-error" role="alert">
      <span><i class="ti ti-alert-circle" aria-hidden="true"></i> {loadError}</span>
      <button type="button" onclick={() => loadDate(selectedDate)}>Try again</button>
    </div>
  {/if}

  {#if !historyData && isLoading}
    <div class="history-loading">
      <span class="spinner" aria-hidden="true"></span>
      <span>Loading {dateLabel.toLowerCase()}…</span>
    </div>
  {:else if historyData}
    <div class="history-content" class:updating={isLoading}>
      <section class="history-stats" aria-label="Daily summary" use:reorderable={{ key: 'history:stats' }}>
        <StatCard
          label="Active time"
          value={historyData.summary.activeTime}
          variant="hero"
          accent={historyData.summary.activeSeconds > 0}
          icon="ti-clock-hour-4"
          chip={historyData.summary.vsYesterday === null
            ? 'No comparison available'
            : historyData.summary.vsYesterday === 0
              ? '= previous day'
              : `${historyData.summary.vsYesterday > 0 ? '↑' : '↓'} ${Math.abs(historyData.summary.vsYesterday)}m vs previous day`}
          chipClass={historyData.summary.vsYesterday === null || historyData.summary.vsYesterday === 0
            ? 'chip-neu'
            : historyData.summary.vsYesterday > 0 ? 'chip-up' : 'chip-down'}
        />
        <StatCard
          label="Focus score"
          value={String(historyData.summary.focusScore)}
          variant="hero"
          accent={historyData.summary.focusScore >= 40}
          icon="ti-target-arrow"
          chip={historyData.summary.topCategory === '—'
            ? 'No categories yet'
            : `Top: ${historyData.summary.topCategory}`}
          chipClass="chip-neu"
        />
        <StatCard
          label="Keystrokes"
          value={historyData.summary.totalKeystrokes.toLocaleString()}
          variant="hero"
          icon="ti-keyboard"
          chip={`${historyData.summary.totalClicks.toLocaleString()} clicks`}
          chipClass="chip-neu"
        />
        <StatCard
          label="Idle time"
          value={historyData.summary.idleTime}
          variant="hero"
          icon="ti-coffee"
          chip={historyData.summary.idleSeconds > 0 && historyData.summary.activeSeconds + historyData.summary.idleSeconds > 0
            ? `${Math.round(historyData.summary.idleSeconds / (historyData.summary.activeSeconds + historyData.summary.idleSeconds) * 100)}% of session`
            : 'No idle time'}
          chipClass="chip-neu"
        />
      </section>

      <div class="history-overview" use:reorderable={{ key: 'history:overview' }}>
        <CalendarHeatmap
          entries={historyData.heatmap}
          {selectedDate}
          onselect={selectDate}
        />
        <section class="day-details card" aria-label="Selected day details">
          <div class="card-header">
            <i class="ti ti-sparkles" aria-hidden="true"></i>
            <div class="card-title">At a glance</div>
          </div>
          <div class="detail-list">
            <div class="detail-row">
              <span>Most used app</span>
              <strong>{historyData.topApps[0]?.name ?? '—'}</strong>
            </div>
            <div class="detail-row">
              <span>Top category</span>
              <strong class="capitalize">{historyData.summary.topCategory}</strong>
            </div>
            <div class="detail-row">
              <span>Browser visits</span>
              <strong>{historyData.browserSites.reduce((total, site) => total + site.visits, 0).toLocaleString()}</strong>
            </div>
            <div class="detail-row">
              <span>Activity segments</span>
              <strong>{visibleTimeline.length.toLocaleString()}</strong>
            </div>
          </div>
          <div class="activity-split">
            <div class="split-label">
              <span>Session balance</span>
              <span>{historyData.summary.activeSeconds + historyData.summary.idleSeconds > 0
                ? `${Math.round(historyData.summary.activeSeconds / (historyData.summary.activeSeconds + historyData.summary.idleSeconds) * 100)}% active`
                : 'No session time'}</span>
            </div>
            <div class="split-track">
              <div
                class="split-fill"
                style="width: {historyData.summary.activeSeconds + historyData.summary.idleSeconds > 0
                  ? historyData.summary.activeSeconds / (historyData.summary.activeSeconds + historyData.summary.idleSeconds) * 100
                  : 0}%"
              ></div>
            </div>
          </div>
        </section>
      </div>

      {#if !hasActivity}
        <div class="empty-day">
          <i class="ti ti-calendar-off" aria-hidden="true"></i>
          <div>
            <strong>No activity recorded</strong>
            <span>Choose another date from the calendar to continue browsing.</span>
          </div>
        </div>
      {:else}
        <div class="history-grid" use:reorderable={{ key: 'history:apps-categories' }}>
          <TopApps apps={historyData.topApps} />
          <CategoryBreakdown categories={historyData.categories} periodLabel="this day" />
        </div>

        {#if historyData.browserSites.length > 0}
          <div class="history-grid" use:reorderable={{ key: 'history:browser' }}>
            <TopSites sites={historyData.browserSites} emptyLabel="No browsing activity this day." />
            <BrowserHourlyCard browserHourly={fullHourly} />
          </div>
        {/if}

        {#if historyData.audioSessions.length > 0}
          <MediaCard audioSessions={historyData.audioSessions} />
        {/if}

        {#if visibleTimeline.length > 0}
          <section class="timeline-section">
            <div class="section-heading">
              <i class="ti ti-timeline" aria-hidden="true"></i>
              <div>
                <h2>Day timeline</h2>
                <p>{visibleTimeline.length} activity segment{visibleTimeline.length === 1 ? '' : 's'} · switches under 1 minute hidden</p>
              </div>
            </div>
            <TimelineView data={historyData} {timelineGrouped} {showTitles} />
          </section>
        {/if}
      {/if}
    </div>
  {/if}
</div>

<style>
  .history { padding-bottom: var(--space-10); }

  .history-header {
    display: flex;
    align-items: flex-end;
    justify-content: space-between;
    gap: var(--space-6);
    padding: var(--space-5) 0 var(--space-4);
    margin-bottom: var(--space-5);
  }

  .eyebrow {
    color: var(--md-primary);
    font-size: var(--type-page-eyebrow);
    font-weight: var(--weight-semibold);
    letter-spacing: 0.08em;
    text-transform: uppercase;
    margin-bottom: var(--space-1);
  }

  .history-heading h1 {
    color: var(--clr-text-pri);
    font-size: var(--type-page-title);
    font-weight: var(--weight-semibold);
    letter-spacing: -0.03em;
    line-height: 1.15;
  }

  .history-subtitle {
    color: var(--clr-text-sec);
    font-size: var(--type-page-subtitle);
    line-height: 1.45;
    margin-top: var(--space-2);
  }

  .date-controls { display: flex; align-items: center; gap: var(--space-2); flex-shrink: 0; }

  .date-arrow,
  .today-button,
  .date-picker {
    height: 38px;
    border: 1px solid var(--clr-border-strong);
    background: var(--clr-bg-sec);
    color: var(--clr-text-pri);
    border-radius: var(--radius-md);
  }

  .date-arrow {
    width: 38px;
    display: grid;
    place-items: center;
    cursor: pointer;
  }
  .date-arrow:disabled { opacity: 0.35; cursor: not-allowed; }
  .date-arrow:not(:disabled):hover, .today-button:hover { background: var(--clr-bg-ter); border-color: var(--md-primary); }

  .date-picker { display: flex; align-items: center; gap: var(--space-2); padding: 0 var(--space-3); }
  .date-picker i { color: var(--md-primary); }
  .date-picker input {
    border: 0;
    outline: 0;
    background: transparent;
    color: var(--clr-text-pri);
    color-scheme: dark;
    font: 500 var(--text-sm) var(--font-mono);
  }

  .today-button {
    padding: 0 var(--space-4);
    font: var(--weight-semibold) var(--text-xs) var(--font-display);
    cursor: pointer;
  }

  .history-content { display: flex; flex-direction: column; gap: var(--space-5); transition: opacity var(--duration-base); }
  .history-content.updating { opacity: 0.55; pointer-events: none; }

  .history-stats { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: var(--space-4); }
  .history-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: var(--space-4); }
  .history-overview {
    display: grid;
    grid-template-columns: max-content minmax(320px, 1fr);
    gap: var(--space-4);
    align-items: start;
  }

  .day-details { display: flex; flex-direction: column; }
  .detail-list { display: flex; flex-direction: column; }
  .detail-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-4);
    padding: var(--space-3) 0;
    border-bottom: 1px solid var(--clr-border);
    font-size: var(--text-sm);
  }
  .detail-row span { color: var(--clr-text-sec); }
  .detail-row strong {
    color: var(--clr-text-pri);
    font-family: var(--font-mono);
    font-size: var(--text-xs);
    font-weight: var(--weight-medium);
    max-width: 58%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .detail-row strong.capitalize { text-transform: capitalize; }
  .activity-split { margin-top: auto; padding-top: var(--space-5); }
  .split-label { display: flex; justify-content: space-between; color: var(--clr-text-sec); font-size: var(--text-xs); margin-bottom: var(--space-2); }
  .split-track { height: 6px; overflow: hidden; border-radius: var(--radius-full); background: var(--clr-bg-ter); }
  .split-fill { height: 100%; border-radius: inherit; background: var(--md-primary); transition: width var(--duration-base) var(--ease-out); }

  .history-loading {
    min-height: 280px;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: var(--space-3);
    color: var(--clr-text-sec);
  }
  .spinner {
    width: 18px;
    height: 18px;
    border: 2px solid var(--clr-border-strong);
    border-top-color: var(--md-primary);
    border-radius: 50%;
    animation: spin 700ms linear infinite;
  }
  @keyframes spin { to { transform: rotate(360deg); } }

  .history-error,
  .empty-day {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--space-4);
    border-radius: var(--radius-lg);
    padding: var(--space-4) var(--space-5);
    margin-bottom: var(--space-4);
  }
  .history-error { color: var(--md-error); background: var(--md-err-cont); border: 1px solid rgba(224,112,112,0.25); }
  .history-error span { display: flex; align-items: center; gap: var(--space-2); }
  .history-error button { border: 0; background: transparent; color: inherit; font-weight: var(--weight-semibold); cursor: pointer; }

  .empty-day { justify-content: flex-start; color: var(--clr-text-sec); background: var(--clr-bg-sec); border: 1px solid var(--clr-border); }
  .empty-day > i { font-size: 28px; color: var(--clr-text-ter); }
  .empty-day div { display: flex; flex-direction: column; }
  .empty-day strong { color: var(--clr-text-pri); font-size: var(--text-sm); }
  .empty-day span { font-size: var(--text-xs); }

  .timeline-section {
    background: var(--clr-bg-sec);
    border: 1px solid var(--clr-border);
    border-radius: var(--radius-lg);
    padding: var(--space-5);
  }
  .section-heading { display: flex; align-items: center; gap: var(--space-3); margin-bottom: var(--space-4); }
  .section-heading > i { color: var(--md-primary); font-size: var(--text-lg); }
  .section-heading h2 { color: var(--clr-text-pri); font-size: var(--text-md); font-weight: var(--weight-semibold); }
  .section-heading p { color: var(--clr-text-sec); font-size: var(--text-xs); }

  @media (max-width: 1050px) {
    .history-stats { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    .history-overview { grid-template-columns: 1fr; }
  }

  @media (max-width: 760px) {
    .history-header { align-items: stretch; flex-direction: column; padding-top: var(--space-5); }
    .date-controls { width: 100%; }
    .date-picker { flex: 1; }
    .date-picker input { width: 100%; }
    .history-grid { grid-template-columns: 1fr; }
  }

  @media (max-width: 520px) {
    .history-stats { grid-template-columns: 1fr; }
    .today-button { display: none; }
    .timeline-section { padding: var(--space-4); }
  }
</style>
