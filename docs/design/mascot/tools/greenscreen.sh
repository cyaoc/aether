#!/bin/zsh
# greenscreen.sh <art> <out-dir> <name>: keep the heroine exactly and flood everything else with flat chroma green, for keying.
set -e
G=${2:A}; src=${1:A}; out=$3
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to EDIT the attached image, then copy the resulting PNG file to $G/$out.png. Do not write any code. Keep the girl EXACTLY as she is: same position, size, pose, face, every strand of hair including the thin loose strands, her clothes, her colors and her rim light. If her companion sprite is in the picture (the little cloud creature with a pink castle crown, on her shoulder, by her face or on her phone), keep it EXACTLY as it is too, as part of her. Replace EVERYTHING else with one perfectly flat, uniform chroma-key green, exactly #00FF00: the sky, stars, glow, light ribbons, capsules, mountains, city, railing and foliage all become flat #00FF00, including the small gaps between her hair strands. No gradient, no glow, no shadow, no texture in the green. The image size stays the same."
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $src -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
