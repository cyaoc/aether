#!/bin/zsh
# gen-castle.sh <out-dir> <name> <castle-ref> <sheet-file>: the sprite's castle form. The castle reference is a
# screenshot of a live-stream gift and stays out of the repo; only generic features are taken from it.
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2
body="$(cat $T/castle-base.txt) $(cat $T/$4) $(cat $T/castle-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | $T/codex.sh -C $G -i ${3:A} -i $M/textures/sprite-idle.webp -i $M/textures/sprite-sheet.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
