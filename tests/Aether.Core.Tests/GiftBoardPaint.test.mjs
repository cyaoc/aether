// Run with: node --test tests/Aether.Core.Tests/GiftBoardPaint.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { nameFades } from '../../src/Aether.Core/Pages/gift-board/paint.mjs';

// A name 300 texture pixels wide in a window 140 wide, which shows all of it from 160 along.
test('a name that fits fades at neither edge', () => {
  assert.deepEqual(nameFades(120, 140, 0), { left: 0, right: 0 });
});

test('a name shown from its start fades out at its right edge only', () => {
  assert.deepEqual(nameFades(300, 140, 0), { left: 0, right: 1 });
});

test('a name shown from its middle fades out at both edges', () => {
  assert.deepEqual(nameFades(300, 140, 80), { left: 1, right: 1 });
});

test('the right edge fades less as the end of the name comes within 20 pixels of it', () => {
  assert.deepEqual(nameFades(300, 140, 150), { left: 1, right: 0.5 });
});

test('a name shown to its end fades out at its left edge only', () => {
  assert.deepEqual(nameFades(300, 140, 160), { left: 1, right: 0 });
});
