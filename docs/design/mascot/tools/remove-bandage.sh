#!/bin/zsh
# remove-bandage.sh <image> <out-dir> <name> [extra sentence]: paint out the knee bandage; paste the result back with
# bandage-patch.py so only the bandage spots change.
set -e
src=${1:A}; G=${2:A}; out=$3; extra=$4
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to EDIT the attached image, then copy the resulting PNG file to $G/$out.png. Do not write any code. Keep the whole image exactly the same, same size, same positions, same transparent background, same faces, hands, hair, clothes, colors and line work, and change ONLY this: remove every small skin-colored adhesive bandage from her knees and legs, and paint plain knee skin there instead, matching the skin around it. $extra"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $src -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
