// Run with: node --test tests/Aether.Core.Tests/GiftBoardChanges.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { Changes } from '../../src/Aether.Core/Pages/gift-board/changes.mjs';

const row = (id, num = 1) => ({ id, nickname: id, faceUrl: '', giftName: '礼物', iconUrl: '', guardLevel: 0, viewerGuardLevel: 0, price: 100, num });
const board = (...rows) => ({ rows, running: true, options: { threshold: 1, thresholdBy: 1, mascot: true, volume: 60 }, fromGift: false });
const withOptions = (state, options) => ({ ...state, options: { ...state.options, ...options } });
// The state a received gift made: only such a state is played as an event.
const gift = (...rows) => ({ ...board(...rows), fromGift: true });
const [a, b, c, d] = ['a', 'b', 'c', 'd'].map(id => row(id));
const kinds = changes => {
  const kinds = [];
  for (let change; (change = changes.take());) kinds.push(change.kind + (change.id ? ' ' + change.id : '') + (change.debut ? ' debut' : ''));
  return kinds;
};
const play = (changes, state) => { changes.receive(state); return kinds(changes); };
const started = (...states) => { const changes = new Changes(); states.forEach(state => changes.receive(state)); changes.take(); return changes; };

test('the first state of a connection is drawn straight', () => {
  const changes = new Changes();
  changes.receive(gift(a, b));
  assert.deepEqual(kinds(changes), ['redraw']);
});

test('a new row, more of a row, a row passing one and a row taking first place are the four animations', () => {
  const changes = started(board(a, b, c));
  assert.deepEqual(play(changes, gift(a, b, c, d)), ['new d']);
  assert.deepEqual(play(changes, gift(a, b, row('c', 2), d)), ['more c']);
  assert.deepEqual(play(changes, gift(a, row('c', 3), b, d)), ['pass c']);
  assert.deepEqual(play(changes, gift(row('c', 9), a, b, d)), ['top c']);
});

test('the first place adding to its own count is more of the same, not a new first place', () => {
  assert.deepEqual(play(started(board(a, b)), gift(row('a', 2), b)), ['more a']);
});

test('a row that appears in first place is a new first place, on a board with rows and on an empty one', () => {
  assert.deepEqual(play(started(board(a, b)), gift(c, a, b)), ['top c debut']);
  assert.deepEqual(play(started(board()), gift(a)), ['top a debut']);
});

test('removing rows, emptying, changing several rows or switching the mascot redraws without animation', () => {
  const changes = started(board(a, b, c));
  const redrawn = [gift(a, b), gift(), gift(a, b, c), gift(row('a', 2), row('b', 2), c), withOptions(gift(row('a', 2), row('b', 2), c), { mascot: false })];
  for (const state of redrawn) assert.deepEqual(play(changes, state), ['redraw']);
});

test('a state that no gift made is drawn straight, even when it adds, grows or moves just one row', () => {
  const notFromGift = [board(a, b, c, d), board(a, b, row('c', 2)), board(a, row('c', 2), b), board(row('c', 2), a, b), board(d, a, b, c)];
  for (const state of notFromGift) assert.deepEqual(play(started(board(a, b, c)), state), ['redraw']);
  assert.deepEqual(play(started(board()), board(a)), ['redraw']);
});

test('a 期 ended or continued is drawn straight and replaces what waits, though its rows stay the same', () => {
  const changes = started(board(a, b));
  changes.receive(gift(row('a', 2), b));
  assert.equal(changes.receive({ ...board(row('a', 2), b), running: false }), true);
  assert.deepEqual(kinds(changes), ['redraw']);
  assert.equal(changes.receive(board(row('a', 2), b)), true);
  assert.deepEqual(kinds(changes), ['redraw']);
});

test('a new threshold or comparison is drawn straight and replaces what waits, though its rows stay the same', () => {
  for (const options of [{ threshold: 2 }, { thresholdBy: 2 }]) {
    const changes = started(board(a, b));
    changes.receive(gift(row('a', 2), b));
    assert.equal(changes.receive(withOptions(board(row('a', 2), b), options)), true);
    assert.deepEqual(kinds(changes), ['redraw']);
  }
});

test('a state that shows the same board, such as a new volume or a gift below the threshold, plays nothing', () => {
  assert.deepEqual(play(started(board(a, b)), withOptions(board(a, b), { volume: 0 })), []);
  assert.deepEqual(play(started(board(a, b)), gift(a, b)), []);
});

