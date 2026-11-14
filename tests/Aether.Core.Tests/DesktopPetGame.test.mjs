// Run with: node --test tests/Aether.Core.Tests/DesktopPetGame.test.mjs
// The 宝箱小游戏 (#372) on the 桌宠页: when the 捣蛋鬼 comes and goes, what scares it off, what goes into her chest, and what
// a 来访 leaves of her shows.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { gamePet, play, playing, round, state } from './Support/DesktopPetGame.mjs';

const character = JSON.parse(readFileSync(new URL('../../src/Aether.Core/Pages/characters/1/character.json', import.meta.url), 'utf8'));
// A viewer's 一轮送礼 so far with the message's gifts.
const gifts = (uid, spend, so) => round(uid, { spend, count: 1, topCount: 1, items: [{ spend, count: 1, blind: false, iconUrl: '' }], ...so });
const box = (uid, boxSpend) => round(uid, { boxSpend, boxes: 1, items: [{ spend: boxSpend, count: 1, blind: true, iconUrl: '' }] });
// What the page reports to Core of the 捣蛋鬼 (page.mjs).
const reports = pet => { const told = []; pet.addEventListener('visit', event => told.push(event.detail)); return told; };
// The 捣蛋鬼 called and on the page, peeking in.
const visited = settings => { const pet = gamePet(character, settings); pet.summon(); play(pet, 100); return pet; };
// From peeking in to the tug over a chest with something in it: 2.4 s peeking, 9 s flying it over, its fall.
const tugging = () => { const pet = gamePet(character); pet.gm.chest = 500; pet.summon(); play(pet, 12500); return pet; };

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
  pet.guard({ uid: 1, nickname: '舰长', guardLevel: 3, spend: 198000 });
  pet.gift(box(2, 15000));
  assert.equal(pet.gm.chest, 1980 + 150);
});

test('a 大航海 whose show is off scares the 捣蛋鬼 off all the same and plays nothing', () => {
  const pet = visited({ off: ['captain'] });
  pet.guard({ uid: 1, nickname: '舰长', guardLevel: 3, spend: 198000 });
  assert.deepEqual([pet.gm.visit.phase, playing(pet)], ['fled', null]);
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
    const pet = gamePet(character, { off: [...off, 'hum', 'gym', 'hike', 'bike', 'ski'] });
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

test('a viewer sending the 帮它的关键字 twice in the tug is one helper', () => {
  const pet = tugging();
  assert.equal(pet.gm.visit.phase, 'tug');
  pet.danmaku({ uid: 5, nickname: '观众5', joins: true });
  pet.danmaku({ uid: 5, nickname: '观众5', joins: true });
  pet.danmaku({ uid: 6, nickname: '观众6', joins: false });
  assert.deepEqual([...pet.gm.visit.helpers], [5]);
});

test('gifts and blind boxes thrown at the 捣蛋鬼 play nothing of hers, then or after it leaves', () => {
  const pet = visited();
  pet.gift(gifts(1, 100000));
  pet.gift(box(2, 15000));
  pet.receive(state(pet, { pk: true }));
  play(pet, 3000);
  assert.deepEqual([pet.gm.visit, playing(pet)], [null, null]);
  pet.blindBox({ uid: 2, nickname: '观众2', tally: { spend: 15000, openedValue: 16000, outcome: 'Gain' }, boxes: 1, bestGift: '爱心抱枕', bestPrice: 16000 });
  assert.equal(playing(pet), null);
  pet.gift(gifts(1, 100100, { count: 2, items: [{ spend: 100, count: 1, blind: false, iconUrl: '' }] }));
  assert.equal(playing(pet), 'gift');
});

test('every gift thrown goes into her chest, those landing after the one that beat the 捣蛋鬼 off too', () => {
  const empty = visited();
  for (const [uid, spend] of [[1, 10000], [2, 20000], [3, 30000]]) empty.gift(gifts(uid, spend));
  play(empty, 20000);
  const tug = tugging();
  tug.gift(gifts(1, tug.settings.midGift));
  tug.gift(gifts(2, 5000));
  play(tug, 8000);
  assert.deepEqual([empty.gm.chest, tug.gm.chest], [100 + 200 + 300, 500 + 1000 + 50]);
});

test('gifts still to fall when the 捣蛋鬼 is scared off go into her chest, during the lightning too', () => {
  const pet = visited();
  pet.gift(gifts(1, 100000));
  pet.receive(state(pet, { pk: true }));
  play(pet, 6000);
  const zapped = tugging();
  zapped.gift(gifts(1, 6000));
  play(zapped, 3000);
  assert.notEqual(zapped.gm.visit.cut, null);
  zapped.gift(gifts(2, 1000));
  zapped.receive(state(zapped, { pk: true }));
  play(zapped, 8000);
  assert.deepEqual([pet.gm.chest, zapped.gm.visit, zapped.gm.chest], [1000, null, 500 + 60 + 10]);
});

test('a new first place taken by a gift thrown at the 捣蛋鬼 is not crowned after it leaves', () => {
  for (const crownFirst of [false, true]) {
    const pet = gamePet(character), seen = new Set();
    if (crownFirst) pet.crown({ uid: 9, nickname: '观众9' });
    pet.summon();
    play(pet, 100);
    pet.gift(gifts(9, 100));
    if (!crownFirst) pet.crown({ uid: 9, nickname: '观众9' });
    pet.receive(state(pet, { pk: true }));
    play(pet, 8000);
    pet.receive(state(pet, { pk: false }));
    pet.gift(round(9, { ended: true }));
    pet.gift(gifts(9, 200, { count: 2, items: [{ spend: 100, count: 1, blind: false, iconUrl: '' }] }));
    play(pet, 8000, () => seen.add(playing(pet)));
    assert.deepEqual([...seen].filter(Boolean), ['gift'], crownFirst ? 'crowned before it came' : 'crowned while it was here');
  }
});

test('with the room disconnected the 捣蛋鬼 does not come on its own', () => {
  const pet = gamePet(character, { pranksterMinutes: 1 }), told = reports(pet);
  play(pet, 30000);
  pet.danmaku({ uid: 1, nickname: '观众1', joins: false });
  pet.receive(state(pet, { roomConnected: false }));
  play(pet, 40000);
  assert.deepEqual([pet.gm.visit, told], [null, []]);
});
