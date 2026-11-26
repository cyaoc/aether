#!/bin/zsh
# fig-edit.sh <approved sheet.png> <out name>: the approved cos design sheet with only her figure changed (#418: a full bust is
# her own figure, the same under every costume). One-off; the wording is the HER FIGURE sentence of tools/cos-*-base.txt.
set -e
T=${0:A:h:h}/tools; src=${1:A}; G=${src:h}; out=$2
fig=$(grep -o 'HER FIGURE: [^:]*: [^.]*\.' $T/cos-fire-base.txt)
rm -f $G/$out.png
print -r -- "Use your built-in image generation tool (not code) to edit the attached image into ONE new image, then copy the generated PNG file to $G/$out.png. Do not write any code. It is an approved design sheet of a chibi girl in a cosplay outfit. Change ONLY her chest, in every view: $fig Keep everything else exactly as it is: the four views, their poses, sizes and places, her face, hair, eyes, every piece of the outfit, the prop, the close-up and the background." | $T/codex.sh -C $G -i $src -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
