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
  <span class="metric-val" class:accent class:amber>{value}</span>
  {#if chip}
    <span class="stat-chip {chipClass}">{chip}</span>
  {/if}
</div>

<style>
  .stat-card {
    background: var(--clr-bg-sec);
    border: 1px solid var(--clr-border);
    border-radius: var(--shape-md);
    padding: 16px 20px;
  }

  .stat-label {
    font-size: 11px;
    font-weight: 500;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--clr-text-sec);
    margin-bottom: 4px;
  }

  .metric-val.accent { color: var(--md-primary); }
  .metric-val.amber { color: var(--md-secondary); }

  .stat-chip {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    margin-top: 5px;
    font-size: 10px;
    padding: 2px 7px;
    border-radius: 999px;
    font-weight: 500;
    width: fit-content;
  }

  .chip-up {
    background: rgba(99,153,34,0.12);
    color: #3B6D11;
  }

  .chip-down {
    background: rgba(226,75,74,0.1);
    color: #A32D2D;
  }

  .chip-neu {
    background: var(--clr-bg-ter);
    color: var(--clr-text-sec);
  }
</style>
