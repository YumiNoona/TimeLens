import './docs.css';
import {
  BellRing, CalendarRange, ChartNoAxesColumn, Compass, createIcons, Database,
  Download, EyeOff, Globe2, KeyRound, LayoutDashboard, OctagonX, PackageCheck,
  Puzzle, RefreshCw, Route, Search, ShieldBan, ShieldCheck, SlidersHorizontal,
  Tags, Wrench
} from 'lucide';

createIcons({
  icons: {
    BellRing, CalendarRange, ChartNoAxesColumn, Compass, Database, Download,
    EyeOff, Globe2, KeyRound, LayoutDashboard, OctagonX, PackageCheck, Puzzle,
    RefreshCw, Route, Search, ShieldBan, ShieldCheck, SlidersHorizontal, Tags,
    Wrench
  }
});

const search = document.querySelector('#docs-search');
const articles = [...document.querySelectorAll('article[data-search]')];
const noResults = document.querySelector('#no-results');
const navLinks = [...document.querySelectorAll('.docs-sidebar nav a')];

search.addEventListener('input', () => {
  const query = search.value.trim().toLowerCase();
  let visible = 0;
  articles.forEach((article) => {
    const content = `${article.dataset.search} ${article.textContent}`.toLowerCase();
    const match = !query || content.includes(query);
    article.hidden = !match;
    if (match) visible += 1;
  });
  noResults.hidden = visible !== 0;
});

const sectionObserver = new IntersectionObserver((entries) => {
  const current = entries
    .filter((entry) => entry.isIntersecting)
    .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
  if (!current) return;
  navLinks.forEach((link) => link.classList.toggle('active', link.hash === `#${current.target.id}`));
}, { rootMargin: '-18% 0px -72% 0px', threshold: 0 });

articles.forEach((article) => sectionObserver.observe(article));
