import { AudioTrack, prepareCues } from '../video/animations/audio.mjs';

const SOUND = '/gift-board/audio/578803__nomiqbomi__sparkle.ogg';
// The page's only sound, a soft sparkle for a new first place. Its loudest moment lands 950 ms after the event starts, the
// frame where the mascot cheers and the confetti flies (design package README, 声音).
const CUE = { file: SOUND, t: 0.95, peak: -12 };

async function load() {
  const data = await (await fetch(SOUND)).arrayBuffer();
  const ctx = new AudioContext({ latencyHint: 'interactive' });
  try {
    return { ctx, cues: prepareCues([CUE], { [SOUND]: await ctx.decodeAudioData(data) }) };
  } catch (error) {
    ctx.close();
    throw error;
  }
}

export class CheerSound {
  /** Loads the sound the first time a volume above 0 asks for it; a volume of 0 loads nothing. A load that fails is tried again
   * the next time a volume above 0 asks. */
  prepare(volume) {
    if (volume > 0) this.loading ??= load().then(loaded => { this.loaded = loaded; }, () => { this.loading = undefined; });
    return this.loading;
  }

  /** The track of one new first place. */
  track(volume) {
    return new CheerTrack(this, volume);
  }
}

/** Call `frame` with the milliseconds since the event began, and `volume` with a newly saved volume. The sound starts on the
 * first frame that finds it loaded and the volume above 0, so a load that finishes before the sparkle's moment still plays. */
class CheerTrack {
  constructor(sound, volume) {
    Object.assign(this, { sound, level: volume });
  }

  frame(milliseconds) {
    if (!this.audio && this.sound.loaded && this.level > 0) {
      const { ctx, cues } = this.sound.loaded;
      ctx.resume();
      // The duration only matters to looping cues.
      this.audio = new AudioTrack(ctx, cues, this.level, 0);
    }
    this.audio?.frame(milliseconds);
  }

  volume(volume) {
    this.level = volume;
    if (this.audio) this.audio.master.gain.value = volume / 100;
  }

  stop() {
    this.audio?.stop();
  }
}
