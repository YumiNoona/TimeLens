<script lang="ts">
  let {
    label,
    value,
    chip = '',
    chipClass = '',
    accent = false,
    icon = '',
    variant = 'default',
  }: {
    label: string;
    value: string;
    chip?: string;
    chipClass?: string;
    accent?: boolean;
    icon?: string;
    variant?: 'default' | 'hero';
  } = $props();

  const isHero = $derived(variant === 'hero');
</script>

<div class="stat-card" class:hero={isHero} class:accented={accent}>
  {#if icon}
    <i class="ti {icon} stat-icon" aria-hidden="true"></i>
  {/if}
  <div class="stat-value" class:accent>{value}</div>
  <div class="stat-label">{label}</div>
  {#if chip}
    <span class="stat-chip {chipClass}">{chip}</span>
  {/if}
</div>

<style>
  .stat-card {
    background: var(--clr-bg-sec);
    border: 1px solid var(--clr-border);
    border-radius: var(--radius-lg);
    padding: var(--space-4) var(--space-5);
    display: flex;
    flex-direction: column;
    gap: 2px;
    transition: border-color var(--duration-fast) var(--ease-out);
    position: relative;
    overflow: hidden;
  }

  .stat-card::before {
    content: '';
    position: absolute;
    inset: 0;
    border-radius: inherit;
    opacity: 0;
    background: radial-gradient(ellipse at top left, rgba(200,232,106,0.04), transparent 70%);
    transition: opacity var(--duration-slow) var(--ease-out);
    pointer-events: none;
  }

  .stat-card:hover { border-color: var(--clr-border-strong); }
  .stat-card:hover::before { opacity: 1; }

  .stat-card.hero {
    padding: var(--space-4) var(--space-5);
  }

  .stat-icon {
    font-size: var(--text-md);
    color: var(--clr-text-ter);
    margin-bottom: var(--space-2);
  }

  .stat-value {
    font-size: var(--text-xl);
    font-weight: var(--weight-bold);
    color: var(--clr-text-pri);
    line-height: 1.1;
    letter-spacing: -0.03em;
    font-family: var(--font-mono);
    font-feature-settings: 'tnum';
  }

  .hero .stat-value {
    font-size: var(--text-2xl);
  }

  .stat-value.accent {
    color: var(--md-primary);
  }

  .stat-label {
    font-size: var(--text-2xs);
    font-weight: var(--weight-semibold);
    text-transform: uppercase;
    letter-spacing: 0.08em;
    color: var(--clr-text-sec);
  }

  .stat-chip {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    margin-top: var(--space-2);
    font-size: var(--text-2xs);
    font-weight: var(--weight-medium);
    padding: 3px 8px;
    border-radius: var(--radius-full);
    width: fit-content;
    font-family: var(--font-mono);
  }

  .chip-up {
    background: rgba(200, 232, 106, 0.12);
    color: var(--md-primary);
  }

  .chip-down {
    background: rgba(224, 112, 112, 0.12);
    color: var(--md-error);
  }

  .chip-neu {
    background: var(--clr-bg-ter);
    color: var(--clr-text-sec);
  }
</style>
