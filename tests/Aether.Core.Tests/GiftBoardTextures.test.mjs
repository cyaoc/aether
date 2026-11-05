// Run with: node --test tests/Aether.Core.Tests/GiftBoardTextures.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { memo } from '../../src/Aether.Core/Pages/gift-board/textures.mjs';

const destroyedWithSource = [];
const make = (...args) => ({ args, destroy: withSource => destroyedWithSource.push(withSource) });

test('a texture is made once for each list of arguments', () => {
  const names = memo(make);
  const first = names('月下Lily', 150);
  assert.equal(names('月下Lily', 150), first);
  assert.notEqual(names('月下Lily', 156), first);
  assert.notEqual(names('Lily', 150), first);
});

test('clearing destroys every texture made, its source too, and the next call makes a new one', () => {
  const counts = memo(make);
  const old = [counts(1), counts(2)];
  destroyedWithSource.length = 0;
  counts.clear();
  assert.deepEqual(destroyedWithSource, [true, true]);
  assert.notEqual(counts(1), old[0]);
});

test('sweeping destroys what was not asked for since the sweep before, so counts that went by do not pile up', () => {
  const counts = memo(make);
  for (let num = 1; num <= 3; num++) counts(num);
  counts.sweep();
  const kept = counts(3);
  destroyedWithSource.length = 0;
  counts.sweep();
  assert.deepEqual(destroyedWithSource, [true, true]);
  assert.equal(counts(3), kept);
});
