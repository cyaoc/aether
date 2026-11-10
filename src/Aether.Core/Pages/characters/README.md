# Characters

One folder for each character (GLOSSARY 角色), named by her number; Core's `Character.Current` says which one is drawn, and pages ask
for her files as `/characters/current/`. The code knows her pictures only by the names in her `character.json` and draws with its
numbers, never by who she is; `CharacterTests` checks every character has every picture and number her manifest calls for.

## character.json

| Section | What |
|---|---|
| `sprite` | Her sprite, `idle`, `blink`, `lookRight` and `happy`, on one canvas at one scale |
| `giftBoard` | The gift board's two poses, idle and cheering, on one canvas at one scale, and what was measured on them in the board's design pixels: `height` drawn, the ribbon's cut on each (`idleCut`, `cheerCut`, a fraction of the picture's height), the sprite's place by her shoulder (`shoulder`) and the cheer's rays (`cheerCenter`), y from the ribbon's top, and `spriteSize`, how high the sprite's picture is drawn beside her |

## 1

The new mascot, from the mascot design package `docs/design/mascot/textures/` (#355), unchanged: `peek-idle`, `peek-cheer`,
`sprite-idle`, `sprite-blink`, `sprite-look-right` and `sprite-happy`. The gift board numbers are #343's: the cuts 0.855 and
0.845, the sprite at (358, ribbon's top − 70) with its picture 84 px high, the creature in it about 76 px; her poses
lie over the old ones, so the height and the cheer's centre are the old board's.
