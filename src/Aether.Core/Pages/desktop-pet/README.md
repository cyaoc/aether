# 桌宠页

The 桌宠 (GLOSSARY) plays on this page, a browser source in a corner of the stream. Core tells it what happens on
`/desktop-pet/events` (`DesktopPet.cs`); the page decides what to play.

## shows.json, the 演出表

Every 演出 of the 桌宠, her 日常小动作 and 心情, in one table that Core (`ShowTable.cs`), the GUI's 桌宠 page and this page all
read; the page fetches it as `/desktop-pet/shows.json`. A new 演出, such as PK, is a few more rows here and her lines in each
character's `character.json`; settings know it by its id and play it unless it is turned off, so it needs no settings upgrade.

| Section | What |
|---|---|
| `groups` | `id`, `label` and, for a group of 演出, `note`: the groups the GUI lists 演出 and kinds of lines under, in this order (`mood`, `daily` and `mishap` hold 心情, 日常小动作 and their mishaps' lines; `chestGame` and `prankster` hold hers and the 捣蛋鬼's in a 宝箱小游戏 来访) |
| `shows` | A row for each 演出, 日常小动作 and 心情: `id`; `group`; `label`; `tier`, its 演出档 (`SS`, `S`, `A`, `B`, `C`, `D`); `lines`, the ids of the kinds of lines it says; `tests`, what a 试演 of it chooses, each a key of `variants` or `null` for the row itself, none for a row that has no 试演; and, for a 演出 the GUI switches by group, `note`, when it plays, where `{mid}`, `{big}` and `{big-1}` stand for the 中礼物 and 大礼物 thresholds in 电池 |
| `lines` | A row for each kind of line: `id`, which settings and `character.json` key lines by; `group`; `label`; `note`, when she says it; `placeholders`, the names a line may hold in braces |
| `variants` | The label of each 试演 variant: a blind box's ending (`Gain`, `Even`, `Loss`, as Core tells a 一轮盲盒's result, and `Jackpot`), a 日常小动作's `Mishap`, a lottery's step |

Rows of the group `mood` cannot be turned off: a 心情 comes of how quiet the room has been, and `desktop_pet.*_minutes` of 0
keeps her out of it. Nor can the group `chestGame`, the endings of a 宝箱小游戏 来访 (`pranksterKnocked` on an empty chest,
`chestKept`, `chestRobbed`): a 来访 plays whole or not at all, so they have no 试演 and the GUI's 演出 tab does not list them.
What she and the 捣蛋鬼 say while it is on the page (`chestFell`, `pranksterHit`, `herHit` and the group `prankster`) is said in
no row: a 来访 is no 演出. Every other row can be turned off, by its id in `desktop_pet.shows_off`.

## What the page is told

The `state` event, while the 桌宠 is on, carries `settings` and `lines` beside the room, the lottery and the 礼物榜, and comes
again whenever settings are saved:

| Field | What |
|---|---|
| `settings.off` | Ids of the rows turned off: a 演出 turned off does not play, nor does a lower one in its place |
| `settings.midGift`, `settings.bigGift` | The 中礼物 and 大礼物 thresholds in 金瓜子, as the gift rounds' amounts are; blind box rounds use them too |
| `settings.boredMinutes`, `settings.gloomyMinutes`, `settings.sleepyMinutes` | Quiet minutes before 无聊, 沮丧 and 犯困; 0 never |
| `settings.dailyMinSeconds`, `settings.dailyMaxSeconds` | How long she waits, at random between the two, before each 日常小动作 |
| `settings.mishapPercent` | The chance, 0 to 100, that a 日常小动作 goes wrong |
| `settings.pranksterMinutes` | The most minutes between the 捣蛋鬼's own 来访, counted from the last one's end; 0 only when the streamer 叫它来 |
| `settings.pranksterKeyword` | The 帮它的关键字, which its 招人 line (`{关键字}`) and the line under the tug's bar say |
| `lines` | The current character's lines by kind: hers, with each kind the streamer changed replaced |
| `pk` | Whether the streamer is in a PK, by the PK messages' commands alone (`DanmakuProtocol.cs`). One that ends a PK ends it, as do ten minutes without any PK message and a reconnect, which cannot tell |

