#!/bin/zsh
# sprite-eyes.sh <idle.png> <out-dir> <name> <blink|right|happy>: redraw only the sprite's eyes (happy also its mouth
# and arms); paste blink and right back with sprite-patch.py so every other pixel stays the idle sprite's.
set -e
src=${1:A}; G=${2:A}; out=$3
case $4 in
  blink) eyes="both eyes gently closed into soft downward-curved lines, as in a blink";;
  right) eyes="both eyes still open and round but glancing to the viewer's RIGHT, the pupils and highlights shifted toward the right side of each eye";;
  happy) eyes="HAPPY: both eyes squeezed shut into upward arcs, the mouth wide open in a big joyful smile, deeper pink blush, and its two tiny round arms raised up high beside its crown; its crown, flag, bow, crest, body and cloud wisps stay exactly where they are";;
esac
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to EDIT the attached image, then copy the resulting PNG file to $G/$out.png. Do not write any code. Keep the whole image exactly the same, same size, same position, same transparent background, the same crown, flag, bow, crest, mouth, blush, arms, body and colors, and change ONLY what this asks: $eyes."
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $src -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
