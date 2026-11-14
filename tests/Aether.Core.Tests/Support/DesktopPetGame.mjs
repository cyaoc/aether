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

export function gamePet(character, settings = {}, rng = seeded(7)) {
  const art = character.desktopPet, pet = new Pet(null, { currentTime: 0 }, shows, character), size = { width: 1, height: 1 };
  Object.assign(pet, { props: { admship: size }, castle: { closed: size, box: { h: 1 } }, cells: new Proxy({}, { get: () => Array(6).fill(CELL) }), herLeft: -60,
    prankster, chestBox: { w: 72, h: 58, open: 104, k: H / prankster.anchor }, gameLoaded: true,
    tweens: Object.fromEntries(Object.keys(art.transitions).map(pair => [pair, [pair, pair]])) });
  for (const k of [...Object.keys(art.sheets), 'game']) pet.loads[k] = Promise.resolve();
  pet.reset(rng);
  pet.receive({ enabled: true, roomConnected: true, settings: { ...SETTINGS, ...settings }, lines: character.lines });
  return pet;
}

// One of a gift message's gifts (GiftItem), a 小花花 unless so says otherwise; a viewer's 一轮送礼 as Core tells it after a gift
// message, with its gifts, or as it ends; one telling count gifts for spend 金瓜子 in all, and one telling n 盲盒 at 150 电池 each.
export const item = (spend, so) => ({ name: '小花花', spend, count: 1, blind: false, castle: false, iconUrl: '', ...so });
export const round = (uid, so) => ({ uid, nickname: '观众' + uid, ended: false, items: [], ...so });
export const gifts = (uid, spend, count = 1) => round(uid, { items: [item(spend, { count })] });
export const box = (uid, n = 1, so) => round(uid, { items: [item(15000 * n, { name: '棉花糖', count: n, blind: true, ...so })] });
// The 桌宠页's state as Core tells it, in a connected room with what pet was told last; and the show she plays.
export const state = (pet, so) => ({ enabled: true, roomConnected: true, settings: pet.settings, lines: pet.lines, ...so });
export const playing = pet => pet.cur?.name ?? null;

/** Plays ms of frames on the page's clock, each frame's state passed to seen. */
export function play(pet, ms, seen = () => {}) {
  for (const end = pet.now() + ms; pet.now() < end;) { pet.master.currentTime += FRAME; seen(pet.stateAt(pet.now())); }
}
