// The timelines of the four events and the easing they use (design package README, 动画). Times are milliseconds since the event began.
import { SIZES, TOP_COUNT } from './layout.mjs';
import { k, mix, out } from '../video/animations/toolkit.mjs';

export const easeInOut = p => p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2;
// Overshoots and settles on 1: the board's 1 + 2.7 (p - 1)^3 + 1.7 (p - 1)^2 multiplied out, which is exactly 0 at 0.
export const easeBack = p => p * (4.7 + p * (2.7 * p - 6.4));
/** The place `p` of the way from geometry `a` to geometry `b`; the rank (tier) it counts as changes half way. */
export function lerpGeometry(a, b, p) {
  const g = {};
  for (const key in a) g[key] = mix(a[key], b[key], p);
  g.tier = p < 0.5 ? a.tier : b.tier;
  return g;
}

/** `duration` is when the event's last effect has ended; `move` is the window in which the rows change places. */
export const EVENTS = {
  new: { duration: 1600, move: [0, 260] },
  more: { duration: 700 },
  pass: { duration: 1300, move: [200, 800] },
  top: { duration: 3250, move: [300, 1000] }
};
// The list scrolls a lap of 13 rows (840 design pixels) in 24 seconds, as in the board's scrolling demo.
export const SCROLL_PER_MS = 840 / 24000;

const REST = { alpha: 1, avatarScale: 1, avatarAlpha: 1, nameShift: 0, nameAlpha: 1, iconScale: 1, iconAlpha: 1, countScale: 1, countAlpha: 1, open: 1, z: 0, plateAlpha: 0, lift: 1 };

/** A count that has just grown swells to 1.6 times its size and settles back. */
function countPop(u) {
  const p = k(0, 320, u);
  return p < 0.2 ? mix(1, 1.6, p / 0.2) : mix(1.6, 1, easeBack(k(0.2, 1, p)));
}

// What the row the event is about does: a new row unfolds piece by piece; the others pop its count, and the climbers lift off
// with a backing plate in the shape of the slot they cross.
const RISING = {
  new: u => ({
    open: out(k(620, 860, u)), plateAlpha: k(600, 700, u) * (1 - k(1100, 1300, u)),
    avatarScale: easeBack(k(700, 900, u)), avatarAlpha: k(700, 780, u),
    nameShift: -18 * (1 - out(k(760, 980, u))), nameAlpha: k(760, 900, u),
    iconScale: easeBack(k(820, 1020, u)), iconAlpha: k(820, 900, u),
    countScale: easeBack(k(880, 1080, u)), countAlpha: k(880, 960, u)
  }),
  more: u => ({ countScale: countPop(u), iconScale: 1 + 0.15 * Math.sin(Math.PI * k(0, 300, u)) }),
  pass: u => ({ countScale: countPop(u), plateAlpha: Math.sin(Math.PI * k(150, 850, u)), lift: 1 + 0.04 * Math.sin(Math.PI * k(200, 800, u)) }),
  top: u => ({ countScale: countPop(u), plateAlpha: Math.sin(Math.PI * k(250, 1050, u)), lift: 1 + 0.05 * Math.sin(Math.PI * k(300, 1000, u)) })
};
// The row that was overtaken dims to half and comes back while the climber passes.
const OVERTAKEN = { pass: [200, 800], top: [300, 1000] };

/** How one row looks `u` ms into an event: `role` is 'rising' (the row the event is about), 'overtaken' or anything else. */
export function entryMotion(kind, role, u) {
  if (role === 'rising') return { ...REST, z: 10, ...RISING[kind](u) };
  if (role === 'overtaken' && OVERTAKEN[kind]) return { ...REST, alpha: 1 - 0.5 * Math.sin(Math.PI * k(...OVERTAKEN[kind], u)) };
  return REST;
}

/** The event whose timeline the rows follow. A row that appears in first place (`debut`) is built, and the rows below it are made
 * room for, as with a new row; what is cheered above it is the new first place's. */
export const rowKind = event => event?.debut ? 'new' : event?.kind;

const spritePulse = u => Math.sin(Math.PI * k(140, 300, u));

/** The chat-bubble sprite and the mascot: they blink, and pulse, hop and spin with the events. `event` is undefined between events. */
export function ambient(event, u, t) {
  const idle = { blink: t % 4500 < 140, spritePulse: 0, cheer: 0, hop: 0, spin: 0 };
  if (event?.kind === 'more') return { ...idle, blink: u < 160 };
  if (event?.kind === 'new') return { ...idle, spritePulse: spritePulse(u) };
  if (event?.kind !== 'top') return idle;
  return { ...idle, spritePulse: event.debut ? spritePulse(u) : 0, cheer: k(900, 1020, u) * (1 - k(3000, 3150, u)), hop: -10 * Math.sin(Math.PI * k(900, 1300, u)), spin: 2 * Math.PI * easeInOut(k(950, 1550, u)) };
}

