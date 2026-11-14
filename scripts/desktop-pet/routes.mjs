// The 宝箱小游戏's routes on the design package's board (docs/design/desktop-pet/tools/check-game.mjs), played on the 桌宠页 too:
// compare.mjs plays them on both in Chrome, DesktopPetCharacters.test.mjs on the page's engine in node. compare.mjs hands the
// browser the source of each function below but the board's, so each uses only its arguments and the others.
export { ROUTES } from '../../docs/design/desktop-pet/tools/check-game.mjs';

// The board's names of the shows, by the page's ids; and of the lottery's steps, by the 试演 variants of its one row.
export const NAME = { entry: '进场', gift: '普通礼物', giftStreak: '连着送', midGift: '中礼物', bigGift: '大礼物', blindBoxSmall: '盲盒·小', blindBoxMedium: '盲盒·中', blindBoxLarge: '盲盒·大',
  captain: '舰长', admiral: '提督', governor: '总督', castle: '城堡', newFirst: '新的第一名', pranksterKnocked: '打跑了', chestKept: '守住了', chestRobbed: '被偷了' };
export const STEP = { EntryOpen: '报名开场', EntryClosed: '报名截止', PoolOpen: '奖池开场', PoolCall: '奖池点名', PoolSpin: '奖池转动',
  RevealGrand: '揭晓·大奖', RevealSecond: '揭晓·二奖', RevealThird: '揭晓·三奖', NoEntries: '没人报名', End: '抽奖结束' };

/** What a route's steps call x, for the page `pet` in a connected room: what Core would tell it for each, the shows by the
 * board's names (names: NAME and STEP); a 一轮送礼 of a viewer's gifts, ended after 3 s without one, as Core's (send, end); a
 * castle's 盲盒播报 when the board's stands in for it. A helper's gifts are viewer 2's, who sent the keyword with x.join().
 * later(ms, fn) runs fn ms on from the time the step runs at. */
export function drive(pet, later, { NAME, STEP }) {
  const GUARD = { 舰长: 3, 提督: 2, 总督: 1 }, last = {};
  const state = so => ({ enabled: true, roomConnected: true, settings: pet.settings, lines: pet.lines, lottery: null, ...so });
  return {
    get gm() { return pet.gm; },
    get cur() { return pet.cur && { name: NAME[pet.cur.name] ?? STEP[pet.cur.opts.result] }; },
    visit: () => pet.summon(),
    heard: () => pet.danmaku({ uid: 3, nickname: '路人', joins: false }),
    join: () => pet.danmaku({ uid: 2, nickname: '星河', joins: true }),
    pkOn: on => pet.receive(state({ pk: on })),
    fire: (name, opts) => GUARD[name] ? pet.guard({ uid: 1, nickname: '星河', guardLevel: GUARD[name], spend: opts.price * 100 })
      : (pet.receive(state()), pet.receive(state({ lottery: { mode: 'Entry', id: 'E1', keyword: '抽奖', phase: 'Open', entryCount: 0, draws: [], drawUntil: null } }))),
    give: list => list.forEach((m, i) => {
      const gifts = [].concat(m), uid = gifts[0].helper ? 2 : 1;
      later(i * 350, () => send(pet, last, uid, gifts));
      later(i * 350 + 3000, () => end(pet, last, uid, gifts));
      for (const g of gifts.filter(g => g.castle)) later(i * 350 + 3800, () => pet.blindBox({ uid, nickname: '星河', tally: { spend: spendOf(g), openedValue: g.price * 100, outcome: 'Gain' }, boxes: g.n, bestGift: g.gift, bestPrice: g.price * 100 }));
    })
  };
}

/** A gift message of viewer uid's, the board's gifts `list`, told with its gifts as Core tells them: the last message of the
 * viewer's round (last) until another comes. */
export function send(pet, last, uid, list) {
  last[uid] = list;
  const before = pet.cur;
  told(pet, uid, { items: itemsOf(list) });
  // the board counts a gift for 开心 again as its show starts (src/Aether.Core/Pages/desktop-pet/README.md, deliberate differences)
  if (pet.cur !== before && pet.cur.opts.round) pet.recent.push(pet.now());
}

/** The viewer's round ends, unless another gift message came after `list`. */
export function end(pet, last, uid, list) {
  if (last[uid] === list) told(pet, uid, { ended: true });
}

/** The viewer's 一轮送礼 as Core tells it. */
export function told(pet, uid, so) {
  pet.gift({ uid, nickname: '星河', ended: false, items: [], ...so });
}

/** The board's gifts `list` as Core tells a gift message's (GiftItem), each gift's name standing in for its id. */
export function itemsOf(list) {
  return list.map(g => ({ giftId: g.gift, name: g.gift, price: g.price * 100, spend: spendOf(g), count: g.n, blind: !!g.box, castle: !!g.castle, iconUrl: '' }));
}

/** What a gift of the board's cost, in 金瓜子: a 盲盒 its box's price. */
export function spendOf(g) { return (g.box ? g.boxPrice : g.price) * 100 * g.n; }

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
