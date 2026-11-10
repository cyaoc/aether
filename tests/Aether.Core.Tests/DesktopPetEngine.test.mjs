// Run with: node --test tests/Aether.Core.Tests/DesktopPetEngine.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { Engine, seeded } from '../../src/Aether.Core/Pages/desktop-pet/engine.mjs';

const read = path => JSON.parse(readFileSync(new URL('../../src/Aether.Core/Pages/' + path, import.meta.url), 'utf8'));
const { shows } = read('desktop-pet/shows.json'), { lines } = read('characters/1/character.json');
// The defaults, as the 桌宠页's state tells them (amounts in 金瓜子).
const SETTINGS = { off: [], midGift: 100000, bigGift: 1000000, boredMinutes: 15, gloomyMinutes: 40, sleepyMinutes: 60, dailyMinSeconds: 20, dailyMaxSeconds: 40, mishapPercent: 25 };
const MINUTE = 60000;

// A 桌宠页 connected to a room, its clock at 0.
function pet(settings = {}) {
  const engine = new Engine({ currentTime: 0 }, shows);
  engine.reset(seeded(7));
  engine.receive({ enabled: true, roomConnected: true, settings: { ...SETTINGS, ...settings }, lines });
  return engine;
}
const advance = (engine, ms) => { engine.master.currentTime += ms; engine.update(engine.now()); };
const playing = engine => engine.cur?.name ?? null;
// A viewer's 一轮送礼 so far, as Core tells it after a gift message.
const round = (uid, so) => ({ uid, nickname: '观众' + uid, spend: 0, count: 0, topGift: '小花花', topCount: 0, boxSpend: 0, boxes: 0, castle: false, ended: false, ...so });
const gifts = (uid, spend, count = 1) => round(uid, { spend, count, topCount: count });
const castle = uid => round(uid, { spend: 2233000, count: 1, topGift: '浪漫城堡', topCount: 1, castle: true });
const boxResult = (uid, spend, openedValue, bestPrice = 9000) => ({ uid, nickname: '观众' + uid, tally: { spend, openedValue, outcome: ['Loss', 'Even', 'Gain'][Math.sign(openedValue - spend) + 1] }, boxes: 1, bestGift: '棉花糖', bestPrice });
// The lottery on air, as the 桌宠页's state carries it (LotteryState.cs), and the lottery's steps she plays and has queued.
const onAir = (engine, lottery) => engine.receive({ enabled: true, roomConnected: true, settings: engine.settings, lines, lottery });
const entryLottery = so => ({ mode: 'Entry', id: 'E1', keyword: '来了', phase: 'Open', entryCount: 3, draws: [], drawUntil: null, ...so });
const steps = engine => [engine.cur, ...engine.queue].filter(Boolean).map(show => show.opts.result);
// The 礼物榜 while its 期 runs, rows [id, count] from the first place down, as the 桌宠页's state carries it (GiftBoardState.cs);
// null while it is not counting.
const ranks = (engine, rows, fromGift = true) => engine.receive({ enabled: true, roomConnected: true, settings: engine.settings, lines,
  giftBoard: rows && { rows: rows.map(([id, num]) => ({ id, nickname: '观众' + id, faceUrl: '', viewerGuardLevel: 0, num })), options: { threshold: 100 }, running: true, fromGift } });

test('a gift round plays at once and moves up at the fifth gift, 1000 电池 and 1 万, never playing one twice', () => {
  const engine = pet();
  engine.gift(gifts(1, 100));
  const thanks = engine.cur;
  assert.equal(playing(engine), 'gift');
  engine.gift(gifts(1, 200, 2));
  assert.equal(engine.cur, thanks);
  engine.gift(gifts(1, 500, 5));
  assert.equal(playing(engine), 'giftStreak');
  engine.gift(gifts(1, 100000, 6));
  assert.equal(playing(engine), 'midGift');
  engine.gift(gifts(1, 1000000, 7));
  assert.equal(playing(engine), 'bigGift');
  advance(engine, 6000);
  engine.gift(gifts(1, 1000100, 8));
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

test('开心 counts gifts, blind boxes and 大航海, not entries', () => {
  const engine = pet();
  for (const uid of [1, 2, 3]) engine.entry({ uid, nickname: '进场观众' });
  assert.equal(engine.mood(engine.now()), 'calm');
  engine.gift(gifts(1, 100));
  engine.gift(round(2, { boxes: 1, boxSpend: 15000 }));
  engine.guard({ uid: 3, nickname: '舰长', guardLevel: 3 });
  assert.equal(engine.mood(engine.now()), 'happy');
  advance(engine, MINUTE);
  assert.equal(engine.mood(engine.now()), 'calm');
});

test('犯困 comes after an hour with nothing and ends on an entry or a danmaku; an entry wakes her before it plays', () => {
  const engine = pet();
  advance(engine, 60 * MINUTE);
  assert.equal(engine.mood(engine.now()), 'sleepy');
  engine.danmaku();
  assert.equal(engine.mood(engine.now()), 'gloomy');
  advance(engine, 60 * MINUTE);
  engine.entry({ uid: 1, nickname: '进场观众' });
  assert.deepEqual([playing(engine), engine.cur.pre], ['entry', 1000]);
  assert.equal(engine.mood(engine.now()), 'gloomy');
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
  engine.receive({ enabled: true, roomConnected: false, settings: SETTINGS, lines });
  advance(engine, 120 * MINUTE);
  assert.equal(engine.mood(engine.now()), 'calm');
  engine.receive({ enabled: true, roomConnected: true, settings: SETTINGS, lines });
  advance(engine, 14 * MINUTE);
  assert.equal(engine.mood(engine.now()), 'calm');
});

test('blind boxes are shaken until their result comes, and the show opens them then, sized by what they cost', () => {
  const engine = pet();
  engine.gift(round(1, { boxes: 1, boxSpend: 15000 }));
  engine.gift(round(1, { boxes: 2, boxSpend: 30000, ended: true }));
  advance(engine, 30000);
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'shakeBox']);
  engine.blindBox(boxResult(1, 30000, 18000));
  assert.deepEqual([playing(engine), engine.cur.opts.result, engine.stateAt(engine.now()).pose], ['blindBoxSmall', 'Loss', 'openBox']);
  assert.equal(engine.rounds.length, 0);

  advance(engine, 3000);
  engine.blindBox(boxResult(2, 150000, 150000));
  assert.deepEqual([playing(engine), engine.cur.opts.result, engine.stateAt(engine.now()).pose], ['blindBoxMedium', 'Even', 'shakeBox']);
  advance(engine, 4000);
  engine.blindBox(boxResult(3, 15000, 1245000, 1245000));
  assert.deepEqual([playing(engine), engine.cur.opts.result], ['blindBoxLarge', 'Jackpot']);
});

