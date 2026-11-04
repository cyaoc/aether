// Small helpers for drawing on 2D canvases, shared by the textures and the effects.
export const SANS = '"Noto Sans SC", "PingFang SC", sans-serif';
export const NUMERALS = '"Playfair Num", Georgia, serif';
export const PAD = 40;   // room around glass shapes for their outer glow

/** A canvas of the given size with `draw(context)` already run on it. */
export function paint(width, height, draw) {
  const canvas = document.createElement('canvas');
  canvas.width = Math.ceil(width);
  canvas.height = Math.ceil(height);
  draw(canvas.getContext('2d'));
  return canvas;
}

export function roundRect(g, x, y, w, h, r) {
  g.beginPath();
  g.moveTo(x + r, y);
  g.arcTo(x + w, y, x + w, y + h, r);
  g.arcTo(x + w, y + h, x, y + h, r);
  g.arcTo(x, y + h, x, y, r);
  g.arcTo(x, y, x + w, y, r);
  g.closePath();
}

/** A linear gradient whose colours are spread evenly from the start to the end. */
export function gradient(g, x0, y0, x1, y1, colors) {
  const result = g.createLinearGradient(x0, y0, x1, y1);
  colors.forEach((color, i) => result.addColorStop(i / (colors.length - 1), color));
  return result;
}

/** A radial gradient from `inner` at its centre to `outer` at the rim. */
export function glow(g, x, y, radius, inner, outer) {
  const result = g.createRadialGradient(x, y, 0, x, y, radius);
  result.addColorStop(0, inner);
  result.addColorStop(1, outer);
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
