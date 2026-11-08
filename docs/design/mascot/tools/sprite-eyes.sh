#!/bin/zsh
# sprite-eyes.sh <idle.png> <out-dir> <name> <blink|right>: redraw only the sprite's eyes; paste them back with
# sprite-patch.py so every other pixel stays the idle sprite's.
set -e
src=${1:A}; G=${2:A}; out=$3
case $4 in
  blink) eyes="both eyes gently closed into soft downward-curved lines, as in a blink";;
  right) eyes="both eyes still open and round but glancing to the viewer's RIGHT, the pupils and highlights shifted toward the right side of each eye";;
esac
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to EDIT the attached image, then copy the resulting PNG file to $G/$out.png. Do not write any code. Keep the whole image exactly the same, same size, same position, same transparent background, the same crown, flag, bow, crest, mouth, blush, arms, body and colors, and change ONLY the two eyes: $eyes."
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $src -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
