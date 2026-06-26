<script lang="ts">
  import type { AudioEntry } from '../types';

  let { audioSessions }: { audioSessions: AudioEntry[] } = $props();
</script>

{#if audioSessions.length > 0}
  <div class="card">
    <div class="card-header">
      <i class="ti ti-volume-2" aria-hidden="true"></i>
      <div class="card-title">Media sessions</div>
    </div>
    <div class="media-list">
      {#each audioSessions as a}
        <div class="media-row">
          <span class="media-name">{a.exeName}</span>
          <span class="media-count">{a.sessions} session{a.sessions !== 1 ? 's' : ''}</span>
        </div>
      {/each}
    </div>
  </div>
{/if}

<style>
  .media-list {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }

  .media-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--space-2) 0;
    border-bottom: 1px solid var(--clr-border);
  }

  .media-row:last-child { border-bottom: none; }

  .media-name {
    font-size: var(--text-sm);
    color: var(--clr-text-pri);
    font-weight: var(--weight-medium);
  }

  .media-count {
    font-size: var(--text-xs);
    font-family: var(--font-mono);
    color: var(--clr-text-sec);
    font-feature-settings: 'tnum';
    flex-shrink: 0;
  }
</style>
