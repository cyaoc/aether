// Run with: node --test tests/Aether.Core.Tests/DesktopPetEngine.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { DAILY, Engine, seeded } from '../../src/Aether.Core/Pages/desktop-pet/engine.mjs';
import { SETTINGS } from './Support/DesktopPetSettings.mjs';
import { box, gifts, item, playing, round, state } from './Support/DesktopPetGame.mjs';

const read = path => JSON.parse(readFileSync(new URL('../../src/Aether.Core/Pages/' + path, import.meta.url), 'utf8'));
const { shows } = read('desktop-pet/shows.json'), { lines } = read('characters/1/character.json');
const MINUTE = 60000;

// A 桌宠页 connected to a room, its clock at 0.
function pet(settings = {}, rng = seeded(7)) {
  const engine = new Engine({ currentTime: 0 }, shows);
  engine.reset(rng);
  engine.receive({ enabled: true, roomConnected: true, settings: { ...SETTINGS, ...settings }, lines });
  return engine;
}
const advance = (engine, ms) => { engine.master.currentTime += ms; engine.update(engine.now()); };
const boxResult = (uid, spend, openedValue, bestPrice = 9000) => ({ uid, nickname: '观众' + uid, tally: { spend, openedValue, outcome: ['Loss', 'Even', 'Gain'][Math.sign(openedValue - spend) + 1] }, boxes: 1, bestGift: '棉花糖', bestPrice });
// A viewer's blind box opening a castle, the only way B站 gives one, and its 盲盒播报.
const castle = (engine, uid) => {
  engine.gift(box(uid, 1, { castle: true }));
  engine.blindBox(boxResult(uid, 15000, 2233000, 2233000));
};
// The lottery on air, as the 桌宠页's state carries it (LotteryState.cs), and the lottery's steps she plays and has queued.
const onAir = (engine, lottery) => engine.receive(state(engine, { lottery }));
const entryLottery = so => ({ mode: 'Entry', id: 'E1', keyword: '来了', phase: 'Open', entryCount: 3, draws: [], drawUntil: null, ...so });
const steps = engine => [engine.cur, ...engine.queue].filter(Boolean).map(show => show.opts.result);
// The 礼物榜 while its 期 runs, rows [uid, count] of one gift each from the first place down, as the 桌宠页's state carries it
// (GiftBoardState.cs); null while it is not counting.
const ranks = (engine, rows, fromGift = true) => engine.receive(state(engine, {
  giftBoard: rows && { rows: rows.map(([uid, num]) => ({ id: `${uid}/gift:1`, uid, nickname: '观众' + uid, faceUrl: '', guardLevel: 0, viewerGuardLevel: 0, num })),
    options: { threshold: 100 }, running: true, fromGift } }));

// What she says after so many minutes of a quiet room, then again each of ms later, with a kind of her lines these and
// every draw in the middle: of three lines the second, of two the second, and the next rather than the same again.
function quietLines(minutes, kind, list, ms) {
  const engine = pet({}, () => 0.5);
  engine.receive(state(engine, { lines: { ...lines, [kind]: list } }));
  advance(engine, minutes * MINUTE);
  engine.stateAt(engine.now());
  return ms.map(step => { advance(engine, step); return engine.stateAt(engine.now()).bubble?.text; });
}

test('a gift round plays at once and moves up at the fifth gift, 1000 电池 and 1 万, never playing one twice', () => {
  const engine = pet();
  engine.gift(gifts(1, 100));
  const thanks = engine.cur;
  assert.equal(playing(engine), 'gift');
  engine.gift(gifts(1, 100));
  assert.equal(engine.cur, thanks);
  engine.gift(gifts(1, 300, 3));
  assert.equal(playing(engine), 'giftStreak');
  engine.gift(gifts(1, 99500));
  assert.equal(playing(engine), 'midGift');
  engine.gift(gifts(1, 900000));
  assert.equal(playing(engine), 'bigGift');
  advance(engine, 6000);
  engine.gift(gifts(1, 100));
  assert.equal(playing(engine), null);
});

