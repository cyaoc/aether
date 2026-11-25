// The 桌宠's engine, ported from the design package's board (docs/design/desktop-pet/boards/DP-DesktopPet.dc.html): which
// show plays when (tiers, the queue, a 一轮送礼 growing into bigger shows, blind boxes waiting for their result), her 心情
// and 日常小动作, the 宝箱小游戏, and what she and her sprite are doing at any moment. It draws nothing (Pet in pet.mjs does)
// and reads no picture, so node can run it; where the 宝箱小游戏 places things it has from Pet's measures of the pictures.
import { k, smooth as ease } from '../video/animations/toolkit.mjs';
import { Changes } from '../gift-board/changes.mjs';
import { viewerName } from '../lottery/viewer.mjs';
import { PK_INTEL } from './pk-intel.mjs';
import { SIGN } from './sign.mjs';

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
  pranksterKnocked: { fx: [['spark', 450, 0.5, 0.3, 8], ['hearts', 600, 3]] },
  chestKept: { fx: [['confetti', 300], ['halo', 300, 1800, 0.4], ['hearts', 900, 4]] },
  chestRobbed: { fx: [['gloom', 400]] },
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
  // (glow: the colour of a show's effects where it is not its tier's: the two moved tier in #401 and kept their look)
  entry: { dur: 1600, glow: 'C' }, gift: { dur: 2000 }, giftStreak: { dur: 2600 },
  blindBoxSmall: { dur: 2600 }, midGift: { dur: 3500 }, blindBoxMedium: { dur: 4000 },
  captain: { dur: 4500 }, bigGift: { dur: 6000 }, blindBoxLarge: { dur: 6000 },
  admiral: { dur: 8000 }, governor: { dur: 15000 }, castle: { dur: 11500 }, newFirst: { dur: 2600 }, lottery: { steps: LOTTERY },
  // the chest game's: the 捣蛋鬼 knocked away, beaten off (zapDur: the longer show after the lightning), gone with the chest
  pranksterKnocked: { dur: 4300 }, chestKept: { dur: 4800, zapDur: 7200 }, chestRobbed: { dur: 5600 },
  blindBoxQuery: { dur: 6000, glow: 'D' },
  // B站 PK (design package README 15): she holds up her sign when it is time to act (PK_SIGN)
  pkCondition: { dur: 7600 }, pkDouble: { dur: 6900 }, pkDoubleEnding: { dur: 10900 }, pkLastSeconds: { dur: 10900 },
  // the other side's card (PK_INTEL, pk-intel.mjs), the 10 s between the match and the start
  pkOpponent: { dur: 10000 },
  // the PK's result by its score (PK_RESULT.of), as long as each ending plays (durs)
  pkResult: { dur: 5000, durs: { BigWin: 5000, Win: 4000, NarrowWin: 4400, Draw: 3600, NarrowLoss: 4400, Lose: 4400, BigLoss: 5000 } },
  // a change in how the PK stands, while it is on (PK_GAP); C: never over a sign or a thank-you, 20 s apart at least
  pkSwing: { dur: 2800, durs: { Overtake: 2800, Overtaken: 2800, PullAhead: 2600, FallBehind: 2600, CloseIn: 2400, CaughtUp: 2400 } },
  // our side's double bonus challenge done or run out, from B站's capsule; B, as her viewers made it
  pkChallenge: { dur: 3400, durs: { Done: 3200, Failed: 3400 } }
};
// Her sign shows (ms). The page moves the sign itself wherever it goes far: it flies out of her sprite to over her head (fly)
// as she raises her empty hands to take it (raise, the last part of fly), and when she lowers them it flies back into her
// sprite (back, drawn by Pet for whatever made her let go). The double bonus's condition opens with her hiding behind it
// (hide: it flies into her hands in front of her and she ducks behind it, peeks over it, a line; lift: she brings it down to
// her chest and it glides up in front of her as she raises her arms). 2倍 jumps twice as she bobs (jump: from, ms, px up; in
// the air her jumping drawing, lifted so her hands and the sign go on where they were).
// A countdown counts to the moment Core told (until): one that waited behind another show counts what is left, and one with
// under least left is not held up at all; it ends down after it reaches 0 (down).
export const PK_SIGN = { fly: 450, raise: 260, back: 420, hide: [450, 1500], lift: 180, least: 3000, down: 900, lower: { pkCondition: 6900, pkDouble: 6200, count: 10000 },
  jump: [[800, 420, 30], [1500, 420, 30]], pose: { pkCondition: 'signUp', pkDouble: 'signCheer', count: 'signUrge' } };
// The countdowns by what they count to, as Core tells them (pkCountdown): the double bonus's end, or the PK's.
const COUNTDOWNS = { double: 'pkDoubleEnding', last: 'pkLastSeconds' };
// B站 PK's 对手情报卡 (ms): her sprite prints it over her head as it does the 账单卡, turns it over (only when it has both
// sides), and it rolls itself up as the PK starts. The front, who the other streamer is, shows 4.5 s, its back, three
// rows of the same kind to glance at, 2.9 s (user: “一般对手信息可以显示时间长点、前3显示的短点”; it was 3.5 and 3.9); all
// of it before the start, when the gifts come thickest (首次送礼翻倍) and a show still on would drop their thank-yous.
// She is startled at the match, then nervous over the front, and clenches her fist for her viewers over the back.
export const INTEL = { print: [800, 1500], turn: [6000, 6400], roll: [9300, 9800], done: 9700 };
// B站 PK's result from PK_BATTLE_END's votes, ours and theirs: the same is a Draw (none on either side too); winning, they
// had half ours or less BigWin, within a tenth of the larger NarrowWin, else Win; losing the same the other way: BigLoss,
// NarrowLoss, Lose. (The first list had no plain Lose: a 5000 : 8000 loss would have said 就差一点点 or 被打得好惨.)
const PK_RESULT = {
  of: (ours, theirs) => {
    if (ours === theirs) return 'Draw';
    const [hi, lo] = ours > theirs ? [ours, theirs] : [theirs, ours], close = hi - lo <= hi * 0.1, rout = lo * 2 <= hi;
    return ours > theirs ? (rout ? 'BigWin' : close ? 'NarrowWin' : 'Win') : (rout ? 'BigLoss' : close ? 'NarrowLoss' : 'Lose');
  }
};
// While a PK is on, how it stands (from each score B站 sends) and the 中途比分 a change calls for: who leads (the last
// side ahead: a tie between does not end a lead), far (one side twice the other or more) and close (within a tenth of
// the larger), the same yardstick as PK_RESULT. Nothing before either side has MIN votes (10 : 0 is no lead worth a
// show); a change within COOL of the last show is let go, its standing still kept (README 15).
const PK_GAP = {
  MIN: 100, COOL: 20000,
  stand(ours, theirs, was = {}) {
    // under MIN nothing is far or close yet: 50 : 0 counted as far, and 300 : 0 then had no PullAhead
    const hi = Math.max(ours, theirs), lo = Math.min(ours, theirs), lead = ours > theirs ? 1 : ours < theirs ? -1 : was.lead || 0, on = hi >= PK_GAP.MIN;
    return { ours, theirs, lead, far: on && lo * 2 <= hi, close: on && hi - lo <= hi * 0.1 };
  },
  show(a, b) {
    if (Math.max(b.ours, b.theirs) < PK_GAP.MIN) return null;
    if (a.lead && b.lead && a.lead !== b.lead) return b.lead > 0 ? 'Overtake' : 'Overtaken';
    if (b.far && !a.far) return b.lead > 0 ? 'PullAhead' : 'FallBehind';
    if (b.close && !a.close && a.lead) return a.lead > 0 ? 'CaughtUp' : 'CloseIn';
    return null;
  }
};
// Shows that give way: neither plays while the 捣蛋鬼 is here, which they do not scare off (fire), nor over blind boxes she
// shakes, as the 盲盒·小 they open into would be dropped behind it; otherwise each goes by its tier.
const YIELDING = ['entry', 'blindBoxQuery'];
// 盲盒查询 (queryShow): the show's steps (ms); her pose and the sprite's once it is home, by the result; and what a 试演 of each
// result says, the board's buttons' (今日).
const QUERY = { print: [800, 1500], stamp: 1900, react: 2100, down: 2200, side: 3000, roll: [5200, 5700], done: 5600 };
const BILL_POSE = { Gain: 'cheer', Even: 'nod', Loss: 'pout', None: 'point' }, BILL_SPRITE = { Gain: 'sHappy', Even: 'nod', Loss: 'hide', None: 'flag' };
const QUERY_TRIAL = { Gain: { period: '今日', spend: '15', profit: '4.5' }, Even: { period: '今日', spend: '15', profit: '0' },
  Loss: { period: '今日', spend: '45', profit: '12.5' }, None: { period: '今日' } };
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
// What a round's gifts (GiftItem, none blind) come to: their 投入 and how many, and the one priced highest by its price (the
// first of those priced alike) and how many of it by its gift id, the rule of Core's 一轮送礼 (GiftRoundFeature).
function giftsOf(gifts) {
  const many = list => list.reduce((n, g) => n + g.count, 0), top = gifts.reduce((a, g) => g.price > a.price ? g : a);
  return { spend: gifts.reduce((n, g) => n + g.spend, 0), count: many(gifts), top: top.name, topCount: many(gifts.filter(g => g.giftId === top.giftId)) };
}
const BLIND_BOXES = ['blindBoxSmall', 'blindBoxMedium', 'blindBoxLarge'];
// The 演出表's groups the engine plays by (shows.json): a 心情 holds her rather than plays, a blind box says its ending's lines.
export const MOOD = 'mood';
const BLIND_BOX = 'blindBox';
// The groups whose shows thank for a gift, a 大航海 too (the board's EVENTS' gift): such a show cheers her up over a stolen
// chest, and scares the 捣蛋鬼 off with a ring of light.
const GIFTS = ['gift', BLIND_BOX, 'castle', 'guard'];
// Which lines a blind box's ending (a 一轮盲盒's outcome as Core tells it, or Jackpot) says.
const BOX_LINES = { Gain: 'blindBoxGain', Even: 'blindBoxEven', Loss: 'blindBoxLoss', Jackpot: 'blindBoxJackpot' };
export const PAGE = { w: 480, h: 640 }, H = 250, FOOT = { x: 240, y: 626 };
export const SPRITE_H = 76;
// How high she rises in each hop up or down the 总督's stair of light (README 3, 7).
export const HOP = 40;
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
// The 扭蛋 daily (#403): she points home, the sprite fetches a battery from her chest there, spins the cabinet up at her
// right; she hops over, presses the battery into its bay, turns the knob, squats to take the capsule out, hops back,
// shakes it by her ear and twists it open: the plush pops out (she hugs it, the sprite takes it home) or it is empty (she
// wails, the sprite pats her head, she wipes her tears). The cabinet (her prop gacha, taller than her) stands with its
// bottom middle at x on her ground line; its parts, and its edge in the cells she was drawn in front of it, are measured
// on her pictures (her desktopPet's points.gacha).
export const GACHA = { x: 392, capR: 13,
  // the show's steps (ms); won: the plush's ending, lost: the empty one's
  t: { fetch: 900, back: 1700, give: 2300, spin: 2700, up: 3300, hop: [3600, 4200], press: 4200, drop: 4700, turn: 4900,
    out: 7000, take: 7300, stand: 7900, home: [8300, 8900], shake: 8900, twist: 9900, open: 10400,
    // won: the plush rises out of the capsule over her head (rise), hangs there (hang), comes down into her arms (down),
    // and she hugs it (hug, when her in-between with its 820 + 120 ms frames is done: slow)
    won: { rise: 450, hang: 680, down: 820, hug: 11340, spin: 12800, gone: [13300, 13600], take: 13800, away: 14000, end: 14800 },
    lost: { empty: 10400, wail: 11300, pat: 11600, wipe: 13400, spin: 13500, gone: [14100, 14400], idle: 14600, end: 15200 } } };
DAILY.gacha = [GACHA.t.won.end, GACHA.t.lost.end - GACHA.t.won.end];

// ---- The 宝箱小游戏 (#372, #382; design package README 12). Nothing of it is on the page until the 捣蛋鬼 comes, and nothing of
// it stays: her shows are all as they were, and her chest is at home, off the left of the page, where the game only counts
// what the stream's gifts put in it. It comes by with the chest it has stolen, hanging from its hook rope, over her head and
// down on her right, making for the right edge. An empty chest it opens, mocks and throws back; one with gifts in it she takes
// by the handle and pulls for home, and the room has a minute to pull the tug bar's line her way with 人气票, or its way with
// 小花花 (danmaku only pick a side and throw paper balls). The tricks played on her are the game's alone: a 小花花 on her head,
// the fifth flattening her, the lightning frizzing her hair, and being dragged along in the last stretch. Its drawings and the
// chest's are the page's own (prankster/prankster.json).
// 电池 to the 金瓜子 Core tells amounts in (Battery.cs): the game counts in 电池, as its board says them.
const COINS = 100;
// Its idle drawing's height on the page, a little under half of hers; the ground line the chest stands on; where the chest
// lands, just clear of her right side; where the chest stands in the tug (she stands where the ring in her fists meets the
// chest, the 捣蛋鬼 where its rope runs straight from the other ring into its fists) and how far the three drift with
// the line; how the chest hangs from the hook in one of its rings, that end up; how far below the end of the rope in a
// carrying drawing the chest's link is (a bit of rope, the hook, the ring).
export const PK_H = 112, CHEST = { y: 626 }, HANG = -0.32;
const LAND = 372, TUG = { chest: 292, drift: 5 }, LINK = 18;
// The hook is HOOK long. The 捣蛋鬼 leans back as it pulls (LEAN) and as it hauls the chest off (HAUL), so that its rope
// runs straight from the ring into its fists; TRIM px of the rope its pulling drawings hold are left undrawn.
export const HOOK = 6;
const LEAN = -0.4, HAUL = -0.5, TRIM = 40;
// When she has lost the chest her line goes at ROB.quiet, and she curls up sulking at ROB.curl (chestRobbed).
const ROB = { quiet: 4600, curl: 5000 };
// How long the game's end leaves her in a 心情: robbed, sad until a gift (at most this long); kept, glad.
const MOOD_HOLD = { robbed: 60000, kept: 60000 };
// How long after it is beaten off (won), knocked off an empty chest (knocked) or scared off once it runs (fled) it is off the
// page and the 来访 is over: it goes slowly enough to be seen.
const OFF = { won: 3400, knocked: 2600, fled: 2400 };
// How long the sprite pats her frizzed hair before it is smooth (two pats); the stars gather round her head for GATHER of it
// and fall as glitter for SETTLE after.
const PATS = 900, GATHER = 500;
export const SETTLE = 1000;
// A gift falls DROP ms before it lands; a disguise holds FREEZE ms; the lightning set piece lasts ZAP ms (the countdown
// waits), its bolt hitting her BOLT ms in; it and the chest fall FALL ms when it can hold the chest up no longer; the sprite
// flies the chest home in HOME ms, or in TIDY before a show that scared the 捣蛋鬼 off. Of that time the sprite flies to the
// chest for GO ms, presses an open lid shut for SHUT ms and takes up the dropped rope for TAKE ms; the rest it lifts the chest
// and flies it off.
export const DROP = 350;
const FREEZE = 1800, ZAP = 2400, BOLT = 900, FALL = 480, HOME = 2600, TIDY = 2300, GO = 400, SHUT = 350, TAKE = 250;
// The show does not wait a set time but until the chest is home; after the lightning until the sprite has come back and
// patted her hair smooth (unfrizz). FRIZZ is the most it waits either way.
const FRIZZ = 9000;
// The sprite goes for the dropped rope only once the 捣蛋鬼 is this far right of where the rope's end is (or gone).
const CLEAR = 150;
// Scared off, it lets go of everything with a start: the sprite, by her, starts at the chest that falls for NOTICE ('!').
export const NOTICE = 500;
// peek (looking in and back out), carry (flying over with the chest), loiter (on an empty chest), fight and sprint (the
// fight's last stretch): how long each lasts; urge: from this much time left the countdown grows anxious; grace: at 0:00 the
// fight waits this long for 票 sent before 0 that are late on the way; flash: the winning half of the bar flashes this long;
// buzzer: a 绝杀 is written up this long; about: someone is about who wrote or came in this recently, for it to come on its
// own; hourly: how many times an hour at most it comes on its own (叫它来 brings it whenever).
export const GAME = { peek: 2400, carry: 9000, loiter: 45000, fight: 60000, sprint: 10000, urge: 30000, grace: 10000, buzzer: 1700, flash: 600, about: 5 * 60000, hourly: 2 };
// The phases in which it is already on its way off the page: a gift then is a gift to her again.
export const DONE = new Set(['knocked', 'toss', 'leave', 'won', 'lost', 'fled', 'away', 'snatch']);
// Off with the chest in the air (away): she starts for AWAY.spot ms, then reaches up after it.
const AWAY = { spot: 500 };
// A gift that scares it off: light in its tier's colour swells in her for BURST.glow, and a ring of it goes out from her, out
// to BURST.r px in BURST.ms, slowing as it goes (giftBurst).
export const BURST = { glow: 260, ms: 1500, r: 540 };
// Off with an empty chest for a 小花花 (snatch): it laughs its thanks on the lid till SNATCH.laugh, hops off over the chest's
// ring by SNATCH.hop after, and rises SNATCH.rise px with the chest on its rope by SNATCH.up; then it is away, slowly up and
// right (SNATCH.fly px/ms), out of the page as she drops to her knees.
const SNATCH = { laugh: 1200, hop: 350, up: 1900, rise: 110, fly: [0.07, -0.17] };
// It lies DAZED ms on the chest's lid where it fell; in the tug it is UP and pulling that long after the chest lands.
const DAZED = 1300, UP = 2200;
// The tug's two sides, by the gift's name (B站 has several ids of each): 人气票 pull for her (1), 小花花 for the 捣蛋鬼 (-1), one
// 票 a gift. The line starts in the middle of the bar, span 票 from either end; the 票 count the moment they reach the page,
// and enough of one side's to take the line to its end ends the fight there and then (秒杀). At 0:00 the side the line is on
// wins, a tie the 捣蛋鬼's; one decided by a gift that came in the last GAME.sprint or the grace is a 绝杀.
const SIDE_GIFT = { 人气票: 1, 小花花: -1 };
// A danmaku of one on a side throws a paper ball that flies BALL ms; a lucky one opens whoever it hits for LUCK.ms, the other
// side's gift counting twice, not again until LUCK.gap after. One on no side is called out by name, no sooner than TAUNT
// after the last; RETORT.hits balls of one side's in RETORT.ms have the one hit answer back, RETORT.gap apart at least.
export const LUCK = { ms: 20000, gap: 5000 }, BALL = 650;
const TAUNT = 8000, RETORT = { hits: 5, ms: 5000, gap: 10000 };
// The tug bar along the top of the page.
export const BAR = { x: 20, y: 96, w: 440, h: 20 };

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
 * pictures' sizes, `props` and `castle`, are Pet's to measure, and so are the 对手情报卡's words (`measureG`, a canvas's 2D
 * context). A 试演 that is over raises `trialended` with its number (trialOver). */
