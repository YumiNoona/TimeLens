// Aggregate counts only. Never read event.key, event.code, input values or page text.
(() => {
  const runtime = (typeof browser !== 'undefined' ? browser : chrome).runtime;
  let current = null;
  const pending = [];
  let sending = false;
  function collect(keys, clicks) {
    if (!document.hasFocus() || document.visibilityState !== 'visible') return;
    const now = Date.now();
    const url = location.href;
    if (!/^https?:\/\//.test(url)) return;
    if (current && (current.url !== url || current.second !== Math.floor(now / 1000))) seal();
    if (!current) current = { batchId: crypto.randomUUID(), url, title: document.title.slice(0, 4096),
      observedAt: new Date(now).toISOString(), second: Math.floor(now / 1000), keystrokes: 0, clicks: 0 };
    current.keystrokes += keys;
    current.clicks += clicks;
  }
  function seal() {
    if (current) { pending.push(current); current = null; }
    // Bounded, memory-only retry. Closed pages and extended outages are not reconstructed.
    while (pending.length > 120) pending.shift();
  }
  async function flush() {
    seal();
    if (sending) return;
    sending = true;
    try {
      while (pending.length) {
        const batch = pending[0];
        if (Date.now() - Date.parse(batch.observedAt) > 120000) { pending.shift(); continue; }
        const response = await runtime.sendMessage({ type: 'timelens-input', batch });
        if (!response || response.retry) break;
        if (pending[0] === batch) pending.shift();
      }
    } catch (_) { /* Retry the same ID; the desktop deduplicates it. */ }
    finally { sending = false; }
  }
  document.addEventListener('keydown', event => { if (event.isTrusted) collect(1, 0); }, true);
  document.addEventListener('pointerdown', event => { if (event.isTrusted) collect(0, 1); }, true);
  window.addEventListener('pagehide', flush);
  window.addEventListener('blur', flush);
  window.addEventListener('focus', () => { collect(0, 0); flush(); });
  document.addEventListener('visibilitychange', flush);
  setInterval(flush, 1000);
  collect(0, 0); flush();
})();
