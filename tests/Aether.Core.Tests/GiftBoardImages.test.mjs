// Run with: node --test tests/Aether.Core.Tests/GiftBoardImages.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';

// A browser's Image that loads when the test says, and the bit of PixiJS the page's pictures use.
const loads = [];
globalThis.Image = class { set src(url) { loads.push({ url, image: this }); } };
const EMPTY = { empty: true };
globalThis.PIXI = { Texture: { EMPTY, from: image => ({ image, destroyedWithSource: undefined, destroy(withSource) { this.destroyedWithSource = withSource; } }) } };
const { RemoteImages } = await import('../../src/Aether.Core/Pages/gift-board/images.mjs');

// The oldest load of the address still pending arrives.
const arrive = async url => {
  const load = loads.find(load => load.url === url && !load.done);
  load.done = true;
  load.image.onload();
  await new Promise(resolve => setTimeout(resolve));
};

test('a picture is empty until it arrives and is loaded once', async () => {
  loads.length = 0;
  const images = new RemoteImages();
  assert.equal(images.texture('a'), EMPTY);
  assert.equal(images.texture('a'), EMPTY);
  await arrive('a');
  assert.equal(images.texture('a').image, loads[0].image);
  assert.equal(loads.length, 1);
});

test('a picture the board no longer shows is destroyed, its source too, and one it still shows is kept', async () => {
  loads.length = 0;
  const images = new RemoteImages();
  images.texture('a');
  images.texture('b');
  await arrive('a');
  await arrive('b');
  const [a, b] = [images.texture('a'), images.texture('b')];
  images.keepOnly(new Set(['b']));
  assert.deepEqual([a.destroyedWithSource, b.destroyedWithSource], [true, undefined]);
  assert.equal(images.texture('b'), b);
  assert.equal(images.texture('a'), EMPTY);
  assert.equal(loads.length, 3);
});

test('a picture let go of while it loads is not kept when it arrives', async () => {
  loads.length = 0;
  const images = new RemoteImages();
  images.texture('a');
  images.keepOnly(new Set());
  await arrive('a');
  assert.equal(images.textures.size, 0);
});
