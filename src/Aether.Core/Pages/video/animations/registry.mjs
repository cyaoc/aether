import {Moon} from './general-60.mjs';
import {general} from './general.mjs';
import {Slice} from './general-30.mjs';

export const registry = {
    // Final bars at logical 8150, plus eight hit stops totalling 914 ms.
    'general-60': {character:general, images:general.ev4, audio:'60', duration:9064, create:context=>new Moon(context)},
  // EV1 bars finish at logical 6680 ms; add its 120 ms hit stop, not the board loop padding.
  'general-30': {character: general, images: general.ev1, audio: '30', duration: 6800, create: context => new Slice(context)}
};