test('a higher tier cuts a lower one short, the same tier queues up to three and C is dropped while she is busy', () => {
  const engine = pet();
  engine.gift(gifts(1, 100000));
  engine.gift(gifts(2, 1000000));
  assert.equal(playing(engine), 'bigGift');
  for (const uid of [3, 4, 5, 6]) engine.gift(gifts(uid, 1000000));
  engine.gift(gifts(7, 100000));
  engine.gift(gifts(8, 100));
  assert.deepEqual(engine.queue.map(q => q.name), ['bigGift', 'bigGift', 'bigGift', 'midGift']);
  advance(engine, 6000);
  assert.equal(engine.cur.opts.nickname, '观众3');
});

test('a gift round reaching five gifts takes over its own thank-you, not another viewer\'s', () => {
  const engine = pet();
  engine.gift(gifts(1, 100));
  engine.gift(gifts(2, 500, 5));
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['gift', '观众1']);
});

// A round's gift priced highest and how many of it, as Core's 一轮送礼 (GiftRoundFeature) has them: by its price, not by what
// was paid for it, and counted by its gift id, not its name.
test('a round thanks for the gift priced highest though it was paid less for, and counts it by its gift id', () => {
  const discounted = pet();
  discounted.gift(round(1, { items: [item(800, { giftId: 31164, name: '告白气球' }), item(500, { giftId: 31170, name: '小电视飞船', price: 1000 })] }));
  const alike = pet();
  alike.gift(round(1, { items: [item(200, { count: 2 }), item(200, { giftId: 31037, count: 2 })] }));
  assert.deepEqual([discounted.cur.opts.gift, alike.cur.opts.gift, alike.cur.opts.count], ['小电视飞船', '小花花', 2]);
});

test('an entry plays only while she is idle, over no show nor 日常小动作, and does not queue', () => {
  const engine = pet();
  engine.entry({ uid: 1, nickname: '进场观众' });
  assert.equal(playing(engine), 'entry');
  advance(engine, 1600);
  engine.gift(gifts(1, 100));
  engine.entry({ uid: 2, nickname: '进场观众' });
  assert.deepEqual([playing(engine), engine.queue], ['gift', []]);
  advance(engine, 2000);
  advance(engine, 40000);
  assert.ok(['hum', 'gym', 'hike', 'bike', 'ski'].includes(playing(engine)));
  const daily = engine.cur;
  engine.entry({ uid: 3, nickname: '进场观众' });
  assert.equal(engine.cur, daily);
});

test('her line says a viewer\'s whole nickname, however long', () => {
  const engine = pet(), nickname = '星河一只很长的昵称啊啊啊啊长长长长长长长';
  engine.entry({ uid: 1, nickname });
  advance(engine, 500);
  const text = engine.stateAt(engine.now()).bubble.text;
  assert.ok(text.includes(nickname));
  // her line as written: no … put in for a cut, though her entry lines have their own
  assert.ok(lines.entry.includes(text.replace(nickname, '{昵称}')));
});

test('an entry does not play while she shakes a round\'s blind boxes', () => {
  const engine = pet();
  engine.gift(box(1));
  engine.entry({ uid: 2, nickname: '进场观众' });
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'shakeBox']);
});

test('开心 counts gifts, blind boxes and 大航海, not entries', () => {
  const engine = pet();
  for (const uid of [1, 2, 3]) engine.entry({ uid, nickname: '进场观众' });
  assert.equal(engine.mood(engine.now()), 'calm');
  engine.gift(gifts(1, 100));
  engine.gift(box(2));
  engine.guard({ uid: 3, nickname: '舰长', guardLevel: 3 });
  assert.equal(engine.mood(engine.now()), 'happy');
  advance(engine, MINUTE);
  assert.equal(engine.mood(engine.now()), 'calm');
});

test('犯困 comes after an hour with nothing and ends on an entry or a danmaku; an entry wakes her before it plays', () => {
  const engine = pet();
  advance(engine, 60 * MINUTE);
  assert.equal(engine.mood(engine.now()), 'sleepy');
  engine.danmaku({ uid: 1, nickname: '观众1', joins: false });
  assert.equal(engine.mood(engine.now()), 'gloomy');
  advance(engine, 60 * MINUTE);
  engine.entry({ uid: 1, nickname: '进场观众' });
  assert.deepEqual([playing(engine), engine.cur.pre], ['entry', 1000]);
  assert.equal(engine.mood(engine.now()), 'gloomy');
});

