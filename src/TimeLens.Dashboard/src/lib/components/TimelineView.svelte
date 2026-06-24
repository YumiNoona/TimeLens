<script lang="ts">
  import type { DashboardData, TimelineBlock } from '../types';
  import { colorForCategory } from '../colors';

  let { data, timelineGrouped = false }: { data: DashboardData; timelineGrouped?: boolean } = $props();

  let selectedTypes = $state<string[]>([]);
  let groupedMode = $state(timelineGrouped);
  let expanded = $state<Set<string>>(new Set());

  let types = $derived([...new Set(data.timeline.map(b => b.type.toLowerCase()))]);

  let filtered = $derived(
    selectedTypes.length === 0
      ? data.timeline
      : data.timeline.filter(b => selectedTypes.includes(b.type.toLowerCase()))
  );

  type TreeNode = {
    id: string;
    startHour: number;
    endHour: number;
    type: string;
    label: string;
    depth: number;
    children: TreeNode[];
    durationSeconds: number;
    count: number;
  };

  let tree = $derived.by((): TreeNode[] => {
    if (!groupedMode) return [];
    if (filtered.length === 0) return [];

    // Level 0: group by category (merge adjacent same-category runs)
    const cats: { type: string; startHour: number; endHour: number; blocks: TimelineBlock[] }[] = [];
    for (const b of filtered) {
      const last = cats[cats.length - 1];
      if (last && last.type === b.type && Math.abs(last.endHour - b.startHour) < 0.1) {
        last.endHour = b.endHour;
        last.blocks.push(b);
      } else {
        cats.push({ type: b.type, startHour: b.startHour, endHour: b.endHour, blocks: [b] });
      }
    }

    const result: TreeNode[] = [];
    let nodeId = 0;

    for (const cat of cats) {
      // Level 1: group by exeName within category
      const appGroups: { exe: string; startHour: number; endHour: number; blocks: TimelineBlock[] }[] = [];
      for (const b of cat.blocks) {
        const last = appGroups[appGroups.length - 1];
        if (last && last.exe === b.exeName && Math.abs(last.endHour - b.startHour) < 0.1) {
          last.endHour = b.endHour;
          last.blocks.push(b);
        } else {
          appGroups.push({ exe: b.exeName, startHour: b.startHour, endHour: b.endHour, blocks: [b] });
        }
      }

      const catChildren: TreeNode[] = [];
      for (const ag of appGroups) {
        // Level 2: individual blocks (window title changes)
        const blockChildren: TreeNode[] = ag.blocks.map(b => ({
          id: `t${nodeId++}`,
          startHour: b.startHour,
          endHour: b.endHour,
          type: b.type,
          label: b.windowTitle || b.exeName,
          depth: 2,
          children: [],
          durationSeconds: b.durationSeconds,
          count: 0,
        }));

        const dur = ag.blocks.reduce((s, b) => s + b.durationSeconds, 0);
        catChildren.push({
          id: `t${nodeId++}`,
          startHour: ag.startHour,
          endHour: ag.endHour,
          type: cat.type,
          label: ag.exe,
          depth: 1,
          children: blockChildren,
          durationSeconds: dur,
          count: ag.blocks.length,
        });
      }

      const dur = cat.blocks.reduce((s, b) => s + b.durationSeconds, 0);
      result.push({
        id: `t${nodeId++}`,
        startHour: cat.startHour,
        endHour: cat.endHour,
        type: cat.type,
        label: cat.type,
        depth: 0,
        children: catChildren,
        durationSeconds: dur,
        count: cat.blocks.length,
      });
    }
    return result;
  });

  function fmtHour(n: number): string {
    const h = Math.floor(n);
    const m = Math.floor((n % 1) * 60);
    return `${h}:${String(Math.min(m, 59)).padStart(2, '0')}`;
  }

  function fmtDuration(secs: number): string {
    const m = Math.floor(secs / 60);
    if (m < 60) return m + 'm';
    const h = Math.floor(m / 60);
    return h + 'h ' + (m % 60) + 'm';
  }

  function toggleType(t: string) {
    if (selectedTypes.includes(t)) {
      selectedTypes = selectedTypes.filter(x => x !== t);
    } else {
      selectedTypes = [...selectedTypes, t];
    }
  }

  function toggleNode(id: string) {
    const next = new Set(expanded);
    if (next.has(id)) next.delete(id); else next.add(id);
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
      {#each tree as node}
        {#snippet renderNode(n: TreeNode)}
          {@const open = expanded.has(n.id)}
          {@const hasKids = n.children.length > 0}
          <!-- svelte-ignore a11y_click_events_have_key_events a11y_no_static_element_interactions -->
          <div
            class="tl-row"
            class:d0={n.depth === 0}
            class:d1={n.depth === 1}
            class:d2={n.depth === 2}
            class:has-kids={hasKids}
            style="padding-left: {12 + n.depth * 22}px"
            onclick={() => hasKids && toggleNode(n.id)}
            role="listitem"
          >
            {#if hasKids}
              <i class="ti ti-chevron-{open ? 'down' : 'right'} chevron" aria-hidden="true"></i>
            {:else}
              <span class="chevron-spacer"></span>
            {/if}
            <div class="tl-time">
              <span>{fmtHour(n.startHour)}</span>
              <span class="tl-arrow">→</span>
              <span>{fmtHour(n.endHour)}</span>
            </div>
            <div class="tl-bar-bg">
              <div class="tl-bar" style="width: {((n.endHour - n.startHour) / 24) * 100}%; background: {colorForCategory(n.type)}"></div>
            </div>
            <span class="tl-type" class:cat={n.depth === 0} class:exe={n.depth === 1} class:title={n.depth === 2}>
              {n.label}
            </span>
            <span class="tl-dur">{fmtDuration(n.durationSeconds)}</span>
          </div>
          {#if open}
            <div class="tl-children">
              {#each n.children as child}
                {@render renderNode(child)}
              {/each}
            </div>
          {/if}
        {/snippet}
        {@render renderNode(node)}
      {/each}
    {:else}
      {#each filtered as block}
        <div class="tl-flat" role="listitem">
          <span class="chevron-spacer"></span>
          <div class="tl-time">
            <span>{fmtHour(block.startHour)}</span>
            <span class="tl-arrow">→</span>
            <span>{fmtHour(block.endHour)}</span>
          </div>
          <div class="tl-bar-bg">
            <div class="tl-bar" style="width: {((block.endHour - block.startHour) / 24) * 100}%; background: {colorForCategory(block.type)}"></div>
          </div>
          <span class="tl-type">{block.type}</span>
          <span class="tl-dur">{fmtDuration(block.durationSeconds)}</span>
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

  .timeline-blocks { display: flex; flex-direction: column; gap: 1px; }

  .tl-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-2) var(--sp-3);
    cursor: default;
    position: relative;
  }
  .tl-row.has-kids { cursor: pointer; }
  .tl-row.d0 { background: var(--md-surface-2); font-weight: 600; }
  .tl-row.d1 { background: var(--md-surface-1); }
  .tl-row.d2 { background: var(--md-surface-1); opacity: 0.85; }
  .tl-row.d0:hover { background: var(--md-surface-3); }
  .tl-row.d1:hover { background: var(--md-surface-2); }
  .tl-row.d2:hover { background: var(--md-surface-2); }

  .tl-children {
    border-left: 2px solid var(--md-primary);
    margin-left: 24px;
  }

  .chevron {
    font-size: 12px;
    color: var(--md-on-surf-dim);
    flex-shrink: 0;
    width: 14px;
  }
  .chevron-spacer { width: 14px; flex-shrink: 0; }

  .tl-flat {
    display: flex; align-items: center; gap: var(--sp-2);
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
  .tl-bar { height: 100%; border-radius: 99px; min-width: 4px; }
  .tl-type {
    width: 100px; flex-shrink: 0;
    font-size: 12px; text-transform: capitalize;
    color: var(--md-on-surf);
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .tl-type.cat { font-weight: 600; color: var(--md-on-surf); }
  .tl-type.exe { font-family: var(--font-mono); font-size: 11px; color: var(--md-on-surf-var); }
  .tl-type.title { font-family: var(--font-mono); font-size: 11px; color: var(--md-on-surf-dim); font-style: italic; }
  .tl-dur {
    width: 48px; flex-shrink: 0; text-align: right;
    font-family: var(--font-mono);
    font-size: 11px;
    color: var(--md-on-surf-dim);
  }
  .empty { font-size: 13px; color: var(--md-on-surf-dim); text-align: center; padding: var(--sp-6); }
</style>
