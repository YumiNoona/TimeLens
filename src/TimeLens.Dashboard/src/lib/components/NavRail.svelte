<script lang="ts">
  import LayoutDashboard from '@lucide/svelte/icons/layout-dashboard';
  import CalendarRange from '@lucide/svelte/icons/calendar-range';
  import ChartPie from '@lucide/svelte/icons/chart-pie';
  import Globe from '@lucide/svelte/icons/globe';
  import ListTree from '@lucide/svelte/icons/list-tree';
  import ShieldCheck from '@lucide/svelte/icons/shield-check';
  import Tags from '@lucide/svelte/icons/tags';
  import Settings2 from '@lucide/svelte/icons/settings-2';
  import Clock3 from '@lucide/svelte/icons/clock-3';
  import Ellipsis from '@lucide/svelte/icons/ellipsis';

  let { active = 'today', onselect }: { active?: string; onselect?: (id: string) => void } = $props();
  let moreDialog: HTMLDialogElement | undefined = $state();
  const mobilePrimary = ['today', 'apps', 'timeline', 'block'];
  function select(id: string) {
    moreDialog?.close();
    onselect?.(id);
  }

  const items = [
    { id: 'today', icon: LayoutDashboard, label: 'Today' },
    { id: 'history', icon: CalendarRange, label: 'History' },
    { id: 'apps', icon: ChartPie, label: 'Apps' },
    { id: 'browser', icon: Globe, label: 'Browser' },
    { id: 'timeline', icon: ListTree, label: 'Timeline' },
  ];

  const bottom = [
    { id: 'block', icon: ShieldCheck, label: 'Block' },
    { id: 'rules', icon: Tags, label: 'Rules' },
    { id: 'settings', icon: Settings2, label: 'Settings' },
  ];
</script>

<nav class="rail" aria-label="Main navigation">
  <div class="rail-logo">
    <Clock3 size={25} strokeWidth={2.2} aria-hidden="true" />
  </div>

  {#each items as item}
    {@const Icon = item.icon}
    <button
      class="rail-item"
      class:mobile-secondary={!mobilePrimary.includes(item.id)}
      class:active={active === item.id}
      onclick={() => select(item.id)}
      aria-current={active === item.id ? 'page' : undefined}
      aria-label={item.label}
      title={item.label}
    >
      <Icon size={22} strokeWidth={1.9} aria-hidden="true" />
      <span>{item.label}</span>
    </button>
  {/each}

  <div class="rail-spacer"></div>

  {#each bottom as item}
    {@const Icon = item.icon}
    <button class="rail-item" class:mobile-secondary={!mobilePrimary.includes(item.id)} class:active={active === item.id} onclick={() => select(item.id)} aria-current={active === item.id ? 'page' : undefined} aria-label={item.label} title={item.label}>
      <Icon size={22} strokeWidth={1.9} aria-hidden="true" />
      <span>{item.label}</span>
    </button>
  {/each}
  <button class="rail-item mobile-more" class:active={!mobilePrimary.includes(active)} onclick={() => moreDialog?.showModal()} aria-label="More views" aria-haspopup="dialog">
    <Ellipsis size={22} aria-hidden="true" /><span>More</span>
  </button>
</nav>

<dialog bind:this={moreDialog} class="more-dialog" aria-labelledby="more-title" onclick={(event) => { if (event.target === moreDialog) moreDialog.close(); }} onkeydown={(event) => { if (event.key === 'Escape') moreDialog?.close(); }}>
  <header><h2 id="more-title">Explore TimeLens</h2><button type="button" onclick={() => moreDialog?.close()} aria-label="Close more views">Close</button></header>
  {#each [...items, ...bottom].filter(item => !mobilePrimary.includes(item.id)) as item}
    {@const Icon = item.icon}
    <button class="more-link" class:active={active === item.id} onclick={() => select(item.id)} aria-current={active === item.id ? 'page' : undefined}>
      <Icon size={22} aria-hidden="true" /><span>{item.label}</span>
    </button>
  {/each}
</dialog>

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

  .rail-logo :global(svg) {
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
    position: relative;
    transition: color 180ms var(--ease-out), transform 180ms var(--ease-out);
    color: var(--clr-text-sec);
    background: none;
    border: none;
    font-family: inherit;
  }

  .rail-item :global(svg) { transition: transform 280ms var(--ease-out), stroke-width 180ms var(--ease-out); }
  .rail-item span { font-size: 10px; font-weight: 500; letter-spacing: 0.03em; }

  .rail-item:hover { color: var(--clr-text-pri); transform: translateY(-1px); }
  .rail-item:hover :global(svg) { transform: scale(1.1) rotate(-4deg); }
  .rail-spacer { flex: 1; }
  .rail-item.active { color: var(--md-primary); }
  .rail-item.active::before {
    content: '';
    position: absolute;
    left: 1px;
    top: 16px;
    bottom: 16px;
    width: 2px;
    border-radius: 99px;
    background: var(--md-primary);
    box-shadow: 0 0 8px color-mix(in srgb, var(--md-primary) 45%, transparent);
  }
  .rail-item.active :global(svg) { transform: scale(1.08); stroke-width: 2.2; }
  .mobile-more { display: none; }
  .more-dialog { margin: auto; width: min(440px, calc(100vw - 24px)); max-height: calc(100dvh - 32px); overflow: auto; padding: 20px; border: 1px solid var(--clr-border-strong); border-radius: 20px; background: var(--clr-bg-sec); color: var(--clr-text-pri); }
  .more-dialog::backdrop { background: rgba(0,0,0,.65); }
  .more-dialog header { display:flex; align-items:center; justify-content:space-between; gap:12px; margin-bottom:12px; }
  .more-dialog h2 { font-size:18px; }
  .more-dialog header button, .more-link { min-height:48px; padding:12px; border:0; border-radius:12px; color:inherit; background:var(--clr-bg-ter); cursor:pointer; }
  .more-link { display:flex; align-items:center; gap:16px; width:100%; margin-top:8px; text-align:left; }
  .more-link.active { color:var(--md-primary); }

  @media (max-width: 760px) {
    .rail {
      position: fixed;
      z-index: 20;
      inset: auto 0 0;
      width: 100%;
      height: calc(76px + env(safe-area-inset-bottom, 0px));
      padding: 4px 8px calc(4px + env(safe-area-inset-bottom, 0px));
      border-right: 0;
      border-top: 1px solid var(--clr-border);
      flex-direction: row;
      align-items: center;
      gap: 2px;
      overflow-x: hidden;
      overflow-y: hidden;
      box-shadow: 0 -8px 24px rgba(0,0,0,0.22);
    }
    .rail-logo, .rail-spacer { display: none; }
    .rail-item { flex:1; width:auto; min-width:48px; height:60px; border-radius:var(--radius-md); }
    .rail-item.mobile-secondary { display:none; }
    .mobile-more { display:flex; }
    .rail-item :global(svg) { width:22px; height:22px; }
    .rail-item.active::before { inset: auto 12px 1px; width: auto; height: 2px; }
    .rail-item span { font-size: 10px; letter-spacing: 0; }
  }
</style>
