// The 宝箱小游戏's routes on the design package's board (docs/design/desktop-pet/tools/check-game.mjs), played on the 桌宠页 too:
// compare.mjs plays them on both in Chrome, DesktopPetCharacters.test.mjs on the page's engine in node. drive and walk are whole
// in themselves, for compare.mjs hands their source to the browser.
export { ROUTES } from '../../docs/design/desktop-pet/tools/check-game.mjs';

// The board's names of the shows, by the page's ids; and of the lottery's steps, by the 试演 variants of its one row.
export const NAME = { entry: '进场', gift: '普通礼物', giftStreak: '连着送', midGift: '中礼物', bigGift: '大礼物', blindBoxSmall: '盲盒·小', blindBoxMedium: '盲盒·中', blindBoxLarge: '盲盒·大',
  captain: '舰长', admiral: '提督', governor: '总督', castle: '城堡', newFirst: '新的第一名', pranksterKnocked: '打跑了', chestKept: '守住了', chestRobbed: '被偷了' };
export const STEP = { EntryOpen: '报名开场', EntryClosed: '报名截止', PoolOpen: '奖池开场', PoolCall: '奖池点名', PoolSpin: '奖池转动',
  RevealGrand: '揭晓·大奖', RevealSecond: '揭晓·二奖', RevealThird: '揭晓·三奖', NoEntries: '没人报名', End: '抽奖结束' };

/** What a route's steps call x, for the page `pet` in a connected room: what Core would tell it for each, the shows by the
 * board's names (names: NAME and STEP); a 一轮送礼 of a viewer's gifts, ended after 3 s without one, as Core's; a castle's
 * 盲盒播报 when the board's stands in for it; a helper's gift from a viewer the board takes for a helper whether or not they
 * sent the keyword. later(ms, fn) runs fn ms on from the time the step runs at. */
export function drive(pet, later, { NAME, STEP }) {
  const GUARD = { 舰长: 3, 提督: 2, 总督: 1 }, rounds = {}, stamps = {};
  const state = so => ({ enabled: true, roomConnected: true, settings: pet.settings, lines: pet.lines, lottery: null, ...so });
  const told = (uid, r, so) => pet.gift({ uid, nickname: '星河', ...r, topCount: r.count, castle: false, ended: false, items: [], ...so });
  const spendOf = g => (g.box ? g.boxPrice : g.price) * 100 * g.n;
  const send = (uid, list, stamp) => {
    const r = rounds[uid] ??= { spend: 0, count: 0, boxSpend: 0, boxes: 0 }, items = list.map(g => ({ spend: spendOf(g), count: g.n, blind: !!g.box, iconUrl: '' }));
    for (const g of list) if (g.box) { r.boxSpend += spendOf(g); r.boxes += g.n; } else { r.spend += spendOf(g); r.count += g.n; }
    stamps[uid] = stamp;
    // the board takes a helper's gift for one whether or not they sent the keyword: so is it here, the helpers as they were
    const v = pet.gm.visit, helped = uid === 2 && v && !v.helpers.has(uid), before = pet.cur;
    if (helped) v.helpers.add(uid);
    told(uid, r, { topGift: list[0].gift, castle: list.some(g => g.castle), items });
    if (helped) v.helpers.delete(uid);
    // the board counts a gift for 开心 again as its show starts (src/Aether.Core/Pages/desktop-pet/README.md, deliberate differences)
    if (pet.cur !== before && pet.cur.opts.round) pet.recent.push(pet.now());
  };
  const end = (uid, stamp) => { if (stamps[uid] === stamp) { told(uid, rounds[uid], { ended: true }); delete rounds[uid]; } };
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
      const gifts = [].concat(m), uid = gifts[0].helper ? 2 : 1, stamp = {};
      later(i * 350, () => send(uid, gifts, stamp));
      later(i * 350 + 3000, () => end(uid, stamp));
      for (const g of gifts.filter(g => g.castle)) later(i * 350 + 3800, () => pet.blindBox({ uid, nickname: '星河', tally: { spend: spendOf(g), openedValue: g.price * 100, outcome: 'Gain' }, boxes: g.n, bestGift: g.gift, bestPrice: g.price * 100, castle: true }));
    })
  };
}

/** Plays a route's steps on the frame grid of clock ({ t, frame(), act() }) as check-game.mjs plays them in real time: wait
 * renders frames for that long, jump moves the clock on without any, do runs its code at the next frame's time (act: what it
 * sends arrives in that frame, as the board takes it sent), until renders frames until its test holds. */
export async function walk(steps, x, clock) {
  for (const [kind, arg] of steps) {
    if (kind === 'wait') for (const end = clock.t + arg; clock.t < end;) await clock.frame();
    if (kind === 'jump') clock.t += arg;
    if (kind === 'do') { clock.act(); new Function('x', arg)(x); }
    if (kind === 'until') for (let k = 0, ok = new Function('x', 'return ' + arg); !ok(x); k++) { if (k > 3600) throw new Error('never ' + arg); await clock.frame(); }
  }
}
