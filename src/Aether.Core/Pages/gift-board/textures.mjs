// The board's textures, drawn once on 2D canvases (design package GB1): the glass cards and pills, the rank numerals, the
// title, the names and counts, the sprite and the star. The local pictures come in as loaded images.
import { CARDS, LAYOUT } from './layout.mjs';
import { NUMERALS, PAD, SANS, fitText, gradient, paint, roundRect, seeded } from './paint.mjs';

// Gold, silver-white and ice-blue rims: the cards also differ in size and numeral, never in hue alone.
export const RIM = [
  { stops: ['#fff3c8', '#f6cb62', '#d8962a'], glow: 'rgba(255,196,80,0.85)', numerals: ['#fff6d6', '#f7c95a', '#c9851d'], ring: 0xf6cb62, tint: ['rgba(255,196,90,0.42)', 'rgba(255,196,90,0)'], width: 2.8, blur: 22 },
  { stops: ['#ffffff', '#e3e9f4', '#a9b5cb'], glow: 'rgba(225,235,255,0.7)', numerals: ['#ffffff', '#e2e8f3', '#9ea9bf'], ring: 0xe3e9f4, tint: ['rgba(235,242,255,0.26)', 'rgba(235,242,255,0)'], width: 2.2, blur: 15 },
  { stops: ['#eaf3ff', '#9ec6ff', '#5b8cff'], glow: 'rgba(120,170,255,0.8)', numerals: ['#f2f8ff', '#a8ccff', '#5b8cff'], ring: 0x9ec6ff, tint: ['rgba(110,165,255,0.34)', 'rgba(110,165,255,0)'], width: 2.2, blur: 15 }
];
// Names are drawn at 28 px and scaled to 29, 27 or 23 px; a card leaves a name 150 px, a pill 156 px.
export const NAME_WIDTH = { card: 150 * 28 / 29, row: 156 * 28 / 23 };
const ROW_NUMERAL = ['#ffffff', '#dfe6ff', '#b8c6ff'];
const AVATAR_COLORS = [['#8fb2ff', '#3a4fd8'], ['#ffc2d9', '#d0568a'], ['#a8f0e0', '#2c9c86'], ['#ffd9a0', '#d4862e'], ['#d6c2ff', '#7a4fd8'], ['#b9e4ff', '#2f86c9']];

const titleTexture = () => PIXI.Texture.from(paint(300, 80, g => {
  g.font = '800 40px ' + SANS;
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.letterSpacing = '6px';
  g.shadowColor = 'rgba(16,24,110,0.9)';
  g.shadowBlur = 8;
  g.shadowOffsetY = 2;
  g.fillStyle = '#ffffff';
  g.fillText('礼物榜', 153, 42);
  g.shadowColor = 'rgba(190,210,255,0.9)';
  g.shadowBlur = 14;
  g.shadowOffsetY = 0;
  g.fillText('礼物榜', 153, 42);
}));

// A soft shadow under a card or pill so it floats above the stream.
function shadow(g, x, y, w, h, r, alpha) {
  g.save();
  g.filter = 'blur(9px)';
  g.fillStyle = 'rgba(6,10,40,' + alpha + ')';
  roundRect(g, x, y + 7, w, h, r);
  g.fill();
  g.restore();
}

// A dark translucent body so white text reads on bright scenes, a frost of light over it and the rank's tint on its left.
function paintCardBody(g, w, h, rim) {
  g.save();
  roundRect(g, 0, 0, w, h, 18);
  g.clip();
  g.fillStyle = 'rgba(20,28,96,0.5)';
  g.fillRect(0, 0, w, h);
  g.fillStyle = gradient(g, 0, 0, 0, h, ['rgba(255,255,255,0.22)', 'rgba(255,255,255,0.08)', 'rgba(255,255,255,0.12)']);
  g.fillRect(0, 0, w, h);
  const tint = g.createRadialGradient(60, h / 2, 6, 60, h / 2, w * 0.7);
  tint.addColorStop(0, rim.tint[0]);
  tint.addColorStop(1, rim.tint[1]);
  g.fillStyle = tint;
  g.fillRect(0, 0, w, h);
  g.fillStyle = gradient(g, 0, 0, 0, h * 0.45, ['rgba(255,255,255,0.20)', 'rgba(255,255,255,0)']);
  g.fillRect(0, 0, w, h * 0.45);
  g.restore();
}

