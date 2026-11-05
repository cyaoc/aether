// Run with: node --test tests/Aether.Core.Tests/GiftBoardSound.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { CheerSound } from '../../src/Aether.Core/Pages/gift-board/sound.mjs';

const PEAK_SAMPLE = 300;   // the loudest sample of the fake sound, 0.3 s in
const LATENCY = 0.03;

// A browser audio stack that records what the page asks of it.
function fakeAudio() {
  const log = { contexts: 0, closed: 0, failures: 0, fetched: [], gains: [], starts: [] };
  const buffer = { duration: 2.77, sampleRate: 1000, numberOfChannels: 1, getChannelData: () => Array.from({ length: 2770 }, (_, i) => i === PEAK_SAMPLE ? 1 : 0) };
  globalThis.fetch = async url => { log.fetched.push(url); return { arrayBuffer: async () => new ArrayBuffer(0) }; };
  globalThis.AudioContext = class {
    constructor() { log.contexts++; Object.assign(this, { state: 'running', currentTime: 10, baseLatency: 0.01, outputLatency: 0.02, destination: {} }); }
    decodeAudioData = async () => {
      if (log.failures > 0) { log.failures--; throw new Error('解码失败'); }
      return buffer;
    };
    resume() { return Promise.resolve(); }
    close() { log.closed++; }
    createGain() { return { gain: { setValueAtTime: (value, at) => log.gains.push({ value, at }), linearRampToValueAtTime() {} }, connect() {}, disconnect() {} }; }
    createBufferSource() { return { playbackRate: {}, connect: () => ({ connect() {} }), start: (...args) => log.starts.push({ args }) }; }
  };
  return log;
}

// Plays a track's frames from the start of its event to `until` milliseconds, 16 ms apart.
const playTo = (track, until, from = 0) => { for (let t = from; t <= until; t += 16) track.frame(t); };

test('a volume of 0 loads nothing and plays nothing', async () => {
  const log = fakeAudio(), sound = new CheerSound();
  await sound.prepare(0);
  playTo(sound.track(0), 4000);
  assert.deepEqual([log.contexts, log.fetched, log.starts], [0, [], []]);
});

test('the sound is loaded once, when a volume above 0 first asks for it', async () => {
  const log = fakeAudio(), sound = new CheerSound();
  await Promise.all([sound.prepare(60), sound.prepare(30)]);
  assert.deepEqual([log.contexts, log.fetched], [1, ['/gift-board/audio/578803__nomiqbomi__sparkle.ogg']]);
});

test('a sound that finishes loading before its moment still plays, and one that finishes after it does not', async () => {
  const log = fakeAudio(), early = new CheerSound(), late = new CheerSound();
  const loading = [early, late].map(sound => sound.prepare(60));
  const [beforeIt, afterIt] = [early.track(60), late.track(60)];
  playTo(beforeIt, 100);
  playTo(afterIt, 1500);
  await Promise.all(loading);
  playTo(beforeIt, 4000, 116);
  playTo(afterIt, 4000, 1516);
  assert.equal(log.starts.length, 1);
});

test('a load that fails lets the context go and is tried again the next time a volume asks', async () => {
  const log = fakeAudio(), sound = new CheerSound();
  log.failures = 1;
  await sound.prepare(60);
  assert.deepEqual([log.contexts, log.closed, sound.loaded], [1, 1, undefined]);
  await sound.prepare(60);
  const track = sound.track(60);
  playTo(track, 4000);
  assert.deepEqual([log.contexts, log.starts.length], [2, 1]);
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

test('the chosen volume is the master gain, and a volume saved later changes it before and while the sound plays', async () => {
  const log = fakeAudio(), sound = new CheerSound();
  await sound.prepare(40);
  const track = sound.track(40);
  track.frame(0);
  assert.equal(track.audio.master.gain.value, 0.4);
  assert.deepEqual(log.starts, []);
  playTo(track, 1000, 16);
  track.volume(25);
  assert.equal(track.audio.master.gain.value, 0.25);
  const muted = sound.track(60);
  muted.volume(0);
  playTo(muted, 4000);
  assert.equal(log.starts.length, 1);
});
