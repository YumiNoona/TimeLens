const API = 'http://127.0.0.1:47821/api/browser-event';
const AUDIBLE_API = 'http://127.0.0.1:47821/api/audible-status';
const DASHBOARD = 'http://127.0.0.1:47821/';

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
        browser: 'firefox',
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
      body: JSON.stringify({ audible, browser: 'firefox' }),
    }).catch(() => {});
  } catch {}
}

function checkAudible(tab) {
  if (tab?.audible) reportAudible(true);
}

browser.browserAction.onClicked.addListener(() => {
  browser.tabs.create({ url: DASHBOARD });
});

browser.tabs.onActivated.addListener(({ tabId }) => {
  browser.tabs.get(tabId, (tab) => {
    if (tab?.url && tab.url.startsWith('http')) {
      sendTab(tabId, tab.url, tab.title);
    }
    checkAudible(tab);
  });
});

browser.tabs.onUpdated.addListener((tabId, changeInfo, tab) => {
  if (changeInfo.audible !== undefined) {
    reportAudible(!!changeInfo.audible);
  }
  if (changeInfo.status === 'complete' && tab?.url && tab.url.startsWith('http')) {
    sendTab(tabId, tab.url, tab.title);
  }
});
