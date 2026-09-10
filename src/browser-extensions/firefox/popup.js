const runtime = (typeof browser !== 'undefined' ? browser : chrome).runtime;
const status = document.getElementById('status');
const pair = document.getElementById('pair');
document.getElementById('version').textContent = 'v' + runtime.getManifest().version;

function refreshStatus() {
  runtime.sendMessage({ type: 'timelens-status' }).then(result => {
    pair.classList.toggle('visible', !result || !result.paired);
    status.textContent = !result || !result.paired
      ? 'Not paired. Enter the code shown in the TimeLens Privacy center.'
      : !result.connected
        ? 'Paired · Desktop app unavailable.'
        : result.trackingEnabled ? 'Connected · Browser tracking is enabled.' : 'Connected · Browser tracking is paused.';
  }).catch(() => { status.textContent = 'Extension background service unavailable.'; });
}

document.getElementById('pairButton').addEventListener('click', () => {
  const button = document.getElementById('pairButton');
  const codeInput = document.getElementById('code');
  const code = codeInput.value.replace(/\D/g, '');
  if (code.length !== 8) { status.textContent = 'Enter the complete 8-digit code.'; return; }
  button.disabled = true;
  runtime.sendMessage({ type: 'timelens-pair', code }).then(result => {
    if (!result || !result.ok) throw new Error();
    codeInput.value = '';
    refreshStatus();
  }).catch(() => { status.textContent = 'Pairing failed. Generate a new code and try again.'; })
    .finally(() => { button.disabled = false; });
});
refreshStatus();
