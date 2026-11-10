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
const boxResult = (uid, spend, openedValue, bestPrice = 9000) => ({ uid, nickname: '观众' + uid, tally: { spend, openedValue, outcome: ['Loss', 'Even', 'Gain'][Math.sign(openedValue - spend) + 1] }, boxes: 1, bestGift: '棉花糖', bestPrice });

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
