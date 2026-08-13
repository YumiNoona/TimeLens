import Sortable from 'sortablejs';
import type { Action } from 'svelte/action';

const STORAGE_PREFIX = 'timelens.card-layout.v1.';

export type ReorderableOptions = {
  key: string;
  draggable?: string;
};

function slug(value: string): string {
  return value.trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 48);
}

function directItems(node: HTMLElement, selector: string): HTMLElement[] {
  return Array.from(node.querySelectorAll<HTMLElement>(selector)).filter(item => item.parentElement === node);
}

function ensureIds(items: HTMLElement[]): void {
  const used = new Set<string>();
  items.forEach((item, index) => {
    const label = item.dataset.layoutId
      || item.querySelector<HTMLElement>('.card-title, .stat-label, .section-heading strong, .card-header h2, .card-header')?.innerText
      || `card-${index + 1}`;
    const base = slug(label) || `card-${index + 1}`;
    let id = base;
    let suffix = 2;
    while (used.has(id)) id = `${base}-${suffix++}`;
    used.add(id);
    item.dataset.layoutId = id;
  });
}

function savedOrder(key: string): string[] {
  try {
    const value = JSON.parse(localStorage.getItem(STORAGE_PREFIX + key) || '[]');
    return Array.isArray(value) ? value.filter(item => typeof item === 'string') : [];
  } catch {
    return [];
  }
}

function arrange(node: HTMLElement, items: HTMLElement[], order: string[]): void {
  if (!order.length) return;
  const positions = new Map(order.map((id, index) => [id, index]));
  const original = new Map(items.map((item, index) => [item, index]));
  items
    .slice()
    .sort((a, b) => (positions.get(a.dataset.layoutId || '') ?? 10_000 + (original.get(a) ?? 0))
      - (positions.get(b.dataset.layoutId || '') ?? 10_000 + (original.get(b) ?? 0)))
    .forEach(item => node.appendChild(item));
}

function persist(node: HTMLElement, selector: string, key: string): void {
  const order = directItems(node, selector).map(item => item.dataset.layoutId || '').filter(Boolean);
  localStorage.setItem(STORAGE_PREFIX + key, JSON.stringify(order));
}

function addHandle(item: HTMLElement, key: string): void {
  if (item.querySelector(':scope > .card-drag-handle')) return;
  item.classList.add('layout-card');

  const handle = document.createElement('button');
  handle.type = 'button';
  handle.className = 'card-drag-handle';
  handle.innerHTML = '<i class="ti ti-grip-vertical" aria-hidden="true"></i>';
  const name = item.querySelector<HTMLElement>('.card-title, .stat-label, .section-heading strong, .card-header h2, .card-header')?.innerText?.trim() || 'card';
  handle.setAttribute('aria-label', `Move ${name}`);
  handle.title = `Drag to move ${name}. Use Alt + arrow keys to reorder.`;

  handle.addEventListener('click', event => event.preventDefault());
  handle.addEventListener('keydown', event => {
    if (!event.altKey || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    event.stopPropagation();
    const container = item.parentElement as HTMLElement | null;
    if (!container) return;
    const items = directItems(container, ':scope > .layout-card');
    const index = items.indexOf(item);
    const backwards = event.key === 'ArrowLeft' || event.key === 'ArrowUp';
    const nextIndex = Math.max(0, Math.min(items.length - 1, index + (backwards ? -1 : 1)));
    if (index === nextIndex) return;
    if (backwards) container.insertBefore(item, items[nextIndex]);
    else container.insertBefore(item, items[nextIndex].nextSibling);
    persist(container, ':scope > .layout-card', key);
    handle.focus();
  });

  item.appendChild(handle);
}

export const reorderable: Action<HTMLElement, ReorderableOptions> = (node, initialOptions) => {
  let options = initialOptions;
  let selector = options.draggable || ':scope > *';

  let items = directItems(node, selector);
  ensureIds(items);
  const defaultOrder = items.map(item => item.dataset.layoutId || '');
  arrange(node, items, savedOrder(options.key));
  items = directItems(node, selector);
  items.forEach(item => addHandle(item, options.key));
  node.classList.add('reorderable-grid');

  const sortable = Sortable.create(node, {
    // Sortable evaluates `draggable` from the item itself, where a `:scope >`
    // selector does not match. The action already marks every direct item, so
    // use that stable class for pointer dragging and keep `selector` for order.
    draggable: '.layout-card',
    handle: '.card-drag-handle',
    direction: () => {
      const columns = getComputedStyle(node).gridTemplateColumns.trim().split(/\s+/).filter(Boolean);
      return columns.length > 1 ? 'horizontal' : 'vertical';
    },
    animation: document.documentElement.classList.contains('motion-off') ? 0 : 180,
    fallbackOnBody: true,
    fallbackTolerance: 3,
    swapThreshold: 0.55,
    invertSwap: true,
    // Pointer fallback avoids browser-specific HTML5 drag/data-transfer quirks
    // and behaves consistently in Chrome, Edge, Firefox, and touch devices.
    forceFallback: true,
    delay: 80,
    delayOnTouchOnly: true,
    touchStartThreshold: 5,
    ghostClass: 'layout-card-ghost',
    chosenClass: 'layout-card-chosen',
    dragClass: 'layout-card-dragging',
    onChange: () => persist(node, selector, options.key),
    onUpdate: () => persist(node, selector, options.key),
    onEnd: () => {
      persist(node, selector, options.key);
    }
  });

  const reset = (event: Event) => {
    const scope = (event as CustomEvent<{ scope: string }>).detail?.scope || 'all';
    if (scope !== 'all' && options.key !== scope && !options.key.startsWith(`${scope}:`)) return;
    localStorage.removeItem(STORAGE_PREFIX + options.key);
    arrange(node, directItems(node, selector), defaultOrder);
  };
  window.addEventListener('timelens-layout-reset', reset);

  return {
    update(nextOptions) {
      options = nextOptions;
      selector = options.draggable || ':scope > *';
    },
    destroy() {
      sortable.destroy();
      window.removeEventListener('timelens-layout-reset', reset);
      directItems(node, ':scope > .layout-card').forEach(item => {
        item.classList.remove('layout-card');
        item.querySelector(':scope > .card-drag-handle')?.remove();
      });
    }
  };
};

export function resetCardLayouts(scope = 'all'): void {
  Object.keys(localStorage)
    .filter(key => key.startsWith(STORAGE_PREFIX))
    .filter(key => {
      if (scope === 'all') return true;
      const layoutKey = key.slice(STORAGE_PREFIX.length);
      return layoutKey === scope || layoutKey.startsWith(`${scope}:`);
    })
    .forEach(key => localStorage.removeItem(key));
  window.dispatchEvent(new CustomEvent('timelens-layout-reset', { detail: { scope } }));
}
