// Run with: node --test tests/Aether.Core.Tests/GiftBoardPaint.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { fitText } from '../../src/Aether.Core/Pages/gift-board/paint.mjs';

// A context whose text is as wide as it has UTF-16 units, so that a cut inside a surrogate pair would be easy to make.
const ruler = { measureText: text => ({ width: text.length }) };

test('a name that fits is left as it is', () => {
  assert.equal(fitText(ruler, '月下Lily', 6), '月下Lily');
});

test('a name that is too long ends in an ellipsis that still fits', () => {
  assert.equal(fitText(ruler, 'Yuzu_Sour_Yuzu', 8), 'Yuzu_So…');
});

test('a name is never cut inside an emoji or another character of two UTF-16 units', () => {
  // Cut by UTF-16 units, the width 6 would leave 樱花🌸 and half of the next flower.
  assert.equal(fitText(ruler, '樱花🌸🌸🌸', 6), '樱花🌸…');
  assert.equal(fitText(ruler, '𠮷野家牛丼', 4), '𠮷野…');
});
