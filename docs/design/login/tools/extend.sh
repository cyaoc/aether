#!/bin/zsh
# Extends the chosen portrait into the full-window landscape with codex: extend.sh <dir> <out-name>.
# extend-input.webp is the portrait fitted to 1024 high on the left, the rest a smear of its last column.
# The splash anatomy and negative blocks are always appended, so the repaint cannot leave them out.
set -e
T=${0:A:h}; D=$T/../..; G=$1; out=$2
sips -s format png $T/extend-input.webp --out $G/extend-input.png >/dev/null
change="The attached image is a 1536x1024 landscape illustration for the QR-code login screen of a desktop app. Its left 683 pixels are finished artwork; the rest, on the right, is only a smeared placeholder. Repaint ONLY the right part so the whole picture becomes one seamless landscape illustration with no visible seam: continue the deep indigo and royal blue night sky with faint stars and the soft Milky Way, the far blurred city lights, hills and river along the bottom edge, and the soft ribbons of light with small blank glowing comment capsules that flow from her phone toward the right. A QR-code card will be placed over the middle of the right half, so the right half stays calm, dark and fairly empty open sky: no new characters, no buildings or hills rising above the bottom 22 percent of the image, no bright objects or dense clusters of capsules in the middle of the right half. Keep the heroine, her pose, face, hair, headset, outfit, both hands, the phone, the floating sprite, the colors, lighting and art style of the left part EXACTLY the same, pixel for pixel wherever possible. Keep the same 1536x1024 size."
body="$change $(cat $D/splash/tools/prompt-anatomy.txt) $(cat $D/splash/tools/prompt-negative.txt) $(cat $T/prompt-negative-landscape.txt)"
rm -f $G/$out.png  # the success check below must not pass on an old file
prompt="Use your built-in image generation tool (not code) to create ONE edited landscape image of 1536x1024, then copy the generated PNG file to $G/$out.png. Do not write any code. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $G/extend-input.png -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
