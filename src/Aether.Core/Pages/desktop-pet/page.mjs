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

// What Core tells the 桌宠 (DesktopPet.cs), each to the engine's handler of the same name.
const events = new EventSource('/desktop-pet/events');
events.addEventListener('state', message => pet.receive(JSON.parse(message.data)));
for (const name of ['gift', 'blindBox', 'guard', 'entry', 'danmaku', 'testShow', 'stopTestShow'])
  events.addEventListener(name, message => pet[name](JSON.parse(message.data)));
// A 试演 that is over is reported, as the video page reports what it played, so the GUI offers another (DesktopPetPage.cs).
pet.addEventListener('trialended', event => fetch(`/desktop-pet/report?trial=${event.detail}`, { method: 'POST' }));
// The next frame is asked for first, so one that throws is lost alone.
const loop = () => { requestAnimationFrame(loop); pet.frame(); };
requestAnimationFrame(loop);
