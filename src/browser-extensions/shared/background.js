// Shared background script for Chrome MV3 and Firefox MV2.
const BROWSER = typeof browser !== 'undefined' && browser.runtime && browser.runtime.id ? 'firefox' : 'chrome';
const api = BROWSER === 'firefox' ? browser : chrome;
const actionApi = api.action || api.browserAction;
const ROOT = 'http://127.0.0.1:47821';
const EVENT_API = ROOT + '/api/browser-event';
const STATE_API = ROOT + '/api/browser-block-state';
const SETTINGS_API = ROOT + '/api/settings';
const HEARTBEAT_API = ROOT + '/api/extension-heartbeat';
const TAB_HEARTBEAT_API = ROOT + '/api/browser-heartbeat';
const LEAVE_API = ROOT + '/api/browser-leave';
const AUDIBLE_API = ROOT + '/api/audible-status';
const DASHBOARD = ROOT + '/';
const BLOCKED_PAGE = api.runtime.getURL('blocked.html');
const QUEUE_KEY = 'timelens_queue';
const MAX_QUEUE_SIZE = 500;
const EXTENSION_VERSION = api.runtime.getManifest().version;

let trackingEnabled = true;
let blockingEnabled = false;
const lastUrl = {};
const debounceTimers = {};
const imageDataCache = {};

function checkedFetch(url, options) {
  return fetch(url, options).then(function(response) {
    if (!response.ok) throw new Error('HTTP ' + response.status);
    return response;
  });
}

function stateFor(domain) {
  return checkedFetch(STATE_API + '?domain=' + encodeURIComponent(domain || ''))
    .then(function(response) { return response.json(); });
}

function hydrateNotifyImage(response) {
  const presentation = response && response.presentation;
  const imageUrl = presentation && presentation.imageUrl;
  if (!presentation || response.action !== 'notify' || !imageUrl) return Promise.resolve(response);
  if (imageDataCache[imageUrl]) {
    presentation.imageDataUrl = imageDataCache[imageUrl];
    return Promise.resolve(response);
  }
  return checkedFetch(imageUrl).then(function(imageResponse) { return imageResponse.blob(); }).then(function(blob) {
    return blob.arrayBuffer().then(function(buffer) {
      const bytes = new Uint8Array(buffer);
      let binary = '';
      for (let offset = 0; offset < bytes.length; offset += 0x8000) {
        binary += String.fromCharCode.apply(null, bytes.subarray(offset, Math.min(offset + 0x8000, bytes.length)));
      }
      imageDataCache[imageUrl] = 'data:' + (blob.type || 'image/png') + ';base64,' + btoa(binary);
      presentation.imageDataUrl = imageDataCache[imageUrl];
      return response;
    });
  }).catch(function() { return response; });
}

function sendHeartbeat() {
  checkedFetch(HEARTBEAT_API + '?browser=' + encodeURIComponent(BROWSER) +
    '&version=' + encodeURIComponent(EXTENSION_VERSION) + '&ts=' + Date.now(), { method: 'POST' })
    .catch(function() {});
}

function fetchSettings() {
  return checkedFetch(SETTINGS_API).then(function(response) { return response.json(); }).then(function(settings) {
    trackingEnabled = settings.trackBrowser !== false;
    blockingEnabled = settings.focusMode === true;
    return settings;
  }).catch(function() {});
}