export class Engine extends EventTarget {
  constructor(master, shows, art) {
    super();
    Object.assign(this, { master, art });
    this.events = Object.fromEntries(shows.map(row => [row.id, { ...EVENTS[row.id], tier: row.tier, group: row.group, gift: GIFTS.includes(row.group) }]));
  }

  now() { return this.master.currentTime; }

  reset(rng) {
    this.cur = this.seen = this.trial = this.lot = this.reveal = this.moodHold = null; this.queue = []; this.rounds = []; this.crowns = new Map(); this.lottery = undefined;
    this.changes = new Changes();
    this.lastGift = this.lastAny = this.now(); this.recent = []; this.nextDaily = this.now() + 6000; this.lastLine = {}; this.said = null; this.rng = rng;
    this.gm = this.newGame(this.now()); this.gl = null; this.unopened = new Map();
  }

  // The viewers' 一轮送礼 and what goes with them, gone with the room or her switched off: no 盲盒播报 is to come for them.
  forgetRounds() { this.rounds = []; this.crowns.clear(); this.unopened.clear(); }

  // The 桌宠页's state: whether she is on, how the streamer set her to play and what she says, the lottery on air, the 礼物榜
  // while its 期 runs and whether the streamer is in a PK. Her quiet-room clocks start when the room connects, and so do the
  // 捣蛋鬼's own 来访; while no room is she stays calm, and a round she was shaking boxes for will not end. The chest game's
  // go with the room: one here runs off as at a PK (callOff), what was thrown at it counts for nothing, and her chest, 连守 and
  // what it remembers start from nothing.
  receive(state) {
    const now = this.now();
    Object.assign(this, { on: state.enabled, settings: state.settings, lines: state.lines });
    if (state.roomConnected && !this.room) { this.lastGift = this.lastAny = now; this.recent = []; this.gm.last = now; }
    if (!state.roomConnected) {
      this.forgetRounds(); this.callOff(now);
      if (this.gm.visit) Object.assign(this.gm.visit, { queue: [], hits: [] });
      // a gift whose light is on its way plays now, and what waited behind it, as after a PK; their 电池 go with the room's
      if (this.gm.burst?.due != null) this.burstIn();
      Object.assign(this.gm, { chest: 0, pile: 0, streak: 0, steals: 0, brag: false, grudge: null, aside: 0, asidePile: 0, top: null });
    }
    this.room = state.roomConnected;
    // switched off she is told nothing more, and back on she starts from what she is told then; Core forgets the 捣蛋鬼 too
    if (!state.enabled) {
      this.cur = this.seen = this.trial = this.moodHold = null; this.queue = []; this.forgetRounds();
      this.lottery = undefined; this.reveal = null; this.changes.reset(); this.gm = this.newGame(now); return;
    }
    if (!!state.pk !== this.gm.pk) this.pkOn(!!state.pk);
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
  // way than the gift that took its first place, and may come first: the crown then waits for that gift's round. One taken
  // while the 捣蛋鬼 is here waits for it to be gone and the chest home (gm.top, update): what is thrown at it plays no show.
  crown({ uid, nickname }) {
    if (this.throwing()) return void (this.gm.top = nickname);
    const r = this.rounds.find(round => round.uid === uid && !round.ended);
    if (r) r.top = nickname; else this.crowns.set(uid, nickname);
  }

  // The new first place is crowned now, unless the crowning is turned off.
  crowned(nickname) { if (!this.settings.off.includes('newFirst')) this.start('newFirst', { nickname, chained: true }); }

  fire(name, opts = {}) {
    const now = this.now(), ev = this.events[name], vis = this.gm.visit, live = this.throwing();
    // a gift while the 捣蛋鬼 is here (not on its way off): its light goes out from her first, and it is scared off, and the
    // show goes on, once the light reaches it (gameUpdate fires it again, burst); what comes meanwhile waits behind
    if (ev.gift && !opts.burst && !opts.game && live) {
      if (this.gm.burst?.due != null) return void this.gm.burst.more.push([name, opts]);
      return this.giftBurst(now, ev, name, opts);
    }
    // (the game's own shows, its endings, never: she has just been in it; nor anything while the 捣蛋鬼 is here: the room is
    // not quiet then, though no gift has come for long)
    if (!opts.chained && !opts.game) opts = { ...opts, wake: opts.wake ?? (!vis && this.drowsy(now)) };
    // Anyone coming in counts as something happening for 犯困; a show turned off plays nothing, not a lesser one instead.
    this.lastAny = now;
    // while the 捣蛋鬼 is here she waves at no one, and anything else scares it off, a show turned off too; the show then waits
    // while the sprite takes the chest home (scared, a gift's sender named; the game's own shows are opts.game)
    if (vis && !opts.game) {
      if (YIELDING.includes(name)) return;
      opts = { ...opts, ...this.scared(now, ev.gift && opts.nickname) };
    }
    // a 大航海's 电池 go into the chest like a gift's (after the scare: the chest it saved goes home with them)
    if (opts.spend) this.gameGift({ spend: opts.spend, count: 1 });
    if (this.settings.off.includes(name)) { if (opts.frizz) this.gm.fix = { start: now, pose: opts.frizz }; return; }
    if (YIELDING.includes(name) && this.shaking()) return;
    const curTier = this.cur ? TIER[this.events[this.cur.name].tier] : -1, tier = TIER[ev.tier];
    // A gift round that grows to 5 gifts takes over its own plain thank-you, instead of being dropped as busy.
    // The two showpieces never cut each other short: the governor waits for the castle and then goes first.
    const show = name === 'governor' && this.cur?.name === 'castle';
    const grown = name === 'giftStreak' && this.cur?.name === 'gift' && this.cur.opts.round === opts.round;
    // While the 宝箱小游戏 is wound up she is busy (wrapping): nothing cuts in, save the show that scared it off (live)
    const wait = !opts.game && !live && this.wrapping(now);
    if (!wait && (!this.cur || tier > curTier && !show || grown)) return this.start(name, opts);
    if (tier <= TIER.C) return;
    if (tier < TIER.S && this.queue.filter(q => this.events[q.name].tier === ev.tier).length >= 3) return;
    this.queue.push({ name, opts });
  }

  start(name, opts) {
    const now = this.now(), c = this.cur, gm = this.gm, { dailyMinSeconds, dailyMaxSeconds } = this.settings;
    if (opts.until != null && opts.until - now < PK_SIGN.least || opts.by <= now) return;
    this.trialOver(c);
    // a PK countdown cut off goes back to the head of the queue: once the bigger show is over it is held up again for what is
    // left, if PK_SIGN.least or more (the check above); every other show cut off is gone
    if (c?.opts.until != null && c.name !== name) this.queue.unshift({ name: c.name, opts: c.opts });
    // one that comes while the chest is still going home, or her hair is still to be patted smooth, waits for them, whether the
    // show it takes over had them still to do or none played (a PK scared the 捣蛋鬼 off); the game's own shows play the chest home
    // (only a 试演 still comes so, and has a line carried and a pose held below: what happened waits its turn, wrapping in fire)
    if (!opts.game) {
      opts = { ...opts, tidy: opts.tidy || (gm.out ? TIDY : 0), frizz: opts.frizz || this.frizzed(gm.fix, now) || this.frizzed(c, now) };
      gm.fix = null;
    }
    const ev = this.events[name], wake = !!opts.wake, dur = DAILY[name] ? DAILY[name][0] + (opts.mishap ? DAILY[name][1] : 0) : (opts.zap && ev.zapDur) || ev.durs?.[opts.result] || (ev.steps?.[opts.result] ?? ev).dur;
    // one that scared the 捣蛋鬼 off, or came after, waits, at most FRIZZ, for the chest to go home and her hair to be smooth
    // (stateAt), the chest maybe home already; meanwhile she says on the line she was saying in the game's show it takes over
    // (speaking, as stateAt saw it last), until that show would have stopped it: 被偷了 at ROB.quiet, the others at their end;
    // and she waits in the pose she had there (held), as one waiting before it had her
    const was = this.speaking?.c === c && this.speaking, waits = opts.tidy || opts.frizz;
    const carried = waits ? c?.carried ?? (was && { text: was.text, from: was.from, until: c.start + c.pre + (c.name === 'chestRobbed' ? ROB.quiet : c.dur) }) : null;
    const held = !waits || !c ? null : c.opts.game ? this.held(c) : now < c.start + c.pre ? c.held : null;
    // (robbed: 被偷了 waits while it makes off with the chest, the 来访's last moments; endVisit starts it)
    this.cur = { name, opts, start: now, pre: Math.max(wake ? 1000 : 0, waits || opts.robbed ? FRIZZ : 0), dur, line: this.pick(this.lineKey(name, opts)), wakeLine: wake ? this.pick('wake') : null, carried, held };
    // a round's blind boxes were shaken all the while they came in: the show opens the box right away instead of shaking it again
    if (opts.shaken && BOX_OPEN[name]) this.cur.start -= BOX_OPEN[name];
    // a late countdown is shorter, and a late 对手情报卡, rolled up as the PK starts: their moments are on the clock, so what
    // comes before the show (waking up, the chest going home) is taken off them too
    if (opts.until != null) this.cur.dur = Math.min(dur, opts.until - now - this.cur.pre + PK_SIGN.down);
    if (opts.by != null) this.cur.dur = Math.min(dur, opts.by - now - this.cur.pre);
    if (DAILY[name]) this.nextDaily = now + dur + dailyMinSeconds * 1000 + this.rng() * (dailyMaxSeconds - dailyMinSeconds) * 1000;
    // after the chest game: robbed, she is sad until a gift cheers her up (MOOD_HOLD.robbed at most, its own lines); kept, glad
    // for MOOD_HOLD.kept (and no 日常小动作 meanwhile: those come only when she is calm)
    if (name === 'chestRobbed') this.moodHold = { mood: 'gloomy', lines: 'chestRobbedGloomy', until: now + dur + MOOD_HOLD.robbed };
    // a gift cheers her up: one whose show plays, not one let go while she is busy
    else if (ev.gift && this.moodHold?.mood === 'gloomy') this.moodHold = null;
    if (name === 'chestKept') this.moodHold = { mood: 'happy', until: now + dur + MOOD_HOLD.kept };
  }

  // A blind box says the lines of its ending, a 盲盒查询 its result's, the lottery its step's, 守住了 by a 绝杀 that one's, a PK's
  // result and 中途比分 their ending's and a 双倍挑战 how it came out; every other show its own.
  lineKey(name, opts = {}) {
    const ev = this.events[name];
    return ev?.group === BLIND_BOX ? BOX_LINES[opts.result || 'Gain'] : name === 'blindBoxQuery' ? 'query' + (opts.result || 'Gain')
      : name === 'pkResult' || name === 'pkSwing' ? 'pk' + (opts.result || 'Win') : name === 'pkChallenge' ? 'pkChallenge' + (opts.result || 'Done')
      : ev?.steps ? ev.steps[opts.result].line : name === 'chestKept' && opts.buzzer ? 'chestKeptBuzzer' : name;
  }

  // ---- what Core tells it (DesktopPet.cs)
  // Whether someone coming in wakes her first (冷场后来了人): she is in a quiet room's 沮丧 or 犯困, not in a 心情 the game
  // left her in (moodHold): sad over a stolen chest a gift cheers her up at once, and glad she is awake.
  drowsy(now) { return !(this.moodHold && now < this.moodHold.until) && ['gloomy', 'sleepy'].includes(this.mood(now, false)); }

  // A gift, a blind box or a 大航海 counts for 开心 and wakes her from 沮丧; anything at all wakes her from 犯困.
  gifted(now) { this.lastGift = this.lastAny = now; this.recent = this.recent.filter(r => now - r < 60000).concat(now); }

  // A viewer's 一轮送礼, told after each gift message with the gifts in it (GiftItem), and once more as it ends
  // (GiftRoundProgress). Gifts do not wait for the round to end: she plays at once the show its gifts have come to so far, and
  // moves up when it grows into a bigger one (the fifth gift, the 中礼物 and 大礼物 thresholds); one already played is not
  // played again. Its blind boxes wait for their result (blindBox), and each gift message flies a little box into her arms.
  // Every gift goes into her chest at home. While the 捣蛋鬼 is here a 人气票 or 小花花 is thrown (arrive) and is no part of her
  // round, and what else comes plays nothing until the round comes to a 中礼物.
  gift(p) {
    const now = this.now();
    let r = this.rounds.find(round => round.uid === p.uid && !round.ended);
    if (p.ended) {
      if (r) { r.ended = true; if (r.told === r.boxes) this.rounds.splice(this.rounds.indexOf(r), 1); }
      return;
    }
    // any gift cuts a 盲盒查询 short, its card gone at once: blind boxes and a round's gift short of moving it up play no show to
    // take it over (a 试演's is over, as one cut short by a show)
    if (this.cur?.name === 'blindBoxQuery') { this.trialOver(this.cur); this.drop(); }
    // the first gift into a quiet room wakes her first (冷场后来了人), before the round's own show (not while the 捣蛋鬼 is
    // here: fire)
    if (!r) {
      const wake = !this.gm.visit && this.drowsy(now);
      this.rounds.push(r = { uid: p.uid, start: now, arrivals: [], gifts: [], boxes: 0, told: 0, held: false, wake, wakeLine: wake ? this.pick('wake') : null, played: -1, top: this.crowns.get(p.uid) });
      this.crowns.delete(p.uid);
    }
    this.gifted(now);
    const kept = p.items.map(item => this.arrive(item, r, { uid: p.uid, name: p.nickname, face: p.faceUrl }));
    // a new first place they took, none of them playing a show now, is crowned once the 捣蛋鬼 has gone (update)
    if (r.top && kept.every(Boolean)) { this.gm.top = r.top; r.top = null; }
    if (kept.every(k => k === 'thrown')) return;
    r.arrivals.push(now);
    // a castle opened from one of the round's blind boxes, the only way B站 gives one, plays instead of its gifts and blind
    // boxes, with the result that reports the box it came from (blindBox)
    if (p.items.some(item => item.castle)) r.played = CASTLE;
    if (!p.items.some((item, i) => !item.blind && !kept[i])) return;
    const { spend, count, top, topCount } = giftsOf(r.gifts), { midGift, bigGift } = this.settings;
    const kind = spend >= bigGift ? 'bigGift' : spend >= midGift ? 'midGift' : count >= 5 ? 'giftStreak' : 'gift';
    if (GIFT_SHOWS.indexOf(kind) <= r.played) return;
    this.fire(kind, { nickname: p.nickname, gift: top, count: kind === 'giftStreak' ? count : topCount, wake: r.played < 0 && r.wake, round: r });
    r.played = GIFT_SHOWS.indexOf(kind); r.wake = false;
  }

  // One gift of viewer w's message comes into its round r, as the game takes it (gameGift): hers (false), kept in the round
  // with no show while the 捣蛋鬼 is here ('quiet'), or thrown and no part of the round ('thrown'). A blind box waits in the
  // round for its 盲盒播报, held while the 捣蛋鬼 is here: she does not shake those (shaking).
  arrive(item, r, w) {
    if (item.blind) { this.unopen(w.uid, item, r); r.boxes += item.count; r.held = false; } else r.gifts.push(item);
    const kept = this.gameGift(item, r, w);
    if (kept === 'thrown') r.gifts.pop();
    return kept;
  }

  // A 一轮盲盒's result, as its 盲盒播报 reported it (BlindBoxResult): the size by what the boxes cost, the ending by what they
  // opened; a single gift opened at the 大礼物 threshold is a jackpot and at least the big one. The castles they opened play
  // the castle instead, one for each.
  blindBox(b) {
    const told = this.tell(b), { midGift, bigGift } = this.settings, r = told.find(t => t.top) ?? told[0];
    // Every castle the report says came out plays, boxes she never saw too (the page reloaded), each after the one before
    // (同档排队); a result before the castle of a round plays nothing, the castle standing in for it.
    for (let i = 0; i < b.castles; i++) this.fire('castle', { nickname: b.nickname, round: r });
    if (b.castles || told.some(t => t.played === CASTLE)) return;
    const jackpot = b.bestPrice >= bigGift, size = jackpot || b.tally.spend >= bigGift ? 2 : b.tally.spend >= midGift ? 1 : 0;
    // While the 捣蛋鬼 is here a 盲盒·小 only goes in her chest, as a gift short of a 中礼物 does (gameGift); a bigger one
    // plays, and scares it off, as castles do.
    if (!size && this.throwing()) { if (r?.top) { this.gm.top = r.top; r.top = null; } return; }
    this.fire(BLIND_BOXES[size], { nickname: b.nickname, result: jackpot ? 'Jackpot' : b.tally.outcome, gift: b.bestGift, count: b.boxes, shaken: !!r, round: r });
  }

  // The rounds a result tells: a 盲盒播报 reports every box sent before it went out, in the order they came (unopen), over
  // however many 一轮送礼, and none sent while it went out, which wait for their own. A round is done once it ended and all is
  // told.
  tell(b) {
    const told = [], boxes = this.unopened.get(b.uid) ?? [];
    let left = b.boxes;
    for (let s; left > 0 && (s = boxes[0]);) {
      const n = Math.min(left, s.n);
      left -= n; s.n -= n;
      if (!s.n) boxes.shift();
      s.r.told += n;
      if (!told.includes(s.r)) told.push(s.r);
      if (s.r.ended && s.r.told === s.r.boxes) this.rounds.splice(this.rounds.indexOf(s.r), 1);
    }
    return told;
  }

  // A viewer's boxes not yet opened, in the order they came: a gift message's (GiftItem), n of them in their round r.
  unopen(uid, { count }, r) {
    if (!this.unopened.has(uid)) this.unopened.set(uid, []);
    this.unopened.get(uid).push({ r, n: count });
  }

  // She waves only at a viewer wearing this room's 粉丝团 (Core's wave), and as a D show only while she is idle; anyone else
  // coming in still counts for 犯困.
  entry(e) { if (e.wave) this.fire('entry', { nickname: e.nickname }); else this.lastAny = this.now(); }

  // A viewer's 盲盒查询 as its reply went out (BlindBoxQueryResult): its 账单卡 says what the reply said. Its 投入 is no gift, so
  // it is kept out of opts.spend, which goes in her chest (fire).
  blindBoxQuery({ nickname, result, period, spend, profit }) { this.fire('blindBoxQuery', { nickname, result, bill: { period, spend, profit } }); }

  // B站 PK's moments to act on, our side's alone (DesktopPet.cs): the double bonus's condition in B站's words, the double on,
  // and a countdown to the double's end or the PK's, which she counts to the moment Core told, leftMs from now on her clock
  // (PK_SIGN), however late she gets to it.
  pkCondition({ text }) { this.fire('pkCondition', { text }); }

  pkDouble() { this.fire('pkDouble'); }

  pkCountdown({ kind, leftMs }) { if (COUNTDOWNS[kind]) this.fire(COUNTDOWNS[kind], { until: this.now() + leftMs }); }

  // B站 PK matched: the 对手情报卡 of what B站 answered about the other streamer (OpponentCard.cs), none with nothing on either
  // side. It is rolled up as the PK starts, leftMs from now (by): asking B站 takes up to 3 s and a show before it may take more,
  // so it may start that much late and play that much shorter.
  pkMatch({ intel, leftMs }) {
    const L = PK_INTEL.layout(this.measureG, intel);
    if (L.front || L.back) this.fire('pkOpponent', { intel, by: this.now() + leftMs });
  }

  // Our side's 双倍挑战 done or run out (Done, Failed).
  pkChallenge({ result }) { if (EVENTS.pkChallenge.durs[result]) this.fire('pkChallenge', { result }); }

  // A PK's score: each change checked against how it stood (PK_GAP), the 中途比分 played if one is due and the last was COOL
  // ago on her clock; how it stands is kept either way.
  pkScore({ ours, theirs }) {
    const now = this.now(), a = this.pkGap || PK_GAP.stand(0, 0), b = PK_GAP.stand(ours, theirs, a), show = PK_GAP.show(a, b);
    this.pkGap = b;
    const cool = show && this.pkGapAt != null && now - this.pkGapAt < PK_GAP.COOL;
    // the 20 s count from a show that played, not one let go because she was busy
    if (show && !cool) { this.fire('pkSwing', { result: show }); if (this.cur?.name === 'pkSwing' && this.cur.start === now) this.pkGapAt = now; }
  }

  // The PK over (PK_BATTLE_END): its result by the score (PK_RESULT).
  pkEnd({ ours, theirs }) { this.fire('pkResult', { result: PK_RESULT.of(ours, theirs) }); }

  // a danmaku is not played (README 10), it tells her someone is there (犯困 needs an hour with nothing at all), and is its
  // viewer's in the 宝箱小游戏 (gameDanmaku): the 站队 it takes is Core's to tell (side), what it says never leaves Core
  danmaku(d) {
    this.lastAny = this.now();
    this.gameDanmaku({ uid: d.uid, name: d.nickname }, d.side);
  }

  // A 大航海 plays at once, in no 一轮送礼, and is a gift to her 心情; what it cost goes into her chest, the one it saved if it
  // scared the 捣蛋鬼 off (fire).
  guard(g) { this.fire(GUARDS[g.guardLevel - 1], { nickname: g.nickname, spend: g.spend }); this.gifted(this.now()); }

  // A 试演 plays the show from its start (a blind box from its shaking), or holds her in the 心情, until it is stopped. It
  // scares the 捣蛋鬼 off as what happened would.
  testShow({ show, variant, nickname, id }) {
    const scare = this.gm.visit ? this.scared(this.now()) : {};
    this.trial = null;
    if (this.events[show].group === MOOD) {
      this.drop();
      if (scare.frizz) this.gm.fix = { start: this.now(), pose: scare.frizz };
      this.trial = show; return;
    }
    this.start(show, { nickname, result: variant, mishap: variant === 'Mishap', trial: true, trialId: id, ...scare });
  }

  // Only the 试演: a show of what happened since it plays on.
  stopTestShow() {
    this.trial = null;
    if (this.cur?.opts.trial) this.drop();
  }

  // A show dropped with her hair still frizzed leaves the sprite to pat it smooth as with no show after (gm.fix): one still
  // waiting for it on its clock, so the pats go on where they were.
  drop() {
    const c = this.cur, now = this.now(), pose = this.frizzed(c, now);
    if (pose) this.gm.fix = { start: now < c.start + c.pre ? c.start : now, pose };
    this.cur = null;
  }

  // Whether her hair is frizzed at now in show c, or with no show (gm.fix), and still to be patted smooth, and then the pose she
  // waits for it in (frizz: unfrizz): c waits for that (pre), or it is the game's own after the lightning (zap), 守住了 cheering
  // on and 被偷了 kneeling as she was. Once the sprite has patted it smooth it is not, the glitter still settling (SETTLE) or not.
  frizzed(c, now) {
    if (!c) return null;
    if (!c.opts) return this.smoothed(-c.start, now - c.start) ? null : c.pose;
    if (now < c.start + c.pre) return c.opts.frizz && !this.smoothed(-c.start, now - c.start) ? c.opts.frizz : null;
    return c.opts.zap && !this.smoothed(Math.floor(c.start), now - c.start - c.pre) ? this.held(c) : null;
  }

  // Whether her sprite has patted her hair smooth by t into what smooth plays by seed.
  smoothed(seed, t) { const p = this.pat; return p?.seed === seed && t >= p.fix; }

  // The pose she is in through the game's show c, which one taking it over waits in: kneeling after 被偷了, else cheering on.
  held(c) { return c.name === 'chestRobbed' ? 'robbed' : 'cheer'; }

  // A 试演 of a show played out, or cut short by another show, is over, and the page tells Core by its number (page.mjs); one
  // stopped is Core's own doing.
  trialOver(show) { if (show?.opts.trial) this.dispatchEvent(new CustomEvent('trialended', { detail: show.opts.trialId })); }

  // The round whose blind boxes she shakes while no show plays: not over boxes held for the 捣蛋鬼 here (arrive).
  shaking() { return this.rounds.find(r => r.boxes > r.told && !(r.held && this.gm.visit)); }

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

  // A line said for a while, a 心情's, a jolt's or a 日常小动作's going wrong, is picked when she starts saying it (at) and kept
  // till she is done; at is rounded, as worked out from now it comes out a hair apart from frame to frame.
  saying(name, at) {
    const start = Math.round(at);
    if (this.said?.at !== start) this.said = { at: start, line: this.pick(name) };
    return this.said.line;
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
      if (top) this.crowned(top);
    }
    // what waited, the best first; while the 宝箱小游戏 is wound up it waits on (wrapping)
    if (!this.cur && this.queue.length && !this.wrapping(now)) {
      const best = Math.max(...this.queue.map(q => TIER[this.events[q.name].tier]));
      const i = this.queue.findIndex(q => TIER[this.events[q.name].tier] === best);
      const [q] = this.queue.splice(i, 1);
      this.start(q.name, { ...q.opts, chained: true });
    }
    this.gameUpdate(now);
    const gm = this.gm;
    // a first place taken while the 捣蛋鬼 was here, where what is thrown plays no show, waits for it to be gone and the chest
    // home
    if (gm.top && !this.cur && !gm.visit && !gm.out && !gm.fix && !this.shaking()) { this.crowned(gm.top); gm.top = null; }
    // not while she shakes a round's blind boxes either: that lasts until the 盲盒播报 goes out, which may take half a minute;
    // nor while she waits through a lottery, nor while the 捣蛋鬼 is here or the sprite still takes the chest home or pats her
    // hair smooth; nor in a PK: humming or skiing right after the score swung makes no sense
    const kinds = Object.keys(DAILY).filter(kind => !this.settings.off.includes(kind));
    if (!this.cur && !this.shaking() && !this.lot && !gm.visit && !gm.out && !gm.fix && !gm.pk && this.mood(now) === 'calm' && now >= this.nextDaily && kinds.length) {
      const kind = kinds[Math.floor(this.rng() * kinds.length)], mishap = this.rng() * 100 < this.settings.mishapPercent;
      this.start(kind, { mishap });
    }
  }

