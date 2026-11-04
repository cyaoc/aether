// The light the events add: sparks, a comet, light ribbons, rays and confetti, drawn on three 2D canvases as big as the page
// (design package GB1). Everything is drawn in design pixels and clipped to the board, so nothing leaves it.
import { CARDS, LAYOUT, spriteHome } from './layout.mjs';
import { EVENTS, easeInOut, flare } from './motion.mjs';
import { canvasOf, glow, k, mix, out, rnd } from '../video/animations/toolkit.mjs';

const GOLD = '255,226,160', ICE = '190,210,255';
const CONFETTI = ['#FFD36B', '#FFFFFF', '#6F86FF', '#FF9EC4', '#9FE3FF'];

class Layer {
  /** `blend` is 'add' for light that sums up and 'normal' for confetti that keeps its colours. */
  constructor(width, height, blend) {
    this.canvas = canvasOf(width, height, () => {});
    this.context = this.canvas.getContext('2d');
    this.texture = PIXI.Texture.from(this.canvas);
    this.sprite = new PIXI.Sprite(this.texture);
    this.sprite.blendMode = blend;
    this.composite = blend === 'add' ? 'lighter' : 'source-over';
  }

  begin(layout) {
    const g = this.context;
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.clearRect(0, 0, layout.width, layout.height);
    g.setTransform(layout.scale, 0, 0, layout.scale, layout.x, 0);
    g.save();
    g.beginPath();
    g.rect(0, 0, LAYOUT.width, layout.designHeight);
    g.clip();
    g.globalCompositeOperation = this.composite;
  }

  end() {
    this.context.restore();
    this.context.globalCompositeOperation = 'source-over';
    this.texture.source.update();
  }

  destroy() {
    this.sprite.destroy({ texture: true, textureSource: true });
  }
}

export class Effects {
  /** The rays go under everything on the stage, the confetti and the light over it. */
  constructor(stage, width, height) {
    this.rays = new Layer(width, height, 'add');
    this.confetti = new Layer(width, height, 'normal');
    this.light = new Layer(width, height, 'add');
    this.layers = [this.rays, this.confetti, this.light];
    this.g = this.light.context;
    stage.addChildAt(this.rays.sprite, 0);
    stage.addChild(this.confetti.sprite, this.light.sprite);
  }

  /** Draws the running event's effects `u` ms into it, following the climbing row `riser` (as the scene placed it). The canvases
   * are only cleared and uploaded while an event runs, and once more when it ends. */
  draw(layout, event, u, riser) {
    if (!event && !this.drawn) return;
    this.drawn = Boolean(event);
    Object.assign(this, { layout, u });
    for (const layer of this.layers) layer.begin(layout);
    if (event && riser) this.paintEvent(riser, event);
    for (const layer of this.layers) layer.end();
  }

  paintEvent(riser, event) {
    switch (event.kind) {
      case 'new': return this.newRow(riser);
      case 'more': return this.more(riser);
      case 'pass': return this.pass(riser, event);
      default: return this.top(riser, event);
    }
  }

  sparkle(x, y, scale, alpha) {
    if (alpha <= 0) return;
    const { g } = this, r = 26 * scale, halo = g.createRadialGradient(x, y, 0, x, y, r);
    halo.addColorStop(0, 'rgba(255,255,255,' + alpha + ')');
    halo.addColorStop(0.25, 'rgba(190,210,255,' + alpha * 0.4 + ')');
    halo.addColorStop(1, 'rgba(150,180,255,0)');
    g.fillStyle = halo;
    g.beginPath();
    g.arc(x, y, r, 0, Math.PI * 2);
    g.fill();
    g.fillStyle = 'rgba(255,255,255,' + alpha + ')';
    g.beginPath();
    g.moveTo(x, y - r);
    g.quadraticCurveTo(x + r * 0.08, y - r * 0.08, x + r, y);
    g.quadraticCurveTo(x + r * 0.08, y + r * 0.08, x, y + r);
    g.quadraticCurveTo(x - r * 0.08, y + r * 0.08, x - r, y);
    g.quadraticCurveTo(x - r * 0.08, y - r * 0.08, x, y - r);
    g.fill();
  }

  /** A few sparks flying out of (x, y) from `at` ms for `life` ms. */
  burst({ seed, x, y, at, count, life, spread }) {
    const { u } = this;
    if (u < at || u > at + life) return;
    const p = (u - at) / life;
    for (let i = 0; i < count; i++) {
      const angle = rnd(i, seed) * Math.PI * 2, distance = spread * (0.4 + 0.6 * rnd(i, seed + 1)) * out(p);
      this.sparkle(x + Math.cos(angle) * distance, y + Math.sin(angle) * distance * 0.7, (0.35 + 0.4 * rnd(i, seed + 2)) * (1 - p * 0.5), 1 - p);
    }
  }

