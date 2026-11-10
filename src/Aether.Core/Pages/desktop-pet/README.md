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