  // 开心 counts only gifts, 大航海 included: a room viewers keep coming into would otherwise never be calm, and the daily actions
  // only come when she is calm (README 6). A 心情 set to 0 minutes never comes. hold: a 心情 the chest game left her in
  // (moodHold) goes first; the quiet-room rules alone decide whether a gift wakes her (sad over the chest she has not dozed
  // off nor been left alone: a gift is thanked at once).
  mood(now, hold = true) {
    if (this.trial) return this.trial;
    if (!this.room) return 'calm';
    if (hold && this.moodHold && now < this.moodHold.until) return this.moodHold.mood;
    const recent = this.recent.filter(r => now - r < 60000).length, { boredMinutes, gloomyMinutes, sleepyMinutes } = this.settings;
    const quiet = (since, minutes) => minutes > 0 && now - since >= minutes * 60000;
    if (quiet(this.lastAny, sleepyMinutes)) return 'sleepy';
    if (quiet(this.lastGift, gloomyMinutes)) return 'gloomy';
    if (quiet(this.lastGift, boredMinutes)) return 'bored';
    return recent >= 3 ? 'happy' : 'calm';
  }

  // ---- the 宝箱小游戏 (#372) -----------------------------------------------------------------------------------------
  // The 捣蛋鬼 has stolen her chest: it flies in from the left with the chest hanging from its own hook rope, over her head,
  // and comes down with it on her right, on its way out to the right. An empty chest it opens, mocks, and throws back. One
  // with gifts in it springs open as it lands: she takes hold of the handle at her end and pulls for home, it pulls its rope
  // for the right edge, and the room has a minute to beat it off. There is one rope in all of it, the 捣蛋鬼's; the chest has
  // two plain handles. Its drawings, the chest's and their numbers are this.prankster (prankster/prankster.json), measured by
  // Pet (pkCell, chestBox), which also loads them: this.gameLoaded once they are.
  // gm: chest (the 电池 in her chest at home) and pile (the gifts drawn in it), drops and nums (what falls and the numbers
  // that rise, for drawGame), visit (the 捣蛋鬼 while it is here), due (it is to come once nothing plays: 'called' or on its
  // 'own'), came (when it came on its own this past hour), pk, last (when it last left), out (the sprite flying the chest
  // home), fix (her frizzed hair patted smooth with no show after), walk (her hopping to another place), streak (连守) and
  // steals, brag and grudge (what it opens with next time), say (her line of the game), herX (where she stands), aside and
  // asidePile (what came while it had the chest, for the chest once it has gone), top (a new first place to crown then);
  // and, once there is one, burst (a gift's light on its way to scare it off) and buzzer (the 绝杀 to write up).
  newGame(now) {
    return { chest: 0, pile: 0, drops: [], nums: [], visit: null, due: false, came: [], pk: false, last: now, out: null, fix: null, walk: null, streak: 0, steals: 0, brag: false, grudge: null, say: null, herX: FOOT.x,
      aside: 0, asidePile: 0, top: null };
  }

  // One gift x of viewer w's (a gift message's item, or a 大航海 as one) in the viewer's round r. Out of a 来访 it only goes
  // into the chest at home: her own show plays as ever (false). While the 捣蛋鬼 is here a 人气票 or 小花花 is thrown (queue),
  // one 票 each, twice while the other side is open (luck), and plays nothing ('thrown'). Any other gift plays nothing,
  // staying in the round ('quiet'), until the round, what it sent before the 来访 too, comes to a 中礼物: then its show plays
  // (false), which scares it off (fire); a blind box stays in its round for the 盲盒播报 (held, blindBox). Those it is not
  // fought over: they are kept aside for her chest at home, which it has on the page, and go in once it has gone, however it
  // went (endVisit). Once a gift's light is on its way the fight is decided: a 人气票 or 小花花 then is only a gift (giftBurst).
  gameGift(x, r, w) {
    const gm = this.gm, v = gm.visit, spend = x.spend / COINS, side = !x.blind && SIDE_GIFT[x.name], now = this.now();
    if (!this.throwing()) { gm.chest += spend; gm.pile += x.count; return false; }
    if (!side || gm.burst?.due != null) {
      gm.aside += spend; gm.asidePile += x.count;
      if (x.blind) { r.held = true; return false; }
      return side || giftsOf(r.gifts).spend >= this.settings.midGift ? false : 'quiet';
    }
    this.enlist(w, side, 'gift');
    const more = v.luck?.side === side && now < v.luck.until ? x.count : 0, q = { n: x.count + more, count: x.count, side, spend, w, double: more > 0, early: v.phase === 'peek' };
    v.queue.push(q);
    if (!q.early) this.reckon(q, now);
    return 'thrown';
  }

  // A 人气票 or 小花花 counts the moment it comes: on the bar (tally), or over an empty chest in the air it decides it there
  // and then (finish). One while it only peeks in counts once the chest hangs from its rope on its way in (early, gameUpdate).
  reckon(q, now) {
    const v = this.gm.visit;
    if (v.loot) this.tally(q.w, q.side, q.n, now, q.double);
    else if (['carry', 'fall'].includes(v.phase)) this.finish(now, q.side, { w: q.w });
  }

  // Whether the fight is on: the chest has something in it, and it is flying in with it or they are pulling (the bar is up).
  contested(v) { return v.loot && ['peek', 'carry', 'fall', 'tug'].includes(v.phase); }

  // The score as the 票 reach the page (red, blue), and the bar's line with it there and then: where it was stays white a
  // moment (ghost), the bar shakes, and +N and the name rise from where it is now. Then whose side it now favours (hers only
  // when she is ahead: a tie is its) and who turned it so, as it stood then (flip: who scored the 绝杀 if it stays so); then,
  // a side with enough on the way to take the line to its end wins at once.
  tally(w, side, n, now, double = false) {
    const v = this.gm.visit, before = v.red - v.blue;
    v[side > 0 ? 'red' : 'blue'] += n;
    const lead = v.red - v.blue, favours = lead > 0 ? 1 : -1;
    v.ghost = { lead: before, at: now, n };
    this.gm.nums.push({ at: now, text: `+${n} ${w.name}`, side, x: this.lineX(v), big: double || Math.abs(lead) >= v.span });
    if (favours !== v.favours) { v.favours = favours; v.flip[favours] = { w, late: v.judge != null || (v.left ?? GAME.fight) <= GAME.sprint }; }
    if (Math.abs(lead) >= v.span) this.finish(now, favours, { w, late: v.judge != null || (v.left ?? GAME.fight) <= GAME.sprint });
  }

  // The fight ends with side's 秒杀 (by: the gift that did it). In the tug what is on its way lands at once, the lightning cut
  // short; in the air she knocks it down (it lets go as if scared off, the chest falls and goes home) or it gets away with the
  // chest over the right of the page.
  finish(now, side, by) {
    const gm = this.gm, v = gm.visit;
    if (DONE.has(v.phase)) return;
    for (const q of [...v.hits, ...v.queue]) { v.spent += q.spend; v.count += q.count; }
    v.hits = []; v.queue = [];
    const buzzer = by.late ? by.w : null;
    if (v.phase !== 'tug') {
      v.pos = { x: this.gl?.pk?.x ?? v.pos.x, y: this.gl?.pk?.y ?? v.pos.y };
      return side > 0 ? this.airKnock(now, by.w) : this.airSteal(now, by.w);
    }
    v.net = side * v.span; v.cut = null;
    if (side > 0) this.win(now, by.w, buzzer); else this.lose(now, buzzer);
  }

  // Its lightning's bolt hits her (BOLT into it): its side gains a quarter of the way to an end at once, shown as a gift
  // reaching the page is (the line back her way, where it was white a moment, the jolt, +N in its colour), and the chest jerks
  // its way (tumble). Who turned it stays a viewer's: the lightning is no one's 绝杀.
  zapBack(now) {
    const v = this.gm.visit, n = Math.round(v.span / 4), before = v.red - v.blue;
    v.blue += n; v.net -= n; v.tumble = now;
    v.ghost = { lead: before, at: now, n };
    this.gm.nums.push({ at: now, text: `+${n} 捣蛋鬼`, side: -1, x: this.lineX(v) });
    const lead = v.red - v.blue, favours = lead > 0 ? 1 : -1;
    if (favours !== v.favours) { v.favours = favours; v.flip[favours] = null; }
    if (lead <= -v.span) this.finish(now, -1, { w: null });
  }

  // Knocked out of the air: it lets go and flees as a 大航海 scares it, the chest falls and the sprite takes it home, and she
  // cheers as when it is beaten off (守住了) the moment it is knocked down, not once the chest is home; her line still waits
  // for the chest to be up past her bubble (chestUp). (scare puts what was thrown in the chest and counts it kept.) An empty
  // chest knocked down is as one knocked off on the ground: 打跑了, and no bar.
  airKnock(now, w) {
    const gm = this.gm, v = gm.visit;
    this.scare(now); gm.grudge = w.name;
    if (!v.loot) return this.fire('pranksterKnocked', { game: true, nickname: w.name });
    Object.assign(v, { result: 1, resultAt: now });
    this.fire('chestKept', { game: true, streak: gm.streak, nickname: w.name });
  }

  // Off with an empty chest for a 小花花: it laughs its thanks, gets up with the chest on its rope and is away with it, and she
  // cries over it (被偷了, once it is off: she is sorry, then reaches after it). She has lost her chest, so 连守 goes; there
  // was nothing in it, so it has nothing to brag of next time.
  snatch(now, w) {
    const gm = this.gm;
    Object.assign(gm, { chest: 0, pile: 0, streak: 0, steals: gm.steals + 1 });
    this.phase('snatch', now); this.pkSay('pranksterFlower', now, w.name); gm.visit.snatched = true;
    this.fire('chestRobbed', { game: true, robbed: true });
  }

  // Off with it in the air: it flies on over the right of the page with the chest still hanging from its rope (away). (An
  // empty chest, given it by a 小花花 in the air, it thanks them for, as for one on the ground: snatch.)
  airSteal(now, w) {
    const gm = this.gm, v = gm.visit;
    Object.assign(gm, { chest: 0, pile: 0, streak: 0, steals: gm.steals + 1, brag: v.loot });
    if (v.loot) Object.assign(v, { result: -1, resultAt: now });
    this.phase('away', now); this.pkSay(v.loot ? 'pranksterRobs' : 'pranksterFlower', now, w.name);
    this.fire('chestRobbed', { game: true, robbed: true });
  }

