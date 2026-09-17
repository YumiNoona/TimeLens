import { test } from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
const sharedSource = readFileSync(new URL('../src/browser-extensions/shared/background.js', import.meta.url), 'utf8');

for (const family of ['chrome', 'firefox']) test(`${family}: focus, navigation, private tabs, recovery`, async () => {
  const source = sharedSource;
  const calls = [];
  const event = () => ({ addListener(fn) { this.listener = fn; } });
  let focused = true;
  let tab = { id: 1, active: true, url: 'https://example.com/a', title: 'A', audible: false };
  let offline = false;
  let unauthorized = false;
  let blockAction = 'none';
  const injections = [], redirects = [];
  const messageListeners = [];
  const stored = { timelens_pair_token: 'test-token' };
  const api = {
    runtime: { id: 'test', getManifest: () => ({ version: '7.8.0' }), getURL: p => `extension://${p}`,
      onMessage: { addListener(fn) { messageListeners.push(fn); } }, onStartup: event(), onInstalled: event() },
    action: { onClicked: event() },
    windows: { getLastFocused: async () => ({ id: 7, focused, type: 'normal' }), onFocusChanged: event() },
    tabs: { query: async query => { if (query.audible) return tab.audible ? [tab] : []; assert.equal(query.windowId, 7); return [tab]; },
      onActivated: event(), onUpdated: event(), onRemoved: event(), executeScript: async (id, options) => { injections.push(options.code); }, update: async (id, options) => { redirects.push(options.url); } },
    storage: { local: {
      remove(key) { delete stored[key]; },
      get: async key => typeof key === 'string' ? { [key]: stored[key] } : { ...stored },
      set: async values => { Object.assign(stored, structuredClone(values)); }
    } }, scripting: { executeScript: async options => { injections.push(options.func.name); } },
    alarms: { create() {}, onAlarm: event() }
  };
  const sandbox = { [family === 'firefox' ? 'browser' : 'chrome']: api, navigator: { userAgent: 'Chrome' }, URL, Date, AbortSignal, crypto: globalThis.crypto,
    setInterval() {}, clearInterval() {}, fetch: async (url, options) => {
      if (offline) throw new Error('offline');
      if (unauthorized) return { ok: false, status: 401, json: async () => ({}) };
      calls.push({ url, body: options?.body ? JSON.parse(options.body) : null, headers: options?.headers || {} });
      return { ok: true, json: async () => url.endsWith('/api/extension/settings')
        ? { trackBrowser: true, trackInput: true, focusMode: true }
        : url.endsWith('/api/browser-input') ? { accepted: true }
          : { action: blockAction, presentation: { target: 'example.com' } } };
    } };
  vm.runInNewContext(source, sandbox);
  const settle = async () => { for (let i = 0; i < 12; i++) await new Promise(setImmediate); };
  const observations = () => calls.filter(x => x.url.endsWith('/api/browser-event'));
  await settle();
  await assert.rejects(sandbox.checkedFetch('https://example.net/private'), /Non-local/);
  assert.equal(observations().at(-1).body.tabId, 1);
  assert.ok(observations().at(-1).body.observedAt);
  assert.equal(observations().at(-1).headers['X-TimeLens-Extension'], 'test-token');
  tab = { ...tab, id: 2, url: 'https://example.com/b' };
  api.tabs.onActivated.listener({ tabId: 2 }); await settle();
  assert.equal(observations().at(-1).body.tabId, 2);
  const count = observations().length;
  focused = false; api.windows.onFocusChanged.listener(-1); await settle();
  assert.equal(observations().length, count);
  assert.ok(calls.at(-2).url.endsWith('/api/browser-leave') || calls.at(-1).url.endsWith('/api/browser-leave'));
  focused = true; tab.incognito = true; api.tabs.onActivated.listener({ tabId: 2 }); await settle();
  assert.equal(observations().length, count);
  tab.incognito = false; offline = true; api.tabs.onActivated.listener({ tabId: 2 }); await settle();
  tab.url = 'https://recovered.example/'; offline = false;
  api.alarms.onAlarm.listener({ name: 'timelens-heartbeat' }); await settle();
  assert.equal(observations().at(-1).body.url, tab.url);
  assert.equal(observations().length, count + 1);
  const mediaResponse = await new Promise(resolve => {
    for (const listener of messageListeners) {
      const handled = listener({ type: 'timelens-media-state', state: { mediaId: 'video-1', kind: 'video',
        playing: true, audible: true, muted: false, pictureInPicture: true, visibility: 'pip', confidence: 'media-element' } },
        { tab, frameId: 0 }, resolve);
      if (handled) break;
    }
  });
  assert.ok(mediaResponse);
  const mediaCall = calls.findLast(x => x.url.endsWith('/api/media-state'));
  assert.equal(mediaCall.body.url, tab.url);
  assert.equal(mediaCall.body.pictureInPicture, true);
  assert.ok(mediaCall.body.observedAt);
  unauthorized = true;
  await assert.rejects(sandbox.checkedFetch('http://127.0.0.1:47821/api/extension/settings'), /HTTP 401/);
  assert.equal(stored.timelens_pair_token, 'test-token', 'Transient 401 must not erase pairing');
  const rejectedStatus = await new Promise(resolve => {
    for (const listener of messageListeners) if (listener({ type: 'timelens-status' }, {}, resolve)) break;
  });
  assert.equal(rejectedStatus.needsPair, true, 'A server-rejected token must ask the user to pair again');
  assert.equal(rejectedStatus.connected, false, 'A stored but rejected token must never be shown as connected');
  unauthorized = false;
  const recoveredStatus = await new Promise(resolve => {
    for (const listener of messageListeners) if (listener({ type: 'timelens-status' }, {}, resolve)) break;
  });
  assert.equal(recoveredStatus.connected, true, 'A valid saved token must reconnect without another pairing step');
  blockAction = 'notify'; api.tabs.onActivated.listener({ tabId: 2 }); await settle();
  assert.ok(injections.some(text => text.includes('mountNotifyToast')), 'Notify must inject a reminder');
  blockAction = 'strict'; api.tabs.onActivated.listener({ tabId: 2 }); await settle();
  assert.ok(redirects.some(url => url.startsWith('extension://blocked.html?')), 'Strict must redirect to the blocked page');

  offline = true;
  const queued = await new Promise(resolve => {
    for (const listener of messageListeners) {
      const handled = listener({ type: 'timelens-input', batch: { batchId: crypto.randomUUID(), url: tab.url,
        title: tab.title, observedAt: new Date().toISOString(), keystrokes: 2, clicks: 1 } }, { tab, frameId: 0 }, resolve);
      if (handled) break;
    }
  });
  assert.equal(queued.queued, true);
  assert.equal(stored.timelens_pending_input_v1.length, 1, 'Pending input must survive extension shutdown or upgrade');
  offline = false;
  api.runtime.onInstalled.listener({ reason: 'update', previousVersion: '7.3.0' }); await settle();
  assert.equal(stored.timelens_pending_input_v1.length, 0, 'A preserved pending batch must drain after update');
});

