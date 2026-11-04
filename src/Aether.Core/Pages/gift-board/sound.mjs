import { AudioTrack, prepareCues } from '../video/animations/audio.mjs';

const SOUND = '/gift-board/audio/578803__nomiqbomi__sparkle.ogg';
// The page's only sound, a soft sparkle for a new first place. Its loudest moment lands 950 ms after the event starts, the
// frame where the mascot cheers and the confetti flies (design package README, 声音).
const CUE = { file: SOUND, t: 0.95, peak: -12 };

async function load() {
  const ctx = new AudioContext({ latencyHint: 'interactive' });
  const buffer = await ctx.decodeAudioData(await (await fetch(SOUND)).arrayBuffer());
  return { ctx, cues: prepareCues([CUE], { [SOUND]: buffer }) };
}

export class CheerSound {
  /** Loads the sound the first time a volume above 0 asks for it; a volume of 0 loads nothing. */
  prepare(volume) {
    if (volume > 0) this.loading ??= load().then(loaded => { this.loaded = loaded; });
    return this.loading;
  }

  /** The track of one new first place; call its `frame` with the milliseconds since the event began. There is none while the
   * volume is 0 or the sound is still loading. */
  track(volume) {
    if (volume <= 0 || !this.loaded) return undefined;
    const { ctx, cues } = this.loaded;
    ctx.resume();
    // The duration only matters to looping cues.
    return new AudioTrack(ctx, cues, volume, 0);
  }
}