test('a show turned off does not play, nor a lesser one in its place', () => {
  const engine = pet({ off: ['bigGift', 'entry'] });
  engine.gift(gifts(1, 1000000));
  engine.entry({ uid: 2, nickname: '进场观众' });
  assert.equal(playing(engine), null);
  engine.gift(gifts(3, 100000));
  assert.equal(playing(engine), 'midGift');
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

test('a 总督 coming while the castle plays waits for it and then goes first; neither is ever dropped', () => {
  const engine = pet();
  engine.gift(castle(1));
  engine.guard({ uid: 2, nickname: '提督', guardLevel: 2 });
  engine.guard({ uid: 3, nickname: '总督', guardLevel: 1 });
  engine.gift(castle(4));
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['governor', '总督']);
  advance(engine, 15000);
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['castle', '观众4']);
  advance(engine, 11500);
  assert.equal(playing(engine), 'admiral');
});

test('a castle plays instead of the round\'s gifts and blind boxes, and one opened from a blind box when their result comes', () => {
  const engine = pet();
  engine.gift(gifts(1, 100));
  engine.gift({ ...castle(1), spend: 2233100, count: 2 });
  assert.equal(playing(engine), 'castle');
  engine.gift({ ...castle(1), spend: 3233100, count: 3 });
  advance(engine, 11500);
  assert.equal(playing(engine), null);

  engine.gift(round(2, { boxes: 1, boxSpend: 15000, castle: true }));
  engine.gift(round(2, { spend: 1000000, count: 1, topCount: 1, boxes: 1, boxSpend: 15000, castle: true }));
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'shakeBox']);
  engine.blindBox({ ...boxResult(2, 15000, 2233000, 2233000), castle: true });
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.deepEqual([playing(engine), engine.queue], [null, []]);
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
  ranks(engine, [['a', 5]], false);
  engine.gift(gifts(2, 1000000));
  ranks(engine, [['b', 1], ['a', 5]]);
  assert.equal(playing(engine), 'bigGift');
  advance(engine, 6000);
  assert.deepEqual([playing(engine), engine.cur.opts.nickname], ['newFirst', '观众b']);
  advance(engine, 2600);

  ranks(engine, null);
  engine.gift(gifts(3, 1000000));
  advance(engine, 6000);
  assert.notEqual(playing(engine), 'newFirst');
});

test('the big shows and the lottery turned off do not play, nor a lesser show in their place, nor does she wait through a lottery', () => {
  const engine = pet({ off: ['castle', 'governor', 'admiral', 'captain', 'newFirst', 'lottery'] });
  engine.gift(castle(1));
  engine.gift(round(2, { boxes: 1, boxSpend: 15000, castle: true }));
  engine.blindBox({ ...boxResult(2, 15000, 2233000, 2233000), castle: true });
  for (const guardLevel of [1, 2, 3]) engine.guard({ uid: 3, nickname: '大航海', guardLevel });
  onAir(engine, entryLottery({}));
  onAir(engine, entryLottery({ phase: 'Closed' }));
  assert.deepEqual([playing(engine), engine.queue, engine.stateAt(engine.now()).pose], [null, [], 'idle']);
  ranks(engine, [['a', 5]], false);
  engine.gift(gifts(4, 1000000));
  ranks(engine, [['b', 1], ['a', 5]]);
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

test('a castle cuts a lottery wait short, and once it is over she is back to waiting for the draw', () => {
  const engine = pet();
  onAir(engine, entryLottery({ phase: 'Closed' }));
  advance(engine, 2000);
  assert.equal(engine.stateAt(engine.now()).pose, 'hope');
  engine.gift(castle(1));
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'hope']);
});
