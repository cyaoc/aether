#!/bin/zsh
# gen-ship.sh <out-dir> <name>: the governor's flagship, transparent.
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2
body="$(cat $T/ship-base.txt) $(cat $T/ship-sheet.txt) $(cat $T/ship-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i ${T:h}/textures/castle-closed.webp -i $M/textures/sprite-idle.webp -i ${T:h}/textures/fleet-sheet.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
