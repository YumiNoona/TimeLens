document.getElementById('version').textContent = 'v' + (typeof browser !== 'undefined' ? browser : chrome).runtime.getManifest().version;
fetch('http://127.0.0.1:47821/api/settings', { signal: AbortSignal.timeout(3000) })
  .then(response => { if (!response.ok) throw new Error(); return response.json(); })
  .then(settings => { document.getElementById('status').textContent = settings.trackBrowser === false
    ? 'Connected · Browser tracking is disabled in desktop settings.' : 'Connected · Browser tracking is enabled.'; })
  .catch(() => { document.getElementById('status').textContent = 'Desktop app unavailable. Start TimeLens on this computer, then reopen this popup.'; });
