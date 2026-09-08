import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizeTimeline, fmtTime } from '../src/TimeLens.Dashboard/src/lib/utils.ts';
const block = (start, end, title = 'A') => ({ startHour: start / 3600, endHour: end / 3600, durationSeconds: end - start, type: 'work', exeName: 'editor.exe', windowTitle: title });
test('timeline preserves short visits and does not fill gaps or replace context', () => {
  const original = [block(0, 2), block(4, 6), block(6, 8, 'B')];
  const result = normalizeTimeline(original);
  assert.equal(result.length, 3);
  assert.equal(result.reduce((sum, item) => sum + item.durationSeconds, 0), 6);
  assert.equal(normalizeTimeline([block(0, 2), block(2, 4)]).length, 1);
  assert.equal(original[0].endHour, 2 / 3600);
  assert.equal(fmtTime(2 / 60), '2s');
});