  // A light capsule with a tail of 24 fading dots along a path.
  comet(path, head, length, alpha) {
    const { g } = this;
    for (let i = 0; i < 24; i++) {
      const q = head - i * length / 24;
      if (q < 0) break;
      const [x, y] = path(q), fade = 1 - i / 24;
      g.fillStyle = 'rgba(200,215,255,' + 0.45 * alpha * fade + ')';
      g.beginPath();
      g.arc(x, y, 3 + 13 * fade, 0, Math.PI * 2);
      g.fill();
    }
    const [x, y] = path(head);
    g.fillStyle = 'rgba(255,255,255,' + alpha + ')';
    g.beginPath();
    g.ellipse(x, y, 26, 9, 0, 0, Math.PI * 2);
    g.fill();
    this.sparkle(x, y, 1.1, alpha);
  }

  // A smooth line through the points, drawn three times: wide and faint, medium, thin and bright.
  ribbon(points, width, alpha, color) {
    if (alpha <= 0) return;
    const { g } = this, last = points[points.length - 1];
    g.lineCap = 'round';
    g.beginPath();
    g.moveTo(points[0][0], points[0][1]);
    for (let i = 1; i < points.length - 1; i++)
      g.quadraticCurveTo(points[i][0], points[i][1], (points[i][0] + points[i + 1][0]) / 2, (points[i][1] + points[i + 1][1]) / 2);
    g.lineTo(last[0], last[1]);
    for (const [lineWidth, strength] of [[width * 6, 0.12], [width * 2.4, 0.3], [width, 0.9]]) {
      g.lineWidth = lineWidth;
      g.strokeStyle = 'rgba(' + color + ',' + alpha * strength + ')';
      g.stroke();
    }
  }

  // The sprite fires a capsule of light like a danmaku; it arcs down to the slot and unfolds into the row.
  newRow({ g: row }) {
    const { u, g } = this, [fromX, fromY] = spriteHome(this.layout), toX = LAYOUT.contentX + 30, toY = row.y + row.h / 2;
    const path = q => [mix(fromX, toX, easeInOut(q)), mix(fromY, toY, easeInOut(q)) - 60 * Math.sin(Math.PI * q)];
    const fly = k(200, 640, u), sweep = k(640, 1000, u);
    if (fly > 0 && u < 760) this.comet(path, fly, 0.45, 1 - k(640, 760, u));
    if (sweep > 0 && sweep < 1) {
      const x = LAYOUT.contentX + LAYOUT.contentWidth * out(sweep), shine = g.createLinearGradient(x - 120, 0, x, 0);
      shine.addColorStop(0, 'rgba(200,215,255,0)');
      shine.addColorStop(1, 'rgba(230,238,255,0.65)');
      g.fillStyle = shine;
      g.fillRect(x - 120, row.y + 2, 120, row.h - 4);
    }
    this.burst({ seed: 7, x: LAYOUT.contentX + LAYOUT.contentWidth - 30, y: toY, at: 980, count: 6, life: 600, spread: 50 });
  }

  more({ g: row }) {
    this.burst({ seed: 13, x: row.cx - 24, y: row.cy, at: 0, count: 7, life: 700, spread: 50 });
  }

  // The climber rides a light trail along the list's right edge, inside the board's margin, and sparks fly where it lands.
  pass(riser, event) {
    this.trail(riser, event, ICE);
    this.burst({ seed: 19, x: riser.g.cx - 30, y: riser.g.cy, at: 0, count: 6, life: 600, spread: 46 });
    this.burst({ seed: 23, x: LAYOUT.contentX + LAYOUT.contentWidth - 20, y: riser.to.y + riser.to.h / 2, at: 800, count: 5, life: 500, spread: 36 });
  }

  top(riser, event) {
    const middle = [LAYOUT.width / 2, this.layout.top + LAYOUT.cardsY + CARDS[0].h / 2];
    // A row with no place to climb from is fired at its slot by the sprite instead.
    if (event.debut) this.newRow(riser);
    else {
      this.trail(riser, event, GOLD);
      this.burst({ seed: 29, x: riser.g.cx - 30, y: riser.g.cy, at: 0, count: 6, life: 600, spread: 46 });
    }
    this.spiral(middle);
    this.bloom(middle);
    if (this.layout.mascot) this.cheer();
    this.shine();
  }

  trail({ from, to }, { kind }, color) {
    const [start, end] = EVENTS[kind].move, { u } = this, progress = k(start, end, u);
    if (progress <= 0 || u >= end + 400) return;
    const points = [];
    for (let i = 0; i <= 24; i++) {
      const q = i / 24 * Math.min(1, progress);
      points.push([LAYOUT.contentX + LAYOUT.contentWidth + 4 + 12 * Math.sin(Math.PI * q), mix(from.y + from.h / 2, to.y + to.h / 2, q)]);
    }
    this.ribbon(points, 2, 0.8 * (1 - k(end, end + 400, u)), color);
  }

