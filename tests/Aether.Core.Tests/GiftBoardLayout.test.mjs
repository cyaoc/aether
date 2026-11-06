// Run with: node --test tests/Aether.Core.Tests/GiftBoardLayout.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { LAYOUT, RIM_RADIUS, fitLayout, lapLength, rimBox, rimLength, rimPoint, scrolledY, slotGeometry } from '../../src/Aether.Core/Pages/gift-board/layout.mjs';

const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-9, `${actual} is not ${expected}`);

test('480 x 1000 is the recommended size: the mascot, the top three and six rows', () => {
  const layout = fitLayout(480, 1000, true);
  assert.deepEqual([layout.scale, layout.x, layout.top, layout.fit], [1, 0, 220, 6]);
});

test('switching the mascot off lifts the list and adds three rows to the same page', () => {
  const layout = fitLayout(480, 1000, false);
  assert.deepEqual([layout.scale, layout.top, layout.fit], [1, 8, 9]);
});

test('a wider page scales the whole board by its width and keeps the same rows', () => {
  const layout = fitLayout(600, 1250, true);
  assert.deepEqual([layout.scale, layout.x, layout.designHeight, layout.fit], [1.25, 0, 1000, 6]);
});

test('a page shorter than the minimum board scales it down by height and centres it', () => {
  const layout = fitLayout(480, 640, true);
  near(layout.scale, 640 / 820);
  assert.equal(layout.designHeight, 820);
  near(layout.x, (480 - 480 * (640 / 820)) / 2);
  assert.equal(layout.fit, 3);
});

test('without the mascot the same short page needs no scaling', () => {
  const layout = fitLayout(480, 640, false);
  assert.deepEqual([layout.scale, layout.x, layout.fit], [1, 0, 3]);
});

test('the top three and the rows share the columns and differ in size by rank', () => {
  const layout = fitLayout(480, 1000, true);
  const [first, second, third, row] = [0, 1, 2, 3].map(slot => slotGeometry(layout, slot));
  assert.deepEqual([first.y, second.y, third.y, row.y], [312, 422, 524, 636]);
  assert.deepEqual([first.h, second.h, third.h, row.h], [98, 90, 90, 50]);
  assert.deepEqual([first.ar, second.ar, third.ar, row.ar], [33, 30, 30, 19]);
  for (const g of [first, second, third, row]) assert.deepEqual([g.ax, g.nx, g.ix, g.cx], [140, 197, 376, 442]);
  assert.equal(LAYOUT.columns.rank, 54);
  // The first card's 总督 frame reaches about 47 right of the avatar's middle; the name starts 10 past it.
  assert.equal(first.nx - first.ax, 57);
  assert.equal(slotGeometry(layout, 4).y, row.y + 60);
});

test('a row copy that has scrolled out of the top for good comes back below its other copy', () => {
  const layout = fitLayout(480, 1000, true), lap = lapLength(13);
  assert.equal(lap, 840);
  assert.equal(scrolledY(layout, lap, 200, 636), 436);
  assert.equal(scrolledY(layout, lap, 500, 636), 1816);
  assert.equal(scrolledY(layout, lap, 500, 636 + lap), 976);
});

test("the light runs round a card's rim from where the top edge leaves its left corner, clockwise and back", () => {
  const layout = fitLayout(480, 1000, true), box = rimBox(layout, 0), [x, y, w, h] = box, length = rimLength(box);
  // The first card drawn in by 1.2 on every side, with corners of 17.
  assert.deepEqual(box, [21.2, 313.2, 437.6, 95.6]);
  assert.equal(RIM_RADIUS, 17);
  assert.deepEqual(rimPoint(box, 0), [x + 17, y]);
  const back = rimPoint(box, length);
  near(back[0], x + 17);
  near(back[1], y);
  // A little past the start it is further right on the top edge, and a little before it, coming up the left side.
  assert.ok(rimPoint(box, 5)[0] > x + 17);
  assert.ok(rimPoint(box, -5)[0] < x + 17 && rimPoint(box, -5)[1] > y);
});

test("every point on a corner of the rim is 17 from that corner's centre", () => {
  const box = rimBox(fitLayout(480, 1000, true), 1), [x, y, w, h] = box, r = 17, q = Math.PI * r / 2;
  const straight = [w - 2 * r, h - 2 * r];
  const centres = [[x + w - r, y + r], [x + w - r, y + h - r], [x + r, y + h - r], [x + r, y + r]];
  let s = straight[0];
  centres.forEach(([cx, cy], corner) => {
    for (const along of [0.2, 0.5, 0.8]) {
      const [px, py] = rimPoint(box, s + along * q);
      near(Math.hypot(px - cx, py - cy), r);
    }
    s += q + straight[(corner + 1) % 2];
  });
});

test('walking the rim a pixel at a time never jumps', () => {
  for (const rank of [0, 1, 2]) {
    const box = rimBox(fitLayout(480, 1000, true), rank), length = rimLength(box);
    let last = rimPoint(box, 0);
    for (let s = 1; s <= Math.ceil(length); s++) {
      const point = rimPoint(box, s);
      assert.ok(Math.hypot(point[0] - last[0], point[1] - last[1]) <= 1.0001, `rank ${rank} at ${s}`);
      last = point;
    }
  }
});
