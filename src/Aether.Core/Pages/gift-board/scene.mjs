// The Pixi scene of one board: the mascot, the ribbon, the top three cards, the pills and the sprite (design package GB1). It is
// built for a layout and a list of rows; every frame it is placed again from the clock, the scroll and the running event.
import { CARDS, LAYOUT, TOP_COUNT, lapLength, scrolledY, shiftedGeometry, slotGeometry, slotsOf, spriteHome } from './layout.mjs';
import { EVENTS, ambient, easeBack, easeInOut, entryMotion, flare, lerpGeometry, twinkle, unfolded } from './motion.mjs';
import { Entry } from './entry.mjs';
import { PAD } from './paint.mjs';
import { canvasOf, k, out } from '../video/animations/toolkit.mjs';

// The pose pictures are 1536 high; the body is cut off at this fraction of it, which goes behind the ribbon.
const MASCOT = { idleCut: 1352 / 1536, cheerCut: 1278 / 1536, scale: 0.17 };

export class Scene {
  /** `rows` are the board's rows in rank order; `kit` is `{ textures, images }`. */
  constructor(kit, layout, rows) {
    Object.assign(this, { kit, layout, rows });
    this.slots = slotsOf(rows);
    this.rowCount = Math.max(0, rows.length - TOP_COUNT);
    this.lap = lapLength(this.rowCount);
    this.scrolls = this.rowCount > layout.fit;
    this.root = new PIXI.Container();
    this.root.position.set(layout.x, 0);
    this.root.scale.set(layout.scale);
    // Nothing of the board, its glow and star included, is drawn beyond its own rectangle, whatever the page around it.
    const board = new PIXI.Graphics().rect(0, 0, LAYOUT.width, layout.designHeight).fill(0xffffff);
    this.root.addChild(board);
    this.root.mask = board;
    this.buildMascot();
    this.buildPanel();
    this.buildEntries();
    this.sprite = new PIXI.Sprite(kit.textures.sprite);
    this.sprite.anchor.set(0.5);
    this.root.addChild(this.sprite);
  }

  buildMascot() {
    this.mascot = new PIXI.Container();
    this.root.addChild(this.mascot);
    if (!this.layout.mascot) return;
    const { textures } = this.kit;
    this.idle = this.pose(textures.idle, MASCOT.idleCut);
    this.cheer = this.pose(textures.cheer, MASCOT.cheerCut);
  }

  pose(texture, cut) {
    const sprite = new PIXI.Sprite(texture);
    sprite.anchor.set(0.5, cut);
    sprite.scale.set(MASCOT.scale * 1536 / texture.height);
    this.mascot.addChild(sprite);
    return sprite;
  }

  buildPanel() {
    const { textures } = this.kit, { top } = this.layout;
    this.panel = new PIXI.Container();
    this.root.addChild(this.panel);
    const banner = new PIXI.Sprite(textures.banner);
    banner.anchor.set(0.5);
    banner.scale.set(LAYOUT.bannerWidth / textures.banner.width);
    banner.position.set(LAYOUT.width / 2, top + LAYOUT.bannerCenter);
    const title = new PIXI.Sprite(textures.title);
    title.anchor.set(0.5);
    title.position.set(LAYOUT.width / 2, top + LAYOUT.bannerCenter - 2);
    this.panel.addChild(banner, title);
    this.cards = CARDS.map((card, rank) => this.addCard(card, rank));
    this.buildRows();
    this.topLayer = new PIXI.Container();
    this.topLayer.sortableChildren = true;
    this.panel.addChild(this.topLayer);
  }

  addCard(card, rank) {
    const { textures } = this.kit, container = new PIXI.Container();
    container.position.set(LAYOUT.contentX, this.layout.top + LAYOUT.cardsY + card.y);
    this.panel.addChild(container);
    const plate = new PIXI.Sprite(textures.cards[rank]);
    plate.position.set(-PAD, -PAD);
    const numeral = new PIXI.Sprite(textures.cardNumerals[rank]);
    numeral.anchor.set(0.37, 0.5);
    numeral.position.set(LAYOUT.columns.rank - LAYOUT.contentX, card.h / 2 + 2);
    numeral.scale.set(rank === 0 ? 0.92 : 0.84);
    container.addChild(plate, numeral);
    if (rank === 0) this.decorateFirstCard(container);
    return container;
  }