// The rank's metal rim with its glow, and a thin white line inside it.
function paintCardRim(g, w, h, rim) {
  g.shadowColor = rim.glow;
  g.shadowBlur = rim.blur;
  g.lineWidth = rim.width;
  g.strokeStyle = gradient(g, 0, 0, w, h, rim.stops);
  roundRect(g, 1.2, 1.2, w - 2.4, h - 2.4, 17);
  g.stroke();
  g.stroke();
  g.shadowBlur = 0;
  g.lineWidth = 1;
  g.strokeStyle = 'rgba(255,255,255,0.32)';
  roundRect(g, 5, 5, w - 10, h - 10, 13);
  g.stroke();
}

const cardTexture = rank => PIXI.Texture.from(paint(LAYOUT.contentWidth + PAD * 2, CARDS[rank].h + PAD * 2, g => {
  const w = LAYOUT.contentWidth, h = CARDS[rank].h;
  g.translate(PAD, PAD);
  shadow(g, 4, 2, w - 8, h - 4, 18, 0.45);
  paintCardBody(g, w, h, RIM[rank]);
  paintCardRim(g, w, h, RIM[rank]);
}));

const rowTexture = () => PIXI.Texture.from(paint(LAYOUT.contentWidth + PAD * 2, LAYOUT.rowHeight + PAD * 2, g => {
  const w = LAYOUT.contentWidth, h = LAYOUT.rowHeight;
  g.translate(PAD, PAD);
  shadow(g, 4, 0, w - 8, h - 2, h / 2, 0.4);
  roundRect(g, 0, 0, w, h, h / 2);
  g.fillStyle = gradient(g, 0, 0, 0, h, ['rgba(30,42,140,0.66)', 'rgba(14,20,80,0.66)']);
  g.fill();
  g.fillStyle = gradient(g, 0, 0, 0, h / 2, ['rgba(255,255,255,0.16)', 'rgba(255,255,255,0)']);
  roundRect(g, 2, 2, w - 4, h / 2, h / 2 - 2);
  g.fill();
  g.shadowColor = 'rgba(150,180,255,0.7)';
  g.shadowBlur = 8;
  g.lineWidth = 1.6;
  g.strokeStyle = gradient(g, 0, 0, w, 0, ['rgba(225,235,255,0.95)', 'rgba(170,195,255,0.75)', 'rgba(225,235,255,0.9)']);
  roundRect(g, 0.8, 0.8, w - 1.6, h - 1.6, h / 2 - 0.8);
  g.stroke();
}));

// Rank numerals: big slanted italics; the top three get a thin slanted divider after them.
const numeralTexture = (n, colors, size, divider) => PIXI.Texture.from(paint(size * (divider ? 1.9 : 1.4), size * 1.4, g => {
  g.font = 'italic 800 ' + size + 'px ' + NUMERALS;
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.shadowColor = 'rgba(10,16,80,0.7)';
  g.shadowBlur = 10;
  g.fillStyle = gradient(g, 0, size * 0.25, 0, size * 1.15, colors);
  g.fillText(String(n), size * 0.7, size * 0.74);
  if (!divider) return;
  g.shadowBlur = 0;
  g.strokeStyle = 'rgba(255,255,255,0.55)';
  g.lineWidth = 2;
  g.beginPath();
  g.moveTo(size * 1.42, size * 0.42);
  g.lineTo(size * 1.24, size * 1.08);
  g.stroke();
}));

function nameTexture(text, maxWidth) {
  const probe = paint(1, 1, () => {}).getContext('2d');
  probe.font = '500 28px ' + SANS;
  const shown = fitText(probe, text, maxWidth);
  return PIXI.Texture.from(paint(probe.measureText(shown).width + 16, 48, g => {
    g.font = '500 28px ' + SANS;
    g.textBaseline = 'middle';
    g.shadowColor = 'rgba(10,16,90,0.8)';
    g.shadowBlur = 6;
    g.fillStyle = '#ffffff';
    g.fillText(shown, 8, 25);
  }));
}

const countTexture = n => PIXI.Texture.from(paint(120, 48, g => {
  g.font = '800 26px ' + SANS;
  g.textAlign = 'right';
  g.textBaseline = 'middle';
  g.shadowColor = 'rgba(10,16,90,0.8)';
  g.shadowBlur = 6;
  g.fillStyle = '#ffffff';
  g.fillText('×' + n, 112, 25);
}));

