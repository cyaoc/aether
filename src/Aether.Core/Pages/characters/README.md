# Characters

One folder for each character (GLOSSARY 角色), named by her number; Core's `Character.Current` says which one is drawn, and pages ask
for her files as `/characters/current/`. The code knows her pictures only by the names in her `character.json` and draws with its
numbers, never by who she is; `CharacterTests` checks every character has every picture and number her manifest calls for.

## character.json

| Section | What |
|---|---|
| `giftBoard` | The gift board's two poses, idle and cheering, on one canvas at one scale, and what was measured on them in the board's design pixels: `height` drawn, the ribbon's cut on each (`idleCut`, `cheerCut`, a fraction of the picture's height), the sprite's place by her shoulder (`shoulder`) and the cheer's rays (`cheerCenter`), y from the ribbon's top, and `spriteSize`, how high the sprite's picture is drawn beside her |

## 1

`peek-idle.webp` and `peek-cheer.webp` are the gift board design package's mascot poses, unchanged; the numbers are the ones the
gift board had for them.
