import { test } from 'node:test';
import assert from 'node:assert/strict';
import { releaseVersion, selectLatestRelease } from '../server/github-release.js';

const assets = ['TimeLens.exe', 'TimeLens-Setup.exe', 'TimeLens-Firefox-Extension.zip', 'SHA256SUMS.txt'];
const release = (tag, extra = {}) => ({
  tag_name: tag,
  draft: false,
  prerelease: false,
  assets: assets.map(name => ({ name })),
  ...extra,
});

test('website and app feed choose the greatest complete stable release', () => {
  const selected = selectLatestRelease([
    release('v7.3.0'),
    release('v7.4.0'),
    release('v8.0.0', { draft: true }),
    release('v9.0.0', { assets: [{ name: 'TimeLens.exe' }] }),
    release('v10.0.0-beta.1'),
  ], 'TimeLens-Setup.exe');
  assert.equal(selected.tag_name, 'v7.4.0');
  assert.equal(releaseVersion(selected.tag_name), '7.4.0');
});

test('prerelease tags are not exposed to stable desktop updaters', () => {
  assert.throws(() => releaseVersion('v7.4.0-beta.1'));
});
