const API = 'http://127.0.0.1:47821/api/browser-event';
const AUDIBLE_API = 'http://127.0.0.1:47821/api/audible-status';
const DASHBOARD = 'http://127.0.0.1:47821/';

const lastUrl = {};

function sendTab(tabId, url, title) {
  try {
    const u = new URL(url);
    fetch(API, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        domain: u.hostname,
        url: url,
        title: title || '',
        browser: 'chrome',
        audible: false,
      }),
    }).catch(() => {});
  } catch {}
}

function reportAudible(audible) {
  try {
    fetch(AUDIBLE_API, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ audible, browser: 'chrome' }),
    }).catch(() => {});
  } catch {}
}

function checkAudible(tab) {
  if (tab?.audible) reportAudible(true);
}

chrome.action.onClicked.addListener(() => {
  chrome.tabs.create({ url: DASHBOARD });
});

chrome.tabs.onActivated.addListener(({ tabId }) => {
  chrome.tabs.get(tabId, (tab) => {
    if (tab?.url && tab.url.startsWith('http')) {
      lastUrl[tabId] = tab.url;
      sendTab(tabId, tab.url, tab.title);
    }
    checkAudible(tab);
  });
});

chrome.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.audible !== undefined) {
    reportAudible(!!changeInfo.audible);
  }
  if (changeInfo.status === 'complete' && tab?.url && tab.url.startsWith('http') && lastUrl[tabId] !== tab.url) {
    lastUrl[tabId] = tab.url;
    sendTab(tabId, tab.url, tab.title);
  }
});

chrome.tabs.onRemoved.addListener((tabId) => {
  delete lastUrl[tabId];
});
