// Run with: node --test tests/Aether.Core.Tests/DesktopPetGame.test.mjs
// The 宝箱小游戏 (#372, #382) on the 桌宠页: when the 捣蛋鬼 comes and goes, the tug bar's rules (the two sides' gifts, 秒杀,
// time-up and 绝杀, what danmaku do), what scares it off, what goes into her chest, what a 来访 leaves of her shows, and which
// of B站's pictures the page keeps after it.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { box, flowers, gamePet, gifts, other, play, playing, round, state, tickets } from './Support/DesktopPetGame.mjs';

const character = JSON.parse(readFileSync(new URL('../../src/Aether.Core/Pages/characters/1/character.json', import.meta.url), 'utf8'));
// What the page reports to Core of the 捣蛋鬼 (page.mjs).
const reports = pet => { const told = []; pet.addEventListener('visit', event => told.push(event.detail)); return told; };
// The 捣蛋鬼 called and on the page, peeking in.
const visited = settings => { const pet = gamePet(character, settings); pet.summon(); play(pet, 100); return pet; };
// From peeking in to the tug over a chest with something in it: 2.4 s peeking, 9 s flying it over, its fall.
const tugging = (chest = 500, settings) => { const pet = gamePet(character, settings); pet.gm.chest = chest; pet.summon(); play(pet, 12500); return pet; };
// Halfway there: it flies the chest over her head.
const flying = (chest = 500) => { const pet = gamePet(character); pet.gm.chest = chest; pet.summon(); play(pet, 5000); return pet; };
// In the tug with 55 人气票 landed, past halfway to its end: its lightning has struck her and still plays.
const zapped = settings => { const pet = tugging(500, settings); pet.gift(tickets(1, 55)); play(pet, 3000); assert.notEqual(pet.gm.visit.cut, null); return pet; };
// Plays the tug on until its countdown is down to left ms.
const countdown = (pet, left) => { while (pet.gm.visit.left > left) play(pet, 100); };
// Plays on until she plays show, past what it waits for first.
const until = (pet, show) => { for (let i = 0; i < 1000 && !(playing(pet) === show && pet.now() >= pet.cur.start + pet.cur.pre); i++) play(pet, 100); };
// A 试演 of the 总督, which takes over whatever she plays; and a 舰长 for a month, 1980 电池.
const governor = { show: 'governor', variant: null, nickname: '总督', id: 9 };
const captain = { uid: 1, nickname: '舰长', guardLevel: 3, spend: 198000 };
// A viewer's blind box that opens a castle, and its 盲盒播报; the 日常小动作 turned off, for tests that see every show she plays.
const castleBox = () => box(2, 1, { name: '浪漫城堡', castle: true });
const castled = { uid: 2, nickname: '观众2', tally: { spend: 15000, openedValue: 2233000, outcome: 'Gain' }, boxes: 1, bestGift: '浪漫城堡', bestPrice: 2233000, castles: 1 };
// The 盲盒播报 of a viewer's n boxes at 150 电池 each, a little to the good.
const opened = (uid, n) => ({ uid, nickname: '观众' + uid, tally: { spend: 15000 * n, openedValue: 16000 * n, outcome: 'Gain' }, boxes: n, bestGift: '爱心抱枕', bestPrice: 16000 });
const NO_DAILY = ['hum', 'gym', 'hike', 'bike', 'ski'];
// Every show she plays from now on, in order.
const shown = pet => { const shows = new Set(); return [shows, () => { if (pet.cur) shows.add(pet.cur); }]; };

test('叫它来 brings the 捣蛋鬼 once the show she plays is over, and the page reports it setting off and leaving', () => {
  const pet = gamePet(character), told = reports(pet);
  pet.gift(gifts(1, 100));
  pet.summon();
  play(pet, 1500);
  pet.gift(round(1, { ended: true }));
  assert.deepEqual([playing(pet), pet.gm.visit, told], ['gift', null, []]);
  play(pet, 1000);
  assert.deepEqual([pet.gm.visit?.phase, told], ['peek', ['start']]);
  pet.receive(state(pet, { pk: true }));
  play(pet, 3000);
  assert.deepEqual([pet.gm.visit, told], [null, ['start', 'end']]);
});

test('a PK keeps the 捣蛋鬼 away: called on its way it is reported gone, and on the page it runs off', () => {
  const pet = gamePet(character), told = reports(pet);
  pet.gift(gifts(1, 100));
  pet.summon();
  pet.receive(state(pet, { pk: true }));
  pet.summon();
  play(pet, 3000);
  assert.deepEqual([pet.gm.visit, told], [null, ['end']]);
  const here = visited();
  here.receive(state(here, { pk: true }));
  assert.equal(here.gm.visit.phase, 'fled');
});

test('大航海 and blind boxes go into her chest at what was paid, a blind box as it comes', () => {
  const pet = gamePet(character);
  pet.guard(captain);
  pet.gift(box(2));
  assert.equal(pet.gm.chest, 1980 + 150);
});

test('a 大航海 whose show is off scares the 捣蛋鬼 off all the same and plays nothing', () => {
  const pet = visited({ off: ['captain'] });
  pet.guard(captain);
  play(pet, 1500);
  assert.deepEqual([pet.gm.visit.phase, playing(pet)], ['fled', null]);
});

test('a gift that scares the 捣蛋鬼 off sends a ring of light out from her first: until it gets there the countdown, its lightning and what is thrown stand still, and a 人气票 or 小花花 is only a gift', () => {
  const pet = tugging(), v = pet.gm.visit;
  play(pet, 3000);
  // 55 人气票 already falling, which land past halfway to its end, and 2 more still to fall
  pet.gift(tickets(3, 55));
  play(pet, 100);
  pet.gift(tickets(4, 2));
  pet.guard(captain);
  const left = v.left;
  assert.deepEqual([v.phase, playing(pet), pet.gm.burst.due > pet.now()], ['tug', null, true]);
  // (a hundred 小花花 would have been its 秒杀)
  pet.gift(flowers(2, 100));
  play(pet, 400);
  assert.deepEqual([v.phase, playing(pet), v.left, v.blue, v.net, v.cut ?? null, v.queue.length, pet.gm.aside], ['tug', null, left, 0, 55, null, 1, 100]);
  play(pet, 1500);
  assert.deepEqual([v.phase, playing(pet), pet.gm.burst.due], ['fled', 'captain', null]);
});

test('its lightning cast as the light sets out strikes her all the same, but gives it nothing', () => {
  const pet = tugging(), v = pet.gm.visit;
  pet.gift(tickets(1, 55));
  play(pet, 2500);
  pet.guard(captain);
  play(pet, 400);
  assert.deepEqual([v.bolted, v.blue, v.phase], [true, 0, 'tug']);
  play(pet, 1500);
  assert.deepEqual([v.blue, v.phase, pet.cur.opts.frizz], [0, 'fled', 'cheer']);
});