/** A new row's own slot stays empty until the row has unfolded there, then its backing appears. */
export const unfolded = u => k(640, 760, u);

/** The first card's star twinkles every three seconds; a new first place makes the gold rim flare and the star bloom. */
export function twinkle(t) {
  const tw = (t % 3000) / 600;
  return { scale: 0.3 + 0.16 * (tw < 1 ? Math.sin(Math.PI * tw) : 0), rotation: 0.4 * (tw < 1 ? tw : 0) };
}
export const flare = u => k(1600, 1680, u) * (1 - k(1800, 2600, u));

// 流光 on the top three's cards (design package README, 前三名流光). `rank` counts from 0, `t` is the page clock and `landedAgo`
// how long ago a row landed in the card, Infinity when none has.

/** Where the head of a card's first arc is, as a fraction of its rim: a lap in 6 s on the first card, 9 s on the others. A new first
 * place's arcs spin two laps more in 1600 ms and end up where they would have been. */
export function arcHead(rank, t, landedAgo) {
  const spin = rank === 0 ? 2 * easeInOut(k(0, 1600, landedAgo)) : 0;
  return (t / (rank === 0 ? 6000 : 9000) + spin + 0.37 * rank) % 1;
}

// When, in ms into its timeline, a row's unfold or move into its new place ends.
const LANDS = { new: 860, pass: EVENTS.pass.move[1], top: EVENTS.top.move[1] };

/** Where and when the row an event is about lands among the top three, now that it is in `slot`: it is new there or has climbed
 * there. A row that drops or only grows does not land. */
export function landing(event, slot) {
  const before = event.slots[event.id];
  return slot < TOP_COUNT && (before === undefined || before > slot) ? { slot, at: LANDS[rowKind(event)] } : undefined;
}

/** How far across its card the sheen is, swept between 0 and 1, and how bright: in 1100 ms every 5 s on the first card and every
 * 10 s on the others, 3.3 s apart. A row that has just landed sweeps its card once in 700 ms, 1.6 times as bright. */
export function sheenSweep(rank, t, landedAgo) {
  if (landedAgo < 700) return { progress: landedAgo / 700, strength: 1.6 };
  const phase = rank === 0 ? t % 5000 : (t + 10000 - 3300 * rank) % 10000;
  return { progress: phase / 1100, strength: 1 };
}

/** A row that has just landed lights its card's whole rim: it comes on in 80 ms and fades back to the arcs from 150 to 1200 ms. */
export const rimFlood = landedAgo => k(0, 80, landedAgo) * (1 - k(150, 1200, landedAgo));
/** The soft lights under a card swell to 1.8 times when a row lands in it and settle back in 1800 ms. */
export const lightSwell = landedAgo => 1 + 0.8 * (1 - k(0, 1800, landedAgo));
/** A card below the first flashes as a row lands in it, fading out in 360 ms. */
export const cardFlash = landedAgo => 0.5 * (1 - k(0, 360, landedAgo));
/** The card's rank number pops to 1.3 times in 95 ms and eases back by 380 ms. */
export function numeralPop(landedAgo) {
  const p = k(0, 380, landedAgo);
  return p < 0.25 ? mix(1, 1.3, p / 0.25) : mix(1.3, 1, out(k(0.25, 1, p)));
}

/** How long ago a row landed in the card of `rank`, by the clock `t`, for a landing `landed` at a clock time. */
export const sinceLanding = (landed, rank, t) => landed?.slot === rank && t >= landed.at ? t - landed.at : Infinity;

// The top three's names that do not fit their cards take turns (design package README, 长名字和头像框间距): each stands at its start
// for 2 s, scrolls to its end at 40 design pixels a second, stands there 1.5 s, and fades out and back in at its start in 500 ms.
const NAME_STILL = 2000, NAME_AT_END = 1500, NAME_FADE_BACK = 500, NAME_SPEED = 40;

/** Which of the top three's names is scrolling `t` ms into the names' turns, how far along (`offset`, in the 28 px pixels names are
 * drawn in) and how opaque it is; `overs` is how far each card's name runs past its window, 0 for a name that fits. Undefined when
 * every name fits. */
export function nameTurn(overs, t) {
  const turns = overs.flatMap((over, rank) => {
    const scrolling = over * SIZES[rank].ns / 28 * 1000 / NAME_SPEED;
    return over > 0 ? [{ rank, over, scrolling, length: NAME_STILL + scrolling + NAME_AT_END + NAME_FADE_BACK }] : [];
  });
  if (turns.length === 0) return undefined;
  let u = t % turns.reduce((sum, turn) => sum + turn.length, 0);
  for (const { rank, over, scrolling, length } of turns) {
    if (u >= length) {
      u -= length;
      continue;
    }
    const fade = k(length - NAME_FADE_BACK, length, u);
    return { rank, offset: fade < 0.5 ? over * k(NAME_STILL, NAME_STILL + scrolling, u) : 0, alpha: Math.abs(1 - 2 * fade) };
  }
}
