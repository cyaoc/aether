#!/bin/zsh
# clean-plate.sh <art> <out-dir> <name>: remove the heroine from a launch art and paint what was behind her.
set -e
G=${2:A}; src=${1:A}; out=$3
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to EDIT the attached image, then copy the resulting PNG file to $G/$out.png. Do not write any code. Remove the girl COMPLETELY, together with her companion sprite if there is one (the little cloud creature with a pink castle crown, wherever it is: on her shoulder, by her face or on her phone), and paint what is behind them as if they had never been there: continue the deep indigo starry sky and the milky way; keep the soft violet, pink and warm peach afterglow glowing where it is, now glowing on its own above the horizon and the distant mountains; continue the thin ribbons of light and the small blank glowing capsules that passed behind her; continue the distant mountains, the city lights, the hillside foliage and the glowing railing in the lower right. Keep everything else exactly the same, same size, same composition, same colors. There is no person and no sprite anywhere: no hair, no hands, no clothing, no little creature, no crown, no silhouette, no shadow."
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $src -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
