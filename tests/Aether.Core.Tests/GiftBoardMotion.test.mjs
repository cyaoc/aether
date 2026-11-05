// Run with: node --test tests/Aether.Core.Tests/GiftBoardMotion.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { EVENTS, ambient, arcHead, easeBack, entryMotion, landing, rowKind, sheenSweep, sinceLanding } from '../../src/Aether.Core/Pages/gift-board/motion.mjs';

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

const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-9, `${actual} is not ${expected}`);
// The same place on the rim, a fraction of a lap: 0 and 1 are both where it starts.
const samePlace = (a, b) => near(Math.min(Math.abs(a - b), 1 - Math.abs(a - b)), 0);
const NONE = Infinity;

test("the first card's arcs go round in 6 seconds and the other two cards' in 9", () => {
  for (const t of [0, 1234, 50000]) {
    samePlace(arcHead(0, t + 6000, NONE), arcHead(0, t, NONE));
    samePlace(arcHead(0, t + 3000, NONE), (arcHead(0, t, NONE) + 0.5) % 1);
    for (const rank of [1, 2]) {
      samePlace(arcHead(rank, t + 9000, NONE), arcHead(rank, t, NONE));
      samePlace(arcHead(rank, t + 4500, NONE), (arcHead(rank, t, NONE) + 0.5) % 1);
    }
  }
});

test("a new first place's arcs spin two laps more and are back where they would have been 1600 ms after it lands", () => {
  const t = 20000;
  assert.ok(Math.abs(arcHead(0, t, 400) - arcHead(0, t, NONE)) > 0.1);
  samePlace(arcHead(0, t, 1600), arcHead(0, t, NONE));
  samePlace(arcHead(0, t, 0), arcHead(0, t, NONE));
  // Only the first card spins.
  assert.equal(arcHead(1, t, 400), arcHead(1, t, NONE));
});

test("a row lands among the top three when its move or unfold there ends, and a row that drops or only grows does not", () => {
  // `slots` are the ranks before the event, the second argument the rank the row has now.
  assert.deepEqual(landing({ kind: 'pass', id: 'a', slots: { a: 5 } }, 2), { slot: 2, at: 800 });
  assert.deepEqual(landing({ kind: 'top', id: 'a', slots: { a: 2 } }, 0), { slot: 0, at: 1000 });
  assert.deepEqual(landing({ kind: 'new', id: 'a', slots: {} }, 1), { slot: 1, at: 860 });
  assert.deepEqual(landing({ kind: 'top', debut: true, id: 'a', slots: {} }, 0), { slot: 0, at: 860 });
  assert.equal(landing({ kind: 'pass', id: 'a', slots: { a: 1 } }, 2), undefined);
  assert.equal(landing({ kind: 'more', id: 'a', slots: { a: 1 } }, 1), undefined);
  assert.equal(landing({ kind: 'pass', id: 'a', slots: { a: 7 } }, 3), undefined);
  assert.equal(landing({ kind: 'new', id: 'a', slots: {} }, 4), undefined);
});

test("only the card a row landed in counts the time since, and only once it has landed", () => {
  const landed = { slot: 2, at: 10000 };
  assert.equal(sinceLanding(landed, 2, 10250), 250);
  assert.equal(sinceLanding(landed, 2, 9990), NONE);
  assert.equal(sinceLanding(landed, 1, 10250), NONE);
  assert.equal(sinceLanding(undefined, 0, 10250), NONE);
});

const sweeping = (rank, t, landedAgo = NONE) => { const { progress } = sheenSweep(rank, t, landedAgo); return progress > 0 && progress < 1; };

test("the sheen sweeps the first card every 5 seconds and the other two every 10, at 3.3 and 6.6 seconds", () => {
  for (const lap of [0, 1, 7]) {
    for (const at of [100, 550, 1050]) assert.ok(sweeping(0, lap * 5000 + at), `first at ${lap * 5000 + at}`);
    for (const at of [1150, 3000, 4950]) assert.ok(!sweeping(0, lap * 5000 + at), `first at ${lap * 5000 + at}`);
    const t = lap * 10000;
    for (const at of [3400, 3850, 4350]) assert.ok(sweeping(1, t + at) && !sweeping(2, t + at), `second at ${t + at}`);
    for (const at of [6700, 7150, 7650]) assert.ok(sweeping(2, t + at) && !sweeping(1, t + at), `third at ${t + at}`);
    for (const at of [100, 2000, 5000, 9000]) assert.ok(!sweeping(1, t + at) && !sweeping(2, t + at), `at ${t + at}`);
  }
  assert.deepEqual(sheenSweep(0, 550, NONE), { progress: 0.5, strength: 1 });
});

test("a row landing in a card sweeps it once, faster and brighter, in place of its ordinary sheen", () => {
  // At 2000 the first card has no sheen of its own.
  assert.deepEqual(sheenSweep(0, 2000, 350), { progress: 0.5, strength: 1.6 });
  assert.ok(!sweeping(0, 2000, 700));
});
