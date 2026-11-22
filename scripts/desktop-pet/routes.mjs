// The 宝箱小游戏's routes on the design package's board (docs/design/desktop-pet/tools/check-game.mjs), played on the 桌宠页 too:
// compare.mjs plays them on both in Chrome, DesktopPetCharacters.test.mjs on the page's engine in node. compare.mjs hands the
// browser the source of each function below but the board's, so each uses only its arguments and the others.
export { ROUTES } from '../../docs/design/desktop-pet/tools/check-game.mjs';

// The board's names of the shows, by the page's ids; and of the lottery's steps, by the 试演 variants of its one row.
export const NAME = { entry: '进场', gift: '普通礼物', giftStreak: '连着送', midGift: '中礼物', bigGift: '大礼物', blindBoxSmall: '盲盒·小', blindBoxMedium: '盲盒·中', blindBoxLarge: '盲盒·大',
  captain: '舰长', admiral: '提督', governor: '总督', castle: '城堡', newFirst: '新的第一名', pranksterKnocked: '打跑了', chestKept: '守住了', chestRobbed: '被偷了',
  blindBoxQuery: '盲盒查询', pkCondition: 'PK·双倍条件', pkDouble: 'PK·2倍', pkDoubleEnding: 'PK·双倍还剩10秒', pkLastSeconds: 'PK·最后10秒' };
export const STEP = { EntryOpen: '报名开场', EntryClosed: '报名截止', PoolOpen: '奖池开场', PoolCall: '奖池点名', PoolSpin: '奖池转动',
  RevealGrand: '揭晓·大奖', RevealSecond: '揭晓·二奖', RevealThird: '揭晓·三奖', NoEntries: '没人报名', End: '抽奖结束' };
// The board's viewers in the game (its VIEWERS), as Core tells of a viewer: each one's face the 礼物榜's sample avatar the
// board gives them (its BLOB av0…av7), which the design package's server.mjs serves by that id.
const FACES = ['c620d7f4bc5af562ce5ffd444676ff76', 'fe74bf47e64cdfb2bcb9e7b82e0b5fb9', '0f4430b615cac01b7810b7521a933def', 'b983e6ae5179fd006a444e444faa6b9f',
  '6d0f7caff30f5aeb137edd01a4566882', 'e6fca7276cf94c054a7f9f49c542d0c9', '8a343577fdd5c70379a9198a27823463', '9a8ff6273af0377beb655e5a706329d3'];
export const VIEWERS = ['星河', '小鱼干', '奶茶不加糖', '今天也要早睡', '柚子', '路过的猫', '阿狸', '晚风'].map((nickname, i) => ({ uid: 101 + i, nickname, faceUrl: '/_blob/' + FACES[i] }));
// Whoever the board names in her lines (its 普通昵称), none of its viewers in the game: the viewer of a gift it gives none.
export const STAR = { uid: 1, nickname: '星河', faceUrl: '' };

/** What a route's steps call x, for the page `pet` in a connected room: what Core would tell it for each, the shows by the
 * board's names (names: NAME and STEP); a 一轮送礼 of a viewer's gifts, ended after 3 s without one, as Core's (send, end), and
 * the 盲盒播报 of its blind boxes 3.8 s after them, when the board's stands in for it. someone picks one of the board's viewers
 * (VIEWERS) as the board does, by the page's random numbers, one drawn for each as on the board; a gift the board gives no
 * viewer is STAR's. later(ms, fn) runs fn ms on from the time the step runs at. */
