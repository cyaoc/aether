# 贴图素材的来源

`textures/` 里的 28 张图在 2026-10-24 由 Codex CLI 0.163 的内置生图功能（OpenAI 图像生成）按下面的英文描述生成，
再用脚本处理成 WebP：发光类（纯黑底）把亮度转成透明度、单色的统一成白色；框和贴纸是生成时就带透明通道。
这里只作来源记录，**实现时直接用 `textures/` 里的定稿，不要重新生成**。

通用后缀：

- 黑底：`Background must be PURE BLACK #000000 everywhere outside the artwork: no vignette, no noise, no stray dots, no text, no watermark, no frame. Everything must fade to pure black well before the canvas edges.`
- 透明：`The background must be fully TRANSPARENT (alpha). If you cannot output transparency, use a perfectly flat solid #00FF00 chroma-key green instead. No text, no letters, no watermark.`
- 贴纸：`Kawaii anime sticker style: flat pastel colors, thick dark plum (#4B2A4F) outlines, a thin white sticker border, clean vector-like shapes.`

| 文件 | 描述 |
|---|---|
| summon/nebula | a game VFX texture for an anime gacha summon portal: a swirling cosmic nebula vortex seen head-on, wide landscape canvas 1536x1024, ellipse about 1.55 times wider than tall filling ~85% of the canvas; deep violet, indigo, royal blue and magenta gas clouds spiral inward around a darker calm center, laced with fine golden and white star dust. + 黑底 |
| summon/circle | an intricate summoning magic circle, line art, perfectly symmetric, front view, square; concentric rings, a hexagram and nested polygons, two bands of invented arcane runic glyphs (NOT real letters), tiny star marks, filigree; monochrome WHITE lines only. + 黑底 |
| summon/burst | impact sprite for a gacha reveal: radial light burst with hundreds of thin sharp rays, a blinding white core, a thin shockwave ring at ~55% radius, scattered sparkles; monochrome. + 黑底 |
| summon/flare | a single bright star flare: white core with soft halo, four long thin spikes, two fainter diagonal spikes; monochrome. + 黑底 |
| summon/frame-ssr | ornate golden trading-card frame for the top-rarity SSR card, portrait, filling the canvas: gilded border with filigree, gemstones at the corners, a winged crest with a star at the top center, a matching bottom ornament, thin bright inner line. + 透明 |
| summon/frame-sr | ornate frame for the second-rarity SR card: polished SILVER border, violet amethyst gems at the corners, one small four-point star at the top center, thin inner violet glow; clearly simpler than the gold SSR frame. + 透明 |
| summon/frame-r | plain frame for the common R card: thin brushed blue-steel border with small sapphire studs at the corners, nothing else. + 透明 |
| summon/face-ssr / face-sr / face-r | 2026-10-25 重做（旧版金色宇宙、紫色星云太花，字下面显脏）。portrait card-face background, one centered motif (center at 50% width, 49% height), the outer ~15% on all four sides plain evenly dark (the frame covers it, no brightening toward the top), smooth radial gradient, thin hairline line art with a soft glow, no clouds / nebula / rays / noise / text. SSR = THE SUN: two thin concentric gold circles with 16 slender rays, warm amber-brown ground. SR = THE MOON: a slender lavender crescent with three tiny sparkles, violet-indigo ground. R = A STAR: one slim pale-blue four-pointed star, navy ground. 生成后从图案中心放大 1.3 / 1.5 / 1.9 倍（图案要比奖品名大，从字的上下露出来），再只把明显亮于底色的线条提亮 1.6 / 2.0 / 2.6 倍（底色颗粒不动），缩到 600×900。 |
| summon/card-back | back side of a summon card: midnight-navy with a subtle starfield, an ornate symmetrical gold line-art emblem (a star inside concentric magic circles), thin gold border with corner flourishes. |
| summon/comet | comet streak, landscape 1536x512: a blinding white round head near the left (~10% from the left edge), a long tapering tail to the right with sparkling filaments; monochrome. + 黑底 |
| mecha/hud | sci-fi mecha targeting HUD ring, symmetric, square: segmented concentric rings, a compass bezel of tick marks, broken arcs, triangular markers and crosshair notches, tiny hexagons; monochrome WHITE lines, no numbers. + 黑底 |
| mecha/grid | holographic targeting dome texture, landscape: honeycomb hexagon grid of thin glowing white lines, brightest in the center fading out in an ellipse, a few lit cells, faint scanlines. + 黑底 |
| mecha/shock | impact sprite for a mecha target-lock: bright electric shockwave ring with jagged lightning arcs and branching bolts, shards of light, a blinding core; monochrome. + 黑底 |
| mecha/panel-s | UI frame for the top-rank result panel: dark gunmetal armored plates with chamfered corners, red warning lights and red-and-black hazard stripes at the corners, glowing red-orange edge lines, bolts and vents. + 透明 |
| mecha/panel-a | UI frame for the A-rank panel: brushed light-steel plates, glowing ORANGE edge lines and an orange light at each corner, a few bolts; lighter and simpler than the S frame, no hazard stripes. + 透明 |
| mecha/panel-b | UI frame for the B-rank panel: a thin holographic cyan-blue line frame with small angular corner brackets and tick marks, almost no metal. + 透明 |
| mecha/emblems | three rank badges in one row: crimson-red and gold shield badge with a bold S and spiky wing fins; orange and silver hexagon badge with a bold A and two chevrons; plain cool-blue steel rounded-square badge with a bold B. + 透明（允许字母） |
| mecha/tile | sci-fi HUD target card background, fills the canvas: dark gunmetal panel with beveled edges, fine hexagon micro-pattern, grey corner brackets, vent lines along the bottom; neutral grey, plain dark center. |
| gacha/sunburst | kawaii radial sunburst: alternating wide rays with rounded tips, tiny stars, hearts and sparkles between the rays; flat WHITE shapes. + 黑底 |
| gacha/pow | comic explosion burst sticker shape without text: WHITE fill, thick dark plum outline, dark plum offset shadow to the lower right, thin white sticker border. + 透明 |
| gacha/confetti | sprite sheet of 16 kawaii confetti pieces in a strict 4×4 grid: rows pink / yellow / mint (heart, star, curly ribbon, wrapped candy), last row lavender heart, sky blue star, lavender ribbon, sky blue mini capsule. + 贴纸 + 透明 |
| gacha/capsules | three gacha capsules in a row, front view: glossy colored top half and glossy white bottom half with a dark seam ring; golden yellow with a star sticker, pink with a heart, mint green with a clover. + 贴纸 + 透明 |
| gacha/frame-grand | frame for the grand-prize sticker card: rounded border of candy-striped pink-and-gold ribbon, a golden crown with hearts at the top center, stars, hearts and a bow at each corner. + 贴纸 + 透明 |
| gacha/frame-second | frame for the second-prize sticker card: pink scalloped lace, a heart and a small bow at the four corners ONLY, plain top and bottom edges, no crown, no gold. + 贴纸 + 透明 |
| gacha/frame-third | frame for the third-prize sticker card: simple mint-green scalloped sticker border with one tiny white star at each corner. + 贴纸 + 透明 |
| gacha/body | the BODY ONLY of a kawaii gacha machine, front view: chunky rounded glossy pastel pink box, thick dark plum outline and offset shadow, a few small stickers near the left and right edges; no dome, crank, screen, coin slot or outlet. + 透明 |
