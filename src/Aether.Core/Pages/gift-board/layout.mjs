// The board is one component, 480 design pixels wide; every number here is measured from its top-left corner (design package README, GB1).
export const LAYOUT = {
  width: 480,
  contentX: 20, contentWidth: 440,   // the top three and the rows share this column and the inner columns below
  columns: { rank: 62, avatar: 148, name: 192, gift: 376, count: 442 },
  rowHeight: 50, rowPitch: 60,
  mascotZone: 220,                   // room above the ribbon for the mascot; without her the list moves up into it
  zoneWithoutMascot: 8,
  minHeight: 820,                    // the mascot, the ribbon, the top three and three rows; a shorter page scales the board down
  minHeightWithoutMascot: 608,
  bannerCenter: 40, bannerWidth: 380, cardsY: 92, rowsY: 416, bottom: 12,
  fadeTop: 24, fadeBottom: 36        // a scrolling list fades out over this many pixels at its top and bottom
};
export const CARDS = [{ y: 0, h: 98 }, { y: 110, h: 90 }, { y: 212, h: 90 }];
export const TOP_COUNT = CARDS.length;

// Avatar radius, name size, icon size and count scale by rank: the first card, the other two cards, a row.
const SIZES = [{ ar: 33, ns: 29, is: 46, cs: 1.05 }, { ar: 30, ns: 27, is: 42, cs: 0.95 }, { ar: 30, ns: 27, is: 42, cs: 0.95 }, { ar: 19, ns: 23, is: 34, cs: 0.84 }];

/** Lays the board out from the page's own size: the width sets the scale and the height sets how many rows fit. A page
 * shorter than the minimum board scales it down by its height and centres it. */
export function fitLayout(width, height, mascot) {
  const top = mascot ? LAYOUT.mascotZone : LAYOUT.zoneWithoutMascot;
  const scale = Math.min(width / LAYOUT.width, height / (mascot ? LAYOUT.minHeight : LAYOUT.minHeightWithoutMascot));
  const designHeight = height / scale, rowsY = top + LAYOUT.rowsY, viewHeight = designHeight - rowsY - LAYOUT.bottom;
  const fit = Math.max(1, Math.floor((viewHeight + LAYOUT.rowPitch - LAYOUT.rowHeight) / LAYOUT.rowPitch));
  return { width, height, mascot, scale, designHeight, x: (width - LAYOUT.width * scale) / 2, top, rowsY, viewHeight, fit };
}

/** Where a rank sits and how big its avatar, name, icon and count are; `tier` is the rank for the top three, 3 for a row. */
export function slotGeometry(layout, slot) {
  const { columns } = LAYOUT, tier = Math.min(slot, TOP_COUNT);
  const y = slot < TOP_COUNT ? layout.top + LAYOUT.cardsY + CARDS[slot].y : layout.rowsY + (slot - TOP_COUNT) * LAYOUT.rowPitch;
  const h = slot < TOP_COUNT ? CARDS[slot].h : LAYOUT.rowHeight, middle = y + h / 2;
  return { y, h, tier, ...SIZES[tier], ax: columns.avatar, ay: middle, nx: columns.name, ny: middle, ix: columns.gift, iy: middle, cx: columns.count, cy: middle };
}

/** The rows scroll in a loop of a lap that is all rows and one empty row; each row has two copies so the loop has no seam. */
export function lapLength(rowCount) {
  return (rowCount + 1) * LAYOUT.rowPitch;
}

/** Where the row copy at y (unscrolled) is once the list has scrolled by `scroll`: a copy that has left the top for good
 * comes back below the other one. */
export function scrolledY(layout, lap, scroll, y) {
  const moved = y - scroll;
  return moved < layout.rowsY - lap + layout.viewHeight ? moved + lap * 2 : moved;
}

/** Each row's slot: its rank counted from 0. */
export const slotsOf = rows => Object.fromEntries(rows.map((row, rank) => [row.id, rank]));

/** The same place `dy` design pixels lower. */
export const shiftedGeometry = (g, dy) => ({ ...g, y: g.y + dy, ay: g.ay + dy, ny: g.ny + dy, iy: g.iy + dy, cy: g.cy + dy });

/** Where the bubble sprite floats: by the mascot's shoulder, or on the ribbon's right end once she is switched off. */
export const spriteHome = layout => layout.mascot ? [LAYOUT.width / 2 + 118, layout.top - 150] : [448, layout.top + 34];

// A card's rim, which the top three's flowing light runs along (design package README, 前三名流光).
export const RIM_RADIUS = 17;

/** The rim of a card `[x, y, w, h]`: the card drawn in by 1.2 on every side, with corners of RIM_RADIUS. */
export const rimOf = ([x, y, w, h]) => [x + 1.2, y + 1.2, w - 2.4, h - 2.4];

/** The rim of the card of `rank` on the board. */
export const rimBox = (layout, rank) => rimOf([LAYOUT.contentX, layout.top + LAYOUT.cardsY + CARDS[rank].y, LAYOUT.contentWidth, CARDS[rank].h]);

export const rimLength = ([, , w, h]) => 2 * (w + h - 4 * RIM_RADIUS) + 2 * Math.PI * RIM_RADIUS;

/** The point `s` along the rim, clockwise from where the top edge leaves its left corner; any `s` wraps round. */
export function rimPoint(box, s) {
  const [x, y, w, h] = box, r = RIM_RADIUS, a = w - 2 * r, b = h - 2 * r, q = Math.PI * r / 2, length = rimLength(box);
  const corner = (cx, cy, from, d) => [cx + r * Math.cos(from + d / r), cy + r * Math.sin(from + d / r)];
  s = ((s % length) + length) % length;
  if (s < a) return [x + r + s, y];
  s -= a;
  if (s < q) return corner(x + w - r, y + r, -Math.PI / 2, s);
  s -= q;
  if (s < b) return [x + w, y + r + s];
  s -= b;
  if (s < q) return corner(x + w - r, y + h - r, 0, s);
  s -= q;
  if (s < a) return [x + w - r - s, y + h];
  s -= a;
  if (s < q) return corner(x + r, y + h - r, Math.PI / 2, s);
  s -= q;
  if (s < b) return [x, y + h - r - s];
  return corner(x + r, y + r, Math.PI, s - b);
}