test('a 心情 says a line at random every 15 s, never the same one twice in a row, and keeps it while she says it', () => {
  assert.deepEqual(quietLines(16, 'bored', ['甲', '乙', '丙'], [1000, 3000, 12000]), ['乙', '乙', '丙']);
});

test('jolting awake from 犯困 every half minute she says a line of its own kind, the one left when only one is', () => {
  const engine = pet();
  engine.receive(state(engine, { lines: { ...lines, jolt: ['才没睡！'] } }));
  advance(engine, 60 * MINUTE);
  engine.stateAt(engine.now());
  advance(engine, 23500);
  assert.equal(engine.stateAt(engine.now()).bubble?.text, '才没睡！');
});

test('jolting awake she says a line of its kind at random, never the same one twice in a row, and keeps it while she says it', () => {
  assert.deepEqual(quietLines(60, 'jolt', ['甲', '乙'], [23100, 1200, 28800, 1200]), ['乙', '乙', '甲', '甲']);
});

test('the 心情 come after the minutes set, and never for 0', () => {
  const engine = pet({ boredMinutes: 5, gloomyMinutes: 0, sleepyMinutes: 0 });
  advance(engine, 4 * MINUTE);
  assert.equal(engine.mood(engine.now()), 'calm');
  advance(engine, MINUTE);
  assert.equal(engine.mood(engine.now()), 'bored');
  advance(engine, 600 * MINUTE);
  assert.equal(engine.mood(engine.now()), 'bored');
});

test('without a room she stays calm, and her quiet-room clocks start when one connects', () => {
  const engine = pet();
  engine.receive(state(engine, { roomConnected: false }));
  advance(engine, 120 * MINUTE);
  assert.equal(engine.mood(engine.now()), 'calm');
  engine.receive(state(engine));
  advance(engine, 14 * MINUTE);
  assert.equal(engine.mood(engine.now()), 'calm');
});

test('blind boxes are shaken until their result comes, and the show opens them then, sized by what they cost', () => {
  const engine = pet();
  engine.gift(box(1, 2));
  engine.gift(round(1, { ended: true }));
  advance(engine, 30000);
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'shakeBox']);
  engine.blindBox({ ...boxResult(1, 30000, 18000), boxes: 2 });
  assert.deepEqual([playing(engine), engine.cur.opts.result, engine.stateAt(engine.now()).pose], ['blindBoxSmall', 'Loss', 'openBox']);
  assert.equal(engine.rounds.length, 0);

  advance(engine, 3000);
  engine.blindBox(boxResult(2, 150000, 150000));
  assert.deepEqual([playing(engine), engine.cur.opts.result, engine.stateAt(engine.now()).pose], ['blindBoxMedium', 'Even', 'shakeBox']);
  advance(engine, 4000);
  engine.blindBox(boxResult(3, 15000, 1245000, 1245000));
  assert.deepEqual([playing(engine), engine.cur.opts.result], ['blindBoxLarge', 'Jackpot']);
});

test('one 盲盒播报 opens the boxes of every 一轮送礼 sent before it went out; boxes sent while it went out wait for their own', () => {
  const engine = pet();
  for (const n of [2, 1]) {
    engine.gift(box(1, n));
    engine.gift(round(1, { ended: true }));
  }
  engine.gift(box(1));
  engine.blindBox({ ...boxResult(1, 45000, 30000), boxes: 3 });
  assert.equal(playing(engine), 'blindBoxSmall');
  advance(engine, 30000);
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'shakeBox']);

  engine.gift(round(1, { ended: true }));
  engine.blindBox(boxResult(1, 15000, 9000));
  assert.deepEqual([playing(engine), engine.rounds], ['blindBoxSmall', []]);
});

test('a castle opened while an earlier 盲盒播报 went out plays once, with the result that opens it', () => {
  const engine = pet();
  engine.gift(box(1, 2));
  engine.gift(box(1, 1, { castle: true }));
  engine.blindBox({ ...boxResult(1, 30000, 18000), boxes: 2 });
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'shakeBox']);

  engine.gift(round(1, { ended: true }));
  engine.blindBox(boxResult(1, 15000, 2233000, 2233000));
  assert.deepEqual([playing(engine), engine.queue, engine.rounds], ['castle', [], []]);
});

