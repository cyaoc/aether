// Run with: node --test tests/Aether.Core.Tests/DesktopPetEngine.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { COS, DAILY, Engine, FOOT, GLOW, PK_SIGN, SPRITE_H, TWEEN, seeded } from '../../src/Aether.Core/Pages/desktop-pet/engine.mjs';
import { PK_INTEL } from '../../src/Aether.Core/Pages/desktop-pet/pk-intel.mjs';
import { SETTINGS } from './Support/DesktopPetSettings.mjs';
import { MEASURE, box, gamePet, gifts, item, play, playing, round, state } from './Support/DesktopPetGame.mjs';

const read = path => JSON.parse(readFileSync(new URL('../../src/Aether.Core/Pages/' + path, import.meta.url), 'utf8'));
const { shows } = read('desktop-pet/shows.json'), character = read('characters/1/character.json'), { lines } = character;
const MINUTE = 60000;

// A 桌宠页 connected to a room, its clock at 0.
function pet(settings = {}, rng = seeded(7)) {
  const engine = new Engine({ currentTime: 0 }, shows, character.desktopPet);
  engine.reset(rng);
  engine.receive({ enabled: true, roomConnected: true, settings: { ...SETTINGS, ...settings }, lines });
  return engine;
}
const advance = (engine, ms) => { engine.master.currentTime += ms; engine.update(engine.now()); };
const boxResult = (uid, spend, openedValue, bestPrice = 9000) => ({ uid, nickname: '观众' + uid, tally: { spend, openedValue, outcome: ['Loss', 'Even', 'Gain'][Math.sign(openedValue - spend) + 1] }, boxes: 1, bestGift: '棉花糖', bestPrice, castles: 0 });
// A viewer's blind box opening a castle, the only way B站 gives one, and its 盲盒播报.
const castle = (engine, uid) => {
  engine.gift(box(uid, 1, { castle: true }));
  engine.blindBox({ ...boxResult(uid, 15000, 2233000, 2233000), castles: 1 });
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

// A viewer coming in wearing this room's 粉丝团, whom she waves at (DesktopPetFeature).
const fan = uid => ({ uid, nickname: '进场观众', wave: true });

test('an entry plays only while she is idle, over no show nor 日常小动作, and does not queue', () => {
  const engine = pet();
  engine.entry(fan(1));
  assert.equal(playing(engine), 'entry');
  advance(engine, 1600);
  engine.gift(gifts(1, 100));
  engine.entry(fan(2));
  assert.deepEqual([playing(engine), engine.queue], ['gift', []]);
  advance(engine, 2000);
  advance(engine, 40000);
  assert.ok(Object.keys(DAILY).includes(playing(engine)));
  const daily = engine.cur;
  engine.entry(fan(3));
  assert.equal(engine.cur, daily);
});

test('her line says a viewer\'s whole nickname, however long', () => {
  const engine = pet(), nickname = '星河一只很长的昵称啊啊啊啊长长长长长长长';
  engine.entry({ ...fan(1), nickname });
  advance(engine, 500);
  const text = engine.stateAt(engine.now()).bubble.text;
  assert.ok(text.includes(nickname));
  // her line as written: no … put in for a cut, though her entry lines have their own
  assert.ok(lines.entry.includes(text.replace(nickname, '{昵称}')));
});

test('an entry does not play while she shakes a round\'s blind boxes', () => {
  const engine = pet();
  engine.gift(box(1));
  engine.entry(fan(2));
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'shakeBox']);
});

// A viewer's 盲盒查询 as its reply went out (BlindBoxQueryResult).
const query = (uid, so) => ({ uid, nickname: '观众' + uid, period: '本月', result: 'Loss', spend: '45', profit: '12.5', ...so });

test('a 盲盒查询 (C) cuts a wave or a 日常小动作 short, plays over no thank-you nor blind boxes she shakes, and does not queue', () => {
  const engine = pet();
  engine.blindBoxQuery(query(1));
  assert.equal(playing(engine), 'blindBoxQuery');
  advance(engine, 6000);
  engine.gift(gifts(1, 100));
  engine.blindBoxQuery(query(2));
  assert.deepEqual([playing(engine), engine.queue], ['gift', []]);
  advance(engine, 2000);
  assert.equal(playing(engine), null);
  engine.gift(box(3));
  engine.blindBoxQuery(query(4));
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).pose], [null, 'shakeBox']);
  engine.blindBox(boxResult(3, 15000, 9000));
  assert.equal(playing(engine), 'blindBoxSmall');
  advance(engine, 2600);
  assert.equal(playing(engine), null);
  engine.entry(fan(5));
  engine.blindBoxQuery(query(6));
  assert.equal(playing(engine), 'blindBoxQuery');
  engine.entry(fan(7));
  assert.deepEqual([playing(engine), engine.queue], ['blindBoxQuery', []]);
  advance(engine, 6000);
  advance(engine, 40000);
  assert.ok(DAILY[playing(engine)]);
  engine.blindBoxQuery(query(8));
  assert.equal(playing(engine), 'blindBoxQuery');
});

test('a gift cuts a wave short: no thank-you is dropped for someone coming in (#401)', () => {
  const engine = pet();
  engine.entry(fan(1));
  engine.gift(gifts(2, 100));
  assert.equal(playing(engine), 'gift');
});