function mountNotifyToast(presentation, domain) {
  const ROOT_ID = '__timelens_focus_toast';
  const existing = document.getElementById(ROOT_ID);
  if (existing) existing.remove();
  if (window.__timelensFocusTimer) clearInterval(window.__timelensFocusTimer);
  if (window.__timelensFocusPulse) clearInterval(window.__timelensFocusPulse);

  const root = document.createElement('aside');
  root.id = ROOT_ID;
  root.setAttribute('role', 'status');
  root.style.cssText = 'all:initial;position:fixed;left:22px;bottom:22px;z-index:2147483647;width:min(390px,calc(100vw - 44px));box-sizing:border-box;padding:16px 18px 16px 16px;border:1px solid rgba(126,215,218,.46);border-radius:16px;background:#11191c;color:#edf6f4;box-shadow:0 20px 60px rgba(0,0,0,.48);font:14px/1.45 Inter,Segoe UI,sans-serif;display:flex;gap:14px;align-items:flex-start;transition:opacity .18s ease,transform .18s ease';
  const image = presentation && (presentation.imageDataUrl || presentation.imageUrl);
  if (image) {
    const img = document.createElement('img');
    img.src = image;
    img.alt = '';
    img.style.cssText = 'width:62px;height:62px;flex:0 0 62px;border-radius:11px;object-fit:cover;background:#0b1113';
    img.onerror = function() { img.remove(); };
    root.appendChild(img);
  }
  const copy = document.createElement('div');
  copy.style.cssText = 'min-width:0;flex:1';
  const eyebrow = document.createElement('div');
  eyebrow.textContent = 'TIMELENS · NOTIFY';
  eyebrow.style.cssText = 'margin:0 0 3px;color:#83d7d8;font:600 10px/1.2 Inter,Segoe UI,sans-serif;letter-spacing:.11em';
  const title = document.createElement('strong');
  title.textContent = (presentation && presentation.title) || 'Focus Mode';
  title.style.cssText = 'display:block;margin:0 22px 2px 0;color:#f4fbfa;font:600 14px/1.35 Inter,Segoe UI,sans-serif';
  const message = document.createElement('div');
  message.textContent = (presentation && presentation.message) || 'This website is on your focus list.';
  message.style.cssText = 'color:#a9b9b6;font:12px/1.45 Inter,Segoe UI,sans-serif;overflow-wrap:anywhere';
  copy.append(eyebrow, title, message);
  root.appendChild(copy);
  const close = document.createElement('button');
  close.type = 'button';
  close.textContent = '×';
  close.setAttribute('aria-label', 'Dismiss TimeLens reminder');
  close.style.cssText = 'all:initial;position:absolute;right:10px;top:8px;color:#93a5a2;cursor:pointer;font:20px/1 Segoe UI,sans-serif';
  close.onclick = function() { root.style.opacity = '0'; root.style.pointerEvents = 'none'; };
  root.appendChild(close);
  (document.documentElement || document.body).appendChild(root);

  function showAgain() {
    if (!document.getElementById(ROOT_ID)) return;
    root.style.opacity = '1';
    root.style.pointerEvents = 'auto';
    root.style.transform = 'scale(1.015)';
    setTimeout(function() { root.style.transform = 'scale(1)'; }, 180);
  }

  async function checkState() {
    try {
      const runtime = typeof browser !== 'undefined' ? browser.runtime : chrome.runtime;
      const response = await runtime.sendMessage({ type: 'timelens-check-block', domain: domain });
      if (!response || response.action === 'none') {
        clearInterval(window.__timelensFocusTimer);
        root.remove();
      } else if (response.action === 'strict') {
        clearInterval(window.__timelensFocusTimer);
        location.href = runtime.getURL('blocked.html') + '?target=' + encodeURIComponent(domain) + '&url=' + encodeURIComponent(location.href);
      }
    } catch (_) {}
  }

  window.__timelensFocusTimer = setInterval(checkState, 2000);
  window.__timelensFocusPulse = setInterval(showAgain, 12000);
}

function clearNotifyToast() {
  const root = document.getElementById('__timelens_focus_toast');
  if (root) root.remove();
  if (window.__timelensFocusTimer) clearInterval(window.__timelensFocusTimer);
  if (window.__timelensFocusPulse) clearInterval(window.__timelensFocusPulse);
}

function executeInTab(tabId, fn, args) {
  if (BROWSER === 'chrome' && api.scripting) {
    return api.scripting.executeScript({ target: { tabId: tabId }, func: fn, args: args || [] }).catch(function() {});
  }
  const code = '(' + fn.toString() + ').apply(null,' + JSON.stringify(args || []) + ');';
  return api.tabs.executeScript(tabId, { code: code }).catch(function() {});
}

function applyBlockResponse(tabId, originalUrl, response) {
  if (!response || response.action === 'none') {
    return executeInTab(tabId, clearNotifyToast, []);
  }
  if (response.action === 'notify') {
    return executeInTab(tabId, mountNotifyToast, [response.presentation || {}, response.presentation && response.presentation.target || '']);
  }
  if (response.action === 'strict' && originalUrl && originalUrl.indexOf(BLOCKED_PAGE) !== 0) {
    const target = response.presentation && response.presentation.target || '';
    return api.tabs.update(tabId, { url: BLOCKED_PAGE + '?target=' + encodeURIComponent(target) + '&url=' + encodeURIComponent(originalUrl) });
  }
}

api.runtime.onMessage.addListener(function(message, sender, sendResponse) {
  if (!message || message.type !== 'timelens-check-block') return false;
  stateFor(message.domain).then(sendResponse).catch(function() { sendResponse({ action: 'none' }); });
  return true;
});