test('scared off while the bar is up, in the tug or still in the air, it is her 秒杀: the line runs to her end, the sender\'s name rises from there, 连守 goes up and she cheers until the show starts', () => {
  for (const pet of [tugging(), flying()]) {
    const v = pet.gm.visit, names = new Set(), poses = new Set();
    pet.gift(tickets(2, 3));
    pet.guard(captain);
    play(pet, 2500, st => { for (const num of pet.gm.nums) if (num.big) names.add(`${num.text} ${num.x}`); if (pet.cur) poses.add(st.pose); });
    assert.deepEqual([v.phase, v.swept, v.result, pet.gm.streak, pet.gm.chest, playing(pet), pet.cur.opts.kept, [...names], [...poses]],
      ['fled', true, 1, 1, 500 + 3 + 1980, 'captain', true, ['舰长 460'], ['cheer']]);
  }
  // (an empty chest is no tug)
  const empty = visited();
  empty.guard(captain);
  play(empty, 1500);
  assert.deepEqual([empty.gm.visit.swept ?? false, empty.gm.streak, empty.cur.opts.kept], [false, 0, false]);
});

test('a lottery scares it off at once, with no light: in the tug the line runs to her end all the same, and no name rises', () => {
  const pet = tugging(), v = pet.gm.visit;
  pet.receive(state(pet, { lottery: { mode: 'Entry', id: 'E1', keyword: '抽奖', phase: 'Open', entryCount: 0, draws: [], drawUntil: null } }));
  assert.deepEqual([v.phase, pet.gm.burst ?? null, v.swept, pet.gm.nums, pet.gm.streak, playing(pet), pet.cur.opts.kept], ['fled', null, true, [], 1, 'lottery', true]);
});

test('scared off by a gift after the lightning, her sprite pats her hair smooth before it takes the chest home, and the show starts once both are done', () => {
  const pet = zapped(), waiting = new Set(), early = [];
  pet.guard(captain);
  play(pet, 1500);
  assert.deepEqual([playing(pet), pet.cur.opts.frizz, pet.cur.opts.kept, pet.gm.out.wait], ['captain', 'cheer', true, true]);
  // while the chest waits she is frizzed, and it may go once her hair is smooth; the show waits for the chest on (the sprite
  // is not drawn here to take it: it is home when the page gives up on it)
  play(pet, 10000, st => {
    if (pet.now() < pet.cur.start + pet.cur.pre) waiting.add(`${st.pose} ${pet.gm.out.wait}`); else if (pet.gm.out) early.push(pet.now());
  });
  assert.deepEqual([[...waiting], early, pet.gm.out], [['cheerZap true', 'cheer false'], [], null]);
});

test('a 试演 scares the 捣蛋鬼 off as what happened would, and plays once the chest is home', () => {
  const pet = visited();
  pet.testShow({ show: 'captain', variant: null, nickname: '星河', id: 1 });
  assert.deepEqual([pet.gm.visit.phase, playing(pet)], ['fled', 'captain']);
});

test('the 捣蛋鬼 comes between a pool lottery\'s draws, not while one is drawn, its show on or off', () => {
  const pool = so => ({ mode: 'Pool', id: 'P1', phase: 'Idle', uid: null, draws: [], drawUntil: null, ...so });
  // (and no 日常小动作 for it to wait for)
  for (const off of [[], ['lottery']]) {
    const pet = gamePet(character, { off: [...off, ...NO_DAILY] });
    pet.receive(state(pet, { lottery: pool() }));
    play(pet, 3000);
    pet.receive(state(pet, { lottery: pool({ phase: 'Spinning' }) }));
    pet.summon();
    play(pet, 5000);
    assert.equal(pet.gm.visit, null);
    pet.receive(state(pet, { lottery: pool({ phase: 'Idle', draws: [{ id: 1, uid: 42, prize: { name: '周边', grade: 'Third' } }] }) }));
    play(pet, 1000);
    assert.equal(pet.gm.visit?.phase, 'peek');
  }
});

test('what is said in a 来访 is a line of its kind at random, never the same one twice in a row', () => {
  // every draw in the middle: of three lines the second, and the third rather than the second again
  const pet = gamePet({ ...character, lines: { ...character.lines, pranksterPeek: ['甲', '乙', '丙'] } }, {}, () => 0.5), said = [];
  for (const _ of [1, 2]) {
    pet.summon();
    play(pet, 100);
    said.push(pet.gm.visit.say.text);
    pet.receive(state(pet, { pk: true }));
    play(pet, 3000);
    pet.receive(state(pet, { pk: false }));
  }
  assert.deepEqual(said, ['乙', '丙']);
});

test('the 捣蛋鬼 peeks in and slides back out as it came, off the page before it flies the chest over, never jumping', () => {
  const pet = gamePet(character), seen = [];
  pet.summon();
  play(pet, 3000, () => seen.push({ ...pet.gl.pk }));
  const steps = seen.slice(1).map((pk, i) => Math.abs(pk.x - seen[i].x));
  assert.deepEqual([seen.some(pk => pk.pose === 'peek' && pk.x === 24), seen.at(-1).pose, Math.max(...steps) < 10], [true, 'carry', true]);
});

test('the tug\'s line starts in the middle, a 人气票 pushes it toward the 捣蛋鬼\'s end and a 小花花 toward hers, each side\'s score counting them as they reach the page', () => {
  const pet = tugging(), v = pet.gm.visit, bar = () => [v.red, v.blue, Math.round(pet.lineX(v) * 10) / 10, v.net];
  // a chest of 500 电池 puts each end 100 票 from the middle of the bar, 436 px wide from x 22
  assert.deepEqual([v.span, ...bar()], [100, 0, 0, 240, 0]);
  pet.gift(tickets(1, 10));
  assert.deepEqual(bar(), [10, 0, 261.8, 0]);
  pet.gift(flowers(2, 30));
  assert.deepEqual(bar(), [10, 30, 196.4, 0]);
  // (the chest on the ground goes by what has landed: once it is up and they drop)
  play(pet, 3000);
  assert.deepEqual(bar(), [10, 30, 196.4, -20]);
});

test('each end is a twentieth of the chest\'s 电池 from the middle, 100 票 at the least and 500 at the most', () => {
  assert.deepEqual([500, 4000, 20000].map(chest => tugging(chest).gm.visit.span), [100, 200, 500]);
});

test('a side whose gifts reaching the page take the line to its end wins there and then: nothing waits to land, and the lightning is cut short', () => {
  const pet = tugging();
  pet.gift(tickets(1, 55));
  play(pet, 2200);
  assert.notEqual(pet.gm.visit.cut, null);
  pet.gift(tickets(2, 45));
  assert.deepEqual([pet.gm.visit.phase, pet.gm.visit.cut, playing(pet), pet.gm.streak, pet.gm.chest], ['won', null, 'chestKept', 1, 600]);
  const stolen = tugging();
  stolen.gift(flowers(1, 100));
  assert.deepEqual([stolen.gm.visit.phase, stolen.gm.streak, stolen.gm.chest, stolen.gm.brag], ['lost', 0, 0, true]);
});

