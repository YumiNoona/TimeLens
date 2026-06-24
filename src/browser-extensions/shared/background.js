// Single source of truth for both Chrome MV3 and Firefox MV2.
// Browser auto-detection: Firefox exposes `browser` globally; Chrome does not.
const BROWSER = typeof browser !== 'undefined' && browser.runtime?.id ? 'firefox' : 'chrome';
const api = BROWSER === 'firefox' ? browser : chrome;
const actionApi = api.action || api.browserAction;

const API = 'http://127.0.0.1:47821/api/browser-event';
const AUDIBLE_API = 'http://127.0.0.1:47821/api/audible-status';
const SETTINGS_API = 'http://127.0.0.1:47821/api/settings';
const DASHBOARD = 'http://127.0.0.1:47821/';
const QUEUE_KEY = 'timelens_queue';

let trackingEnabled = true;

function refreshTrackingFlag() {
  fetch(SETTINGS_API)
    .then(r => r.json())
    .then(s => { trackingEnabled = s.trackBrowser !== false; })
    .catch(() => {});
}
refreshTrackingFlag();
setInterval(refreshTrackingFlag, 300_000);

// --- Dedup ---
const lastUrl = {};
const debounceTimers = {};

// --- Offline queue ---
function enqueue(event) {
  api.storage.local.get(QUEUE_KEY, (result) => {
    const queue = result[QUEUE_KEY] || [];
    queue.push(event);
    api.storage.local.set({ [QUEUE_KEY]: queue });
  });
}

function flushQueue() {
  api.storage.local.get(QUEUE_KEY, (result) => {
    const queue = result[QUEUE_KEY];
    if (!queue || queue.length === 0) return;
    api.storage.local.remove(QUEUE_KEY);
    for (const evt of queue) {
      fetch(API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(evt),
      }).catch(() => enqueue(evt));
    }
  });
}

function doSendTab(url, title, audible) {
  if (!trackingEnabled) return;
  try {
    const u = new URL(url);
    const body = { domain: u.hostname, url, title: title || '', browser: BROWSER, audible: !!audible };
    fetch(API, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
      .then(() => flushQueue())
      .catch(() => enqueue(body));
  } catch {}
}

function sendTab(tabId, url, title, audible) {
  if (!trackingEnabled) return;
  if (debounceTimers[tabId]) clearTimeout(debounceTimers[tabId]);
  debounceTimers[tabId] = setTimeout(() => {
    delete debounceTimers[tabId];
    doSendTab(url, title, audible);
  }, 1000);
}

function reportAudible(audible) {
  if (!trackingEnabled) return;
  fetch(AUDIBLE_API, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ audible, browser: BROWSER }),
  }).catch(() => {});
}

// --- Event listeners ---
actionApi.onClicked.addListener(() => {
  api.tabs.create({ url: DASHBOARD });
});

api.tabs.onActivated.addListener(({ tabId }) => {
  api.tabs.get(tabId, (tab) => {
    if (tab?.url && tab.url.startsWith('http')) {
      lastUrl[tabId] = tab.url;
      sendTab(tabId, tab.url, tab.title, tab.audible);
    }
  });
});

api.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.audible !== undefined) {
    reportAudible(!!changeInfo.audible);
  }
  if (changeInfo.status === 'complete' && tab?.url && tab.url.startsWith('http') && lastUrl[tabId] !== tab.url) {
    lastUrl[tabId] = tab.url;
    sendTab(tabId, tab.url, tab.title, tab.audible);
  }
});

api.tabs.onRemoved.addListener((tabId) => {
  delete lastUrl[tabId];
  if (debounceTimers[tabId]) {
    clearTimeout(debounceTimers[tabId]);
    delete debounceTimers[tabId];
  }
});

// Retry queued events on startup
flushQueue();
