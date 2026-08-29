const API = 'http://127.0.0.1:47821/api/browser-block-state';
const params = new URLSearchParams(location.search);
const target = params.get('target') || '';
const original = params.get('url') || '';
document.getElementById('target').textContent = target;

async function refresh() {
  try {
    const response = await fetch(API + '?domain=' + encodeURIComponent(target), { cache: 'no-store' });
    const state = await response.json();
    if (state.action !== 'strict') {
      if (original) location.replace(original);
      return;
    }
    const presentation = state.presentation || {};
    document.getElementById('title').textContent = presentation.title || 'Stay focused';
    document.getElementById('message').textContent = presentation.message || 'This website is blocked while Focus Mode is active.';
    if (presentation.imageUrl && !document.querySelector('#visual img')) {
      const image = document.createElement('img');
      image.src = presentation.imageUrl;
      image.alt = '';
      document.getElementById('visual').replaceChildren(image);
    }
  } catch (_) {}
}

refresh();
setInterval(refresh, 1000);
