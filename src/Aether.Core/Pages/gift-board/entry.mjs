// One viewer's row on the board: the avatar (with the guard frame), the name, the gift icon and the count. The scene gives it a
// geometry (a card or a pill) and a motion every frame.
import { LAYOUT } from './layout.mjs';
import { GUARD_BADGE, GUARD_FRAME } from './images.mjs';
import { NAME_WIDTH, RIM } from './textures.mjs';
import { PAD } from './paint.mjs';

const centred = texture => {
  const sprite = new PIXI.Sprite(texture);
  sprite.anchor.set(0.5);
  return sprite;
};
const leftAligned = texture => {
  const sprite = new PIXI.Sprite(texture);
  sprite.anchor.set(0, 0.5);
  return sprite;
};

/** The B站 pictures a row shows: its avatar, the guard frame round it, and its gift icon or guard badge. */
export const picturesOf = row => ({
  face: row.faceUrl,
  frame: GUARD_FRAME[row.guardLevel],
  icon: row.guardLevel ? GUARD_BADGE[row.guardLevel] : row.iconUrl
});

export class Entry {
  /** `copy` is 0, or 1 for the second copy of a row in a scrolling list. */
  constructor(row, copy, kit) {
    const { textures } = kit;
    Object.assign(this, { row, copy, kit, pictures: picturesOf(row) });
    this.container = new PIXI.Container();
    this.plate = new PIXI.Sprite(textures.row);
    this.plate.alpha = 0;
    this.ring = new PIXI.Graphics();
    this.avatar = centred(PIXI.Texture.EMPTY);
    this.frame = row.guardLevel ? centred(PIXI.Texture.EMPTY) : null;
    if (this.frame) this.frame.anchor.set(0.4975, 0.505);
    this.cardName = leftAligned(textures.name(row.nickname, NAME_WIDTH.card));
    this.rowName = leftAligned(textures.name(row.nickname, NAME_WIDTH.row));
    this.icon = centred(PIXI.Texture.EMPTY);
    this.count = new PIXI.Sprite(textures.count(row.num));
    this.count.anchor.set(1, 0.5);
    this.clip = new PIXI.Graphics();
    this.container.addChild(...[this.plate, this.ring, this.avatar, this.frame, this.cardName, this.rowName, this.icon, this.count, this.clip].filter(Boolean));
  }

  /** Puts the row at geometry `g`, looking as `motion` says, `arc` design pixels to the right (a climber swings out of the list). */
  apply(g, motion, arc) {
    this.container.alpha = motion.alpha;
    this.container.zIndex = motion.z;
    this.placePlate(g, motion.plateAlpha, arc);
    this.placePicture(g, motion, arc);
    this.placeName(g, motion, arc);
    this.placeIcon(g, motion, arc);
    this.placeCount(g, motion, arc);
    this.placeReveal(g, motion.open, arc);
  }

  // The backing of a climber takes the shape of the slot it is crossing: a card among the top three, a pill below.
  placePlate(g, alpha, arc) {
    const card = g.tier < 3;
    this.plate.texture = card ? this.kit.textures.cards[g.tier] : this.kit.textures.row;
    this.plate.alpha = alpha;
    this.plate.position.set(LAYOUT.contentX - PAD + arc, (card ? g.y : g.y + (g.h - LAYOUT.rowHeight) / 2) - PAD);
  }

  placePicture(g, { avatarScale, avatarAlpha, lift }, arc) {
    const { row, kit } = this;
    this.avatar.texture = row.faceUrl ? kit.images.texture(this.pictures.face) : kit.textures.initial(row.nickname);
    this.avatar.position.set(g.ax + arc, g.ay);
    this.avatar.width = this.avatar.height = 2 * g.ar * avatarScale * lift;
    this.avatar.alpha = avatarAlpha;
    // A guard's picture wears B站's frame instead of the ring.
    this.ring.clear().circle(g.ax + arc, g.ay, g.ar + 1.5).stroke({
      width: g.tier < 3 ? 2.4 : 1.6, color: g.tier < 3 ? RIM[g.tier].ring : 0xffffff, alpha: row.guardLevel ? 0 : 0.9
    });
    if (!this.frame) return;
    this.frame.texture = kit.images.texture(this.pictures.frame);
    this.frame.position.set(g.ax + arc, g.ay);
    // The frame is 0.7 of the avatar's width; the picture shows through its middle.
    this.frame.width = this.frame.height = (2 * g.ar / 0.7) * avatarScale * lift;
    this.frame.alpha = avatarAlpha;
  }

  placeName(g, { nameShift, nameAlpha, lift }, arc) {
    for (const [sprite, shown] of [[this.cardName, g.tier < 3], [this.rowName, g.tier === 3]]) {
      sprite.position.set(g.nx - 8 * g.ns / 28 + arc + nameShift, g.ny);
      sprite.scale.set(g.ns / 28 * lift);
      sprite.alpha = shown ? nameAlpha : 0;
    }
  }

  placeIcon(g, { iconScale, iconAlpha, lift }, arc) {
    this.icon.texture = this.kit.images.texture(this.pictures.icon);
    this.icon.position.set(g.ix + arc, g.iy);
    this.icon.width = this.icon.height = g.is * iconScale * lift;
    this.icon.alpha = iconAlpha;
  }

  placeCount(g, { countScale, countAlpha, lift }, arc) {
    this.count.position.set(g.cx + arc, g.cy);
    this.count.scale.set(g.cs * countScale * lift);
    this.count.alpha = countAlpha;
  }

  // A new row opens from its left end to its full width.
  placeReveal(g, open, arc) {
    if (open >= 1) {
      this.container.mask = null;
      this.clip.clear();
      return;
    }
    this.clip.clear().rect(LAYOUT.contentX + arc, g.y - 10, LAYOUT.contentWidth * open, g.h + 20).fill(0xffffff);
    this.container.mask = this.clip;
  }
}
