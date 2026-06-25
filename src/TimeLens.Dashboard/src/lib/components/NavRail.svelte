<script lang="ts">
  let { active = 'today', onselect }: { active?: string; onselect?: (id: string) => void } = $props();

  const items = [
    { id: 'today', icon: 'ti-layout-dashboard', label: 'Today' },
    { id: 'apps', icon: 'ti-chart-donut', label: 'Apps' },
    { id: 'browser', icon: 'ti-world', label: 'Browser' },
    { id: 'timeline', icon: 'ti-timeline', label: 'Timeline' },
  ];

  const bottom = [
    { id: 'block', icon: 'ti-shield', label: 'Block' },
    { id: 'rules', icon: 'ti-tag', label: 'Rules' },
    { id: 'settings', icon: 'ti-settings-2', label: 'Settings' },
  ];
</script>

<nav class="rail" aria-label="Main navigation">
  <div class="rail-logo">
    <i class="ti ti-clock-hour-4" aria-hidden="true"></i>
  </div>

  {#each items as item}
    <button
      class="rail-item"
      class:active={active === item.id}
      onclick={() => onselect?.(item.id)}
      aria-current={active === item.id ? 'page' : undefined}
    >
      <i class="ti {item.icon}" aria-hidden="true"></i>
      <span>{item.label}</span>
    </button>
  {/each}

  <div style="flex:1"></div>

  {#each bottom as item}
    <button class="rail-item" class:active={active === item.id} onclick={() => onselect?.(item.id)}>
      <i class="ti {item.icon}" aria-hidden="true"></i>
      <span>{item.label}</span>
    </button>
  {/each}
</nav>

<style>
  .rail {
    width: 76px;
    background: var(--clr-bg-sec);
    border-right: 0.5px solid var(--clr-border);
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: 16px 0;
    gap: 4px;
    flex-shrink: 0;
    overflow-y: auto;
  }

  .rail-logo {
    width: 48px;
    height: 48px;
    border-radius: 12px;
    background: var(--md-primary);
    display: flex;
    align-items: center;
    justify-content: center;
    margin-bottom: 16px;
  }

  .rail-logo i {
    color: #1a2400;
    font-size: 26px;
  }

  .rail-item {
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 4px;
    width: 64px;
    height: 60px;
    border-radius: 12px;
    cursor: pointer;
    transition: background 0.12s;
    color: var(--clr-text-sec);
    background: none;
    border: none;
    font-family: inherit;
  }

  .rail-item i { font-size: 24px; }
  .rail-item span { font-size: 10px; font-weight: 500; letter-spacing: 0.03em; }

  .rail-item:hover { background: var(--clr-bg-ter); }
  .rail-item.active { background: rgba(200,232,106,0.15); }
  .rail-item.active i,
  .rail-item.active span { color: #7a9a00; }
</style>