test('its lightning\'s bolt gives the 捣蛋鬼 a quarter of the way to an end, shown as a gift is; cut short before it strikes, her hair is not frizzed', () => {
  const pet = tugging(), v = pet.gm.visit;
  pet.gift(tickets(1, 55));
  play(pet, 2200);
  assert.deepEqual([v.cut != null, v.blue, v.net], [true, 0, 55]);
  play(pet, 1000);
  assert.deepEqual([v.blue, v.net, pet.gm.nums.at(-1).text, pet.gm.nums.at(-1).side, v.bolted], [25, 30, '+25 捣蛋鬼', -1, true]);
  const spared = tugging(), poses = new Set();
  spared.gift(tickets(1, 55));
  play(spared, 2200);
  spared.gift(tickets(2, 45));
  play(spared, 3000, st => poses.add(st.pose));
  assert.deepEqual([spared.gm.visit.blue, spared.cur.opts.zap, [...poses].filter(pose => pose.endsWith('Zap'))], [0, false, []]);
});

test('beaten while it is still getting up from its fall, the 捣蛋鬼 is knocked flying off the lid', () => {
  const pet = tugging();
  pet.gift(tickets(1, 100));
  assert.deepEqual([pet.gm.visit.phase, playing(pet)], ['knocked', 'chestKept']);
});

test('while it still flies the chest in, enough 人气票 knock it out of the air and she cheers at once, and enough 小花花 have it off with the chest', () => {
  const kept = flying(), poses = new Set();
  kept.gift(tickets(1, 100));
  assert.deepEqual([kept.gm.visit.phase, playing(kept), kept.cur.pre, kept.gm.streak, kept.gm.chest], ['fled', 'chestKept', 0, 1, 600]);
  const stolen = flying();
  stolen.gift(flowers(1, 100));
  assert.deepEqual([stolen.gm.visit.phase, stolen.gm.streak, stolen.gm.chest], ['away', 0, 0]);
  // she starts, reaches up after it until it is off the page, and only then kneels crying
  play(stolen, 2300, st => poses.add(st.pose));
  assert.deepEqual([...poses], ['startled', 'reachUp']);
  play(stolen, 300, st => poses.add(st.pose));
  assert.deepEqual([stolen.gm.visit, playing(stolen), [...poses].at(-1)], [null, 'chestRobbed', 'robbed']);
});

test('over an empty chest one 小花花 has the 捣蛋鬼 take the chest: 连守 goes, she cries once it is off with it, and it has nothing to brag of next time', () => {
  const pet = gamePet(character), poses = new Set();
  pet.gm.streak = 2;
  pet.summon();
  play(pet, 20000);
  assert.equal(pet.gm.visit.phase, 'sit');
  pet.gift(flowers(1));
  play(pet, 1000);
  assert.deepEqual([pet.gm.visit.phase, pet.gm.visit.say.text, pet.gm.streak, pet.gm.steals, pet.gm.brag, pet.gm.chest, playing(pet)],
    ['snatch', '谢谢观众1！\n箱子归我了～', 0, 1, false, 0, 'chestRobbed']);
  // sorry about her empty chest till it is up and away with it, she starts, reaches after it, and kneels crying
  play(pet, 5000, st => poses.add(st.pose));
  assert.deepEqual([pet.gm.visit, [...poses]], [null, ['sorry', 'startled', 'reachUp', 'robbed']]);
});

test('over an empty chest still in the air one gift decides it there and then: a 人气票 knocks it down and off, a 小花花 has it fly off with the chest', () => {
  const knocked = flying(0);
  knocked.gift(tickets(1));
  assert.deepEqual([knocked.gm.visit.phase, playing(knocked), knocked.cur.opts.nickname, knocked.gm.chest, knocked.gm.streak], ['fled', 'pranksterKnocked', '观众1', 1, 0]);
  const taken = flying(0);
  taken.gm.streak = 2;
  taken.gift(flowers(1));
  assert.deepEqual([taken.gm.visit.phase, taken.gm.visit.say.text, playing(taken), taken.gm.streak, taken.gm.brag], ['away', '谢谢观众1！\n箱子归我了～', 'chestRobbed', 0, false]);
});

test('a 人气票 or 小花花 sent while it only peeks in counts once it flies in with the chest on its rope', () => {
  const pet = gamePet(character), empty = gamePet(character);
  pet.gm.chest = 500;
  for (const [p, gift] of [[pet, tickets(1, 100)], [empty, flowers(1)]]) {
    p.summon();
    play(p, 1000);
    p.gift(gift);
    assert.deepEqual([p.gm.visit.phase, p.gm.visit.red, p.gm.visit.chest, playing(p)], ['peek', 0, null, null]);
    play(p, 2000);
  }
  assert.deepEqual([pet.gm.visit.phase, pet.gm.visit.red, playing(pet)], ['fled', 100, 'chestKept']);
  assert.deepEqual([empty.gm.visit.phase, playing(empty)], ['away', 'chestRobbed']);
});

test('at 0:00 the tug waits five seconds for what is late on the way, then the side the line is on wins, a tie the 捣蛋鬼\'s', () => {
  const outcome = sent => {
    const pet = tugging();
    countdown(pet, 0);
    play(pet, 4000);
    assert.deepEqual([pet.gm.visit.phase, pet.gm.visit.left], ['tug', 0]);
    sent(pet);
    play(pet, 1500);
    return [pet.gm.visit.phase, playing(pet)];
  };
  assert.deepEqual(outcome(() => {}), ['lost', 'chestRobbed'], '0 : 0');
  assert.deepEqual(outcome(pet => pet.gift(tickets(1))), ['won', 'chestKept'], '1 : 0, the one sent in the grace');
  assert.deepEqual(outcome(pet => { pet.gift(tickets(1, 3)); pet.gift(flowers(2, 3)); }), ['lost', 'chestRobbed'], '3 : 3');
  assert.deepEqual(outcome(pet => pet.gift(flowers(2))), ['lost', 'chestRobbed'], '0 : 1');
});

test('a 绝杀 is a tug decided by a gift that came in its last ten seconds or the grace, not by a lead held from before', () => {
  // each gift at so much of the countdown left (the grace 1 s in: -1000), then on to the result
  const ended = (...sent) => {
    const pet = tugging();
    for (const [left, gift] of sent) { countdown(pet, Math.max(0, left)); play(pet, Math.max(0, -left)); pet.gift(gift); }
    for (let i = 0; i < 1000 && !pet.gm.visit.result; i++) play(pet, 100);
    // who scored it, on which side, the kind of line she says of it and what the 捣蛋鬼 says
    const kind = ['chestKept', 'chestKeptBuzzer', 'chestRobbed'].find(k => character.lines[k].includes(pet.cur.line));
    return [pet.gm.visit.phase, pet.gm.buzzer?.side ?? null, pet.gm.buzzer?.w.name ?? null, kind, pet.gm.visit.say.text];
  };
  const flees = '你们给我\n等着——！';
  assert.deepEqual(ended([30000, tickets(1, 5)]), ['won', null, null, 'chestKept', flees], 'ahead since half a minute before');
  assert.deepEqual(ended([5000, tickets(1, 5)]), ['won', 1, '观众1', 'chestKeptBuzzer', flees], 'put ahead 5 s before the end');
  assert.deepEqual(ended([30000, flowers(3, 5)], [-1000, tickets(2, 6)]), ['won', 1, '观众2', 'chestKeptBuzzer', flees], 'turned in the grace');
  assert.deepEqual(ended([30000, tickets(1, 5)], [5000, flowers(2, 6)]), ['lost', -1, '观众2', 'chestRobbed', '多谢观众2！\n箱子归我啦～'], 'turned its way 5 s before the end');
  assert.deepEqual(ended([5000, tickets(3, 100)]), ['won', 1, '观众3', 'chestKeptBuzzer', flees], 'a 秒杀 5 s before the end');
  assert.deepEqual(ended([30000, flowers(2, 5)]), ['lost', null, null, 'chestRobbed', '多谢款待，\n下次再来！'], 'behind since half a minute before');
});

