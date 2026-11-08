#!/bin/zsh
# gen-fleet.sh <out-dir> <name> <sheet-file>: the chibi in the captain, admiral and governor outfits.
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2
body="$(cat $T/fleet-base.txt) $(cat $T/$3) $(cat $T/fleet-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $M/textures/chibi-sheet.webp -i $M/textures/chibi-turnaround.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
