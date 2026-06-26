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
    gap: 7px;
    padding: 6px 14px;
    border-radius: var(--radius-full);
    background: rgba(200,232,106,0.1);
    border: 1px solid rgba(200,232,106,0.2);
    font-size: var(--text-sm);
    font-weight: var(--weight-medium);
    color: var(--md-primary);
    backdrop-filter: blur(8px);
    transition: border-color var(--duration-fast) var(--ease-out);
  }

  .live-chip:hover {
    border-color: rgba(200,232,106,0.35);
  }

  .live-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--md-primary);
    box-shadow: 0 0 8px rgba(200,232,106,0.4);
    animation: pulse 2s ease-in-out infinite;
  }

  .live-dot.idle {
    background: var(--md-secondary);
    box-shadow: 0 0 8px rgba(232,162,58,0.4);
  }

  @keyframes pulse {
    0%, 100% { opacity: 1; transform: scale(1); }
    50% { opacity: 0.5; transform: scale(0.85); }
  }

  .audio-icon {
    font-size: var(--text-base);
    color: var(--md-tertiary);
  }

  .live-app {
    font-family: var(--font-mono);
    font-size: var(--text-sm);
    max-width: 160px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .live-sep {
    opacity: 0.3;
  }

  .live-idle {
    opacity: 0.7;
    font-family: var(--font-mono);
    font-size: var(--text-sm);
  }
</style>
