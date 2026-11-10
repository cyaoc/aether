#!/bin/zsh
# gen-princess.sh <out-dir> <name> <sheet-file>: the chibi in her princess outfit.
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2
body="$(cat $T/princess-base.txt) $(cat $T/$3) $(cat $T/princess-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | $T/codex.sh -C $G -i $M/textures/chibi-sheet.webp -i $M/textures/chibi-turnaround.webp -i ${T:h}/textures/castle-closed.webp -i $M/textures/sprite-idle.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
