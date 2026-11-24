// Run with: node --test tests/Aether.Core.Tests/DesktopPetCharacters.test.mjs
// Every character is drawn whole for the 桌宠 too (#343): her manifest's desktopPet section has every pose, drawing of her
// sprite and prop the 桌宠页 plays, every strip it names, and a way across every change of pose; CharacterTests checks every
// picture it names is there.
import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { MOOD } from '../../src/Aether.Core/Pages/desktop-pet/engine.mjs';
import { gamePet } from './Support/DesktopPetGame.mjs';
import { NAME, ROUTES, STAR, STEP, VIEWERS, drive, frames, walk } from '../../scripts/desktop-pet/routes.mjs';

const pages = new URL('../../src/Aether.Core/Pages/', import.meta.url), read = path => JSON.parse(readFileSync(new URL(path, pages), 'utf8'));
const { shows } = read('desktop-pet/shows.json'), prankster = read('desktop-pet/prankster/prankster.json');
// Her sprite's own pictures (the character's sprite section) by the names the shows give them.
const SINGLES = ['sIdle', 'sBlink', 'sHappy', 'sLook'];

// Every state she can be in: each show the page plays from its start to its end (each blind box ending, each lottery step,
// each 日常小动作 with and without its mishap, woken up first), each 心情 for half a minute and a round of blind boxes she shakes.
// Her 桌宠页 stands in a box for each of her pictures (gamePet): any size will do for which poses and drawings she takes.
function everyState(character) {
  const engine = gamePet(character), states = [];
  for (const row of shows.filter(row => row.group !== MOOD)) for (const variant of row.tests) {
    engine.testShow({ show: row.id, variant, nickname: '星河' });
    const { start, dur } = engine.cur = { ...engine.cur, pre: 1000, wakeLine: '' };
    for (let t = 0; t < 1000 + dur; t += 20) { engine.master.currentTime = start + t; states.push(engine.stateAt(engine.now())); }
  }
  for (const mood of ['happy', 'calm', 'bored', 'gloomy', 'sleepy']) for (let t = 0; t < 30000; t += 20) states.push(engine.idleState(t, mood, t));
  states.push(engine.roundState(0, { start: 0, arrivals: [0], wake: false }));
  return states;
}

// Her 桌宠页 with each of her in-betweens named by its transition, as loading her sheets gives the routes them.
function routing(character) {
  const pet = gamePet(character);
  pet.tweens = Object.fromEntries(Object.keys(character.desktopPet.transitions).map(pair => [pair, [pair, pair]]));
  return pet;
}

// Each change of pose in a show the page plays (each 试演 variant), from standing in and back out to it, with the first show
// it comes in.
function poseChanges(character) {
  const engine = gamePet(character), changes = {};
  for (const row of shows.filter(row => row.group !== MOOD)) for (const variant of row.tests) {
    const poses = [...showPoses(engine, row.id, { result: variant, mishap: variant === 'Mishap', seed: 0 }), 'idle'];
    poses.slice(1).forEach((pose, i) => { if (pose !== poses[i]) changes[`${poses[i]}>${pose}`] ??= row.id + (variant ? '·' + variant : ''); });
  }
  return changes;
}

// The poses a show plays one after another, from standing in.
function showPoses(pet, show, opts) {
  const poses = ['idle'];
  pet.start(show, opts);
  for (let t = 0; t <= pet.cur.dur; t += 20) { const pose = pet.eventState(show, t, opts, 'x').pose; if (pose !== poses.at(-1)) poses.push(pose); }
  return poses;
}

// Each change of her pose and the 捣蛋鬼's (facing the same way: turning round it simply switches, its in-betweens drawn facing
// one way) on the chest game's routes, played on her page as the design package plays them on its board, with the first route
// it comes on.
async function gamePoseChanges(character) {
  const changes = { her: {}, its: {} };
  for (const [route, steps] of Object.entries(ROUTES)) {
    const pet = gamePet(character);
    let her = null, its = null;
    const clock = frames(0, t => { pet.master.currentTime = t; }, t => {
      const st = pet.stateAt(t), pk = pet.gl.pk;
      if (her && st.pose !== her) changes.her[`${her}>${st.pose}`] ??= route;
      if (pk && its && pk.pose !== its.pose && pk.flip === its.flip) changes.its[`${its.pose}>${pk.pose}`] ??= route;
      [her, its] = [st.pose, pk];
    });
    await walk(steps, drive(pet, clock.later, { NAME, STEP, VIEWERS, STAR }), clock);
  }
  return changes;
}

test('every picture prankster.json names ships with the 桌宠页', () => {
  for (const file of [...Object.values(prankster.sheets), ...Object.values(prankster.props), ...Object.values(prankster.frames)])
    assert.ok(existsSync(new URL('desktop-pet/prankster/' + file, pages)), file);
});