A `gift` tells a viewer's 一轮送礼 by the gifts of each message, and once more as it `ended`, with none: the page makes her
round of those it does not throw at the 捣蛋鬼. Its gifts and a `guard` carry what the 宝箱小游戏 counts, by what the viewer
paid, as the chest does:

| Field | What |
|---|---|
| `gift`'s `items` | The viewer's gifts in the message: each its `giftId`, `name` and `price` of one, `spend` in 金瓜子 (a 盲盒's is the box's price, not what it opened), `count`, whether it is `blind` and whether that opened a 浪漫城堡 (`castle`), and `iconUrl`; a 盲盒's id, name, price and icon are those of the gift it opened, the icon empty when B站 gave none. Her round's gift priced highest is the first of those of the highest `price`, and how many of it all of its `giftId`: the same rule as Core's 一轮送礼 (`GiftRoundFeature`) |
| `guard`'s `spend` | What the 大航海 cost in 金瓜子, for all its months |

A `danmaku` tells only `uid`, `nickname` and `joins`, whether it is the 帮它的关键字 saved last (Core compares the two trimmed, as
a 报名抽奖 compares its keyword): what a viewer wrote never reaches the stream. The account's own danmaku, the bot's thanks,
welcomes and 盲盒播报 coming back from B站, are not told. `summon` is the streamer's 叫它来: the 捣蛋鬼 comes once the show she
plays is over, and the blind boxes she shakes for their 盲盒播报 opened. The page reports the 捣蛋鬼 setting off, called or on its own, with `POST /desktop-pet/report?visit=start`, and
leaving with `?visit=end`, also when it leaves before it set off, as when a PK starts or the room disconnects; from these Core
tells the GUI whether it is on its way or on the page.

## The page

