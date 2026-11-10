#!/bin/zsh
# gen-sprite-sheet.sh <out-dir> <name> <sheet-number>: one 3x2 pose sheet of the current mascot's sprite, transparent.
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2
body="$(cat $T/sprite-pet-base.txt) $(cat $T/sprite-sheet-$3.txt) $(cat $T/sprite-pet-negative.txt)"
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | $T/codex.sh -C $G -i $M/textures/sprite-sheet.webp -i $M/textures/sprite-idle.webp -i $M/textures/sprite-happy.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
