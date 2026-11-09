#!/bin/zsh
# gen-frames.sh <out-dir> <name> <ref> [<description>]: four frames of one drawing moving in place (a loop), a 2 x 2 grid on flat green,
# in <out-dir>/<name>-frames.png. <ref> is as in frame-patch.py (textures/pose-1-common.webp:2). Codex gets the grid with
# <ref> already in its top-left quarter as frame 1 (frame-patch.py --grid), so it draws frames 2-4 beside it at its size;
# frames/<description>.txt (default <name>) says what stays still and what each frame does. Then: frame-patch.py <ref> <frames> <strip.png>.
set -e
T=${0:A:h}; G=${1:A}; name=$2; desc=${4:-$2}
${PYTHON:-python3} $T/frame-patch.py --grid $3 $G/$name-grid.png
rm -f $G/$name-frames.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to edit the attached image into ONE new image, then copy the generated PNG file to $G/$name-frames.png. Do not write any code. The attached image is a 2 by 2 grid of FOUR ANIMATION FRAMES on flat #00FF00 green, in this order: top-left, top-right, bottom-left, bottom-right. Frame 1 (top-left) is already drawn: keep it exactly as it is. Draw frames 2, 3 and 4 in the other quarters: the same character with her proportions, line art, colors, outfit and rendering exactly as in frame 1, her head and face exactly as big as in frame 1, at the same size and in the same place in her quarter, whatever rests on the ground staying at the same height, on the same perfectly flat solid #00FF00 green with no green anywhere on her. $(cat $T/frames/$desc.txt) DO NOT DRAW: $(cat $T/frames-negative.txt)"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $G/$name-grid.png -o $G/$name.log.txt > $G/$name.out 2>&1
[[ -f $G/$name-frames.png ]] && echo "OK $name" || { echo "NO OUTPUT $name"; exit 1; }
