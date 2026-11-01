import {general} from './general.mjs';
import {Triple} from './general-40.mjs';
import {Swallow} from './general-50.mjs';
import {Slice} from './general-30.mjs';

export const registry = {
  'general-40': {character: general, images: general.ev2, audio: '40', duration: 7550, create: context => new Triple(context)},
  'general-50': {character: general, images: general.ev3, audio: '50', duration: 8480, create: context => new Swallow(context)},
  // EV1 bars finish at logical 6680 ms; add its 120 ms hit stop, not the board loop padding.
  'general-30': {character: general, images: general.ev1, audio: '30', duration: 6800, create: context => new Slice(context)}
};
