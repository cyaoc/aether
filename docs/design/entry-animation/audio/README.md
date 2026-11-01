# 进场动画音效

每档一条预混好的音轨，播放时和动画同时起步。`cues.json` 记每档用哪段素材、放在第几秒、多响，`python3 tools/mix-audio.py` 重新混出 `tier-*.ogg`。

现在只有一般人 30 档的粗混 `tier-30.ogg`。40、50、60 档按同样的规则写进 `cues.json`，天空裂开的声音还没选。

## 规则

低频按流派定，不是统一一套。

| 流派 | 声音方向 |
|---|---|
| 一般人（太刀） | 薄、快、利，不用重低音 |
| 提督（巨剑） | 重低音和碎石声放在这里 |
| 舰长（双刀）、总督（剑帝） | 做到时再定 |

一般人的节奏：

1. 蓄力几乎无声：很轻的风声，出刀前一声细刀鸣，然后静一拍。
2. 斩击是短而利的切割声。
3. 顿帧那 0.12 秒全静，再浮出轻的金属余韵，不补重响。
4. 刀镡碰鞘时一声入鞘。

## 试听时否掉的

- 上升类的 riser：用户说"和飞机引擎一样、浮夸了"。
- 命中加重低音：用户说这是太刀，主要是切割，这样像钝器，顿帧也一样。
- 试听时把所有声音拉到一样响会失真：蓄力单独听和出刀一样大，要放进整段里听。

## 来源

都是 Freesound 上的 CC0，署名不是必须的，都已打开页面确认。

| 用途 | 文件 | 作者 |
|---|---|---|
| 蓄力风声 | [gentle-wind](https://freesound.org/people/fthgurdy/sounds/528944/) | fthgurdy |
| 细刀鸣 | [unsheath_sword](https://freesound.org/people/Qat/sounds/107589/) | Qat |
| 斩击 | [samurai-slash](https://freesound.org/people/nekoninja/sounds/370204/) | nekoninja |
| 金属余韵 | [samurai-slash2](https://freesound.org/people/nekoninja/sounds/512785/) | nekoninja |
| 入鞘 | [sword-re-sheathed](https://freesound.org/people/XfiXy8/sounds/467292/) | XfiXy8 |
| 60 档世界碎裂 | [glass-break](https://freesound.org/people/unfa/sounds/221528/) | unfa |

glass-break 的作者不同意用于训练生成式模型，放进动画没有影响，但不要拿它跑模型工具。

从 Freesound 下载原文件要登录。在已登录的浏览器里打开 `https://freesound.org/people/<作者>/sounds/<编号>/download/` 就能下；连续下载要放慢，否则会被限流。
