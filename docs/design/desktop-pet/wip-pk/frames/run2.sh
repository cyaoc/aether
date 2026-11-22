#!/bin/zsh
# the PK sign's loops and ducking behind it (user: the first loops flashed, “没有过度帧”): each frame a little from the last, the sway in two strips, ducking
# behind the sign. textures/pose-18-sign.webp must be codex's sheet with its blank sign while these run (README 15)
cd ${0:A:h}/../..
F=wip-pk/frames
run() { local n=$1; shift; uv run --with pillow --with numpy python -I "$@" > $F/$n.log 2>&1; echo "done $n: $(tail -1 $F/$n.log)"; }
run signSwayL tools/gen-loop.py signUp $F/signSwayL &
run signSwayR tools/gen-loop.py signUp $F/signSwayR --strip signSwayR &
run signBounce2 tools/gen-loop.py signCheer $F/signBounce2 &
run signPump2 tools/gen-loop.py signUrge $F/signPump2 &
run signHideIn tools/inbetween.py signHideIn idle signHide $F/signHideIn --tries 3 &
wait
