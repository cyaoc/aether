# 素材来源

全部用本机 codex 的内置生图生成（2026-10-25），命令形如：

```sh
codex exec --skip-git-repo-check -s workspace-write -C <目录> [-i <参考图>] -o <log> \
  "Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to <路径>. Do not write any code. Image: <下面的描述>"
```

生成后裁到不透明区域、缩到高 1200，存成 WebP。冲击帧这类白底黑图把亮度转成透明度、颜色换成白色。

## 主角四种身份（hero-1 到 hero-4）

先出一张四人设定图，保证是同一个人；但四个人互相重叠，切不开。于是再把设定图作参考（`-i`），每次只重画一个人，得到 `hero-1`（一般人）到 `hero-4`（总督）。

设定图：

> Landscape 1536x1024. Character outfit progression sheet, the SAME original anime character shown FOUR times side by side in one row, evenly spaced, not overlapping: a slim androgynous young warrior seen in a three-quarter BACK view, looking slightly over the shoulder, the face is completely hidden in deep shadow under messy dark hair (no visible eyes, nose or mouth), dynamic heroic stance, each figure fills the full image height and is cropped at mid-thigh by the bottom edge. Japanese anime gacha-game splash art style, crisp clean lineart, cel shading, strong colored rim light. Fully transparent background with a real alpha channel: no background color, no floor, no cast shadow, no text, no labels, no frames. From left to right: (1) light dark-red and white short jacket, holding ONE katana in the right hand, red-white rim light; (2) fitted blue outfit with a short blue cape, holding TWO short swords, one in each hand, blue rim light; (3) heavy purple and silver plate armor with a long flowing purple cape, a HUGE greatsword resting on the shoulder, purple rim light; (4) long majestic black robe with ornate gold trim and gold shoulder ornaments, empty right hand raised commandingly, gold rim light. Same character, same hair, same body in all four.

单人重画（N = 1 到 4）：

> The attached image is a character sheet with the same anime character shown four times, numbered 1 to 4 from left to right. … Image: Portrait 1024x1536. Only figure number N from the sheet, alone, redrawn at full size: exactly the same character, same outfit, same weapon, same colors, same three-quarter back view with the face hidden in shadow, same anime splash-art style and rim light. The figure fills the frame height, head near the top, cropped at mid-thigh by the bottom edge, and every part of the outfit and weapon fits inside the frame. Fully transparent background with a real alpha channel: no background color, no other figures, no floor, no shadow, no text.

## 一般人的动作姿势（pose-2 到 pose-5）

都以 `hero-1` 作参考，共同的结尾：

> Exactly the same character as the reference: same messy dark hair, same red-and-white short jacket, black pants, red ribbons, fingerless gloves, same katana; the face stays completely hidden in shadow (no eyes, nose or mouth visible). Japanese anime gacha-game splash art style identical to the reference: crisp clean lineart, cel shading, strong red rim light. Fully transparent background with a real alpha channel: no background color, no floor, no cast shadow, no text, no effects other than what is described.

- pose-2 蓄势（第一次右边被裁掉，又把它和 hero-1 一起作参考重画了一次，要求四周留边；右腿仍碰边，处理时把右边 90 px 渐隐）：Portrait 1024x1536. Iaido draw stance seen in a three-quarter back view, the character faces toward the RIGHT side of the image: crouching low with knees bent, body leaning forward to the right, left hand holding the scabbard at the left hip, right hand gripping the katana hilt about to draw, jacket tails, red ribbons and hair blown strongly backward to the left by wind, tense coiled pose. … zoomed out a little so the entire figure including both hands, the hilt and the scabbard fits inside the frame with clear empty margins on the left, the right and the top; only the legs are cut by the bottom edge at the knees.
- pose-3 拖影：Landscape 1536x1024. The instant of a horizontal draw-cut seen in a three-quarter back view: the body twisted hard toward the right, right arm sweeping the katana from left to right in a wide arc, the blade drawn as a classic anime motion SMEAR frame (a stretched, distorted white-red streak shape following the arc with several ghosted blade edges), jacket, ribbons and hair whipping. The figure is on the left half, the smear extends into the right half. Cropped at the knees by the bottom edge.
- pose-4 斩后：Landscape 1536x1024. Follow-through just after the slash, three-quarter back view: the character has lunged far forward to the right in a low wide stance, right arm fully extended to the right holding the katana level, the blade glinting, jacket, ribbons and hair trailing behind to the left. The figure spans the left two thirds. Cropped at the knees by the bottom edge.
- pose-5 残心：Portrait 1024x1536. Zanshin finishing pose in a three-quarter back view facing slightly right: standing upright and calm, the katana held low and out to the right side right after a blade flick, jacket and ribbons settling, a few strands of hair still lifted. Head near the top, cropped at the knees by the bottom edge.

## 其他

- moon（60 档的月夜背景）：Landscape 1536x1024. Cinematic anime background art: a deep night sky over a dark horizon, a huge pale full moon slightly right of center partly veiled by thin ink-wash clouds, faint red haze near the horizon, very dark and moody, desaturated with deep navy and black tones, Japanese ink-wash (sumi-e) brush texture in the clouds, no characters, no buildings in the foreground, no text.
- impact（冲击帧的集中线）：Square 1254x1254. Manga impact frame: a powerful radial burst of sharp white concentration lines (shuuchuu-sen) exploding outward from the center, with jagged white ink splashes and spiky shock shapes near the center, high-contrast brutal anime fight style. Pure solid black background (#000000). White only, no grey wash, no color, no text.
- gold-sword（总督身后的金剑）：Portrait 1024x1536. One single ornate floating golden sword pointing straight down, perfectly vertical and centered, long blade, jeweled gold crossguard and hilt, faint gold glow, anime game item art, crisp lineart and cel shading. Fully transparent background with a real alpha channel, nothing else in the image, no text.

## 经验

- 要求透明底时，codex 能给出真正带透明通道的 PNG。
- 同一张图里画多个人能保证一致，但人物容易重叠；先出设定图、再以它为参考逐个重画，一致性很好。
- 动作姿势以亮相图为参考生成，服装、发型、刀都能保持一致；要明确写"四周留边"，否则容易把人物切在图边。
