# 进场动画音效

声音由动画帧触发，不预混。动画时钟不动，播到哪一帧就触发那一帧的声音，各层各自触发。每档的声音和时间点在 `events.json`，调试流程见 `WORKFLOW.md` 第九节。

## 一般人的声音

| 层 | 做法 |
|---|---|
| 太鼓 BGM | 完整循环，从开场响到结束，最重的一记落在最后一刀 |
| 太鼓渐快 | 蓄力的紧张感靠它，由慢到快、由轻到重，第一刀前 0.16 秒停住 |
| 前几刀 | 切割声加单击太鼓，连斩时每一刀稍微变调 |
| 最后一刀 | 完整的切割声，最响点对准白闪 |
| 入鞘 | 刀镡碰鞘时一声 |
| 世界碎裂 | 只有 60 档，拍刀之后 |

## 规则

- 对齐最响的点，不是文件开头。用完整的声音，不要截短。
- 顿帧不静音，切割声的尾巴自然盖过定格。
- 蓄力不用金属声，否则和斩击分不开；要有节拍才紧张。
- 低频按流派定。一般人是太刀，不用爆破声和重低音；重低音和碎石声留给提督的巨剑；舰长、总督做到时再定。
- 重量靠太鼓，音色靠小扬声器也能听到的频段：能量都在 200 Hz 以下的声音，电脑和手机基本放不出来。

试过什么、为什么否掉，见 `WORKFLOW.md` 第九节的结论表。

## 本地试听

```bash
node tools/server.mjs 8813
open "http://localhost:8813/render.html?board=EV2-EntryTriple.dc.html&sound=40"
```

页面上每一层都有开关，默认全关，下一轮循环生效。

## 来源

都是 Freesound 上的 CC0，已打开页面确认，署名不是必须的。

| 用途 | 文件 | 作者 |
|---|---|---|
| 太鼓 BGM | [Taiko Drumloop 003](https://freesound.org/people/Xinematix/sounds/491279/) | Xinematix |
| 太鼓渐快、前几刀的单击 | [taiko](https://freesound.org/people/Anuj_Shrestha1/sounds/486140/) | Anuj_Shrestha1 |
| 60 档残影齐斩的单击 | [taiko2](https://freesound.org/people/Anuj_Shrestha1/sounds/486141/) | Anuj_Shrestha1 |
| 切割声 | [samurai-slash](https://freesound.org/people/nekoninja/sounds/370204/) | nekoninja |
| 入鞘 | [sword-re-sheathed](https://freesound.org/people/XfiXy8/sounds/467292/) | XfiXy8 |
| 60 档世界碎裂 | [glass-break](https://freesound.org/people/unfa/sounds/221528/) | unfa |

glass-break 的作者不同意用于训练生成式模型，放进动画没有影响，但不要拿它跑模型工具。
