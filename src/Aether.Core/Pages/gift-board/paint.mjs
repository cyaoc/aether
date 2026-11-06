// Small helpers for drawing on 2D canvases, shared by the textures and the effects.
import { k } from '../video/animations/toolkit.mjs';

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

// A name's window fades out over this many texture pixels at an edge with more of the name hidden beyond it.
const NAME_FADE = 20;

/** How strongly a window `width` wide, showing a name `whole` wide from `offset` along, fades out at its left and right edges:
 * fully once NAME_FADE pixels of the name are hidden beyond an edge, less as fewer are (design package README, 长名字和头像框间距). */
export const nameFades = (whole, width, offset) => ({ left: k(0, NAME_FADE, offset), right: k(0, NAME_FADE, whole - width - offset) });

/** Draws on `g`, a canvas `width` + 16 wide, the part of the whole name `name` (`{ canvas, width }`, drawn 8 px in from its left)
 * that a window `width` wide shows from `offset` along; the 8 px either side keep the text's shadow. Ported from GB1's `drawWindow`. */
export function drawNameWindow(g, name, width, offset) {
  const { left, right } = nameFades(name.width, width, offset), { width: w, height: h } = g.canvas;
  g.clearRect(0, 0, w, h);
  g.drawImage(name.canvas, -offset, 0);
  g.globalCompositeOperation = 'destination-out';
  // Untouched at `from`, cut away by `strength` at the edge `to`, and as much again over the margin `[x, x + margin]` beyond it.
  const fade = (from, to, strength, x, margin) => {
    g.fillStyle = gradient(g, [from, 0], [to, 0], ['rgba(0,0,0,0)', 'rgba(0,0,0,' + strength + ')']);
    g.fillRect(Math.min(from, to), 0, NAME_FADE, h);
    g.fillStyle = 'rgba(0,0,0,' + strength + ')';
    g.fillRect(x, 0, margin, h);
  };
  if (right > 0) fade(8 + width - NAME_FADE, 8 + width, right, 8 + width, w);
  if (left > 0) fade(8 + NAME_FADE, 8, left, 0, 8);
  g.globalCompositeOperation = 'source-over';
}
