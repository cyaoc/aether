// The timelines of the four events and the easing they use (design package README, 动画). Times are milliseconds since the event began.
export const ramp = (from, to, x) => Math.min(1, Math.max(0, (x - from) / (to - from)));
export const lerp = (a, b, p) => a + (b - a) * p;
export const easeOut = p => 1 - Math.pow(1 - p, 3);
export const easeInOut = p => p < 0.5 ? 4 * p * p * p : 1 - Math.pow(-2 * p + 2, 3) / 2;
// Overshoots and settles on 1: the board's 1 + 2.7 (p - 1)^3 + 1.7 (p - 1)^2 multiplied out, which is exactly 0 at 0.
export const easeBack = p => p * (4.7 + p * (2.7 * p - 6.4));
/** The place `p` of the way from geometry `a` to geometry `b`; the rank (tier) it counts as changes half way. */
export function lerpGeometry(a, b, p) {
  const g = {};
  for (const key in a) g[key] = lerp(a[key], b[key], p);
  g.tier = p < 0.5 ? a.tier : b.tier;
  return g;
}

/** A repeatable pseudo-random number in 0..1 for the i-th spark of a seed. */
export const scatter = (i, seed) => { const x = Math.sin(i * 127.1 + seed * 311.7) * 43758.5453; return x - Math.floor(x); };

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
  const p = ramp(0, 320, u);
  return p < 0.2 ? lerp(1, 1.6, p / 0.2) : lerp(1.6, 1, easeBack(ramp(0.2, 1, p)));
}

// What the row the event is about does: a new row unfolds piece by piece; the others pop its count, and the climbers lift off
// with a backing plate in the shape of the slot they cross.
const RISING = {
  new: u => ({
    open: easeOut(ramp(620, 860, u)), plateAlpha: ramp(600, 700, u) * (1 - ramp(1100, 1300, u)),
    avatarScale: easeBack(ramp(700, 900, u)), avatarAlpha: ramp(700, 780, u),
    nameShift: -18 * (1 - easeOut(ramp(760, 980, u))), nameAlpha: ramp(760, 900, u),
    iconScale: easeBack(ramp(820, 1020, u)), iconAlpha: ramp(820, 900, u),
    countScale: easeBack(ramp(880, 1080, u)), countAlpha: ramp(880, 960, u)
  }),
  more: u => ({ countScale: countPop(u), iconScale: 1 + 0.15 * Math.sin(Math.PI * ramp(0, 300, u)) }),
  pass: u => ({ countScale: countPop(u), plateAlpha: Math.sin(Math.PI * ramp(150, 850, u)), lift: 1 + 0.04 * Math.sin(Math.PI * ramp(200, 800, u)) }),
  top: u => ({ countScale: countPop(u), plateAlpha: Math.sin(Math.PI * ramp(250, 1050, u)), lift: 1 + 0.05 * Math.sin(Math.PI * ramp(300, 1000, u)) })
};
// The row that was overtaken dims to half and comes back while the climber passes.
const OVERTAKEN = { pass: [200, 800], top: [300, 1000] };

/** How one row looks `u` ms into an event: `role` is 'rising' (the row the event is about), 'overtaken' or anything else. */
export function entryMotion(kind, role, u) {
  if (role === 'rising') return { ...REST, z: 10, ...RISING[kind](u) };
  if (role === 'overtaken' && OVERTAKEN[kind]) return { ...REST, alpha: 1 - 0.5 * Math.sin(Math.PI * ramp(...OVERTAKEN[kind], u)) };
  return REST;
}

/** The chat-bubble sprite and the mascot: they blink, and pulse, hop and spin with the events. `kind` is undefined between events. */
export function ambient(kind, u, t) {
  const idle = { blink: t % 4500 < 140, spritePulse: 0, cheer: 0, hop: 0, spin: 0 };
  if (kind === 'more') return { ...idle, blink: u < 160 };
  if (kind === 'new') return { ...idle, spritePulse: Math.sin(Math.PI * ramp(140, 300, u)) };
  if (kind !== 'top') return idle;
  return { ...idle, cheer: ramp(900, 1020, u) * (1 - ramp(3000, 3150, u)), hop: -10 * Math.sin(Math.PI * ramp(900, 1300, u)), spin: 2 * Math.PI * easeInOut(ramp(950, 1550, u)) };
}

/** The first card's star twinkles every three seconds; a new first place makes the gold rim flare and the star bloom. */
export function twinkle(t) {
  const tw = (t % 3000) / 600;
  return { scale: 0.3 + 0.16 * (tw < 1 ? Math.sin(Math.PI * tw) : 0), rotation: 0.4 * (tw < 1 ? tw : 0) };
}
export const flare = u => ramp(1600, 1680, u) * (1 - ramp(1800, 2600, u));