test('in the last ten seconds the 捣蛋鬼 hauls the chest off and she is dragged along while she is not ahead; ahead, they pull on as before', () => {
  const pet = tugging(), now = () => { let her; play(pet, 2000, st => { her = st.pose; }); return [her, pet.gl.pk.pose]; };
  pet.gift(tickets(1, 5));
  countdown(pet, 9000);
  // (its bye is for the first time it really hauls the chest off)
  assert.deepEqual([...now(), pet.gm.visit.bye ?? false], ['tug', 'tug', false]);
  pet.gift(flowers(2, 6));
  assert.deepEqual([...now(), pet.gm.visit.say.text], ['dragged', 'haul', character.lines.pranksterSprint[0]]);
  pet.gift(tickets(1, 2));
  assert.deepEqual(now(), ['tug', 'tug']);
});

test('five 小花花 in one message flatten her, falling as one', () => {
  const pet = tugging(), poses = new Set();
  let drops = 0;
  pet.gift(flowers(5, 5));
  play(pet, 4000, st => { poses.add(st.pose); drops = Math.max(drops, pet.gm.drops.length); });
  assert.deepEqual([poses.has('flat'), drops], [true, 1]);
});

test('人气票 and 小花花 thrown at the 捣蛋鬼 play nothing of hers, then or after it leaves', () => {
  const pet = visited();
  pet.gift(tickets(1, 3));
  pet.gift(flowers(2));
  pet.receive(state(pet, { pk: true }));
  play(pet, 3000);
  assert.deepEqual([pet.gm.visit, playing(pet)], [null, null]);
  pet.gift(gifts(1, 100));
  assert.equal(playing(pet), 'gift');
});

test('any other gift plays nothing while the 捣蛋鬼 is here, and its 电池 go into her chest only once it has gone', () => {
  const pet = tugging();
  pet.gift(other(1, 30000));
  play(pet, 1000);
  assert.deepEqual([playing(pet), pet.gm.visit.phase, pet.gm.chest, pet.gm.visit.red, pet.gm.drops.length], [null, 'tug', 500, 0, 0]);
  pet.receive(state(pet, { pk: true }));
  play(pet, 3000);
  assert.deepEqual([pet.gm.visit, pet.gm.chest], [null, 500 + 300]);
});

test('a viewer\'s 一轮送礼 that comes to a 中礼物 with such gifts plays its show, which scares the 捣蛋鬼 off, what the round sent before the 来访 counting too', () => {
  const pet = gamePet(character);
  // 400 电池 before it comes, thanked as ever; then 300 and 300 more of the same round while it is here
  pet.gift(other(1, 40000));
  pet.summon();
  play(pet, 15000);
  assert.deepEqual([playing(pet), pet.gm.visit.phase], [null, 'tug']);
  pet.gift(other(1, 30000));
  pet.gift(other(2, 90000));
  play(pet, 2000);
  assert.deepEqual([playing(pet), pet.gm.visit.phase], [null, 'tug']);
  pet.gift(other(1, 30000));
  play(pet, 2000);
  assert.deepEqual([playing(pet), pet.cur.opts.nickname, pet.gm.visit.phase], ['midGift', '观众1', 'fled']);
});

test('blind boxes sent while the 捣蛋鬼 is here wait unshaken for their 盲盒播报: short of a 中礼物 they play nothing, at one they play and scare it off', () => {
  const marks = new Set(), seen = st => { for (const [fx] of st.fx) marks.add(fx); };
  const small = tugging();
  small.gift(box(2, 3));
  play(small, 1000, seen);
  small.blindBox(opened(2, 3));
  play(small, 1000, seen);
  assert.deepEqual([playing(small), small.gm.visit.phase, marks.has('shakeMarks'), small.gm.chest], [null, 'tug', false, 500]);
  const mid = tugging();
  mid.gift(box(2, 10));
  mid.blindBox(opened(2, 10));
  play(mid, 2000);
  assert.deepEqual([playing(mid), mid.gm.visit.phase], ['blindBoxMedium', 'fled']);
});

test('boxes whose 盲盒播报 has not come when the 捣蛋鬼 is gone she shakes then, and they open as ever', () => {
  const pet = tugging(), poses = new Set();
  pet.gift(box(2));
  pet.receive(state(pet, { pk: true }));
  play(pet, 2000, st => poses.add(st.pose));
  assert.deepEqual([pet.gm.visit?.phase, poses.has('shakeBox')], ['fled', false]);
  play(pet, 8000, st => poses.add(st.pose));
  assert.deepEqual([pet.gm.visit, poses.has('shakeBox'), pet.gm.chest], [null, true, 500 + 150]);
  pet.blindBox(opened(2, 1));
  assert.equal(playing(pet), 'blindBoxSmall');
});

test('a castle out of a blind box sent while the 捣蛋鬼 is here scares it off when its 盲盒播报 comes and plays, as each castle after it does', () => {
  const pet = visited({ off: NO_DAILY }), [shows, seen] = shown(pet);
  pet.gift(box(2, 2, { name: '浪漫城堡', castle: true }));
  play(pet, 500, seen);
  assert.equal(playing(pet), null);
  pet.blindBox({ ...castled, boxes: 2, castles: 2 });
  play(pet, 30000, seen);
  assert.deepEqual([pet.gm.visit, [...shows].map(show => show.name)], [null, ['castle', 'castle']]);
});

test('a castle played in a 一轮送礼 begun before the 捣蛋鬼 came is played again for one sent in the round while it is here', () => {
  const pet = gamePet(character, { off: NO_DAILY }), [shows, seen] = shown(pet);
  pet.gift(castleBox());
  pet.blindBox(castled);
  play(pet, 14000, seen);
  pet.summon();
  play(pet, 100, seen);
  assert.equal(pet.gm.visit?.phase, 'peek');
  pet.gift(castleBox());
  pet.blindBox(castled);
  play(pet, 30000, seen);
  assert.deepEqual([...shows].map(show => show.name).filter(name => name === 'castle'), ['castle', 'castle']);
});