  // Whoever sends a 人气票 or 小花花 (by: 'gift') or says 1 or 2 (by: 'dm') is on that side this 来访, the newest last; a
  // gift's side stands, so 1 or 2 after it does not move them (their face would hang on one side, their balls fly from the
  // other). Returns the side they are on.
  enlist(w, side, by) {
    const sides = this.gm.visit.sides, s = sides.get(w.uid);
    if (s?.by === 'gift' && by === 'dm') return s.side;
    sides.delete(w.uid); sides.set(w.uid, { ...w, side, by, at: this.now() });
    return side;
  }

  // Whether a gift now is thrown at the 捣蛋鬼: it is here and not yet on its way off the page.
  throwing() { return !!this.gm.visit && !DONE.has(this.gm.visit.phase); }

  // 叫它来 (Core's summon), or its own time come (gameUpdate): it comes once whatever she plays is over (a 日常小动作 is not cut
  // short).
  summon() { if (!this.gm.visit && !this.gm.pk) this.gm.due = 'called'; }

  // It is not to come after all, nor to stay (a PK, the room disconnected): on its way, it is reported gone; one here goes as if
  // scared off, with no show after: she lets go and is herself again; after the lightning she cheers frizzed until the sprite,
  // the chest home, has patted her hair smooth (gm.fix).
  callOff(now) {
    const gm = this.gm, v = gm.visit;
    if (gm.due) this.visited('end');
    gm.due = false;
    if (!this.throwing()) return;
    const frizz = v.phase === 'tug' && v.bolted;
    this.scare(now);
    if (frizz) gm.fix = { start: now, pose: 'cheer' };
  }

  // A PK is no time for it (callOff); a new one stands at 0 : 0, its first 中途比分 not held back by the last PK's (pkScore).
  pkOn(on) {
    this.gm.pk = on;
    if (on) { this.callOff(this.now()); this.pkGap = null; this.pkGapAt = null; }
  }

  // The 来访 starts, or it is over, as the page reports to Core (page.mjs).
  visited(what) { this.dispatchEvent(new CustomEvent('visit', { detail: what })); }

  startVisit(now) {
    const gm = this.gm;
    if (gm.due === 'own') gm.came.push(now);
    gm.due = false;
    // loot: there is something in the chest to fight over; span: how many 票 each end of the bar is from the middle
    gm.visit = { phase: 'peek', at: now, queue: [], hits: [], spent: 0, count: 0, freezes: 0, helped: 0,
      sides: new Map(), balls: [], struck: {}, retorted: {}, taunted: new Set(), taunt: -Infinity, lucked: -Infinity, luck: null,
      span: clamp(Math.round(gm.chest / 20), 300, 500), net: 0, red: 0, blue: 0, favours: -1, flip: {}, drag: 0, pos: { x: -70, y: 120 }, chest: null, loot: gm.chest > 0 };
    this.pkSay(gm.brag ? 'pranksterBrag' : gm.grudge ? 'pranksterGrudge' : 'pranksterPeek', now, gm.grudge);
    gm.brag = false; gm.grudge = null;   // (said once: it tells of its last 来访)
    this.visited('start');
  }

  // Its next phase; from and fromCx are where it and the chest (standing) were, for the phases that go on from there.
  phase(p, now) {
    const v = this.gm.visit;
    Object.assign(v, { phase: p, at: now, from: { ...v.pos }, fromCx: v.chest?.x ?? LAND });
    if (['knocked', 'fled'].includes(p)) v.say = null;
  }

  // Where the tug's line is across the bar, by the score: in the middle while it is level.
  lineX(v) { return BAR.x + 2 + (BAR.w - 4) * clamp(0.5 + (v.red - v.blue) / (2 * v.span), 0, 1); }

  // In the last stretch, while she is not ahead, it hauls the chest off and she is dragged along; ahead, she holds on.
  dragging(v) { return v.sprint != null && v.net <= 0; }

  // A line of a kind of the game's, naming a viewer. The 捣蛋鬼's bubble is narrow: a space in its line breaks it.
  gameLine(kind, nickname) { return this.fill(this.pick(kind), kind, { nickname }); }

  pkSay(kind, now, name) { this.gm.visit.say = { text: this.gameLine(kind, name).replaceAll(' ', '\n'), at: now }; }

  herSay(kind, now, name) { this.gm.say = { text: this.gameLine(kind, name), at: now }; }

  // She hops to x in so many little hops (never a slide).
  hopTo(x, now, hops = 1) { const gm = this.gm; if (Math.abs(x - gm.herX) > 1) gm.walk = { from: gm.herX, to: x, at: now, ms: 300 * hops, hops }; }

  // The chest it has let go of goes home: the sprite presses the lid shut if it is open, takes up the 捣蛋鬼's dropped hook
  // rope and flies the chest back over her head, off the left of the page, in ms. t2 is how long it is before the sprite can
  // have the rope; got (gameState) is when it really has it.
  sendHome(now, ms = HOME, popped = false) {
    const v = this.gm.visit, c = v.chest;
    if (!c) return;
    const open = popped || !!this.gl?.open;
    this.gm.out = { at: now, ms, hook: this.ring(c.x, 1), open, t2: GO + (open ? SHUT : 0) + TAKE, got: null };
    v.chest = null;
  }

  pkCell(pose) { return this.still(pose, this.prankster.poses); }

  // Where the rope in its carrying drawing ends on the page (tip, measured by Pet), and how far from the sprite's centre the
  // rope in its drawing ends.
  pkTip(p) {
    const c = this.pkCell(p.pose), s = PK_H / c.ruler * p.scale, x = (p.flip ? -1 : 1) * c.tip.x * s, y = c.tip.y * s, k = Math.cos(p.rot), n = Math.sin(p.rot);
    return { x: p.x + x * k - y * n, y: p.y + x * n + y * k };
  }

  spTip(pose) { const c = this.cell(pose, this.art.sprite), s = SPRITE_H / c.ruler; return { x: c.tip.x * s, y: c.tip.y * s }; }

  // Where its flight with the chest ends: the end of its rope right over where the chest's handle will stand.
  carryEnd() { const c = this.pkCell('carry'); return { x: this.ring(LAND, 1).x + c.tip.x * PK_H / c.ruler, y: 198 }; }

  // The link the chest's ring on side s (-1 hers, 1 the 捣蛋鬼's) hangs from, when the chest stands at x. The chest's two ring
  // handles are not in its drawings: each is the one ring picture, hung from the link on the chest's side and swung toward
  // whatever pulls it. ring, in the drawings' px (chestBox.k brings them to the page): the link, in from the chest's side and
  // up from its foot; the point of the ring's picture it hangs from; from there to the middle of its band on the far side.
  ring(x, s) { const { ring } = this.prankster, box = this.chestBox; return { x: x + s * (box.w / 2 - ring.x * box.k), y: CHEST.y - ring.y * box.k }; }

  // How far in front of her centre the chest's link is in a drawing of hers that holds its ring (her drawings that hold it
  // were drawn with the chest standing in the canvas; the link is at column grip of their cells).
  grip(pose) { const c = this.still(pose); return (c.ox + this.art.grip - c.cx) * H / c.ruler; }

  // A pose's own cell of its sheet, of her poses unless set is another's (a name not in it, that sheet's first), not a loop's
  // frame (Pet's cell): what is measured on a drawing standing still.
  still(pose, set = this.art.poses) { const [k, i] = set[pose] || [pose, 0]; return this.cells[k][i]; }

  // Where her sprite pats her head as she lies where the 捣蛋鬼 has dragged the chest from her: as 'head' puts it over her
  // standing, her head lyingHead across her lying drawing.
  comfort(st, now) { const b = this.herBox(st, this.cell(st.pose)); return ['at', b.x + b.w * this.art.lyingHead[st.pose] - 36, b.y - 15 + 3 * Math.sin(now / 500)]; }

  // Where the 捣蛋鬼 floats to pull at the ring that hangs from p: leaning back by rot, the rope its drawing holds (less trim) in
  // one line with the hook and the ring. pkRopeEnd: where that rope ends on the page, the 捣蛋鬼 at p (the board's pkEnd: that
  // name is the PK's end, an event the page is told).
  pkPull(p, pose, rot, trim) {
    const c = this.pkCell(pose), s = PK_H / c.ruler, x = (c.x - c.cx + trim) * s, y = (c.y - c.cy + c.h * c.rope) * s;
    const d = this.prankster.ring.reach * this.chestBox.k + HOOK + 2, k = Math.cos(rot), n = Math.sin(rot);
    return { x: p.x + d * k - x * k + y * n, y: p.y + d * n - x * n - y * k };
  }

  pkRopeEnd(p) {
    const c = this.pkCell(p.pose), s = PK_H / c.ruler * p.scale, x = (c.x - c.cx + (p.trim || 0)) * s + 2, y = (c.y - c.cy + c.h * c.rope) * s, k = Math.cos(p.rot), n = Math.sin(p.rot);
    return { x: p.x + x * k - y * n, y: p.y + x * n + y * k };
  }

  // A 人气票 or 小花花 lands (h: its 票 n, twice in a lucky window, the gifts count, its side, spend and viewer w). Over an
  // empty chest one gift decides it: a 人气票 knocks it off the page, and is the chest's first; a 小花花 goes in the chest, and
  // it is off with the chest (snatch). In the tug a 人气票 moves the line its 票 toward
  // the 捣蛋鬼's end, a 小花花 its 票 toward hers, landing on her head: she lets go of the handle, so the 捣蛋鬼, glad of it,
  // points at her and laughs, and the chest comes a little its way (tumble), while her sprite pulls on alone; then she takes
  // hold again. The fifth of them, counted by the gift (count, five in one message too), flattens her: she lies flat while her
  // sprite pulls on, catches the handle lying down and is dragged a little before she is up. While she is dragged in the last
  // stretch she only winces. One that lands once the fight is over goes home to her chest.
  strike(now, h) {
    const gm = this.gm, v = gm.visit, name = h.w.name;
    if (DONE.has(v.phase)) { gm.chest += h.spend; gm.pile += h.count; return; }
    if (!v.loot) {
      if (h.side < 0) return this.snatch(now, h.w);
      gm.grudge = name; gm.chest += h.spend; gm.pile += h.count;
      this.phase('knocked', now); this.sendHome(now, HOME, true); return this.fire('pranksterKnocked', { game: true, nickname: name });   // the lid it sat on springs open for the gift
    }
    v.spent += h.spend; v.count += h.count;
    v.net += h.side * h.n;   // as it is, so time up judges it right: only the chest's drift stops at an end
    if (h.side < 0) {
      if (this.dragging(v)) { v.wince = now; return; }
      Object.assign(v, { bonk: now, tumble: now, helped: v.helped + h.count });
      if (v.helped % 5 < h.count) v.buried = now;   // one of these was the fifth, the tenth…
      return this.herSay('herHit', now, name);
    }
    v.hit = now; gm.grudge = name;
    this.herSay('pranksterHit', now, name);
  }

  // The chest has landed beside her: she starts back from the crash, then lunges for its handle and pulls, and the minute
  // starts. (How far each end is, span, was set as it came: the bar shows from then on.)
  grab(now) {
    const gm = this.gm, v = gm.visit;
    Object.assign(v, { left: GAME.fight, grabbed: now });
    this.phase('tug', now); v.say = null; this.herSay('chestFell', now);
    this.hopTo(Math.min(gm.herX, this.ring(LAND, -1).x - this.grip('lunge') - 10), now);   // back far enough to lunge forward
  }

  // A danmaku of w's (said: the side it picks, 1 or -1, or none) while it is here. Flying over with the chest, any danmaku
  // freezes it in a disguise while she looks about (three times at most). 1 or 2 puts w on a side (enlist); a viewer B站 tells
  // with no UID is nobody to put on one. On the ground beside her (the tug, or sitting on an empty chest), and not while the
  // lightning plays: one on a side throws a paper ball at the other side, a lucky one now and then while they fight, as often
  // as the streamer set; one on no side is called out by name, once a 来访, no sooner than TAUNT after the last and not over
  // another line of its own. A danmaku never moves the bar.
  gameDanmaku(w, said) {
    const v = this.gm.visit, now = this.now();
    if (!v || DONE.has(v.phase)) return;
    if (v.phase === 'carry' && v.freezes < 3 && !(now - v.frozen < FREEZE)) { v.frozen = now; v.freezes++; }
    if (!w.uid) return;
    const side = said ? this.enlist(w, said, 'dm') : v.sides.get(w.uid)?.side;
    if (!['tug', 'sit'].includes(v.phase) || v.cut != null) return;
    if (side) {
      const lucky = v.phase === 'tug' && !v.luck && now - v.lucked >= LUCK.gap && this.rng() < this.settings.luckyPercent / 100;
      return void v.balls.push({ at: now, side, lucky, w, seed: this.rng() });   // (seed: the board's draw, which its routes are compared by)
    }
    if (v.taunted.has(w.uid) || now - v.taunt < TAUNT || v.say && now - v.say.at < 3000) return;   // (its bubble shows 3 s)
    v.taunted.add(w.uid); v.taunt = now; this.pkSay('pranksterTaunt', now, w.name);
  }

  // The paper balls that hit this frame: a lucky one opens whoever it hit, its side's gift counting twice for LUCK.ms (it says
  // so: hit, it begs nobody to send 人气票 now; her hit, it calls for 小花花); RETORT.hits of one side's in RETORT.ms has the
  // one hit answer back to whoever threw the last (it to her side, she to its), not over a line just said.
  ballsLand(now) {
    const v = this.gm.visit;
    for (const b of v.balls) {
      if (b.hit != null || now - b.at < BALL) continue;
      b.hit = now;
      const run = v.struck[b.side] = (v.struck[b.side] || []).filter(t => now - t < RETORT.ms).concat(now);
      if (b.lucky && v.phase === 'tug' && v.cut == null && !v.luck) {
        v.luck = { side: b.side, at: now, until: now + LUCK.ms }; v.lucked = now + LUCK.ms;
        this.pkSay(b.side > 0 ? 'pranksterDazed' : 'pranksterUrge', now);
      } else if (run.length >= RETORT.hits && now - (v.retorted[b.side] ?? -Infinity) >= RETORT.gap && !(v.say && now - v.say.at < 3000) && !(this.gm.say && now - this.gm.say.at < 2600)) {   // (its bubble 3 s, hers 2.6)
        v.retorted[b.side] = now;
        if (b.side > 0) this.pkSay('pranksterRetort', now, b.w.name); else this.herSay('herPelted', now, b.w.name);
      }
    }
    v.balls = v.balls.filter(b => b.hit == null || now - b.hit < 450);
    if (v.luck && now >= v.luck.until) { v.luckOut = { side: v.luck.side, at: now }; v.luck = null; }
  }

  // A gift that scares it off (a 大航海, a 中礼物 or bigger, a castle; not a lottery or a PK) does it before everyone's eyes:
  // light in the colour of the gift's tier swells in her and goes out as a ring (drawGame); due is when the ring, slowing as
  // it goes, reaches the edge of the 捣蛋鬼 where it is now (her middle o, its v.pos), and then it is scared off.
  giftBurst(now, ev, name, opts) {
    const gm = this.gm, p = gm.visit.pos, o = { x: gm.herX, y: FOOT.y - H * 0.5 };
    const k = clamp((Math.hypot(p.x - o.x, p.y - o.y) - PK_H * 0.35 - 30) / BURST.r, 0, 0.9);
    gm.burst = { at: now, o, rgb: GLOW[ev.tier], due: now + BURST.glow + BURST.ms * (1 - Math.sqrt(1 - k)), name, opts, more: [] };
  }

  // The light has reached it: the gift's show goes on, and what waited behind it.
  burstIn() {
    const b = this.gm.burst;
    b.due = null; this.fire(b.name, { ...b.opts, burst: true });
    for (const [n, o] of b.more) this.fire(n, o);
  }

  // While the fight is on it is her 秒杀: the line runs to its end, her half flashes as when she wins, and the name of whoever
  // sent the gift rises from that end (a lottery, a 试演 has none).
  sweep(now, name) {
    const gm = this.gm, v = gm.visit;
    Object.assign(v, { result: 1, resultAt: now, swept: true, ghost: { lead: v.red - v.blue, at: now, n: 30 } });
    if (name) gm.nums.push({ at: now, text: name, side: 1, x: BAR.x + BAR.w, big: true });
  }

  // Something that scares it off came (a 大航海, a gift's show, a castle, a lottery, a 试演): it lets go and flees, and the sprite
  // hurries the chest home before the show starts; while the fight is on that is the chest kept, and she cheers, saved (kept,
  // sweep). What the show then waits for: the chest home (tidy, TIDY at least) and, after the lightning, her hair, she
  // cheering on frizzed (frizz), which her sprite, right by her, smooths before it takes the chest (wait).
  scared(now, name) {
    const v = this.gm.visit, frizz = v.phase === 'tug' && v.bolted ? 'cheer' : null, kept = this.contested(v);
    if (kept) this.sweep(now, name);
    const tidy = this.scare(now);
    if (kept && frizz && this.gm.out) this.gm.out.wait = true;
    return { tidy, frizz, kept };
  }

  // What is thrown and not yet landed goes in the chest there and then.
  scare(now) {
    const gm = this.gm, v = gm.visit;
    if (DONE.has(v.phase)) return 0;
    for (const q of [...v.hits, ...v.queue]) { v.spent += q.spend; v.count += q.count; }
    v.hits = []; v.queue = [];
    if (this.contested(v)) gm.streak++;
    gm.chest += v.spent; gm.pile += v.count;
    const hung = v.chest && v.chest.x == null && gm.hung;   // flying with it, the chest hanging from its rope
    this.phase('fled', now);
    if (!v.chest) return 0;
    let fall = null;
    if (hung) {
      // it falls straight down from where it hung (kept on the page), and stands where it lands
      const box = this.chestBox, ex = this.ring(0, 1).x, cx = clamp(hung.hook.x - ex, box.w / 2 + 4, PAGE.w - box.w / 2 - 4);
      fall = { at: now, hook: { ...hung.hook }, rot: hung.rot };
      v.chest = { x: cx };
      // were it to come down on her, she hops out from under it first, away from it
      const reach = box.w / 2 + 50;
      if (Math.abs(cx - gm.herX) < reach) {
        let side = gm.herX <= cx ? -1 : 1, to = cx + side * (reach + 20);
        if (to < 60 || to > PAGE.w - 60) { side = -side; to = cx + side * (reach + 20); }
        this.hopTo(clamp(to, 60, PAGE.w - 60), now);
      }
      v.dodge = now;
    }
    this.sendHome(now, TIDY, !!(fall && v.loot)); Object.assign(gm.out, { hop: 2, fall, spot: now + (fall ? FALL : 0) });   // she hops back to the middle once the chest is up out of her way
    return TIDY;
  }

