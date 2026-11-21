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
| `variants` | The label of each 试演 variant: a blind box's ending (`Gain`, `Even`, `Loss`, as Core tells a 一轮盲盒's result, and `Jackpot`), a 盲盒查询's result (the same three and `None`, no record), a 日常小动作's `Mishap`, a lottery's step |

Rows of the group `mood` cannot be turned off: a 心情 comes of how quiet the room has been, and `desktop_pet.*_minutes` of 0
keeps her out of it. Nor can the group `chestGame`, the endings of a 宝箱小游戏 来访 (`pranksterKnocked` on an empty chest,
`chestKept`, `chestRobbed`): a 来访 plays whole or not at all, so they have no 试演 and the GUI's 演出 tab does not list them.
What she and the 捣蛋鬼 say while it is on the page (`chestFell`, `pranksterHit`, `herHit`, `herPelted` and the group
`prankster`) is said in no row: a 来访 is no 演出. Every other row can be turned off, by its id in `desktop_pet.shows_off`.

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
| `settings.luckyPercent` | The chance, 0 to 100, that a 纸团 thrown in the tug is a 幸运纸团; 0 never |
| `lines` | The current character's lines by kind: hers, with each kind the streamer changed replaced |
| `pk` | Whether the streamer is in a PK, by the PK messages' commands alone (`DanmakuProtocol.cs`). One that ends a PK ends it, as do ten minutes without any PK message and a reconnect, which cannot tell |

A `gift` tells a viewer's 一轮送礼 by the gifts of each message, and once more as it `ended`, with none: the page makes her
round of those it does not throw at the 捣蛋鬼. Its gifts and a `guard` carry what the 宝箱小游戏 counts, by what the viewer
paid, as the chest does:

| Field | What |
|---|---|
| `gift`'s `faceUrl` | The viewer's face as the message carries it, empty when it has none and as the round `ended` |
| `gift`'s `items` | The viewer's gifts in the message: each its `giftId`, `name` and `price` of one, `spend` in 金瓜子 (a 盲盒's is the box's price, not what it opened), `count`, whether it is `blind` and whether that opened a 浪漫城堡 (`castle`); a 盲盒's id, name and price are those of the gift it opened. Her round's gift priced highest is the first of those of the highest `price`, and how many of it all of its `giftId`: the same rule as Core's 一轮送礼 (`GiftRoundFeature`) |
| `guard`'s `spend` | What the 大航海 cost in 金瓜子, for all its months; a push B站 repeats (the same `Tid`) is told once |

A `blindBoxQuery` tells a viewer's 盲盒查询 as its reply went out, sent or given up (a first rate limit waits for its retry),
for the 账单卡: `uid`, `nickname`, `period` (今日, 本月 or 今年), `result` (`Gain`, `Even`, `Loss`, or `None` with no blind box
recorded) and `spend` and `profit`, the 投入 and the 盈亏 without its sign as the reply writes them (`BlindBoxTally.Shown()`),
null for `None`. She shows it only while she is idle, as she waves at an entry: never over a show or blind boxes she shakes,
never queued, never while the 捣蛋鬼 is here; and any gift cuts it short at once, blind boxes and a round's gift that plays
nothing too.

A `danmaku` tells only `uid`, `nickname` and `side`, the 站队 it takes: 1, hers, when all of it is "1", -1, the 捣蛋鬼's, when
it is "2", else 0 (Core compares it trimmed, as a 报名抽奖 compares its keyword): what a viewer wrote never reaches the
stream. The account's own danmaku, the bot's thanks,
welcomes and 盲盒播报 coming back from B站, are not told. `summon` is the streamer's 叫它来: the 捣蛋鬼 comes once the show she
plays is over, and the blind boxes she shakes for their 盲盒播报 opened. The page gives itself an id as it loads, asks for its
events with `/desktop-pet/events?page=` and that id, and reports the 捣蛋鬼 setting off, called or on its own, with
`POST /desktop-pet/report?visit=start&page=` and the id, and leaving with `visit=end`, also when it leaves before it set off,
as when a PK starts or the room disconnects; from these Core tells the GUI whether it is on its way or on the page. A page
gone, as the streaming software reloaded it, no longer has it there. Each such report also says `run=`, the `run` of the
state the page was told last: how many times the 桌宠 was turned off. Core takes none from before it was last turned off,
however late it comes, as the page forgot that 来访. The page sends its reports one after another, each once Core has
answered the one before, and one that got no answer, or a 5xx, again a second later until it gets one. Each time its events
reconnect, Core has forgotten the 捣蛋鬼 on it, and the page reports it setting off again with the first state it is told, or,
the one 叫它来 called not yet set off, `visit=called`: it is on its way. A state of another `run` than the page had tells it
the 桌宠 was turned off while its events were down: it forgets all first, as when it is switched off.