test('every 人气票 and 小花花 thrown goes into her chest, those still to land when the tug ends too', () => {
  const empty = visited();
  for (const [uid, n] of [[1, 1], [2, 2], [3, 3]]) empty.gift(tickets(uid, n));
  play(empty, 20000);
  const tug = tugging();
  tug.gift(flowers(2, 5));
  tug.gift(tickets(1, 105));
  play(tug, 8000);
  assert.deepEqual([empty.gm.chest, tug.gm.chest], [1 + 2 + 3, 500 + 5 + 105]);
});

test('what is thrown and still to fall when the 捣蛋鬼 is scared off goes into her chest, during the lightning too', () => {
  const pet = visited();
  pet.gift(tickets(1, 10));
  pet.receive(state(pet, { pk: true }));
  play(pet, 6000);
  const struck = zapped();
  struck.gift(flowers(2, 10));
  struck.receive(state(struck, { pk: true }));
  play(struck, 8000);
  assert.deepEqual([pet.gm.chest, struck.gm.visit, struck.gm.chest], [10, null, 500 + 55 + 10]);
});

test('a new first place taken by a gift thrown at the 捣蛋鬼 is crowned once it has gone and the chest is home, the 礼物榜 telling of it before the gift or after', () => {
  for (const crownFirst of [false, true]) {
    const pet = gamePet(character, { off: NO_DAILY }), [shows, seen] = shown(pet);
    if (crownFirst) pet.crown({ uid: 9, nickname: '观众9' });
    pet.summon();
    play(pet, 100);
    pet.gift(tickets(9));
    if (!crownFirst) pet.crown({ uid: 9, nickname: '观众9' });
    let crowned = null;
    play(pet, 20000, () => { seen(); if (playing(pet) === 'newFirst') crowned ??= [pet.gm.visit, pet.gm.out]; });
    assert.deepEqual([[...shows].map(show => [show.name, show.opts.nickname]), crowned], [[['pranksterKnocked', '观众9'], ['newFirst', '观众9']], [null, null]],
      crownFirst ? 'the 礼物榜 before it came' : 'the 礼物榜 while it was here');
  }
});

test('a new first place taken by blind boxes that play nothing while the 捣蛋鬼 is here is crowned once it has gone', () => {
  // (the 礼物榜 telling of it before the 捣蛋鬼 came, the boxes' message after)
  const pet = gamePet(character, { off: NO_DAILY }), [shows, seen] = shown(pet);
  pet.gm.chest = 500;
  pet.crown({ uid: 9, nickname: '观众9' });
  pet.summon();
  play(pet, 12500);
  pet.gift(box(9, 2));
  pet.blindBox(opened(9, 2));
  pet.receive(state(pet, { pk: true }));
  play(pet, 20000, seen);
  assert.deepEqual([...shows].map(show => [show.name, show.opts.nickname]), [['newFirst', '观众9']]);
});

test('while a 来访 is wound up the shows that come wait their turn and cut in on nothing, then play the highest first: just after she wins, just after she is robbed, after a PK scared the 捣蛋鬼 off, and after a 舰长 did', () => {
  const ends = {
    'chestKept': pet => pet.gift(tickets(1, 100)),
    'chestRobbed': pet => pet.gift(flowers(1, 200)),
    'a PK': pet => pet.receive(state(pet, { pk: true })),
    'captain': pet => { pet.guard(captain); play(pet, 1500); }
  };
  // (each after the lightning, so that her hair is to be smoothed too)
  for (const [first, end] of Object.entries(ends)) {
    const pet = zapped({ off: NO_DAILY }), [shows, seen] = shown(pet);
    end(pet);
    play(pet, 500, seen);
    // a 中礼物 and then a 总督, and a plain gift, which is let go
    pet.gift(other(4, 100000));
    pet.guard({ uid: 3, nickname: '总督', guardLevel: 1, spend: 19998000 });
    pet.gift(other(5, 100));
    const waits = [pet.queue.map(q => q.name), pet.cur !== null && !pet.cur.opts.game && pet.now() >= pet.cur.start + pet.cur.pre], tidying = [];
    play(pet, 45000, () => { seen(); if (['governor', 'midGift'].includes(playing(pet)) && (pet.gm.visit || pet.gm.out || pet.gm.fix)) tidying.push(pet.now()); });
    const played = [...shows], whole = played.slice(1).every((show, i) => show.start >= played[i].start + played[i].pre + played[i].dur - 20);
    assert.deepEqual([waits, played.map(show => show.name), whole, tidying],
      [[['midGift', 'governor'], false], [...first === 'a PK' ? [] : [first], 'governor', 'midGift'], true, []], first);
  }
});

test('a gift let go while she is busy being robbed does not cheer her up; one whose show plays does', () => {
  const pet = tugging(500, { off: NO_DAILY });
  pet.gift(flowers(1, 100));
  play(pet, 500);
  pet.gift(flowers(2));
  until(pet, 'chestRobbed');
  play(pet, 6000);
  assert.deepEqual([playing(pet), pet.mood(pet.now())], [null, 'gloomy']);
  pet.gift(flowers(3));
  assert.deepEqual([playing(pet), pet.mood(pet.now()) === 'gloomy'], ['gift', false]);
});

test('while the 捣蛋鬼 is here nothing wakes her first, though the room was quiet before it came', () => {
  const pet = tugging();
  pet.lastGift = pet.lastAny = pet.now() - 46 * 60000;
  pet.receive(state(pet, { lottery: { mode: 'Entry', id: 'E1', keyword: '抽奖', phase: 'Open', entryCount: 0, draws: [], drawUntil: null } }));
  assert.deepEqual([playing(pet), pet.cur.wakeLine], ['lottery', null]);
});

test('what the 捣蛋鬼 opens with tells of its last 来访 only: whoever hit it then is named once', () => {
  const pet = tugging(500, { off: NO_DAILY }), said = [];
  pet.gift(tickets(4, 3));
  play(pet, 3000);
  for (const _ of [1, 2]) {
    pet.receive(state(pet, { pk: true }));
    play(pet, 9000);
    pet.receive(state(pet, { pk: false }));
    pet.summon();
    play(pet, 100);
    said.push(pet.gm.visit.say.text);
  }
  assert.deepEqual(said, ['上次砸我的\n观众4在吗？', '嘘——\n别告诉她']);
});

test('with the room disconnected the 捣蛋鬼 does not come on its own', () => {
  const pet = gamePet(character, { pranksterMinutes: 1 }), told = reports(pet);
  play(pet, 30000);
  pet.danmaku({ uid: 1, nickname: '观众1', side: 0 });
  pet.receive(state(pet, { roomConnected: false }));
  play(pet, 40000);
  assert.deepEqual([pet.gm.visit, told], [null, []]);
});

