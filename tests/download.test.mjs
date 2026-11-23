import { test } from 'node:test';
import assert from 'node:assert/strict';
import handler from '../api/download.js';

test('Android downloads select the APK from the complete reset release', async () => {
  const previous = globalThis.fetch;
  const names = ['TimeLens.exe', 'TimeLens-Setup.exe', 'SHA256SUMS.txt', 'TimeLens-Android.apk'];
  const requests = [];
  globalThis.fetch = async url => {
    requests.push(url);
    if (url.includes('/releases?')) return { ok: true, json: async () => [{ tag_name: 'v1.0.0', assets: names.map(name => ({ name, url: 'https://api.github.com/assets/' + name })) }] };
    return { status: 302, headers: new Headers({ location: 'https://release-assets.githubusercontent.com/test.apk' }) };
  };
  const response = { headers: {}, setHeader(name, value) { this.headers[name] = value; }, status(value) { this.code = value; return this; }, end() {}, json(value) { this.body = value; return this; } };
  try {
    await handler({ method: 'GET', query: { platform: 'android' } }, response);
    assert.equal(response.code, 302);
    assert.match(requests.at(-1), /TimeLens-Android\.apk$/);
    assert.equal(response.headers.Location, 'https://release-assets.githubusercontent.com/test.apk');
    await handler({ method: 'GET', query: { platform: '../private' } }, response);
    assert.equal(response.code, 400);
    assert.equal(requests.length, 2, 'Invalid platform names must not reach the download service');
  } finally { globalThis.fetch = previous; }
});