## The page

Design source: the desktop pet design package's board `docs/design/desktop-pet/boards/DP-DesktopPet.dc.html`, kept in step
with this page (`DesktopPetBrowserTests` compares the two, show by show, pixel for pixel).
Its engine is `engine.mjs` (which show plays when, the 一轮送礼, the 心情 and 日常小动作, what she and her sprite do at a
moment) and its drawing `pet.mjs` and `effects.mjs`, ported function for function on one Canvas 2D; `page.mjs` hands each
event above to the engine's handler of the same name. The board's numbers measured on her pictures are the current
character's `desktopPet` (`../characters/README.md`); the shows' timings, effects and the sprite's ways stay here, the same for
every character. The page plays every row of the 演出表: the 心情, the 日常小动作 and their mishaps, entries, gifts, blind boxes,
castles, 大航海, a new first place on the 礼物榜 (found by the 礼物榜页's `../gift-board/changes.mjs`), each step of a lottery and
a 盲盒查询's 账单卡;
and the 宝箱小游戏 (below).

Her speech bubbles and the effects' marks are in the 礼物榜页's Noto Sans SC (`../gift-board/fonts/noto-sans-sc.css`, `SANS`
of `../gift-board/paint.mjs`), loaded before she says anything; the stream's PC need not have the board's PingFang SC.

The page is 480 × 640 design px, scaled with the browser source and standing on its bottom edge, and draws her pictures at
their own resolution. It loads her pictures and props at once but for the sheets of the 日常小动作, the castle, 大航海 and the
lottery, each with its strips when it first plays (the lottery's too when she first waits through one), and the 宝箱小游戏's,
hers and the 捣蛋鬼's, when it is first to come: all of them decoded take about 574 MB, the game's more. A pose whose sheet is
still loading keeps her in the drawing she has. A load that fails, as when the web service restarts, is tried again when next
asked for, at the soonest a second later: the game's while it is still to come, a sheet's when its show next plays.

## The 宝箱小游戏

The design package's README 12 and its board with `game` 开 are the game; the page plays it as the board does, its engine in
`engine.mjs` (the 来访's clock, `gameUpdate`, where everything is at a moment, `gameState`, and the tug bar's rules, from
`gameGift` on) and its drawing in `pet.mjs` (`drawGame`, the tug's board `drawBoard`, a 绝杀 `drawBuzzer`). Nothing of it is
drawn while the 捣蛋鬼 is away, and her shows are as they were.

The 捣蛋鬼's drawings, its loops and in-betweens, and the chest's are the same for every character, in `prankster/`; its
`prankster.json`:

| Field | What |
|---|---|
| `anchor` | How tall the standing drawing the chest was drawn beside is, in the chest's px |
| `sheets`, `poses`, `strips`, `transitions`, `frames` | Its pose sheets (3 × 2, the first cell its idle drawing, the ruler), its poses' `[sheet, cell]`, its loops and its in-betweens, as a character's |
| `cuts` | Its changes of pose left a plain switch on purpose, either way round (its disguises pop on and off, it ducks out of sight, falls, tumbles); knocked flying and turning round it always switches, and any other change with no in-betweens of its own goes by way of pulling its rope (`pkRoute`) |
| `props` | The chest, shut and open, the ring picture each of its two handles is, and the paper ball a danmaku throws |
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
  any picture `prankster.json` names that is not there. `DesktopPetGame.test.mjs` checks when the 捣蛋鬼 comes and goes, the
  tug bar's rules (the sides, 秒杀, time-up and 绝杀, what danmaku do), what scares it off and what goes into her chest.
- `scripts/desktop-pet/compare.mjs --port 8820` plays every show, 心情 and round, and the 宝箱小游戏's routes of the design
  package's `tools/check-game.mjs` (played on the page by `scripts/desktop-pet/routes.mjs`), on the page and the board from one
  seed, frame by frame on one clock, on Chrome's CPU canvas, and compares their pixels. `DesktopPetBrowserTests` runs it, about a minute, when
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
- A castle in a 一轮送礼 stops its gift and blind box shows and waits for its blind boxes' result as they do, and every castle
  opened plays, one after another, as many as the result says (`castles`, boxes the page never saw too): B站 gives a castle
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
- The 宝箱小游戏 comes on its own every `pranksterMinutes`, counted from the room connecting, never at 0, and at most twice an
  hour, or when the streamer 叫它来; the board's game was on or off and came every 30 minutes. It sets off once its pictures
  are there and only in a connected room: one due when the room disconnects does not come, and one here runs off as at a PK,
  its 来访 counting for nothing, what was thrown or kept aside for her chest too. The page reports it setting off and leaving.
  What it and she say in a 来访 are her character's lines of the groups `chestGame` and `prankster` (a space breaks the
  捣蛋鬼's line), and a paper ball is a 幸运纸团 `luckyPercent` times in a hundred, never at 0; the board said fixed lines,
  and one ball in ten was lucky.
- The viewers of a 来访 are the room's. Whoever sends a 人气票 or 小花花 is on its side with the face their gift message
  carried, or, with none, the first character of their name on the side's colour; the board's are eight sample viewers with
  avatars of their own. A gift message's gifts are each taken as the board takes one, so one message may throw some and keep
  others for her 一轮送礼. The page lets the faces go when the 来访 is over, but for one a 绝杀 still shows; the board keeps its
  eight. The 站队 a danmaku takes is Core's to tell, and a viewer B站 tells with no UID takes
  none and is not called out (flying over, any danmaku still freezes the 捣蛋鬼); the board's buttons say 1 or 2 for one of
  its viewers.
- Any other gift is kept aside for her chest, playing nothing, until its viewer's 一轮送礼 comes to the 中礼物 threshold, when
  its show plays and scares the 捣蛋鬼 off. As on the board the whole round counts, what it sent before the 来访 too, and
  no 人气票 or 小花花 thrown in it; the round is Core's, one for each viewer, where the board has one for all. A viewer's
  blind boxes sent while it is here wait, unshaken, for Core's 盲盒播报: one that cost less than a 中礼物 in all plays nothing
  while it is here, a bigger one, or a castle, plays and scares it off. The board's stood in for the 盲盒播报 3.8 s after
  the round's last gift.
- A first place taken while it is here is crowned once it has gone and the chest is home. Taken by a gift whose own show
  scares it off (a round come to a 中礼物), the board crowns it right after that show, before the shows that waited; the
  page, told of it while the 捣蛋鬼 is still here, crowns it as any other taken then, after them (`gm.top`). The 礼物榜 tells
  the page of it apart from the gift, before it or after, where the board's gift said so itself.
- A 试演 and a show turned off scare it off too: a gift's show turned off sends its ring of light all the same and plays
  nothing, and a 试演 scares it at once, with no light and no name rising from the bar. A PK does as the board's button
  did, and a lottery is drawn, keeping it away, by `lotOf` whether its show is on or not.
- While a 来访 is wound up a show of what happened waits its turn, as on the board; a 试演 starts at once all the same, and
  then waits for the chest to be home and her hair to be smooth, whatever it takes over: a show that scared the 捣蛋鬼 off and
  waits, 守住了 or 被偷了 before the sprite has patted her hair smooth, or none (a PK scared it off). Taking over one of the
  game's own shows she waits in the pose she had there, cheering on after 守住了 and 打飞 and kneeling after 被偷了, and says
  on its line until that show would have stopped it; otherwise she waits standing, or cheering frizzed after the lightning.
  Her hair is patted smooth before she shakes a round's blind boxes too, and the 捣蛋鬼 comes again only once the last 来访 is
  tidied up.