test('she waves only at a viewer wearing this room\'s 粉丝团; anyone else coming in still wakes her from 犯困', () => {
  const engine = pet();
  advance(engine, 60 * MINUTE);
  assert.equal(engine.mood(engine.now()), 'sleepy');
  engine.entry({ uid: 1, nickname: '路人', wave: false });
  assert.deepEqual([playing(engine), engine.mood(engine.now())], [null, 'gloomy']);
});

test('a wave and a 账单卡 keep the colour they had before they moved tier', () => {
  const engine = pet();
  engine.entry(fan(1));
  assert.equal(engine.stateAt(engine.now()).glow, GLOW.C);
  advance(engine, 1600);
  engine.blindBoxQuery(query(2));
  assert.equal(engine.stateAt(engine.now()).glow, GLOW.D);
});

test('a gift cuts a 盲盒查询 short, its 账单卡 gone at once and never played again', () => {
  const engine = pet();
  engine.blindBoxQuery(query(1));
  advance(engine, 3000);
  assert.ok(engine.stateAt(engine.now()).bill);
  engine.gift(gifts(2, 100));
  assert.deepEqual([playing(engine), engine.stateAt(engine.now()).bill], ['gift', undefined]);
  advance(engine, 2000);
  assert.equal(playing(engine), null);
});

test('a gift that plays nothing yet cuts a 盲盒查询 short all the same: blind boxes, which she shakes at once, and a round\'s gift short of moving it up, a 试演\'s too', () => {
  const engine = pet();
  engine.blindBoxQuery(query(1));
  advance(engine, 2200);
  engine.gift(box(2));
  const st = engine.stateAt(engine.now());
  assert.deepEqual([playing(engine), st.bill, st.pose], [null, undefined, 'shakeBox']);

  const again = pet(), ended = [];
  again.addEventListener('trialended', event => ended.push(event.detail));
  again.gift(gifts(3, 100));
  advance(again, 2000);
  again.testShow({ show: 'blindBoxQuery', variant: 'Gain', nickname: '星河', id: 7 });
  again.gift(gifts(3, 100));
  assert.deepEqual([playing(again), again.stateAt(again.now()).bill, ended], [null, undefined, [7]]);
});

test('a 盲盒查询\'s 账单卡 says what the reply said, her line of its result at its foot, and she says nothing in a bubble', () => {
  const engine = pet();
  engine.blindBoxQuery(query(1));
  const seen = [];
  play(engine, 6000, st => seen.push(st));
  assert.deepEqual(seen.filter(st => st.bubble), []);
  const { result, period, name, spend, profit, line } = seen.find(st => st.bill).bill;
  assert.deepEqual({ result, period, name, spend, profit }, { result: 'Loss', period: '本月', name: '观众1', spend: '45', profit: '12.5' });
  assert.ok(lines.queryLoss.includes(line));
});

test('a 盲盒查询 turned off does not play', () => {
  const engine = pet({ off: ['blindBoxQuery'] });
  engine.blindBoxQuery(query(1));
  assert.equal(playing(engine), null);
});

test('开心 counts gifts, blind boxes and 大航海, not entries', () => {
  const engine = pet();
  for (const uid of [1, 2, 3]) engine.entry(fan(uid));
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
  engine.danmaku({ uid: 1, nickname: '观众1', side: 0 });
  assert.equal(engine.mood(engine.now()), 'gloomy');
  advance(engine, 60 * MINUTE);
  engine.entry(fan(1));
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
  engine.blindBox({ ...boxResult(1, 15000, 2233000, 2233000), castles: 1 });
  assert.deepEqual([playing(engine), engine.queue, engine.rounds], ['castle', [], []]);
});

test('a round that played its castle plays another opened from its blind boxes after it', () => {
  const engine = pet({ off: Object.keys(DAILY) });
  castle(engine, 1);
  assert.equal(playing(engine), 'castle');
  engine.gift(box(1, 1, { castle: true }));
  engine.gift(round(1, { ended: true }));
  engine.blindBox({ ...boxResult(1, 15000, 2233000, 2233000), castles: 1 });
  assert.deepEqual([playing(engine), engine.queue.map(q => q.name), engine.rounds], ['castle', ['castle'], []]);
  advance(engine, 11500);
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.equal(playing(engine), null);
});

test('two castles one 盲盒播报 opens play one after another, a 总督 coming meanwhile waiting for the one playing and going first', () => {
  const engine = pet({ off: Object.keys(DAILY) });
  engine.gift(box(1, 2, { castle: true }));
  engine.blindBox({ ...boxResult(1, 30000, 4466000, 2233000), boxes: 2, castles: 2 });
  engine.guard({ uid: 2, nickname: '总督', guardLevel: 1 });
  assert.deepEqual([playing(engine), engine.queue.map(q => q.name)], ['castle', ['castle', 'governor']]);
  advance(engine, 11500);
  assert.equal(playing(engine), 'governor');
  advance(engine, 15000);
  assert.equal(playing(engine), 'castle');
  advance(engine, 11500);
  assert.equal(playing(engine), null);
});

