<script lang="ts">
  let {
    label,
    value,
    chip,
    chipClass = 'chip-neu',
    accent = false,
    amber = false,
  }: {
    label: string;
    value: string;
    chip?: string;
    chipClass?: string;
    accent?: boolean;
    amber?: boolean;
  } = $props();
</script>

<div class="stat-card">
  <span class="stat-label">{label}</span>
  <span class="stat-val" class:accent class:amber>{value}</span>
  {#if chip}
    <span class="stat-chip {chipClass}">{chip}</span>
  {/if}
</div>

<style>
  .stat-card {
    background: var(--md-surface-2);
    border-radius: var(--shape-lg);
    padding: var(--sp-4) var(--sp-5);
    border: 1px solid var(--md-outline);
    display: flex;
    flex-direction: column;
    gap: var(--sp-2);
  }

  .stat-label {
    font-size: 11px;
    font-weight: 500;
    color: var(--md-on-surf-dim);
    letter-spacing: 0.08em;
    text-transform: uppercase;
  }

  .stat-val {
    font-family: var(--font-mono);
    font-size: 26px;
    font-weight: 600;
    color: var(--md-on-surf);
    line-height: 1;
    letter-spacing: -0.02em;
  }

  .stat-val.accent { color: var(--md-primary); }
  .stat-val.amber { color: var(--md-secondary); }

  .stat-chip {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    font-size: 11px;
    font-weight: 500;
    border-radius: var(--shape-xs);
    padding: 2px 6px;
    width: fit-content;
  }

  .chip-up {
    background: rgba(200,232,106,0.12);
    color: var(--md-primary);
  }

  .chip-down {
    background: rgba(232,162,58,0.12);
    color: var(--md-secondary);
  }

  .chip-neu {
    background: var(--md-surface-3);
    color: var(--md-on-surf-var);
  }
</style>