  // Beaten off: what was thrown lands in the chest, the sprite flies it home, and she thanks whoever landed the last of it
  // (w), or whoever scored the 绝杀 (buzzer), if it was one.
  win(now, w, buzzer) {
    const gm = this.gm, v = gm.visit;
    gm.streak++; gm.chest += v.spent; gm.pile += v.count;
    if (buzzer) gm.buzzer = { at: now + GAME.flash, side: 1, w: buzzer };
    Object.assign(v, { result: 1, resultAt: now });
    // beaten while it is still getting up from its fall it is knocked flying off the lid, as off an empty chest
    this.phase(v.phase === 'tug' && now - v.grabbed < UP ? 'knocked' : 'won', now); this.sendHome(now); this.hopTo(FOOT.x, now + HOME * 0.6, 3); this.pkSay('pranksterFlees', now);
    this.fire('chestKept', { game: true, streak: gm.streak, zap: !!v.bolted, nickname: (buzzer ?? w)?.name, buzzer: !!buzzer });
  }

  // It has the chest, and she cries once it is off the page with it (robbed: endVisit).
  lose(now, buzzer) {
    const gm = this.gm;
    Object.assign(gm, { chest: 0, pile: 0, streak: 0, steals: gm.steals + 1, brag: true });   // next time it brags of it
    if (buzzer) gm.buzzer = { at: now + GAME.flash, side: -1, w: buzzer };
    Object.assign(gm.visit, { result: -1, resultAt: now });
    this.phase('lost', now); this.pkSay(buzzer ? 'pranksterBuzzer' : 'pranksterRobs', now, buzzer?.name);
    this.fire('chestRobbed', { game: true, zap: !!gm.visit.bolted, robbed: true });
  }

  // 0:00 and the grace are over: what is on its way lands, and the side the line is on wins, a tie the 捣蛋鬼's. It is a 绝杀
  // when whoever put the winners ahead did so in the last stretch or the grace.
  settle(now) {
    const v = this.gm.visit;
    for (const q of [...v.hits, ...v.queue]) this.strike(now, q);
    v.hits = []; v.queue = [];
    if (DONE.has(v.phase)) return;
    const side = v.net > 0 ? 1 : -1, f = v.flip[side], buzzer = f?.late ? f.w : null;
    if (side > 0) this.win(now, f?.w, buzzer); else this.lose(now, buzzer);
  }

  // It is off the page: what came while it was here goes in her chest (gameGift), and, robbed, she cries now (被偷了).
  endVisit(now) {
    const gm = this.gm, c = this.cur;
    gm.chest += gm.aside; gm.pile += gm.asidePile; gm.aside = gm.asidePile = 0;
    gm.visit = null; gm.last = now; if (!gm.walk) this.hopTo(FOOT.x, now, 2); this.visited('end');
    if (c?.opts.robbed && now < c.start + c.pre) c.pre = now - c.start;
  }

  // The 宝箱小游戏 is still being wound up: the 捣蛋鬼 is still on the page, the chest is on its way home, her hair is still to be
  // smoothed, the game's own last show plays (守住了, 打跑了, 被偷了), or the show that scared it off waits for those. Meanwhile
  // she is busy: a show that comes waits its turn (a C one is let go), and none cuts in.
  wrapping(now) {
    const gm = this.gm, c = this.cur;
    return !!(gm.visit || gm.out || gm.fix || c && (c.opts.game || (c.opts.tidy || c.opts.frizz) && now < c.start + c.pre));
  }

  // The 来访's clock. It comes on its own at most once in pranksterMinutes and GAME.hourly times an hour, in a connected room
  // when someone is about, and once called; then, the room still connected, once its pictures are there and she plays
  // nothing, a show, a 试演 or a round's blind boxes shaken for their 盲盒播报 (shaking; a 一轮送礼 of gifts going on does not
  // keep it waiting), no lottery is drawn (lotOf, whether its show is on or not), no PK is on and the last 来访 is tidied up
  // (the chest home, her hair smooth). Then each phase runs its time, and the tug its countdown.
  gameUpdate(now) {
    if (this.gm.burst?.due != null && now >= this.gm.burst.due) this.burstIn();
    const gm = this.gm, v = gm.visit, dt = now - (gm.clock ?? now), every = this.settings.pranksterMinutes * 60000;
    gm.clock = now;
    gm.drops = gm.drops.filter(d => now - d.at < 1400); gm.nums = gm.nums.filter(n => now - n.at < 1100);
    // the chest is home once the sprite has flown it off the page (or, should the sprite never get the rope, a while after)
    if (gm.out && (gm.out.got != null ? now - gm.out.got >= gm.out.ms - gm.out.t2 && !(this.sv?.x > -50) : now - gm.out.at >= gm.out.ms + 6000)) gm.out = null;
    if (!v) {
      gm.came = gm.came.filter(t => now - t < 3600000);
      if (this.room && every && now - gm.last >= every && now - this.lastAny < GAME.about && gm.came.length < GAME.hourly) gm.due ||= 'own';
      if (gm.due && this.room && this.gameLoaded && !this.cur && !this.trial && !this.shaking() && !lotOf(this.lottery) && !gm.pk && !gm.out && !gm.fix) this.startVisit(now);
      return;
    }
    // what is thrown waits while the chest is in the air, until the 捣蛋鬼 is on its feet again, and while the lightning plays;
    // then all of it drops
    const away = ['peek', 'carry', 'fall'].includes(v.phase) || v.phase === 'tug' && now - v.grabbed < UP || v.phase === 'peer' && now - v.at < DAZED + 400 || gm.burst?.due != null;
    if (!away && v.cut == null && v.queue.length) {
      for (const q of v.queue) { gm.drops.push({ at: now, her: q.side < 0 && v.loot, big: q.n >= 10, side: q.side }); v.hits.push({ at: now + DROP, ...q }); }   // over an empty chest a 小花花 is for it
      v.queue = [];
    }
    for (const h of v.hits.filter(h => h.at <= now)) this.strike(now, h);
    this.ballsLand(now);
    v.hits = v.hits.filter(h => h.at > now);
    const u = now - v.at;
    switch (v.phase) {
      case 'peek': if (u >= GAME.peek) this.phase('carry', now); break;
      case 'carry':
        // what came while it peeked counts now, the chest hanging from its rope (drawn: v.chest) on its way in
        if (v.chest) for (const q of v.queue.filter(q => q.early)) { q.early = false; if (!DONE.has(v.phase)) this.reckon(q, now); }
        if (DONE.has(v.phase)) break;
        if (now - v.frozen < FREEZE) v.at += dt;   // frozen, its clock stands still
        else if (u >= GAME.carry) {
          // the chest is too much for it: over her right it can hold on no longer, and comes down with the chest
          v.pos = this.carryEnd();   // where the carrying ends, whatever frame drew it last
          this.phase('fall', now);
        }
        break;
      case 'fall':
        if (u < FALL) break;
        // the chest stands on the ground, the 捣蛋鬼 in a heap on its lid
        v.chest = { x: LAND }; v.landed = now; v.pos = { x: LAND, y: CHEST.y - this.chestBox.h - PK_H * 0.3 };
        if (v.loot) this.grab(now); else { this.phase('peer', now); this.hopTo(FOOT.x - 60, now + DAZED + 300); }
        break;
      case 'peer': if (u >= DAZED + 1900) { this.phase('shrug', now); this.pkSay('pranksterEmpty', now); } break;
      case 'shrug': if (u >= 2800) { this.phase('sit', now); this.pkSay('pranksterSits', now); } break;
      case 'sit': {
        const n = Math.floor(u / 20000);
        if (u >= GAME.loiter) { this.phase('toss', now); this.pkSay('pranksterToss', now); }
        else if (n > (v.heckled || 0)) { v.heckled = n; v.laugh = now; this.pkSay('pranksterSits', now); }
        break;
      }
      case 'toss': if (u >= 1700) this.phase('leave', now); break;
      case 'tug':
        if (v.cut != null) {   // the set piece plays: the countdown waits; its bolt knocks her lead back (zapBack)
          if (!v.bolted && now - v.cut >= BOLT) { v.bolted = true; if (gm.burst?.due == null) this.zapBack(now); }
          if (v.cut != null && now - v.cut >= ZAP) v.cut = null;
          break;
        }
        // a gift's light is on its way to scare it off: the fight is decided, so the countdown and the lightning wait, and no
        // 秒杀 comes first
        if (gm.burst?.due != null) break;
        if (v.zapped == null && v.judge == null && v.net >= v.span / 2) { v.zapped = v.cut = now; this.pkSay('pranksterZap', now); break; }
        // it answers her once her bubble is gone (the two stand close then), and after that calls viewers to its side
        if (!v.caught && now - v.grabbed >= 2900) { v.caught = true; this.pkSay('pranksterCaught', now); }
        if (!v.recruited && now - v.grabbed >= 6200) { v.recruited = true; this.pkSay('pranksterRecruit', now); }
        if (v.judge == null) {
          v.left -= dt;
          if (v.left <= GAME.sprint && v.sprint == null) v.sprint = now;
          if (v.left <= 0) Object.assign(v, { left: 0, judge: now });   // GAME.grace for what is late on the way
        } else if (now - v.judge >= GAME.grace) { this.settle(now); break; }
        // its bye the first time it hauls the chest off
        if (this.dragging(v) && !v.bye) { v.bye = true; this.pkSay('pranksterSprint', now); }
        // dragged in the last stretch while she is not ahead, held while she is: the chest comes back as smoothly as it went
        v.drag += clamp((this.dragging(v) ? 150 * k01(now, v.sprint, v.sprint + GAME.sprint) : 0) - v.drag, -dt * 0.06, dt * 0.06);
        break;
      case 'snatch': if (u >= SNATCH.up) { this.phase('away', now); v.from.x += 2 * this.pkTip({ pose: 'carry', flip: false, x: 0, y: 0, rot: 0, scale: 1 }).x; } break;
      case 'lost': if (u >= 1600) this.endVisit(now); break;
      case 'away': if (u >= 2400) this.endVisit(now); break;
      default: if (u >= (v.phase === 'fled' ? (v.ran ?? Infinity) + OFF.fled : OFF[v.phase] ?? 1800)) this.endVisit(now);   // leave, knocked, won, fled: it is off the page
    }
  }

