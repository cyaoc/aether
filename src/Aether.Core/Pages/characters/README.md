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
| `desktopPet` | Her Q 版 for the 桌宠 (`../desktop-pet/pet.mjs` draws her with it), in `desktop-pet/`: `sheets`, her pose sheets and her sprite's, 3 × 2 cells each; `views`, her sprite's four turnaround views in a row; `props`, what lands by her or in her hands and what the big shows bring on; `anchor`, how tall the standing drawing the props were drawn beside is, in their px; `points`, where on a prop the big shows place things, in its px: the flagship's bow deck she stands on and mainmast top, the admiral's ship's mainmast top and the bow, mainmast, foremast and stern its flags are strung by, and the castle's gate; `poses` and `sprite`, the `[sheet, cell]` of each of her poses and her sprite's drawings; `frames`, the strips of four frames by name; `strips`, the strip a pose moves through (`f`), a frame every `ms` or the `[frame, ms]` steps of a `loop` or of `once`; `transitions`, the strip whose two middle frames take her from pose `a` to `b` (`b` to `a` backwards); `cuts`, changes of pose with none, where she simply switches; `sameSpot`, poses placed where an earlier one at the same spot is, so what lies on the ground stays put; `body`, how many px a pose is moved so her body stands where she stands idle; `spriteStrips`, her sprite's moving drawings, `[strip, ms a frame]`; for the 宝箱小游戏, `slow`, the ms a frame of the in-betweens played slower than 90 (her hair settling, her curling up to sulk), `grip`, the column of her cells holding the chest's ring where its link is, `hair`, where her head is across the drawings her frizzed hair settles into, and `lyingHead`, how far across her lying drawings her head is. The first cell of every sheet is the same standing drawing, the ruler every sheet is scaled by, and each row's drawings stand on its lowest drawn pixel, its ground line; the page measures both |
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
Her Q 版 is that package's (`textures/`, unchanged, as `desktop-pet/`) and the board's numbers for it: `POSE`, `STRIP`, `TRANS`,
`CUT`, `FRAMES` (the files `tools/blobs.json` names), `SAME_SPOT`, `BODY`, `SPRITE`, `SSTRIP` and `ANCHOR`, for the poses of the
shows the 桌宠页 plays, and her 宝箱小游戏's `SLOW`, `GRIP`, `HAIR` and `HEAD` as `slow`, `grip`, `hair` and `lyingHead`, and as `points` the flagship's (`PROPS`), the admiral's ship's (`ADM_SHIP`'s `mast` and `rig`) and the
castle's gate, which `castleShow` takes at 0.503 and 0.676 of the 1024 px picture.
