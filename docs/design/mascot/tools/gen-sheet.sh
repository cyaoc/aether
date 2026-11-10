#!/bin/zsh
# gen-sheet.sh <out-dir> <name> <A|B>: one SSR design sheet of her (A turnaround, B expressions); S2b is the face standard.
set -e
T=${0:A:h}; P=${T:h}; G=${1:A}; out=$2; sheet=$3
refs=(-i $P/refs/face-s2b.webp -i $P/textures/sheet-turnaround.webp)
[[ $sheet == B ]] && refs=(-i $P/refs/face-s2b.webp -i $P/textures/sheet-expressions.webp -i $P/textures/chibi-sheet.webp)
body="$(cat $T/sheet-base.txt) $(cat $T/sheet-$sheet.txt) $(cat $T/sheet-anatomy.txt) $(cat $T/sheet-negative.txt)"
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. The first attached image is the launch art of this woman: her face, hair and SSR rendering are the standard to match. The second attached image is a design sheet of her: follow its layout and outfit details. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G $refs -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
