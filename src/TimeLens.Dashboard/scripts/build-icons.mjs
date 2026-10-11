import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const names = new Set();
const aliases = { 'brand-brave':'shield', 'brand-rider':'code', 'brand-intellij':'code', 'brand-pycharm':'brand-python',
  'brand-webstorm':'brand-javascript', 'brand-clion':'code', 'brand-eclipse':'code', 'brand-sublime':'code',
  'brand-vim':'code', 'brand-atom':'atom', 'brand-mattermost':'messages', 'brand-outlook':'mail', 'brand-obs':'video',
  'brand-unreal':'device-gamepad', 'brand-godot':'device-gamepad', 'broom':'brush', 'file-rename':'edit',
  'brand-hashnode':'article', 'brand-confluence':'files', 'brand-linear':'list-check', 'brand-basecamp':'tent',
  'brand-huggingface':'robot', 'brand-netlify':'cloud', 'brand-heroku':'cloud', 'brand-stackblitz':'bolt',
  'brand-canva':'palette', 'brand-codewars':'code', 'brand-exercism':'code', 'sort-alpha':'sort-ascending-letters' };
function scan(path) {
  for (const entry of readdirSync(path, { withFileTypes: true })) {
    const full = resolve(path, entry.name);
    if (entry.isDirectory()) scan(full);
    else if (/\.(svelte|ts)$/.test(entry.name))
      for (const match of readFileSync(full, 'utf8').matchAll(/\bti-([a-z0-9]+(?:-[a-z0-9]+)*)/g)) names.add(match[1]);
  }
}
scan(resolve(root, 'src'));
let css = '/* Generated from only the icons referenced by source. */\n.ti{display:inline-block;width:1em;height:1em;background-color:currentColor;mask:var(--ti-icon) center/contain no-repeat;-webkit-mask:var(--ti-icon) center/contain no-repeat;vertical-align:-.125em;flex-shrink:0}\n';
for (const name of [...names].sort()) {
  if (name === 'chevron') continue; // Layout class, not an icon.
  const asset = aliases[name] || name;
  const filled = asset.endsWith('-filled');
  const svg = readFileSync(resolve(root, 'node_modules/@tabler/icons/icons', filled ? 'filled' : 'outline', `${filled ? asset.slice(0,-7) : asset}.svg`), 'utf8')
    .replace(/width="24" height="24"/, '').replaceAll('currentColor', 'black');
  css += `.ti-${name}{--ti-icon:url("data:image/svg+xml,${encodeURIComponent(svg)}")}\n`;
}
writeFileSync(resolve(root, 'src/icons.css'), css);
console.log(`Bundled ${names.size} SVG icons, no icon fonts.`);
