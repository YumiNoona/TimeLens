// Single source of truth for both Chrome MV3 and Firefox MV2.
const BROWSER = typeof browser !== 'undefined' && browser.runtime?.id ? 'firefox' : 'chrome';
const api = BROWSER === 'firefox' ? browser : chrome;
const actionApi = api.action || api.browserAction;

const API = 'http://127.0.0.1:47821/api/browser-event';
const AUDIBLE_API = 'http://127.0.0.1:47821/api/audible-status';
const SETTINGS_API = 'http://127.0.0.1:47821/api/settings';
const HEARTBEAT_API = 'http://127.0.0.1:47821/api/extension-heartbeat';
const DASHBOARD = 'http://127.0.0.1:47821/';
const BLOCKED_PAGE = api.runtime.getURL('blocked.html');
const QUEUE_KEY = 'timelens_queue';

// --- Block state ---
let trackingEnabled = true;
let blockedDomains = [];
var ACTIVE_RULE_IDS = [];
var _scheduledRefresh = null;

// --- Heartbeat ---
function sendHeartbeat() {
  fetch(HEARTBEAT_API + '?ts=' + Date.now(), { method: 'POST' }).catch(function() {});
}
setInterval(sendHeartbeat, 30_000);
sendHeartbeat();

// --- Block rule application ---
function applyBlockRules(domains) {
  blockedDomains = domains;
  if (BROWSER === 'chrome') {
    var oldIds = ACTIVE_RULE_IDS.slice();
    var newRules = [];
    var nextId = 1;
    for (var i = 0; i < domains.length; i++) {
      var d = domains[i];
      newRules.push({
        id: nextId++,
        priority: 1,
        action: { type: 'redirect', redirect: { extensionPath: '/blocked.html' } },
        condition: {
          urlFilter: '||' + d.domain + '^',
          resourceTypes: ['main_frame', 'sub_frame']
        }
      });
    }
    ACTIVE_RULE_IDS = newRules.map(function(r) { return r.id; });
    chrome.declarativeNetRequest.updateDynamicRules({
      removeRuleIds: oldIds,
      addRules: newRules
    }).catch(function() {
      if (newRules.length > 5000) {
        chrome.declarativeNetRequest.updateDynamicRules({
          removeRuleIds: oldIds,
          addRules: newRules.slice(0, 5000)
        }).catch(function() {});
      }
    });
  }
}

// --- Firefox: webRequest blocking listener ---
if (BROWSER === 'firefox') {
  browser.webRequest.onBeforeRequest.addListener(
    function(details) {
      if (!trackingEnabled) return;
      try {
        var url = new URL(details.url);
        var host = url.hostname.toLowerCase();
        for (var i = 0; i < blockedDomains.length; i++) {
          var b = blockedDomains[i];
          var bd = b.domain.toLowerCase().replace(/^\./, '');
          if (host === bd || host.endsWith('.' + bd)) {
            if (b.until && Date.now() >= b.until) continue;
            return { redirectUrl: BLOCKED_PAGE };
          }
        }
      } catch(e) {}
      return;
    },
    { urls: ['<all_urls>'], types: ['main_frame', 'sub_frame'] },
    ['blocking']
  );
}

// --- Settings + Blocklist polling ---
function fetchSettings() {
  if (_scheduledRefresh) { clearTimeout(_scheduledRefresh); _scheduledRefresh = null; }
  fetch(SETTINGS_API)
    .then(function(r) { return r.json(); })
    .then(function(s) {
      trackingEnabled = s.trackBrowser !== false;
      var raw = s.focusBlocklist || '[]';
      try { raw = JSON.parse(raw); } catch { raw = []; }
      if (!Array.isArray(raw)) raw = [];
      var newDomains = [];
      var earliestExpiry = Infinity;
      for (var i = 0; i < raw.length; i++) {
        var entry = raw[i];
        var id = (entry && entry.i) || entry;
        if (typeof id !== 'string') continue;
        if (id.indexOf('.exe') !== -1) continue;
        if (entry && entry.m === 't' && entry.e) {
          var exp = new Date(entry.e).getTime();
          if (Date.now() >= exp) continue;
          if (exp < earliestExpiry) earliestExpiry = exp;
        }
        newDomains.push({ domain: id, until: (entry && entry.m === 't') ? new Date(entry.e).getTime() : null });
      }
      applyBlockRules(newDomains);
      if (earliestExpiry < Infinity) {
        var delay = Math.max(0, earliestExpiry - Date.now()) + 100;
        _scheduledRefresh = setTimeout(fetchSettings, delay);
      }
    })
    .catch(function() {});
}
fetchSettings();
setInterval(fetchSettings, 15_000);

