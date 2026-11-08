#!/bin/zsh
# gen-castle-final.sh <out-dir> <name> <castle-ref> <chosen-sheet> <detail-sheet>: the castle cut-out, gate closed,
# from the chosen design sheet with the rose window and sky bridges of another.
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2
body="$(cat $T/castle-base.txt) $(cat $T/castle-final.txt) $(cat $T/castle-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i ${3:A} -i $M/textures/sprite-idle.webp -i ${4:A} -i ${5:A} -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
