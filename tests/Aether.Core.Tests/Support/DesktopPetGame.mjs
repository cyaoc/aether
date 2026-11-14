// A 桌宠页 for the node tests of the 宝箱小游戏 (#372): Pet in a connected room with the default settings, its clock at 0 and
// every picture stood in for by one box (each cell, the chest at its size), so that where she and the 捣蛋鬼 are can be worked
// out without drawing; her in-betweens are named by their transitions, for her routes.
import { readFileSync } from 'node:fs';
import { H, seeded } from '../../../src/Aether.Core/Pages/desktop-pet/engine.mjs';
import { Pet } from '../../../src/Aether.Core/Pages/desktop-pet/pet.mjs';
import { SETTINGS } from './DesktopPetSettings.mjs';

const read = path => JSON.parse(readFileSync(new URL('../../../src/Aether.Core/Pages/' + path, import.meta.url), 'utf8'));
const { shows } = read('desktop-pet/shows.json'), prankster = read('desktop-pet/prankster/prankster.json');
const CELL = { x: 0, y: 0, w: 100, h: 100, cx: 50, cy: 50, ox: 0, oy: 0, ground: 100, ruler: 100, rope: 0.5, tip: { x: 0, y: 40 } };
const FRAME = 1000 / 60;

export function gamePet(character, settings = {}) {
  const art = character.desktopPet, pet = new Pet(null, { currentTime: 0 }, shows, character), size = { width: 1, height: 1 };
  Object.assign(pet, { props: { admship: size }, castle: { closed: size, box: { h: 1 } }, cells: new Proxy({}, { get: () => Array(6).fill(CELL) }), herLeft: -60,
    prankster, chestBox: { w: 72, h: 58, open: 104, k: H / prankster.anchor }, gameLoaded: true,
    tweens: Object.fromEntries(Object.keys(art.transitions).map(pair => [pair, [pair, pair]])) });
  for (const k of [...Object.keys(art.sheets), 'game']) pet.loads[k] = Promise.resolve();
  pet.reset(seeded(7));
  pet.receive({ enabled: true, roomConnected: true, settings: { ...SETTINGS, ...settings }, lines: character.lines });
  return pet;
}

// A viewer's 一轮送礼 so far, as Core tells it after a gift message; the 桌宠页's state as Core tells it, in a connected room with
// what pet was told last; and the show she plays.
export const round = (uid, so) => ({ uid, nickname: '观众' + uid, spend: 0, count: 0, topGift: '小花花', topCount: 0, boxSpend: 0, boxes: 0, castle: false, ended: false, items: [], ...so });
export const state = (pet, so) => ({ enabled: true, roomConnected: true, settings: pet.settings, lines: pet.lines, ...so });
export const playing = pet => pet.cur?.name ?? null;

/** Plays ms of frames on the page's clock, each frame's state passed to seen. */
export function play(pet, ms, seen = () => {}) {
  for (const end = pet.now() + ms; pet.now() < end;) { pet.master.currentTime += FRAME; seen(pet.stateAt(pet.now())); }
}
