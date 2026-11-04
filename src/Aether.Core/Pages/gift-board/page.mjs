import { Board } from './board.mjs';
import { RemoteImages, loadImage } from './images.mjs';
import { CheerSound } from './sound.mjs';
import { createTextures } from './textures.mjs';

const [banner, idle, cheer] = await Promise.all(['banner', 'mascot-peek-idle', 'mascot-peek-cheer'].map(name => loadImage(`/gift-board/textures/${name}.webp`)));
await Promise.all([
  document.fonts.load('500 28px "Noto Sans SC"', '月下Lily龍'),
  document.fonts.load('800 44px "Noto Sans SC"', '礼物榜×0123456789'),
  document.fonts.load('italic 800 60px "Playfair Num"', '0123456789')
]);
const app = new PIXI.Application();
await app.init({ canvas: document.querySelector('canvas'), width: innerWidth, height: innerHeight, backgroundAlpha: 0, antialias: true, preference: 'webgl', resolution: 1 });
const kit = { textures: createTextures({ banner, idle, cheer }), images: new RemoteImages() };
// The one clock of every frame is an animation, so a test can stop it at any moment and step it.
const clock = document.documentElement.animate([{ outlineColor: 'transparent' }, { outlineColor: 'transparent' }], { duration: 1000, iterations: Infinity });
const board = new Board(app, kit, clock, new CheerSound());

const events = new EventSource('/gift-board/events');
events.addEventListener('open', () => board.reconnected());
events.addEventListener('state', message => board.receive(JSON.parse(message.data)));
window.addEventListener('resize', () => board.resize(innerWidth, innerHeight));
