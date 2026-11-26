#!/bin/zsh
# run-fig.sh: the cos pose sheets with her own full figure (#418): sheets 27-38 edited in place (only her chest), the fighter's
# 39-40 drawn for the first time. The ice and Miao sheets redraw a failing cell from their ready pose (gen-sheet --start).
cd ${0:A:h}
E="her chest only: she naturally has a full, large bust (her own figure, as in the third attached design sheet), shown as it would be under the outfit she wears in that cell: rounding out a fitted top, softened under a loose jacket, coat or armor; never exaggerated, never with cleavage."
job() { local n=$1; shift; uv run --with pillow --with numpy python -I ../tools/gen-sheet.py $n sheet-$n "$@" > sheet-$n-fig.log 2>&1; echo "$n: $(tail -n1 sheet-$n-fig.log)"; }
for n in 27 28 29 30 35 36; do job $n --edit $E & done
wait
job 31 --edit $E --start icReady.png &
job 32 --edit $E --start icReady.png &
job 33 --edit $E --start miReady.png &
job 34 --edit $E --start miReady.png &
job 37 --edit $E & job 38 --edit $E &
wait
job 39 & job 40 &
wait
