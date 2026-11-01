# 启动页插画的来源

`textures/splash.webp` 在 2026-11-01 由 Codex CLI 0.166 的内置生图功能（OpenAI 图像生成）生成，
参考图是 `docs/design/app-icon/aether-1024.png`，再用 Pillow 转成 WebP（quality 80，method 6）。
这里只作来源记录，**实现时直接用定稿，不要重新生成**。

固定段落在 `tools/`，生成时总会接上：

- `prompt-base.txt`：画风、角色、气泡精灵、配色、版式和裁切安全区。
- `prompt-anatomy.txt`：人体和手部结构。
- `prompt-negative.txt`：负向词。codex 没有单独的负向参数，写进提示词正文。

定稿的场景描述：

> Framed from the knees up, with plenty of empty sky above her head. She stands turned slightly toward the left, looking at the viewer with a warm smile. Her raised left hand gives a small open-palm wave beside her shoulder, all five digits visible and well shaped. Her right hand is held out in front of her at chest height, palm up, with the sprite hovering just above the open palm. Hair and ribbons drift gently.

## 生成时发现的问题

第一轮 3 张都没过检查，问题都写进了负向词或版式规则，再重画：

| 问题 | 写进哪里 |
|---|---|
| 头顶贴上边缘，宽窗口会被裁掉 | `prompt-base.txt`：头发最高处离上边缘至少 13% |
| 胸部夸张 | `prompt-base.txt`：体型纤细；`prompt-negative.txt` |
| 仰视、短裙、露大腿、腿环 | `prompt-base.txt`：平视、裙长到膝盖上方、配深色厚袜；`prompt-negative.txt` |
| 手指细长、弯向不一致 | `prompt-negative.txt` |
| 精灵贴着图片边缘 | `prompt-base.txt`：手和精灵离左右边缘至少 8%；`prompt-negative.txt` |
| 肩膀露肤、袖子断成两段 | `prompt-negative.txt`，再用 `tools/fix.sh` 只修这一处 |
| 张开的手手指交叉 | `prompt-negative.txt`，再用 `tools/fix.sh` 只修这一处 |

检查时用全尺寸裁图看每只手，顺着手臂追到肩膀，张开的手数够五根手指。这张图的两只手都是原图，没有修过。
