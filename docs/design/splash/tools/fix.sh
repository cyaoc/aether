#!/bin/zsh
# Repaints one spot of an image with codex: fix.sh <dir> <base-name> <out-name> <change text>.
set -e
T=${0:A:h}; G=$1; base=$2; out=$3; change=$4
body="Edit the attached image $base.png. Keep the composition, framing, character, face, hair, colors, lighting, background and art style EXACTLY the same, pixel for pixel wherever possible, and the same 1536x1024 size. Change ONLY this: $change $(cat $T/prompt-anatomy.txt) $(cat $T/prompt-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE edited image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $G/$base.png -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
