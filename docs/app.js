const dialog = document.querySelector('#donate-dialog');
const closeButton = dialog?.querySelector('.dialog-close');
const copyButton = dialog?.querySelector('.copy-upi');
const copyStatus = dialog?.querySelector('#copy-status');
const upiId = 'rushikeshingale2001@okicici';

document.querySelectorAll('.donate-trigger').forEach((button) => {
  button.addEventListener('click', () => {
    if (!dialog?.open) dialog?.showModal();
  });
});

if (window.location.hash === '#donate') dialog?.showModal();

closeButton?.addEventListener('click', () => dialog.close());
dialog?.addEventListener('click', (event) => {
  const bounds = dialog.getBoundingClientRect();
  const outside = event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom;
  if (outside) dialog.close();
});

copyButton?.addEventListener('click', async () => {
  let copied = false;
  try {
    await navigator.clipboard.writeText(upiId);
    copied = true;
  } catch {
    const input = document.createElement('textarea');
    input.value = upiId;
    input.setAttribute('readonly', '');
    input.style.cssText = 'position:fixed;opacity:0';
    document.body.appendChild(input);
    input.select();
    copied = document.execCommand('copy');
    input.remove();
  }
  const label = copyButton.querySelector('span');
  if (copied) {
    if (label) label.textContent = 'Copied';
    if (copyStatus) copyStatus.textContent = 'UPI ID copied to your clipboard.';
    window.setTimeout(() => {
      if (label) label.textContent = 'Copy';
      if (copyStatus) copyStatus.textContent = 'Scan the QR or copy the ID into any UPI app.';
    }, 2200);
  } else if (copyStatus) copyStatus.textContent = `Copy this UPI ID: ${upiId}`;
});
