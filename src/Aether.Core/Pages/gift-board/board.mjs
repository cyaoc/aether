import { Changes } from './changes.mjs';
import { Effects } from './effects.mjs';
import { picturesOf } from './entry.mjs';
import { fitLayout, slotsOf } from './layout.mjs';
import { EVENTS, SCROLL_PER_MS } from './motion.mjs';
import { Scene } from './scene.mjs';

/** The gift board on a canvas: it shows each pushed state, plays the event the change calls for, and scrolls a long list.
 * `clock` is the page's one animation: its currentTime is the time of every frame, so a test can stop it anywhere and step it. */
export class Board {
  constructor(app, kit, clock, sound) {
    Object.assign(this, { app, kit, clock, sound, changes: new Changes(), scroll: 0, lastTime: clock.currentTime });
    this.effects = new Effects(app.stage, app.screen.width, app.screen.height);
    // The canvas says what it is playing, for anyone watching it from outside.
    this.app.canvas.dataset.event = '';
    app.ticker.add(() => this.frame());
  }

  /** A new connection to the server: the first state it pushes is drawn as it is. */
  reconnected() {
    this.changes.reset();
  }

  receive(state) {
    // The latest saved volume, whichever state is waiting its turn; it reaches a cheer already playing too.
    this.volume = state.options.volume;
    this.track?.volume(state.options.volume);
    if (this.changes.receive(state)) this.interrupt();
    this.sound.prepare(state.options.volume);
  }

  resize(width, height) {
    this.app.renderer.resize(width, height);
    this.effects.destroy();
    this.effects = new Effects(this.app.stage, width, height);
    if (this.state) this.show(this.state);
  }

  // The board for a state, drawn from scratch; a 期 that is not running is not on the page at all.
  show(state) {
    this.state = state;
    this.scene?.destroy();
    this.scene = undefined;
    if (!state.running) return;
    this.layout = fitLayout(this.app.screen.width, this.app.screen.height, state.options.mascot);
    this.scene = new Scene(this.kit, this.layout, state.rows);
    this.app.stage.addChildAt(this.scene.root, 1);
  }

  // A board that is drawn over as a whole (a 期 started, ended or continued, another room) lets go of the names and counts of the old one, and
  // of the pictures the new one does not show, so that a run of days does not keep a picture of every viewer it ever showed.
  forget(state) {
    this.scene?.destroy();
    this.scene = undefined;
    this.kit.textures.forget();
    this.kit.images.keepOnly(new Set(state.rows.flatMap(row => Object.values(picturesOf(row)))));
  }

  frame() {
    const t = this.clock.currentTime;
    this.advanceScroll(t);
    if (this.event && t - this.event.startedAt >= EVENTS[this.event.kind].duration) this.finish();
    // A redraw takes no time, so the next change may start in the same frame.
    while (!this.event) {
      const change = this.changes.take();
      if (!change) break;
      this.begin(change, t);
    }
    const u = this.event && t - this.event.startedAt;
    // Without a scene there is no event either, and the effects clear what they last drew.
    const riser = this.scene?.update(t, this.scroll, this.event);
    this.effects.draw(this.layout, this.event, u, riser);
    this.track?.frame(u);
  }

  // The list scrolls steadily, and stands still while an event plays.
  advanceScroll(t) {
    const elapsed = Math.max(0, t - this.lastTime);
    this.lastTime = t;
    if (this.scene?.scrolls && !this.event) this.scroll = (this.scroll + SCROLL_PER_MS * elapsed) % this.scene.lap;
  }

  begin({ kind, id, debut, state }, t) {
    const before = this.state;
    if (kind === 'redraw') this.forget(state);
    this.show(state);
    if (kind === 'redraw') return;
    this.event = { kind, id, debut, startedAt: t, slots: slotsOf(before.rows) };
    this.app.canvas.dataset.event = kind;
    // Only a new first place sounds.
    if (kind === 'top') this.track = this.sound.track(this.volume);
  }

  // The event playing is out of date: it stops where it is, its sound with it.
  interrupt() {
    this.track?.stop();
    this.finish();
  }

  finish() {
    this.event = undefined;
    this.track = undefined;
    this.app.canvas.dataset.event = '';
  }
}
