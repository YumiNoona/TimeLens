// Single source of truth for both Chrome MV3 and Firefox MV2.
const BROWSER = typeof browser !== 'undefined' && browser.runtime?.id ? 'firefox' : 'chrome';
const api = BROWSER === 'firefox' ? browser : chrome;
const actionApi = api.action || api.browserAction;

const API = 'http://127.0.0.1:47821/api/browser-event';
const AUDIBLE_API = 'http://127.0.0.1:47821/api/audible-status';
const SETTINGS_API = 'http://127.0.0.1:47821/api/settings';
const HEARTBEAT_API = 'http://127.0.0.1:47821/api/extension-heartbeat';
const TAB_HEARTBEAT_API = 'http://127.0.0.1:47821/api/browser-heartbeat';
const DASHBOARD = 'http://127.0.0.1:47821/';
const BLOCKED_PAGE = api.runtime.getURL('blocked.html');
const QUEUE_KEY = 'timelens_queue';
const MAX_QUEUE_SIZE = 500;
const EXTENSION_VERSION = api.runtime.getManifest().version;

// --- Block state ---
let trackingEnabled = true;
let blockingEnabled = false;
let blockedDomains = [];
var ACTIVE_RULE_IDS = [];
var _scheduledRefresh = null;
var _ruleUpdate = Promise.resolve();

function checkedFetch(url, options) {
  return fetch(url, options).then(function(response) {
    if (!response.ok) throw new Error('HTTP ' + response.status);
    return response;
  });
}

// --- Extension heartbeat ---
function sendHeartbeat() {
  checkedFetch(
    HEARTBEAT_API + '?browser=' + encodeURIComponent(BROWSER) +
      '&version=' + encodeURIComponent(EXTENSION_VERSION) + '&ts=' + Date.now(),
    { method: 'POST' }
  ).catch(function() {});
}
sendHeartbeat();

// --- Tab heartbeat: bounds duration miscalculation to ~45s ---
function sendTabHeartbeat() {
  if (!trackingEnabled) return;
  api.tabs.query({ active: true, currentWindow: true }, function(tabs) {
    if (!tabs || !tabs.length || !tabs[0].url || tabs[0].url.indexOf('http') !== 0) return;
    var tab = tabs[0];
    if (!lastUrl[tab.id]) return;
    try {
      var u = new URL(tab.url);
      fetch(TAB_HEARTBEAT_API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          tabId: tab.id,
          domain: u.hostname,
          url: tab.url,
          title: tab.title || '',
          browser: BROWSER
        }),
      }).catch(function() {});
    } catch(e) {}
  });
}

