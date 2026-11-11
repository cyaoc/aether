// Run with: node --test tests/Aether.Core.Tests/DesktopPetCharacters.test.mjs
// Every character is drawn whole for the 桌宠 too (#343): her manifest's desktopPet section has every pose, drawing of her
// sprite and prop the 桌宠页 plays, every strip it names, and a way across every change of pose; CharacterTests checks every
// picture it names is there.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { Engine, MOOD, seeded } from '../../src/Aether.Core/Pages/desktop-pet/engine.mjs';
import { Pet } from '../../src/Aether.Core/Pages/desktop-pet/pet.mjs';
import { SETTINGS } from './Support/DesktopPetSettings.mjs';

const pages = new URL('../../src/Aether.Core/Pages/', import.meta.url), read = path => JSON.parse(readFileSync(new URL(path, pages), 'utf8'));
const { shows } = read('desktop-pet/shows.json');
// Her sprite's own pictures (the character's sprite section) by the names the shows give them.
const SINGLES = ['sIdle', 'sBlink', 'sHappy', 'sLook'];

// Her 桌宠页's engine in a connected room, its clock at 0. Pet measures the props the big shows are placed by; any size will
// do for which poses and drawings she takes.
function engineOf(character) {
  const engine = new Engine({ currentTime: 0 }, shows, character.desktopPet), size = { width: 1, height: 1 };
  Object.assign(engine, { props: { admship: size }, castle: { closed: size, box: { h: 1 } } });
  engine.reset(seeded(7));
  engine.receive({ enabled: true, roomConnected: true, settings: SETTINGS, lines: character.lines });
  return engine;
}

// Every state she can be in: each show the page plays from its start to its end (each blind box ending, each lottery step,
// each 日常小动作 with and without its mishap, woken up first), each 心情 for half a minute and a round of blind boxes she shakes.
function everyState(character) {
  const engine = engineOf(character), states = [];
  for (const row of shows.filter(row => row.group !== MOOD)) for (const variant of row.tests) {
    engine.testShow({ show: row.id, variant, nickname: '星河' });
    const { start, dur } = engine.cur = { ...engine.cur, pre: 1000, wakeLine: '' };
    for (let t = 0; t < 1000 + dur; t += 20) { engine.master.currentTime = start + t; states.push(engine.stateAt(engine.now())); }
  }
  for (const mood of ['happy', 'calm', 'bored', 'gloomy', 'sleepy']) for (let t = 0; t < 30000; t += 20) states.push(engine.idleState(t, mood, t));
  states.push(engine.roundState(0, { start: 0, arrivals: [0], wake: false }));
  return states;
}

// Each change of pose in a show the page plays (each 试演 variant), from standing in and back out to it, with the first show
// it comes in.
function poseChanges(character) {
  const engine = engineOf(character), changes = {};
  for (const row of shows.filter(row => row.group !== MOOD)) for (const variant of row.tests) {
    const opts = { result: variant, mishap: variant === 'Mishap' };
    engine.start(row.id, opts);
    let pose = 'idle';
    for (let t = 0; t <= engine.cur.dur + 20; t += 20) {
      const next = t > engine.cur.dur ? 'idle' : engine.eventState(row.id, t, { ...opts, seed: 0 }, 'x').pose;
      if (next !== pose) changes[`${pose}>${next}`] ??= row.id + (variant ? '·' + variant : '');
      pose = next;
    }
  }
  return changes;
}

for (const folder of readdirSync(new URL('characters/', pages), { withFileTypes: true }).filter(entry => entry.isDirectory()).map(entry => entry.name)) {
  test(`character ${folder} has every pose, sprite drawing and prop the 桌宠页 plays`, () => {
    const character = read(`characters/${folder}/character.json`), art = character.desktopPet, states = everyState(character);
    for (const pose of new Set(states.map(st => st.pose))) assert.ok(art.sheets[art.poses[pose]?.[0]], `pose ${pose}`);
    for (const drawing of new Set(states.flatMap(st => [st.sprite, st.sView ?? 'v1']))) assert.ok(SINGLES.includes(drawing) || art.sprite[drawing] || art.spriteStrips[drawing], `sprite ${drawing}`);
    const props = { gift: ['giftbox', 'giftboxOpen', 'balloons'], castle: ['castleClosed', 'castleOpen'], ship: ['flagship'], admShip: ['admship'], emblem: ['emblem'] };
    for (const [thing, names] of Object.entries(props)) if (states.some(st => st[thing])) for (const prop of names) assert.ok(art.props[prop], `prop ${prop}`);
  });

  test(`character ${folder} has a way across every change of pose in a show: in-between frames, a cut or a route through them`, () => {
    const character = read(`characters/${folder}/character.json`), pet = new Pet(null, { currentTime: 0 }, shows, character);
    // what loading her sheets gives the routes: the in-between frames of each of her transitions
    pet.tweens = Object.fromEntries(Object.keys(character.desktopPet.transitions).map(pair => [pair, [pair, pair]]));
    const none = Object.entries(poseChanges(character)).filter(([pair]) => pet.route(...pair.split('>')) === null).map(([pair, show]) => `${pair}（${show}）`);
    assert.deepEqual(none, []);
  });

  test(`character ${folder}'s desktop pet strips and placings name her poses, drawings and frames`, () => {
    const art = read(`characters/${folder}/character.json`).desktopPet;
    assert.equal(typeof art.anchor, 'number');
    for (const [, [sheet, cell]] of Object.entries({ ...art.poses, ...art.sprite })) assert.ok(art.sheets[sheet] && cell >= 0 && cell < 6, sheet);
    for (const [pose, strip] of Object.entries(art.strips)) assert.ok(art.poses[pose] && art.frames[strip.f] && (strip.ms > 0 || (strip.loop ?? strip.once).length), pose);
    for (const [pair, strip] of Object.entries(art.transitions)) assert.ok(pair.split('>').every(pose => art.poses[pose]) && art.frames[strip], pair);
    for (const [drawing, [strip, ms]] of Object.entries(art.spriteStrips)) assert.ok((SINGLES.includes(drawing) || art.sprite[drawing] || /^v[1-3]$/.test(drawing)) && art.frames[strip] && ms > 0, drawing);
    for (const [pose, spot] of Object.entries(art.sameSpot)) assert.equal(art.poses[pose][0], art.poses[spot][0], pose);
    for (const [pose, dx] of Object.entries(art.body)) assert.ok(art.poses[pose] && typeof dx === 'number', pose);
    for (const pair of art.cuts) assert.ok(pair.split('>').every(pose => art.poses[pose]), pair);
    const { flagship, admship, castleClosed } = art.points;
    for (const point of [flagship.deck, flagship.mast, admship.mast, ...admship.rig, castleClosed.gate]) assert.ok(point.length === 2 && point.every(Number.isFinite), 'points');
  });
}
