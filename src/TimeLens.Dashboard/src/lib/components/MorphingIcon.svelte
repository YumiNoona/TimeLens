<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import { canonicalD, createMorph, type Morph } from 'morphicons/dom';
  import type { IconInput } from 'morphicons';

  let {
    icon,
    size = 16,
    strokeWidth = 2,
    class: className = ''
  }: {
    icon: IconInput;
    size?: number;
    strokeWidth?: number;
    class?: string;
  } = $props();

  const initialIcon = untrack(() => icon);
  const initialPath = canonicalD(initialIcon);
  let pathElement: SVGPathElement;
  let driver: Morph | undefined;
  let renderedIcon = initialIcon;

  onMount(() => {
    driver = createMorph(pathElement, initialIcon, { reducedMotion: 'user' });
    renderedIcon = initialIcon;
    return () => driver?.destroy();
  });

  $effect(() => {
    const nextIcon = icon;
    if (driver && nextIcon !== renderedIcon) {
      driver.morphTo(nextIcon, 'snappy');
      renderedIcon = nextIcon;
    }
  });
</script>

<svg
  class={className}
  width={size}
  height={size}
  viewBox="0 0 24 24"
  fill="none"
  stroke="currentColor"
  stroke-width={strokeWidth}
  stroke-linecap="round"
  stroke-linejoin="round"
  aria-hidden="true"
>
  <path bind:this={pathElement} d={initialPath}></path>
</svg>
