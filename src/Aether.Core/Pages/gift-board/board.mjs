import { Changes } from './changes.mjs';
import { Effects } from './effects.mjs';
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
    this.changes.receive(state);
    this.sound.prepare(state.volume);
  }

  resize(width, height) {
    this.app.renderer.resize(width, height);
    this.effects.destroy();
    this.effects = new Effects(this.app.stage, width, height);
    if (this.state) this.show(this.state);
  }

  // The board for a state, drawn from scratch.
  show(state) {
    this.layout = fitLayout(this.app.screen.width, this.app.screen.height, state.mascot);
    this.scene?.destroy();
    this.scene = new Scene(this.kit, this.layout, state.rows);
    this.app.stage.addChildAt(this.scene.root, 1);
    this.state = state;
  }

  // A board that is drawn over as a whole (another show, a clear, a restore) lets go of the names and counts of the old one, so
  // that a run of days does not keep a picture of every viewer it ever showed.
  forget() {
    this.scene?.destroy();
    this.scene = undefined;
    this.kit.textures.forget();
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
    if (!this.scene) return;
    const u = this.event && t - this.event.startedAt;
    const riser = this.scene.update(t, this.scroll, this.event);
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
    if (kind === 'redraw') this.forget();
    this.show(state);
    if (kind === 'redraw') return;
    this.event = { kind, id, debut, startedAt: t, slots: slotsOf(before.rows) };
    this.app.canvas.dataset.event = kind;
    // Only a new first place sounds; with the volume at 0 there is no track.
    if (kind === 'top') this.track = this.sound.track(state.volume);
  }

  finish() {
    this.event = undefined;
    this.track = undefined;
    this.app.canvas.dataset.event = '';
  }
}