test('a round that played its castle plays another opened from its blind boxes after it', () => {
  const engine = pet({ off: Object.keys(DAILY) });
  castle(engine, 1);
  assert.equal(playing(engine), 'castle');
  engine.gift(box(1, 1, { castle: true }));
  engine.gift(round(1, { ended: true }));
  engine.blindBox(boxResult(1, 15000, 2233000, 2233000));
  assert.deepEqual([playing(engine), engine.queue.map(q => q.name), engine.rounds], ['castle', ['castle'], []]);
  advance(engine, 11500);
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.equal(playing(engine), null);
});

test('two castles one 盲盒播报 opens play one after another, a 总督 coming meanwhile waiting for the one playing and going first', () => {
  const engine = pet({ off: Object.keys(DAILY) });
  engine.gift(box(1, 2, { castle: true }));
  engine.blindBox({ ...boxResult(1, 30000, 4466000, 2233000), boxes: 2 });
  engine.guard({ uid: 2, nickname: '总督', guardLevel: 1 });
  assert.deepEqual([playing(engine), engine.queue.map(q => q.name)], ['castle', ['castle', 'governor']]);
  advance(engine, 11500);
  assert.equal(playing(engine), 'governor');
  advance(engine, 15000);
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.equal(playing(engine), null);
});

test('a castle a merged 盲盒播报 has not reached yet still plays, with the result that reaches it', () => {
  const engine = pet();
  engine.gift(box(1, 1, { castle: true }));
  engine.gift(round(1, { ended: true }));
  engine.gift(box(1));
  engine.gift(box(1, 1, { castle: true }));
  engine.blindBox({ ...boxResult(1, 30000, 2245000, 2233000), boxes: 2 });
  assert.equal(playing(engine), 'castle');

  engine.gift(round(1, { ended: true }));
  engine.blindBox(boxResult(1, 15000, 2233000, 2233000));
  assert.deepEqual([engine.queue.map(q => q.name), engine.rounds], [['castle'], []]);
});

test('a show turned off does not play, nor a lesser one in its place', () => {
  const engine = pet({ off: ['bigGift', 'entry'] });
  engine.gift(gifts(1, 1000000));
  engine.entry({ uid: 2, nickname: '进场观众' });
  assert.equal(playing(engine), null);
  engine.gift(gifts(3, 100000));
  assert.equal(playing(engine), 'midGift');
});

test('a show turned off while it waits its turn does not play', () => {
  const engine = pet();
  engine.guard({ uid: 1, nickname: '总督', guardLevel: 1 });
  engine.guard({ uid: 2, nickname: '提督', guardLevel: 2 });
  engine.receive(state(engine, { settings: { ...engine.settings, off: ['admiral'] } }));
  advance(engine, 30000);
  assert.notEqual(playing(engine), 'admiral');
  assert.deepEqual(engine.queue, []);
});

test('only the 日常小动作 left on come, at the interval set, and none goes wrong at 0 percent', () => {
  const engine = pet({ off: ['hum', 'gym', 'hike', 'bike'], dailyMinSeconds: 60, dailyMaxSeconds: 60, mishapPercent: 0 });
  for (let n = 0; n < 5; n++) {
    advance(engine, 6000);
    assert.deepEqual([playing(engine), engine.cur.opts.mishap], ['ski', false]);
    const end = engine.cur.start + engine.cur.dur;
    advance(engine, end - engine.now());
    advance(engine, 59999 - 6000);
    assert.equal(playing(engine), null);
    advance(engine, 1);
  }
});

