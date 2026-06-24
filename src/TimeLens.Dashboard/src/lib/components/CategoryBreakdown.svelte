<script lang="ts">
  import type { CategoryEntry } from '../types';
  import { colorForCategory } from '../colors';
  import { fmtTime } from '../utils';

  let { categories }: { categories: CategoryEntry[] } = $props();
</script>

<div class="card">
  <div class="card-title">
    <i class="ti ti-chart-bar" aria-hidden="true"></i>
    Category breakdown
  </div>

  <div role="list">
    {#each categories as cat}
      <div class="cat-row" role="listitem">
        <div class="cat-name">{cat.name}</div>
        <div class="cat-track">
          <div class="cat-fill" style="width: {cat.percentage}%; background: {colorForCategory(cat.name)}"></div>
        </div>
        <div class="cat-pct" style="color: {colorForCategory(cat.name)}">{cat.percentage}%</div>
        <div class="cat-time">{fmtTime(cat.minutes)}</div>
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

  .cat-row {
    display: flex;
    align-items: center;
    gap: var(--sp-3);
    margin-bottom: var(--sp-3);
  }

  .cat-name {
    width: 72px;
    font-size: 12px;
    color: var(--md-on-surf-var);
  }

  .cat-track {
    flex: 1;
    height: 6px;
    background: var(--md-surface);
    border-radius: var(--shape-full);
    overflow: hidden;
  }

  .cat-fill { height: 100%; border-radius: var(--shape-full); }

  .cat-pct {
    width: 36px;
    text-align: right;
    font-size: 11px;
    font-family: var(--font-mono);
    font-weight: 500;
  }

  .cat-time {
    width: 44px;
    text-align: right;
    font-size: 11px;
    color: var(--md-on-surf-dim);
    font-family: var(--font-mono);
  }
</style>
