import test from 'node:test';
import assert from 'node:assert/strict';
import {registry} from '../../src/Aether.Core/Pages/video/animations/registry.mjs';

test('triple and swallow finish after the last letterbox fades, including all hit stops',()=>{
  assert.equal(registry['general-40']?.duration,7550);
  assert.equal(registry['general-50']?.duration,8480);
});

import {slash} from '../../src/Aether.Core/Pages/video/animations/toolkit.mjs';
import {revealName} from '../../src/Aether.Core/Pages/video/animations/scenes.mjs';
import {general} from '../../src/Aether.Core/Pages/video/animations/general.mjs';

test('slash forwards an explicit canvas to every band and strand',()=>{
  const calls=[],canvas={};
  slash({band:(...args)=>calls.push(args),arcLine:(...args)=>calls.push(args)},general.palette,{},1,.3,1,40,.5,10,1,canvas);
  assert.ok(calls.length>3);
  assert.ok(calls.every(args=>args.at(-1)===canvas));
});

test('name reveal retains existing leftward exit and permits move-owned positioning',()=>{
  const positions=[];
  const mask={clear(){return this;},rect(){return this;},fill(){return this;}};
  const scene={nameMask:mask,nameT:{width:100},nameGhosts:[],nameG:{position:{set:(...xy)=>positions.push(xy)}}};
  const timing={start:0,end:100,ghostIn:0,ghostOut:100,ghostEnd:200,exit:100,finish:300};
  revealName(scene,200,timing);
  assert.deepEqual(positions,[[50,330]]);
  revealName(scene,200,{...timing,x:30,y:420,dx:80});
  assert.deepEqual(positions.at(-1),[50,420]);
  revealName(scene,200,{...timing,x:null,y:null});
  assert.equal(positions.length,2);
});