test('every change carries the state it leads to', () => {
  const changes = started(board(a));
  const next = withOptions(gift(a, b), { volume: 30 });
  changes.receive(next);
  assert.equal(changes.take().state, next);
});

test('up to three changes wait their turn in the order they came', () => {
  const changes = started(board(a));
  changes.receive(gift(a, b));
  changes.receive(gift(a, b, c));
  changes.receive(gift(a, b, c, d));
  assert.deepEqual(kinds(changes), ['new b', 'new c', 'new d']);
});

test('more than three waiting changes jump to the latest state, and later ones follow from it', () => {
  const changes = started(board(a));
  const rows = [a, b, c, d, row('e'), row('f')];
  for (let count = 2; count <= 5; count++) changes.receive(gift(...rows.slice(0, count)));
  const jump = changes.take();
  assert.deepEqual([jump.kind, jump.state.rows.length, changes.take()], ['redraw', 5, undefined]);
  changes.receive(gift(...rows));
  assert.deepEqual(kinds(changes), ['new f']);
});

test('a board no gift made replaces whatever waits, and says so, so that what is playing can stop', () => {
  const changes = started(board(a));
  assert.equal(changes.receive(gift(a, b)), false);
  assert.equal(changes.receive(gift(a, b, c)), false);
  assert.equal(changes.receive(board()), true);
  assert.deepEqual(kinds(changes), ['redraw']);
  assert.equal(changes.receive(board(a)), true);
  const reconnected = new Changes();
  assert.equal(reconnected.receive(board(a)), true);
});

test('changes a gift made wait their turn, even a redraw, and a board that looks the same replaces nothing', () => {
  const changes = started(board(a, b));
  assert.equal(changes.receive(gift(a, b, c)), false);
  assert.equal(changes.receive(gift(row('a', 2), row('b', 2), c)), false);
  assert.equal(changes.receive(withOptions(board(row('a', 2), row('b', 2), c), { volume: 0 })), false);
  assert.deepEqual(kinds(changes), ['new c', 'redraw']);
});

test('a state that only brings avatars B站 left out of the gifts is drawn in its turn and cuts nothing off', () => {
  const changes = started(board(a, b));
  assert.equal(changes.receive(gift(a, b, c)), false);
  assert.equal(changes.receive(board(a, b, { ...c, faceUrl: 'face' })), false);
  assert.deepEqual(kinds(changes), ['new c', 'pictures']);
});

test('a gift below the threshold that tells the guard of a viewer on the board is drawn in its turn as pictures', () => {
  const changes = started(board(a, b));
  assert.equal(changes.receive(gift(a, { ...b, viewerGuardLevel: 1 })), false);
  assert.deepEqual(kinds(changes), ['pictures']);
});

test('avatars that keep coming while an event plays wait as one change that is still only pictures', () => {
  const changes = started(board(a, b, c, d));
  changes.receive(gift(a, b, c, row('d', 2)));
  changes.take();
  const faced = n => board(...[a, b, c, row('d', 2)].map((row, i) => i < n ? { ...row, faceUrl: 'face-' + row.id } : row));
  for (let n = 1; n <= 4; n++) assert.equal(changes.receive(faced(n)), false);
  const change = changes.take();
  assert.deepEqual([change.kind, change.state.rows.map(row => row.faceUrl), changes.take()],
    ['pictures', ['face-a', 'face-b', 'face-c', 'face-d'], undefined]);
});

test('avatars between the changes of gifts take no place in the backlog', () => {
  const changes = started(board(a, b, c, d));
  const [c2, d2] = [row('c', 2), row('d', 2)];
  const faced = row => ({ ...row, faceUrl: 'face-' + row.id });
  for (const state of [gift(a, b, c2, d), board(a, b, faced(c2), d), gift(a, b, faced(c2), d2), board(a, b, faced(c2), faced(d2))])
    assert.equal(changes.receive(state), false);
  assert.deepEqual(kinds(changes), ['more c', 'pictures', 'more d', 'pictures']);
});

test('after a new connection the first state is drawn straight again and the old queue is dropped', () => {
  const changes = started(board(a));
  changes.receive(gift(a, b));
  changes.reset();
  assert.deepEqual(play(changes, gift(a, b, c)), ['redraw']);
});