test('a 日常小动作 going wrong says a line of its kind at random, never the same one twice in a row, and keeps it while she says it', () => {
  // every draw in the middle: of three lines the second, and the third rather than the second again
  const engine = pet({}, () => 0.5), said = [];
  engine.receive(state(engine, { lines: { ...lines, slipped: ['甲', '乙', '丙'] } }));
  for (const _ of [1, 2]) {
    engine.start('hike', { mishap: true });
    const seen = new Set();
    for (let t = 0, end = engine.cur.dur; t < end; t += 100) { advance(engine, 100); seen.add(engine.stateAt(engine.now()).bubble?.text); }
    said.push(['甲', '乙', '丙'].filter(line => seen.has(line)));
  }
  assert.deepEqual(said, [['乙'], ['丙']]);
});

test('a 试演 plays a show from its start or holds her in a 心情 until it is stopped', () => {
  const engine = pet();
  engine.testShow({ show: 'blindBoxLarge', variant: 'Loss', nickname: '星河' });
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose, engine.cur.opts.result], ['blindBoxLarge', 'shakeBox', 'Loss']);
  engine.testShow({ show: 'gloomy', variant: null, nickname: '星河' });
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'gloomy']);
  engine.stopTestShow();
  assert.equal(engine.stateAt(engine.now()).pose, 'idle');
  engine.testShow({ show: 'bike', variant: 'Mishap', nickname: '星河' });
  assert.deepEqual([playing(engine), engine.cur.opts.mishap], ['bike', true]);
});

test('stopping a 试演 stops only the 试演: a show of what happened plays on', () => {
  const engine = pet();
  engine.testShow({ show: 'gift', variant: null, nickname: '星河' });
  engine.stopTestShow();
  assert.equal(playing(engine), null);
  engine.testShow({ show: 'gift', variant: null, nickname: '星河' });
  advance(engine, 2000);
  castle(engine, 1);
  engine.stopTestShow();
  assert.equal(playing(engine), 'castle');
});

test('a 试演 played out or cut short by a show of what happened says it is over; one stopped or held in a 心情 does not', () => {
  const engine = pet();
  const ended = [];
  engine.addEventListener('trialended', event => ended.push(event.detail));
  engine.testShow({ show: 'gift', variant: null, nickname: '星河', id: 1 });
  advance(engine, 1999);
  assert.deepEqual(ended, []);
  advance(engine, 1);
  assert.deepEqual(ended, [1]);
  engine.testShow({ show: 'gift', variant: null, nickname: '星河', id: 2 });
  castle(engine, 1);
  assert.deepEqual(ended, [1, 2]);
  advance(engine, 11500);
  engine.testShow({ show: 'gift', variant: null, nickname: '星河' });
  engine.stopTestShow();
  engine.testShow({ show: 'gloomy', variant: null, nickname: '星河' });
  advance(engine, 60000);
  assert.deepEqual(ended, [1, 2]);
});

test('a 总督 coming while the castle plays waits for it and then goes first; neither is ever dropped', () => {
  const engine = pet();
  castle(engine, 1);
  engine.guard({ uid: 2, nickname: '提督', guardLevel: 2 });
  engine.guard({ uid: 3, nickname: '总督', guardLevel: 1 });
  castle(engine, 4);
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['governor', '总督']);
  advance(engine, 15000);
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['castle', '观众4']);
  advance(engine, 11500);
  assert.equal(playing(engine), 'admiral');
});

test('a castle opened from a blind box plays when their result comes, instead of the round\'s gifts and blind boxes', () => {
  // no 日常小动作 comes once it is over, so what plays then is the round's
  const engine = pet({ off: Object.keys(DAILY) });
  engine.gift(box(2, 1, { castle: true }));
  engine.gift(gifts(2, 1000000));
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'shakeBox']);
  engine.blindBox(boxResult(2, 15000, 2233000, 2233000));
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.deepEqual([playing(engine), engine.queue], [null, []]);

  // B站 gives no castle as a gift; one that came would be a 大礼物 by its 投入
  engine.gift(round(3, { items: [item(2233000, { name: '浪漫城堡' })] }));
  assert.equal(playing(engine), 'bigGift');
});