// --- Tracking ---
const LEAVE_API = 'http://127.0.0.1:47821/api/browser-leave';
var lastUrl = {};
var debounceTimers = {};

function enqueue(event) {
  api.storage.local.get(QUEUE_KEY, function(result) {
    var queue = result[QUEUE_KEY] || [];
    queue.push(event);
    var obj = {};
    obj[QUEUE_KEY] = queue;
    api.storage.local.set(obj);
  });
}

function flushQueue() {
  api.storage.local.get(QUEUE_KEY, function(result) {
    var queue = result[QUEUE_KEY];
    if (!queue || queue.length === 0) return;
    api.storage.local.remove(QUEUE_KEY);
    for (var i = 0; i < queue.length; i++) {
      var evt = queue[i];
      var target = evt._leave ? LEAVE_API : API;
      fetch(target, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(evt),
      }).catch(function() { enqueue(evt); });
    }
  });
}

function doSendTab(tabId, url, title, audible) {
  if (!trackingEnabled) return;
  try {
    var u = new URL(url);
    var body = { tabId: tabId, domain: u.hostname, url: url, title: title || '', browser: BROWSER, audible: !!audible };
    fetch(API, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
      .then(function(r) { return r.json(); })
      .then(function(resp) {
        if (resp && resp.blocked) {
          api.tabs.update(tabId, { url: BLOCKED_PAGE });
        }
        flushQueue();
      })
      .catch(function() { enqueue(body); });
  } catch(e) {}
}

function sendTab(tabId, url, title, audible) {
  if (!trackingEnabled) return;
  if (debounceTimers[tabId]) clearTimeout(debounceTimers[tabId]);
  debounceTimers[tabId] = setTimeout(function() {
    delete debounceTimers[tabId];
    doSendTab(tabId, url, title, audible);
  }, 1000);
}

function reportAudible(audible) {
  if (!trackingEnabled) return;
  fetch(AUDIBLE_API, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ audible: audible, browser: BROWSER }),
  }).catch(function() {});
}

// --- Event listeners ---
actionApi.onClicked.addListener(function() {
  api.tabs.create({ url: DASHBOARD });
});

api.tabs.onActivated.addListener(function(info) {
  api.tabs.get(info.tabId, function(tab) {
    if (tab && tab.url && tab.url.indexOf('http') === 0) {
      lastUrl[info.tabId] = tab.url;
      sendTab(info.tabId, tab.url, tab.title, tab.audible);
    }
  });
});

api.tabs.onUpdated.addListener(function(tabId, changeInfo, tab) {
  if (changeInfo.audible !== undefined) {
    reportAudible(!!changeInfo.audible);
  }
  if (changeInfo.status === 'complete' && tab && tab.url && tab.url.indexOf('http') === 0 && lastUrl[tabId] !== tab.url) {
    lastUrl[tabId] = tab.url;
    sendTab(tabId, tab.url, tab.title, tab.audible);
  }
});

api.tabs.onRemoved.addListener(function(tabId) {
  if (lastUrl[tabId]) {
    var body = { tabId: tabId, _leave: true };
    fetch(LEAVE_API, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }).catch(function() { enqueue(body); });
  }
  delete lastUrl[tabId];
  if (debounceTimers[tabId]) {
    clearTimeout(debounceTimers[tabId]);
    delete debounceTimers[tabId];
  }
});

flushQueue();
