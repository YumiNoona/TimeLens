const runtime = browser.runtime;
const params = new URLSearchParams(location.search);
const target = params.get('target') || '';
const original = params.get('url') || '';
document.getElementById('target').textContent = target;

async function refresh() {
  try {
    const state = await runtime.sendMessage({ type: 'timelens-check-block', domain: target });
    if (state.action !== 'strict') {
      if (original) location.replace(original);
      else clearInterval(refreshTimer);
      return;
    }
    const presentation = state.presentation || {};
    document.getElementById('title').textContent = presentation.title || 'Stay focused';
    document.getElementById('message').textContent = presentation.message || 'This website is blocked while Focus Mode is active.';
    const media = presentation.mediaDataUrl || presentation.imageUrl;
    if (media && !document.querySelector('#visual img')) {
      const image = document.createElement('img');
      image.src = media;
      image.alt = '';
      document.getElementById('visual').replaceChildren(image);
    }
  } catch (_) {}
}

refresh();
const refreshTimer = setInterval(refresh, 2000);
window.addEventListener('pagehide', () => clearInterval(refreshTimer), { once: true });
