// B站's pictures: avatars, gift icons, guard frames and badges, loaded as they are asked for.
// B站's official guard avatar frames and badges by guard_level (1 总督, 2 提督, 3 舰长), from the design package's tools/blobs.json.
const BADGE_ROOT = 'https://i0.hdslb.com/bfs/activity-plat/static/20211222/627754775478985e330c25a90ec7baf0/';
export const GUARD_FRAME = {
  1: 'https://i0.hdslb.com/bfs/live/39164ebfdd39db3d284b1221765e7e57f5a49958.png',
  2: 'https://i0.hdslb.com/bfs/live/09937c3beb0608e267a50ac3c7125c3f2d709098.png',
  3: 'https://i0.hdslb.com/bfs/live/80f732943cc3367029df65e267960d56736a82ee.png'
};
export const GUARD_BADGE = { 1: BADGE_ROOT + 'icon-guard1.png', 2: BADGE_ROOT + 'icon-guard2.png', 3: BADGE_ROOT + 'icon-guard3.png' };

export class RemoteImages {
  constructor() {
    this.textures = new Map();
  }

  /** The texture of a B站 picture: empty until it has loaded, and for good when it fails or there is no address. */
  texture(url) {
    if (!url) return PIXI.Texture.EMPTY;
    if (!this.textures.has(url)) {
      this.textures.set(url, PIXI.Texture.EMPTY);
      this.load(url);
    }
    return this.textures.get(url);
  }

  load(url) {
    const image = new Image();
    // The image host answers a Referer from another site with 403, and none with 200 and `Access-Control-Allow-Origin: *`.
    image.referrerPolicy = 'no-referrer';
    image.crossOrigin = 'anonymous';
    image.onload = () => this.textures.set(url, PIXI.Texture.from(image));
    image.src = url;
  }
}
