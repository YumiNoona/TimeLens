<script lang="ts">
  import type { DashboardData, TimelineBlock } from '../types';
  import { colorForCategory } from '../colors';

  let { data, timelineGrouped = false }: { data: DashboardData; timelineGrouped?: boolean } = $props();

  let selectedTypes = $state<string[]>([]);
  let groupedMode = $state(timelineGrouped);
  let expanded = $state<Set<number>>(new Set());

  let types = $derived([...new Set(data.timeline.map(b => b.type))]);

  let filtered = $derived(
    selectedTypes.length === 0
      ? data.timeline
      : data.timeline.filter(b => selectedTypes.includes(b.type))
  );

  type Group = { startHour: number; endHour: number; type: string; count: number; blocks: TimelineBlock[] };

  let groups = $derived.by((): Group[] => {
    if (!groupedMode) return [];
    const result: Group[] = [];
    for (const b of filtered) {
      const last = result[result.length - 1];
      if (last && last.type === b.type && Math.abs(last.endHour - b.startHour) < 0.1) {
        last.endHour = b.endHour;
        last.count++;
        last.blocks.push(b);
      } else {
        result.push({ startHour: b.startHour, endHour: b.endHour, type: b.type, count: 1, blocks: [b] });
      }
    }
    return result;
  });

  function fmtHour(n: number): string {
    const h = Math.floor(n);
    const m = Math.floor((n % 1) * 60);
    return `${h}:${String(Math.min(m, 59)).padStart(2, '0')}`;
  }

  function toggleType(t: string) {
    if (selectedTypes.includes(t)) {
      selectedTypes = selectedTypes.filter(x => x !== t);
    } else {
      selectedTypes = [...selectedTypes, t];
    }
  }

  function toggleGroup(i: number) {
    const next = new Set(expanded);
    if (next.has(i)) next.delete(i); else next.add(i);
    expanded = next;
  }
</script>

