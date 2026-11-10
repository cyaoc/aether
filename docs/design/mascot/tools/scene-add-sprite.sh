#!/bin/zsh
# scene-add-sprite.sh <art> <out-dir> <name> <placement-file>: draw her sprite INTO a finished key visual, touching and
# interacting with her and lit by the same light, instead of pasting a cut-out on top.
set -e
T=${0:A:h}; P=${T:h}; src=${1:A}; G=${2:A}; out=$3
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to EDIT the first attached image, then copy the resulting PNG file to $G/$out.png. Do not write any code. Keep the whole picture the same: the same size and composition, the girl's pose, face, expression, hands, hair, outfit and colors, the background, the starry sky, the afterglow and the ribbons of light. ADD her companion sprite into the scene, drawn in the same SSR anime rendering as the girl: the sprite is exactly the character in the second and third attached images (a round creamy-white cloud creature with big round eyes, a small cat-like mouth, two tiny round arms, cloud wisps below, a crown of cream battlements with three rose-pink spires and a red pennant on the middle spire, a red ribbon bow with a small rose shield crest). $(cat $T/$4) It is lit by the same light as the girl in this scene, with the same rim light and color temperature, so it belongs to the picture and does not look pasted in. Do not draw a glow outline or a sticker border around it. Do not add any other character, hand or object."
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $src -i $P/textures/sprite-sheet.webp -i $P/textures/sprite-idle.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