test('browser packages contain the canonical scripts and resources', () => {
  for (const family of ['chrome', 'firefox']) {
    const root = new URL(`../src/browser-extensions/${family}/`, import.meta.url);
    const manifest = JSON.parse(readFileSync(new URL('manifest.json', root)));
    assert.equal(manifest.version, '7.8.0');
    if (family === 'firefox') {
      assert.equal(manifest.browser_specific_settings.gecko.id, 'timelens@timelens.app');
      assert.equal(manifest.browser_specific_settings.gecko_android.strict_min_version, '142.0');
      assert.deepEqual(manifest.browser_specific_settings.gecko.data_collection_permissions.required,
        ['browsingActivity', 'websiteActivity']);
    }
    assert.deepEqual(manifest.content_scripts[0].matches, ['http://*/*', 'https://*/*']);
    for (const file of ['popup.html', 'popup.js', 'blocked.html', 'blocked.js']) assert.ok(readFileSync(new URL(file, root), 'utf8').length);
    for (const icon of Object.values(manifest.icons)) assert.ok(readFileSync(new URL(icon, root)).length);
  }
  assert.doesNotMatch(sharedSource, /storage\.local\.remove\(['"]timelens_queue/, 'Startup must not delete prior extension state');
});


test('content input counts trusted focused events only and retries stable batches', async () => {
  const listeners = {}, sent = [];
  let timer, focused = true, now = Date.now(), retry = false;
  const content = readFileSync(new URL('../src/browser-extensions/shared/content.js', import.meta.url), 'utf8');
  const addEventListener = (name, fn) => { listeners[name] = fn; };
  const sandbox = { chrome: { runtime: { sendMessage: async msg => { sent.push(structuredClone(msg)); if (retry) { retry = false; return { retry: true }; } return { accepted: true }; } } },
    document: { hasFocus: () => focused, visibilityState: 'visible', title: 'Test', addEventListener },
    window: { addEventListener }, location: { href: 'https://example.com/page' },
    Date: class extends Date { static now() { return now; } }, crypto: globalThis.crypto,
    setTimeout(fn) { timer = fn; return 1; } };
  sandbox.window.top = sandbox.window;
  sandbox.window.setTimeout = sandbox.setTimeout;
  vm.runInNewContext(content, sandbox);
  await new Promise(setImmediate);
  const real = { isTrusted: true, get key() { throw new Error('Typed key must never be read'); } };
  listeners.keydown(real); listeners.keydown(real); listeners.pointerdown(real);
  listeners.keydown({ isTrusted: false });
  focused = false; listeners.keydown(real); focused = true;
  retry = true; await timer(); await new Promise(setImmediate); await timer(); await new Promise(setImmediate);
  const nonzero = sent.filter(x => x.batch.keystrokes > 0);
  assert.equal(nonzero.length, 2);
  assert.equal(nonzero[0].batch.batchId, nonzero[1].batch.batchId);
  assert.equal(nonzero[0].batch.keystrokes, 2); assert.equal(nonzero[0].batch.clicks, 1);
  now += 2000; sandbox.location.href = 'https://example.com/next';
  listeners.keydown(real); await timer(); await new Promise(setImmediate);
  assert.equal(sent.at(-1).batch.url, sandbox.location.href);
  assert.notEqual(sent.at(-1).batch.batchId, nonzero[0].batch.batchId);
});

test('content media collector reports background, PiP, checkpoints and stop without polling frames', async () => {
  const handlers = new Map(), sent = [], timers = [];
  const on = (name, fn) => handlers.set(name, [...(handlers.get(name) || []), fn]);
  const media = { tagName: 'VIDEO', paused: false, ended: false, readyState: 4, muted: false,
    volume: 1, currentTime: 321.5, duration: 7200, playbackRate: 1.25,
    isConnected: true, querySelectorAll: () => [] };
  const document = { visibilityState: 'hidden', title: 'Tutorial', pictureInPictureElement: null,
    documentElement: {}, hasFocus: () => false, querySelectorAll: () => [media], addEventListener: on };
  const window = { addEventListener: on, setTimeout(fn, delay) { timers.push({ fn, delay }); return timers.length; }, clearTimeout() {} };
  window.top = window;
  class MutationObserver { observe() {} }
  const content = readFileSync(new URL('../src/browser-extensions/shared/content.js', import.meta.url), 'utf8');
  vm.runInNewContext(content, { chrome: { runtime: { sendMessage: async msg => { sent.push(structuredClone(msg)); return { accepted: true }; } } },
    document, window, location: { href: 'https://youtube.com/watch?v=tutorial' }, Date, crypto: globalThis.crypto,
    MutationObserver, setTimeout: window.setTimeout });
  await new Promise(setImmediate);
  const first = sent.find(x => x.type === 'timelens-media-state');
  assert.equal(first.state.visibility, 'background');
  assert.equal(first.state.playing, true);
  assert.equal(first.state.positionSeconds, 321.5);
  assert.equal(first.state.durationSeconds, 7200);
  assert.equal(first.state.playbackRate, 1.25);
  document.pictureInPictureElement = media;
  for (const fn of handlers.get('enterpictureinpicture') || []) fn({ target: media });
  await new Promise(setImmediate);
  assert.equal(sent.at(-1).state.visibility, 'pip');
  assert.equal(timers.some(x => x.delay === 15000), true, 'Playback uses a low-frequency lease checkpoint');
  media.paused = true;
  for (const fn of handlers.get('pause') || []) fn({ target: media });
  await new Promise(setImmediate);
  assert.equal(sent.at(-1).state.playing, false);
});