// --- Block rule application ---
function applyBlockRules(domains) {
  blockedDomains = blockingEnabled ? domains : [];
  if (BROWSER === 'chrome') {
    var newRules = [];
    var nextId = 1;
    for (var i = 0; i < blockedDomains.length && newRules.length < 5000; i++) {
      var d = blockedDomains[i];
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
    _ruleUpdate = _ruleUpdate.then(function() {
      return chrome.declarativeNetRequest.getDynamicRules().then(function(existing) {
        var oldIds = existing.map(function(rule) { return rule.id; });
        return chrome.declarativeNetRequest.updateDynamicRules({
          removeRuleIds: oldIds,
          addRules: newRules
        });
      }).then(function() {
        ACTIVE_RULE_IDS = newRules.map(function(rule) { return rule.id; });
      });
    }).catch(function(err) {
      console.error('TimeLens: updateDynamicRules failed:', err.message || err);
    });
  }
}

// --- Firefox: webRequest blocking listener ---
if (BROWSER === 'firefox') {
  browser.webRequest.onBeforeRequest.addListener(
    function(details) {
      if (!blockingEnabled) return;
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
    .then(function(r) {
      if (!r.ok) throw new Error('HTTP ' + r.status);
      return r.json();
    })
    .then(function(s) {
      trackingEnabled = s.trackBrowser !== false;
      blockingEnabled = s.focusMode === true;
      var raw = s.focusBlocklist || '[]';
      try { raw = JSON.parse(raw); } catch { raw = []; }
      if (!Array.isArray(raw)) raw = [];
      var newDomains = [];
      var earliestExpiry = Infinity;
      for (var i = 0; i < raw.length; i++) {
        var entry = raw[i];
        var rawId = (entry && entry.i) || entry;
        if (typeof rawId !== 'string') continue;
        var id = rawId.toLowerCase().trim();
        if (!id || id.endsWith('.exe')) continue;
        id = id.replace(/^https?:\/\//, '').split('/')[0].replace(/^www\./, '').replace(/^\.+|\.+$/g, '');
        if (!id || !/^[a-z0-9.-]+$/.test(id)) continue;
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

// Recover persisted rule IDs on Chrome MV3 service worker restart
(function initBlockState() {
  if (BROWSER === 'chrome') {
    chrome.declarativeNetRequest.getDynamicRules(function(existing) {
      ACTIVE_RULE_IDS = existing.map(function(r) { return r.id; });
      console.log('TimeLens: recovered ' + ACTIVE_RULE_IDS.length + ' existing dynamic rules');
      fetchSettings();
    });
  } else {
    fetchSettings();
  }
})();

// --- Tracking ---
const LEAVE_API = 'http://127.0.0.1:47821/api/browser-leave';
var lastUrl = {};
var debounceTimers = {};

function enqueue(event) {
  api.storage.local.get(QUEUE_KEY, function(result) {
    var queue = result[QUEUE_KEY] || [];
    queue.push(event);
    if (queue.length > MAX_QUEUE_SIZE) queue = queue.slice(queue.length - MAX_QUEUE_SIZE);
    var obj = {};
    obj[QUEUE_KEY] = queue;
    api.storage.local.set(obj);
  });
}

var _flushing = false;
function flushQueue() {
  if (_flushing) return;
  _flushing = true;
  api.storage.local.get(QUEUE_KEY, function(result) {
    var queue = result[QUEUE_KEY];
    if (!queue || queue.length === 0) { _flushing = false; return; }
    api.storage.local.remove(QUEUE_KEY);
    var remaining = queue.length;
    for (var i = 0; i < queue.length; i++) {
      let evt = queue[i];
      var target = evt._leave ? LEAVE_API : API;
      checkedFetch(target, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(evt),
      }).then(function() {
        remaining--;
        if (remaining === 0) _flushing = false;
      }).catch(function() {
        enqueue(evt);
        remaining--;
        if (remaining === 0) _flushing = false;
      });
    }
  });
}

function doSendTab(tabId, url, title, audible) {
  if (!trackingEnabled) return;
  try {
    var u = new URL(url);
    var body = { tabId: tabId, domain: u.hostname, url: url, title: title || '', browser: BROWSER, audible: !!audible };
    checkedFetch(API, {
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
  checkedFetch(AUDIBLE_API, {
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
    if (tab && tab.url && tab.url.indexOf('http') === 0 && lastUrl[info.tabId] !== tab.url) {
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
    if (!tab.active) return; // skip background tabs — only track what you see
    lastUrl[tabId] = tab.url;
    sendTab(tabId, tab.url, tab.title, tab.audible);
  }
});

api.tabs.onRemoved.addListener(function(tabId) {
  if (lastUrl[tabId]) {
    var body = { tabId: tabId, browser: BROWSER, _leave: true };
    checkedFetch(LEAVE_API, {
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

// Alarms wake Chrome MV3 service workers reliably; intervals alone stop when the
// worker is suspended. Firefox supports the same API and benefits from one path.
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
} else {
  setInterval(sendHeartbeat, 30_000);
  setInterval(sendTabHeartbeat, 45_000);
  setInterval(fetchSettings, 15_000);
  setInterval(flushQueue, 60_000);
}
