<script lang="ts">
  import type { DashboardData, TimelineBlock } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtHourFull, fmtDuration } from '../utils';
  import { timeFormat } from '../stores/settings';

  let { data, timelineGrouped = false }: { data: DashboardData; timelineGrouped?: boolean } = $props();

  let selectedTypes = $state<string[]>([]);
  let groupedMode = $state(timelineGrouped ?? true);
  let expanded = $state<Set<string>>(new Set());

  let types = $derived([...new Set(data.timeline.map(b => b.type.toLowerCase()))]);

  let filtered = $derived(
    selectedTypes.length === 0
      ? data.timeline
      : data.timeline.filter(b => selectedTypes.includes(b.type.toLowerCase()))
  );

  let maxSpan = $derived(Math.max(...filtered.map(b => b.endHour - b.startHour), 0.01));

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

    // Level 0: group ALL blocks of same type across the entire day
    const catMap = new Map<string, { type: string; startHour: number; endHour: number; blocks: TimelineBlock[] }>();
    for (const b of filtered) {
      const key = b.type.toLowerCase();
      if (!catMap.has(key)) {
        catMap.set(key, { type: b.type, startHour: b.startHour, endHour: b.endHour, blocks: [] });
      }
      const cat = catMap.get(key)!;
      cat.startHour = Math.min(cat.startHour, b.startHour);
      cat.endHour = Math.max(cat.endHour, b.endHour);
      cat.blocks.push(b);
    }
    const cats = [...catMap.values()].sort((a, b) => a.startHour - b.startHour);

    const result: TreeNode[] = [];
    let nodeId = 0;

    for (const cat of cats) {
      // Level 1: group ALL blocks for the same exe into one parent (not split by time)
      const appMap = new Map<string, { exe: string; startHour: number; endHour: number; blocks: TimelineBlock[] }>();
      for (const b of cat.blocks) {
        if (b.durationSeconds < 5) continue; // skip sub-5s noise inside groups
        if (!appMap.has(b.exeName)) {
          appMap.set(b.exeName, { exe: b.exeName, startHour: b.startHour, endHour: b.endHour, blocks: [] });
        }
        const ag = appMap.get(b.exeName)!;
        ag.startHour = Math.min(ag.startHour, b.startHour);
        ag.endHour = Math.max(ag.endHour, b.endHour);
        ag.blocks.push(b);
      }
      const appGroups = [...appMap.values()].sort((a, b) => a.startHour - b.startHour);

      const catChildren: TreeNode[] = [];
      for (const ag of appGroups) {
        const blockChildren: TreeNode[] = ag.blocks.map(b => ({
          id: `t${nodeId++}`,
          startHour: b.startHour,
          endHour: b.endHour,
          type: b.type,
          label: b.project ? `${b.windowTitle || b.exeName} · ${b.project}` : (b.windowTitle || b.exeName),
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

  function fmtHour(n: number): string { return fmtHourFull(n, $timeFormat); }

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
  <div class="tl-toolbar">
    <button class="mode-btn chip-button" onclick={() => groupedMode = !groupedMode}>
      <i class="ti ti-{groupedMode ? 'list' : 'folders'}" aria-hidden="true"></i>
      {groupedMode ? 'Flat' : 'Grouped'}
    </button>
  </div>
  <div class="filter-row">
    {#each types as t}
      <button class="type-chip chip-button" class:active={selectedTypes.includes(t)} onclick={() => toggleType(t)}>
        <span class="chip-dot" style="background: {colorForCategory(t)}"></span>
        {t}
      </button>
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
            style="padding-left: {12 + n.depth * 16}px"
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
              <div class="tl-bar" style="width: {((n.endHour - n.startHour) / maxSpan) * 100}%; background: {colorForCategory(n.type)}"></div>
            </div>
            <span class="tl-type" class:cat={n.depth === 0} class:exe={n.depth === 1} class:title={n.depth === 2}>
              {n.label}
            </span>
            <span class="tl-dur">{fmtDuration(n.durationSeconds)}</span>
          </div>
          {#if open}
            <div class="tl-children" style="border-color: {colorForCategory(n.type)}">
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
            <div class="tl-bar" style="width: {((block.endHour - block.startHour) / maxSpan) * 100}%; background: {colorForCategory(block.type)}"></div>
          </div>
          <span class="tl-type">{block.type}{#if block.project} · {block.project}{/if}</span>
          <span class="tl-dur">{fmtDuration(block.durationSeconds)}</span>
        </div>
      {/each}
    {/if}
  </div>
</div>

<style>
  .tlv { display: flex; flex-direction: column; gap: var(--sp-4); }
  .tl-toolbar { display: flex; align-items: center; justify-content: flex-start; }
  .mode-btn i { font-size: 14px; }
  .filter-row { display: flex; gap: var(--sp-2); flex-wrap: wrap; }
  .type-chip {
    font-size: 11px; font-weight: 500;
    text-transform: capitalize;
  }
  .chip-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; }
  .type-chip.clear { color: var(--md-error); border-color: rgba(224,112,112,0.3); }

  .timeline-blocks { display: flex; flex-direction: column; gap: 2px; }

  .tl-row {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-2) var(--sp-3);
    margin: 1px 0;
    cursor: default;
    position: relative;
  }
  .tl-row.has-kids { cursor: pointer; }
  .tl-row.d0 { background: var(--clr-bg-ter); font-weight: 600; }
  .tl-row.d1 { background: var(--clr-bg-sec); }
  .tl-row.d2 { background: var(--clr-bg-sec); opacity: 0.85; }
  .tl-row.d0:hover { background: var(--clr-bg-ter); }
  .tl-row.d1:hover { background: var(--clr-bg-ter); }
  .tl-row.d2:hover { background: var(--clr-bg-ter); }

  .tl-children {
    border-left: 2px solid;
    margin-left: 24px;
  }

  .chevron {
    font-size: 12px;
    color: var(--clr-text-ter);
    flex-shrink: 0;
    width: 14px;
  }
  .chevron-spacer { width: 14px; flex-shrink: 0; }

  .tl-flat {
    display: flex; align-items: center; gap: var(--sp-2);
    padding: var(--sp-2) var(--sp-3);
    margin: 1px 0;
    background: var(--clr-bg-sec);
    border-radius: var(--shape-sm);
    overflow: hidden;
  }

  .tl-time {
    display: flex; align-items: center; gap: var(--sp-1);
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--clr-text-sec);
    width: 110px; flex-shrink: 0;
  }
  .tl-arrow { color: var(--md-primary); font-size: 10px; }
  .tl-bar-bg { flex: 1; min-width: 40px; height: 10px; background: var(--clr-bg-ter); border-radius: 99px; overflow: hidden; }
  .tl-bar { height: 100%; border-radius: 99px; min-width: 4px; max-width: 100%; }
  .tl-type {
    flex: 1; min-width: 0;
    font-size: 12px; text-transform: capitalize;
    color: var(--clr-text-pri);
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .tl-type.cat { font-weight: 600; color: var(--clr-text-pri); }
  .tl-type.exe { font-family: var(--font-mono); font-size: 11px; color: var(--clr-text-sec); }
  .tl-type.title { font-family: var(--font-mono); font-size: 11px; color: var(--clr-text-ter); font-style: italic; }
  .tl-dur {
    width: 48px; flex-shrink: 0; text-align: right;
    font-family: var(--font-mono);
    font-size: 11px;
    color: var(--clr-text-ter);
    margin-left: var(--sp-4);
  }
  .empty { font-size: 13px; color: var(--clr-text-ter); text-align: center; padding: var(--sp-6); }
</style>
