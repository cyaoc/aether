// Peak alignment and scheduling follow the design package's tools/render.html sound branch.
export function prepareCues(cues, buffers) {
  return cues.map(c => {
    const buffer = buffers[c.file], rate = c.rate ?? 1, from = c.from ?? 0;
    const end = Math.min(buffer.duration, from + (c.length ?? buffer.duration));
    let top = 0, at = from;
    for (let ch = 0; ch < buffer.numberOfChannels; ch++) {
      const samples = buffer.getChannelData(ch);
      for (let i = Math.floor(from * buffer.sampleRate); i < end * buffer.sampleRate; i++)
        if (Math.abs(samples[i]) > top) { top = Math.abs(samples[i]); at = i / buffer.sampleRate; }
    }
    return {...c, buffer, rate, from, end, level: 10 ** (c.peak / 20) / top,
      startAt: c.at ?? c.t - (at - from) / rate};
  });
}

export class AudioTrack {
  constructor(ctx, cues, volume, duration) {
    Object.assign(this, {ctx, cues, duration, playing: [], fired: new Set()});
    this.master = ctx.createGain(); this.master.gain.value = volume / 100;
    this.master.connect(ctx.destination);
  }
  frame(milliseconds) {
    const t = milliseconds / 1000, ctx = this.ctx;
    if (ctx.state && ctx.state !== 'running') return;
    const latency = (ctx.baseLatency || 0) + (ctx.outputLatency || 0);
    this.cues.forEach((c, i) => {
      if (this.fired.has(i) || c.startAt - t - latency >= .12) return;
      this.fired.add(i);
      if (!c.loop && t > .1 && c.startAt < t - .1) return;
      const wait = Math.max(0, c.startAt - t - latency), skip = Math.max(0, t + latency - c.startAt) * c.rate;
      const left = c.loop ? this.duration - t : (c.end - c.from - skip) / c.rate;
      if (left <= 0) return;
      const source = ctx.createBufferSource(), gain = ctx.createGain(), when = ctx.currentTime + wait;
      source.buffer = c.buffer; source.playbackRate.value = c.rate; source.loop = !!c.loop;
      source.connect(gain).connect(this.master);
      gain.gain.setValueAtTime(c.fadeIn ? 0 : c.level, when);
      if (c.fadeIn) gain.gain.linearRampToValueAtTime(c.level, when + c.fadeIn);
      if (c.fadeOut) { gain.gain.setValueAtTime(c.level, when + Math.max(0, left - c.fadeOut)); gain.gain.linearRampToValueAtTime(0, when + left); }
      source.start(when, c.loop ? (c.from + skip) % c.buffer.duration : c.from + skip, left * c.rate);
      this.playing.push(source);
    });
  }
  stop() {
    for (const source of this.playing) source.stop();
    this.playing = [];
    this.master.disconnect?.();
  }
}
