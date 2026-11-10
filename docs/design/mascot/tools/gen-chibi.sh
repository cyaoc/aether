#!/bin/zsh
# gen-chibi.sh <out-dir> <name>: the user's chibi sheet extended to a full-body turnaround; the chibi sheet is the standard.
set -e
T=${0:A:h}; P=${T:h}; G=${1:A}; out=$2
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $(cat $T/chibi.txt)"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $P/textures/chibi-sheet.webp -i $P/refs/chibi-neutral.webp -i $P/textures/sheet-turnaround.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
