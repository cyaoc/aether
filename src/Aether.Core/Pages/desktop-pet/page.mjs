import { Pet } from './pet.mjs';

const read = async url => (await fetch(url)).json();
const [character, { shows }] = await Promise.all([read('/characters/current/character.json'), read('/desktop-pet/shows.json'),
  // her lines and the effects' marks are in the 礼物榜's Noto Sans SC, loaded before she says anything
  ...['500', '800'].map(weight => document.fonts.load(`${weight} 18px "Noto Sans SC"`))]);
// The one clock of every frame is an animation, so a test can stop it at any moment and step it.
const master = document.documentElement.animate([{ outlineColor: 'transparent' }, { outlineColor: 'transparent' }], { duration: 1000, iterations: Infinity });
// Exported for the browser tests and the design package's --page tools: what she plays from Core's events is this engine's
// alone, and her pixels cannot tell which show it is.
export const pet = new Pet(document.querySelector('canvas'), master, shows, character);
pet.reset(Math.random);
await pet.boot();
pet.resize(innerWidth, innerHeight);
addEventListener('resize', () => pet.resize(innerWidth, innerHeight));

// What Core tells the 桌宠 (DesktopPet.cs), each to the engine's handler of the same name. The page's id tells Core which page
// its reports come from, so one reloaded no longer has the 捣蛋鬼 there.
const id = Math.random().toString(36).slice(2);
const events = new EventSource(`/desktop-pet/events?page=${id}`);
// Its reports go one after another, each once Core has taken the one before, so Core never takes a 来访's start after its end.
// One that does not get there (Core restarting, the connection lost, Core failing on it) is sent again a second later until it
// does; one Core turned down (4xx) never would.
let reported = Promise.resolve();
const deliver = async url => {
  for (;;) {
    try { if ((await fetch(url, { method: 'POST' })).status < 500) return; } catch { }
    await new Promise(again => setTimeout(again, 1000));
  }
};
const report = query => { reported = reported.then(() => deliver(`/desktop-pet/report?${query}`)); };
// Its 来访 reports say the run the state told last, so Core takes none from before the 桌宠 was turned off, however late. A
// state's run is hers once she has taken it: what she reports as she does (a 叫它来 it calls off) is of the run before.
let run;
const visit = what => report(`visit=${what}&page=${id}&run=${run}`);
// Each time its events (re)connect, Core has forgotten the 捣蛋鬼 here, as a page gone has none (and took no report meanwhile):
// the first state of the connection has the page tell it again that it set off, or that the one 叫它来 called is on its way.
// A state of another run than hers means the 桌宠 was turned off while her events were down: she is told first what Core tells
// a page then (switched off, so no room either, DesktopPetState), so nothing of the run before, nor her quiet-room clocks,
// comes back in the new one.
let connected = false;
events.addEventListener('open', () => { connected = true; });
events.addEventListener('state', message => {
  const state = JSON.parse(message.data);
  if (run !== undefined && state.run !== run) pet.receive({ enabled: false, roomConnected: false, run: state.run });
  pet.receive(state); run = state.run;
  if (connected && pet.gm.visit) visit('start'); else if (connected && pet.gm.due === 'called') visit('called');
  connected = false;
});
for (const name of ['gift', 'blindBox', 'blindBoxQuery', 'guard', 'entry', 'danmaku', 'pkCondition', 'pkDouble', 'pkCountdown', 'testShow', 'stopTestShow', 'summon'])
  events.addEventListener(name, message => pet[name](JSON.parse(message.data)));
// A 试演 that is over is reported, as the video page reports what it played, so the GUI offers another (DesktopPetPage.cs).
pet.addEventListener('trialended', event => report(`trial=${event.detail}`));
// So is the 捣蛋鬼 setting off and leaving, for the GUI's 叫它来 (DesktopPet.cs).
pet.addEventListener('visit', event => visit(event.detail));
// The next frame is asked for first, so one that throws is lost alone.
const loop = () => { requestAnimationFrame(loop); pet.frame(); };
requestAnimationFrame(loop);
