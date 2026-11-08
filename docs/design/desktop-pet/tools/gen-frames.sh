#!/bin/zsh
# gen-frames.sh <out-dir> <name> <ref> [<end ref>]: four animation frames of one drawing, a 2 x 2 grid on flat green, in
# <out-dir>/<name>-frames.png. <ref> is as in frame-patch.py (textures/pose-1-common.webp:2); frames/<name>.txt says
# what stays still and what each frame does. Frame 1 is the reference itself; a transition attaches the drawing it ends
# on as a second reference. Then: frame-patch.py <ref> <out-dir>/<name>-frames.png <strip.png>.
set -e
T=${0:A:h}; G=${1:A}; name=$2; shift 2
refs=(); i=0
for r in "$@"; do
  i=$((i + 1)); ${PYTHON:-python3} $T/frame-patch.py --green $r $G/$name-ref$i.png; refs+=(-i $G/$name-ref$i.png)
done
rm -f $G/$name-frames.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$name-frames.png. Do not write any code. Image: a 2 by 2 grid of FOUR ANIMATION FRAMES drawn from the attached drawing, square, each frame in its own quarter, in this order: top-left, top-right, bottom-left, bottom-right. Every frame keeps the attached drawing's character, proportions, line art, colors, outfit and rendering EXACTLY, at the same size and in the same place in its quarter as the drawing in its square, whatever rests on the ground staying at the same height, on the same perfectly flat solid #00FF00 green background with no green anywhere on the figure. $(cat $T/frames/$name.txt) DO NOT DRAW: grid lines, borders, labels, numbers, text, motion lines, speed lines, sparkles, hearts, effects, a ground, a shadow, a different face, a bigger head, a different hairstyle or outfit, a third arm or leg, a missing limb, two arms from one shoulder, a hand with no arm, anything touching the edge of its quarter, any change to the parts the frames say stay still."
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G $refs -o $G/$name.log.txt > $G/$name.out 2>&1
[[ -f $G/$name-frames.png ]] && echo "OK $name" || { echo "NO OUTPUT $name"; exit 1; }