test('叫它来 waits while she shakes a viewer\'s blind box for its 盲盒播报, and brings the 捣蛋鬼 once the box\'s show is over', () => {
  const pet = gamePet(character), told = reports(pet), inTug = new Set();
  pet.gift(box(2));
  pet.summon();
  play(pet, 3000);
  pet.gift(round(2, { ended: true }));
  play(pet, 5000);
  assert.deepEqual(told, []);
  pet.blindBox({ uid: 2, nickname: '观众2', tally: { spend: 15000, openedValue: 16000, outcome: 'Gain' }, boxes: 1, bestGift: '爱心抱枕', bestPrice: 16000 });
  assert.deepEqual([playing(pet), told], ['blindBoxSmall', []]);
  play(pet, 2000);
  assert.deepEqual(told, ['start']);
  // nothing of the round is drawn on her in the tug: no shaking, no gift flying into her arms
  play(pet, 15000, st => { if (pet.gm.visit?.phase === 'tug') for (const [fx] of st.fx) inTug.add(fx); });
  assert.deepEqual([...inTug].filter(fx => ['shakeMarks', 'giftArc'].includes(fx)), []);
});

test('on its own the 捣蛋鬼 comes at most twice an hour, and 叫它来 brings it all the same', () => {
  const pet = gamePet(character, { pranksterMinutes: 1 }), told = reports(pet), comes = () => told.filter(t => t === 'start').length;
  const busy = ms => { for (const end = pet.now() + ms; pet.now() < end;) { pet.danmaku({ uid: 1, nickname: '观众1', side: 0 }); play(pet, 20000); } };
  busy(6 * 60000);
  assert.equal(comes(), 2);
  pet.summon();
  play(pet, 1000);
  assert.equal(comes(), 3);
  busy(60 * 60000);
  assert.equal(comes(), 5);
});

test('a 试演 taking over a show that scared the 捣蛋鬼 off waits as it did, for the chest to go home and her hair', () => {
  const pet = tugging(), struck = zapped(), early = [];
  for (const p of [pet, struck]) {
    p.testShow({ show: 'captain', variant: null, nickname: '舰长', id: 1 });
    play(p, 500);
    p.testShow(governor);
  }
  assert.deepEqual([playing(struck), struck.cur.opts.frizz], ['governor', 'cheer']);
  play(pet, 12000, () => { if (pet.gm.out && pet.now() >= pet.cur.start + pet.cur.pre) early.push(pet.now()); });
  assert.deepEqual([playing(pet), early], ['governor', []]);
});

test('a 试演 coming while the chest is still going home from a PK that scared the 捣蛋鬼 off waits for it, and her hair', () => {
  const pet = tugging(), struck = zapped();
  for (const p of [pet, struck]) {
    p.receive(state(p, { pk: true }));
    play(p, 500);
    p.testShow({ show: 'captain', variant: null, nickname: '舰长', id: 1 });
  }
  assert.deepEqual([playing(struck), struck.cur.opts.frizz, struck.gm.fix], ['captain', 'cheer', null]);
  for (const p of [pet, struck]) {
    const early = [];
    play(p, 12000, () => { if (p.gm.out && p.cur && p.now() >= p.cur.start + p.cur.pre) early.push(p.now()); });
    assert.deepEqual(early, []);
  }
});

test('a 试演 stopped, or replaced by a 心情\'s, while it waits for her hair leaves the sprite patting it smooth where it was', () => {
  for (const next of [p => p.stopTestShow(), p => p.testShow({ show: 'bored', variant: null, nickname: '', id: 2 })]) {
    const pet = zapped(), poses = [];
    pet.testShow({ show: 'captain', variant: null, nickname: '星河', id: 1 });
    play(pet, 500);
    const { start } = pet.cur;
    next(pet);
    assert.deepEqual([playing(pet), pet.gm.fix], [null, { start, pose: 'cheer' }]);
    play(pet, 9000, st => poses.push(st.pose));
    assert.deepEqual([poses[0], poses.includes('cheer'), pet.gm.fix], ['cheerZap', true, null]);
  }
});

test('after the lightning and a PK, with the chest home and her hair still frizzed, a show and blind boxes wait for her hair', () => {
  for (const next of [p => p.guard(captain), p => p.gift(box(2))]) {
    const pet = zapped(), poses = [];
    pet.receive(state(pet, { pk: true }));
    play(pet, 3000);
    pet.gm.out = null;   // the chest home (the page's sprite is not drawn here to take it)
    next(pet);
    play(pet, 10000, st => { if (!pet.cur || pet.now() < pet.cur.start + pet.cur.pre) poses.push(st.pose); });
    assert.deepEqual([poses[0], poses.includes('cheer'), pet.gm.fix], ['cheerZap', true, null]);
    assert.ok(pet.cur || pet.shaking());
  }
});

test('a 试演 taking over 守住了 or 被偷了 after the lightning waits for her hair if the sprite has not patted it smooth yet, cheering on or kneeling as she was, and not after', () => {
  const after = show => {
    const pet = zapped();
    // beaten off there and then, or else the countdown runs out with the 捣蛋鬼 ahead
    pet.gift(show === 'chestKept' ? tickets(2, 100) : flowers(3, 60));
    until(pet, show);
    play(pet, 1000);
    assert.deepEqual([playing(pet), pet.cur.opts.zap], [show, true]);
    return pet;
  };
  // (the sprite is not drawn here, so her hair is smooth at the latest each show has it: smooth)
  for (const [show, smooth, pose] of [['chestKept', 5000, 'cheer'], ['chestRobbed', 3500, 'robbed']]) {
    const early = after(show), late = after(show), poses = new Set();
    early.testShow(governor);
    play(early, 9000, st => { if (early.now() < early.cur.start + early.cur.pre) poses.add(st.pose); });
    assert.deepEqual([show, playing(early), early.cur.opts.frizz, [...poses]], [show, 'governor', pose, [pose + 'Zap', pose]]);
    play(late, smooth);
    late.testShow(governor);
    assert.deepEqual([show, playing(late), late.cur.opts.frizz], [show, 'governor', null]);
  }
});

test('a 试演 taking over 被偷了 has her say on its line while it waits, until 被偷了 would have stopped it', () => {
  const pet = zapped();
  pet.gift(flowers(3, 60));
  until(pet, 'chestRobbed');
  const robbed = pet.cur.start + pet.cur.pre;
  let line;
  play(pet, 1000, st => { line = st.bubble?.text; });
  pet.testShow(governor);
  const said = [];
  play(pet, 5000, st => said.push([pet.now() - robbed, st.bubble?.text ?? null]));
  const at = from => said.filter(([t]) => t >= from && t < from + 1000).map(([, text]) => text);
  assert.deepEqual([playing(pet), typeof line, new Set(at(1100)), new Set(at(4610))], ['governor', 'string', new Set([line]), new Set([null])]);
});

