<script lang="ts">
  import type { AudioEntry, WebMediaSummary } from '../types';
  import { fmtPrecise } from '../utils';
  import { showSeconds } from '../stores/settings';

  let { audioSessions, webMedia = { playbackSeconds: 0, backgroundSeconds: 0, pictureInPictureSeconds: 0, entries: [] } }:
    { audioSessions: AudioEntry[]; webMedia?: WebMediaSummary } = $props();
</script>

{#if audioSessions.length > 0 || webMedia.entries.length > 0}
  <div class="card">
    <div class="card-header">
      <i class="ti ti-volume-2" aria-hidden="true"></i>
      <div class="card-title">Concurrent media playback</div>
    </div>
    <p class="media-note">Playback is shown separately and is never added to active work time.</p>
    <div class="media-list">
      {#each webMedia.entries as item}
        <div class="media-row">
          <span class="media-name">{item.title || item.domain}<small>{item.domain} · {item.kind}{item.confidence === 'audible-fallback' ? ' · estimated' : ''}</small></span>
          <span class="media-count">{fmtPrecise(item.playbackSeconds, $showSeconds)}{item.pictureInPictureSeconds ? ` · ${fmtPrecise(item.pictureInPictureSeconds, $showSeconds)} PiP` : item.backgroundSeconds ? ` · ${fmtPrecise(item.backgroundSeconds, $showSeconds)} background` : ''}</span>
        </div>
      {/each}
      {#each audioSessions as a}
        <div class="media-row">
          <span class="media-name">{a.exeName}<small>Windows audio session</small></span>
          <span class="media-count">{fmtPrecise(a.playbackSeconds, $showSeconds)} · {a.sessions} session{a.sessions !== 1 ? 's' : ''}</span>
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
  .media-name small{display:block;margin-top:2px;color:var(--clr-text-ter);font-size:10px;font-weight:400}
  .media-note{margin:0 0 8px;color:var(--clr-text-ter);font-size:10px}

  .media-count {
    font-size: var(--text-xs);
    font-family: var(--font-mono);
    color: var(--clr-text-sec);
    font-feature-settings: 'tnum';
    flex-shrink: 0;
  }
</style>