test('an entry lottery plays each step, and she reacts to a draw only when the 抽奖页\'s effects end, at its DrawUntil', t => {
  t.mock.method(Date, 'now', () => 0);
  const engine = pet();
  onAir(engine, entryLottery({ phase: 'Preparing', entryCount: 0 }));
  assert.deepEqual(steps(engine), []);
  onAir(engine, entryLottery({ entryCount: 0 }));
  assert.deepEqual(steps(engine), ['EntryOpen']);
  assert.match(engine.fill(engine.cur.line, 'lottery', engine.cur.opts), /来了|报名/);
  advance(engine, 2600);
  onAir(engine, entryLottery({ phase: 'Closed' }));
  assert.deepEqual(steps(engine), ['EntryClosed']);
  advance(engine, 2000);
  const draw = { id: 'D1', award: { name: '一等奖', prize: '手办', grade: 'Grand' }, winner: { uid: 1, nickname: '幸运儿' } };
  onAir(engine, entryLottery({ phase: 'Closed', draws: [draw], drawUntil: new Date(7000).toISOString() }));
  advance(engine, 4499);
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'hope']);
  advance(engine, 1);
  advance(engine, 2499);
  assert.deepEqual([steps(engine), engine.stateAt(engine.now()).pose], [['RevealGrand'], 'hope']);
  advance(engine, 1);
  assert.equal(engine.stateAt(engine.now()).pose, 'cheer');
  assert.equal(engine.cur.opts.nickname, '幸运儿');
  advance(engine, 2700);
  onAir(engine, entryLottery({ phase: 'Ended', draws: [draw] }));
  assert.deepEqual(steps(engine), ['End']);

  advance(engine, 2600);
  onAir(engine, entryLottery({ id: 'E2', entryCount: 0 }));
  advance(engine, 2600);
  onAir(engine, entryLottery({ id: 'E2', phase: 'Closed', entryCount: 0 }));
  assert.deepEqual(steps(engine), ['NoEntries']);
  advance(engine, 2800);
  onAir(engine, entryLottery({ id: 'E2', phase: 'Ended', entryCount: 0 }));
  assert.deepEqual([steps(engine), engine.stateAt(engine.now()).pose], [[], 'idle']);
});

test('a pool lottery opens, calls a viewer and spins, reveals a draw at its DrawUntil and ends as a new lottery opens', t => {
  t.mock.method(Date, 'now', () => 0);
  const engine = pet(), pool = so => ({ mode: 'Pool', id: 'P1', phase: 'Idle', uid: null, draws: [], drawUntil: null, ...so });
  onAir(engine, pool());
  assert.deepEqual(steps(engine), ['PoolOpen']);
  advance(engine, 2600);
  onAir(engine, pool({ phase: 'Spinning', uid: 42 }));
  assert.deepEqual([steps(engine), engine.cur.opts.nickname], [['PoolCall', 'PoolSpin'], 'UID 42']);
  advance(engine, 2000);
  advance(engine, 2000);
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'hope']);
  const draw = { id: 'D1', prize: { name: '周边', grade: 'Third' }, uid: 42, nickname: '星河' };
  onAir(engine, pool({ uid: 42, draws: [draw], drawUntil: new Date(7000).toISOString() }));
  advance(engine, 4500);
  const { nickname, award, prize } = engine.cur.opts;
  assert.deepEqual([steps(engine), nickname, award, prize], [['RevealThird'], '星河', '周边', '周边']);
  advance(engine, 2500);
  onAir(engine, pool({ uid: 42, draws: [draw] }));
  advance(engine, 2100);
  assert.notEqual(engine.stateAt(engine.now()).pose, 'hope');
  onAir(engine, pool({ id: 'P2' }));
  assert.deepEqual(steps(engine), ['End', 'PoolOpen']);
});

test('a gift taking the 礼物榜\'s first place is crowned after its show, and nothing is while the 礼物榜 is not counting', () => {
  const engine = pet();
  ranks(engine, [[9, 5]], false);
  engine.gift(gifts(2, 1000000));
  ranks(engine, [[2, 1], [9, 5]]);
  assert.equal(playing(engine), 'bigGift');
  advance(engine, 6000);
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['newFirst', '观众2']);
  advance(engine, 2600);

  ranks(engine, null);
  engine.gift(gifts(3, 1000000));
  advance(engine, 6000);
  assert.notEqual(playing(engine), 'newFirst');
});