for (const folder of readdirSync(new URL('characters/', pages), { withFileTypes: true }).filter(entry => entry.isDirectory()).map(entry => entry.name)) {
  test(`character ${folder} has every pose, sprite drawing and prop the 桌宠页 plays`, () => {
    const character = read(`characters/${folder}/character.json`), art = character.desktopPet, states = everyState(character);
    for (const pose of new Set(states.map(st => st.pose))) assert.ok(art.sheets[art.poses[pose]?.[0]], `pose ${pose}`);
    for (const drawing of new Set(states.flatMap(st => [st.sprite, st.sView ?? 'v1']))) assert.ok(SINGLES.includes(drawing) || art.sprite[drawing] || art.spriteStrips[drawing], `sprite ${drawing}`);
    const props = { gift: ['giftbox', 'giftboxOpen', 'balloons'], castle: ['castleClosed', 'castleOpen'], ship: ['flagship'], admShip: ['admship'], emblem: ['emblem'], gacha: ['gacha', 'battery'], pop: ['plush'] };
    for (const [thing, names] of Object.entries(props)) if (states.some(st => st[thing])) for (const prop of names) assert.ok(art.props[prop], `prop ${prop}`);
  });

  test(`character ${folder} has a way across every change of pose in a show: in-between frames, a cut or a route through them`, () => {
    const character = read(`characters/${folder}/character.json`), pet = routing(character);
    const none = Object.entries(poseChanges(character)).filter(([pair]) => pet.route(...pair.split('>')) === null).map(([pair, show]) => `${pair}（${show}）`);
    assert.deepEqual(none, []);
  });

  test(`character ${folder}'s 被反超 goes from startled to stomping by her standing, never by way of opening a blind box`, () => {
    const pet = routing(read(`characters/${folder}/character.json`)), poses = showPoses(pet, 'pkSwing', { result: 'Overtaken', seed: 0 });
    assert.deepEqual(poses, ['idle', 'surprised', 'idle', 'pout', 'idle']);
    assert.deepEqual(poses.slice(1).flatMap((pose, i) => pet.route(poses[i], pose)).filter(pair => pair.includes('Box')), []);
  });

  test(`character ${folder}'s 扭蛋 cut short before the capsule opens never goes on to open it: from none of its poses to any other show's is there a way through twisting it open, the plush or the empty halves`, () => {
    const character = read(`characters/${folder}/character.json`), pet = routing(character);
    const won = showPoses(pet, 'gacha', { mishap: false, seed: 0 }), lost = showPoses(pet, 'gacha', { mishap: true, seed: 0 });
    const before = won.slice(1, won.indexOf('gTwist') + 1), others = Object.keys(character.desktopPet.poses).filter(pose => pose === 'idle' || ![...won, ...lost].includes(pose));
    const through = before.flatMap(from => others.flatMap(to => (pet.route(from, to) ?? []).filter(pair => /gTwist|gHug|gEmpty/.test(pair)).map(pair => `${from}>${to}: ${pair}`)));
    assert.deepEqual([...new Set(through)], []);
  });

  test(`character ${folder} has a way across every change of pose on the 宝箱小游戏's routes, and the 捣蛋鬼 its in-betweens or a cut written down`, async () => {
    const character = read(`characters/${folder}/character.json`), changes = await gamePoseChanges(character), { cuts } = prankster;
    const pet = gamePet(character), none = Object.entries(changes.her).filter(([pair]) => pet.route(...pair.split('>')) === null);
    // (its in-betweens as the page plays them, pkRoute; a cut is written either way round, and knocked flying it always switches)
    const its = Object.entries(changes.its).filter(([pair]) => {
      const [from, to] = pair.split('>');
      return !pet.pkRoute(from, to) && !cuts.includes(pair) && !cuts.includes(`${to}>${from}`) && to !== 'knocked';
    });
    assert.deepEqual([...none, ...its].map(([pair, route]) => `${pair}（${route}）`), []);
  });

  test(`character ${folder}'s desktop pet strips and placings name her poses, drawings and frames`, () => {
    const art = read(`characters/${folder}/character.json`).desktopPet;
    assert.equal(typeof art.anchor, 'number');
    for (const [, [sheet, cell]] of Object.entries({ ...art.poses, ...art.sprite })) assert.ok(art.sheets[sheet] && cell >= 0 && cell < 6, sheet);
    for (const [pose, strip] of Object.entries(art.strips)) assert.ok(art.poses[pose] && art.frames[strip.f] && (!strip.f2 || art.frames[strip.f2]) && (strip.ms > 0 || (strip.loop ?? strip.once).length), pose);
    for (const [drawing, places] of Object.entries(art.signs)) assert.ok((art.sheets[drawing] || art.frames[drawing]) && places.every(place => place === null || place.length === 6), drawing);
    for (const [pair, strip] of Object.entries(art.transitions)) assert.ok(pair.split('>').every(pose => art.poses[pose]) && art.frames[strip], pair);
    for (const [drawing, [strip, ms]] of Object.entries(art.spriteStrips)) assert.ok((SINGLES.includes(drawing) || art.sprite[drawing] || /^v[1-3]$/.test(drawing)) && art.frames[strip] && ms > 0, drawing);
    for (const [pose, spot] of Object.entries(art.sameSpot)) assert.equal(art.poses[pose][0], art.poses[spot][0], pose);
    for (const [pose, dx] of Object.entries(art.body)) assert.ok(art.poses[pose] && typeof dx === 'number', pose);
    for (const pair of [...art.cuts, ...art.noWayThrough]) assert.ok(pair.split('>').every(pose => art.poses[pose]), pair);
    const { flagship, admship, castleClosed, gacha } = art.points;
    for (const point of [flagship.deck, flagship.mast, admship.mast, ...admship.rig, castleClosed.gate, gacha.bay, gacha.outlet, gacha.hug]) assert.ok(point.length === 2 && point.every(Number.isFinite), 'points');
    assert.deepEqual([gacha.win.length, gacha.knob.length, typeof gacha.cell], [4, 3, 'number'], 'the 扭蛋 cabinet\'s window, knob and cell');
  });
}
