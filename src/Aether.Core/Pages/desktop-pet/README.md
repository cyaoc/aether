# 桌宠页

The 桌宠 (GLOSSARY) plays on this page, a browser source in a corner of the stream. Core tells it what happens on
`/desktop-pet/events` (`DesktopPet.cs`); the page decides what to play.

## shows.json, the 演出表

Every 演出 of the 桌宠, her 日常小动作 and 心情, in one table that Core (`ShowTable.cs`), the GUI's 桌宠 page and this page all
read; the page fetches it as `/desktop-pet/shows.json`. A new 演出, such as PK, is a few more rows here and her lines in each
character's `character.json`; settings know it by its id and play it unless it is turned off, so it needs no settings upgrade.

| Section | What |
|---|---|
| `groups` | `id`, `label` and, for a group of 演出, `note`: the groups the GUI lists 演出 and kinds of lines under, in this order (`mood`, `daily` and `mishap` hold 心情, 日常小动作 and their mishaps' lines) |
| `shows` | A row for each 演出, 日常小动作 and 心情: `id`; `group`; `label`; `tier`, its 演出档 (`SS`, `S`, `A`, `B`, `C`, `D`); `lines`, the ids of the kinds of lines it says; `tests`, what a 试演 of it chooses, each a key of `variants` or `null` for the row itself, none for a row that has no 试演; and, for a 演出 the GUI switches by group, `note`, when it plays, where `{mid}`, `{big}` and `{big-1}` stand for the 中礼物 and 大礼物 thresholds in 电池 |
| `lines` | A row for each kind of line: `id`, which settings and `character.json` key lines by; `group`; `label`; `note`, when she says it; `placeholders`, the names a line may hold in braces |
| `variants` | The label of each 试演 variant: a blind box's ending (`Gain`, `Even`, `Loss`, as Core tells a 一轮盲盒's result, and `Jackpot`), a 日常小动作's `Mishap`, a lottery's step |

Rows of the group `mood` cannot be turned off: a 心情 comes of how quiet the room has been, and `desktop_pet.*_minutes` of 0
keeps her out of it. Every other row can, by its id in `desktop_pet.shows_off`.

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
| `lines` | The current character's lines by kind: hers, with each kind the streamer changed replaced |

## The page

Design source: the desktop pet design package's board `docs/design/desktop-pet/boards/DP-DesktopPet.dc.html` at `da08028`.
Its engine is `engine.mjs` (which show plays when, the 一轮送礼, the 心情 and 日常小动作, what she and her sprite do at a
moment) and its drawing `pet.mjs` and `effects.mjs`, ported function for function on one Canvas 2D; `page.mjs` hands each
event above to the engine's handler of the same name. The board's numbers measured on her pictures are the current
character's `desktopPet` (`../characters/README.md`); the shows' timings, effects and the sprite's ways stay here, the same for
every character. The page plays every row of the 演出表: the 心情, the 日常小动作 and their mishaps, entries, gifts, blind boxes,
castles, 大航海, a new first place on the 礼物榜 (found by the 礼物榜页's `../gift-board/changes.mjs`) and each step of a lottery.

Her speech bubbles and the effects' marks are in the 礼物榜页's Noto Sans SC (`../gift-board/fonts/noto-sans-sc.css`, `SANS`
of `../gift-board/paint.mjs`), loaded before she says anything; the stream's PC need not have the board's PingFang SC.

The page is 480 × 640 design px, scaled with the browser source and standing on its bottom edge, and draws her pictures at
their own resolution. It loads her pictures and props at once but for the sheets of the 日常小动作, the castle, 大航海 and the
lottery, each with its strips when it first plays (the lottery's too when she first waits through one): all of them decoded
take about 574 MB. A pose whose sheet is still loading keeps her in the drawing she has.

The page's checks against the board, for whoever changes it or her pictures. The design package's `tools/server.mjs` serves
the page beside the board, as the web service serves it, telling it it is on in a connected room with the default settings:
`node docs/design/desktop-pet/tools/server.mjs 8820`.

- `DesktopPetCharacters.test.mjs` (`node --test`) finds every change of pose in her shows with no way across.
- `scripts/desktop-pet/compare.mjs --port 8820` plays every show, 心情 and round on the page and the board from one seed, frame
  by frame on one clock, and compares their pixels. `DesktopPetBrowserTests` runs it, about half a minute, when
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
- A castle in a 一轮送礼, sent or opened, stops its gift and blind box shows; one among blind boxes waits for their result as
  they do. The board played a blind box show after a castle sent in the same round.
- The lottery is Core's state, so its steps play as its phase changes and she waits through it by that phase (entries open, a
  draw coming), not by the steps the board's buttons started: a 试演 of a step leaves her as she was. She reacts to a draw at
  its DrawUntil, when the 抽奖页's effects end; its show, which holds her breath 2.5 s first, starts that much earlier. A pool
  lottery has no end of its own, so it ends as a new lottery replaces it, and between draws she does her 日常小动作. The viewer a
  pool calls is named by UID, all its state has, as the 抽奖页 names a winner without a nickname.
- A new first place on the 礼物榜 is crowned after the show of the 一轮送礼 that took it, its blind boxes' once the 盲盒播报 opens
  them; the board's gift said it took first place. The 礼物榜 reaches the page another way than that gift and may come first:
  the crown then waits for the gift's round. As on the board, a 大航海 is not crowned.
- A 试演 starts its show at once, or holds her in its 心情, until it is stopped; stopping it never stops a show of what happened.
  A show's 试演 played out, or cut short by a show of what happened, is reported with `POST /desktop-pet/report`, as the video
  page reports what it played, and the GUI offers another.
- Jolting awake from 犯困 every half minute she says a line of its own kind, `jolt`, in turn; the board said the second of
  冷场后来了人's, which the streamer may now change or take away.
- Shows, 心情, 日常小动作, endings, lottery steps and kinds of lines go by their ids in the 演出表 rather than the board's Chinese
  names, and her lines are picked with `Math.random`, not the board's fixed seed.
