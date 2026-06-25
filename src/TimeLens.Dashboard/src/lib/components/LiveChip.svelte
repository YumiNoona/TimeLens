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
    gap: 6px;
    padding: 5px 10px;
    border-radius: 999px;
    background: rgba(200,232,106,0.12);
    border: 0.5px solid rgba(200,232,106,0.4);
    font-size: 11px;
    font-weight: 500;
    color: #7a9a00;
  }

  .live-dot {
    width: 7px;
    height: 7px;
    border-radius: 50%;
    background: #8ab800;
    animation: pulse 1.8s ease-in-out infinite;
  }

  .live-dot.idle {
    background: var(--md-secondary);
  }

  @keyframes pulse {
    0%, 100% { opacity: 1 }
    50% { opacity: 0.4 }
  }

  .audio-icon {
    font-size: 13px;
    color: var(--md-tertiary);
  }

  .live-app {
    font-family: var(--font-mono);
    font-size: 11px;
    color: #7a9a00;
  }

  .live-sep { color: rgba(122,154,0,0.4); }

  .live-idle {
    color: rgba(122,154,0,0.7);
    font-family: var(--font-mono);
    font-size: 11px;
  }
</style>