  // Three ribbons of light spiral in around the first card.
  spiral(middle) {
    const progress = k(900, 1650, this.u);
    if (progress <= 0 || progress >= 1) return;
    for (let r = 0; r < 3; r++) {
      const points = [];
      for (let i = 0; i <= 30; i++) {
        const angle = r * 2.09 + progress * 7 + i * 0.12, radius = mix(205, 30, out(progress)) * (0.7 + 0.3 * i / 30);
        points.push([middle[0] + Math.cos(angle) * radius * 1.1, middle[1] + Math.sin(angle) * radius * 0.42]);
      }
      this.ribbon(points, 2.2, 0.9 * Math.sin(Math.PI * progress), r === 0 ? GOLD : ICE);
    }
  }

  // The gold rim flares (the scene lights the card) and a warm glow and a burst of sparks open behind it.
  bloom(middle) {
    const { g, u } = this;
    glow(g, middle[0], middle[1], 220, '255,230,170', 0.3 * flare(u), '255,200,120');
    this.burst({ seed: 37, x: middle[0], y: middle[1], at: 1620, count: 12, life: 1100, spread: 170 });
  }

  // The cheer fills the space above the ribbon: slow rays and a halo behind the mascot, confetti and stars thrown up from
  // behind the ribbon, and a ring of sparkles round the sprite.
  cheer() {
    this.rayLight();
    this.confettiShower();
    const ring = k(950, 1150, this.u) * (1 - k(1500, 1750, this.u)), [x, y] = spriteHome(this.layout);
    if (ring <= 0) return;
    for (let i = 0; i < 6; i++) {
      const angle = i * Math.PI / 3 + this.u * 0.006;
      this.sparkle(x + Math.cos(angle) * 46, y + Math.sin(angle) * 40, 0.32, ring);
    }
  }

  rayLight() {
    const g = this.rays.context, { u } = this, strength = k(880, 1080, u) * (1 - k(2500, 3100, u));
    if (strength <= 0) return;
    g.save();
    g.translate(LAYOUT.width / 2, this.layout.top - 96);
    g.rotate(u * 0.0004);
    for (let i = 0; i < 14; i++) {
      const start = i * Math.PI * 2 / 14, ray = g.createRadialGradient(0, 0, 10, 0, 0, 250);
      ray.addColorStop(0, 'rgba(255,240,200,' + 0.42 * strength + ')');
      ray.addColorStop(1, 'rgba(255,220,150,0)');
      g.fillStyle = ray;
      g.beginPath();
      g.moveTo(0, 0);
      g.arc(0, 0, 250, start - 0.09, start + 0.09);
      g.closePath();
      g.fill();
    }
    glow(g, 0, 0, 150, '255,245,220', 0.4 * strength, '255,230,180');
    g.restore();
  }

  // 70 pieces thrown up from behind the ribbon, one in four a star, falling back and fading out.
  confettiShower() {
    const g = this.confetti.context, { u } = this, start = 950, life = 2300;
    if (u < start || u >= start + life) return;
    const t = (u - start) / 1000, fade = 1 - k(start + life * 0.7, start + life, u);
    for (let i = 0; i < 70; i++) {
      const x = 150 + 180 * rnd(i, 71) + (rnd(i, 72) - 0.5) * 560 * t + 16 * Math.sin(t * 7 + i);
      const y = this.layout.top + 24 - (560 + 480 * rnd(i, 73)) * t + 0.5 * 1100 * t * t;
      if (i % 4 === 0) {
        this.sparkle(x, y, 0.3 + 0.25 * rnd(i, 74), fade);
        continue;
      }
      g.save();
      g.translate(x, y);
      g.rotate(t * (4 + 6 * rnd(i, 75)) + i);
      g.scale(1, Math.abs(Math.cos(t * 9 + i)) + 0.15);
      g.globalAlpha = fade;
      g.fillStyle = CONFETTI[i % CONFETTI.length];
      g.fillRect(-5, -2.5, 10, 5);
      g.restore();
    }
  }

  // A light runs along the ribbon from left to right.
  shine() {
    const { g } = this, progress = k(950, 1350, this.u);
    if (progress <= 0 || progress >= 1) return;
    const x = mix(40, 440, easeInOut(progress)), y = this.layout.top + 8, light = g.createLinearGradient(x - 40, 0, x + 40, 0);
    light.addColorStop(0, 'rgba(255,255,255,0)');
    light.addColorStop(0.5, 'rgba(255,255,255,0.6)');
    light.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = light;
    g.beginPath();
    g.moveTo(x - 30, y);
    g.lineTo(x + 50, y);
    g.lineTo(x + 30, y + 64);
    g.lineTo(x - 50, y + 64);
    g.closePath();
    g.fill();
  }

  destroy() {
    for (const layer of this.layers) layer.destroy();
  }
}
