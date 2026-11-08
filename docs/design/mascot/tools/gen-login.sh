#!/bin/zsh
# gen-login.sh <out-dir> <name>: the login-screen illustration of her, same place and hour as the launch art.
set -e
T=${0:A:h}; P=${T:h}; G=${1:A}; out=$2
body="$(cat $T/login-base.txt) $(cat $T/login-pose.txt) $(cat $T/login-sky.txt) $(cat $T/scene-anatomy.txt) $(cat $T/scene-negative.txt) $(cat $T/login-negative.txt)"
rm -f $G/$out.png
prompt="Use your built-in image generation tool (not code) to create ONE image, then copy the generated PNG file to $G/$out.png. Do not write any code. The first attached image is her launch art: keep her face, hair, SSR rendering, the place and the starry sky with the afterglow from it, but not its pose or framing. The second attached image is her design turnaround: her identity and outfit must match it. Image: $body"
print -r -- "$prompt" | codex exec --skip-git-repo-check -s workspace-write -C $G -i $P/refs/splash-art.webp -i $P/textures/sheet-turnaround.webp -o $G/$out.log.txt
[[ -f $G/$out.png ]] && echo "OK $out" || { echo "NO OUTPUT $out"; exit 1; }
