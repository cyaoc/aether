#!/bin/zsh
# the PK sign's third round (user: still flashing where she raises and lowers it): she raises and lowers her arms empty,
# the board moves the sign itself. Drawn from the wiped sheet (README 15)
cd ${0:A:h}/../..
F=wip-pk/frames
run() { local n=$1; shift; uv run --with pillow --with numpy python -I "$@" > $F/$n.log 2>&1; echo "done $n: $(tail -1 $F/$n.log)"; }
run signRaise tools/inbetween.py signRaise idle signUp $F/signRaise --tries 3 &
run signCheerRaise tools/inbetween.py signCheerRaise idle signCheer $F/signCheerRaise --tries 3 &
run signUrgeRaise tools/inbetween.py signUrgeRaise idle signUrge $F/signUrgeRaise --tries 3 &
wait