test('a first place the 礼物榜 tells before the gift that took it is crowned after that gift\'s show', () => {
  const engine = pet();
  ranks(engine, [[9, 5]], false);
  ranks(engine, [[2, 1], [9, 5]]);
  engine.gift(gifts(2, 1000000));
  advance(engine, 6000);
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['newFirst', '观众2']);
});

test('a first place taken by a round of blind boxes is crowned after the show its 盲盒播报 opens, also over a 日常小动作', () => {
  const engine = pet();
  ranks(engine, [[9, 5]], false);
  engine.gift(box(1));
  ranks(engine, [[1, 9], [9, 5]]);
  engine.blindBox(boxResult(1, 15000, 18000));
  advance(engine, 1700);
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['newFirst', '观众1']);
  advance(engine, 2600);

  advance(engine, 40000);
  const daily = playing(engine);
  assert.ok(daily in DAILY);
  engine.gift(box(2));
  ranks(engine, [[2, 20], [1, 9], [9, 5]]);
  assert.equal(playing(engine), daily);
  engine.blindBox(boxResult(2, 15000, 18000));
  advance(engine, 1700);
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['newFirst', '观众2']);
});

test('the big shows and the lottery turned off do not play, nor a lesser show in their place, nor does she wait through a lottery', () => {
  const engine = pet({ off: ['castle', 'governor', 'admiral', 'captain', 'newFirst', 'lottery'] });
  castle(engine, 1);
  engine.gift(box(2, 1, { castle: true }));
  engine.blindBox(boxResult(2, 15000, 2233000, 2233000));
  for (const guardLevel of [1, 2, 3]) engine.guard({ uid: 3, nickname: '大航海', guardLevel });
  onAir(engine, entryLottery({}));
  onAir(engine, entryLottery({ phase: 'Closed' }));
  assert.deepEqual([playing(engine), engine.queue, engine.stateAt(engine.now()).pose], [null, [], 'idle']);
  ranks(engine, [[9, 5]], false);
  engine.gift(gifts(4, 1000000));
  ranks(engine, [[4, 1], [9, 5]]);
  advance(engine, 6000);
  assert.notEqual(playing(engine), 'newFirst');
});

test('each big show and lottery step plays as a 试演 and ends', () => {
  const engine = pet();
  for (const row of shows.filter(row => ['castle', 'guard', 'newFirst', 'lottery'].includes(row.group))) for (const variant of row.tests) {
    engine.testShow({ show: row.id, variant, nickname: '星河' });
    assert.deepEqual([playing(engine), engine.cur.opts.result], [row.id, variant]);
    advance(engine, engine.cur.dur);
    assert.notEqual(engine.cur?.opts.result, variant);
  }
});

test('switched off and on again, she plays nothing of what the lottery did meanwhile', () => {
  const engine = pet(), settings = engine.settings;
  onAir(engine, entryLottery({}));
  advance(engine, 2600);
  engine.receive({ enabled: false });
  engine.receive({ enabled: true, roomConnected: true, settings, lines, lottery: entryLottery({ phase: 'Ended' }) });
  assert.deepEqual(steps(engine), []);
});

test('switched off and on again, she plays nothing she was playing, had queued or was shaking boxes for before', () => {
  const engine = pet(), settings = engine.settings;
  engine.guard({ uid: 1, nickname: '总督', guardLevel: 1 });
  engine.guard({ uid: 2, nickname: '提督', guardLevel: 2 });
  engine.gift(box(3));
  engine.receive({ enabled: false });
  // switched off the page draws nothing, so her engine is not run: only its clock goes on
  engine.master.currentTime += 60 * MINUTE;
  engine.receive({ enabled: true, roomConnected: true, settings, lines });
  advance(engine, 1);
  // her clocks start again, so a 日常小动作 of her own may come
  assert.ok(!['governor', 'admiral'].includes(playing(engine)));
  assert.deepEqual([engine.queue, engine.shaking()], [[], undefined]);
});

test('a castle cuts a lottery wait short, and once it is over she is back to waiting for the draw', () => {
  const engine = pet();
  onAir(engine, entryLottery({ phase: 'Closed' }));
  advance(engine, 2000);
  assert.equal(engine.stateAt(engine.now()).pose, 'hope');
  castle(engine, 1);
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'hope']);
});
