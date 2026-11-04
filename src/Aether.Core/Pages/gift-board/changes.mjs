// Which animation a pushed state calls for, and in what order the changes are played.
const BACKLOG_LIMIT = 3;   // more changes than this waiting jump to the latest state
const REDRAW = { kind: 'redraw' };

/** One gift changes one row: it appears (`new`, or `top` with `debut` when it appears in first place), its count grows (`more`), it
 * climbs (`pass`), or it takes first place (`top`). Anything else, such as rows removed, several rows changed or the mascot
 * switched, is redrawn without animation, and a state that shows the same board is nothing. A state that no gift made is redrawn
 * too, even when it happens to change one row: a restore or a new threshold must not cheer. */
function compare(before, after) {
  if (before.mascot !== after.mascot) return REDRAW;
  const earlier = new Map(before.rows.map((row, rank) => [row.id, { num: row.num, rank }]));
  const changed = after.rows.map((row, rank) => ({ row, rank, old: earlier.get(row.id) })).filter(({ row, old }) => old?.num !== row.num);
  const kept = after.rows.filter(row => earlier.has(row.id)).length;
  if (kept < before.rows.length || changed.length > 1) return REDRAW;
  if (changed.length === 0) return after.rows.every((row, rank) => earlier.get(row.id).rank === rank) ? undefined : REDRAW;
  if (!after.fromGift) return REDRAW;
  const [{ row, rank, old }] = changed;
  if (!old) return rank === 0 ? { kind: 'top', id: row.id, debut: true } : { kind: 'new', id: row.id };
  if (row.num < old.num) return REDRAW;
  return { kind: rank === old.rank ? 'more' : rank === 0 ? 'top' : 'pass', id: row.id };
}

/** The changes between the states pushed so far, oldest first. */
export class Changes {
  constructor() {
    this.reset();
  }

  /** A new connection starts over: its first state is drawn as it is. */
  reset() {
    this.last = undefined;
    this.waiting = [];
  }

  receive(state) {
    const change = this.last ? compare(this.last, state) : REDRAW;
    this.last = state;
    if (!change) return;
    this.waiting.push({ ...change, state });
    if (this.waiting.length > BACKLOG_LIMIT) this.waiting = [{ ...REDRAW, state }];
  }

  take() {
    return this.waiting.shift();
  }
}
