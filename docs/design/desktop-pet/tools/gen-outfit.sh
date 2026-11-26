#!/bin/zsh
# gen-outfit.sh <out-dir> <name> <outfit> [<extra reference> ...]: a design sheet of the chibi in one of her sports outfits.
# Prompts: <outfit>-base.txt, <outfit>-sheet.txt and <outfit>-negative.txt; a missing sheet or negative falls back to the
# family's (cos-fire uses cos-sheet.txt, cos-negative.txt), then to outfit-negative.txt.
set -e
T=${0:A:h}; M=${T:h:h}/mascot; G=${1:A}; out=$2; o=$3; shift 3
pick() { local f; for f in $T/$o-$1.txt $T/${o%%-*}-$1.txt $T/outfit-$1.txt; do [[ -f $f ]] && { cat $f; return }; done; print "no $1 prompt for $o" >&2; return 1 }
body="$(pick base) $(pick sheet) $(pick negative)"
refs=(-i $M/textures/chibi-sheet.webp -i $M/textures/chibi-turnaround.webp); for r in "$@"; do refs+=(-i ${r:A}); done
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | $T/codex.sh -C $G $refs -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
