#!/bin/zsh
# gen-peek.sh <out-dir> <name> <idle|cheer>: one gift-board pose of her peeking over the title ribbon, transparent.
set -e
T=${0:A:h}; P=${T:h}; G=${1:A}; out=$2; pose=$3
old=$P/../gift-board/textures/mascot-peek-$pose.webp
body="$(cat $T/peek-base.txt) $(cat $T/peek-$pose.txt) $(cat $T/scene-anatomy.txt) $(cat $T/scene-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $P/refs/face-s2b.webp -i $P/textures/sheet-turnaround.webp -i $old -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
