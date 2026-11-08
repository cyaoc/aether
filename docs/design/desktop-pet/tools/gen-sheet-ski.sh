#!/bin/zsh
# gen-sheet-ski.sh <out-dir> <name> <sheet-number>: a 3x2 pose sheet in the ski outfit (ski-pose-base.txt, the sheet, pet-negative.txt).
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2
body="$(cat $T/ski-pose-base.txt) $(cat $T/sheet-$3.txt) $(cat $T/pet-negative.txt)"
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $M/textures/chibi-sheet.webp -i $M/textures/chibi-turnaround.webp -i ${T:h}/textures/ski-sheet.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
