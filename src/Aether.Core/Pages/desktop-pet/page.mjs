import { Pet } from './pet.mjs';

const read = async url => (await fetch(url)).json();
const [character, { shows }] = await Promise.all([read('/characters/current/character.json'), read('/desktop-pet/shows.json')]);
// The one clock of every frame is an animation, so a test can stop it at any moment and step it.
const master = document.documentElement.animate([{ outlineColor: 'transparent' }, { outlineColor: 'transparent' }], { duration: 1000, iterations: Infinity });
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
const loop = () => { pet.frame(); requestAnimationFrame(loop); };
requestAnimationFrame(loop);
