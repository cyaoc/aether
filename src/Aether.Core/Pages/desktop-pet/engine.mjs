// The 桌宠's engine, ported from the design package's board (docs/design/desktop-pet/boards/DP-DesktopPet.dc.html): which
// show plays when (tiers, the queue, a 一轮送礼 growing into bigger shows, blind boxes waiting for their result), her 心情
// and 日常小动作, and what she and her sprite are doing at any moment. It draws nothing (Pet in pet.mjs does) and reads no
// picture, so node can run it.
import { k, smooth as ease } from '../video/animations/toolkit.mjs';
import { Changes } from '../gift-board/changes.mjs';
import { viewerName } from '../lottery/viewer.mjs';

export const seeded = seed => () => { seed |= 0; seed = seed + 0x6D2B79F5 | 0; let t = Math.imul(seed ^ seed >>> 15, 1 | seed); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
export const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
export const k01 = (t, a, b) => k(a, b, t);
export { ease };
export const bump = (t, a, b) => Math.sin(Math.PI * k01(t, a, b));
// A drawing swap over 150 ms: the new one comes in over the old, which only then goes, so neither shows through the other.
export const xfade = u => [1 - ease(k01(u, 60, 150)), ease(k01(u, 0, 90))];
const at = (t, list) => { let v = list[0][1]; for (const [s, x] of list) if (t >= s) v = x; return v; };

// Effects on top of each show's own, stronger the higher the tier (design package README 7.1): C a manga symbol and a small
// burst; B a ring on the ground and things floating up; A light behind her and a shake at the peak; S the whole page. A
// lottery's are its steps'.
// fx: [type, from ms, ...]; shake: [from ms, px, ms].
const EVENT_FX = {
  entry: { fx: [['bang', 0], ['spark', 150, 0.2, 0.18, 6], ['halo', 0, 900, 0.25]] },
  gift: { fx: [['giftArc', 0], ['halo', 300, 900, 0.3]] },
  giftStreak: { fx: [['giftStack', 0], ['steam', 1300, 1200], ['spark', 1250, 0.5, 0.3, 10], ['halo', 1250, 1300, 0.35]] },
  midGift: { fx: [['boxDrop', 200], ['spark', 1300, 0.5, 0.58, 8], ['shockwave', 1200], ['halo', 1200, 2000, 0.4], ['anchors', 1400, 1900, 'hearts']], shake: [[1200, 2, 200]] },
  blindBoxMedium: {
    fx: [['shakeMarks', 0, 0.83, 0.44, 1200], ['seamGlow', 0, 1200, 'shake'], ['halo', 1200, 2600, 0.45], ['shockwave', 2600]],
    shake: [[1200, 2, 200]]
  },
  blindBoxSmall: { fx: [['shakeMarks', 0, 0.83, 0.44, 900]] },
  blindBoxLarge: {
    fx: [['shakeMarks', 0, 0.83, 0.44, 1500], ['seamGlow', 0, 1500, 'shake'], ['rays', 3500, 2300, 0.55], ['burst', 3500, 400], ['shockwave', 3500]],
    shake: [[2500, 3, 260]]
  },
  bigGift: {
    fx: [['pillar', 0, 2800, 390, 626, 70], ['shockwave', 2800, null, { x: 390, y: 624 }], ['dust', 2800, null, 6, { x: 390, y: 626 }], ['bang', 2850], ['shock', 2850],
      ['pillar', 3400, 1300, 390, 470, 100], ['fountain', 3400, 2000, { x: 390, y: 470 }], ['rays', 4400, 1600, 0.55], ['burst', 4400, 300], ['confetti', 4400], ['hearts', 4600, 4]],
    shake: [[2800, 2, 200], [3400, 3, 260]]
  },
  newFirst: { fx: [['rays', 1300, 1000, 0.35], ['glint', 1350, 0.5, 0], ['spark', 1300, 0.5, 0.05, 8]] },
  captain: { fx: [['shockwave', 600], ['splash', 600], ['anchors', 700, 2400], ['glint', 1000, 0.14, 0.3], ['halo', 600, 2400, 0.3]] },
  admiral: {
    fx: [['shockwave', 700], ['splash', 700], ['spark', 3600, 0.2, 0.3, 12], ['rays', 3600, 2600, 0.55], ['burst', 3600, 300], ['confetti', 3700]],
    shake: [[700, 2, 200], [3600, 3, 240]]
  },
  castle: {
    fx: [['bang', 300], ['spark', 2200, 0.5, 0.4, 10], ['petals', 2600, 6400], ['halo', 3000, 1500, 0.4], ['stepGlow', 4500, 3000], ['towerSparkles', 7500, 1600],
      ['dust', 9800], ['shockwave', 9800]],
    shake: [[1600, 2, 240]]
  },
  EntryOpen: { fx: [['spot', 0, 2200], ['bang', 0], ['confetti', 200]] },
  PoolOpen: { fx: [['spot', 0, 2200], ['bang', 0], ['confetti', 200]] },
  EntryClosed: { fx: [['shock', 0], ['sweat', 300, 1600]] },
  PoolSpin: { fx: [['starRing', 0, 1700], ['sweat', 400, 1600]] },
  PoolCall: { fx: [['spot', 0, 1700], ['bang', 100], ['spark', 300, 0.9, 0.3, 8]] },
  RevealGrand: { fx: [['sweat', 600, 1300], ['burst', 2500, 400], ['rays', 2500, 2400, 0.75], ['halo', 2500, 2400, 0.7]], shake: [[2500, 3, 260]] },
  RevealSecond: { fx: [['sweat', 600, 1300], ['halo', 2500, 1800, 0.35], ['confetti', 2500]] },
  RevealThird: { fx: [['sweat', 600, 1300], ['spark', 2500, 0.5, 0.15, 6], ['hearts', 2700, 2]] },
  NoEntries: { fx: [['gloom', 200, 2200], ['rain', 400, 2000], ['leaf', 300]] },
  End: { fx: [['halo', 0, 2000, 0.3], ['hearts', 300, 3], ['spark', 1450, 0.2, 0.2, 8]] }
};
export const TIER = { SS: 5, S: 4, A: 3, B: 2, C: 1, D: 0 };
// Each tier's own glow (README 7.1), like gacha rarity: cool and quiet low, warmer higher; gold is the governor's, and a
// blind box jackpot's.
export const GLOW = { SS: '254,197,79', S: '253,183,202', A: '180,140,255', B: '124,200,255', C: '255,211,223', D: '255,255,255' };
// She holds her breath this long in a lottery's reveal before she reacts; a draw is revealed this long before its DrawUntil,
// so she reacts as the 抽奖页's effects end and steals none of their show (README 4).
const REVEAL = 2500;
// The lottery's steps (README 4), each a show of its own on the board, are the 试演 variants of the 演出表's one lottery row:
// how long each lasts and the kind of line it says; the grand prize shines gold, a gacha's top pull, though it is an A show.
const LOTTERY = {
  EntryOpen: { dur: 2600, line: 'lotteryEntryOpen' }, EntryClosed: { dur: 2000, line: 'lotteryEntryClosed' },
  PoolOpen: { dur: 2600, line: 'lotteryPoolOpen' }, PoolCall: { dur: 2000, line: 'lotteryPoolCall' }, PoolSpin: { dur: 2000, line: 'lotteryPoolSpin' },
  RevealGrand: { dur: 5200, line: 'lotteryGrand', glow: 'SS' }, RevealSecond: { dur: 4600, line: 'lotterySecond' }, RevealThird: { dur: 4600, line: 'lotteryThird' },
  NoEntries: { dur: 2800, line: 'lotteryNoEntries' }, End: { dur: 2600, line: 'lotteryEnd' }
};
// How long each show lasts and, for the lottery, its steps; its 演出档 and group are the 演出表's (shows.json).
const EVENTS = {
  entry: { dur: 1600 }, gift: { dur: 2000 }, giftStreak: { dur: 2600 },
  blindBoxSmall: { dur: 2600 }, midGift: { dur: 3500 }, blindBoxMedium: { dur: 4000 },
  captain: { dur: 4500 }, bigGift: { dur: 6000 }, blindBoxLarge: { dur: 6000 },
  admiral: { dur: 8000 }, governor: { dur: 15000 }, castle: { dur: 11500 }, newFirst: { dur: 2600 }, lottery: { steps: LOTTERY }
};
// A 大航海 by B站's guard_level: 1 总督, 2 提督, 3 舰长.
const GUARDS = ['governor', 'admiral', 'captain'];
// What a 试演 thanks for, where its line names a gift.
const GIFT = { gift: '小花花', giftStreak: '小花花', midGift: '告白气球', bigGift: '小电视飞船', blindBoxSmall: '棉花糖', blindBoxMedium: '爱心抱枕', blindBoxLarge: '爱心抱枕', castle: '浪漫城堡' };
// What a 试演 of the lottery names as its keyword, award and prize.
const LOTTERY_TRIAL = { keyword: '抽奖', award: '一等奖', prize: '定制周边' };
// The admiral's ship stops behind her on the upper page, farther off (s), its bottom on y; her manifest has its mainmast top
// and where its flags are strung.
const ADM_SHIP = { x: 300, y: 300, s: 0.6 };
// Where the flagship's bow deck stops on the page: she stands there at her full size.
const SHIP_DECK = { x: 133, y: 398 };
// When a blind box show opens its box; after a round she has shaken it already, so the show starts there (start()).
const BOX_OPEN = { blindBoxSmall: 900, blindBoxMedium: 1200, blindBoxLarge: 1500 };
// Where the sprite listens while she shakes a blind box: beside the box at its height, clear of the box and of the shake
// marks around it (the box is at 0.83, 0.44 of her shakeBox drawing); it used to sit on the box, the marks over it.
const BOX_EAR = ['rel', 1.28, 0.4];
// A gift round's shows from small to big: the round plays the one it has grown to, moving up as it grows (README 2).
const GIFT_SHOWS = ['gift', 'giftStreak', 'midGift', 'bigGift', 'castle'], CASTLE = GIFT_SHOWS.indexOf('castle');
const BLIND_BOXES = ['blindBoxSmall', 'blindBoxMedium', 'blindBoxLarge'];
// The 演出表's groups the engine plays by (shows.json): a 心情 holds her rather than plays, a blind box says its ending's lines.
export const MOOD = 'mood';
const BLIND_BOX = 'blindBox';
// Which lines a blind box's ending (a 一轮盲盒's outcome as Core tells it, or Jackpot) says.
const BOX_LINES = { Gain: 'blindBoxGain', Even: 'blindBoxEven', Loss: 'blindBoxLoss', Jackpot: 'blindBoxJackpot' };
export const PAGE = { w: 480, h: 640 }, H = 250, FOOT = { x: 240, y: 626 };
export const SPRITE_H = 76;
// The sports (README 6), each in its own outfit: the sprite dresses her for it, she gets ready and goes for act ms; then
// she stops (calm) or, now and then, it goes wrong (fall); then the sprite dresses her back. [ms after the stop, pose];
// lines: [ms after it goes wrong, what she says], each line matching what she does then (no plaster talk without a plaster);
// hitAt and plasterAt: where she hits the ground and where the plaster goes, the same knee (fractions of her drawing).
const SPORTS = {
  gym: { ready: 'gymReady', act: ['curl', 3000], calm: [[0, 'gymReady'], [300, 'gymWipe']], calmMs: 1200,
    fall: [[0, 'gymHurt'], [1400, 'gymRub'], [2800, 'gymWipe']], fallMs: 3800, lines: [[0, 'droppedOnFoot']] },
  hike: { ready: 'hikeReady', act: ['hike', 3400], calm: [[0, 'hikeReady']], calmMs: 300,
    fall: [[0, 'slip'], [1400, 'hikeBrush'], [2800, 'hikeUp']], fallMs: 3800, lines: [[0, 'slipped'], [1400, 'dustOff']] },
  bike: { ready: 'bikeReady', act: ['bike', 3400], calm: [[0, 'bikeReady']], calmMs: 300,
    fall: [[0, 'crash'], [1500, 'bikePlaster'], [3300, 'bikeUp']], fallMs: 4800, lines: [[0, 'bikeCrash'], [1500, 'plaster']], plaster: 1500, plasterAt: [0.22, 0.79], hitAt: [0.43, 0.91] },
  ski: { ready: 'skiReady', act: ['ski', 2500], calm: [[0, 'skiReady']], calmMs: 300,
    fall: [[0, 'skiDown'], [1000, 'skiPopOut'], [2100, 'skiShake']], fallMs: 3300, lines: [[0, 'skiFall']] }
};
// The 日常小动作 by their ids in the 演出表, [main, more when it goes wrong]: dressing 0.7 s, getting ready 0.4 s, the act,
// the stop, dressing back 0.9 s
export const DAILY = { hum: [4000, 1500] };
for (const [kind, sp] of Object.entries(SPORTS)) DAILY[kind] = [1100 + sp.act[1] + sp.calmMs + 900, sp.fallMs - sp.calmMs];

// ---- the lottery on air, as Core tells it (LotteryState.cs)
// What she waits through while no show plays: entries open, or a draw coming (entries closed with someone to draw, a pool
// spinning or its draw on the way to its DrawUntil); a pool between draws is no wait, and has no end.
const lotOf = l => l?.mode === 'Entry' ? (l.phase === 'Open' ? 'open' : l.phase === 'Closed' && l.entryCount ? 'waiting' : null)
  : l?.mode === 'Pool' && (l.phase === 'Spinning' || l.drawUntil) ? 'waiting' : null;
// The steps a new state of a lottery calls for, after the last state of the lottery before it, each [step, what its line
// fills in]: a lottery opened; entries opened, and closed with or without anyone; a pool calling a viewer and spinning; one
// with entries ended, or a pool with draws given way to a new lottery.
function stepsOf(prev, next) {
  const was = prev?.id === next.id ? prev : null, steps = [];
  if (!was && prev?.mode === 'Pool' && prev.draws.length) steps.push(['End']);
  if (!was && next.mode === 'Pool') steps.push(['PoolOpen']);
  if (next.mode === 'Pool' && next.phase === 'Spinning' && was?.phase !== 'Spinning') {
    if (next.uid) steps.push(['PoolCall', { nickname: viewerName({ uid: next.uid }) }]);
    steps.push(['PoolSpin']);
  }
  const entry = { Open: [['EntryOpen', { keyword: next.keyword }]], Closed: [[next.entryCount ? 'EntryClosed' : 'NoEntries']], Ended: next.entryCount ? [['End']] : [] };
  if (next.mode === 'Entry' && next.phase !== was?.phase) steps.push(...entry[next.phase] ?? []);
  return steps;
}
// A draw's reveal by its grade, with who won what: an entry lottery's award and prize, a pool's prize as both.
const revealOf = (lottery, draw) => lottery.mode === 'Entry'
  ? { result: 'Reveal' + draw.award.grade, nickname: draw.winner.nickname, award: draw.award.name, prize: draw.award.prize }
  : { result: 'Reveal' + draw.prize.grade, nickname: viewerName(draw), award: draw.prize.name, prize: draw.prize.name };

/** The engine of the 桌宠页: Core tells it what happens (DesktopPet.cs) and it decides what she plays. `master` is the page's
 * one animation, whose currentTime is the time of everything; `shows` are the rows of the 演出表, which give each show its
 * 演出档; `art` is the current character's desktopPet (characters/README.md), whose props' points place the big shows. Their
 * pictures' sizes, `props` and `castle`, are Pet's to measure. A 试演 that is over raises `trialended` with its number (trialOver). */
export class Engine extends EventTarget {
  constructor(master, shows, art) {
    super();
    Object.assign(this, { master, art });
    this.events = Object.fromEntries(shows.map(row => [row.id, { ...EVENTS[row.id], tier: row.tier, group: row.group }]));
  }

  now() { return this.master.currentTime; }

  reset(rng) {
    this.cur = this.seen = this.trial = this.lot = this.reveal = null; this.queue = []; this.rounds = []; this.crowns = new Map(); this.lottery = undefined;
    this.changes = new Changes();
    this.lastGift = this.lastAny = this.now(); this.recent = []; this.nextDaily = this.now() + 6000; this.lastLine = {}; this.rng = rng;
  }

  // The 桌宠页's state: whether she is on, how the streamer set her to play and what she says, the lottery on air and the 礼物榜
  // while its 期 runs. Her quiet-room clocks start when the room connects; while none is she stays calm, and a round she was
  // shaking boxes for will not end.
  receive(state) {
    const now = this.now();
    Object.assign(this, { on: state.enabled, settings: state.settings, lines: state.lines });
    if (state.roomConnected && !this.room) { this.lastGift = this.lastAny = now; this.recent = []; }
    if (!state.roomConnected) { this.rounds = []; this.crowns.clear(); }
    this.room = state.roomConnected;
    // switched off she is told nothing more, and back on she starts from what she is told then
    if (!state.enabled) {
      this.cur = this.seen = this.trial = null; this.queue = []; this.rounds = []; this.crowns.clear();
      this.lottery = undefined; this.reveal = null; this.changes.reset(); return;
    }
    // a show turned off does not play, even one that was waiting its turn
    this.queue = this.queue.filter(q => !this.settings.off.includes(q.name));
    this.onAir(state.lottery);
    this.ranked(state.giftBoard);
  }

  // The lottery on air (README 4): she plays each step it takes and waits through it while no show plays (lot), and reveals
  // a draw at its DrawUntil, on the clock of the 抽奖页, which counts down to it the same way. The first state she is told is
  // where she starts from, and a lottery back on air (the room reconnected) is no new one.
  onAir(next) {
    const prev = this.lottery;
    this.lottery = next ?? prev ?? null;
    this.lot = this.settings.off.includes('lottery') ? null : lotOf(next);
    if (prev === undefined || !next) return;
    for (const [step, opts] of stepsOf(prev, next)) this.fire('lottery', { ...opts, result: step });
    const draw = next.draws.at(-1);
    if (draw && next.drawUntil && !(prev?.id === next.id && prev.draws.some(d => d.id === draw.id)))
      this.reveal = { at: this.now() + Date.parse(next.drawUntil) - Date.now() - REVEAL, opts: revealOf(next, draw) };
  }

  // The 礼物榜 while its 期 runs, none else: a gift that takes its first place, as changes.mjs tells the 礼物榜页, crowns the
  // new first (crown). A 大航海 plays in no 一轮送礼, and is not crowned.
  ranked(board) {
    if (!board) return this.changes.reset();
    this.changes.receive(board);
    for (let change; (change = this.changes.take());) if (change.kind === 'top' && !change.state.rows[0].guardLevel) this.crown(change.state.rows[0]);
  }

  // The crown is the viewer's 一轮送礼's: it follows the round's show playing or coming next, its blind boxes' once their
  // 盲盒播报 opens them (update), and with none it is not played on its own (README 2). The 礼物榜 reaches the page another
  // way than the gift that took its first place, and may come first: the crown then waits for that gift's round.
  crown({ uid, nickname }) {
    const r = this.rounds.find(round => round.uid === uid && !round.ended);
    if (r) r.top = nickname; else this.crowns.set(uid, nickname);
  }

  fire(name, opts = {}) {
    const now = this.now(), ev = this.events[name];
    if (!opts.chained) opts = { ...opts, wake: opts.wake ?? ['gloomy', 'sleepy'].includes(this.mood(now)) };
    // Anyone coming in counts as something happening for 犯困; a show turned off plays nothing, not a lesser one instead.
    this.lastAny = now;
    if (this.settings.off.includes(name)) return;
    // she waves at whoever comes in only while she is idle: never over a daily action, any show or blind boxes she shakes,
    // and it does not queue
    if (name === 'entry' && (this.cur || this.shaking())) return;
    const curTier = this.cur ? TIER[this.events[this.cur.name].tier] : -1, tier = TIER[ev.tier];
    // A gift round that grows to 5 gifts takes over its own plain thank-you, instead of being dropped as busy.
    // The two showpieces never cut each other short: the governor waits for the castle and then goes first.
    const show = name === 'governor' && this.cur?.name === 'castle';
    const grown = name === 'giftStreak' && this.cur?.name === 'gift' && this.cur.opts.round === opts.round;
    if (!this.cur || tier > curTier && !show || grown) return this.start(name, opts);
    if (tier <= TIER.C) return;
    if (tier < TIER.S && this.queue.filter(q => this.events[q.name].tier === ev.tier).length >= 3) return;
    this.queue.push({ name, opts });
  }

  start(name, opts) {
    this.trialOver(this.cur);
    const now = this.now(), { dailyMinSeconds, dailyMaxSeconds } = this.settings;
    const ev = this.events[name], wake = !!opts.wake, dur = DAILY[name] ? DAILY[name][0] + (opts.mishap ? DAILY[name][1] : 0) : (ev.steps?.[opts.result] ?? ev).dur;
    this.cur = { name, opts, start: now, pre: wake ? 1000 : 0, dur, line: this.pick(this.lineKey(name, opts)), wakeLine: wake ? this.pick('wake') : null };
    // a round's blind boxes were shaken all the while they came in: the show opens the box right away instead of shaking it again
    if (opts.shaken && BOX_OPEN[name]) this.cur.start -= BOX_OPEN[name];
    if (DAILY[name]) this.nextDaily = now + dur + dailyMinSeconds * 1000 + this.rng() * (dailyMaxSeconds - dailyMinSeconds) * 1000;
  }

  // A blind box says the lines of its ending, the lottery its step's; every other show its own.
  lineKey(name, opts = {}) {
    const ev = this.events[name];
    return ev?.group === BLIND_BOX ? BOX_LINES[opts.result || 'Gain'] : ev?.steps ? ev.steps[opts.result].line : name;
  }

  // ---- what Core tells it (DesktopPet.cs)
  // A gift, a blind box or a 大航海 counts for 开心 and wakes her from 沮丧; anything at all from 犯困.
  gifted(now) { this.lastGift = this.lastAny = now; this.recent = this.recent.filter(r => now - r < 60000).concat(now); }

  // A viewer's 一轮送礼 as it stands after a gift message, and once more as it ends (GiftRoundProgress). Gifts do not wait for
  // the round to end: she plays at once the show its gifts have come to so far, and moves up when it grows into a bigger one
  // (the fifth gift, the 中礼物 and 大礼物 thresholds); one already played is not played again. Its blind boxes wait for their
  // result (blindBox), and each gift message flies a little box into her arms.
  gift(p) {
    const now = this.now();
    let r = this.rounds.find(round => round.uid === p.uid && !round.ended);
    if (p.ended) {
      if (r) { r.ended = true; if (r.told === r.boxes) this.rounds.splice(this.rounds.indexOf(r), 1); }
      return;
    }
    // the first gift into a quiet room wakes her first (冷场后来了人), before the round's own show
    if (!r) {
      const wake = ['gloomy', 'sleepy'].includes(this.mood(now));
      this.rounds.push(r = { uid: p.uid, start: now, arrivals: [], boxes: 0, told: 0, wake, wakeLine: wake ? this.pick('wake') : null, played: -1, top: this.crowns.get(p.uid) });
      this.crowns.delete(p.uid);
    }
    r.arrivals.push(now); r.boxes = p.boxes;
    this.gifted(now);
    // a castle opened from one of the round's blind boxes, the only way B站 gives one, plays once instead of its gifts and
    // blind boxes, with the result that reports the box it came from
    if (p.castle && p.boxes > r.told && r.played < CASTLE) Object.assign(r, { castle: true, castleAt: p.boxes, played: CASTLE });
    if (!p.count || r.castle) return;
    const { midGift, bigGift } = this.settings;
    const kind = p.spend >= bigGift ? 'bigGift' : p.spend >= midGift ? 'midGift' : p.count >= 5 ? 'giftStreak' : 'gift';
    if (GIFT_SHOWS.indexOf(kind) <= r.played) return;
    this.fire(kind, { nickname: p.nickname, gift: p.topGift, count: kind === 'giftStreak' ? p.count : p.topCount, wake: r.played < 0 && r.wake, round: r });
    r.played = GIFT_SHOWS.indexOf(kind); r.wake = false;
  }

  // A 一轮盲盒's result, as its 盲盒播报 reported it (BlindBoxResult): the size by what the boxes cost, the ending by what they
  // opened; a single gift opened at the 大礼物 threshold is a jackpot and at least the big one. A castle in the round plays
  // the castle instead.
  blindBox(b) {
    const told = this.tell(b), { midGift, bigGift } = this.settings, r = told.find(t => t.top) ?? told[0];
    // A round's castle plays once: waiting on its 盲盒播报, with the result that tells the boxes up to it (a result before
    // it plays nothing, the castle standing in for it); another opened in a round that already played one, not again.
    const reached = told.filter(t => t.castle && t.told >= t.castleAt), played = told.some(t => t.played === CASTLE && !t.castle);
    if (reached.length || b.castle && !played) {
      for (const t of reached) t.castle = false;
      return this.fire('castle', { nickname: b.nickname, round: r });
    }
    if (told.some(t => t.played === CASTLE)) return;
    const jackpot = b.bestPrice >= bigGift, spend = b.tally.spend;
    this.fire(BLIND_BOXES[jackpot || spend >= bigGift ? 2 : spend >= midGift ? 1 : 0],
      { nickname: b.nickname, result: jackpot ? 'Jackpot' : b.tally.outcome, gift: b.bestGift, count: b.boxes, shaken: !!r, round: r });
  }

  // The rounds a result tells, oldest first: a 盲盒播报 reports every box sent before it went out, over however many 一轮送礼
  // they came in, and none sent while it went out, which wait for their own. A round is done once it ended and all is told.
  tell(b) {
    const told = [];
    for (let left = b.boxes, r; left > 0 && (r = this.rounds.find(x => x.uid === b.uid && x.boxes > x.told));) {
      const n = Math.min(left, r.boxes - r.told);
      r.told += n; left -= n; told.push(r);
      if (r.ended && r.told === r.boxes) this.rounds.splice(this.rounds.indexOf(r), 1);
    }
    return told;
  }

  entry(e) { this.fire('entry', { nickname: e.nickname }); }

  // a danmaku is not played (README 10), it only tells her someone is there: 犯困 needs an hour with nothing at all
  danmaku() { this.lastAny = this.now(); }

  // A 大航海 plays at once, in no 一轮送礼, and is a gift to her 心情.
  guard(g) { this.fire(GUARDS[g.guardLevel - 1], { nickname: g.nickname }); this.gifted(this.now()); }

  // A 试演 plays the show from its start (a blind box from its shaking), or holds her in the 心情, until it is stopped.
  testShow({ show, variant, nickname, id }) {
    this.trial = null;
    if (this.events[show].group === MOOD) { this.trial = show; this.cur = null; return; }
    this.start(show, { nickname, result: variant, mishap: variant === 'Mishap', trial: true, trialId: id });
  }

  // Only the 试演: a show of what happened since it plays on.
  stopTestShow() {
    this.trial = null;
    if (this.cur?.opts.trial) this.cur = null;
  }

  // A 试演 of a show played out, or cut short by another show, is over, and the page tells Core by its number (page.mjs); one
  // stopped is Core's own doing.
  trialOver(show) { if (show?.opts.trial) this.dispatchEvent(new CustomEvent('trialended', { detail: show.opts.trialId })); }

  // The round whose blind boxes she shakes while no show plays.
  shaking() { return this.rounds.find(r => r.boxes > r.told); }

  // While a round with blind boxes is still coming in she does not just wait: she shakes the box the whole time, until the
  // 盲盒播报 tells how it came out, and each gift flies into her arms.
  roundState(now, r) {
    const st = this.base();
    st._t = now - r.start; st.round = r;
    if (r.wake && st._t < 1000) {
      st.pose = 'awake'; st.sprite = 'surprised'; st.fx.push(['bang', 0], ['shock', 0], ['sweat', 200]); st.y -= 10 * bump(st._t, 0, 300);
      st.bubble = { text: this.fill(r.wakeLine), t: st._t };
      return st;
    }
    st.pose = 'shakeBox'; st.sprite = 'sLook'; st.sFlip = true; st.sp = BOX_EAR;
    for (const x of r.arrivals) st.fx.push(['giftArc', x - r.start]);
    st.fx.push(['shakeMarks', 0, 0.83, 0.44, 1e9]);
    return st;
  }

  pick(name) {
    const list = this.lines[name]; if (!list) return null;
    let i = Math.floor(this.rng() * list.length);
    if (list.length > 1 && i === this.lastLine[name]) i = (i + 1) % list.length;
    this.lastLine[name] = i;
    return list[i];
  }

  update(now) {
    if (this.reveal && now >= this.reveal.at) { this.fire('lottery', this.reveal.opts); this.reveal = null; }
    const c = this.cur;
    if (c && now >= c.start + c.pre + c.dur) {
      this.cur = this.seen = null;
      this.trialOver(c);
      // the first place its round took on the 礼物榜 is crowned next, once (crown)
      const round = c.opts.round, top = round?.top;
      if (round) round.top = null;
      if (top && !this.settings.off.includes('newFirst')) this.start('newFirst', { nickname: top, chained: true });
      else if (this.queue.length) {
        const best = Math.max(...this.queue.map(q => TIER[this.events[q.name].tier]));
        const i = this.queue.findIndex(q => TIER[this.events[q.name].tier] === best);
        const [q] = this.queue.splice(i, 1);
        this.start(q.name, { ...q.opts, chained: true });
      }
    }
    // not while she shakes a round's blind boxes either: that lasts until the 盲盒播报 goes out, which may take half a minute;
    // nor while she waits through a lottery
    const kinds = Object.keys(DAILY).filter(kind => !this.settings.off.includes(kind));
    if (!this.cur && !this.shaking() && !this.lot && this.mood(now) === 'calm' && now >= this.nextDaily && kinds.length) {
      const kind = kinds[Math.floor(this.rng() * kinds.length)], mishap = this.rng() * 100 < this.settings.mishapPercent;
      this.start(kind, { mishap });
    }
  }

  // 开心 counts only gifts, 大航海 included: a room viewers keep coming into would otherwise never be calm, and the daily actions
  // only come when she is calm (README 6). A 心情 set to 0 minutes never comes.
  mood(now) {
    if (this.trial) return this.trial;
    if (!this.room) return 'calm';
    const recent = this.recent.filter(r => now - r < 60000).length, { boredMinutes, gloomyMinutes, sleepyMinutes } = this.settings;
    const quiet = (since, minutes) => minutes > 0 && now - since >= minutes * 60000;
    if (quiet(this.lastAny, sleepyMinutes)) return 'sleepy';
    if (quiet(this.lastGift, gloomyMinutes)) return 'gloomy';
    if (quiet(this.lastGift, boredMinutes)) return 'bored';
    return recent >= 3 ? 'happy' : 'calm';
  }

  // ---- what she is doing at a moment --------------------------------------------------------------------------------
  // The show playing (after waking up, when it woke her), else the round she shakes blind boxes for, else her 心情; a round's
  // gifts still fly into her arms over whatever she is doing.
  stateAt(now) {
    this.update(now);
    const c = this.cur, shaking = this.shaking();
    let st;
    if (c && now < c.start + c.pre) { st = this.base(); st.pose = 'awake'; st.sprite = 'surprised'; st.fx.push(['bang', c.start], ['shock', c.start], ['sweat', c.start + 200]); st.y -= 10 * bump(now - c.start, 0, 300); st.bubble = { text: this.fill(c.wakeLine), t: now - c.start }; }
    else if (c) {
      st = this.eventState(c.name, now - c.start - c.pre, { ...c.opts, seed: Math.floor(c.start) }, this.fill(c.line, c.name, c.opts));
      // she says her line at the castle's gate once she gets there
      if (c.name === 'castle' && st.bubble?.at) st.bubble.text = this.fill(c.gateLine ??= this.pick('castleGate'), c.name, c.opts);
    } else if (shaking) st = this.roundState(now, shaking);
    else {
      const mood = this.mood(now), key = mood + (this.lot ?? '');
      if (this.seen?.key !== key) this.seen = { key, at: now };
      st = this.idleState(now, mood, now - this.seen.at);
    }
    for (const r of this.rounds) if (r !== st.round) for (const x of r.arrivals) st.fx.push(['giftArc', st._t == null ? x : st._t - (now - x)]);
    return st;
  }

  base() { return { pose: 'idle', x: FOOT.x, y: FOOT.y, s: 1, sx: 1, sprite: 'sIdle', sp: null, sBehind: false, sScale: 1, sAlpha: 1, fx: [], bubble: null }; }

  // since: how long she has been in this mood (or lottery wait) without an event.
  idleState(now, mood, since = now) {
    const st = this.base();
    if (this.lot === 'waiting') { st.pose = 'hope'; st.fx.push(['sweat', 0, 2400]); this.talk(st, 'lotteryWaiting', since); return st; }
    if (mood === 'happy') { st.y -= 6 * Math.abs(Math.sin(now / 380)); st.orbit = now; }
    if (mood === 'calm' && now % 7000 > 5800) st.sprite = 'sLook';  // now and then it looks up at her
    if (mood === 'bored') { st.fx.push(['dots', 0, 6000], ['sigh', 0, 7000]); }
    if (mood === 'gloomy') st.fx.push(['gloom', 0]);
    if (mood === 'bored') { st.pose = 'bored'; st.sprite = now % 8000 < 2600 ? 'yawn' : 'sIdle'; st.sp = ['home', 10 * Math.sin(now / 1800), 6 * Math.sin(now / 1300)]; }
    if (mood === 'gloomy') { st.pose = 'gloomy'; st.sprite = 'pat'; st.sp = 'head'; }
    if (mood === 'sleepy') { st.pose = 'doze'; st.sprite = 'sleep'; st.sp = 'knee'; }
    this.talk(st, this.lot === 'open' ? 'lotteryEntryOngoing' : mood, since);
    if (mood === 'sleepy') {
      // every other half minute her head drops onto her knees: she jolts awake, insists she wasn't asleep, and dozes off again
      const u = since % 30000;
      const jolts = this.lines.jolt;
      if (u >= 23000 && u < 24400) { st.pose = 'awake'; st.y -= 10 * bump(u, 23000, 23300); st.sprite = 'surprised'; st.bubble = { text: jolts[Math.floor(since / 30000) % jolts.length], t: u - 23000 }; st.fx.push(['bang', now - (u - 23000)], ['shock', now - (u - 23000)]); }
      else st.fx.push(['z', now]);
    }
    return st;
  }

  // A mood or a lottery wait says its first line 0.6 s in and the next one every 15 s after (平静 and 开心 say nothing).
  talk(st, key, since) {
    const lines = this.lines[key], u = since % 15000;
    if (lines && u >= 600 && u < 4600) st.bubble = { text: this.fill(lines[Math.floor(since / 15000) % lines.length], key, { keyword: this.lottery?.keyword }), t: u - 600 };
  }

  // A show's state t ms in; a lottery's is its step's (opts.result).
  eventState(name, t, opts, line) {
    const st = this.base(), step = this.events[name]?.steps?.[opts.result], key = step ? opts.result : name;
    st._t = t; st.glow = GLOW[step?.glow || this.events[name]?.tier || 'D'];
    const say = (from, text = line) => { if (t >= from && text) st.bubble = { text, t: t - from }; };
    const hop = (a, h = 10) => { st.y -= h * bump(t, a, a + 320); if (h >= 10) st.fx.push(['dust', a + 300]); };  // a puff where she lands
    switch (key) {
      // it looks up at her first, then waves its flag with her
      case 'entry': st.pose = 'wave'; hop(0, 8); st.sprite = t < 350 ? 'sLook' : 'flag'; say(100); break;
      // they bow together
      case 'gift': st.pose = t < 1500 ? 'bow' : 'idle'; st.sprite = t < 1500 ? 'nod' : 'sHappy'; st.fx.push(['hearts', 200, 3]); say(150); break;
      // it copies her: up beside her shoulder, hiding its face too
      // little boxes fly in from both sides one after another; at the fifth she hides her face
      case 'giftStreak':
        st.pose = t < 1250 ? 'idle' : 'shy'; if (t >= 1250) { st.sprite = 'hide'; st.sp = ['rel', -0.04, 0.42]; } else st.sprite = 'sLook';
        if (t >= 1250) st.fx.push(['hearts', 1250, 7]); say(1250); break;
      // the box drops from above her head into her hands; she cheers and tosses the sprite
      case 'midGift':
        st.pose = at(t, [[0, 'idle'], [1100, 'giftBox'], [2000, 'cheer']]); hop(1200, 6); hop(2000, 14);
        if (t < 2000) { st.sprite = 'sLook'; st.sp = ['rel', 0.04, 0.52]; } else this.toss(st, t, 2000);
        say(1200); break;
      case 'bigGift': this.bigGift(t, st, say); break;
      // it listens at the box with her (its seams glowing in the tier's colour first) and is blown back when it opens; then
      // the round's ending: a win cheers (a big one sends a beam to the top of the page and a star that bursts over it),
      // breaking even she smiles and nods, a loss pouts and stamps and then bows her thanks while it pats her head
      case 'blindBoxSmall': case 'blindBoxMedium': case 'blindBoxLarge': {
        const size = BLIND_BOXES.indexOf(name), r = opts.result || 'Gain', win = r === 'Gain' || r === 'Jackpot', loss = r === 'Loss';
        // the light is the tier's (中 sea blue, 大 purple); a jackpot shines gold, the top colour
        if (r === 'Jackpot') st.glow = GLOW.SS;
        const o = BOX_OPEN[name], wow = [0, 2000, 2600][size];
        const steps = size === 0 ? [[0, 'shakeBox'], [o, 'openBox'], [1500, loss ? 'pout' : 'nod']]
          : loss ? [[0, 'shakeBox'], [o, 'openBox'], [[0, 2000, 3500][size], 'pout'], [[0, 3000, 4700][size], 'bow']]
          : [[0, 'shakeBox'], [o, 'openBox'], [wow, 'surprised'], [[0, 2600, 3500][size], win ? 'cheer' : 'nod']];
        const react = steps[size === 0 ? 2 : loss ? 2 : 3][0], thanks = loss && size ? steps[3][0] : Infinity;
        st.pose = at(t, steps);
        if (win && size) hop(react, 14); else if (win) hop(react, 8);
        if (loss && t >= react && t < thanks) st.y -= 4 * Math.abs(Math.sin((t - react) / 90));
        if (t < o) { st.sprite = 'sLook'; st.sFlip = true; st.sp = BOX_EAR; }
        else if (t < react) { if (loss && !size) { st.sprite = 'hide'; st.sp = ['rel', 1.1, 0.3]; } else { st.sprite = 'surprised'; st.sp = ['rel', 1.12, 0.02]; } }
        else if (win) { st.sprite = 'sHappy'; st.sp = ['home', 0, -14 * bump(t, react, react + 320)]; }
        else if (loss && t < thanks) { st.sprite = 'pat'; st.sp = 'head'; }
        else st.sprite = 'nod';
        if (size && !loss) st.fx.push(['bang', wow], ['shock', wow]);
        if (size === 2) st.fx.push(['pillar', o, 1100, null, 'box'], ['bigStar', o], ['fountain', 2500, 1800, { x: FOOT.x, y: 200 }]);
        if (size === 1) st.fx.push(['beam', o], ['fountain', o + 50, 1300, { x: FOOT.x, y: 500 }]);
        if (win) st.fx.push(['sparkles', react], ...(size ? [['confetti', react]] : []));
        if (loss) st.fx.push(...(size ? [] : [['puff', o]]), ['sweat', react - 550], ['gloom', react - 500, 450], ['anger', react, 900]);
        say(o + 100); break;
      }
      // the sprite flies up to her head with a crown and puts it on her, and flies back
      case 'newFirst':
        st.pose = t < 1300 ? 'idle' : 'crownV';
        st.sprite = t < 1600 ? 'crown' : 'sHappy';
        st.sp = t < 1600 ? ['crown', ease(k01(t, 0, 900)), ease(k01(t, 900, 1300))] : ['home-from-top', ease(k01(t, 1600, 2400))];
        st.fx.push(['ring', 1300], ['sparkles', 1300]); say(1300); break;
      // the sprite dresses her, salutes beside her in its sailor collar, and dresses her back
      case 'captain':
        if (t < 600) this.dress(st, t, 0);
        else { st.pose = 'capSalute'; hop(600, 8); st.sprite = 'sailor'; st.sp = ['rel', -0.1, 0.42]; }
        if (t >= 3700) { st.pose = t < 4300 ? 'capSalute' : 'idle'; if (t < 4300) this.dress(st, t, 3700); else st.sprite = 'sHappy'; }
        st.fx.push(['flash', 600, 0.6], ['seaBubbles', 600], ['flash', 4300, 0.6]); say(800); break;
      case 'admiral': this.admiral(t, st, say); break;
      case 'governor': this.governor(t, st, say); break;
      case 'castle': this.castleShow(t, st, say); break;
      // it flies up beside her raised fist with its flag
      case 'EntryOpen': case 'PoolOpen': st.pose = 'announce'; hop(0, 12); st.sprite = 'flag'; st.sp = ['rel', -0.18, 0.05]; st.fx.push(['sparkles', 0]); say(150); break;
      // both hold their breath: it looks up at her, trembling
      case 'EntryClosed': case 'PoolSpin': st.pose = 'hope'; st.sprite = 'sLook'; st.sp = ['home', 1.5 * Math.sin(t / 30), 0]; say(150); break;
      // it dashes out the way she points, toward the viewer, waving its flag
      case 'PoolCall': st.pose = 'point'; st.sprite = 'flag'; st.sp = ['rel', 1.0, 0.32]; st.sScale = 1 + 0.3 * bump(t, 200, 1600); say(150); break;
      case 'RevealGrand': case 'RevealSecond': case 'RevealThird': {
        const react = REVEAL;
        st.pose = t < react ? 'hope' : { RevealGrand: 'cheer', RevealSecond: 'clap', RevealThird: 'nod' }[key];
        if (t < react) { st.sprite = 'sLook'; st.sp = ['home', 1.5 * Math.sin(t / 30), 0]; }
        else if (key === 'RevealGrand') { hop(react, 14); st.fx.push(['confetti', react]); this.toss(st, t, react); }
        else if (key === 'RevealSecond') { st.sprite = 'sHappy'; st.sp = ['home', 0, -8 * Math.abs(Math.sin(Math.PI * (t - react) / 440))]; st.fx.push(['sparkles', react]); }
        else st.sprite = 'nod';
        say(react); break;
      }
      // she bows her thanks with the sprite, straightens up and waves goodbye while it waves its flag
      case 'End': st.pose = at(t, [[0, 'bow'], [1200, 'idle'], [1450, 'wave']]); st.sprite = t < 1200 ? 'nod' : 'flag'; say(150); break;
      case 'NoEntries': st.pose = 'dejected'; st.sprite = 'pat'; st.sp = 'head'; say(200); break;
      case 'hum': case 'gym': case 'hike': case 'bike': case 'ski': return this.daily(t, st, { ...opts, kind: name });
    }
    const extra = EVENT_FX[key];
    if (extra) { st.fx.push(...extra.fx); st.shake = extra.shake; }
    return st;
  }

  daily(t, st, o) {
    if (SPORTS[o.kind]) return this.sport(t, st, o);
    // humming: the sprite sways along; when her voice cracks it hides its head
    const main = DAILY[o.kind][0];
    st._t = t;
    const say = (from, key) => { if (t >= from) st.bubble = { text: this.lines[key][(o.seed || 0) % this.lines[key].length], t: t - from }; };
    if (t < main) { st.pose = 'hum'; st.fx.push(['notes', 0]); st.sprite = 'sHappy'; st.sp = ['home', 6 * Math.sin(2 * Math.PI * t / 1280), 0]; say(200, o.kind); return st; }
    st.pose = 'crack'; st.sprite = 'hide'; st.fx.push(['brokenNote', main], ['shock', main], ['sweat', main + 300]); say(main, 'voiceCrack');
    return st;
  }

  // A sport (SPORTS): the sprite joins in (cheers her lifts with its flag, leads her hike, flies beside her bike or skis); if
  // it goes wrong it starts, then rubs her foot or pats her head, and nods along while she puts a plaster on bare skin.
  sport(t, st, o) {
    st._t = t;
    const sp = SPORTS[o.kind], go = 1100, stop = go + sp.act[1], end = stop + (o.mishap ? sp.fallMs : sp.calmMs) + 900, back = end - 900;
    const say = (from, key) => { if (t >= from) st.bubble = { text: this.lines[key][(o.seed || 0) % this.lines[key].length], t: t - from }; };
    const lead = o.kind === 'hike' ? ['at', 70, FOOT.y - 0.62 * H + 5 * Math.sin(t / 300)] : ['rel', -0.22, 0.3];
    st.fx.push(['flash', 700, 0.8], ['flash', back + 700, 0.8]);
    if (o.kind === 'ski') st.fx.push(['snow', 0, end]);
    if (t < 700) { this.dress(st, t, 0); return st; }
    if (t >= back + 700) { st.sprite = 'sHappy'; return st; }
    if (t < go) { st.pose = sp.ready; st.sprite = 'sHappy'; }
    else if (t < stop) {
      const u = t - go;
      st.pose = sp.act[0];
      if (o.kind === 'gym') { st.sprite = 'flag'; st.sp = ['home', 0, -6 * Math.abs(Math.sin(Math.PI * u / 400))]; st.fx.push(['sweat', go + 300, 800]); }
      if (o.kind === 'hike') { st.sprite = 'flag'; st.sp = lead; st.fx.push(['sweat', go + 800, 1600]); for (let k = 0; k * 340 < sp.act[1]; k++) st.fx.push(['dust', go + k * 340, null, 2]); }
      if (o.kind === 'bike') {
        st.y -= 2 * Math.abs(Math.sin(Math.PI * u / 220)); st.x += 14 * Math.sin(u / 900); st.sView = 'v2'; st.sp = lead;
        st.fx.push(['speed', go, sp.act[1] - 400]); for (let k = 0; k * 440 < sp.act[1]; k++) st.fx.push(['dust', go + k * 440, [0.78, 0.97], 2]);
      }
      if (o.kind === 'ski') { st.x += 10 * Math.sin(u / 700); st.sView = 'v2'; st.sp = lead; st.fx.push(['speed', go, sp.act[1] - 200], ['spray', go, sp.act[1]]); }
    } else if (!o.mishap) { st.pose = at(t - stop, sp.calm); st.sprite = 'sHappy'; }
    else {
      const u = t - stop, hit = stop + (o.kind === 'gym' ? 60 : 150);
      st.pose = at(u, sp.fall);
      if (u < 450) { st.sprite = 'surprised'; st.sp = o.kind === 'gym' ? null : lead; }
      else if (o.kind === 'gym') { st.sprite = u < 2800 ? 'pat' : 'sHappy'; st.sp = u < 2800 ? ['rel', 0.3, 0.8] : null; }
      else if (sp.lines[1] && u >= sp.lines[1][0]) { st.sprite = sp.plaster ? 'nod' : 'sHappy'; st.sp = ['rel', -0.06, 0.5]; }
      else { st.sprite = 'pat'; st.sp = 'head'; }
      if (o.kind === 'gym') st.fx.push(['impact', hit, [0.62, 0.95]], ['tears', stop + 200, 2600]);
      else if (o.kind === 'ski') st.fx.push(['snowBurst', hit], ['impact', hit, [0.45, 0.85]], ['dizzy', stop + 1100, 1000], ['snowShake', stop + 2200, 1000]);
      else st.fx.push(['impact', hit, sp.hitAt || [0.45, 0.9]], ['dust', hit, null, 6], ['dizzy', stop + 250, 1200, o.kind === 'bike' ? [0.25, 0.1] : null]);
      if (sp.plaster) st.fx.push(['spark', stop + sp.plaster + 1700, ...sp.plasterAt, 6]);
      st.shake = [[hit, o.kind === 'gym' ? 2 : 3, 240]];
    }
    if (t >= back) this.dress(st, t, back);
    if (o.mishap && t >= stop) for (const [ms, key] of sp.lines) say(stop + ms, key);
    else say(1300, o.kind);
    return st;
  }

  // The governor (SS, README 3): the page edges glow gold, a sea of gold clouds rolls in and the flagship sails up to us out
  // of the distance while the sprite dresses her; it flies up to plant its flag on the mainmast; a stair of light takes her
  // up onto the bow, where she points ahead: the emblem rises in front of the sails, three fireworks go up, gold rains over
  // the page; she waves, comes back down the stair, and the ship sails off into the distance leaving a gold wake.
  governor(t, st, say) {
    const come = ease(k01(t, 1500, 4000)), go = ease(k01(t, 12000, 14000)), far = { x: 380, y: 170 }, gone = { x: 60, y: 90 };
    const deck = t < 12000 ? { x: far.x + (SHIP_DECK.x - far.x) * come, y: far.y + (SHIP_DECK.y - far.y) * come + 3 * Math.sin(t / 500) }
      : { x: SHIP_DECK.x + (gone.x - SHIP_DECK.x) * go, y: SHIP_DECK.y + (gone.y - SHIP_DECK.y) * go };
    const s = (0.3 + 0.7 * come) * (1 - 0.8 * go);
    st.ship = { x: deck.x, y: deck.y, s, a: k01(t, 1500, 1900) * (1 - k01(t, 13300, 14000)) };
    const mast = this.shipPoint(st.ship, this.art.points.flagship.mast);
    // up the stair in three hops, and down again
    const climb = (a, b, from, to) => { const w = k01(t, a, b), hopN = Math.floor(w * 3), u = w * 3 - hopN; return { x: from.x + (to.x - from.x) * w, y: from.y + (to.y - from.y) * w - 40 * Math.sin(Math.PI * Math.min(1, u)) }; };
    if (t >= 5500 && t < 6500) Object.assign(st, climb(5500, 6500, FOOT, deck));
    else if (t >= 6500 && t < 11400) { st.x = deck.x; st.y = deck.y; }
    else if (t >= 11400 && t < 12000) Object.assign(st, climb(11400, 12000, deck, FOOT));
    // the sprite dresses her while the ship comes in, then flies ahead up to the mainmast with its flag before she
    // climbs aboard, and flies back down to her when she jumps off
    if (t < 2900) { st.pose = 'surprised'; st.sprite = 'surprised'; st.sp = ['rel', -0.1, 0.42]; }
    else if (t < 3900) { st.pose = 'surprised'; this.dress(st, t, 2900); }
    else if (t < 14000) {
      st.pose = t < 5500 ? 'govPoint' : t < 6500 || t >= 11400 && t < 12000 ? 'govJump' : t < 9600 ? 'govPoint' : 'govWave';
      st.sprite = 'flag';
      st.sp = t < 11400 ? ['mast', mast.x, mast.y] : ['rel', -0.1, 0.42];
    } else if (t < 14800) { st.pose = 'govWave'; this.dress(st, t, 14000); }
    else st.sprite = 'sHappy';
    if (t >= 6600 && t < 12000) st.emblem = { x: 355, y: 150 - 40 * ease(k01(t, 6600, 7400)), s: 0.8 * ease(k01(t, 6600, 7200)), a: k01(t, 6600, 6900) * (1 - k01(t, 11400, 12000)) };
    st.fx.push(['edgeGlow', 0, 15000], ['bang', 200], ['shock', 200], ['cloudSea', 1500, 13000, SHIP_DECK.y + 160], ['gulls', 2400], ['flash', 3900, 1],
      ['lightStairs', 5300, 1400, FOOT, deck], ['pillar', 6600, 3000], ['rays', 6600, 4800, 0.6, { x: 355, y: 110 }], ['burst', 6600, 300],
      ['skyFireworks', 6800, [[0, 70, 90, '#FFD45E'], [600, 240, 46, '#FF9AB5'], [1200, 420, 100, '#9CCFFF'], [6000, 380, 80, '#FFD45E'], [6500, 440, 170, '#FFE9A8']]],
      ['lightStairs', 11200, 1000, FOOT, deck], ['wake', 12000, 3000, [{ x: 440, y: 560 }, { x: 250, y: 330 }, { x: 70, y: 100 }]], ['flash', 14800, 0.8]);
    if (t >= 6800 && t < 12000) st.fx.push(['goldConfetti', 6800]);
    st.shake = [[4000, 3, 300], [6600, 4, 320]];
    say(1700);
    return st;
  }

  // The admiral (A, README 3): dressed by the sprite, she raises her spyglass to the left, where her ship appears far off;
  // the sprite flies out to meet it and rides in on its mainmast while it sails up to stop behind her; she lowers the glass
  // and salutes, and the ship answers: it dresses its flags from bow to stern and glows; then it sails off to the right,
  // the sprite flies back down to her and dresses her back.
  admiral(t, st, say) {
    // It is drawn side on, so it only ever moves along its heading: in from beyond the left edge, slowing to a stop behind
    // her (running on a little and settling back, its bow dipping), rocking on the waves, then gathering way out to the
    // right with its bow lifting. (It used to slide in down a slant while growing, which a ship seen side on cannot do.)
    const a = { ...ADM_SHIP, ...this.art.points.admship }, w = this.props.admship.width * H / this.art.anchor * a.s, from = -w / 2 - 30, to = PAGE.w + w / 2 + 30;
    const xAt = u => from + (a.x - from) * (1 - (1 - k01(u, 1100, 3400)) ** 3) + 10 * Math.sin(Math.PI * k01(u, 3400, 4300)) + (to - a.x) * k01(u, 6000, 7600) ** 2;
    const speed = Math.min(1, Math.abs(xAt(t + 20) - xAt(t - 20)) / 40 / 0.45);  // 1 at full way
    const ship = { x: xAt(t), y: a.y + 4 * Math.sin(t / 450), s: a.s, a: t >= 1100 && t < 7700 ? 1 : 0, speed,
      rot: 0.015 * Math.sin(t / 650) + 0.035 * Math.sin(Math.PI * k01(t, 3300, 4300)) - 0.03 * Math.sin(Math.PI * k01(t, 6000, 6800)),
      flags: ease(k01(t, 3600, 4300)), glow: bump(t, 3500, 5600) };
    st.admShip = ship;
    const mast = this.admPoint(ship, a.mast);
    if (t < 700) this.dress(st, t, 0);
    else if (t < 7200) {
      st.pose = t < 3500 ? 'spyglass' : 'admSalute';
      st.sprite = t >= 2300 && t < 6200 ? 'flag' : 'sailor';
      st.sp = t >= 1500 && t < 6200 ? ['at', mast.x, mast.y - 4] : ['rel', -0.1, 0.42];
    } else if (t < 7900) { st.pose = 'admSalute'; this.dress(st, t, 7200); }
    else { st.pose = 'idle'; st.sprite = 'sHappy'; }
    st.fx.push(['flash', 700, 1], ['seaBubbles', 700], ['gulls', 1600], ['flash', 7900, 0.8]);
    say(1000);
    return st;
  }

  // Where a point of the admiral's ship (prop px) is on the page, the ship's bottom centre at s.x, s.y, rocked by s.rot.
  admPoint(s, pt) {
    const im = this.props.admship, k = H / this.art.anchor * s.s, dx = (pt[0] - im.width / 2) * k, dy = -(im.height - pt[1]) * k, c = Math.cos(s.rot || 0), n = Math.sin(s.rot || 0);
    return { x: s.x + dx * c - dy * n, y: s.y + dx * n + dy * c };
  }

  // Where a point of the flagship's picture (prop px) is on the page, the ship's deck at sh.x, .y.
  shipPoint(sh, pt) { const k = H / this.art.anchor * sh.s, d = this.art.points.flagship.deck; return { x: sh.x + (pt[0] - d[0]) * k, y: sh.y + (pt[1] - d[1]) * k }; }

  // The sprite turns into the castle behind her; she twirls into the princess gown, curtsies, walks up to the gate and
  // waves from it, then runs back down the steps to us while the castle shrinks back into the sprite at its spot, and the
  // sprite circles her to dress her back. Her line at the gate is stateAt's.
  castleShow(t, st, say) {
    const b = this.castle.box, cs = 340 / b.h, home = this.home(0), [gx, gy] = this.art.points.castleClosed.gate;
    const rise = ease(k01(t, 900, 1600)), back = ease(k01(t, 9600, 10400)), top = 420 - 230 * rise;
    const cx = PAGE.w / 2 + (home.x - PAGE.w / 2) * back, cy = top + (home.y - top) * back;
    st.castle = { x: cx, y: cy, s: cs * (0.6 + 0.4 * rise) * (1 - 0.75 * back), a: k01(t, 900, 1500) * (1 - k01(t, 10000, 10400)),
      open: t < 9000 ? (t >= 7500 ? k01(t, 7500, 7800) : 0) : 1 - k01(t, 9000, 9300) };
    const gate = { x: PAGE.w / 2 + (gx - this.castle.closed.width / 2) * cs, y: top + (gy - this.castle.closed.height / 2) * cs };
    if (t < 1500) { st.sprite = 'glow'; st.sBehind = true; st.sp = ['to-castle', ease(k01(t, 0, 1200)), 1 + 1.6 * k01(t, 300, 1500)]; st.sAlpha = 1 - k01(t, 900, 1500); }
    else if (t < 9600) st.sp = ['gone'];
    else if (t < 10500) { st.sprite = 'sHappy'; st.sp = ['at', cx, cy]; st.sAlpha = k01(t, 10000, 10400); st.sScale = 1.6 - 0.6 * k01(t, 10000, 10500); }
    else if (t < 11200) this.dress(st, t, 10500);
    else st.sprite = 'sHappy';
    if (t < 2200) { st.pose = 'idle'; st.sx = t < 1500 ? 1 : Math.cos((t - 1500) / 90); }
    else if (t < 3000) st.pose = 'spin';
    else if (t < 4500) st.pose = 'curtsy';
    else if (t < 7500) { st.pose = 'walkA'; const w = k01(t, 4500, 7500); st.x = FOOT.x + (gate.x - FOOT.x) * w; st.y = FOOT.y + (gate.y - FOOT.y) * w; st.s = 1 - 0.68 * w; }
    else if (t < 9000) { st.pose = 'lookBack'; st.x = gate.x; st.y = gate.y; st.s = 0.32; }
    else if (t < 9800) {
      // three bounding steps down to us, growing as she comes closer
      const w = ease(k01(t, 9000, 9800));
      st.pose = 'pIdle'; st.x = gate.x + (FOOT.x - gate.x) * w; st.y = gate.y + (FOOT.y - gate.y) * w - 26 * Math.abs(Math.sin(Math.PI * 3 * w)); st.s = 0.32 + 0.68 * w;
    } else st.pose = t < 11200 ? 'pIdle' : 'idle';
    st.fx.push(['rainbowSparkles', 900], ['flash', 2200, 1.2], ['gateGlow', 7500, gate], ['flash', 11200, 1]);
    if (t >= 3000 && t < 4500) say(3000);
    if (t >= 7600 && t < 9000) st.bubble = { t: t - 7600, at: gate };
    return st;
  }

  // The big gift (A): a beam from the top of the page lands at her right; the box comes down it under a bunch of balloons,
  // lands, the balloons float away; the lid flies off, a beam and a fountain of stars and hearts go up to the top; she cheers,
  // and the box shrinks into a star that flies into her arms.
  bigGift(t, st, say) {
    const land = { x: FOOT.x + 150, y: FOOT.y }, fall = 1 - (1 - k01(t, 800, 2800)) ** 2;
    const y = -20 + (land.y + 20) * fall, x = land.x + 14 * Math.sin(t / 260) * (1 - fall);
    const into = ease(k01(t, 5300, 5900)), chest = { x: FOOT.x - 10, y: FOOT.y - 0.45 * H };
    st.gift = { x: x + (chest.x - x) * into, y: y + (chest.y - y) * into, s: 1 - into, open: t >= 3400, lid: t >= 3400 ? t - 3400 : null,
      balloons: t < 3600 ? { rise: Math.max(0, t - 2800), a: 1 - k01(t, 3200, 3600) } : null };
    st.pose = at(t, [[0, 'idle'], [2800, 'surprised'], [4400, 'cheer']]);
    const hop = (a, h) => { st.y -= h * bump(t, a, a + 320); }; hop(4400, 14);
    if (t < 4400) { st.sprite = t < 2800 ? 'sLook' : 'surprised'; st.sp = ['rel', -0.1, 0.42]; } else this.toss(st, t, 4400);
    say(2900);
    return st;
  }

  // She tosses it up over her head: it flies up, hangs there hopping for joy, and only then comes down to circle her. The
  // toss used to last 350 ms, about the flight up, so it started down the moment it got there (用户：一上头顶就下来了).
  // It hangs above her speech bubble, however many lines it has, not behind it.
  toss(st, t, a, hold = 1100) {
    st.sprite = 'sHappy';
    if (t < a + 350 + hold) st.sp = ['tossed', 0.05 * Math.abs(Math.sin(Math.max(0, t - a - 350) / 170))];
    else st.orbit = t - a - 350 - hold;
  }

  // The sprite dresses her up (or back) from a: it circles her glowing, leaving a trail, and the caller flashes as it is done.
  dress(st, t, a) {
    st.sprite = 'glow'; st.orbit = 1000 / 1.2 + (t - a) * 2.2; st.orbitR = 90;  // from her left, where it waits
    st.fx.push(['trail', a]);
  }

  // Low in front of her (README 1.1), on her left: a little outside her standing outline, so it overlaps her hair.
  home(now) { return { x: FOOT.x + this.herLeft - SPRITE_H * 0.22, y: FOOT.y - 0.3 * H + 4 * Math.sin(now / 700) }; }

  fill(text, name, opts = {}) {
    if (!text) return text;
    return text.replaceAll('{昵称}', opts.nickname ?? '').replaceAll('{礼物}', opts.gift || GIFT[name] || '小花花').replaceAll('{个数}', String(opts.count ?? 3))
      .replaceAll('{关键字}', opts.keyword ?? LOTTERY_TRIAL.keyword).replaceAll('{奖项}', opts.award ?? LOTTERY_TRIAL.award).replaceAll('{奖品}', opts.prize ?? LOTTERY_TRIAL.prize);
  }
}