  // The first card flares gold and its four-pointed star blooms when someone takes first place.
  decorateFirstCard(container) {
    const { textures } = this.kit;
    this.flare = new PIXI.Sprite(textures.cards[0]);
    this.flare.position.set(-PAD, -PAD);
    this.flare.blendMode = 'add';
    this.flare.alpha = 0;
    this.star = new PIXI.Sprite(textures.star);
    this.star.anchor.set(0.5);
    this.star.blendMode = 'add';
    this.star.position.set(LAYOUT.contentWidth - 8, 6);
    this.star.scale.set(0.32);
    container.addChildAt(this.flare, 1);
    container.addChild(this.star);
  }

  // The pills' backing and rank numbers stay where the slots are; the rows' entries move over them. A list too long for the
  // page scrolls as two copies in a loop, behind a mask that fades its top and bottom.
  buildRows() {
    const { textures } = this.kit, copies = this.scrolls ? 2 : 1;
    this.rowsLayer = new PIXI.Container();
    this.panel.addChild(this.rowsLayer);
    this.slotPlates = [];
    for (let copy = 0; copy < copies; copy++)
      for (let pill = 0; pill < this.rowCount; pill++) {
        const plate = new PIXI.Sprite(textures.row), numeral = new PIXI.Sprite(textures.rowNumeral(pill + TOP_COUNT + 1));
        numeral.anchor.set(0.5);
        this.rowsLayer.addChild(plate, numeral);
        this.slotPlates.push({ pill, copy, plate, numeral });
      }
    this.entryRows = new PIXI.Container();
    this.rowsLayer.addChild(this.entryRows);
    if (this.scrolls) this.rowsLayer.mask = this.addFade();
  }

  addFade() {
    const { viewHeight, rowsY } = this.layout, height = viewHeight + 8;
    this.fadeTexture = PIXI.Texture.from(canvasOf(LAYOUT.width, Math.ceil(height), g => {
      const fade = g.createLinearGradient(0, 0, 0, height);
      fade.addColorStop(0, 'rgba(255,255,255,0)');
      fade.addColorStop(LAYOUT.fadeTop / height, '#fff');
      fade.addColorStop(1 - LAYOUT.fadeBottom / height, '#fff');
      fade.addColorStop(1, 'rgba(255,255,255,0)');
      g.fillStyle = fade;
      g.fillRect(0, 0, LAYOUT.width, height);
    }));
    const sprite = new PIXI.Sprite(this.fadeTexture);
    sprite.position.set(0, rowsY - 4);
    this.panel.addChild(sprite);
    return sprite;
  }

  buildEntries() {
    this.entries = this.rows.flatMap(row => (this.scrolls ? [0, 1] : [0]).map(copy => new Entry(row, copy, this.kit)));
    for (const entry of this.entries) this.topLayer.addChild(entry.container);
  }

  /** Places everything for the clock `t`, the list scrolled by `scroll` and the running `event` (none between events).
   * Returns the climbing row's place, which the effects follow. */
  update(t, scroll, event) {
    const frame = { t, scroll, event, u: event && t - event.startedAt };
    this.placeSlotPlates(frame);
    this.cards.forEach((card, rank) => {
      card.visible = rank < this.rows.length;
      card.alpha = this.slotAlpha(rank, frame);
    });
    const riser = this.placeEntries(frame);
    this.placeFirstStar(frame);
    this.placeMascot(t, ambient(event?.kind, frame.u, t));
    return riser;
  }

  slotAlpha(slot, { event, u }) {
    return event?.kind === 'new' && slot === this.slots[event.id] ? unfolded(u) : 1;
  }

  rowY(pill, copy, scroll) {
    const y = this.layout.rowsY + pill * LAYOUT.rowPitch;
    return this.scrolls ? scrolledY(this.layout, this.lap, scroll, y + copy * this.lap) : y;
  }

  // Only the part of the scrolling list inside the page is placed; the rest waits out of sight.
  inWindow(y) {
    const { rowsY, viewHeight } = this.layout;
    return !this.scrolls || (y > rowsY - LAYOUT.rowPitch && y < rowsY + viewHeight);
  }

