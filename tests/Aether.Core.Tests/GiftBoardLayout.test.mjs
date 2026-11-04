// Run with: node --test tests/Aether.Core.Tests/GiftBoardLayout.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { fitLayout, lapLength, scrolledY, slotGeometry } from '../../src/Aether.Core/Pages/gift-board/layout.mjs';

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
  for (const g of [first, second, third, row]) assert.deepEqual([g.ax, g.nx, g.ix, g.cx], [148, 192, 376, 442]);
  assert.equal(slotGeometry(layout, 4).y, row.y + 60);
});

test('a row copy that has scrolled out of the top for good comes back below its other copy', () => {
  const layout = fitLayout(480, 1000, true), lap = lapLength(13);
  assert.equal(lap, 840);
  assert.equal(scrolledY(layout, lap, 200, 636), 436);
  assert.equal(scrolledY(layout, lap, 500, 636), 1816);
  assert.equal(scrolledY(layout, lap, 500, 636 + lap), 976);
});
