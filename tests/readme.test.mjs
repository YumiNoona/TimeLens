import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
const readmeUrl = new URL('../README.md', import.meta.url);
const readme = readFileSync(readmeUrl, 'utf8');

test('product README stays version-free and links real dashboard screenshots', () => {
  assert.doesNotMatch(readme, /\bv?\d+\.\d+(?:\.\d+)?\b/);
  for (const name of ['today', 'timeline', 'browser']) {
    const path = `docs/screenshots/dashboard-${name}.jpg`;
    assert.ok(readme.includes(path));
    const image = readFileSync(new URL(`../${path}`, import.meta.url));
    assert.equal(image.readUInt16BE(0), 0xffd8);
    assert.ok(image.length > 10_000, `${path} should contain a real screenshot`);
  }
  for (const match of readme.matchAll(/\]\(([^)]+)\)/g)) {
    if (!/^https?:|^#/.test(match[1])) assert.ok(existsSync(new URL(`../${match[1]}`, import.meta.url)), match[1]);
  }
  assert.match(readme, /synthetic example/);
  assert.match(readme, /Android/);
  assert.match(readme, /Firefox.*Chrome/);
});