  // Where the chest and the 捣蛋鬼 are at this moment (this.gl, for drawGame), and what she does about it while no show of hers
  // plays: every phase of a 来访 has its place and pose, and she stays in her 心情 until the chest lands beside her.
  gameState(st, now) {
    const gm = this.gm, v = gm.visit, box = this.chestBox;
    const L = this.gl = { chest: null, open: false, pk: null };
    // she hops to where she is to stand, and everything she plays is played from there
    let hopY = 0;
    if (gm.walk) {
      const k = k01(now, gm.walk.at, gm.walk.at + gm.walk.ms);
      gm.herX = gm.walk.from + (gm.walk.to - gm.walk.from) * ease(k);
      if (k > 0 && k < 1) hopY = 10 * Math.abs(Math.sin(k * Math.PI * gm.walk.hops));
      if (k >= 1) gm.walk = null;
    }
    st.x += gm.herX - FOOT.x; st.y -= hopY;
    if (gm.herX !== FOOT.x && !st.sp) st.sp = ['home', gm.herX - FOOT.x, 0];
    // The chest going home: the sprite comes down on an open lid and presses it shut, takes up the 捣蛋鬼's dropped hook rope
    // (its drawing holds the rope from then on), lifts the chest straight up, higher than her bubble, and flies it over her
    // head off the left of the page. The chest hangs from the end of the sprite's rope, where the sprite really is (drawGame).
    if (gm.out) {
      const o = gm.out, tip = this.spTip('lift'), home = o.hook.x - this.ring(0, 1).x, sv = this.sv;
      // it sets off only once the 捣蛋鬼 is off the chest, and goes for the rope only once it is clear of where the chest goes
      // up (CLEAR); its times run from when it sets off (go). Scared off, the chest it was flying with falls first (fall),
      // landing with a thud; and the sprite starts at it ('!', NOTICE) from o.spot before it does anything.
      const fell = !o.fall || now - o.fall.at >= FALL, startle = o.spot != null && now >= o.spot && now < o.spot + NOTICE;
      if (o.fall && fell && !o.fall.landed) { o.fall.landed = now; if (v) v.landed = now; }
      if (startle) { st.sprite = 'surprised'; L.notice = now - o.spot; }
      // (by her it can be where the chest comes down: it darts out from under it to the side of it it is on, not across her,
      // or to the other side when that is off the page)
      if (o.fall && o.go == null) {
        const cx = o.hook.x - this.ring(0, 1).x, d = box.w / 2 + 36, ok = x => x >= 30 && x <= PAGE.w - 30;
        o.fall.dart ??= (sv ? sv.x : gm.herX) < cx ? (ok(cx - d) ? cx - d : cx + d) : (ok(cx + d) ? cx + d : cx - d);
        st.sp = ['at', o.fall.dart, CHEST.y - 120];
      }
      const away = !v || v.pos.a < 0.05 || v.pos.x > PAGE.w + PK_H / 2 || v.pos.y < -PK_H / 2;
      if (o.go == null && !o.wait && fell && (o.spot == null || now >= o.spot + NOTICE) && (away || Math.abs(v.pos.x - home) > 110 || v.pos.y < CHEST.y - 300)) o.go = now;   // (wait: her hair first, unfrizz)
      const u = o.go == null ? -Infinity : now - o.go;
      const p0 = { x: o.hook.x - tip.x, y: o.hook.y - tip.y - LINK };   // there the end of the sprite's rope is LINK over the hook
      // (the 捣蛋鬼's place last frame: v.pos)
      if (o.clear == null && o.go != null && (away || v.pos.x - p0.x > CLEAR)) o.clear = now;
      if (o.got == null && o.clear != null && u >= o.t2 && sv && Math.hypot(sv.x - p0.x, sv.y - p0.y) < 8) { o.got = now; if (o.hop) this.hopTo(FOOT.x, now + 400, o.hop); }
      st.orbit = null; st.sFlip = false;
      if (o.got != null) {
        // straight up first, above where her bubble is, then across over her head
        const q = k01(now - o.got, 0, o.ms - o.t2), up = ease(k01(q, 0, 0.4)), across = ease(k01(q, 0.4, 1));
        st.sprite = up < 1 ? 'lift' : 'lift2'; st.sp = ['at', p0.x + (-100 - p0.x) * across, p0.y + (56 - p0.y) * up];
        L.chest = { carried: true, len: LINK, lift: ease(k01(q, 0, 0.25)) };
      } else {
        // (it presses at the end of SHUT: the gift that beat the 捣蛋鬼 off is still on its way into the chest)
        const press = o.open && u < GO + SHUT, shut = o.open ? ease(k01(u, GO + SHUT * 0.4, GO + SHUT)) : 1;
        if (o.go != null) {
          st.sprite = press && u >= GO ? 'lid' : 'sIdle';
          st.sp = press ? ['at', home, CHEST.y - box.open + (box.open - box.h) * shut - SPRITE_H * 0.16]
            : o.clear == null ? ['at', home - 20, CHEST.y - box.h - SPRITE_H * 0.45] : ['at', p0.x, p0.y];
        }
        if (!fell) {
          // falling: from where its ring hung to where it is on the ground, coming level, the dropped rope trailing
          const k = Math.min(1, ((now - o.fall.at) / FALL) ** 2);
          L.chest = { still: { x: o.fall.hook.x + (o.hook.x - o.fall.hook.x) * k, y: o.fall.hook.y + (o.hook.y - o.fall.hook.y) * k }, rot: o.fall.rot * (1 - k) }; L.loose = true;
        } else { L.chest = { x: home, y: CHEST.y }; L.open = o.open && shut < 0.5; L.dropped = true; }
      }
    }
    if (!v) return st;
    st.slide = true;   // her place goes with her pose while it is here (drawHer)
    // (free: no show of hers plays, or 被偷了 waits for it to be off with the chest and she is still the game's)
    const u = now - v.at, free = !this.cur || this.cur.opts.robbed && now < this.cur.start + this.cur.pre, bob = 4 * Math.sin(now / 520), low = CHEST.y - 74;
    if (free && ['peek', 'carry', 'fall'].includes(v.phase)) st.bubble = null;   // her 心情 says nothing while a chest flies past where its bubble is
    else if (free) { st.orbit = null; st.y = FOOT.y - hopY; }                     // and beside the chest she is no longer in her 心情: no happy bounce
    const seat = CHEST.y - box.h - PK_H * 0.3, over = CHEST.y - box.open - PK_H * 0.24;   // its height seated on the lid; over the open lid
    const pk = L.pk = { pose: 'idle', x: v.pos.x, y: v.pos.y, flip: false, rot: 0, scale: 1, a: 1 };
    const fly = (vx, vy, grav = 0) => { pk.x = v.from.x + vx * u; pk.y = v.from.y + vy * u + grav * u * u; };
    const hop = (x, y, ms = 400) => { const e = ease(k01(u, 0, ms)); pk.x = v.from.x + (x - v.from.x) * e; pk.y = v.from.y + (y - v.from.y) * e; };
    let cx = v.chest?.x ?? null;   // the chest standing on the ground, if it does
    switch (v.phase) {
      // it slides in and, before it flies the chest over, back out the same way: it takes up its rope off the page
      case 'peek': Object.assign(pk, { pose: 'peek', flip: true, x: -70 + 94 * ease(k01(Math.min(u, GAME.peek - u), 0, 500)), y: 120 + bob }); break;
      case 'carry': {
        // It flies in little goes, its arms in front of it holding its hook rope, the chest hanging from the rope by one handle
        // and swinging as it goes (drawGame); the weight sinks it until it is over her head. Frozen, it is a paper bag or a
        // paper king with a chest still hanging from it. (Its sneaking drawing faces the viewer's left: flipped.)
        const p = u / GAME.carry * 9, go = Math.min(1, (Math.floor(p) + ease(k01(p % 1, 0, 0.6))) / 9), frozen = now - v.frozen < FREEZE;
        const strain = frozen ? 0 : k01(go, 0.72, 1);   // near the end the weight is too much for it: it wobbles, sweats and sinks
        Object.assign(pk, { pose: frozen ? (v.freezes % 2 ? 'carryBag' : 'carryCrown') : 'carry', flip: !frozen, x: -70 + (this.carryEnd().x + 70) * go,
          y: 120 + 70 * Math.sin(Math.PI / 2 * Math.min(1, go / 0.6)) + 8 * k01(go, 0.72, 1) + (frozen ? 0 : bob), rot: 0.12 * strain * Math.sin(now / 80) });
        L.sweat = strain;
        L.chest = { hang: this.pkTip(pk), len: LINK };
        if (frozen && free) { if (st.pose === 'idle') st.pose = 'lookUp'; st.sprite = 'sLook'; L.ask = now - v.frozen; }   // she looks up and about
        break;
      }
      case 'fall': {
        // It can hold on no longer: it and the chest come down together, faster and faster, the chest coming level, and it
        // lands in a heap on the chest's lid.
        const k = Math.min(1, (u / FALL) ** 2), r = this.ring(LAND, 1), y0 = this.pkTip({ ...pk, pose: 'carry', flip: true, x: v.from.x, y: v.from.y }).y + LINK;
        Object.assign(pk, { pose: 'fall', flip: true, x: v.from.x + (LAND - v.from.x) * k, y: v.from.y + (seat - v.from.y) * k, rot: -0.3 * k });
        L.chest = { still: { x: r.x, y: y0 + (r.y - y0) * k }, rot: HANG * (1 - k) };
        break;
      }
      case 'peer': {
        // It lies dazed on the lid where it fell (DAZED); then it is up over the chest and has the lid open: empty, and a moth
        // flies out. She has started at the crash; now she is sorry and embarrassed about her empty chest.
        const up = ease(k01(u, DAZED, DAZED + 400));
        Object.assign(pk, { pose: u < DAZED ? 'dazed' : u < DAZED + 400 ? 'shake' : 'peer', flip: u < DAZED, x: cx, y: seat + (over - seat) * up + bob * up });
        L.open = u >= DAZED + 500; L.moth = u - DAZED - 700; L.ropes = L.slack = true;
        if (free && u >= 250) st.pose = u < DAZED + 700 ? 'startled' : 'sorry';
        break;
      }
      case 'shrug': Object.assign(pk, { pose: 'shrug', x: cx, y: over + (seat - over) * ease(k01(u, 1900, 2700)) + bob * (1 - k01(u, 1900, 2700)) }); L.open = u < 1900; L.ropes = L.slack = true; if (free) st.pose = 'sorry'; break;
      // (she is sorry and embarrassed about her empty chest, not angry)
      case 'sit': Object.assign(pk, { pose: now - v.laugh < 2600 ? 'laugh' : 'sit', x: cx, y: seat }); L.ropes = L.slack = true; if (free) st.pose = 'sorry'; break;
      case 'toss': {
        // bored of an empty chest, it gets off and throws the chest back over her head, home
        pk.pose = u < 450 ? 'shrug' : u < 1200 ? 'throw' : 'laugh'; hop(v.fromCx + 56, low + bob, 400);
        const w = Math.max(0, u - 500);
        if (w > 0) { L.chest = { x: v.fromCx - 0.42 * w, y: CHEST.y - 1.6 * w + 0.00145 * w * w, spin: -w / 180 }; cx = null; v.chest = null; }
        else L.ropes = L.slack = true;   // its hook comes out of the ring as it throws
        if (free) st.pose = 'sorry';
        break;
      }
      case 'leave': fly(0.16, -0.05); pk.y += bob; break;
      // Off with the empty chest (snatch): it settles on the lid and laughs its thanks at her, hops off to the right till its
      // rope hangs straight down to the chest's ring, and rises, the chest coming off the ground by that handle and tilting as
      // it comes up; she stays sorry till it is off (away).
      case 'snatch': {
        const tip = this.pkTip({ pose: 'carry', flip: false, x: 0, y: 0, rot: 0, scale: 1 }), r = this.ring(v.fromCx, 1), need = r.y - LINK - tip.y;   // (the chest's place as it began: once up, it stands nowhere)
        const e = ease(k01(u, 0, 300)), a = ease(k01(u, SNATCH.laugh, SNATCH.laugh + SNATCH.hop)), b = ease(k01(u, SNATCH.laugh + SNATCH.hop, SNATCH.up));
        const bx = v.from.x + (v.fromCx - v.from.x) * e, by = v.from.y + (seat - v.from.y) * e;
        Object.assign(pk, { pose: u < SNATCH.laugh ? 'laugh' : 'carry', x: bx + (r.x - tip.x - bx) * a, y: by + (need - by) * a - SNATCH.rise * b });
        if (b > 0) { L.chest = { hang: this.pkTip(pk), len: LINK, lift: clamp(SNATCH.rise * b / 40, 0, 1) }; cx = null; v.chest = null; }
        else L.ropes = L.slack = true;
        if (free) st.pose = 'sorry';
        break;
      }
      // Off with her chest in the air: she starts, sees it go and reaches up after it on her toes until it is off the page;
      // then she drops to her knees crying (被偷了).
      case 'away':
        Object.assign(pk, { pose: 'carry', flip: true }); fly(...(v.snatched ? SNATCH.fly : [0.3, -0.03])); pk.y += bob; L.chest = { hang: this.pkTip(pk), len: LINK };
        if (free) { st.pose = u < AWAY.spot ? 'startled' : 'reachUp'; st.sprite = 'surprised'; }
        break;
      // knocked off an empty chest it spins up and out of the top; beaten off it zigzags up and out of the top like a balloon
      // letting its air out (both slowly)
      case 'knocked': pk.pose = 'knocked'; fly(0.12, -0.32, 0.00003); pk.rot = u / 260; pk.a = 1 - k01(u, 2100, 2600); break;
      case 'fled': {
        // Scared off, it lets go of everything with a start (startle; a chest in the air falls: gm.out.fall), then it is off to
        // the right, looking back. It jumps with fright first, then runs off bobbing out at the right, at its own height,
        // taking OFF.fled however far from the edge it was (run). (Her frizzed hair the sprite pats smooth once the chest is
        // home: unfrizz.)
        const w = u - (v.ran ??= u);
        const run = Math.max(0, w - 450), vx = Math.max(0.06, (PAGE.w + 60 - v.from.x) / (OFF.fled - 450));
        // (it starts the way it faced, flying with the chest to the right or pulling toward her, and turns to run)
        Object.assign(pk, { pose: w < 450 ? 'startle' : 'flee', flip: w < 450 ? v.from.flip ?? true : true, x: v.from.x + vx * run, y: v.from.y - 18 * bump(w, 0, 450) - 6 * Math.abs(Math.sin(run / 110)), a: 1 - k01(run, OFF.fled - 700, OFF.fled - 450) });
        // a chest it let go of over her head: she hops out from under it, and starts at the thud
        if (v.dodge != null && (free || now < this.cur.start + this.cur.pre) && now - v.dodge < FALL + 700) st.pose = 'startled';
        break;
      }
      // (it drifts off up and to the right, out of the way of the chest the sprite lifts straight up)
      case 'won': pk.pose = 'flat'; fly(0.07, -0.12); pk.x += 24 * Math.sin(u / 95); pk.y += 26 * Math.sin(u / 120); pk.rot = 0.7 * Math.sin(u / 85); pk.a = 1 - k01(u, 2900, 3400); break;
      // it has the chest: she has lost her hold and lies where she was dragged to (her lying drawing with empty fists: the
      // ring she held hangs from the chest again)
      case 'lost': pk.pose = 'haul'; pk.rot = HAUL; fly(0.32, 0); cx = v.fromCx + 0.32 * u; L.open = true; L.ropes = true; if (free) { st.pose = v.bolted ? 'fallenZap' : 'fallen'; Object.assign(st, { sprite: 'pat', sp: this.comfort(st, now) }); } break;
      case 'tug': {
        // She starts back from the crash, lunges for the chest's handle and pulls the chest back toward her (she backs off with
        // it, out from under the dazed 捣蛋鬼), and then the three drift with the line, by what has landed: to its side while
        // it is ahead, back to hers as she gets ahead. In the last stretch, while she is not ahead, it hauls the chest off and
        // she is dragged along (drag).
        const t = now - v.grabbed, e = ease(k01(t, 850, 2300)), cut = v.cut != null ? now - v.cut : null;
        const drift = e * (TUG.drift * clamp(-v.net / v.span, -1, 1) + 1.5 * Math.sin(now / 260)) + v.drag, drag = this.dragging(v);
        // A 小花花 on her head (strike): down is how long she has been flattened (flat 1200 ms, then 700 ms holding the
        // handle lying down), loose whether she has let go. The 捣蛋鬼 is glad of it: it points at her and laughs, and the chest
        // comes a little its way (yank). The bump and the dizzy eyes are for a gift that hits it.
        const down = now - v.buried < 1900 ? now - v.buried : null, loose = down != null ? down < 1200 : now - v.bonk < 800;
        const yank = now - v.tumble < 800 ? 10 * bump(now, v.tumble, v.tumble + 800) : 0;
        cx = v.fromCx + (TUG.chest - v.fromCx) * e + drift + yank;
        // It lies dazed on the lid where it fell; as she pulls the chest away it tumbles off to the far side, heels over head,
        // the lid springs open, and it shakes itself and takes up its rope, still hooked in the handle (UP). It floats where its
        // rope runs straight from the chest's ring into its fists, leaning back (pkPull).
        const place = drag ? this.pkPull(this.ring(cx, 1), 'haul', HAUL, 0) : this.pkPull(this.ring(cx, 1), 'tug', LEAN, TRIM);
        const off = ease(k01(t, 900, 1700)), to = { x: place.x + yank * 0.4, y: place.y + bob * 0.5 };
        pk.x = cx + (to.x - cx) * off; pk.y = seat + (to.y - seat) * off - 26 * Math.sin(Math.PI * off);
        if (t < 900) Object.assign(pk, { pose: 'dazed', flip: true });
        else if (t < 1700) Object.assign(pk, { pose: 'tumble', rot: (off - 0.5) * Math.PI * 2 });
        else if (t < UP) pk.pose = 'shake';
        else pk.pose = cut != null ? 'thunder' : drag ? 'haul' : loose ? 'laugh' : now - v.hit < 450 ? 'ouch' : 'tug';
        if (pk.pose === 'haul') pk.rot = HAUL; else if (pk.pose === 'tug' || pk.pose === 'ouch') Object.assign(pk, { rot: LEAN, trim: TRIM });
        L.ropes = true; L.open = t >= 1100; L.slack = !['tug', 'haul', 'ouch'].includes(pk.pose);   // not pulling, its rope hangs slack
        if (cut != null) L.zap = cut;
        if (free) {
          // after the lightning her hair stays frizzed, whatever she does
          const z = now - v.zapped >= BOLT ? 'Zap' : '';
          st.pose = t < 500 ? 'startled' : t < 850 ? 'lunge' : down != null ? (down < 1200 ? 'flat' : 'dragged') + z : loose ? 'bonk' + z : drag ? 'dragged' + z : 'tug' + z;
          // from her lunge on, the ring in her fists is at the chest's link (her drawings hold it; held: the chest's own ring on
          // her side is then not drawn); where she has let go she stands where she pulled from
          if (t >= 500) {
            const holds = this.art.poses[st.pose][0] === 'game16';
            st.x = gm.herX = this.ring(cx - yank, -1).x - this.grip(holds ? st.pose : 'tug'); gm.walk = null;
            if (holds) L.held = 'her';
          }
          if (now - v.wince < 300) st.y -= 5 * bump(now, v.wince, v.wince + 300);
          // Her sprite is on her side: once the 捣蛋鬼 is up and pulling, it takes hold of the open lid's edge on her side, over
          // her arms, and pulls with her (its two pulling drawings by turns, its fists over the lid's edge: sOver), on alone
          // while she has let go or lies flattened: were it to let go, the 捣蛋鬼 would be off with the chest. (While the 捣蛋鬼
          // still lies on the lid it stays by her, startled.)
          if (t >= UP) {
            const key = Math.floor(now / 400) % 2 ? 'hold2' : 'hold', hc = this.cell(key, this.art.sprite), ks = SPRITE_H / hc.ruler, k = box.k, lid = this.prankster.lid;
            const at = { x: cx - box.w / 2 + lid.x * k, y: CHEST.y - lid.y * k };
            Object.assign(st, { sprite: key, sFlip: true, sOver: true, sp: ['at', at.x + 4 - (hc.cx - hc.x) * ks, at.y - (hc.y + hc.rope * hc.h - hc.cy) * ks] });
          } else st.sprite = 'surprised';
          if (cut != null) st.shake = [[v.cut + BOLT, 3, 260]];
        }
        break;
      }
    }
    v.pos = { x: pk.x, y: pk.y, a: pk.a, flip: pk.flip };
    if (cx != null) { v.chest = { x: cx }; L.chest = { x: cx, y: CHEST.y }; }
    else if (L.chest?.hang && !gm.out) v.chest = {};   // hanging in the air
    if (v.landed != null && now - v.landed < 260 && free) st.shake = [[v.landed, 3, 260]];   // the chest thuds down
    if (free && gm.say && now - gm.say.at < 2600) st.bubble = { text: gm.say.text, t: now - gm.say.at };
    return st;
  }

  // ---- what she is doing at a moment --------------------------------------------------------------------------------
  // The show playing (after waking up, when it woke her), else the round she shakes blind boxes for, else her 心情; a round's
  // gifts still fly into her arms over whatever she is doing.
  stateAt(now) {
    this.update(now);
    const c = this.cur, shaking = this.shaking();
    let st;
    this.speaking = null;
    // a show that scared the 捣蛋鬼 off waits for the chest to go home, and her hair (stateAt's tidying up before the show), in
    // the pose and saying on the line of the game's show it took over (start); saved by it while the fight was on, she cheers
    // (kept)
    if (c && now < c.start + c.pre && !c.wakeLine) {
      st = this.base();
      if (c.opts.frizz) this.unfrizz(st, now - c.start, c, c.opts.frizz);
      else { if (c.opts.kept) Object.assign(st, { pose: 'cheer', sprite: 'sHappy' }); else if (c.held) st.pose = c.held; if (c.opts.tidy && !this.gm.out) c.pre = now - c.start; }
      if (c.opts.kept) for (const a of [300, 1100]) st.y -= 12 * bump(now - c.start, a, a + 320);
      if (c.carried && now < c.carried.until) st.bubble = { text: c.carried.text, t: now - c.carried.from };
    }
    else if (c && now < c.start + c.pre) { st = this.base(); st.pose = 'awake'; st.sprite = 'surprised'; st.fx.push(['bang', c.start], ['shock', c.start], ['sweat', c.start + 200]); st.y -= 10 * bump(now - c.start, 0, 300); st.bubble = { text: this.fill(c.wakeLine), t: now - c.start }; }
    else if (c) {
      st = this.eventState(c.name, now - c.start - c.pre, { ...c.opts, seed: Math.floor(c.start), at: now }, this.fill(c.line, c.name, c.opts));
      // she says her line at the castle's gate once she gets there
      if (c.name === 'castle' && st.bubble?.at) st.bubble.text = this.fill(c.gateLine ??= this.pick('castleGate'), c.name, c.opts);
      if (c.opts.game && st.bubble?.text) this.speaking = { c, text: st.bubble.text, from: now - st.bubble.t };
    // her hair is patted smooth before she shakes a round's blind boxes too
    } else if (this.gm.fix) { st = this.base(); this.unfrizz(st, now - this.gm.fix.start, this.gm.fix, this.gm.fix.pose); if (this.gm.fix.pre != null) this.gm.fix = null; }
    else if (shaking) st = this.roundState(now, shaking);
    else {
      const mood = this.mood(now), key = mood + (this.lot ?? '');
      if (this.seen?.key !== key) this.seen = { key, at: now };
      st = this.idleState(now, mood, now - this.seen.at);
    }
    for (const r of this.rounds) if (r !== st.round) for (const x of r.arrivals) st.fx.push(['giftArc', st._t == null ? x : st._t - (now - x)]);
    return this.gameState(st, now);
  }

  base() { return { pose: 'idle', x: FOOT.x, y: FOOT.y, s: 1, sx: 1, sprite: 'sIdle', sp: null, sBehind: false, sScale: 1, sAlpha: 1, fx: [], bubble: null }; }

  // since: how long she has been in this mood (or lottery wait) without an event.
  idleState(now, mood, since = now) {
    const st = this.base();
    if (this.lot === 'waiting') { st.pose = 'hope'; st.fx.push(['sweat', 0, 2400]); this.talk(st, 'lotteryWaiting', now, since); return st; }
    if (mood === 'happy') { st.y -= 6 * Math.abs(Math.sin(now / 380)); st.orbit = now; }
    if (mood === 'calm' && now % 7000 > 5800) st.sprite = 'sLook';  // now and then it looks up at her
    if (mood === 'bored') { st.fx.push(['dots', 0, 6000], ['sigh', 0, 7000]); }
    if (mood === 'gloomy') st.fx.push(['gloom', 0]);
    if (mood === 'bored') { st.pose = 'bored'; st.sprite = now % 8000 < 2600 ? 'yawn' : 'sIdle'; st.sp = ['home', 10 * Math.sin(now / 1800), 6 * Math.sin(now / 1300)]; }
    if (mood === 'gloomy') { st.pose = 'gloomy'; st.sprite = 'pat'; st.sp = 'head'; }
    if (mood === 'sleepy') { st.pose = 'doze'; st.sprite = 'sleep'; st.sp = 'knee'; }
    this.talk(st, this.lot === 'open' ? 'lotteryEntryOngoing' : this.moodHold?.mood === mood && this.moodHold.lines || mood, now, since);
    if (mood === 'sleepy') {
      // every other half minute her head drops onto her knees: she jolts awake, insists she wasn't asleep, and dozes off again
      const u = since % 30000;
      if (u >= 23000 && u < 24400) { st.pose = 'awake'; st.y -= 10 * bump(u, 23000, 23300); st.sprite = 'surprised'; st.bubble = { text: this.saying('jolt', now - u + 23000), t: u - 23000 }; st.fx.push(['bang', now - (u - 23000)], ['shock', now - (u - 23000)]); }
      else st.fx.push(['z', now]);
    }
    return st;
  }

