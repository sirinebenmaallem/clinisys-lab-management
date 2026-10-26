const { test } = require('node:test');
const assert = require('node:assert/strict');
const { evaluate } = require('../../../frontend/js/result-entry.js');
const reference = { status: 'Applicable', lowerBound: '-2', upperBound: '2' };
test('inclusive bounds and immediate low/normal/high evaluation', () => {
  for (const [value, expected] of [['-2.000001', 'Low'], ['-2', 'Normal'], ['0', 'Normal'], ['2', 'Normal'], ['2.000001', 'High']])
    assert.equal(evaluate(value, reference), expected);
});
test('no unjustified evaluation without a usable reference', () => {
  for (const ref of [null, { status: 'NoReference' }, { status: 'Unavailable' }, { status: 'Applicable', lowerBound: '3', upperBound: '2' }])
    assert.equal(evaluate('1', ref), 'NotEvaluated');
});
test('invalid, empty, excessive precision and lossy JSON numbers are rejected', () => {
  for (const value of ['', ' ', '-', 'NaN', 'Infinity', 'abc', '1.0000001', '1000000000000', '999999999999.999999'])
    assert.equal(evaluate(value, reference), 'Invalid', value);
});
test('negative values, zero and six decimal places remain supported', () => {
  assert.equal(evaluate('-0.000001', reference), 'Normal');
  assert.equal(evaluate('0', reference), 'Normal');
  assert.equal(evaluate('1.123456', reference), 'Normal');
});
