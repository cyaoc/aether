#!/bin/zsh
# Generates one image with codex: gen.sh <dir> <out-name> <prompt-file> <ref.png>...  (refs are relative to <dir>).
# Figures always get the anatomy and negative blocks appended, so no prompt can leave them out. Only backgrounds and
# effects may skip them, with BACKGROUND=1. SHEET=1 is for generating the master sheets themselves.
set -e
T=${0:A:h}; G=$1; out=$2; pf=$3; shift 3
for r in "$@"; do [[ -f $G/$r ]] || { echo "missing ref $r"; exit 1; }; done
# Figures are drawn from the master sheets, so every pose keeps the same person, clothes and sword.
[[ -n $BACKGROUND || -n $SHEET || $1 == sheet-* ]] || { echo "first reference must be a master sheet (sheet-*.png)"; exit 1; }
args=(); for r in "$@"; do args+=(-i $G/$r); done
body=$(cat $pf)
if [[ -n $BACKGROUND ]]; then body="$body $(cat $T/prompt-negative-bg.txt)"
else body="$body $(cat $T/prompt-anatomy.txt) $(cat $T/prompt-negative.txt) $(cat $T/prompt-check.txt)"; fi
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G $args -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
