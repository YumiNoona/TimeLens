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
    background: var(--clr-bg-sec);
    border-radius: var(--shape-md);
    padding: 16px 18px;
  }

  .stat-label {
    font-size: 11px;
    font-weight: 500;
    color: var(--clr-text-sec);
    margin-bottom: 4px;
  }

  .stat-val {
    font-size: 22px;
    font-weight: 500;
    color: var(--clr-text-pri);
    line-height: 1;
    font-family: var(--font-mono);
    letter-spacing: -0.02em;
  }

  .stat-val.accent { color: var(--md-primary); }
  .stat-val.amber { color: var(--md-secondary); }

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
