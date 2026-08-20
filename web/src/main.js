import './style.css';
import {
  BookOpen, CalendarDays, ChartPie, Clock3, Copy, createIcons,
  Database, Download, Globe2, Heart, HeartHandshake, Keyboard, LayoutDashboard,
  MonitorDot, Puzzle, Shield, X
} from 'lucide';

createIcons({
  icons: {
    BookOpen, CalendarDays, ChartPie, Clock3, Copy, Database,
    Download, Globe2, Heart, HeartHandshake, Keyboard, LayoutDashboard,
    MonitorDot, Puzzle, Shield, X
  }
});

const dialog = document.querySelector('#donate-dialog');
const copyButton = document.querySelector('.copy-upi');
const status = document.querySelector('.upi [role="status"]');
const upiId = 'rushikeshingale2001@okicici';

document.querySelectorAll('.donate-open').forEach((button) => {
  button.addEventListener('click', () => {
    if (!dialog.open) dialog.showModal();
  });
});

document.querySelector('.dialog-close').addEventListener('click', () => dialog.close());
dialog.addEventListener('click', (event) => {
  const box = dialog.getBoundingClientRect();
  const outside = event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom;
  if (outside) dialog.close();
});

copyButton.addEventListener('click', async () => {
  let copied = false;
  try {
    await navigator.clipboard.writeText(upiId);
    copied = true;
  } catch {
    const helper = document.createElement('textarea');
    helper.value = upiId;
    helper.style.cssText = 'position:fixed;opacity:0';
    document.body.appendChild(helper);
    helper.select();
    copied = document.execCommand('copy');
    helper.remove();
  }

  if (copied) {
    copyButton.querySelector('span').textContent = 'COPIED';
    status.textContent = 'UPI ID copied to your clipboard.';
    setTimeout(() => {
      copyButton.querySelector('span').textContent = 'COPY ID';
      status.textContent = 'Scan or copy into any UPI app.';
    }, 2200);
  } else {
    status.textContent = `Copy this ID: ${upiId}`;
  }
});

const revealObserver = new IntersectionObserver((entries) => {
  entries.forEach((entry) => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
      revealObserver.unobserve(entry.target);
    }
  });
}, { threshold: 0.13 });

document.querySelectorAll('.reveal').forEach((element) => revealObserver.observe(element));

const tilt = document.querySelector('[data-tilt]');
const tiltArea = document.querySelector('.hero-object');
if (matchMedia('(pointer: fine) and (prefers-reduced-motion: no-preference)').matches) {
  tiltArea.addEventListener('pointermove', (event) => {
    const rect = tiltArea.getBoundingClientRect();
    const x = (event.clientX - rect.left) / rect.width - .5;
    const y = (event.clientY - rect.top) / rect.height - .5;
    tilt.style.transform = `rotate(${2.5 + x * 1.6}deg) perspective(1100px) rotateX(${-y * 2.4}deg) rotateY(${x * 2.4}deg)`;
  });
  tiltArea.addEventListener('pointerleave', () => {
    tilt.style.transform = 'rotate(2.5deg)';
  });
}