test('a 试演 taking over 守住了 or 打飞 waits for the chest cheering on as she was, one over a show that scared the 捣蛋鬼 off standing', () => {
  const ways = {
    chestKept: pet => { pet.gm.chest = 500; pet.summon(); play(pet, 12500); pet.gift(tickets(2, 100)); },
    pranksterKnocked: pet => { pet.summon(); play(pet, 16000); pet.gift(tickets(1)); },
    // the 试演 that scares it off out of the tug
    captain: pet => { pet.gm.chest = 500; pet.summon(); play(pet, 12500); pet.testShow({ show: 'captain', variant: null, nickname: '舰长', id: 1 }); },
  };
  for (const [show, way] of Object.entries(ways)) {
    const pet = gamePet(character), poses = new Set();
    way(pet);
    for (let i = 0; i < 100 && playing(pet) !== show; i++) play(pet, 100);
    play(pet, 500);
    pet.testShow(governor);
    play(pet, 9000, st => { if (pet.now() < pet.cur.start + pet.cur.pre) poses.add(st.pose); });
    assert.deepEqual([show, playing(pet), pet.cur.opts.tidy > 0, [...poses]], [show, 'governor', true, [show === 'captain' ? 'idle' : 'cheer']]);
  }
});

test('her hair patted smooth stays smooth for a 试演 coming while its glitter settles, after a show waiting for it or a PK', () => {
  const scares = { captain: p => p.testShow({ show: 'captain', variant: null, nickname: '舰长', id: 1 }), pk: p => p.receive(state(p, { pk: true })) };
  for (const [way, scare] of Object.entries(scares)) {
    const pet = zapped(), after = [];
    scare(pet);
    let smooth = null;
    for (let i = 0; i < 1000 && smooth == null; i++) play(pet, 17, st => { if (st.pose === 'cheer') smooth = pet.now(); });
    play(pet, 800);
    pet.testShow(governor);
    play(pet, 3000, st => after.push(st.pose));
    assert.deepEqual([way, typeof smooth, playing(pet), pet.cur.opts.frizz, after.includes('cheerZap')], [way, 'number', 'governor', null, false]);
  }
});

test('the game\'s own ending does not wake her: robbed after the room was quiet she kneels crying once it is off with the chest', () => {
  const pet = tugging(), poses = new Set();
  pet.lastGift = pet.lastAny = pet.now() - 46 * 60000;
  until(pet, 'chestRobbed');
  play(pet, 100, st => poses.add(st.pose));
  assert.deepEqual([pet.cur.wakeLine, pet.gm.visit, [...poses]], [null, null, ['robbed']]);
});

test('robbed after the room was quiet, the first gift while she is sad over it cheers her up at once, not waking her first', () => {
  const pet = tugging();
  pet.lastGift = pet.lastAny = pet.now() - 46 * 60000;
  until(pet, 'chestRobbed');
  play(pet, 7000);
  assert.deepEqual([playing(pet), pet.mood(pet.now())], [null, 'gloomy']);
  pet.gift(gifts(1, 100));
  play(pet, 100);
  assert.deepEqual([playing(pet), pet.cur.wakeLine, pet.cur.pre], ['gift', null, 0]);
});

test('叫它来 right after a PK scared the 捣蛋鬼 off brings it once the chest it let go of is home', () => {
  const pet = tugging(), told = reports(pet);
  pet.receive(state(pet, { pk: true }));
  play(pet, 3000);
  pet.receive(state(pet, { pk: false }));
  pet.summon();
  play(pet, 100);
  assert.deepEqual([pet.gm.visit, !!pet.gm.out, told], [null, true, ['end']]);
  play(pet, 8000);
  assert.deepEqual([!!pet.gm.visit, told], [true, ['end', 'start']]);
});

test('the 捣蛋鬼 due while a 试演 plays does not come once the room is disconnected, and is reported gone', () => {
  const pet = gamePet(character, { pranksterMinutes: 1 }), told = reports(pet);
  pet.testShow({ show: 'bored', variant: null, nickname: '', id: 1 });
  play(pet, 61000);
  pet.receive(state(pet, { roomConnected: false }));
  pet.stopTestShow();
  play(pet, 1000);
  assert.deepEqual([pet.gm.visit, told], [null, ['end']]);
});

test('叫它来 brings the 捣蛋鬼 once her show is over, the viewer\'s 一轮送礼 going on or not', () => {
  const pet = gamePet(character), told = reports(pet);
  pet.summon();
  for (let n = 1; n <= 4; n++) {
    pet.gift(gifts(1, 100));
    play(pet, 2000);
  }
  assert.deepEqual([playing(pet), told], [null, ['start']]);
});

test('a gift sent after the 来访 is thanked for itself, not for one thrown at the 捣蛋鬼 earlier in the 一轮送礼', () => {
  const pet = visited();
  pet.gift(tickets(1, 5));
  pet.receive(state(pet, { pk: true }));
  play(pet, 3000);
  pet.gift(gifts(1, 100));
  assert.deepEqual([playing(pet), pet.cur.opts.gift, pet.cur.opts.count], ['gift', '小花花', 1]);
});

test('a new first place taken while the 捣蛋鬼 is here in a 一轮送礼 begun before it came is crowned once it has gone, and not again after the round\'s next show', () => {
  const pet = gamePet(character, { off: NO_DAILY }), [shows, seen] = shown(pet);
  pet.gift(gifts(9, 100));
  pet.summon();
  play(pet, 2500, seen);
  assert.equal(pet.gm.visit?.phase, 'peek');
  pet.gift(tickets(9));
  pet.crown({ uid: 9, nickname: '观众9' });
  pet.receive(state(pet, { pk: true }));
  play(pet, 8000, seen);
  pet.receive(state(pet, { pk: false }));
  pet.gift(other(9, 100000));
  play(pet, 8000, seen);
  assert.deepEqual([...shows].map(show => show.name), ['gift', 'newFirst', 'midGift']);
});

test('the room disconnected while the 捣蛋鬼 is here, it runs off with nothing said, and nothing of the 来访 is counted or played after', () => {
  const pet = gamePet(character, { off: NO_DAILY }), told = reports(pet), shows = new Set();
  pet.gm.chest = 5000;
  pet.summon();
  play(pet, 16000);
  pet.gift(tickets(1, 20));
  play(pet, 1000);
  pet.gift(tickets(1, 10));
  pet.gift(other(2, 30000));
  pet.crown({ uid: 2, nickname: '观众2' });
  pet.receive(state(pet, { roomConnected: false }));
  assert.deepEqual([pet.gm.visit.phase, pet.gm.visit.say], ['fled', null]);
  pet.receive(state(pet));
  play(pet, 20000, () => shows.add(playing(pet)));
  const { chest, pile, streak, grudge } = pet.gm;
  assert.deepEqual([pet.gm.visit, told, chest, pile, streak, grudge, [...shows]], [null, ['start', 'end'], 0, 0, 0, null, [null]]);
});

test('the room disconnected while a gift\'s light is on its way to the 捣蛋鬼, the gift and what waited behind it play nothing after and put nothing in the chest', () => {
  const pet = tugging(500, { off: NO_DAILY }), shows = new Set();
  pet.guard(captain);
  pet.guard(captain);
  assert.deepEqual([pet.gm.burst.due > pet.now(), pet.gm.burst.more.length], [true, 1]);
  pet.receive(state(pet, { roomConnected: false }));
  pet.receive(state(pet));
  play(pet, 20000, () => shows.add(playing(pet)));
  assert.deepEqual([pet.gm.chest, pet.gm.pile, [...shows]], [0, 0, [null]]);
});

