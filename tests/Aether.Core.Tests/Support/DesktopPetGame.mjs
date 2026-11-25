// A 桌宠页 for the node tests of the 宝箱小游戏 (#372) and of her PK shows: Pet in a connected room with the default settings, its
// clock at 0 and every picture stood in for by one box (each cell, the chest at its size, a sign held up in each), so that where
// she, the 捣蛋鬼 and the sign are can be worked out without drawing; her in-betweens are two such boxes and the 捣蛋鬼's are
// named by their transitions, for their routes.
import { readFileSync } from 'node:fs';
import { H, seeded } from '../../../src/Aether.Core/Pages/desktop-pet/engine.mjs';
import { Pet } from '../../../src/Aether.Core/Pages/desktop-pet/pet.mjs';
import { SETTINGS } from './DesktopPetSettings.mjs';

const read = path => JSON.parse(readFileSync(new URL('../../../src/Aether.Core/Pages/' + path, import.meta.url), 'utf8'));
const { shows } = read('desktop-pet/shows.json'), prankster = read('desktop-pet/prankster/prankster.json');
const CELL = { x: 0, y: 0, w: 100, h: 100, cx: 50, cy: 50, ox: 0, oy: 0, ground: 100, ruler: 100, rope: 0.5, tip: { x: 0, y: 40 }, sign: { x: 50, y: 20, a: 0, e: 'b', hold: 'b', h: 40 } };
const FRAME = 1000 / 60;
// Where the 对手情报卡's words are measured, every word 0 wide.
export const MEASURE = { save() {}, restore() {}, measureText: () => ({ width: 0 }) };

export function gamePet(character, settings = {}, rng = seeded(7)) {
  const art = character.desktopPet, pet = new Pet(null, { currentTime: 0 }, shows, character), size = { width: 1, height: 1 };
  Object.assign(pet, { props: { admship: size, gacha: size, sofa: size }, castle: { closed: size, box: { h: 1 } }, cells: new Proxy({}, { get: () => Array(6).fill(CELL) }), herLeft: -60, measureG: MEASURE,
    prankster, chestBox: { w: 72, h: 58, open: 104, k: H / prankster.anchor }, gameLoaded: true,
    tweens: Object.fromEntries(Object.keys(art.transitions).map(pair => [pair, [CELL, CELL]])),
    pkTweens: Object.fromEntries(Object.keys(prankster.transitions).map(pair => [pair, [pair, pair]])) });
  for (const k of [...Object.keys(art.sheets), 'game']) pet.loads[k] = Promise.resolve();
  pet.reset(rng);
  pet.receive({ enabled: true, roomConnected: true, settings: { ...SETTINGS, ...settings }, lines: character.lines });
  return pet;
}

// One of a gift message's gifts (GiftItem), a 小花花 at what was paid for one unless so says otherwise; a viewer's 一轮送礼 as
// Core tells it after a gift message, with its gifts, or as it ends; one telling count gifts for spend 金瓜子 in all, and one
// telling n 盲盒 at 150 电池 each.
export const item = (spend, so = {}) => ({ giftId: 31036, name: '小花花', price: spend / (so.count ?? 1), spend, count: 1, blind: false, castle: false, ...so });
export const round = (uid, so) => ({ uid, nickname: '观众' + uid, faceUrl: '', ended: false, items: [], ...so });
export const gifts = (uid, spend, count = 1) => round(uid, { items: [item(spend, { count })] });
export const box = (uid, n = 1, so) => round(uid, { items: [item(15000 * n, { name: '棉花糖', count: n, blind: true, ...so })] });
// The 宝箱小游戏's 入伙礼物, 1 电池 each: n 人气票, her side's, or 小花花, the 捣蛋鬼's; and a gift that is neither, for spend 金瓜子.
export const tickets = (uid, n = 1) => round(uid, { items: [item(100 * n, { giftId: 33988, name: '人气票', count: n })] });
export const flowers = (uid, n = 1) => gifts(uid, 100 * n, n);
export const other = (uid, spend) => round(uid, { items: [item(spend, { giftId: 31039, name: '牛哇牛哇' })] });
// The 桌宠页's state as Core tells it, in a connected room with what pet was told last; and the show she plays.
export const state = (pet, so) => ({ enabled: true, roomConnected: true, settings: pet.settings, lines: pet.lines, ...so });
export const playing = pet => pet.cur?.name ?? null;

/** Plays ms of frames on the page's clock, each frame's state passed to seen. */
export function play(pet, ms, seen = () => {}) {
  for (const end = pet.now() + ms; pet.now() < end;) { pet.master.currentTime += FRAME; seen(pet.stateAt(pet.now())); }
}