  // A mood or a lottery wait says a line 0.6 s in and another every 15 s after (平静 and 开心 say nothing).
  talk(st, key, now, since) {
    const u = since % 15000;
    if (this.lines[key] && u >= 600 && u < 4600) st.bubble = { text: this.fill(this.saying(key, now - u + 600), key, { keyword: this.lottery?.keyword }), t: u - 600 };
  }

  // A show's state t ms in; a lottery's is its step's (opts.result).
  eventState(name, t, opts, line) {
    const st = this.base(), ev = this.events[name], step = ev?.steps?.[opts.result], key = step ? opts.result : name;
    st._t = t; st.glow = GLOW[step?.glow || ev?.glow || ev?.tier || 'D'];
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
      // the chest game: the 捣蛋鬼 knocked off the page; beaten off, and she tosses the sprite; gone with the chest, and the
      // sprite pats her head. Her line waits for the chest to be up out of its way (chestUp).
      case 'pranksterKnocked': st.pose = t < 400 ? 'surprised' : t < 3700 ? 'cheer' : 'idle'; hop(450, 12); st.sprite = 'sHappy'; say(this.chestUp(t, opts)); break;
      // after the lightning she cheers on with her hair frizzed until the sprite is back from taking the chest home and has
      // patted it smooth (smooth), and the show is longer for it
      case 'chestKept': {
        const dur = opts.zap ? this.events[name].zapDur : this.events[name].dur, fixed = !opts.zap || this.smooth(st, t, opts, 'cheerZap', 'cheer', 0, dur, 'sHappy');
        st.pose = t >= dur - 700 ? 'idle' : fixed ? 'cheer' : 'cheerZap'; hop(300, 12); hop(1100, 14); say(this.chestUp(t, opts));
        if (!opts.zap) { if (t >= 900) this.toss(st, t, 900); else st.sprite = 'sHappy'; }
        break;
      }
      // She cries kneeling on the ground with her line up until ROB.quiet, and once it is gone curls up into the squat of the
      // 沮丧 that follows (ROB.curl), frizzed or not. After the lightning she cries frizzed until the sprite patting her head
      // has smoothed it, 3.2 s at the soonest; on her head it follows her head as she kneels up, patting, never turning to fly.
      case 'chestRobbed': {
        const fixed = !opts.zap || this.smooth(st, t, opts, 'robbedZap', 'robbed', 3200, this.events[name].dur, 'pat');
        st.pose = t >= ROB.curl ? 'gloomy' : fixed ? 'robbed' : 'robbedZap';
        if (!opts.zap) { st.sprite = 'pat'; st.sp = 'head'; }
        st.sStay = true;
        if (t < ROB.quiet) say(500);
        break;
      }
      case 'blindBoxQuery': this.queryShow(t, st, opts, line, hop); break;
      case 'pkCondition': case 'pkDouble': case 'pkDoubleEnding': case 'pkLastSeconds': this.signShow(name, t, st, opts, say); break;
      case 'pkOpponent': this.intelShow(t, st, opts); break;
      case 'pkResult': this.pkEndShow(t, st, opts, say, hop); break;
      case 'pkSwing': this.pkGapShow(t, st, opts, say, hop); break;
      case 'pkChallenge': this.pkChallengeShow(t, st, opts, say); break;
      default: if (DAILY[key]) return this.daily(t, st, { ...opts, kind: key }, line);
    }
    const extra = EVENT_FX[key];
    if (extra) { st.fx.push(...extra.fx); st.shake = extra.shake; }
    return st;
  }

  // 盲盒查询 (QUERY): the 账单卡 hangs just over her head, where Pet places it (billSpot), with her words on it, so no bubble. The
  // sprite flies up to the card's top right corner and, glowing, prints the card out of itself; it stamps the result right
  // there, comes down beside the card's right edge (clear of its words) to her waist and home to her low in front (never
  // across her face) to answer it with her; the card rolls itself up at the end.
  queryShow(t, st, opts, line, hop) {
    const q = QUERY, r = opts.result || 'Gain', stamped = r !== 'None';
    st.pose = at(t, [[0, 'hope'], [q.react, BILL_POSE[r]], [q.done, 'idle']]);
    if (r === 'Gain') { hop(q.react, 14); st.fx.push(['sparkles', q.react]); }
    if (r === 'Loss') { if (t >= q.react && t < q.react + 1200) st.y -= 4 * Math.abs(Math.sin((t - q.react) / 90)); st.fx.push(['sweat', q.react - 100], ['anger', q.react, 900]); }
    if (t < q.print[1]) { st.sprite = t < q.print[0] ? 'sLook' : 'glow'; st.sp = ['bill', 'corner']; }
    else if (t < q.down) { st.sprite = stamped ? 'nod' : 'sLook'; st.sp = ['bill', 'corner']; }
    else if (t < q.side) { st.sprite = 'sLook'; st.sp = ['bill', 'side']; }
    else st.sprite = t < q.side + 500 ? 'sLook' : BILL_SPRITE[r];
    // how much of the card has come out, top first; it bobs, and gives a little as the stamp lands
    const reveal = t < q.print[0] ? 0 : t < q.print[1] ? ease(k01(t, q.print[0], q.print[1])) : 1 - ease(k01(t, q.roll[0], q.roll[1]));
    st.bill = { result: r, name: opts.nickname ?? '', line, ...(opts.bill ?? QUERY_TRIAL[r]), reveal,
      dy: 2 * Math.sin(t / 500) + 3 * bump(t, q.stamp, q.stamp + 200), stampT: stamped && t >= q.stamp ? t - q.stamp : null };
  }

  // B站 PK (PK_SIGN): her sprite glows and the sign flies out of it, small to full size, to over her head as she raises her empty
  // hands for it (the double bonus's condition: into her hands in front of her, she ducks behind it, peeks over it, says her
  // line, brings it down to her chest and it glides up as she raises her arms); she lowers her arms and it flies back into her
  // sprite. The words are on it all the time; a countdown's number pops at each second. Her drawings with a sign draw it
  // themselves (Pet's drawHer); signFly is the sign anywhere else, drawn while her drawing has none. Where it is in each of
  // her drawings is Pet's to measure (signSpot).
  signShow(name, t, st, opts, say) {
    // a countdown: what was left as it began (until, at: the clock now), else the whole of it (a 试演)
    const q = PK_SIGN, count = Object.values(COUNTDOWNS).includes(name), key = count ? 'count' : name, up = q.pose[key];
    const begun = count ? (opts.until != null && opts.at != null ? opts.until - opts.at + t : q.lower.count) : 0, lower = count ? Math.min(q.lower.count, begun) : q.lower[key];
    const left = count ? begun - t : 0, n = Math.max(0, Math.ceil(left / 1000)), pop = count ? 1 - k01(1000 - (left % 1000 || 1000), 0, 220) : 0;
    st.sign = SIGN.words(name, opts, n, pop);
    const hide = name === 'pkCondition', lift = q.hide[1], raised = hide ? lift + q.lift + q.raise : q.fly;
    const sprite = { x: FOOT.x - SIGN.W / 2 - 30, y: FOOT.y - H * 0.55 };   // beside the sign's left edge, never over its words
    const glide = (from, to, u) => { const e = ease(u); return { x: from.x + (to.x - from.x) * e, y: from.y + (to.y - from.y) * e, a: from.a + (to.a - from.a) * e, k: from.k + (to.k - from.k) * e }; };
    const spot = c => ({ ...this.signSpot(st, c), k: 1 }), from = { ...sprite, a: 0, k: 0.15 };
    if (hide) {
      // into her hands in front of her (the first in-between of ducking behind it), then, lifting, down to her chest (the
      // last of them going back) and up over her head as her arms go up
      const duck = this.hop('idle', 'signHide') || [], chest = duck[0] || this.cell('signHide');
      st.pose = t < q.fly ? 'idle' : t < lift ? 'signHide' : t < lift + q.lift ? 'idle' : t < lower ? up : 'idle';
      if (t < q.fly) st.signFly = glide(from, spot(chest), k01(t, 0, q.fly));
      else if (t >= lift + q.lift && t < raised + 60) st.signFly = glide(spot(chest), spot(this.cell(up)), k01(t, lift + q.lift, raised));
      if (t >= q.hide[0] && t < lift) { st.fx.push(['sweat', q.hide[0] + 200]); say(q.hide[0] + 150); }
    } else {
      st.pose = t < q.fly - q.raise ? 'idle' : t < lower ? up : 'idle';
      if (t < q.fly + 60) st.signFly = glide(from, spot(this.cell(up)), k01(t, 0, q.fly));
    }
    // it waits beside the sign while she hides behind it, goes home under it once it is up, and is back beside it for her
    // to give it back
    if (t < q.fly + 150) { st.sprite = 'glow'; st.sp = ['at', sprite.x, sprite.y]; }
    else if (t >= lower) { st.sprite = t < lower + q.raise + q.back ? 'glow' : 'sHappy'; st.sp = ['at', sprite.x, sprite.y]; }
    else { st.sprite = 'sLook'; if (hide && t < lift + q.lift) st.sp = ['at', sprite.x, sprite.y]; }
    if (name === 'pkDouble' && t >= raised && t < lower) {
      st.fx.push(['sparkles', q.jump[0][0]]);
      // (the board measures the lift from the sign in her drawing holding it up, signUp, to the one in her jumping drawing)
      const [c, j] = ['signUp', 'signJump'].map(p => this.still(p)), k = H / c.ruler, drop = ((j.sign.y - j.ground) - (c.sign.y - c.ground)) * k;
      for (const [a, ms, h] of q.jump) {
        const lifted = h * Math.sin(Math.PI * k01(t, a, a + ms));
        if (t < a || t >= a + ms) continue;
        st.y -= lifted;
        if (lifted > h * 0.35) { st.pose = 'signJump'; st.y -= drop; }   // in the air: her legs tuck up, her hands and the sign go on
      }
    }
  }

  // B站 PK's 对手情报卡 (INTEL): as the 账单卡, the sprite flies to the card's top right corner and prints it out of
  // itself, glowing; it taps the card over (a nod) where it has two sides, and goes home down beside the card's right
  // edge. The card hangs over her head on the side shown, as tall as that side; it turns over edge on, its height too.
  // A 试演 shows the design package's usual one.
  intelShow(t, st, opts) {
    const q = INTEL, c = opts.intel || PK_INTEL.SAMPLES['普通'], L = PK_INTEL.layout(this.measureG, c);
    const both = L.front && L.back, turn = both ? ease(k01(t, q.turn[0], q.turn[1])) : L.front ? 0 : 1, h = L[turn < 0.5 ? 'front' : 'back'].h;
    const x = FOOT.x - PK_INTEL.W / 2, y = Math.max(40, FOOT.y - H - 4 - h);
    st.pose = at(t, [[0, 'surprised'], [600, 'hope'], ...(both ? [[q.turn[1], 'announce']] : []), [q.done, 'idle']]);
    if (t >= 1700 && t < q.turn[0]) st.fx.push(['sweat', 1700]);
    // between its trips to the card's corner it waits at her right, below the card, inside the page; it goes home only
    // after the turn, low in front of her (never across her face)
    const corner = ['at', x + PK_INTEL.W - 6, Math.max(SPRITE_H / 2 + 4, y - 4)], side = ['at', Math.min(x + PK_INTEL.W + 22, PAGE.w - 34), FOOT.y - 0.34 * H];
    if (t < q.print[1]) { st.sprite = t < q.print[0] ? 'sLook' : 'glow'; st.sp = corner; }
    else if (both && t >= q.turn[0] - 600 && t < q.turn[1] + 300) { st.sprite = t >= q.turn[0] - 100 ? 'nod' : 'sLook'; st.sp = corner; }
    else if (t < (both ? q.turn[1] + 1100 : q.print[1] + 800)) { st.sprite = 'sLook'; st.sp = side; }
    else st.sprite = both ? 'sHappy' : 'sLook';
    const reveal = t < q.print[0] ? 0 : t < q.print[1] ? ease(k01(t, q.print[0], q.print[1])) : 1 - ease(k01(t, q.roll[0], q.roll[1]));
    if (reveal > 0) st.intel = { c, x, y: y + 2 * Math.sin(t / 500), h, turn, reveal };
  }

  // B站 PK's result (PK_RESULT), with poses she has. Winning she cheers and thanks her viewers: BigWin with confetti and
  // two jumps, then, calming down, she hides her face at how loud she was; NarrowWin startled first (that close!). Draw: a
  // surprised blink, then a nod. Losing: NarrowLoss she stomps (so close), Lose she droops and her sprite pats her head,
  // BigLoss she squats drawing circles under a little rain cloud (Lose and BigLoss looked the same when both drooped); each
  // then bows her thanks, a second line (…After). Never gloating, never fierce.
  pkEndShow(t, st, opts, say, hop) {
    const r = opts.result || 'Win', two = this.lines[`pk${r}After`] && this.saying(`pk${r}After`, opts.seed ?? 0), end = EVENTS.pkResult.durs[r] - 400;
    say(150);
    const steps = {
      BigWin: [[0, 'cheer'], [2800, 'shy']], Win: [[0, 'cheer'], [1900, 'bow']], NarrowWin: [[0, 'surprised'], [700, 'cheer'], [2600, 'bow']],
      Draw: [[0, 'surprised'], [800, 'nod']], NarrowLoss: [[0, 'pout'], [1900, 'bow']], Lose: [[0, 'dejected'], [2000, 'bow']], BigLoss: [[0, 'gloomy'], [2700, 'bow']]
    }[r];
    st.pose = at(t, [...steps, [end, 'idle']]);
    const second = { BigWin: 2900, NarrowLoss: 2000, Lose: 2100, BigLoss: 2800 }[r];
    if (two && t >= second) say(second, two);
    if (r === 'BigWin') { hop(0, 16); hop(480, 16); st.fx.push(['confetti', 100], ['sparkles', 100]); if (t >= 2900) st.fx.push(['sweat', 3000, 1400]); }
    if (r === 'Win') { hop(0, 12); st.fx.push(['sparkles', 100]); }
    if (r === 'NarrowWin') { st.fx.push(['shock', 0], ['sweat', 100, 1400]); hop(700, 10); if (t >= 700) st.fx.push(['sparkles', 800]); }
    if (r === 'NarrowLoss' && t < 1300) { st.y -= 4 * Math.abs(Math.sin(t / 90)); st.fx.push(['anger', 100, 900]); }
    if (r === 'BigLoss') st.fx.push(['gloom', 0, 2600], ['rain', 200, 2400]);
    // its sprite: waves its flag for a win, looks up at a Draw, hides its face with hers, pats her head when she lost
    const win = ['BigWin', 'Win', 'NarrowWin'].includes(r), down = ['dejected', 'gloomy', 'pout'].includes(st.pose);
    if (st.pose === 'shy') { st.sprite = 'hide'; st.sp = ['rel', -0.04, 0.42]; }
    else if (st.pose === 'bow' || st.pose === 'nod') st.sprite = 'nod';
    else if (win) st.sprite = r === 'NarrowWin' && t < 700 ? 'surprised' : 'flag';
    else if (down && r !== 'NarrowLoss') { st.sprite = 'pat'; st.sp = 'head'; }
    else st.sprite = down ? 'hide' : 'sLook';
  }

  // While a PK is on, how it stands has changed (PK_GAP): a short show with poses she has. Overtake she is startled and
  // cheers with a jump; Overtaken startled, then a frustrated stomp, never fierce; PullAhead she claps under hearts;
  // FallBehind she droops and her sprite pats her head; CloseIn hands together, hoping, a few sparkles; CaughtUp nervous,
  // sweating.
  pkGapShow(t, st, opts, say, hop) {
    const r = opts.result || 'Overtake', end = EVENTS.pkSwing.durs[r] - 300;
    say(150);
    // (Overtaken goes from startled to stomping by her standing: the shortest way the board found went through opening a
    // blind box, a box in her hands for a moment)
    const steps = { Overtake: [[0, 'surprised'], [400, 'cheer']], Overtaken: [[0, 'surprised'], [1020, 'idle'], [1200, 'pout']], PullAhead: [[0, 'clap']],
      FallBehind: [[0, 'dejected']], CloseIn: [[0, 'hope']], CaughtUp: [[0, 'hope']] }[r];
    st.pose = at(t, [...steps, [end, 'idle']]);
    if (r === 'Overtake') { st.fx.push(['shock', 0]); hop(400, 14); if (t >= 400) st.fx.push(['sparkles', 450]); st.sprite = t < 400 ? 'surprised' : 'flag'; }
    if (r === 'Overtaken') { st.fx.push(['shock', 0], ['sweat', 200, 1000]); if (t >= 1200 && t < 2200) st.y -= 3 * Math.abs(Math.sin((t - 1200) / 90)); st.sprite = t < 1200 ? 'surprised' : 'hide'; }
    if (r === 'PullAhead') { st.fx.push(['hearts', 200, 3]); st.sprite = 'sHappy'; }
    if (r === 'FallBehind') { st.sprite = 'pat'; st.sp = 'head'; }
    if (r === 'CloseIn') { st.fx.push(['sparkles', 300]); st.sprite = 'sLook'; }
    if (r === 'CaughtUp') { st.fx.push(['sweat', 100, 1800]); st.sprite = 'sLook'; }
    if (t >= end) { st.sprite = 'sIdle'; st.sp = null; }
  }

  // Our double bonus challenge (from B站's capsule). Done: a surprised blink, then she claps for her viewers under
  // sparkles and hearts, her sprite waving its flag. Run out: she droops and her sprite pats her head, then she nods,
  // picking herself up (…After); never asking for more gifts.
  pkChallengeShow(t, st, opts, say) {
    const ok = (opts.result || 'Done') === 'Done', end = EVENTS.pkChallenge.durs[ok ? 'Done' : 'Failed'] - 300;
    say(150);
    if (ok) {
      st.pose = at(t, [[0, 'surprised'], [400, 'clap'], [end, 'idle']]);
      st.sprite = t < 400 ? 'surprised' : 'flag';
      if (t >= 400) st.fx.push(['sparkles', 450], ['hearts', 600, 3]);
    } else {
      st.pose = at(t, [[0, 'dejected'], [1600, 'nod'], [end, 'idle']]);
      const two = this.saying('pkChallengeFailedAfter', opts.seed ?? 0);
      if (t >= 1700) say(1700, two);
      if (t < 1600) { st.sprite = 'pat'; st.sp = 'head'; } else st.sprite = 'nod';
    }
    if (t >= end) { st.sprite = 'sIdle'; st.sp = null; }
  }

  // line: the show's own line; what she says as it goes wrong is a line of its kind (key).
  daily(t, st, o, line) {
    if (SPORTS[o.kind]) return this.sport(t, st, o, line);
    if (o.kind === 'gacha') return this.gacha(t, st, o, line);
    // humming: the sprite sways along; when her voice cracks it hides its head
    const main = DAILY[o.kind][0];
    st._t = t;
    const say = (from, key) => { if (t >= from) st.bubble = { text: key ? this.saying(key, o.seed + from) : line, t: t - from }; };
    if (t < main) { st.pose = 'hum'; st.fx.push(['notes', 0]); st.sprite = 'sHappy'; st.sp = ['home', 6 * Math.sin(2 * Math.PI * t / 1280), 0]; say(200); return st; }
    st.pose = 'crack'; st.sprite = 'hide'; st.fx.push(['brokenNote', main], ['shock', main], ['sweat', main + 300]); say(main, 'voiceCrack');
    return st;
  }

  // The 扭蛋 daily (GACHA): won (o.mishap false) the plush, lost an empty capsule. She moves only by hopping (never a slide)
  // and stands in front of the cabinet where each of her drawings at it has her (gachaX); the cabinet and what happens in
  // it are drawn by drawGacha behind her, the plush popping out of the capsule in front of her.
  gacha(t, st, o, line) {
    st._t = t;
    const T = GACHA.t, W = T.won, L = T.lost, k = H / this.art.anchor, im = this.props.gacha, left = GACHA.x - im.width * k / 2, top = FOOT.y - im.height * k;
    const at2 = (px, py) => ({ x: left + px * k, y: top + py * k });   // a point of the cabinet's picture on the page
    // each line for its own stretch only (until): the show is 14 s, and its first line hung over the whole of it
    const say = (from, until, key) => { if (t >= from && t < until) st.bubble = { text: key ? this.saying(key, o.seed + from) : line, t: t - from }; };
    const press = this.gachaX('gPress'), turn = this.gachaX('gTurn'), take = this.gachaX('gTake');
    // hopping from x0 to x1 between a and b in n hops
    const hops = (a, b, x0, x1, n) => { const w = k01(t, a, b); st.x = x0 + (x1 - x0) * w; st.y -= HOP * 0.45 * Math.abs(Math.sin(Math.PI * n * w)); };
    const end = o.mishap ? L.gone : W.gone;
    const g = st.gacha = { k, ...this.popped(t, T.up, end) };
    const centre = at2(im.width / 2, im.height * 0.45);
    // the sprite spins round where the cabinet comes (and goes), glowing, and it flashes there
    const spin = a => this.spinAt(st, t, a, centre);
    st.fx.push(['flash', T.up, 0.8, { x: centre.x, y: centre.y + 60 }], ['flash', end[0], 0.8, { x: centre.x, y: centre.y + 60 }]);

    // ---- she points home, the sprite fetches the battery and hands it over, spins the cabinet up
    if (t < T.fetch) { st.pose = 'gPointHome'; st.sprite = 'nod'; }
    // it holds the battery out until it goes into her hands, her in-between's frame 3 (90 ms on): its drawing fades over
    // 150 ms, so it lets go 60 ms before, and the two batteries are never seen at once
    else if (t < T.give) { st.pose = 'gRub'; if (t < T.back) { st.sprite = 'sHappy'; st.sp = ['at', -80, FOOT.y - 0.5 * H]; } else { st.sprite = t < T.give - 300 ? 'sCarry' : t < T.give - 60 ? 'sGive' : 'sHappy'; st.sp = ['rel', 0.05, 0.36]; } }
    else if (t < T.spin) { st.pose = 'gBattery'; st.sprite = 'sHappy'; st.sp = ['rel', 0.05, 0.36]; }
    else if (t < T.hop[0]) { st.pose = 'gBattery'; spin(T.spin); }
    say(200, T.up);
    if (t < T.hop[0]) return st;

    // ---- at the cabinet: she hops over with the battery, presses it in, turns the knob, squats for the capsule
    st.slide = true;   // her drawings at the cabinet stand where each has her: the in-betweens move her between them
    if (t < T.press) { st.pose = 'gBattery'; hops(T.hop[0], T.hop[1], FOOT.x, press, 2); }
    else if (t < T.turn) { st.pose = 'gPress'; st.x = press; }
    else if (t < T.take) { st.pose = 'gTurn'; st.x = turn; }
    else if (t < T.stand) { st.pose = 'gTake'; st.x = take; }
    else if (t < T.home[1]) { st.pose = 'gCapsule'; if (t < T.home[0]) st.x = take; else hops(T.home[0], T.home[1], take, FOOT.x, 2); }
    if (t >= T.drop) g.bay = { y: 14 * ease(k01(t, T.drop + 150, T.drop + 550)), a: 1 - k01(t, T.drop + 350, T.drop + 550) };   // in, and down out of sight
    if (t >= T.turn && t < T.out) { const u = t - T.turn; g.knob = u / 260; g.jig = [2.2 * Math.sin(u / 40), 1.6 * Math.sin(u / 55)]; g.shake = 0.8 * Math.sin(u / 35); }
    if (t >= T.out && t < T.take + 180) g.cap = { y: -(this.art.points.gacha.outlet[1] - this.art.points.gacha.win[3]) * (1 - ease(k01(t, T.out, T.out + 220))) - 10 * bump(t, T.out + 220, T.out + 380) };
    // as she hops over it goes ahead to the top of the cabinet's sign (following her, it crossed her face)
    if (t < T.turn) { st.sprite = 'sHappy'; st.sp = ['at', left + 25 * k, top + 60 * k]; }
    else if (t < T.out) { st.sprite = 'sPeek'; st.sp = ['at', left + 175 * k, top + 160 * k + 2 * Math.sin(t / 120)]; st.fx.push(['sweat', T.turn + 600, 900]); }
    else if (t < T.home[1]) { st.sprite = 'sHappy'; st.sp = ['rel', 1.0, 0.25]; }
    if (t < T.shake) return st;

    // ---- back at her place: she shakes the capsule by her ear (the sprite listens) and twists it open
    st.slide = false;
    if (t < T.twist) { st.pose = 'gShake'; st.sprite = 'sListen'; st.sp = ['rel', 1.02, 0.1]; return st; }
    if (t < T.open) { st.pose = 'gTwist'; st.sprite = 'sHappy'; st.sp = ['rel', 1.0, 0.3]; st.fx.push(['shock', T.twist + 200]); return st; }

    if (!o.mishap) {
      // the plush pops out of the capsule and into her arms; she hugs it, hopping; the sprite circles her, spins the cabinet
      // away, takes the plush and flies it home
      // the plush: out of her hands, growing as it rises over her head with a little wobble, hanging there bobbing, then
      // down into her arms where her drawing hugs it (points.gacha.hug, measured on the gHug cell), fading into it
      const u = t - T.open, [hugX, hugY] = this.art.points.gacha.hug;
      if (u < W.down + 60) {
        const r = 1 - Math.pow(1 - k01(u, 0, W.rise), 3), d = ease(k01(u, W.hang, W.down));
        const hx = FOOT.x + 14 + 18 * r, hy = FOOT.y - 0.3 * H + (FOOT.y - H - 10 - (FOOT.y - 0.3 * H)) * r + (u >= W.rise ? 4 * Math.sin((u - W.rise) / 60) : 0);
        st.pop = { x: hx + (FOOT.x + hugX - hx) * d, y: hy + (FOOT.y - hugY - hy) * d, s: 0.25 + 0.75 * r, a: 1 - k01(u, W.down, W.down + 60) };
        if (u < W.rise) st.fx.push(['sparkles', T.open]);
      }
      st.halves = this.halves(t, T.open, 'pop', 1 - k01(t, W.gone[0], W.gone[1]));
      st.pose = t < W.take ? 'gHug' : 'idle';
      // the sprite watches from her side while the plush is in the air (tossed from the start, it hung over her head
      // right where the plush comes), and is tossed for joy once she hugs it
      if (t < W.hug) { st.sprite = 'surprised'; st.sp = ['rel', 1.05, 0.3]; }
      else if (t < W.spin) { st.y -= 12 * bump(t, W.hug + 300, W.hug + 620) + 10 * bump(t, W.hug + 900, W.hug + 1220); this.toss(st, t, W.hug, 900); }
      else if (t < W.gone[1]) spin(W.spin);
      else if (t < W.away) { st.sprite = t < W.take ? 'sHappy' : 'sPlush'; st.sp = ['rel', 0.5, 0.42]; }
      else { st.sprite = 'sPlush'; st.sp = ['at', -90, FOOT.y - 0.55 * H]; }
      st.fx.push(['sparkles', T.open], ['confetti', T.open + 100]);
      say(W.hug, W.spin, 'gachaWin');
      return st;
    }
    // empty: frozen over the halves; she drops them and wails on her knees (the sprite pats her head), wipes her tears and
    // gets up; the sprite spins the cabinet and the halves away
    st.pose = at(t - L.empty, [[0, 'gEmpty'], [L.wail - L.empty, 'gWail'], [L.wipe - L.empty, 'gWipe'], [L.idle - L.empty, 'idle']]);
    if (t >= L.wail) st.halves = this.halves(t, L.wail, 'drop', 1 - k01(t, L.gone[0], L.gone[1]));
    if (t < L.pat) { st.sprite = 'surprised'; st.sp = ['rel', 1.0, 0.3]; }
    else if (t < L.wipe) { st.sprite = 'pat'; st.sp = 'head'; }
    else if (t < L.gone[1]) spin(L.spin);
    else st.sprite = 'sHappy';
    st.fx.push(['shock', L.empty], ['sweat', L.empty + 300, 900]);
    if (t >= L.wail) st.fx.push(['tears', L.wail + 100, L.wipe - L.wail]);
    say(L.empty + 100, L.wail, 'gachaEmpty'); say(L.wail + 150, L.wipe, 'gachaMiss'); say(L.wipe + 150, L.end, 'gachaCheerUp');
    return st;
  }

  // The sprite glowing round the place where a prop comes (or goes) from time a, leaving a trail of stars.
  spinAt(st, t, a, centre) {
    const ang = (t - a) / 130;
    st.sprite = 'glow'; st.sp = ['at', centre.x + 78 * Math.cos(ang), centre.y + 26 * Math.sin(ang)]; st.fx.push(['trail', a]);
  }

  // Where a point of a fixed sheet's cell (its px, as in HOTPOT and MOBILE) is on the page when her spot is (x, y).
  fromSpot(sheet, [px, py], x, y) {
    const c = this.cells[sheet][0], k = H / c.ruler;
    return { x: x + (px - c.cx) * k, y: y + (py - c.ground) * k };
  }

  // A prop the sprite brings with a pop (from up) and takes away with a shrink (gone): its alpha and its scale about its
  // bottom middle, as the 扭蛋 cabinet does.
  popped(t, up, gone) {
    return { a: k01(t, up, up + 200) * (1 - k01(t, gone[0], gone[1])), s: 0.85 + 0.15 * ease(k01(t, up, up + 300)) + 0.06 * bump(t, up + 150, up + 450) - 0.3 * ease(k01(t, gone[0], gone[1])) };
  }

  // The capsule's two halves leaving her hands at a: popping off to either side as the plush comes out (pop), or slipping
  // from her hands as she sinks to her knees (drop); both come to lie by her feet, where they stay till the cabinet goes.
  halves(t, a, how, alpha) {
    const u = t - a, p = k01(u, 0, how === 'pop' ? 450 : 260);
    return { a: alpha, pts: [[-1, -6, -52], [1, 14, 58]].map(([half, sx, tx]) => {
      const x0 = FOOT.x + sx, y0 = FOOT.y - 0.3 * H, x1 = FOOT.x + tx, y1 = FOOT.y - 2;
      const y = how === 'pop' ? y0 + (y1 - y0) * p - 70 * Math.sin(Math.PI * p) : y0 + (y1 - y0) * p * p;
      // both end dome up, on their rims: the pink top half is dome up as it is (0), the white bottom half turned over (π);
      // popping off they tumble on the way (a whole turn, a turn and a half), dropped they only tip a little
      const rot = how === 'pop' ? (half < 0 ? -2 * Math.PI * p : 3 * Math.PI * p) : (half < 0 ? 0.25 * p : Math.PI + 0.3 * p);
      return { half, x: x0 + (x1 - x0) * p, y, rot };
    }) };
  }

  // Where she stands (her drawing's centre) for a drawing of hers at the cabinet, so the cabinet in its cell meets the one on
  // the page: the cell's cabinet edge at points.gacha.cell, the page's at the cabinet picture's left.
  gachaX(pose) {
    const c = this.cells.gacha22?.[this.art.poses[pose][1]], k = H / this.art.anchor, left = GACHA.x - this.props.gacha.width * k / 2;
    return c ? left - (c.ox + this.art.points.gacha.cell - c.cx) * H / c.ruler : FOOT.x;
  }

  // A sport (SPORTS): the sprite joins in (cheers her lifts with its flag, leads her hike, flies beside her bike or skis); if
  // it goes wrong it starts, then rubs her foot or pats her head, and nods along while she puts a plaster on bare skin.
  sport(t, st, o, line) {
    st._t = t;
    const sp = SPORTS[o.kind], go = 1100, stop = go + sp.act[1], end = stop + (o.mishap ? sp.fallMs : sp.calmMs) + 900, back = end - 900;
    const say = (from, key) => { if (t >= from) st.bubble = { text: key ? this.saying(key, o.seed + from) : line, t: t - from }; };
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
    // going wrong, the last of its lines that has come (each picked once)
    if (o.mishap && t >= stop) { const [ms, key] = sp.lines.findLast(([ms]) => t >= stop + ms); say(stop + ms, key); }
    else say(1300);
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
    const climb = (a, b, from, to) => { const w = k01(t, a, b), hopN = Math.floor(w * 3), u = w * 3 - hopN; return { x: from.x + (to.x - from.x) * w, y: from.y + (to.y - from.y) * w - HOP * Math.sin(Math.PI * Math.min(1, u)) }; };
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

  // When her line in chestKept or pranksterKnocked may come (show time): once the sprite has lifted the chest higher than
  // where her bubble comes (it waits for the 捣蛋鬼 to be clear of the chest first, CLEAR, so not at a set time), 1.9 s at
  // the soonest.
  chestUp(t, opts) {
    const k = this.lineWait?.seed === opts.seed ? this.lineWait : (this.lineWait = { seed: opts.seed, at: null }), o = this.gm.out;
    if (k.at == null && (!o || o.got != null && this.now() - o.got >= 0.4 * (o.ms - o.t2))) k.at = Math.max(1900, t);
    return k.at ?? Infinity;
  }

  // A show that scared the 捣蛋鬼 off a tug after the lightning waits for her hair: she cheers frizzed while the sprite takes the
  // chest home and comes back to pat it smooth (smooth), and the show starts once it has settled (c.pre). With no show after
  // (a PK, a show turned off) it is the same (gm.fix). One taking over 被偷了 before her hair was smooth has her go on kneeling
  // there (pose robbed), the sprite on her head patting it as in 被偷了, never turning to fly. A gift's has it the other way
  // round (kept): the sprite, right by her, smooths her hair first while the 捣蛋鬼 runs off (the chest waits, gm.out.wait),
  // then takes the chest home, and the show starts once both are done.
  unfrizz(st, t, c, pose) {
    const robbed = pose === 'robbed', fixed = this.smooth(st, t, { seed: -c.start }, pose + 'Zap', pose, 0, FRIZZ, robbed ? 'pat' : 'sHappy'), o = this.gm.out;
    st.pose = fixed ? pose : pose + 'Zap';
    if (robbed) st.sStay = true;
    if (fixed && o?.wait) o.wait = false;
    if (fixed && t >= this.pat.fix + SETTLE && !(c.opts?.kept && o)) c.pre = t;
  }

  // After the lightning her hair stays frizzed (pose zap) until her sprite pats it smooth: once the sprite is really on her
  // head (sTarget, where it was flying to, and not taking the chest home) it pats it, the last two pats harder (PATS), and as
  // the second lands her hair settles (pose plain, through its slow in-betweens) with a spark under its paw, stars having
  // gathered round her head (hairMagic) to fall down her hair as glitter; then it does after ('pat' goes on patting her head,
  // anything else back by her side). Not before min, nor later than 700 ms before the show's end (dur) if the sprite never
  // got there. Returns whether her hair is itself again.
  smooth(st, t, o, zap, plain, min, dur, after) {
    const p = this.pat?.seed === o.seed ? this.pat : (this.pat = { seed: o.seed, at: null }), sv = this.sv, to = this.sTarget;
    if (p.at == null && (!this.gm.out || this.gm.out.wait) && to?.sp === 'head' && sv && Math.hypot(sv.x - to.x, sv.y - to.y) < 12) p.at = t;
    const fix = p.fix = Math.min(Math.max(min, (p.at ?? Infinity) + PATS), dur - 700 - SETTLE);
    const b = this.herBox(st, this.cell(zap)), a = this.herBox(st, this.cell(plain)), paw = { x: b.x + b.w * 0.25 + 20, y: b.y - SPRITE_H * 0.2 + 14 };
    st.fx.push(['spark', fix, (paw.x - a.x) / a.w, (paw.y - a.y) / a.h, 8]);
    // the stars round her head, given across whichever drawing she is in this frame (they stay at her head as it changes)
    const hair = this.art.hair[plain], h = { x: a.x + a.w * hair[0], y: a.y + a.h * hair[1] }, now = t < fix ? b : a;
    st.fx.push(['hairMagic', fix - GATHER, (h.x - now.x) / now.w, (h.y - now.y) / now.h, GATHER]);
    // its paw comes down on her head (patPhase) just as the hair is fixed
    Object.assign(st, { sStay: p.at != null && (t < fix || after === 'pat'), sprite: t < fix ? 'pat' : after, sp: t < fix || after === 'pat' ? 'head' : null, patPhase: t - fix + 90 * Math.PI, patHard: t >= fix - PATS && t < fix });
    return t >= fix;
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
      .replaceAll('{连守}', String(opts.streak ?? 1)).replaceAll('{关键字}', opts.keyword ?? LOTTERY_TRIAL.keyword).replaceAll('{奖项}', opts.award ?? LOTTERY_TRIAL.award).replaceAll('{奖品}', opts.prize ?? LOTTERY_TRIAL.prize);
  }
}
