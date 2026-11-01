import test from 'node:test';
import assert from 'node:assert/strict';
import {registry} from '../../src/Aether.Core/Pages/video/animations/registry.mjs';
test('60 finishes after the final letterbox and all eight hit stops',()=>{
 assert.ok(registry['general-60']);
 assert.equal(registry['general-60'].duration,9064);
});

import {reach,chordOf,pathOf} from '../../src/Aether.Core/Pages/video/animations/toolkit.mjs';
test('world cuts reach the moon camera boundary without changing the other moves',()=>{
 assert.deepEqual(reach([960,540],[860,540]),[5960,540]);
 assert.deepEqual(reach([960,540],[860,540],6000),[6960,540]);
 assert.deepEqual(chordOf(pathOf([[860,540],[1060,540]]),0,6000),[[-5040,540],[6960,540]]);
});
