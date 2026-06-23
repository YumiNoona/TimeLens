<script lang="ts">
  import type { DashboardData } from '../types';
  let { data }: { data: DashboardData } = $props();

  let selectedTypes = $state<string[]>([]);

  let types = $derived([...new Set(data.timeline.map(b => b.type))]);

  let filtered = $derived(
    selectedTypes.length === 0
      ? data.timeline
      : data.timeline.filter(b => selectedTypes.includes(b.type))
  );

  function toggleType(t: string) {
    if (selectedTypes.includes(t)) {
      selectedTypes = selectedTypes.filter(x => x !== t);
    } else {
      selectedTypes = [...selectedTypes, t];
    }
  }
</script>

<div class="tlv">
  <div class="topbar">
    <h1 class="headline-small">Timeline</h1>
  </div>

  <div class="filter-row">
    {#each types as t}
      <button
        class="type-chip"
        class:active={selectedTypes.includes(t)}
        onclick={() => toggleType(t)}
      >
        {t}
      </button>
    {/each}
    {#if selectedTypes.length > 0}
      <button class="type-chip clear" onclick={() => selectedTypes = []}>Clear</button>
    {/if}
  </div>

  <div class="timeline-blocks" role="list">
    {#each filtered as block}
      <div class="tl-block" role="listitem">
        <div class="tl-time">
          <span>{Math.floor(block.startHour)}:{String(Math.round((block.startHour % 1) * 60)).padStart(2, '0')}</span>
          <span class="tl-arrow">→</span>
          <span>{Math.floor(block.endHour)}:{String(Math.round((block.endHour % 1) * 60)).padStart(2, '0')}</span>
        </div>
        <div class="tl-bar-bg">
          <div
            class="tl-bar"
            style="width: {((block.endHour - block.startHour) / 24) * 100}%"
          ></div>
        </div>
        <span class="tl-type">{block.type}</span>
      </div>
    {/each}
    {#if filtered.length === 0}
      <p class="empty">No blocks match the selected types.</p>
    {/if}
  </div>
</div>

<style>
  .tlv { display: flex; flex-direction: column; gap: var(--sp-4); }
  .topbar { display: flex; align-items: center; justify-content: space-between; }
  .filter-row { display: flex; gap: var(--sp-2); flex-wrap: wrap; }
  .type-chip {
    padding: var(--sp-1) var(--sp-2);
    border-radius: var(--shape-sm);
    border: 1px solid var(--md-outline);
    background: transparent;
    color: var(--md-on-surf-var);
    font-family: inherit;
    font-size: 11px;
    font-weight: 500;
    cursor: pointer;
    text-transform: capitalize;
  }
  .type-chip.active { background: var(--md-primary-cont); color: var(--md-on-pri-cont); border-color: var(--md-primary); }
  .type-chip.clear { color: var(--md-error); border-color: rgba(224,112,112,0.3); }
  .timeline-blocks { display: flex; flex-direction: column; gap: var(--sp-2); }
  .tl-block {
    display: flex; align-items: center; gap: var(--sp-3);
    padding: var(--sp-2) var(--sp-3);
    background: var(--md-surface-1);
    border-radius: var(--shape-sm);
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
  .tl-bar { height: 100%; background: var(--md-primary); border-radius: 99px; min-width: 4px; }
  .tl-type {
    width: 80px; flex-shrink: 0;
    font-size: 12px; font-weight: 500; text-transform: capitalize;
    color: var(--md-on-surf);
  }
  .empty { font-size: 13px; color: var(--md-on-surf-dim); text-align: center; padding: var(--sp-6); }
</style>
