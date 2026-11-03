# 素材来源

`textures/` 里的图在 2026-11-04 由 Codex CLI 0.166 的内置生图功能（OpenAI 图像生成）生成，再用 Pillow 转成 WebP（quality 88，method 6）。生成时带了参考图：启动页插画 `docs/design/splash/textures/splash.webp`、软件图标 `docs/design/app-icon/aether-1024.png`，以及概念图。概念图有两版：第一版是整块底板、看板娘扶着榜挥手（没放进本目录），第二版是定稿的悬浮卡片 `preview/concept.webp`。看板娘后来从“站在榜的左边”改成“从飘带后面探出”，好让同一个组件放在左边、右边、竖屏都一样。这里只作来源记录，**实现时直接用定稿，不要重新生成**。

人物图的提示词总会接上启动页设计包的人体段落和负向词：`docs/design/splash/tools/prompt-anatomy.txt`、`prompt-negative.txt`（概念图允许出现界面和文字，删掉了负向词里禁止文字和界面的那一句）。姿势图的每只手都放大检查过：五根手指，顺着手臂接到肩膀。

## 看板娘姿势（`mascot-peek-idle.webp`、`mascot-peek-cheer.webp`）

从飘带后面探出上半身的两个姿势。参考图依次是第一版的看板娘姿势（已确认的人物）和启动页插画。两个姿势各跑两张，挑了重叠后脸最对得上的一对（idle-1、cheer-1）；生成的 PNG 本身带透明通道，等比缩到 768 × 1152，不裁边，两张保持同一张画布、同一比例。

共用段落：

> A clean cut-out character sprite for a live-stream overlay, portrait canvas 1024x1536. The ORIGINAL heroine from the attached references, exactly the same person, face, hair, headset and outfit: an adult woman of about 22 with a slender, modest figure, long silver-white hair with soft blue tips, large blue eyes, a slim white streaming headset with a small boom microphone, the modest white-and-royal-blue idol-tech jacket with a high collar, full sleeves and gold trims. Style exactly like the references: top-tier modern anime game key art, clean confident line art, cel shading with soft luminous gradients. She is peeking up from behind a horizontal ledge, like a host leaning over the top of a sign: only her upper body is drawn, and the bottom of the figure ends in a perfectly straight horizontal cut at 84 percent of the image height (everything below is hidden behind the sign and must be empty). Scale and placement, the same in every pose of this series: she is centered horizontally, her shoulders span about 46 percent of the image width, her chin is at about 52 percent of the image height. Nothing else: no sign, no ledge, no panel, no UI, no chat-bubble sprite, no ribbons, no sparkles. The background must be fully TRANSPARENT (alpha). If you cannot output transparency, use a perfectly flat solid #00FF00 chroma-key green instead, with no green anywhere on her.

平时（`mascot-peek-idle.webp`）：

> Pose: she leans forward slightly with BOTH forearms resting horizontally on the top of the hidden sign, the forearms lying flat along the line at 80 to 84 percent of the image height, one hand resting lightly on the other wrist in front of her, all fingers relaxed and well shaped; a warm, friendly open smile at the viewer, head tilted a little; hair falling over her shoulders and down in front of the sign line.

欢呼（`mascot-peek-cheer.webp`）：

> Pose: a joyful cheer: both arms raised high above her head, both hands open with all five digits visible on each hand, a big happy open-mouthed smile with her eyes curved shut in delight, hair and ribbons swinging upward; her body is still cut off by the same straight line at 84 percent of the image height, and both raised hands stay at least 5 percent below the top edge.

## 标题飘带（`banner.webp`）

参考图是 `preview/concept.webp`。跑了两张，选飘带比较平、中间好写字的那张，裁掉透明边，缩到 1000 宽。

