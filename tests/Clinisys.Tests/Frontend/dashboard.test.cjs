const { test } = require('node:test');
const assert = require('node:assert/strict');
const { amount, dailySeries } = require('../../../frontend/js/dashboard.js');
test('financial totals preserve cents beyond JavaScript number precision', () => {
  assert.equal(amount('9999999999999999.99'), '9\u202f999\u202f999\u202f999\u202f999\u202f999,99');
  assert.equal(amount('0.00'), '0,00');
  assert.equal(amount('invalid'), 'Indisponible');
});
test('inclusive period includes zero days and leap day in chronological UTC order', () => {
  assert.deepEqual(dailySeries('2024-02-28', '2024-03-01', [{ date: '2024-03-01', requestCount: 3 }]), [
    { date: '2024-02-28', requestCount: 0 }, { date: '2024-02-29', requestCount: 0 }, { date: '2024-03-01', requestCount: 3 }
  ]);
});
test('single day and empty activity are supported', () => {
  assert.deepEqual(dailySeries('2026-01-01', '2026-01-01', []), [{ date: '2026-01-01', requestCount: 0 }]);
});
test('invalid reversed and oversized periods do not create chart points', () => {
  for (const dates of [['invalid', '2026-01-01'], ['2026-02-01', '2026-01-01'], ['2024-01-01', '2025-01-01']])
    assert.deepEqual(dailySeries(...dates, []), []);
});
