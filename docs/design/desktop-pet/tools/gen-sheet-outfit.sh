#!/bin/zsh
# gen-sheet-outfit.sh <out-dir> <name> <sheet-number> <outfit> [<extra reference> ...]: a 3x2 pose sheet in a sports outfit.
# Prompts: <outfit>-pose-base.txt (or outfit-pose-base.txt), sheet-<number>.txt, pet-negative.txt; references: the chibi
# sheet and turnaround, textures/<outfit>-sheet.webp, then any extras (the bicycle for cycling).
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2; n=$3; o=$4; shift 4
base=$T/$o-pose-base.txt; [[ -f $base ]] || base=$T/outfit-pose-base.txt
body="$(cat $base) $(cat $T/sheet-$n.txt) $(cat $T/pet-negative.txt)"
refs=(-i $M/textures/chibi-sheet.webp -i $M/textures/chibi-turnaround.webp -i ${T:h}/textures/$o-sheet.webp); for r in "$@"; do refs+=(-i ${r:A}); done
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G $refs -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
