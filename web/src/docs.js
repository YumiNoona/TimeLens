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
const clearSearch = document.querySelector('#clear-search');
const resetSearch = document.querySelector('#reset-search');
const searchStatus = document.querySelector('#search-status');
const contentsToggle = document.querySelector('#contents-toggle');
const navigation = document.querySelector('#docs-navigation');

function filterArticles() {
  const query = search.value.trim().toLowerCase();
  const words = query.split(/\s+/).filter(Boolean);
  let visible = 0;
  articles.forEach((article) => {
    const content = `${article.dataset.search} ${article.textContent}`.toLowerCase();
    const match = words.every(word => content.includes(word));
    article.hidden = !match;
    if (match) visible += 1;
  });
  navLinks.forEach(link => {
    link.hidden = document.getElementById(link.hash.slice(1))?.hidden === true;
  });
  navigation.querySelectorAll('p').forEach(heading => {
    let next = heading.nextElementSibling;
    let hasVisibleLink = false;
    while (next && next.tagName !== 'P') {
      if (!next.hidden) hasVisibleLink = true;
      next = next.nextElementSibling;
    }
    heading.hidden = !hasVisibleLink;
  });
  noResults.hidden = visible !== 0;
  clearSearch.hidden = !query;
  searchStatus.hidden = !query;
  searchStatus.textContent = query ? `${visible} of ${articles.length} chapters match` : '';
}

function clearQuery() {
  search.value = '';
  filterArticles();
}

search.addEventListener('input', filterArticles);
clearSearch.addEventListener('click', () => { clearQuery(); search.focus(); });
resetSearch.addEventListener('click', () => { clearQuery(); search.focus(); });
search.addEventListener('keydown', event => {
  if (event.key === 'Escape') clearQuery();
  if (event.key === 'Enter') {
    const firstMatch = articles.find(article => !article.hidden);
    if (firstMatch) {
      firstMatch.scrollIntoView({ block: 'start' });
      search.blur();
    }
  }
});

contentsToggle.addEventListener('click', () => {
  const expanded = contentsToggle.getAttribute('aria-expanded') !== 'true';
  contentsToggle.setAttribute('aria-expanded', String(expanded));
  navigation.classList.toggle('is-open', expanded);
});

// A shared section link should work even if an earlier search hid its chapter.
function revealLinkedChapter() {
  const chapter = document.getElementById(location.hash.slice(1));
  if (chapter?.matches('article[data-search]') && chapter.hidden) {
    clearQuery();
    chapter.scrollIntoView({ block: 'start' });
  }
}
navLinks.forEach(link => link.addEventListener('click', () => {
  navigation.classList.remove('is-open');
  contentsToggle.setAttribute('aria-expanded', 'false');
}));
window.addEventListener('hashchange', revealLinkedChapter);
revealLinkedChapter();

const sectionObserver = new IntersectionObserver((entries) => {
  const current = entries
    .filter((entry) => entry.isIntersecting)
    .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)[0];
  if (!current) return;
  navLinks.forEach((link) => link.classList.toggle('active', link.hash === `#${current.target.id}`));
}, { rootMargin: '-18% 0px -72% 0px', threshold: 0 });

articles.forEach((article) => sectionObserver.observe(article));
