// Run with: node --test tests/Aether.Core.Tests/GiftBoardChanges.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { Changes } from '../../src/Aether.Core/Pages/gift-board/changes.mjs';

const row = (id, num = 1) => ({ id, nickname: id, faceUrl: '', giftName: '礼物', iconUrl: '', guardLevel: 0, price: 100, num });
const board = (...rows) => ({ rows, mascot: true, volume: 60 });
const [a, b, c, d] = ['a', 'b', 'c', 'd'].map(id => row(id));
const kinds = changes => { const kinds = []; for (let change; (change = changes.take());) kinds.push(change.kind + (change.id ? ' ' + change.id : '')); return kinds; };
const play = (changes, state) => { changes.receive(state); return kinds(changes); };
const started = (...states) => { const changes = new Changes(); states.forEach(state => changes.receive(state)); changes.take(); return changes; };

test('the first state of a connection is drawn straight', () => {
  const changes = new Changes();
  changes.receive(board(a, b));
  assert.deepEqual(kinds(changes), ['redraw']);
});

test('a new row, more of a row, a row passing one and a row taking first place are the four animations', () => {
  const changes = started(board(a, b, c));
  assert.deepEqual(play(changes, board(a, b, c, d)), ['new d']);
  assert.deepEqual(play(changes, board(a, b, row('c', 2), d)), ['more c']);
  assert.deepEqual(play(changes, board(a, row('c', 3), b, d)), ['pass c']);
  assert.deepEqual(play(changes, board(row('c', 9), a, b, d)), ['top c']);
});

test('the first place adding to its own count is more of the same, not a new first place', () => {
  assert.deepEqual(play(started(board(a, b)), board(row('a', 2), b)), ['more a']);
});

test('a row appearing first is a new row, not a new first place', () => {
  assert.deepEqual(play(started(board(a, b)), board(c, a, b)), ['new c']);
});

test('removing rows, emptying, changing several rows or switching the mascot redraws without animation', () => {
  const changes = started(board(a, b, c));
  const redrawn = [board(a, b), board(), board(a, b, c), board(row('a', 2), row('b', 2), c), { ...board(row('a', 2), row('b', 2), c), mascot: false }];
  for (const state of redrawn) assert.deepEqual(play(changes, state), ['redraw']);
});

test('a state that shows the same board, such as a new volume, plays nothing', () => {
  assert.deepEqual(play(started(board(a, b)), { ...board(a, b), volume: 0 }), []);
});

test('every change carries the state it leads to', () => {
  const changes = started(board(a));
  const next = { ...board(a, b), volume: 30 };
  changes.receive(next);
  assert.equal(changes.take().state, next);
});

test('up to three changes wait their turn in the order they came', () => {
  const changes = started(board(a));
  changes.receive(board(a, b));
  changes.receive(board(a, b, c));
  changes.receive(board(a, b, c, d));
  assert.deepEqual(kinds(changes), ['new b', 'new c', 'new d']);
});

test('more than three waiting changes jump to the latest state, and later ones follow from it', () => {
  const changes = started(board(a));
  const rows = [a, b, c, d, row('e'), row('f')];
  for (let count = 2; count <= 5; count++) changes.receive(board(...rows.slice(0, count)));
  const jump = changes.take();
  assert.deepEqual([jump.kind, jump.state.rows.length, changes.take()], ['redraw', 5, undefined]);
  changes.receive(board(...rows));
  assert.deepEqual(kinds(changes), ['new f']);
});

test('after a new connection the first state is drawn straight again and the old queue is dropped', () => {
  const changes = started(board(a));
  changes.receive(board(a, b));
  changes.reset();
  assert.deepEqual(play(changes, board(a, b, c)), ['redraw']);
});