<div class="tlv">
  <div class="topbar">
    <h1 class="headline-small">Timeline</h1>
    <button class="mode-btn" onclick={() => groupedMode = !groupedMode}>
      <i class="ti ti-{groupedMode ? 'list' : 'folders'}" aria-hidden="true"></i>
      {groupedMode ? 'Flat' : 'Grouped'}
    </button>
  </div>

  <div class="filter-row">
    {#each types as t}
      <button class="type-chip" class:active={selectedTypes.includes(t)} onclick={() => toggleType(t)}>{t}</button>
    {/each}
    {#if selectedTypes.length > 0}
      <button class="type-chip clear" onclick={() => selectedTypes = []}>Clear</button>
    {/if}
  </div>

  <div class="timeline-blocks" role="list">
    {#if filtered.length === 0}
      <p class="empty">No blocks match the selected types.</p>
    {:else if groupedMode}
      {#each groups as group, i}
        {@const open = expanded.has(i)}
        <div class="tl-group" role="listitem">
          <!-- svelte-ignore a11y_click_events_have_key_events a11y_no_static_element_interactions -->
          <div class="tl-block group-header" onclick={() => toggleGroup(i)}>
            <i class="ti ti-chevron-{open ? 'down' : 'right'} chevron" aria-hidden="true"></i>
            <div class="tl-time">
              <span>{fmtHour(group.startHour)}</span>
              <span class="tl-arrow">→</span>
              <span>{fmtHour(group.endHour)}</span>
            </div>
              <div class="tl-bar-bg">
                <div class="tl-bar" style="width: {((group.endHour - group.startHour) / 24) * 100}%; background: {colorForCategory(group.type)}"></div>
              </div>
            <span class="tl-type">{group.type}</span>
            <span class="tl-count">{group.count}</span>
          </div>
          {#if open}
            {#each group.blocks as block}
              <div class="tl-block sub-row">
                <div class="tl-time">
                  <span>{fmtHour(block.startHour)}</span>
                  <span class="tl-arrow">→</span>
                  <span>{fmtHour(block.endHour)}</span>
                </div>
                <div class="tl-bar-bg">
                  <div class="tl-bar" style="width: {((block.endHour - block.startHour) / 24) * 100}%; background: {colorForCategory(block.type)}"></div>
                </div>
                <span class="tl-type">{block.type}</span>
              </div>
            {/each}
          {/if}
        </div>
      {/each}
    {:else}
      {#each filtered as block}
        <div class="tl-block" role="listitem">
          <div class="tl-time">
            <span>{fmtHour(block.startHour)}</span>
            <span class="tl-arrow">→</span>
            <span>{fmtHour(block.endHour)}</span>
          </div>
          <div class="tl-bar-bg">
            <div class="tl-bar" style="width: {((block.endHour - block.startHour) / 24) * 100}%; background: {colorForCategory(block.type)}"></div>
          </div>
          <span class="tl-type">{block.type}</span>
        </div>
      {/each}
    {/if}
  </div>
</div>

<style>
  .tlv { display: flex; flex-direction: column; gap: var(--sp-4); }
  .topbar { display: flex; align-items: center; justify-content: space-between; }

  .mode-btn {
    display: flex; align-items: center; gap: var(--sp-1);
    padding: var(--sp-1) var(--sp-2);
    border-radius: var(--shape-sm);
    border: 1px solid var(--md-outline);
    background: transparent;
    color: var(--md-on-surf-var);
    font-family: inherit;
    font-size: 12px;
    cursor: pointer;
  }
  .mode-btn:hover { background: var(--md-surface-1); color: var(--md-on-surf); }
  .mode-btn i { font-size: 14px; }

  .filter-row { display: flex; gap: var(--sp-2); flex-wrap: wrap; }
  .type-chip {
    padding: var(--sp-1) var(--sp-2);
    border-radius: var(--shape-sm);
    border: 1px solid var(--md-outline);
    background: transparent;
    color: var(--md-on-surf-var);
    font-family: inherit;
    font-size: 11px; font-weight: 500;
    cursor: pointer; text-transform: capitalize;
  }
  .type-chip.active { background: var(--md-primary-cont); color: var(--md-on-pri-cont); border-color: var(--md-primary); }
  .type-chip.clear { color: var(--md-error); border-color: rgba(224,112,112,0.3); }

  .timeline-blocks { display: flex; flex-direction: column; gap: var(--sp-2); }

  .tl-group {
    display: flex; flex-direction: column;
    border: 1px solid var(--md-outline);
    border-radius: var(--shape-sm);
    overflow: hidden;
  }

  .tl-block {
    display: flex; align-items: center; gap: var(--sp-3);
    padding: var(--sp-2) var(--sp-3);
    background: var(--md-surface-1);
    border-radius: var(--shape-sm);
  }

  .group-header {
    border-radius: 0;
    background: var(--md-surface-2);
    cursor: pointer;
  }

  .chevron {
    font-size: 12px;
    color: var(--md-on-surf-dim);
    flex-shrink: 0;
  }

  .sub-row {
    border-radius: 0;
    border-top: 1px solid var(--md-outline);
    padding-left: 42px;
    border-left: 2px solid var(--md-primary);
    margin-left: 12px;
    background: var(--md-surface-1);
  }

  .tl-time {
    display: flex; align-items: center; gap: var(--sp-1);
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--md-on-surf-var);
    width: 100px; flex-shrink: 0;
  }
  .tl-arrow { color: var(--md-primary); font-size: 10px; }
  .tl-bar-bg { flex: 1; height: 10px; background: var(--md-surface-2); border-radius: 99px; overflow: hidden; }
  .tl-bar { height: 100%; border-radius: 99px; min-width: 4px; }
  .tl-type {
    width: 80px; flex-shrink: 0;
    font-size: 12px; font-weight: 500; text-transform: capitalize;
    color: var(--md-on-surf);
  }
  .tl-count {
    width: 28px; flex-shrink: 0; text-align: center;
    font-family: var(--font-mono);
    font-size: 11px;
    background: var(--md-primary-cont);
    color: var(--md-on-pri-cont);
    border-radius: var(--shape-sm);
    padding: 1px 4px;
  }
  .empty { font-size: 13px; color: var(--md-on-surf-dim); text-align: center; padding: var(--sp-6); }
</style>
