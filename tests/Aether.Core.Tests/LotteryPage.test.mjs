// Run with: node --test tests/Aether.Core.Tests/LotteryPage.test.mjs
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';

const script = readFileSync(new URL('../../src/Aether.Core/LotteryPage.html', import.meta.url), 'utf8').match(/<script>([\s\S]*?)<\/script>/)[1];
function page() {
  let now = Date.parse('2026-10-17T12:00:00Z'), nextId = 0;
  const timers = new Map(), elements = new Map(), listeners = new Map();
  const document = { getElementById: id => {
    if (!elements.has(id)) elements.set(id, { textContent: '', hidden: true });
    return elements.get(id);
  }};
  const timer = (callback, delay, repeat) => {
    const id = ++nextId;
    timers.set(id, { callback, at: now + delay, repeat });
    return id;
  };
  vm.runInNewContext(script, {
    document, Date: class extends Date { static now() { return now; } },
    EventSource: class { addEventListener(name, fn) { listeners.set(name, fn); } },
    setTimeout: (fn, ms) => timer(fn, ms, 0), setInterval: (fn, ms) => timer(fn, ms, ms),
    clearTimeout: id => timers.delete(id), clearInterval: id => timers.delete(id),
  });
  return {
    element: id => document.getElementById(id),
    state: state => listeners.get('state')({ data: JSON.stringify(state) }),
    advance: ms => {
      const end = now + ms;
      while (true) {
        const due = [...timers].filter(([, t]) => t.at <= end).sort((a, b) => a[1].at - b[1].at)[0];
        if (!due) break;
        const [id, t] = due;
        now = t.at;
        if (t.repeat) t.at += t.repeat; else timers.delete(id);
        t.callback();
      }
      now = end;
    },
  };
}
const award = { name: '一等奖', prize: '键盘', capacity: 8, grade: 'Grand' };
const entry = { id: 'entry', phase: 'Open', keyword: '<img onerror=alert(1)>', entryCount: 7,
  latestEntries: [{ nickname: '甲' }, { nickname: '乙' }, { nickname: '丙' }], awards: [award], draws: [], remaining: 8, hidden: false, drawUntil: null };
const draws = Array.from({ length: 7 }, (_, index) => ({ id: `d${index}`, awardIndex: 0, award,
  winner: { uid: index + 1, nickname: `观众${index + 1}` }, candidates: ['甲', '乙'], drawnAt: '2026-10-17T12:00:00Z' }));

test('registration, closed and ended snapshots render and winners rotate every five seconds', () => {
  const p = page();
  p.state({ entryLottery: entry });
  assert.equal(p.element('result').hidden, false);
  assert.equal(p.element('prize').textContent, '发弹幕「<img onerror=alert(1)>」');
  assert.match(p.element('details').textContent, /最新报名：甲、乙、丙/);
  p.state({ entryLottery: { ...entry, phase: 'Closed', draws, remaining: 1 } });
  assert.equal(p.element('grade').textContent, '目前的中奖名单');
  assert.match(p.element('viewer').textContent, /还剩 1 个名额/);
  assert.equal(p.element('details').textContent.split('\n').length, 6);
  assert.doesNotMatch(p.element('details').textContent, /观众7/);
  p.advance(4999);
  assert.equal(p.element('prize').textContent, '1 / 2');
  p.advance(1);
  assert.equal(p.element('details').textContent, '一等奖 · 键盘 · 观众7');
  p.state({ entryLottery: { ...entry, phase: 'Ended', draws } });
  assert.equal(p.element('grade').textContent, '完整中奖名单');
  p.state({ entryLottery: { ...entry, phase: 'Ended', draws, hidden: true } });
  assert.equal(p.element('result').hidden, true);
  p.advance(5000);
  p.state({ entryLottery: { ...entry, phase: 'Ended', draws } });
  assert.equal(p.element('result').hidden, false);
  p.state({ entryLottery: { ...entry, phase: 'Preparing' } });
  assert.equal(p.element('result').hidden, true);
});

test('draw reveals winner, ends at seven seconds, and a reconnect uses the remaining deadline', () => {
  const p = page();
  const state = { entryLottery: { ...entry, phase: 'Closed', draws: [draws[0]], drawUntil: '2026-10-17T12:00:07Z' } };
  p.state(state);
  assert.equal(p.element('prize').textContent, '甲');
  p.advance(1800);
  assert.equal(p.element('prize').textContent, '观众1');
  p.state(state);
  p.advance(5199);
  assert.equal(p.element('grade').textContent, '一等奖 · 大奖');
  p.advance(1);
  assert.equal(p.element('grade').textContent, '目前的中奖名单');
  const late = page();
  late.advance(6000);
  late.state(state);
  assert.equal(late.element('prize').textContent, '观众1');
  late.advance(1000);
  assert.equal(late.element('grade').textContent, '目前的中奖名单');
});

test('switching to an idle pool clears entry content and its page timer', () => {
  const p = page();
  p.state({ entryLottery: { ...entry, phase: 'Ended', draws } });
  p.state({ lottery: { id: 'pool', phase: 'Idle', draws: [] } });
  assert.equal(p.element('result').hidden, true);
  p.advance(5000);
  assert.equal(p.element('details').textContent, '');
});
