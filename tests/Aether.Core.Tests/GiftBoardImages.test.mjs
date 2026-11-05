// Run with: node --test tests/Aether.Core.Tests/GiftBoardImages.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';

// A browser's Image that loads when the test says, and the bit of PixiJS the page's pictures use.
const loads = [];
globalThis.Image = class { set src(url) { loads.push({ url, image: this }); } };
const EMPTY = { empty: true };
globalThis.PIXI = { Texture: { EMPTY, from: image => ({ image, destroyedWithSource: undefined, destroy(withSource) { this.destroyedWithSource = withSource; } }) } };
const { RemoteImages, RETRY_MS } = await import('../../src/Aether.Core/Pages/gift-board/images.mjs');

// The oldest load of the address still pending arrives, or fails.
const settle = async (url, outcome) => {
  const load = loads.find(load => load.url === url && !load.done);
  load.done = true;
  load.image[outcome]();
  await new Promise(resolve => setImmediate(resolve));
};
const arrive = url => settle(url, 'onload');
const fail = url => settle(url, 'onerror');

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

test('a picture that fails stays null while it is tried again a while later, three loads at most', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  loads.length = 0;
  const images = new RemoteImages();
  images.texture('a');
  for (let load = 1; load <= 3; load++) {
    await fail('a');
    assert.equal(images.texture('a'), null);
    t.mock.timers.tick(RETRY_MS);
    assert.equal(images.texture('a'), null);
  }
  assert.equal(loads.length, 3);
});

test('a picture that arrives on a later try is shown, and one let go of while it waits is not tried again', async t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  loads.length = 0;
  const images = new RemoteImages();
  images.texture('a');
  images.texture('b');
  await fail('a');
  await fail('b');
  images.keepOnly(new Set(['a']));
  t.mock.timers.tick(RETRY_MS);
  await arrive('a');
  assert.equal(images.texture('a').image, loads[2].image);
  assert.equal(loads.filter(load => load.url === 'b').length, 1);
});
