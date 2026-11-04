# Gift board page resources

Design source: `docs/design/gift-board` at `6c2338a`. The drawing, the layout constants and the event timelines are ported from
`boards/GB1-GiftBoard.dc.html`; the demo data and the board's mock stream are not.
PixiJS is the video page's `/video/lib/pixi.js`, and the sound uses the video page's `animations/audio.mjs`; neither is copied.

## Textures

`textures/banner.webp`, `mascot-peek-idle.webp` and `mascot-peek-cheer.webp` are copied from the design package without changes.

## Fonts

Noto Sans SC (SIL Open Font License, `fonts/NotoSansSC-OFL.txt`), weights 500 and 800. Original: the variable font `NotoSansSC[wght].ttf`
(version 2.004-H2) from https://github.com/google/fonts/blob/main/ofl/notosanssc/NotoSansSC%5Bwght%5D.ttf, downloaded on 2026-11-04,
Git blob SHA `fb0637bafbcd804fe32152370a1225990745b4bc` (17,772,300 bytes).
Each weight is `fontTools.varLib.instancer.instantiateVariableFont(font, {"wght": 500 or 800}, updateFontNames=True)`, then
subset with fonttools 4.60.2 (default `Subsetter` options, WOFF2) to the code points of the video page's `name-font.woff2`
(see `../video/RESOURCES.md`: the 8105 standard characters, the Big5 common traditional characters, ASCII, kana, CJK punctuation,
full/half-width forms) plus `×`, `…` and `礼物榜`. 10,719 code points; only U+FFA0, which the source font lacks, is missing.
Names with other characters use the system sans-serif fallback.

Playfair Display (SIL Open Font License, `fonts/PlayfairDisplay-OFL.txt`), Italic 800, digits for the ranks:
`fonts/playfair-num.woff2` is the design package's `lib/playfair-num.woff2` unchanged (it also holds `×`, which the page does not use).
Original: https://github.com/google/fonts/blob/main/ofl/playfairdisplay/PlayfairDisplay-Italic%5Bwght%5D.ttf.

## Audio

`audio/578803__nomiqbomi__sparkle.ogg` is the design package's `audio/sources/578803__nomiqbomi__sparkle.mp3`
(https://freesound.org/people/nomiqbomi/sounds/578803/, CC0), converted without trimming as the video page's sounds are:
`ffmpeg -i SOURCE -c:a libopus -b:a 160k OUTPUT.ogg`.

## Deliberate differences from the board

- The rows, the order and the counts come from the pushed state, so the board's fixed `DEMO`, `LONG` and `EVENTS` lists are gone.
- The page draws as soon as it can and does not wait for the pictures: B站's images appear when they have loaded, and a failed one stays empty.
- The idle blink of the bubble sprite goes on after a count-increase event; the board stops it for good after the first one.