> Only the title banner from the attached reference image (the blue ribbon behind the title at the top right), redrawn alone at large size with NO text, flat front view, landscape canvas 1536x1024. A horizontal ribbon banner of glossy royal-blue satin (around #4255FF, lighter along the top fold), its two ends folding back and curling slightly at the left and right, a thin luminous white-gold edge line along the top and bottom, a soft blue outer glow and two tiny four-point sparkles near the ends. The main ribbon body has a width-to-height ratio of about 5:1 and fills about 90 percent of the canvas width. The middle of the ribbon is completely EMPTY and smooth, ready for a title to be added later. Clean premium modern anime gacha-game UI, luminous and crisp. No text, no letters, no characters. The background must be fully TRANSPARENT (alpha). If you cannot output transparency, use a perfectly flat solid #00FF00 chroma-key green instead. No watermark.

## 示意头像（`avatars/`）

参考图是第一版概念图。一张 4×4 的头像图，按格切成 16 个圆，缩到 160 px。只在画板里用。

> A 4 by 4 grid sheet of 16 round avatar illustrations in the same soft, luminous anime style as the small round avatars in the attached reference image, square canvas 1254x1254, each avatar a perfect circle of the same size with a thin white ring, evenly spaced with clear gaps: 1 starry night lake, 2 cherry blossom branch, 3 sleepy white cat, 4 sunflower, 5 blue sky with clouds, 6 koi fish, 7 pink sunset clouds, 8 lantern at dusk, 9 shiba puppy face, 10 white rabbit, 11 strawberry cake, 12 crescent moon and stars, 13 ocean wave, 14 red maple leaves, 15 snowy mountain, 16 cup of cocoa. No people, no text, no numbers. The background must be fully TRANSPARENT (alpha) outside the artwork. If you cannot output transparency, use a perfectly flat solid #00FF00 chroma-key green instead. No watermark.

## 概念图（`preview/concept.webp`）

三个融合方案（柔光无边框、悬浮卡片、全息投影）各生成一张，画在明亮的白天游戏画面上比较，定了悬浮卡片这张。共用段落：

> A polished concept mockup of a live-stream overlay, landscape 1536x1024. Background: a real live stream of a bright, colorful, busy outdoor anime game scene in daylight (green fields, blue sky, white clouds, a game character mid-action, a small game HUD in a corner), slightly out of focus — the hardest case for an overlay to blend into. Design goal: the gift leaderboard on the right must BLEND NATURALLY into the stream instead of sitting on it as a heavy block, while every name and number stays clearly readable. Its content: the title 礼物榜; ranks 1 to 3 with large slanted italic numerals 1, 2, 3, round avatars, short Chinese nicknames (星河, 小月, 蓝蓝), a small cute gift icon and ×7, ×5, ×3; ranks 4 to 8 smaller (阿白 ×3, 晴天 ×2, 小鱼 ×2, 云朵 ×1, 晚风 ×1). Rank 1 has a gold accent, rank 2 silver-white, rank 3 ice-blue. The leaderboard is compact, about 24 percent of the image width, placed near the right edge. Palette from the brand: royal blue around #4255FF, white, small gold accents. Style: clean premium modern anime gacha-game UI, luminous and crisp, gorgeous but restrained.
> The mascot: the ORIGINAL heroine from the attached reference images, the same person with the same face, an adult woman of about 22 with a slender, modest figure, long silver-white hair with soft blue tips, large blue eyes, a slim white streaming headset with a small boom microphone, the modest white-and-royal-blue idol-tech jacket with a high collar and full sleeves. She is SMALL: drawn from the waist up and only about as tall as the leaderboard. Her companion: the floating sprite from the attached app icon, a soft glossy white rounded chat bubble with two vertical pill-shaped royal-blue eyes, no mouth, no limbs. Same art quality as the references: top-tier modern anime game key art, clean line art, cel shading with soft luminous gradients.

> Treatment B, FLOATING CARDS: there is NO panel at all. Each rank is its own small floating card with clear gaps between them through which the stream shows: ranks 1 to 3 are compact frosted-glass cards with gold, silver and ice-blue luminous rims; ranks 4 to 8 are smaller rounded pills of dark translucent glass with a thin bright outline and a soft drop shadow so they read on bright backgrounds. The title 礼物榜 sits on a small glowing ribbon-shaped banner above the list. Mascot: at the lower-left of the list, one forearm resting on the nearest card, the other hand giving a small wave; the sprite floats by her shoulder.
