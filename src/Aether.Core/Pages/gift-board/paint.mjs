// Small helpers for drawing on 2D canvases, shared by the textures and the effects.
export const SANS = '"Noto Sans SC", "PingFang SC", sans-serif';
export const NUMERALS = '"Playfair Num", Georgia, serif';
export const PAD = 40;   // room around glass shapes for their outer glow

/** The path of a rounded rectangle `[x, y, w, h]` with corners of radius `r`. */
export function roundRect(g, [x, y, w, h], r) {
  g.beginPath();
  g.moveTo(x + r, y);
  g.arcTo(x + w, y, x + w, y + h, r);
  g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r);
  g.arcTo(x, y, x + w, y, r);
  g.closePath();
}

/** A linear gradient from point `from` to point `to` whose colours are spread evenly along it. */
export function gradient(g, [x0, y0], [x1, y1], colors) {
  const result = g.createLinearGradient(x0, y0, x1, y1);
  colors.forEach((color, i) => result.addColorStop(i / (colors.length - 1), color));
  return result;
}

/** A repeatable pseudo-random generator for a text, so a name always gets the same avatar colours. */
export function seeded(text) {
  let h = 2166136261;
  for (const ch of String(text)) h = Math.imul(h ^ ch.charCodeAt(0), 16777619);
  return () => {
    h = Math.imul(h ^ (h >>> 15), 2246822507);
    h = Math.imul(h ^ (h >>> 13), 3266489909);
    h ^= h >>> 16;
    return (h >>> 0) / 4294967296;
  };
}

/** The text, or its beginning and an ellipsis, so that it is at most `maxWidth` wide in the font the context has now. */
export function fitText(g, text, maxWidth) {
  if (g.measureText(text).width <= maxWidth) return text;
  let cut = text;
  while (cut.length > 1 && g.measureText(cut + '…').width > maxWidth) cut = cut.slice(0, -1);
  return cut + '…';
}
