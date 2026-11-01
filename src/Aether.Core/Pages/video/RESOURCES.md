# Video page animation resources

Design source: `docs/design/entry-animation` at `0464bd8eeceb3c2a47331ab8e05fab8a896ae86c`.
All 51 textures referenced by EV1–EV4 are copied without changes. Design originals remain in place.
PixiJS 8.12.0 and pixi-filters 6.1.4 are the design package's pinned distributions; their original JavaScript files and header notices are unchanged.
Full MIT licenses are copied from the matching upstream tags and published under `Licenses/`:

- `lib/PixiJS-LICENSE.txt`: https://github.com/pixijs/pixijs/blob/v8.12.0/LICENSE
- `lib/pixi-filters-LICENSE.txt`: https://github.com/pixijs/filters/blob/v6.1.4/LICENSE

## Nickname font

LXGW WenKai Medium, SIL Open Font License (see `lib/LXGWWenKai-OFL.txt`). Original from
https://github.com/lxgw/LxgwWenKai/blob/main/fonts/TTF/LXGWWenKai-Medium.ttf,
Git blob SHA `8776123c24ebf011b0ecc6923708cef2d9cadfb0` (25,800,076 bytes).

`lib/standard-8105.txt` is the transcription of 《通用规范汉字表》 from
https://github.com/iDvel/The-Table-of-General-Standard-Chinese-Characters/blob/76c8512624d566ce0c6180948c9a02a4d9b1e0cf/1-8105纯汉字（按顺序排列）.txt.
It contains exactly 8105 distinct characters; all are present in the original and subset cmap.
The subset also includes the 5401 traditional characters in the Big5 common zone (A440–C67E),
ASCII, Japanese kana, CJK punctuation and full/half-width forms present in the source font.
Total subset cmap: 10,718 codepoints. Missing characters use the system sans-serif fallback.
Generated with fonttools 4.68.0 and brotli 1.2.1; text is fitted without truncation.

## Audio

Six original Freesound CC0 sources from the design package, converted without trimming:
`ffmpeg -i SOURCE -c:a libopus -b:a 160k OUTPUT.ogg`.
Events retain design times and gain values. Peak timing is calculated on decoded buffers,
including browser output latency, as in the design sound preview.

- https://freesound.org/people/Xinematix/sounds/491279/ — Taiko Drumloop 003
- https://freesound.org/people/Anuj_Shrestha1/sounds/486140/ — taiko
- https://freesound.org/people/Anuj_Shrestha1/sounds/486141/ — taiko2
- https://freesound.org/people/nekoninja/sounds/370204/ — samurai-slash
- https://freesound.org/people/XfiXy8/sounds/467292/ — sword-re-sheathed
- https://freesound.org/people/unfa/sounds/221528/ — glass-break
