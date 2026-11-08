#!/bin/zsh
# edit-cell.sh <sheet> <out-dir> <name> <cell 1-6> <what>: redraw one cell of a 3x2 sheet, then paste only that cell
# back onto the sheet (codex redraws the whole image a little, so the other cells keep their original pixels).
set -e
T=${0:A:h}; src=${1:A}; G=${2:A}; out=$3; n=$4
names=("top-left" "top-middle" "top-right" "bottom-left" "bottom-middle" "bottom-right")
rm -f $G/$out-edit.png $G/$out.png
prompt="Use your built-in image generation tool (not code) to EDIT the attached image, then copy the resulting PNG file to $G/$out-edit.png. Do not write any code. It is a 3 by 2 pose sheet on a transparent background. Keep the whole image exactly the same, same size, same layout, same transparent background, and redraw ONLY the ${names[$n]} cell: $5 The figure in that cell keeps exactly the same size, position, colors and rendering as in the other cells. No text, no effects."
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $src -o $G/$out.log.txt
[[ -f $G/$out-edit.png ]] || { echo "NO OUTPUT $out"; exit 1; }
${PYTHON:-python3} -c "
from PIL import Image
a = Image.open('$src').convert('RGBA'); e = Image.open('$G/$out-edit.png').convert('RGBA').resize(a.size)
w, h = a.width // 3, a.height // 2; i = $n - 1; box = ((i % 3) * w, (i // 3) * h, (i % 3 + 1) * w, (i // 3 + 1) * h)
a.paste(e.crop(box), box[:2]); a.save('$G/$out.png')"
echo "OK $out"
