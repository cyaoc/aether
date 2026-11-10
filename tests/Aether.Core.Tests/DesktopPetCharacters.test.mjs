// Run with: node --test tests/Aether.Core.Tests/DesktopPetCharacters.test.mjs
// Every character is drawn whole for the 桌宠 too (#343): her manifest's desktopPet section has every pose, drawing of her
// sprite and prop the 桌宠页 plays, and every strip it names; CharacterTests checks every picture it names is there.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { DAILY, Engine, seeded } from '../../src/Aether.Core/Pages/desktop-pet/engine.mjs';

const pages = new URL('../../src/Aether.Core/Pages/', import.meta.url), read = path => JSON.parse(readFileSync(new URL(path, pages), 'utf8'));
const { shows } = read('desktop-pet/shows.json');
const SETTINGS = { off: [], midGift: 100000, bigGift: 1000000, boredMinutes: 15, gloomyMinutes: 40, sleepyMinutes: 60, dailyMinSeconds: 20, dailyMaxSeconds: 40, mishapPercent: 25 };
// Her sprite's own pictures (the character's sprite section) by the names the shows give them.
const SINGLES = ['sIdle', 'sBlink', 'sHappy', 'sLook'];

// Every state she can be in: each show the page plays from its start to its end (each blind box ending, each 日常小动作 with
// and without its mishap, woken up first), each 心情 for half a minute and a round of blind boxes she shakes.
function everyState(character) {
  const engine = new Engine({ currentTime: 0 }, shows), states = [];
  engine.reset(seeded(7));
  engine.receive({ enabled: true, roomConnected: true, settings: SETTINGS, lines: character.lines });
  for (const row of shows.filter(row => engine.events[row.id].dur || DAILY[row.id])) for (const variant of row.tests) {
    engine.testShow({ show: row.id, variant, nickname: '星河' });
    const { start, dur } = engine.cur = { ...engine.cur, pre: 1000, wakeLine: '' };
    for (let t = 0; t < 1000 + dur; t += 20) { engine.master.currentTime = start + t; states.push(engine.stateAt(engine.now())); }
  }
  for (const mood of ['happy', 'calm', 'bored', 'gloomy', 'sleepy']) for (let t = 0; t < 30000; t += 20) states.push(engine.idleState(t, mood, t));
  states.push(engine.roundState(0, { start: 0, arrivals: [0], wake: false }));
  return states;
}

for (const folder of readdirSync(new URL('characters/', pages), { withFileTypes: true }).filter(entry => entry.isDirectory()).map(entry => entry.name)) {
  test(`character ${folder} has every pose, sprite drawing and prop the 桌宠页 plays`, () => {
    const character = read(`characters/${folder}/character.json`), art = character.desktopPet, states = everyState(character);
    for (const pose of new Set(states.map(st => st.pose))) assert.ok(art.sheets[art.poses[pose]?.[0]], `pose ${pose}`);
    for (const drawing of new Set(states.flatMap(st => [st.sprite, st.sView ?? 'v1']))) assert.ok(SINGLES.includes(drawing) || art.sprite[drawing] || art.spriteStrips[drawing], `sprite ${drawing}`);
    if (states.some(st => st.gift)) for (const prop of ['giftbox', 'giftboxOpen', 'balloons']) assert.ok(art.props[prop], `prop ${prop}`);
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
  });
}
