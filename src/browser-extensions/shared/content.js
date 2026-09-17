// Aggregate counts only. Never read event.key, event.code, input values or page text.
(() => {
  const runtime = (typeof browser !== 'undefined' ? browser : chrome).runtime;
  let current = null;
  const pending = [];
  let sending = false;
  let flushTimer = 0;
  function scheduleFlush(delay = 1000) {
    if (flushTimer) return;
    flushTimer = window.setTimeout(() => { flushTimer = 0; void flush(); }, delay);
  }
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
    scheduleFlush();
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
        if (!response || response.retry) { scheduleFlush(5000); break; }
        if (pending[0] === batch) pending.shift();
      }
    } catch (_) { scheduleFlush(5000); /* Retry the same ID; the desktop deduplicates it. */ }
    finally { sending = false; }
  }
  document.addEventListener('keydown', event => { if (event.isTrusted) collect(1, 0); }, true);
  document.addEventListener('pointerdown', event => { if (event.isTrusted) collect(0, 1); }, true);
  window.addEventListener('pagehide', flush);
  window.addEventListener('blur', flush);
  window.addEventListener('focus', () => { if (window === window.top) collect(0, 0); void flush(); });
  document.addEventListener('visibilitychange', flush);
  // Embedded frames remain dormant until actual input occurs.
  if (window === window.top) { collect(0, 0); scheduleFlush(250); }
})();

// Media state is a separate, concurrent signal. It is never treated as keyboard/
// pointer activity and never inflates the exclusive foreground-app clock.
(() => {
  if (!document.querySelectorAll || typeof MutationObserver === 'undefined') return;
  const runtime = (typeof browser !== 'undefined' ? browser : chrome).runtime;
  const ids = new WeakMap();
  const known = new Set();
  let heartbeatTimer = 0;

  function idFor(media) {
    let id = ids.get(media);
    if (!id) { id = crypto.randomUUID(); ids.set(media, id); }
    return id;
  }
  function isPlaying(media) {
    return !media.paused && !media.ended && media.readyState >= 2;
  }
  function pipFor(media) {
    try { return document.pictureInPictureElement === media; } catch (_) { return false; }
  }
  function visibilityFor(media) {
    if (pipFor(media)) return 'pip';
    return document.visibilityState === 'visible' && document.hasFocus() ? 'foreground' : 'background';
  }
  function snapshot(media, forcePlaying) {
    const playing = forcePlaying === undefined ? isPlaying(media) : forcePlaying;
    return {
      mediaId: idFor(media), url: location.href, title: document.title.slice(0, 4096),
      kind: media.tagName && media.tagName.toLowerCase() === 'audio' ? 'audio' : 'video',
      playing, audible: playing && !media.muted && media.volume > 0,
      muted: !!media.muted || media.volume === 0, pictureInPicture: pipFor(media),
      visibility: visibilityFor(media), confidence: 'media-element',
      observedAt: new Date().toISOString()
    };
  }
  function report(media, forcePlaying) {
    if (!/^https?:\/\//.test(location.href)) return;
    const state = snapshot(media, forcePlaying);
    if (state.playing) known.add(media); else known.delete(media);
    try { void runtime.sendMessage({ type: 'timelens-media-state', state }); } catch (_) {}
    updateHeartbeat();
  }
  function heartbeat() {
    heartbeatTimer = 0;
    for (const media of [...known]) {
      if (!media.isConnected || !isPlaying(media)) report(media, false);
      else report(media);
    }
    updateHeartbeat();
  }
  function updateHeartbeat() {
    if (known.size && !heartbeatTimer) heartbeatTimer = window.setTimeout(heartbeat, 15000);
    if (!known.size && heartbeatTimer) { window.clearTimeout(heartbeatTimer); heartbeatTimer = 0; }
  }
  function register(media) {
    if (!media || (media.tagName !== 'AUDIO' && media.tagName !== 'VIDEO')) return;
    if (isPlaying(media)) report(media);
  }
  function scan(node) {
    register(node);
    if (node && node.querySelectorAll) for (const media of node.querySelectorAll('audio,video')) register(media);
  }

  for (const type of ['play', 'playing', 'pause', 'ended', 'emptied', 'volumechange',
    'enterpictureinpicture', 'leavepictureinpicture'])
    document.addEventListener(type, event => {
      const media = event.target;
      if (!media || (media.tagName !== 'AUDIO' && media.tagName !== 'VIDEO')) return;
      report(media, type === 'pause' || type === 'ended' || type === 'emptied' ? false : undefined);
    }, true);
  document.addEventListener('visibilitychange', () => { for (const media of known) report(media); });
  window.addEventListener('pagehide', () => { for (const media of [...known]) report(media, false); });
  new MutationObserver(records => {
    for (const record of records) for (const node of record.addedNodes) scan(node);
  }).observe(document.documentElement || document, { childList: true, subtree: true });
  scan(document);
})();
