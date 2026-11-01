#!/bin/zsh
# Generates one key visual with codex: gen.sh <dir> <out-name> <scene text>. The app icon is the sprite's reference.
# The base, anatomy and negative blocks are always appended, so no prompt can leave them out.
set -e
T=${0:A:h}; G=$1; out=$2; scene=$3
body="$(cat $T/prompt-base.txt) Scene: $scene $(cat $T/prompt-anatomy.txt) $(cat $T/prompt-negative.txt)"
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $T/../../app-icon/aether-1024.png -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
