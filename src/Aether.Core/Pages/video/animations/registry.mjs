import {general} from './general.mjs';
import {Slice} from './slice.mjs';
import {Triple} from './triple.mjs';
import {Swallow} from './swallow.mjs';
import {Moon} from './moon.mjs';

// duration is the frame the picture ends on: the logical end of the last bars plus every hit stop, not the board's loop.
export const registry = {
  'general-30': {character: general, audio: '30', duration: 6800, create: context => new Slice(context)},
  'general-40': {character: general, audio: '40', duration: 7550, create: context => new Triple(context)},
  'general-50': {character: general, audio: '50', duration: 8480, create: context => new Swallow(context)},
  'general-60': {character: general, audio: '60', duration: 9064, create: context => new Moon(context)}
};
