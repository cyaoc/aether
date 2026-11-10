#!/bin/zsh
# gen-scene.sh <out-dir> <name> <pose-file> <sky-file>: one SSR key visual of her; the files are prompt blocks in this folder.
set -e
T=${0:A:h}; P=${T:h}; G=${1:A}; out=$2
body="$(cat $T/scene-base.txt) $(cat $T/$3) $(cat $T/$4) $(cat $T/scene-anatomy.txt) $(cat $T/scene-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. The first attached image is an earlier launch art of this heroine: keep her face, hair, rendering and the starry sky from it. The second attached image is her design turnaround: her identity and outfit must match it. The third attached image shows only how the warm afterglow should glow behind her head; do not copy its sky otherwise. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $P/refs/face-s2b.webp -i $P/textures/sheet-turnaround.webp -i $P/refs/afterglow-s1a.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
