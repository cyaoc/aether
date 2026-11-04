// Run with: node --test tests/Aether.Core.Tests/GiftBoardMotion.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { EVENTS, ambient, easeBack, entryMotion, rowKind } from '../../src/Aether.Core/Pages/gift-board/motion.mjs';

test('the overshooting ease starts at exactly 0, ends at 1 and never dips below 0', () => {
  // A scale that rounding left slightly below 0 would mirror the sprite for good: Pixi keeps the sign of a sprite's scale.
  assert.equal(easeBack(0), 0);
  assert.ok(Math.abs(easeBack(1) - 1) < 1e-12);
  for (let p = 0; p <= 1; p += 0.01) assert.ok(easeBack(p) >= 0, `at ${p}`);
});

test('a new row builds up piece by piece and is whole before its event ends', () => {
  const start = entryMotion('new', 'rising', 0), end = entryMotion('new', 'rising', EVENTS.new.duration);
  assert.deepEqual([start.open, start.avatarScale, start.nameAlpha, start.countAlpha], [0, 0, 0, 0]);
  assert.deepEqual([end.open, end.avatarScale, end.nameAlpha, end.countAlpha, end.plateAlpha], [1, 1, 1, 1, 0]);
});

test('the mascot cheers only for a new first place and is back to her ordinary pose when it ends', () => {
  assert.equal(ambient({ kind: 'top' }, 1500, 0).cheer, 1);
  assert.equal(ambient({ kind: 'top' }, EVENTS.top.duration, 0).cheer, 0);
  for (const kind of ['new', 'more', 'pass']) assert.equal(ambient({ kind }, 1500, 0).cheer, 0);
  assert.equal(ambient(undefined, 1500, 0).cheer, 0);
});

test('a row that appears in first place is built and made room for as a new row is, and cheered as a new first place', () => {
  const debut = { kind: 'top', debut: true };
  assert.deepEqual([rowKind(debut), rowKind({ kind: 'top' }), rowKind({ kind: 'new' }), rowKind(undefined)], ['new', 'top', 'new', undefined]);
  // The sprite pulses as it fires the capsule that opens the row, and the mascot cheers on the first place's timeline.
  assert.ok(ambient(debut, 220, 0).spritePulse > 0);
  assert.equal(ambient({ kind: 'top' }, 220, 0).spritePulse, 0);
  assert.equal(ambient(debut, 1500, 0).cheer, 1);
});
