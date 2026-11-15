// B站's pictures: avatars, gift icons, guard frames and badges, loaded as they are asked for.
import { loadImage } from '../video/animations/toolkit.mjs';

// B站's official guard avatar frames and badges by guard_level (1 总督, 2 提督, 3 舰长), from the design package's tools/blobs.json.
const BADGE_ROOT = 'https://i0.hdslb.com/bfs/activity-plat/static/20211222/627754775478985e330c25a90ec7baf0/';
export const GUARD_FRAME = {
  1: 'https://i0.hdslb.com/bfs/live/39164ebfdd39db3d284b1221765e7e57f5a49958.png',
  2: 'https://i0.hdslb.com/bfs/live/09937c3beb0608e267a50ac3c7125c3f2d709098.png',
  3: 'https://i0.hdslb.com/bfs/live/80f732943cc3367029df65e267960d56736a82ee.png'
};
export const GUARD_BADGE = { 1: BADGE_ROOT + 'icon-guard1.png', 2: BADGE_ROOT + 'icon-guard2.png', 3: BADGE_ROOT + 'icon-guard3.png' };

// The image host answers a Referer from another site with 403, and none with 200 and `Access-Control-Allow-Origin: *`.
export const REMOTE = { referrerPolicy: 'no-referrer', crossOrigin: 'anonymous' };
// A picture that failed to load is tried again this long after, up to LOADS loads in all.
export const RETRY_MS = 10000;
const LOADS = 3;

export class RemoteImages {
  constructor() {
    this.textures = new Map();
  }

  /** The texture of a B站 picture: empty until it has loaded and for good when there is no address, null while it has failed
   * (a sprite then shows nothing, and an avatar its stand-in). */
  texture(url) {
    if (!url) return PIXI.Texture.EMPTY;
    if (!this.textures.has(url)) {
      this.textures.set(url, PIXI.Texture.EMPTY);
      this.load(url, 1);
    }
    return this.textures.get(url);
  }

  // A picture let go of while it loaded or waited to be tried again is not kept, nor is a second load of one that has arrived.
  // One tried again stays null, its stand-in showing, until it arrives.
  load(url, attempt) {
    const pending = () => [PIXI.Texture.EMPTY, null].includes(this.textures.get(url));
    loadImage(url, REMOTE).then(image => {
      if (pending()) this.textures.set(url, PIXI.Texture.from(image));
    }, () => {
      if (!pending()) return;
      this.textures.set(url, null);
      if (attempt < LOADS) setTimeout(() => this.textures.get(url) === null && this.load(url, attempt + 1), RETRY_MS);
    });
  }

  /** Destroys every picture but those at the addresses in `kept`, the ones the board still shows. */
  keepOnly(kept) {
    for (const [url, texture] of this.textures) {
      if (kept.has(url)) continue;
      if (texture && texture !== PIXI.Texture.EMPTY) texture.destroy(true);
      this.textures.delete(url);
    }
  }
}