function enqueue(event) {
  api.storage.local.get(QUEUE_KEY, function(result) {
    let queue = result[QUEUE_KEY] || [];
    queue.push(event);
    if (queue.length > MAX_QUEUE_SIZE) queue = queue.slice(queue.length - MAX_QUEUE_SIZE);
    const next = {}; next[QUEUE_KEY] = queue;
    api.storage.local.set(next);
  });
}

let flushing = false;
function flushQueue() {
  if (flushing) return;
  flushing = true;
  api.storage.local.get(QUEUE_KEY, function(result) {
    const queue = result[QUEUE_KEY] || [];
    if (!queue.length) { flushing = false; return; }
    api.storage.local.remove(QUEUE_KEY);
    Promise.all(queue.map(function(evt) {
      return checkedFetch(evt._leave ? LEAVE_API : EVENT_API, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(evt)
      }).catch(function() { enqueue(evt); });
    })).finally(function() { flushing = false; });
  });
}

function doSendTab(tabId, url, title, audible) {
  if (!trackingEnabled && !blockingEnabled) return;
  try {
    const parsed = new URL(url);
    const body = { tabId: tabId, domain: parsed.hostname, url: url, title: title || '', browser: BROWSER, audible: !!audible };
    checkedFetch(EVENT_API, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    }).then(function(response) { return response.json(); })
      .then(hydrateNotifyImage)
      .then(function(response) { applyBlockResponse(tabId, url, response); flushQueue(); })
      .catch(function() { enqueue(body); });
  } catch (_) {}
}

function sendTab(tabId, url, title, audible) {
  if (debounceTimers[tabId]) clearTimeout(debounceTimers[tabId]);
  debounceTimers[tabId] = setTimeout(function() {
    delete debounceTimers[tabId];
    doSendTab(tabId, url, title, audible);
  }, 500);
}

function sendTabHeartbeat() {
  if (!trackingEnabled) return;
  api.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    if (!tabs || !tabs.length || !tabs[0].url || tabs[0].url.indexOf('http') !== 0) return;
    const tab = tabs[0];
    let domain = '';
    try { domain = new URL(tab.url).hostname; } catch (_) { return; }
    checkedFetch(TAB_HEARTBEAT_API, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tabId: tab.id, domain: domain, url: tab.url, title: tab.title || '', browser: BROWSER })
    }).catch(function() {});
  });
}

function reportAudible(audible) {
  if (!trackingEnabled) return;
  checkedFetch(AUDIBLE_API, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ audible: audible, browser: BROWSER }) }).catch(function() {});
}

actionApi.onClicked.addListener(function() { api.tabs.create({ url: DASHBOARD }); });
api.tabs.onActivated.addListener(function(info) {
  api.tabs.get(info.tabId, function(tab) {
    if (tab && tab.url && tab.url.indexOf('http') === 0) {
      lastUrl[info.tabId] = tab.url;
      sendTab(info.tabId, tab.url, tab.title, tab.audible);
    }
  });
});
api.tabs.onUpdated.addListener(function(tabId, changeInfo, tab) {
  if (changeInfo.audible !== undefined) reportAudible(!!changeInfo.audible);
  if (changeInfo.status === 'complete' && tab && tab.active && tab.url && tab.url.indexOf('http') === 0) {
    lastUrl[tabId] = tab.url;
    sendTab(tabId, tab.url, tab.title, tab.audible);
  }
});
api.tabs.onRemoved.addListener(function(tabId) {
  if (lastUrl[tabId]) {
    const body = { tabId: tabId, browser: BROWSER, _leave: true };
    checkedFetch(LEAVE_API, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
      .catch(function() { enqueue(body); });
  }
  delete lastUrl[tabId];
  if (debounceTimers[tabId]) clearTimeout(debounceTimers[tabId]);
  delete debounceTimers[tabId];
});

sendHeartbeat();
fetchSettings();
flushQueue();
if (api.alarms) {
  api.alarms.create('timelens-heartbeat', { periodInMinutes: 0.5 });
  api.alarms.create('timelens-tab-heartbeat', { periodInMinutes: 0.75 });
  api.alarms.create('timelens-settings', { periodInMinutes: 0.5 });
  api.alarms.create('timelens-flush', { periodInMinutes: 1 });
  api.alarms.onAlarm.addListener(function(alarm) {
    if (alarm.name === 'timelens-heartbeat') sendHeartbeat();
    else if (alarm.name === 'timelens-tab-heartbeat') sendTabHeartbeat();
    else if (alarm.name === 'timelens-settings') fetchSettings();
    else if (alarm.name === 'timelens-flush') flushQueue();
  });
}