Design source: the desktop pet design package's board `docs/design/desktop-pet/boards/DP-DesktopPet.dc.html`, kept in step
with this page (`DesktopPetBrowserTests` compares the two, show by show, pixel for pixel).
Its engine is `engine.mjs` (which show plays when, the 一轮送礼, the 心情 and 日常小动作, what she and her sprite do at a
moment) and its drawing `pet.mjs` and `effects.mjs`, ported function for function on one Canvas 2D; `page.mjs` hands each
event above to the engine's handler of the same name. The board's numbers measured on her pictures are the current
character's `desktopPet` (`../characters/README.md`); the shows' timings, effects and the sprite's ways stay here, the same for
every character. The page plays every row of the 演出表: the 心情, the 日常小动作 and their mishaps, entries, gifts, blind boxes,
castles, 大航海, a new first place on the 礼物榜 (found by the 礼物榜页's `../gift-board/changes.mjs`) and each step of a lottery;
and the 宝箱小游戏 (below).

Her speech bubbles and the effects' marks are in the 礼物榜页's Noto Sans SC (`../gift-board/fonts/noto-sans-sc.css`, `SANS`
of `../gift-board/paint.mjs`), loaded before she says anything; the stream's PC need not have the board's PingFang SC.

The page is 480 × 640 design px, scaled with the browser source and standing on its bottom edge, and draws her pictures at
their own resolution. It loads her pictures and props at once but for the sheets of the 日常小动作, the castle, 大航海 and the
lottery, each with its strips when it first plays (the lottery's too when she first waits through one), and the 宝箱小游戏's,
hers and the 捣蛋鬼's, when it is first to come: all of them decoded take about 574 MB, the game's more. A pose whose sheet is
still loading keeps her in the drawing she has.

## The 宝箱小游戏

The design package's README 12 and its board with `game` 开 are the game; the page plays it as the board does, its engine in
`engine.mjs` (the 来访's clock, `gameUpdate`, and where everything is at a moment, `gameState`) and its drawing in `pet.mjs`
(`drawGame`). Nothing of it is drawn while the 捣蛋鬼 is away, and her shows are as they were.

The 捣蛋鬼's drawings, its loops and in-betweens, and the chest's are the same for every character, in `prankster/`; its
`prankster.json`:

| Field | What |
|---|---|
| `anchor` | How tall the standing drawing the chest was drawn beside is, in the chest's px |
| `sheets`, `poses`, `strips`, `transitions`, `frames` | Its pose sheets (3 × 2, the first cell its idle drawing, the ruler), its poses' `[sheet, cell]`, its loops and its in-betweens, as a character's |
| `cuts` | Its changes of pose with no in-betweens, where it simply switches (it ducks out of sight, falls, tumbles, is knocked away; turning round it always switches) |
| `props` | The chest, shut and open, and the ring picture each of its two handles is |
| `ring` | In the chest's px: the link a ring hangs from, in from the chest's side and up from its foot; the point of the ring's picture it hangs from; from there to the middle of its band on the far side |
| `lid` | Where her sprite takes hold of the open lid to pull with her, in from the chest's side and up from its foot |

Her chest at home, 连守 and what the 捣蛋鬼 remembers (who hit it last, whether it got away with the chest) live in the page
alone: they start from nothing in a room and are gone when it disconnects, the page reloads or the 桌宠 is switched off. Amounts
come from Core in 金瓜子 and are counted in 电池, as the board says them.

The page's checks against the board, for whoever changes it or her pictures. The design package's `tools/server.mjs` serves
the page beside the board, as the web service serves it, telling it it is on in a connected room with the default settings:
`node docs/design/desktop-pet/tools/server.mjs 8820`.

- `DesktopPetCharacters.test.mjs` (`node --test`) finds every change of pose in her shows with no way across, and on the
  宝箱小游戏's routes every change of hers with none and of the 捣蛋鬼's with neither in-betweens nor a cut written down, and
  any picture `prankster.json` names that is not there. `DesktopPetGame.test.mjs` checks when the 捣蛋鬼 comes and goes, what
  scares it off and what goes into her chest.
- `scripts/desktop-pet/compare.mjs --port 8820` plays every show, 心情 and round, and the 宝箱小游戏's routes of the design
  package's `tools/check-game.mjs` (played on the page by `scripts/desktop-pet/routes.mjs`), on the page and the board from one
  seed, frame by frame on one clock, and compares their pixels. `DesktopPetBrowserTests` runs it, about a minute, when
  `AETHER_TEST_CHROME` is set.
- `node docs/design/desktop-pet/tools/motion-scan.mjs 8820 --page` finds the sprite moving more than 20 px in a frame. It plays
  every show in real time, about eleven minutes, too long for a test: run it after changing the sprite's flight, its circling, a toss or
  the pace of a round.
- `node docs/design/desktop-pet/tools/play.mjs <show[:variant]> <ms,…> <out.png> --page [--line]` grabs the page at moments
  of a 试演 playing in real time.

## Deliberate differences from the board

- Core tells the page what happens, so the board's buttons, scenes, caption, fast-forward, stand-in stream behind the page and
  stand-in 盲盒播报 are gone. A 一轮送礼 is Core's: one for each viewer, several at once, over when Core says, and its blind boxes
  open when their result comes. The board kept one round and ended it on its own timers. One 盲盒播报 reports every box the
  viewer sent before it went out, over however many rounds, and none sent while it went out: its result opens that many of
  the viewer's boxes, oldest first, and the rest are shaken on until their own.
- A gift message, and a 大航海, counts once for 开心. The board counted a round's gifts as they came and its show again when
  it started, so two gifts in a minute made her happy.
- She does no 日常小动作 while she shakes a round's blind boxes. Waiting for the 盲盒播报 takes up to half a minute; the board's
  stand-in took 0.8 s and a 日常小动作 could start in it.
- The thresholds, the minutes before each 心情 (0 never), the 日常小动作's interval, which of them she does and their chance of a
  mishap, the shows turned off and her lines are the state's; the board's are fixed. Without a room she stays calm, and her
  quiet-room clocks start when one connects.
- A castle in a 一轮送礼 stops its gift and blind box shows and waits for its blind boxes' result as they do: B站 gives a castle
  only out of a 盲盒, so a castle sent as a gift is that gift. The board also played a castle sent directly, and a blind box
  show after it in the same round.
- The lottery is Core's state, so its steps play as its phase changes and she waits through it by that phase (entries open, a
  draw coming), not by the steps the board's buttons started: a 试演 of a step leaves her as she was. She reacts to a draw at
  its DrawUntil, when the 抽奖页's effects end; its show, which holds her breath 2.5 s first, starts that much earlier. A pool
  lottery has no end of its own, so it ends as a new lottery replaces it, and between draws she does her 日常小动作. The viewer a
  pool calls is named by UID, all its state has, as the 抽奖页 names a winner without a nickname.
- A new first place on the 礼物榜 is crowned after the show of the 一轮送礼 that took it, its blind boxes' once the 盲盒播报 opens
  them; the board's gift said it took first place. The 礼物榜 reaches the page another way than that gift and may come first:
  the crown then waits for the gift's round. As on the board, a 大航海 is not crowned.
- A 试演 starts its show at once, or holds her in its 心情, until it is stopped; stopping it never stops a show of what happened.
  A show's 试演 played out, or cut short by a show of what happened, is reported with `POST /desktop-pet/report?trial=` and the
  number Core told it with, as the video page reports what it played, and the GUI offers another; a page reporting one after
  the next started ends nothing, and a 试演 whose pages all went away (the streaming software reloaded it) is over too.
- Shows, 心情, 日常小动作, endings, lottery steps and kinds of lines go by their ids in the 演出表 rather than the board's Chinese
  names, and her lines are picked with `Math.random`, not the board's fixed seed.
- The 宝箱小游戏 comes on its own every `pranksterMinutes`, counted from the room connecting, never at 0, or when the streamer
  叫它来; the board's game was on or off and came every 30 minutes. It sets off once its pictures are there and only in a
  connected room: one due when the room disconnects does not come, and one here runs off as at a PK, its 来访 counting for
  nothing, what was still to land too. The page reports it setting off and leaving. What it and she say in a 来访 are her
  character's lines of the groups `chestGame` and `prankster` (a space breaks the 捣蛋鬼's line), the 帮它的关键字 the saved
  one; the board said fixed lines.
- A gift message's gifts are thrown at the 捣蛋鬼 one by one, each with B站's icon, a little box while it loads or has none;
  the board threw boxes. As the board's, a viewer's 一轮送礼 is only what they did not throw, and a first place taken while it is
  here is not crowned; a 盲盒播报 of boxes thrown opens none. A viewer's boxes, thrown or not, are told by their 盲盒播报 in the
  order they came, so boxes sent after the 来访 wait for their own, and one telling both plays those not thrown, as many and at
  what they cost; its ending, jackpot and gift are the whole report's, which tells what its boxes opened only all together.
  A castle scares it off when its 盲盒播报 comes; the board's stood in for it 3.8 s after the box. A 一轮送礼's castle plays
  once, thrown or not: one thrown in a round that played a castle before the 来访 plays nothing and lands as any gift. The
  board's round ends with its 盲盒播报, so there a castle thrown after it plays again.
- What a viewer's 一轮送礼 throws at the 捣蛋鬼 beats it off once it comes to the 中礼物 threshold in all, whatever life helpers
  gave it back; a gift counts to the round it was thrown in however late it lands, and the round's gifts sent before the 来访
  do not count. The board has one viewer and no rounds of what it threw: there a single gift of the threshold does it.
- Every gift thrown at the 捣蛋鬼 goes into her chest, playing nothing once it is on its way off: one landing after the gift that
  beat it off or after it was scared off, and one the lightning held back from falling when it is gone. The board's were lost.
- A viewer is on the 捣蛋鬼's side once they send the 帮它的关键字 in the tug, counted once however often they send it, and
  their gifts are a helper's; the board's buttons made any gift a helper's, and every keyword another helper.
- A 试演 and a show turned off scare it off too, a PK as the board's button did, and a lottery is drawn, keeping it away, by
  `lotOf` whether its show is on or not.
