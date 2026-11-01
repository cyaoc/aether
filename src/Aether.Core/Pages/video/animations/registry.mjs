import {general} from './general.mjs';
import {Slice} from './general-30.mjs';

export const registry = {
  // EV1 bars finish at logical 6680 ms; add its 120 ms hit stop, not the board loop padding.
  'general-30': {character: general, images: general.ev1, audio: '30', duration: 6800, create: context => new Slice(context)}
};
