#!/bin/zsh
# gen-sprite.sh <out-dir> <name> <sheet-file> <castle-ref>: her castle sprite. The castle reference is a screenshot of a
# live-stream gift and stays out of the repo; only its features are used.
set -e
T=${0:A:h}; P=${T:h}; G=${1:A}; out=$2
body="$(cat $T/sprite-base.txt) $(cat $T/$3) $(cat $T/sprite-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i ${4:A} -i $P/refs/face-s2b.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
