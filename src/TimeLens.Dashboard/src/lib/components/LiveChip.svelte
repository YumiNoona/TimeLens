<script lang="ts">
  import type { LiveStatus } from '../types';

  let { status }: { status: LiveStatus | null } = $props();
</script>

<div class="live-chip" role="status" aria-live="polite">
  <span class="live-dot" class:idle={status?.isIdle}></span>
  {#if status?.audioActive}
    <i class="ti ti-volume-2 audio-icon" aria-label="Audio playing"></i>
  {/if}
  {#if status}
    <span class="live-app">{status.currentApp}</span>
    <span class="live-sep">·</span>
    <span class="live-idle">
      {status.isIdle ? 'idle ' + status.idleMinutes + 'm' : 'active'}
    </span>
  {:else}
    <span class="live-app">—</span>
  {/if}
</div>

<style>
  .live-chip {
    display: flex;
    align-items: center;
    gap: var(--sp-2);
    background: var(--md-surface-2);
    border: 1px solid var(--md-outline-var);
    border-radius: var(--shape-full);
    padding: var(--sp-2) var(--sp-4);
    font-size: 13px;
    font-weight: 500;
    color: var(--md-on-surf);
  }

  .live-dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: var(--md-primary);
    animation: blink 2s ease-in-out infinite;
  }

  .live-dot.idle {
    background: var(--md-secondary);
  }

  .audio-icon {
    font-size: 14px;
    color: var(--md-tertiary);
  }

  @keyframes blink {
    0%, 100% { opacity: 1 }
    50% { opacity: 0.3 }
  }

  .live-app {
    font-family: var(--font-mono);
    font-size: 12px;
    color: var(--md-primary);
  }

  .live-sep { color: var(--md-on-surf-dim); }

  .live-idle {
    color: var(--md-on-surf-var);
    font-family: var(--font-mono);
    font-size: 12px;
  }
</style>
