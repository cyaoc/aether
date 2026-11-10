#!/bin/zsh
# gen-outfit.sh <out-dir> <name> <outfit> [<extra reference> ...]: a design sheet of the chibi in one of her sports outfits.
# Prompts: <outfit>-base.txt, <outfit>-sheet.txt and <outfit>-negative.txt (or outfit-negative.txt when it has none).
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2; o=$3; shift 3
neg=$T/$o-negative.txt; [[ -f $neg ]] || neg=$T/outfit-negative.txt
body="$(cat $T/$o-base.txt) $(cat $T/$o-sheet.txt) $(cat $neg)"
refs=(-i $M/textures/chibi-sheet.webp -i $M/textures/chibi-turnaround.webp); for r in "$@"; do refs+=(-i ${r:A}); done
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | $T/codex.sh -C $G $refs -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
