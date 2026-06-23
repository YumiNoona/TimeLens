<script lang="ts">
  let { active = 'today', onselect }: { active?: string; onselect?: (id: string) => void } = $props();

  const items = [
    { id: 'today', icon: 'ti-layout-dashboard', label: 'Today' },
    { id: 'history', icon: 'ti-calendar-week', label: 'History' },
    { id: 'apps', icon: 'ti-chart-donut', label: 'Apps' },
    { id: 'timeline', icon: 'ti-timeline', label: 'Timeline' },
  ];

  const bottom = [
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
    width: 80px;
    background: var(--md-surface-1);
    border-right: 1px solid var(--md-outline);
    display: flex;
    flex-direction: column;
    align-items: center;
    padding: var(--sp-3) 0;
    gap: var(--sp-1);
    flex-shrink: 0;
  }

  .rail-logo {
    width: 40px;
    height: 40px;
    background: var(--md-primary-cont);
    border-radius: var(--shape-md);
    display: flex;
    align-items: center;
    justify-content: center;
    margin-bottom: var(--sp-4);
  }

  .rail-logo i {
    color: var(--md-primary);
    font-size: 20px;
  }

  .rail-item {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: var(--sp-1);
    padding: var(--sp-2) 0;
    width: 72px;
    border-radius: var(--shape-lg);
    cursor: pointer;
    transition: background 0.15s;
    color: var(--md-on-surf-var);
    background: none;
    border: none;
    font-family: inherit;
  }

  .rail-item i { font-size: 22px; }
  .rail-item span { font-size: 11px; font-weight: 500; letter-spacing: 0.03em; }

  .rail-item:hover {
    background: rgba(200,232,106,0.06);
    color: var(--md-on-surf);
  }

  .rail-item.active {
    background: var(--md-primary-cont);
    color: var(--md-on-pri-cont);
  }
</style>