test('a danmaku that is just 1 or 2 puts its viewer on a side, the last one said standing; one who sent a 人气票 or 小花花 stays where the gift put them', () => {
  const pet = tugging(), sides = () => Object.fromEntries([...pet.gm.visit.sides].map(([uid, on]) => [uid, `${on.side} by ${on.by}`]));
  pet.danmaku({ uid: 5, nickname: '观众5', side: 1 });
  pet.danmaku({ uid: 6, nickname: '观众6', side: -1 });
  pet.danmaku({ uid: 5, nickname: '观众5', side: -1 });
  pet.gift(tickets(7));
  pet.danmaku({ uid: 7, nickname: '观众7', side: -1 });
  // (one B站 tells with no UID is nobody to put on a side, a danmaku that is neither 1 nor 2 picks none)
  pet.danmaku({ uid: 0, nickname: '', side: 1 });
  pet.danmaku({ uid: 8, nickname: '观众8', side: 0 });
  assert.deepEqual(sides(), { 5: '-1 by dm', 6: '-1 by dm', 7: '1 by gift' });
});

test('in the tug a danmaku of one on a side throws a paper ball at the other side, and one on no side is called out by name, once a 来访 and eight seconds apart; none moves the line', () => {
  const pet = tugging(500, { luckyPercent: 0 }), v = pet.gm.visit, named = () => ['观众5', '观众6', '观众0'].find(name => v.say.text.startsWith(name));
  // (past what it says of its own as the tug begins)
  play(pet, 10000);
  pet.danmaku({ uid: 0, nickname: '观众0', side: 0 });
  assert.equal(named(), undefined);
  pet.danmaku({ uid: 5, nickname: '观众5', side: 0 });
  pet.danmaku({ uid: 6, nickname: '观众6', side: 0 });
  assert.deepEqual([named(), character.lines.pranksterTaunt.map(line => line.replace('{昵称}', '观众5').replace(' ', '\n')).includes(v.say.text)], ['观众5', true]);
  play(pet, 9000);
  pet.danmaku({ uid: 5, nickname: '观众5', side: 0 });
  assert.equal(named(), '观众5');
  pet.danmaku({ uid: 6, nickname: '观众6', side: 0 });
  assert.equal(named(), '观众6');
  pet.danmaku({ uid: 6, nickname: '观众6', side: 1 });
  pet.danmaku({ uid: 6, nickname: '观众6', side: 0 });
  pet.danmaku({ uid: 5, nickname: '观众5', side: -1 });
  assert.deepEqual([v.balls.map(ball => ball.side), v.red, v.blue, v.net, pet.lineX(v)], [[1, 1, -1], 0, 0, 0, 240]);
});

test('five paper balls of one side\'s landing within five seconds have the one they hit answer back to whoever threw the last', () => {
  const pelted = side => {
    const pet = tugging(500, { luckyPercent: 0 });
    play(pet, 10000);
    for (let uid = 1; uid <= 5; uid++) { pet.danmaku({ uid, nickname: '观众' + uid, side }); play(pet, 200); }
    play(pet, 600);
    return pet;
  };
  const it = pelted(1), her = pelted(-1);
  assert.ok(character.lines.pranksterRetort.map(line => line.replace('{昵称}', '观众5').replace(' ', '\n')).includes(it.gm.visit.say.text), it.gm.visit.say.text);
  assert.equal(her.gm.say.text, '观众5！别丢啦～');
});

test('a 幸运纸团 opens whoever it hits for ten seconds, the other side\'s gifts counting twice with no cap, and the next comes no sooner than five seconds after', () => {
  // (every ball a lucky one; each end 200 票 off, so that what is thrown here sets off no lightning)
  const pet = tugging(4000, { luckyPercent: 100 }), v = pet.gm.visit, thrown = () => { pet.danmaku({ uid: 5, nickname: '观众5', side: 1 }); return v.balls.at(-1).lucky; };
  play(pet, 3000);
  assert.equal(thrown(), true);
  play(pet, 700);
  assert.deepEqual([v.luck.side, Math.round(v.luck.until - v.luck.at), v.say.text], [1, 10000, '哎哟！别送\n人气票啊！']);
  pet.gift(tickets(6, 30));
  pet.gift(flowers(7, 4));
  assert.deepEqual([v.red, v.blue, thrown()], [60, 4, false]);
  play(pet, 10000);
  pet.gift(tickets(6));
  assert.deepEqual([v.luck, v.red, thrown()], [null, 61, false]);
  play(pet, 5000);
  assert.equal(thrown(), true);
});

test('with the 幸运纸团 turned off, at 0 %, paper balls are thrown as ever, none of them lucky, and no double opens', () => {
  const lucky = luckyPercent => {
    const pet = tugging(500, { luckyPercent }), v = pet.gm.visit;
    let balls = 0, lucky = 0, opened = 0;
    play(pet, 3000);
    for (let n = 0; n < 60; n++) {
      pet.danmaku({ uid: 5, nickname: '观众5', side: 1 });
      balls++; lucky += v.balls.at(-1).lucky;
      play(pet, 500, () => { opened += !!v.luck; });
    }
    return [balls, lucky > 0, opened > 0];
  };
  assert.deepEqual([lucky(0), lucky(10)], [[60, false, false], [60, true, true]]);
});

// B站's pictures (pet.remote), each arriving once asked for: the two sides' gift icons, 人气票's and 小花花's, asked for with the
// game's pictures, and a viewer's face, asked for as the bar draws who sent the gift.
globalThis.Image = class { set src(url) { queueMicrotask(() => this.onload()); } };
const ICONS = ['https://s1.hdslb.com/bfs/live/7d78646ee4ecc900cebbe46cb963f421079548f0.png', 'https://s1.hdslb.com/bfs/live/5126973892625f3a43a8290be6b625b5e54261a5.png'];
const pictured = () => { const pet = tugging(); for (const url of ICONS) pet.remote(url); return pet; };
const faced = (pet, gift) => { pet.gift({ ...gift, faceUrl: '/face/' + gift.uid }); pet.remote('/face/' + gift.uid); };
// The pictures the page still holds once those asked for have arrived and the 来访 is over.
const left = async pet => { await new Promise(resolve => setImmediate(resolve)); while (pet.gm.visit) play(pet, 100); return [...pet.icons.keys()]; };

test('a 来访 over, the page lets go of the faces of who joined it and keeps each side\'s gift icon', async () => {
  const pet = pictured();
  faced(pet, flowers(2, 5));
  faced(pet, tickets(1, 105));
  assert.deepEqual(await left(pet), ICONS);
});

test('a 绝杀 that lost her the chest is written up past the 来访\'s end, and the face of whoever scored it is kept for it', async () => {
  const pet = pictured();
  faced(pet, tickets(1, 5));
  countdown(pet, 5000);
  faced(pet, flowers(2, 6));
  assert.deepEqual([await left(pet), pet.gm.buzzer.w.name], [[...ICONS, '/face/2'], '观众2']);
});