test('a 盲盒播报 of boxes the page never saw (it reloaded) plays as many castles as it says they opened', () => {
  const engine = pet();
  engine.blindBox({ ...boxResult(1, 30000, 4466000, 2233000), boxes: 2, castles: 2 });
  assert.deepEqual([playing(engine), engine.queue.map(q => q.name)], ['castle', ['castle']]);
});

test('a castle a merged 盲盒播报 has not reached yet still plays, with the result that reaches it', () => {
  const engine = pet();
  engine.gift(box(1, 1, { castle: true }));
  engine.gift(round(1, { ended: true }));
  engine.gift(box(1));
  engine.gift(box(1, 1, { castle: true }));
  engine.blindBox({ ...boxResult(1, 30000, 2245000, 2233000), boxes: 2, castles: 1 });
  assert.equal(playing(engine), 'castle');

  engine.gift(round(1, { ended: true }));
  engine.blindBox({ ...boxResult(1, 15000, 2233000, 2233000), castles: 1 });
  assert.deepEqual([engine.queue.map(q => q.name), engine.rounds], [['castle'], []]);
});

test('a show turned off does not play, nor a lesser one in its place', () => {
  const engine = pet({ off: ['bigGift', 'entry'] });
  engine.gift(gifts(1, 1000000));
  engine.entry(fan(2));
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
  const engine = pet({ off: Object.keys(DAILY).filter(kind => kind !== 'ski'), dailyMinSeconds: 60, dailyMaxSeconds: 60, mishapPercent: 0 });
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

// A 日常小动作 started on her page with opts, gone wrong (mishap) or not, a cos in its costume, and what she is doing ms into it
// on the page's clock.
function daily(show, opts) {
  const engine = gamePet(character);
  engine.start(show, opts);
  const { start } = engine.cur;
  return [engine, ms => { engine.master.currentTime = start + ms; return engine.stateAt(engine.now()); }];
}

// Her poses from those ms into a 日常小动作 on her page, and that it plays until end ms.
function assertPlays(show, opts, poses, end) {
  const [engine, at] = daily(show, opts);
  assert.deepEqual(Object.keys(poses).map(ms => at(+ms).pose), Object.values(poses));
  at(end - 1);
  assert.equal(playing(engine), show);
  at(end);
  assert.equal(playing(engine), null);
}

test('a 扭蛋 takes her to the cabinet and back: won she hugs the plush, 14.8 s; empty she wails and wipes her tears, 15.2 s', () => {
  assertPlays('gacha', { mishap: false }, { 0: 'gPointHome', 4300: 'gPress', 5000: 'gTurn', 7400: 'gTake', 9000: 'gShake', 10000: 'gTwist', 11400: 'gHug', 14000: 'idle' }, 14800);
  assertPlays('gacha', { mishap: true }, { 10500: 'gEmpty', 11400: 'gWail', 13500: 'gWipe', 14700: 'idle' }, 15200);
});

test('a 火锅 has her kneel at the table her sprite brings: yum she eats and feeds it, 11.8 s; spicy it fetches her 豆奶, 12.8 s', () => {
  assertPlays('hotpot', { mishap: false }, { 0: 'hHungry', 2300: 'hPray', 3100: 'hSit', 3700: 'hPickClear', 4700: 'hBlow', 5500: 'hYum', 7300: 'hFeed', 9300: 'hPray', 10500: 'idle' }, 11800);
  assertPlays('hotpot', { mishap: true }, { 3700: 'hPickRed', 6000: 'hSpicy', 8300: 'hDrink', 10300: 'hPray', 11500: 'idle' }, 12800);
});

test('a 手游 on the sofa: won she cheers kicking her legs, 10.6 s; lost the phone falls on her face and she holds her nose, 11.6 s', () => {
  const lying = { 0: 'mPhone', 1699: 'mPhone', 1700: 'mSit', 2500: 'mPlay', 6599: 'mPlay' };
  assertPlays('mobileGame', { mishap: false }, { ...lying, 6600: 'mWin', 8800: 'mSit', 9400: 'mPhone' }, 10600);
  assertPlays('mobileGame', { mishap: true }, { ...lying, 6600: 'mStunned', 7400: 'mSmack', 8100: 'mOuch', 9800: 'mSit', 10400: 'mPhone' }, 11600);
});

// Each costume of a cos, done and gone wrong: dressed by 0.7 s, dressed back over the last 0.9 s, 7.4 s and 9.6 s.
for (const [costume, label, calm, wrong] of [
  ['fighter', '格斗少女 bumps her gloves, takes her guard, throws a straight, an uppercut and a side kick and wipes her nose; cramped she does the splits, rubs her thigh and stands up',
    { 699: 'idle', 700: 'ftReady', 1100: 'ftBump', 2200: 'ftGuard', 3400: 'ftPunch', 3800: 'ftUpper', 4200: 'ftKick', 4900: 'ftWipe', 7199: 'ftWipe', 7200: 'idle' },
    { 4200: 'ftKick', 4500: 'ftSplit', 6400: 'ftRub', 8300: 'ftReady', 9399: 'ftReady', 9400: 'idle' }],
  ['ninja', '白眼格斗忍者 pokes her fingertips together, drops into her stance, thrusts her palm and settles; her lens popped out she crouches for it, puts it back and thanks her sprite',
    { 699: 'idle', 700: 'njReady', 1100: 'njShy', 2400: 'njStance', 3500: 'njPalm', 4300: 'njSettle', 7199: 'njSettle', 7200: 'idle' },
    { 3500: 'njPalm', 4100: 'njLensOut', 4800: 'njSearch', 6300: 'njLensIn', 7300: 'njThanks', 9399: 'njThanks', 9400: 'idle' }],
  ['fire', '火焰扇子女忍者 flicks her fan open, draws it back by her ear, throws it and catches it; still burning it sets her ponytail alight and she holds the singed tip',
    { 699: 'idle', 700: 'fnReady', 1100: 'fnPose', 2300: 'fnWindup', 2900: 'fnThrow', 4120: 'fnCatch', 7199: 'fnCatch', 7200: 'idle' },
    { 2900: 'fnThrow', 4120: 'fnCatch', 4700: 'fnBurn', 5950: 'fnSinged', 9399: 'fnSinged', 9400: 'idle' }],
  ['ice', '冰系女将军 puts her fists on her hips, raises her hand, snaps her fingers and folds her arms; she sneezes, shivers and rubs her nose',
    { 699: 'idle', 700: 'icReady', 1100: 'icProud', 2300: 'icSnapReady', 3000: 'icSnap', 3900: 'icArms', 7199: 'icArms', 7200: 'idle' },
    { 3000: 'icSnap', 3900: 'icSneeze', 4600: 'icShiver', 6400: 'icRubNose', 9399: 'icRubNose', 9400: 'idle' }],
  ['miao', '苗疆蛊毒少女 holds up her orchid fingers, takes her pot, lifts its lid and raises her fingertip; a caterpillar on her hand she cowers and is relieved',
    { 699: 'idle', 700: 'miReady', 1100: 'miPose', 2300: 'miHold', 3000: 'miOpen', 4300: 'miFinger', 7199: 'miFinger', 7200: 'idle' },
    { 3000: 'miOpen', 3600: 'miBug', 4500: 'miCower', 6300: 'miRelief', 9399: 'miRelief', 9400: 'idle' }],
  ['esports', '电竞少女 makes a V, aims, fires three shots, the gun kicking up each time, and blows on its muzzle; showing off she spins it, it flies up and comes down on her head and she picks it up',
    { 699: 'idle', 700: 'esReady', 1100: 'esV', 2300: 'esAim', 2800: 'esRecoil', 3100: 'esAim', 3400: 'esRecoil', 3700: 'esAim', 4000: 'esRecoil', 4600: 'esBlow', 7199: 'esBlow', 7200: 'idle' },
    { 4000: 'esRecoil', 4600: 'esTwirl', 5700: 'esLookUp', 6300: 'esOuch', 7600: 'esReady', 9399: 'esReady', 9400: 'idle' }],
  ['warlord', '绿袍女武将 stands her crossbow up, stands on its bow pulling the string, aims, shoots and shoulders it; stung by the string she drops it, picks it up and stands it up again',
    { 699: 'idle', 700: 'wlReady', 1100: 'wlPose', 2200: 'wlCrouch', 2800: 'wlPull', 3500: 'wlAim', 4400: 'wlShot', 5100: 'wlShoulder', 7199: 'wlShoulder', 7200: 'idle' },
    { 2800: 'wlPull', 3300: 'wlSting', 5600: 'wlReady', 6000: 'wlPose', 8000: 'wlReady', 9399: 'wlReady', 9400: 'idle' }]
]) test(`a cos as ${label}, 7.4 s and 9.6 s`, () => {
  assertPlays('cosplay', { mishap: false, costume }, calm, 7400);
  assertPlays('cosplay', { mishap: true, costume }, wrong, 9600);
});

// What else a cos in its costume, done or gone wrong, has at those ms: an effect of that kind setting off (fx) or turning (turn,
// its f[4]: the ice pillars shattering, a butterfly settling on her fingertip, the light arrow shooting off), the corner
// starting to shake (shake), her sprite's drawing (sprite) making for a spot off the page's left (offLeft) and the kind of
// line she says (line; null: no bubble).
for (const [costume, mishap, label, moments] of [
  ['fire', false, '火焰扇子女忍者\'s fan leaves her hand at 3.08 s and sparks back in it at 4.3 s', { 3080: { fx: 'fanFly' }, 4300: { fx: 'spark' } }],
  ['fire', true, '火焰扇子女忍者\'s fan still burns in her hand and her ponytail catches; her sprite blows it out, it smokes and she says it is singed',
    { 4300: { fx: 'fireAt' }, 4700: { fx: 'shock', line: 'cosFireBurn' }, 4880: { fx: 'fireAt' }, 4950: { sprite: 'sBlow' }, 5250: { fx: 'blowAir' }, 5950: { fx: 'smoke' }, 6300: { line: 'cosFireSinged' } }],
  ['ice', false, '冰系女将军\'s snap sparks at 3.18 s and her ice pillars rise from 3.2 s', { 3180: { fx: 'spark' }, 3200: { fx: 'pillars' } }],
  ['ice', true, '冰系女将军\'s sneeze blows her cap off and shatters her pillars at 4.08 s; her sprite has it at 5.1 s, no bubble while it brings it, and she says it is back',
    { 3900: { fx: 'shock', line: 'cosIceSneeze' }, 4080: { fx: 'item', turn: 'pillars' }, 5100: { sprite: 'sCap' }, 5300: { line: null }, 6649: { line: null }, 6650: { line: 'cosIceCap' } }],
  ['miao', false, '苗疆蛊毒少女\'s crown glints three times and butterflies come out of her pot at 3.18 s, one settling on her fingertip at 4.48 s',
    { 1300: { fx: 'glint' }, 1500: { fx: 'glint' }, 1700: { fx: 'glint' }, 3180: { fx: 'butterflies' }, 4480: { turn: 'butterflies' } }],
  ['miao', true, '苗疆蛊毒少女 finds a caterpillar, flings it off as she cowers, her sprite takes it at 5.7 s and from 5.95 s carries it off the page\'s left, and she is relieved',
    { 3600: { line: 'cosMiaoBug' }, 3780: { fx: 'shock' }, 4500: { fx: 'item' }, 5700: { sprite: 'sBug' }, 5950: { sprite: 'sBug', offLeft: true }, 6300: { line: 'cosMiaoRelief' } }],
  ['esports', false, '电竞少女 fires at 2.8, 3.4 and 4 s, the corner shaking with each shot', { 2800: { fx: 'bolt', shake: true }, 3400: { fx: 'bolt', shake: true }, 4000: { fx: 'bolt', shake: true } }],
  ['esports', true, '电竞少女 spins her gun, it flies up and comes down on her head at 6.3 s, the corner shaking, and her sprite pats it',
    { 4600: { line: 'cosEsportsTwirl' }, 4780: { fx: 'gunTrick' }, 5700: { fx: 'shock', sprite: 'surprised' }, 6300: { fx: 'impact', shake: true, line: 'cosEsportsBonk' }, 6500: { fx: 'dizzy', sprite: 'pat' }, 6600: { fx: 'tears' } }],
  ['warlord', false, '绿袍女武将\'s light arrow forms from 3.68 s and shoots off at 4.58 s in a flash, the corner shaking', { 3680: { fx: 'arrow' }, 4580: { fx: 'flash', turn: 'arrow', shake: true } }],
  ['warlord', true, '绿袍女武将\'s string stings her hand, her sprite flies round to blow on it and she says she will hit it next time',
    { 3300: { line: 'cosWarlordSting', sprite: 'surprised' }, 3480: { fx: 'impact' }, 3600: { fx: 'tears' }, 3700: { sprite: 'sBlow' }, 4100: { fx: 'blowAir' }, 6000: { line: 'cosWarlordAgain' } }]
]) test(`a cos as ${label}`, () => {
  const [, at] = daily('cosplay', { mishap, costume });
  for (const [ms, { fx, turn, shake, sprite, offLeft, line }] of Object.entries(moments)) {
    const st = at(+ms);
    if (fx) assert.ok(st.fx.some(([kind, from]) => kind === fx && from === +ms), `${fx} at ${ms}`);
    if (turn) assert.ok(st.fx.some(f => f[0] === turn && f[4] === +ms), `${turn} turning at ${ms}`);
    if (shake) assert.ok(st.shake.some(([from]) => from === +ms), `shake at ${ms}`);
    if (sprite) assert.equal(st.sprite, sprite, ms);
    // (its whole drawing past the edge)
    if (offLeft) assert.ok(st.sp[0] === 'at' && st.sp[1] < -SPRITE_H, `off the page's left at ${ms}`);
    if (line !== undefined) assert.ok(line ? lines[line].includes(st.bubble?.text) : !st.bubble, `${line} at ${ms}`);
  }
});

test('绿袍女武将\'s light arrow lies level along her crossbow as she aims', () => {
  const [, at] = daily('cosplay', { mishap: false, costume: 'warlord' }), [, , rear, front] = at(3680).fx.find(f => f[0] === 'arrow');
  assert.ok(rear.x > front.x, 'it points to the page\'s left');
  assert.equal(rear.y, front.y);
});

// The drawing of hers a cos in its costume, done, has shown up to ms: the pose she went into last before it, once her
// in-between into it has played out (TWEEN a frame), else null.
function drawnIn(engine, costume, ms) {
  const c = COS[costume](character.desktopPet.points.cos[costume]), poses = [[0, 'idle'], ...c.steps, ...c.calm], i = poses.findLastIndex(([from]) => from < ms);
  return poses[i][0] + engine.route(poses[i - 1][1], poses[i][1]).length * TWEEN <= ms ? poses[i][1] : null;
}

test('火焰扇子女忍者\'s fan leaves her throwing hand and comes back into her catching hand, and 电竞少女\'s first shot leaves the muzzle, each as she is drawn then', () => {
  const { fire, esports } = character.desktopPet.points.cos;
  const [engine, at] = daily('cosplay', { mishap: false, costume: 'fire' }), thrown = at(3080), [, , from, to, D] = thrown.fx.find(f => f[0] === 'fanFly');
  assert.deepEqual([drawnIn(engine, 'fire', 3080), drawnIn(engine, 'fire', 3080 + D)], [fire.throwHand.pose, fire.catchHand.pose]);
  assert.deepEqual([from, to], [engine.onPose(thrown, fire.throwHand.pose, fire.throwHand.at), engine.onPose(thrown, fire.catchHand.pose, fire.catchHand.at)]);
  const [gunner, aim] = daily('cosplay', { mishap: false, costume: 'esports' }), shot = aim(2800), [, , muzzle] = shot.fx.find(f => f[0] === 'bolt' && f[1] === 2800);
  assert.equal(drawnIn(gunner, 'esports', 2800), esports.muzzle.pose);
  assert.deepEqual(muzzle, gunner.onPose(shot, esports.muzzle.pose, esports.muzzle.at));
});

test('格斗少女 bounces on her toes in her guard, from 2.4 s until her straight', () => {
  const [, at] = daily('cosplay', { mishap: false, costume: 'fighter' });
  assert.deepEqual([2399, 2530, 3400].map(ms => Math.round(at(ms).y)), [FOOT.y, FOOT.y - 5, FOOT.y]);
});

test('a cos dresses her as one of the costumes of COS at random, or the one it is given, and she strikes its pose saying its line', () => {
  const KIND = { ninja: 'cosNinja', fire: 'cosFire', ice: 'cosIce', miao: 'cosMiao', esports: 'cosEsports', warlord: 'cosWarlord', fighter: 'cosFighter' }, engine = pet(), seen = new Set();
  for (let n = 0; n < 40; n++) {
    engine.start('cosplay', {});
    const { costume } = engine.cur.opts;
    seen.add(costume);
    assert.ok(lines[KIND[costume]].includes(engine.cur.line), costume);
  }
  assert.deepEqual([...seen].sort(), Object.keys(COS).sort());
  for (const costume of Object.keys(COS)) { engine.start('cosplay', { costume }); assert.equal(engine.cur.opts.costume, costume); }
});

test('her first 日常小动作 on her own is the board\'s from the same seed: which one, whether it goes wrong and a cos\'s costume', () => {
  // the board's first from each seed (its update draws the daily, whether it goes wrong, then the costume), worked out on
  // DP-DesktopPet.dc.html with its DAILY and COS: a cos in each of the seven costumes, and a 扭蛋 gone wrong
  const BOARD = { 1: ['gacha', true], 4: ['cosplay', false, 'fire'], 43: ['cosplay', false, 'miao'], 89: ['cosplay', true, 'fighter'],
    104: ['cosplay', false, 'warlord'], 107: ['cosplay', false, 'esports'], 111: ['cosplay', false, 'ninja'], 116: ['cosplay', false, 'ice'] };
  for (const [seed, picked] of Object.entries(BOARD)) {
    const engine = pet({}, seeded(+seed));
    advance(engine, 6000);
    assert.deepEqual([playing(engine), engine.cur.opts.mishap, engine.cur.opts.costume].filter(v => v !== undefined), picked, seed);
  }
});

test('her sprite hands her the battery and lets go of its drawing holding one 60 ms before hers has it, never two at once', () => {
  const [, at] = daily('gacha', { mishap: false });
  assert.deepEqual([1800, 2239, 2240].map(ms => at(ms).sprite), ['sCarry', 'sGive', 'sHappy']);
});

test('a gift cutting her 扭蛋 short as she turns the knob takes the cabinet, the battery and the capsule with it', () => {
  const [engine, at] = daily('gacha', { mishap: false }), turning = at(6000);
  assert.ok(turning.gacha.knob && turning.gacha.bay);
  engine.gift(gifts(1, 100));
  const st = engine.stateAt(engine.now());
  assert.deepEqual([playing(engine), st.gacha, st.halves, st.pop], ['gift', undefined, undefined, undefined]);
});

for (const [kind, label, wrongly] of [['gacha', '扭蛋', 'empty'], ['hotpot', '火锅', 'spicy'], ['mobileGame', '手游', 'lost'], ['cosplay', 'cos', 'its costume\'s mishap']]) test(`a ${label} comes as one of the 日常小动作 left on, ${wrongly} as often as one goes wrong, and never once turned off`, () => {
  const others = Object.keys(DAILY).filter(other => other !== kind);
  for (const [mishapPercent, wrong] of [[100, true], [0, false]]) {
    const engine = pet({ off: others, mishapPercent });
    advance(engine, 6000);
    assert.deepEqual([playing(engine), engine.cur.opts.mishap], [kind, wrong]);
  }
  const engine = pet({ off: [kind], dailyMinSeconds: 1, dailyMaxSeconds: 1 }), seen = new Set();
  for (let s = 0; s < 800; s++) { advance(engine, 1000); seen.add(playing(engine)); }
  assert.deepEqual([...seen].filter(Boolean).sort(), others.sort());
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
  engine.blindBox({ ...boxResult(2, 15000, 2233000, 2233000), castles: 1 });
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
  engine.blindBox({ ...boxResult(2, 15000, 2233000, 2233000), castles: 1 });
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

// B站 PK (design package README 15): a 桌宠页 whose pictures stand in for hers, its streamer in a PK; a countdown as Core tells
// it, leftMs before the moment it counts to (双倍结束 or 停止计分); and the numbers her sign shows, one after another, as ms play.
const inPk = () => { const engine = gamePet(character); engine.receive(state(engine, { pk: true })); return engine; };
const countdown = (kind, leftMs = 10000) => ({ kind, leftMs });
const numbers = (engine, ms) => {
  const seen = [];
  play(engine, ms, st => { const n = st.sign?.[1]?.[0]?.[0]; if (n !== undefined && n !== seen.at(-1)) seen.push(n); });
  return seen;
};
const TEN = ['10', '9', '8', '7', '6', '5', '4', '3', '2', '1', '0'];

test('a PK countdown told 10 s before its moment counts down on her sign from 10 to 0, and she puts it down', () => {
  const engine = inPk();
  engine.pkCountdown(countdown('last'));
  assert.equal(playing(engine), 'pkLastSeconds');
  assert.deepEqual(numbers(engine, 10500), TEN);
  assert.equal(engine.stateAt(engine.now()).pose, 'idle');
  play(engine, 400);
  assert.equal(playing(engine), null);
});

test('a PK countdown cuts a 账单卡 short: it is held up at once, never kept waiting behind a viewer\'s 盲盒查询', () => {
  const engine = inPk();
  engine.blindBoxQuery(query(1));
  assert.equal(playing(engine), 'blindBoxQuery');
  engine.pkCountdown(countdown('last'));
  assert.equal(playing(engine), 'pkLastSeconds');
});

test('in a PK she starts no 日常小动作, and once it is over she does as before', () => {
  const engine = pet();
  engine.receive(state(engine, { pk: true }));
  advance(engine, 7000);
  assert.equal(playing(engine), null);
  engine.receive(state(engine, { pk: false }));
  advance(engine, 20);
  assert.ok(Object.keys(DAILY).includes(playing(engine)));
});

test('a PK countdown waiting behind a 4.5 s show counts down what is left as it starts and plays 6.4 s', () => {
  const engine = inPk();
  engine.guard({ uid: 1, nickname: '舰长', guardLevel: 3, spend: 19800000 });
  engine.pkCountdown(countdown('double'));
  assert.deepEqual([playing(engine), engine.queue.map(q => q.name)], ['captain', ['pkDoubleEnding']]);
  advance(engine, 4500);
  assert.deepEqual([playing(engine), engine.cur.dur], ['pkDoubleEnding', 6400]);
  // 5.5 s left: 6 as it flies to her, then 5 down to 0
  assert.deepEqual(numbers(engine, 6400), TEN.slice(4));
  assert.equal(playing(engine), null);
});

test('a PK countdown whose moment has passed behind a longer show, or with under 3 s left, is not held up', () => {
  const engine = inPk();
  castle(engine, 1);
  engine.pkCountdown(countdown('last'));
  advance(engine, 11500);
  assert.deepEqual([playing(engine), engine.queue], [null, []]);
  engine.pkCountdown(countdown('last', 2900));
  assert.equal(playing(engine), null);
});

test('a PK countdown cut short by a bigger show is held up again once it is over, for the 3 s or more still left', () => {
  const engine = inPk();
  engine.pkCountdown(countdown('last'));
  advance(engine, 500);
  engine.gift(gifts(1, 1000000));
  assert.deepEqual([playing(engine), engine.queue.map(q => q.name)], ['bigGift', ['pkLastSeconds']]);
  advance(engine, 6000);
  assert.deepEqual([playing(engine), engine.cur.dur], ['pkLastSeconds', 4400]);
  assert.deepEqual(numbers(engine, 4400), TEN.slice(6));
});

test('her sign says the double bonus\'s condition in B站\'s words, more than 9999 in 万', () => {
  const words = text => { const engine = inPk(); engine.pkCondition({ text }); return engine.stateAt(engine.now()).sign.map(row => row.map(run => run[0]).join('')); };
  assert.deepEqual([words('差10票翻倍'), words('差1人翻倍'), words('差12345票翻倍'), words('差1.2万票翻倍')],
    [['差10票', '翻倍！'], ['差1人', '翻倍！'], ['差1.2万票', '翻倍！'], ['差1.2万票', '翻倍！']]);
});

test('her sign and the 对手情报卡 write numbers up to 9999 as they are, then in 万, and a card B站 answered nothing for has no side', () => {
  assert.deepEqual([9999, 12345, 30000, 140323].map(PK_INTEL.count), ['9999', '1.2万', '3万', '14万']);
  assert.deepEqual(PK_INTEL.layout(MEASURE, { name: '星河' }), { front: null, back: null });
});

test('a PK\'s result goes by its score: a 平局 alike (0 : 0 too), and by twice the other side and a tenth of the larger 大胜 to 惨败', () => {
  const ended = ([ours, theirs]) => { const engine = inPk(); engine.pkEnd({ ours, theirs }); return playing(engine) && engine.cur.opts.result; };
  // the board's scores for its buttons, one for each ending (its PK_RESULT.samples)
  const samples = { BigWin: [12000, 3000], Win: [8000, 5000], NarrowWin: [5200, 5000], Draw: [3000, 3000], NarrowLoss: [4800, 5000], Lose: [5000, 8000], BigLoss: [1000, 12000] };
  assert.deepEqual(Object.values(samples).map(ended), Object.keys(samples));
  assert.deepEqual([[0, 0], [10, 0]].map(ended), ['Draw', 'BigWin']);
});

// The 中途比分 she plays as each score of a PK comes, 21 s apart (past the 20 s between two), '' for none.
const swings = (...scores) => {
  const engine = inPk();
  return scores.map(([ours, theirs]) => {
    engine.pkScore({ ours, theirs });
    const swing = playing(engine) === 'pkSwing' ? engine.cur.opts.result : '';
    advance(engine, 21000);
    return swing;
  });
};

test('a PK\'s score plays a 中途比分 as the lead changes hands, one side gets to twice the other or the gap to a tenth, once either has 100', () => {
  assert.deepEqual(swings([10, 0]), ['']);
  assert.deepEqual(swings([50, 0], [300, 0]), ['', 'PullAhead']);
  assert.deepEqual(swings([300, 100], [300, 600]), ['PullAhead', 'Overtaken']);
  assert.deepEqual(swings([500, 400], [500, 500], [500, 600]), ['', 'CaughtUp', 'Overtaken']);
  assert.deepEqual(swings([1000, 2500], [1000, 1050]), ['FallBehind', 'CloseIn']);
});

test('a 中途比分 within 20 s of the last that played is let go, how the PK stands still kept', () => {
  const engine = inPk();
  engine.pkScore({ ours: 300, theirs: 100 });
  advance(engine, 5000);
  engine.pkScore({ ours: 300, theirs: 600 });
  assert.equal(playing(engine), null);
  advance(engine, 16000);
  engine.pkScore({ ours: 800, theirs: 600 });
  assert.deepEqual([playing(engine), engine.cur?.opts.result], ['pkSwing', 'Overtake']);
});

test('a 中途比分 while she plays another show is let go, not queued, and the 20 s count from one that played', () => {
  const engine = inPk();
  engine.guard({ uid: 1, nickname: '舰长', guardLevel: 3, spend: 19800000 });
  engine.pkScore({ ours: 300, theirs: 100 });
  assert.deepEqual([playing(engine), engine.queue], ['captain', []]);
  advance(engine, 5000);
  engine.pkScore({ ours: 300, theirs: 600 });
  assert.deepEqual([playing(engine), engine.cur?.opts.result], ['pkSwing', 'Overtaken']);
});

test('a new PK counts how it stands from 0 : 0', () => {
  const engine = inPk();
  engine.pkScore({ ours: 300, theirs: 100 });
  advance(engine, 21000);
  engine.receive(state(engine, { pk: false }));
  engine.receive(state(engine, { pk: true }));
  engine.pkScore({ ours: 300, theirs: 100 });
  assert.deepEqual([playing(engine), engine.cur?.opts.result], ['pkSwing', 'PullAhead']);
});

test('a new PK plays its first 中途比分 however soon after the last PK\'s', () => {
  const engine = inPk();
  engine.pkScore({ ours: 300, theirs: 100 });
  advance(engine, 5000);
  engine.receive(state(engine, { pk: false }));
  engine.receive(state(engine, { pk: true }));
  engine.pkScore({ ours: 300, theirs: 100 });
  assert.deepEqual([playing(engine), engine.cur?.opts.result], ['pkSwing', 'PullAhead']);
});

// The other streamer as Core tells of them as a PK is matched (pkMatch), with what B站 answered: the rest null, and no 在线榜前三;
// and the sides the 对手情报卡 shows as it plays (0 its front, 1 its back, between them turning over).
const opponent = so => ({ name: '星星点灯', rank: null, firstWin: null, fans: null, guards: null, top3: [], ...so });
const sides = engine => { const seen = new Set(); play(engine, 10000, st => { if (st.intel) seen.add(st.intel.turn); }); return [...seen]; };

test('the 对手情报卡 turns over only when it has both sides, one alone shows all the while, and with neither it does not play', () => {
  const [front, back, both, neither] = [{ fans: 34613 }, { top3: [{ score: 880, wealth: 12, guard: 0, medal: 0 }] }, { fans: 34613, top3: [{ score: 880, wealth: 0, guard: 3, medal: 0 }] }, {}]
    .map(so => { const engine = inPk(); engine.pkMatch({ intel: opponent(so), leftMs: 10000 }); return engine; });
  assert.deepEqual([front, back, both, neither].map(playing), ['pkOpponent', 'pkOpponent', 'pkOpponent', null]);
  const turned = sides(both);
  assert.deepEqual([sides(front), sides(back), [0, 1].map(side => turned.includes(side))], [[0], [1], [true, true]]);
});

test('the 对手情报卡 is rolled up as the PK starts: so much shorter as its lookup took, and not played once the PK has started', () => {
  const [late, started] = [7000, 0].map(leftMs => { const engine = inPk(); engine.pkMatch({ intel: opponent({ fans: 34613 }), leftMs }); return engine; });
  assert.deepEqual([playing(late), late.cur.dur, playing(started)], ['pkOpponent', 7000, null]);
});

test('a 对手情报卡 or a countdown she wakes up for keeps to its moment all the same, her waking up taken off it', () => {
  const [card, count] = [e => e.pkMatch({ intel: opponent({ fans: 34613 }), leftMs: 7000 }), e => e.pkCountdown({ kind: 'last', leftMs: 10000 })]
    .map(tell => { const engine = inPk(); advance(engine, 41 * MINUTE); tell(engine); return engine; });
  assert.deepEqual([card, count].map(e => [playing(e), e.cur.pre, e.cur.pre + e.cur.dur]), [['pkOpponent', 1000, 7000], ['pkLastSeconds', 1000, 10000 + PK_SIGN.down]]);
});