  placeSlotPlates(frame) {
    for (const { pill, copy, plate, numeral } of this.slotPlates) {
      const y = this.rowY(pill, copy, frame.scroll);
      plate.position.set(LAYOUT.contentX - PAD, y - PAD);
      numeral.position.set(LAYOUT.columns.rank, y + LAYOUT.rowHeight / 2 + 1);
      numeral.scale.set(0.9);
      plate.visible = numeral.visible = this.inWindow(y) && pill + TOP_COUNT < this.rows.length;
      plate.alpha = numeral.alpha = this.slotAlpha(pill + TOP_COUNT, frame);
    }
  }

  geometryAt(slot, copy, scroll) {
    const g = slotGeometry(this.layout, slot);
    return this.scrolls && slot >= TOP_COUNT ? shiftedGeometry(g, this.rowY(slot - TOP_COUNT, copy, scroll) - g.y) : g;
  }

  /** What an entry shows this frame, or nothing when it is out of sight: its geometry (between two slots while it moves), its
   * motion and how far it swings out. */
  placement(entry, frame) {
    const { event, u, scroll } = frame, id = entry.row.id, slot = this.slots[id], before = event?.slots[id];
    // The second copy of a row only exists among the pills.
    if (entry.copy > 0 && Math.min(slot, before ?? slot) < TOP_COUNT) return undefined;
    const to = this.geometryAt(slot, entry.copy, scroll), moving = before != null && before !== slot;
    const from = moving ? this.geometryAt(before, entry.copy, scroll) : to;
    const rising = id === event?.id, { g, arc } = moving ? this.slide(from, to, rising, frame) : { g: to, arc: 0 };
    if (g.tier === 3 && !this.inWindow(g.y)) return undefined;
    const role = rising ? 'rising' : event && before === this.slots[event.id] ? 'overtaken' : 'other';
    return { g, from, to, moving, arc, motion: entryMotion(event?.kind, role, u) };
  }

  // A row that changes place slides from its old slot to its new one, easing in its own way; the climber also swings out to the right.
  slide(from, to, rising, { event, u }) {
    const progress = k(...EVENTS[event.kind].move, u), ease = event.kind === 'new' ? out : rising ? easeBack : easeInOut;
    return { g: lerpGeometry(from, to, ease(progress)), arc: rising ? 14 * Math.sin(Math.PI * Math.min(1, progress)) : 0 };
  }

  placeEntries(frame) {
    let riser;
    for (const entry of this.entries) {
      const placement = this.placement(entry, frame);
      entry.container.visible = placement !== undefined;
      if (!placement) continue;
      // Pills in a scrolling list are masked; anything that changes place, and the top three, are not.
      const layer = this.scrolls && !placement.moving && this.slots[entry.row.id] >= TOP_COUNT ? this.entryRows : this.topLayer;
      if (entry.container.parent !== layer) layer.addChild(entry.container);
      entry.apply(placement.g, placement.motion, placement.arc);
      if (entry.row.id === frame.event?.id) riser ??= placement;
    }
    return riser;
  }

  placeFirstStar({ t, event, u }) {
    const { scale, rotation } = twinkle(t), level = event?.kind === 'top' ? flare(u) : 0;
    this.star.scale.set(scale + 0.7 * level);
    this.star.rotation = rotation;
    this.flare.alpha = 0.45 * level;
  }

  placeMascot(t, motion) {
    const { layout } = this, { textures } = this.kit, [homeX, homeY] = spriteHome(layout);
    // She breathes, 1 px up and down; the sprite floats and tilts a little, and floats less once the mascot is hidden.
    this.mascot.position.set(LAYOUT.width / 2, layout.top + 12 + motion.hop + Math.sin(t * 0.0021));
    if (this.idle) {
      this.idle.alpha = 1 - motion.cheer;
      this.cheer.alpha = motion.cheer;
    }
    this.sprite.texture = motion.blink ? textures.spriteBlink : textures.sprite;
    this.sprite.position.set(homeX, homeY + (layout.mascot ? 6 * Math.sin(t * 0.0016) + motion.hop : 2 * Math.sin(t * 0.0016)));
    this.sprite.rotation = 0.08 * Math.sin(t * 0.0011) + motion.spin;
    this.sprite.scale.set((layout.mascot ? 0.4 : 0.28) * (1 + 0.25 * motion.spritePulse));
  }

  destroy() {
    this.root.destroy({ children: true });
    this.fadeTexture?.destroy(true);
  }
}
