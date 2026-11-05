#!/bin/zsh
# Generates one portrait login illustration with codex: gen.sh <dir> <out-name> <scene text>.
# The splash key visual is the heroine's reference and the app icon the sprite's; the splash anatomy and
# negative blocks are always appended, so no prompt can leave them out.
set -e
T=${0:A:h}; D=$T/../..; G=$1; out=$2; scene=$3
sips -s format png $D/splash/textures/splash.webp --out $G/splash-ref.png >/dev/null
body="$(cat $T/prompt-base.txt) Scene: $scene $(cat $D/splash/tools/prompt-anatomy.txt) $(cat $D/splash/tools/prompt-negative.txt) $(cat $T/prompt-negative-login.txt)"
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE portrait image of 1024x1536, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $G/splash-ref.png -i $D/app-icon/aether-1024.png -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
