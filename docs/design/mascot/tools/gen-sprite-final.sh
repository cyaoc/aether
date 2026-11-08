#!/bin/zsh
# gen-sprite-final.sh <out-dir> <name> <castle-ref> <design-sheet>: the sprite's front cut-out, transparent, from its design sheet.
set -e
T=${0:A:h}; P=${T:h}; G=${1:A}; out=$2
body="$(cat $T/sprite-base.txt) $(cat $T/sprite-final.txt) $(cat $T/sprite-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. The third attached image is the approved design sheet of this sprite: match it exactly. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i ${3:A} -i $P/refs/face-s2b.webp -i ${4:A} -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
