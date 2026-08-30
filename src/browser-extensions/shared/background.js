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

function hydrateNotifyMedia(response) {
  const presentation = response && response.presentation;
  const mediaUrl = presentation && presentation.mediaUrl;
  if (!presentation || response.action !== 'notify' || !mediaUrl) return Promise.resolve(response);
  if (imageDataCache[mediaUrl]) {
    presentation.mediaDataUrl = imageDataCache[mediaUrl];
    return Promise.resolve(response);
  }
  return checkedFetch(mediaUrl).then(function(mediaResponse) { return mediaResponse.blob(); }).then(function(blob) {
    return blob.arrayBuffer().then(function(buffer) {
      const bytes = new Uint8Array(buffer);
      let binary = '';
      for (let offset = 0; offset < bytes.length; offset += 0x8000) {
        binary += String.fromCharCode.apply(null, bytes.subarray(offset, Math.min(offset + 0x8000, bytes.length)));
      }
      imageDataCache[mediaUrl] = 'data:' + (blob.type || presentation.mediaType || 'image/png') + ';base64,' + btoa(binary);
      presentation.mediaDataUrl = imageDataCache[mediaUrl];
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
  const ROOT_ID = '__timelens_focus_toasts';
  const oldState = window.__timelensFocusState;
  if (oldState && oldState.domain === domain && document.getElementById(ROOT_ID)) {
    oldState.presentation = presentation;
    const oldStack = document.getElementById(ROOT_ID);
    const nextSide = presentation && presentation.position === 'right' ? 'right' : 'left';
    oldStack.style.left = nextSide === 'left' ? '22px' : 'auto';
    oldStack.style.right = nextSide === 'right' ? '22px' : 'auto';
    const nextInterval = Math.max(5, Math.min(86400, Number(presentation && presentation.repeatIntervalSeconds) || 300));
    if (oldState.intervalSeconds !== nextInterval && oldState.addToast) {
      if (window.__timelensFocusPulse) clearInterval(window.__timelensFocusPulse);
      oldState.intervalSeconds = nextInterval;
      window.__timelensFocusPulse = setInterval(oldState.addToast, nextInterval * 1000);
    }
    return;
  }
  clearNotifyToast();

  const stack = document.createElement('aside');
  stack.id = ROOT_ID;
  stack.setAttribute('aria-label', 'TimeLens reminders');
  const side = presentation && presentation.position === 'right' ? 'right' : 'left';
  stack.style.cssText = 'all:initial;position:fixed;' + side + ':22px;bottom:22px;z-index:2147483647;width:min(410px,calc(100vw - 44px));max-height:calc(100vh - 44px);box-sizing:border-box;display:flex;flex-direction:column;gap:10px;overflow:auto;overscroll-behavior:contain;pointer-events:none;scrollbar-width:thin';
  (document.documentElement || document.body).appendChild(stack);
  const state = {
    domain: domain,
    presentation: presentation,
    intervalSeconds: Math.max(5, Math.min(86400, Number(presentation && presentation.repeatIntervalSeconds) || 300)),
    addToast: null
  };
  window.__timelensFocusState = state;

  function addToast() {
    if (!document.getElementById(ROOT_ID)) return;
    const current = state.presentation || {};
    const card = document.createElement('section');
    card.setAttribute('role', 'status');
    card.style.cssText = 'all:initial;position:relative;width:100%;min-height:104px;box-sizing:border-box;padding:17px 44px 17px 17px;border:1px solid rgba(126,215,218,.46);border-left:4px solid #83d7d8;border-radius:16px;background:#11191c;color:#edf6f4;box-shadow:0 18px 46px rgba(0,0,0,.42);font-family:Inter,Segoe UI,sans-serif;display:flex;gap:14px;align-items:center;pointer-events:auto;animation:timelens-in .2s ease-out';
    const mediaSource = current.mediaDataUrl || current.mediaUrl;
    if (mediaSource) {
      const media = current.mediaType && current.mediaType.indexOf('video/') === 0 ? document.createElement('video') : document.createElement('img');
      media.src = mediaSource;
      media.setAttribute('aria-hidden', 'true');
      media.style.cssText = 'width:72px;height:72px;flex:0 0 72px;border:1px solid rgba(255,255,255,.08);border-radius:12px;object-fit:cover;background:#0b1113';
      if (media.tagName === 'VIDEO') { media.autoplay = true; media.muted = true; media.loop = true; media.playsInline = true; }
      media.onerror = function() { media.remove(); };
      card.appendChild(media);
    }
    const copy = document.createElement('div');
    copy.style.cssText = 'min-width:0;flex:1;display:block';
    const eyebrow = document.createElement('div');
    eyebrow.textContent = 'TIMELENS · NOTIFY';
    eyebrow.style.cssText = 'margin:0 0 5px;color:#83d7d8;font:700 10px/1.2 Inter,Segoe UI,sans-serif;letter-spacing:.12em';
    const title = document.createElement('strong');
    title.textContent = current.title || 'Focus Mode';
    title.style.cssText = 'display:block;margin:0 0 4px;color:#f4fbfa;font:650 15px/1.3 Inter,Segoe UI,sans-serif;white-space:normal;overflow-wrap:anywhere';
    const message = document.createElement('div');
    message.textContent = current.message || 'This website is on your focus list.';
    message.style.cssText = 'display:block;color:#b9c8c5;font:12.5px/1.5 Inter,Segoe UI,sans-serif;white-space:normal;overflow-wrap:anywhere';
    copy.append(eyebrow, title, message);
    card.appendChild(copy);
    const close = document.createElement('button');
    close.type = 'button'; close.textContent = '×';
    close.setAttribute('aria-label', 'Dismiss this TimeLens reminder');
    close.style.cssText = 'all:initial;position:absolute;right:11px;top:10px;width:26px;height:26px;border-radius:8px;color:#b8c8c5;background:rgba(255,255,255,.06);cursor:pointer;font:20px/24px Segoe UI,sans-serif;text-align:center;transition:background .15s,color .15s';
    close.onmouseenter = function() { close.style.background = 'rgba(255,255,255,.13)'; close.style.color = '#fff'; };
    close.onmouseleave = function() { close.style.background = 'rgba(255,255,255,.06)'; close.style.color = '#b8c8c5'; };
    close.onclick = function() { card.remove(); };
    card.appendChild(close);
    stack.insertBefore(card, stack.firstChild);
  }
  state.addToast = addToast;

  async function checkState() {
    try {
      const runtime = typeof browser !== 'undefined' ? browser.runtime : chrome.runtime;
      const response = await runtime.sendMessage({ type: 'timelens-check-block', domain: domain });
      if (!response || response.action === 'none') {
        clearInterval(window.__timelensFocusTimer);
        stack.remove();
      } else if (response.action === 'strict') {
        clearInterval(window.__timelensFocusTimer);
        location.href = runtime.getURL('blocked.html') + '?target=' + encodeURIComponent(domain) + '&url=' + encodeURIComponent(location.href);
      }
    } catch (_) {}
  }

  addToast();
  window.__timelensFocusTimer = setInterval(checkState, 2000);
  window.__timelensFocusPulse = setInterval(addToast, state.intervalSeconds * 1000);
}

function clearNotifyToast() {
  const root = document.getElementById('__timelens_focus_toasts');
  if (root) root.remove();
  if (window.__timelensFocusTimer) clearInterval(window.__timelensFocusTimer);
  if (window.__timelensFocusPulse) clearInterval(window.__timelensFocusPulse);
  window.__timelensFocusState = null;
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
      .then(hydrateNotifyMedia)
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
