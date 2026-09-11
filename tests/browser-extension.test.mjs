import { test } from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
const source = readFileSync(new URL('../src/browser-extensions/firefox/background.js', import.meta.url), 'utf8');

for (const family of ['firefox', 'chrome']) test(`${family}: focus, navigation, private tabs, recovery`, async () => {
  const calls = [];
  const event = () => ({ addListener(fn) { this.listener = fn; } });
  let focused = true;
  let tab = { id: 1, active: true, url: 'https://example.com/a', title: 'A', audible: false };
  let offline = false;
  let blockAction = 'none';
  const injections = [], redirects = [];
  const api = {
    runtime: { id: 'test', getManifest: () => ({ version: '7.1.0' }), getURL: p => `extension://${p}`,
      onMessage: event(), onStartup: event(), onInstalled: event() },
    action: { onClicked: event() },
    windows: { getLastFocused: async () => ({ id: 7, focused, type: 'normal' }), onFocusChanged: event() },
    tabs: { query: async query => { assert.equal(query.windowId, 7); return [tab]; },
      onActivated: event(), onUpdated: event(), onRemoved: event(), executeScript: async (id, options) => { injections.push(options.code); }, update: async (id, options) => { redirects.push(options.url); } },
    storage: { local: { remove() {}, get: async () => ({ timelens_pair_token: 'test-token' }), set: async () => {} } }, scripting: { executeScript: async options => { injections.push(options.func.name); } },
    alarms: { create() {}, onAlarm: event() }
  };
  const sandbox = { [family === 'firefox' ? 'browser' : 'chrome']: api, navigator: { userAgent: 'Chrome' }, URL, Date, AbortSignal,
    setInterval() {}, clearInterval() {}, fetch: async (url, options) => {
      if (offline) throw new Error('offline');
      calls.push({ url, body: options?.body ? JSON.parse(options.body) : null, headers: options?.headers || {} });
      return { ok: true, json: async () => url.endsWith('/api/extension/settings') ? { trackBrowser: true, focusMode: true } : { action: blockAction, presentation: { target: 'example.com' } } };
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
  blockAction = 'notify'; api.tabs.onActivated.listener({ tabId: 2 }); await settle();
  assert.ok(injections.some(text => text.includes('mountNotifyToast')), 'Notify must inject a reminder');
  blockAction = 'strict'; api.tabs.onActivated.listener({ tabId: 2 }); await settle();
  assert.ok(redirects.some(url => url.startsWith('extension://blocked.html?')), 'Strict must redirect to the blocked page');
});

test('Both browser packages contain the canonical scripts and resources', () => {
  for (const family of ['firefox', 'chrome']) {
    const root = new URL(`../src/browser-extensions/${family}/`, import.meta.url);
    const manifest = JSON.parse(readFileSync(new URL('manifest.json', root)));
    assert.equal(manifest.version, '7.2.0');
    assert.equal(readFileSync(new URL('background.js', root), 'utf8'), source);
    for (const file of ['popup.js','blocked.js']) assert.equal(readFileSync(new URL(file, root), 'utf8'), readFileSync(new URL(`../src/browser-extensions/firefox/${file}`, import.meta.url), 'utf8'));
    if (family === 'firefox') {
    assert.equal(manifest.browser_specific_settings.gecko.id, 'timelens@timelens.app');
    assert.equal(manifest.browser_specific_settings.gecko_android.strict_min_version, '142.0');
    assert.deepEqual(manifest.browser_specific_settings.gecko.data_collection_permissions.required,
      ['browsingActivity', 'websiteActivity']);
    }
    assert.equal(readFileSync(new URL('content.js', root), 'utf8'), readFileSync(new URL('../src/browser-extensions/shared/content.js', import.meta.url), 'utf8'));
    assert.deepEqual(manifest.content_scripts[0].matches, ['http://*/*', 'https://*/*']);
    for (const file of ['popup.html', 'popup.js', 'blocked.html', 'blocked.js']) assert.ok(readFileSync(new URL(file, root), 'utf8').length);
    for (const icon of Object.values(manifest.icons)) assert.ok(readFileSync(new URL(icon, root)).length);
  }
});


test('content input counts trusted focused events only and retries stable batches', async () => {
  const listeners = {}, sent = [];
  let timer, focused = true, now = Date.now(), retry = false;
  const content = readFileSync(new URL('../src/browser-extensions/firefox/content.js', import.meta.url), 'utf8');
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
