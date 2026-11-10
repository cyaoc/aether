# Characters

One folder for each character (GLOSSARY 角色), named by her number; Core's `Character.CurrentNumber` says which one is drawn, and
pages ask for her files as `/characters/current/`. The code knows her pictures only by the names in her `character.json` and draws
with its numbers, never by who she is; `CharacterTests` checks every character, and the background she picks, has every picture
and number the manifests call for, and lines of every kind.

## character.json

| Section | What |
|---|---|
| `gui` | Her layers for the GUI's splash and login page, as big as the background under them, and that background: a folder of `../backgrounds/` |
| `sprite` | Her sprite, `idle`, `blink`, `lookRight` and `happy`, on one canvas at one scale |
| `giftBoard` | The gift board's two poses, idle and cheering, on one canvas at one scale, and what was measured on them in the board's design pixels: `height` drawn, the ribbon's cut on each (`idleCut`, `cheerCut`, a fraction of the picture's height), the sprite's place by her shoulder (`shoulder`) and the cheer's rays (`cheerCenter`), y from the ribbon's top, and `spriteSize`, how high the sprite's picture is drawn beside her |
| `lines` | Her lines for the 桌宠: for every kind of line in the 演出表 (`../desktop-pet/shows.json`), by its id, a list the page picks one from at random, holding only the placeholders that kind may; the streamer's changed kinds replace hers (`desktop_pet` settings) |

## Backgrounds

`../backgrounds/<name>/` holds a splash and a login picture, 1536 × 1024, and `background.json`, which names them and gives the
colour the GUI shows until they are drawn. The GUI stacks a character's layer on her background, both `UniformToFill`.

`afterglow` is the new mascot's starry sky with an afterglow (`splash-background`, `login-background` of the mascot design
package); its colour is the mean of its splash picture's top-left corner, where the title is.

## 1

The new mascot, from the mascot design package `docs/design/mascot/textures/` (#355), unchanged: `peek-idle`, `peek-cheer`,
`sprite-idle`, `sprite-blink`, `sprite-look-right`, `sprite-happy`, and `splash-character`, `login-character` as `splash`,
`login`. The gift board numbers are #343's: the cuts 0.855 and 0.845, the sprite at (358, ribbon's top − 70) with its picture
84 px high, the creature in it about 76 px; her poses lie over the old ones, so the height and the cheer's centre are the old
board's. Her lines are `LINES` of the desktop pet design package's board, `docs/design/desktop-pet/boards/DP-DesktopPet.dc.html`.