// A viewer without a picture gets the first character of the name on a colour the name always gets.
const initialTexture = name => PIXI.Texture.from(paint(128, 128, g => {
  const random = seeded(name), [from, to] = AVATAR_COLORS[Math.floor(random() * AVATAR_COLORS.length)];
  g.fillStyle = gradient(g, 20, 10, 108, 118, [from, to]);
  g.beginPath();
  g.arc(64, 64, 62, 0, Math.PI * 2);
  g.fill();
  g.font = '800 58px ' + SANS;
  g.textAlign = 'center';
  g.textBaseline = 'middle';
  g.fillStyle = '#ffffff';
  g.fillText([...name][0], 64, 68);
}));

// The app's chat-bubble sprite: glossy white, two upright royal-blue pill eyes, no mouth.
const spriteTexture = blink => PIXI.Texture.from(paint(160, 160, g => {
  g.translate(80, 76);
  g.shadowColor = 'rgba(160,190,255,0.9)';
  g.shadowBlur = 24;
  g.fillStyle = '#ffffff';
  roundRect(g, -54, -42, 108, 84, 30);
  g.fill();
  g.beginPath();
  g.moveTo(-36, 30);
  g.lineTo(-46, 58);
  g.lineTo(-12, 38);
  g.closePath();
  g.fill();
  g.shadowBlur = 0;
  g.fillStyle = gradient(g, -54, -42, 54, 42, ['#ffffff', '#e8eeff']);
  roundRect(g, -54, -42, 108, 84, 30);
  g.fill();
  g.fillStyle = '#3d52f0';
  for (const x of [-18, 18]) {
    if (blink) roundRect(g, x - 7, -2, 14, 6, 3);
    else roundRect(g, x - 7, -20, 14, 36, 7);
    g.fill();
  }
}));

const starTexture = () => PIXI.Texture.from(paint(128, 128, g => {
  const c = 64, halo = g.createRadialGradient(c, c, 0, c, c, 60);
  halo.addColorStop(0, 'rgba(255,255,255,0.9)');
  halo.addColorStop(0.2, 'rgba(200,215,255,0.35)');
  halo.addColorStop(1, 'rgba(160,180,255,0)');
  g.fillStyle = halo;
  g.fillRect(0, 0, 128, 128);
  g.fillStyle = '#fff';
  g.beginPath();
  g.moveTo(c, 6);
  g.quadraticCurveTo(c + 5, c - 5, c + 58, c);
  g.quadraticCurveTo(c + 5, c + 5, c, 122);
  g.quadraticCurveTo(c - 5, c + 5, c - 58, c);
  g.quadraticCurveTo(c - 5, c - 5, c, 6);
  g.fill();
}));

// The picture cropped to the pixels that are not transparent (sampled every other pixel).
function trimmed(image) {
  const canvas = paint(image.width, image.height, g => g.drawImage(image, 0, 0));
  const { data } = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height);
  let left = canvas.width, top = canvas.height, right = 0, bottom = 0;
  for (let y = 0; y < canvas.height; y += 2)
    for (let x = 0; x < canvas.width; x += 2) {
      if (data[(y * canvas.width + x) * 4 + 3] <= 20) continue;
      left = Math.min(left, x);
      top = Math.min(top, y);
      right = Math.max(right, x);
      bottom = Math.max(bottom, y);
    }
  return PIXI.Texture.from(paint(right - left + 2, bottom - top + 2, g => g.drawImage(canvas, -left, -top)));
}

const memo = make => {
  const made = new Map();
  return (...args) => {
    const key = args.join('\u0001');
    if (!made.has(key)) made.set(key, make(...args));
    return made.get(key);
  };
};

/** Everything the scene draws with; `local` holds the loaded banner and mascot images. Names, counts and the like are made on
 * first use and kept. */
export function createTextures(local) {
  return {
    title: titleTexture(), cards: CARDS.map((_, rank) => cardTexture(rank)), row: rowTexture(), star: starTexture(),
    sprite: spriteTexture(false), spriteBlink: spriteTexture(true),
    cardNumerals: CARDS.map((_, rank) => numeralTexture(rank + 1, RIM[rank].numerals, 70, true)),
    rowNumeral: memo(rank => numeralTexture(rank, ROW_NUMERAL, 30, false)),
    banner: trimmed(local.banner), idle: PIXI.Texture.from(local.idle), cheer: PIXI.Texture.from(local.cheer),
    name: memo(nameTexture), count: memo(countTexture), initial: memo(initialTexture)
  };
}
