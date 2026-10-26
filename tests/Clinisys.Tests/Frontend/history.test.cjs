const { test } = require('node:test');
const assert = require('node:assert/strict');
const { series } = require('../../../frontend/js/history.js');
test('separate exact units and sort each series chronologically without mutating input', () => {
  const points = [
    { requestedAt: '2026-03-01T00:00:00Z', value: 3, unit: 'mg/dL' },
    { requestedAt: '2026-02-01T00:00:00Z', value: 2, unit: 'mmol/L' },
    { requestedAt: '2026-01-01T00:00:00Z', value: 1, unit: 'mg/dL' },
    { requestedAt: '2026-01-02T00:00:00Z', value: 10, unit: 'MG/DL' }
  ];
  const before = JSON.stringify(points), groups = series(points);
  assert.equal(groups.length, 3);
  assert.deepEqual(groups.find(x => x.unit === 'mg/dL').points.map(p => p.value), [1, 3]);
  assert.equal(groups.find(x => x.unit === 'mmol/L').points.length, 1);
  assert.equal(JSON.stringify(points), before);
});
test('no history produces no chart series', () => assert.deepEqual(series([]), []));
test('single point and same date retain all stored measurements', () => {
  const point = { requestedAt: '2026-01-01T00:00:00Z', value: 0, unit: 'unit', abnormality: 'NotEvaluated' };
  assert.deepEqual(series([point]), [{ unit: 'unit', points: [point] }]);
  assert.equal(series([point, { ...point, value: -1 }])[0].points.length, 2);
});