export function drive(pet, later, { NAME, STEP, VIEWERS, STAR }) {
  const GUARD = { 舰长: 3, 提督: 2, 总督: 1 }, SIDE = { 1: 1, 2: -1 }, last = {};
  const state = so => ({ enabled: true, roomConnected: true, settings: pet.settings, lines: pet.lines, lottery: null, ...so });
  const someone = (on = null) => {
    const sides = pet.gm.visit?.sides, pool = VIEWERS.filter(w => on === null ? !sides?.has(w.uid) : !!sides?.has(w.uid) === on);
    const from = pool.length ? pool : on === null ? VIEWERS : [];
    return from.length ? from[Math.floor(pet.rng() * from.length)] : null;
  };
  // The board counts a gift for 开心 once more as its show is fired, past the ring of light that scares the 捣蛋鬼 off (the
  // 桌宠页's README, deliberate differences); so does the page here, once however many routes it plays.
  if (!Object.hasOwn(pet, 'fire')) {
    const fire = pet.fire;
    pet.fire = function (name, opts = {}) { if (opts.round && (opts.burst || !this.throwing())) this.recent.push(this.now()); return fire.call(this, name, opts); };
  }
  return {
    get gm() { return pet.gm; },
    get cur() { return pet.cur && { name: NAME[pet.cur.name] ?? STEP[pet.cur.opts.result], start: pet.cur.start, pre: pet.cur.pre }; },
    now: () => pet.now(),
    someone,
    visit: () => pet.summon(),
    heard: (w = someone(false), said = '') => pet.danmaku({ uid: w.uid, nickname: w.nickname, side: SIDE[said] ?? 0 }),
    pkOn: on => pet.receive(state({ pk: on })),
    fire: (name, opts) => GUARD[name] ? pet.guard({ ...STAR, guardLevel: GUARD[name], spend: opts.price * 100 })
      : (pet.receive(state()), pet.receive(state({ lottery: { mode: 'Entry', id: 'E1', keyword: '抽奖', phase: 'Open', entryCount: 0, draws: [], drawUntil: null } }))),
    give: list => list.forEach((m, i) => {
      const gifts = [].concat(m), who = gifts[0].viewer ?? STAR, boxes = gifts.filter(g => g.box);
      later(i * 350, () => send(pet, last, who, gifts));
      later(i * 350 + 3000, () => end(pet, last, who, gifts));
      if (boxes.length) later(i * 350 + 3800, () => pet.blindBox(announced(who, boxes)));
    })
  };
}

/** A gift message of viewer `who`'s, the board's gifts `list`, told with its gifts as Core tells them: the last message of the
 * viewer's round (last) until another comes. */
export function send(pet, last, who, list) {
  last[who.uid] = list;
  told(pet, who, { items: itemsOf(list) });
}

/** The viewer's round ends, unless another gift message came after `list`. */
export function end(pet, last, who, list) {
  if (last[who.uid] === list) told(pet, who, { ended: true });
}

/** The viewer's 一轮送礼 as Core tells it. */
export function told(pet, who, so) {
  pet.gift({ ...who, ended: false, items: [], ...so });
}

/** The board's gifts `list` as Core tells a gift message's (GiftItem), each gift's name standing in for its id. */
export function itemsOf(list) {
  return list.map(g => ({ giftId: g.gift, name: g.gift, price: g.price * 100, spend: spendOf(g), count: g.n, blind: !!g.box, castle: !!g.castle }));
}

/** What a gift of the board's cost, in 金瓜子: a 盲盒 its box's price. */
export function spendOf(g) { return (g.box ? g.boxPrice : g.price) * 100 * g.n; }

/** The 盲盒播报 of a viewer's blind boxes `boxes`, the board's gifts, as Core tells its result (BlindBoxResult): what they
 * cost and opened, the gift priced highest among them, and the castles. */
export function announced(who, boxes) {
  const sum = of => boxes.reduce((n, g) => n + of(g), 0), spend = sum(spendOf), opened = sum(g => g.price * 100 * g.n), best = boxes.reduce((a, g) => g.price > a.price ? g : a);
  return { uid: who.uid, nickname: who.nickname, tally: { spend, openedValue: opened, outcome: opened > spend ? 'Gain' : opened === spend ? 'Even' : 'Loss' },
    boxes: sum(g => g.n), bestGift: best.gift, bestPrice: best.price * 100, castles: sum(g => g.castle ? g.n : 0) };
}

/** Plays a route's steps on the frame grid of clock (frames) as check-game.mjs plays them in real time: wait renders frames for
 * that long, jump moves the clock on without any, do runs its code at the next frame's time (act: what it sends arrives in that
 * frame, as the board takes it sent), until renders frames until its test holds. */
export async function walk(steps, x, clock) {
  for (const [kind, arg] of steps) {
    if (kind === 'wait') for (const end = clock.t + arg; clock.t < end;) await clock.frame();
    if (kind === 'jump') clock.t += arg;
    if (kind === 'do') { clock.act(); new Function('x', arg)(x); }
    if (kind === 'until') for (let k = 0, ok = new Function('x', 'return ' + arg); !ok(x); k++) { if (k > 3600) throw new Error('never ' + arg); await clock.frame(); }
  }
}

/** The frame grid a route is walked on, from time t, 60 frames a second: act sets the time to the frame's (set), later(ms, fn)
 * runs fn ms from then, and frame runs what is due, has the frame drawn (draw, with its time and number) and moves on. */
export function frames(t, set, draw) {
  const timers = [], clock = { t, n: 0 };
  clock.act = () => set(clock.t);
  clock.later = (ms, fn) => timers.push([clock.t + ms, fn]);
  clock.frame = async () => {
    clock.act();
    for (const [when, fn] of timers.splice(0)) if (when <= clock.t) fn(); else timers.push([when, fn]);
    await draw(clock.t, clock.n);
    clock.n++; clock.t += 1000 / 60;
  };
  return clock;
}
