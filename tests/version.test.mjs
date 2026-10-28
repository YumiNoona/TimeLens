import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
test('desktop, dashboard, website and installer share a release version; Android versions independently', () => {
  const dashboard = JSON.parse(read('src/TimeLens.Dashboard/package.json')).version;
  assert.equal(JSON.parse(read('web/package.json')).version, dashboard);
  assert.match(read('src/TimeLens.TrayApp/TimeLens.TrayApp.csproj'), new RegExp(`<Version>${dashboard.replaceAll('.', '\\.')}</Version>`));
  assert.ok(read('installer/TimeLens.iss').includes(`#define AppVersion "${dashboard}"`));
  assert.match(read('android/app/build.gradle.kts'), /versionName = "\d+\.\d+\.\d+"/);
  assert.match(read('android/app/build.gradle.kts'), /versionCode = \d+/);
  assert.ok(read(`docs/releases/${dashboard}.md`).includes(`TimeLens ${dashboard}`));
  for (const directory of ['web', 'src/TimeLens.Dashboard']) {
    const lock = JSON.parse(read(`${directory}/package-lock.json`));
    assert.equal(lock.version, dashboard);
    assert.equal(lock.packages[''].version, dashboard);
  }
});
