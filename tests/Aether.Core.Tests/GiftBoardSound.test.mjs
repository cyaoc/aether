// Run with: node --test tests/Aether.Core.Tests/GiftBoardSound.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { CheerSound } from '../../src/Aether.Core/Pages/gift-board/sound.mjs';

const PEAK_SAMPLE = 300;   // the loudest sample of the fake sound, 0.3 s in
const LATENCY = 0.03;

// A browser audio stack that records what the page asks of it.
function fakeAudio() {
  const log = { contexts: 0, fetched: [], gains: [], starts: [] };
  const buffer = { duration: 2.77, sampleRate: 1000, numberOfChannels: 1, getChannelData: () => Array.from({ length: 2770 }, (_, i) => i === PEAK_SAMPLE ? 1 : 0) };
  globalThis.fetch = async url => { log.fetched.push(url); return { arrayBuffer: async () => new ArrayBuffer(0) }; };
  globalThis.AudioContext = class {
    constructor() { log.contexts++; Object.assign(this, { state: 'running', currentTime: 10, baseLatency: 0.01, outputLatency: 0.02, destination: {} }); }
    decodeAudioData = async () => buffer;
    resume() { return Promise.resolve(); }
    createGain() { return { gain: { setValueAtTime: (value, at) => log.gains.push({ value, at }), linearRampToValueAtTime() {} }, connect() {}, disconnect() {} }; }
    createBufferSource() { return { playbackRate: {}, connect: () => ({ connect() {} }), start: (...args) => log.starts.push({ args }) }; }
  };
  return log;
}

test('a volume of 0 loads nothing and plays nothing', async () => {
  const log = fakeAudio(), sound = new CheerSound();
  await sound.prepare(0);
  assert.equal(sound.track(0), undefined);
  assert.deepEqual([log.contexts, log.fetched], [0, []]);
});

test('the sound is loaded once, when a volume above 0 first asks for it', async () => {
  const log = fakeAudio(), sound = new CheerSound();
  await Promise.all([sound.prepare(60), sound.prepare(30)]);
  assert.deepEqual([log.contexts, log.fetched], [1, ['/gift-board/audio/578803__nomiqbomi__sparkle.ogg']]);
});

test('no track while the sound is still loading', () => {
  fakeAudio();
  const sound = new CheerSound();
  sound.prepare(60);
  assert.equal(sound.track(60), undefined);
});

test('the loudest moment lands 950 ms after the event starts, scheduled once at -12 dB', async () => {
  const log = fakeAudio(), sound = new CheerSound();
  await sound.prepare(60);
  const track = sound.track(60);
  let called;
  for (let t = 0; t <= 4000; t += 16) {
    const before = log.starts.length;
    track.frame(t);
    if (log.starts.length > before) called = t;
  }
  assert.equal(log.starts.length, 1);
  const { args: [when] } = log.starts[0], peakSeconds = PEAK_SAMPLE / 1000;
  assert.ok(Math.abs((when + peakSeconds + LATENCY) - (10 + (950 - called) / 1000)) < 1e-9, 'the peak reaches the speakers 950 ms after the event started');
  assert.equal(log.gains.length, 1);
  assert.ok(Math.abs(log.gains[0].value - 10 ** (-12 / 20)) < 1e-9);
});

test('the chosen volume is the master gain', async () => {
  const log = fakeAudio(), sound = new CheerSound();
  await sound.prepare(40);
  const track = sound.track(40);
  assert.equal(track.master.gain.value, 0.4);
  assert.deepEqual(log.starts, []);
});
