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
  slash({band:(...args)=>calls.push(args),arcLine:(...args)=>calls.push(args)},general.palette,{},{head:1,tail:.3,head2:1,width:40,fray:.5,seed:10,canvas});
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

import fs from 'node:fs';
test('every image the character names ships with the video page',()=>{
  assert.equal(Object.keys(general.textures).length,51);
  for(const path of Object.values(general.textures))
    assert.ok(fs.existsSync(new URL('../../src/Aether.Core/Pages/video/'+path,import.meta.url)),path);
});

import {cutCells,seamsOf,logicalTime,realTimes,sidesOf,pillar} from '../../src/Aether.Core/Pages/video/animations/toolkit.mjs';
test('a column of light fills with the character colours, fading as it goes',()=>{
  const fills=[],gb={beginPath(){},moveTo(){},lineTo(){},closePath(){},fill(){fills.push(this.fillStyle);}};
  pillar(gb,general.palette,0,0,[0,-100],96,.5);
  assert.deepEqual(fills,['rgba(255,40,60,0.15)','rgba(255,120,130,0.35)','rgba(255,255,255,0.5)']);
});

test('embers peel off the visible stretch of a seam, not the middle of the screen',()=>{
  const [seam]=seamsOf([[[-5000,700],[5000,700]]]);
  assert.ok(seam.pts.length>100);
  assert.ok(seam.pts.every(([x,y])=>x>-60&&x<1980&&y===700));
});

test('a hit stop holds logical time while real time runs on',()=>{
  const stops=[[100,50],[200,30]];
  assert.deepEqual([99,100,149,150,229,250,279,290].map(t=>logicalTime(t,stops)),[99,100,100,100,179,200,200,210]);
  assert.deepEqual(realTimes(stops),[100,250]);
});

test('both sides of a cut close round the sky',()=>{
  const sides=sidesOf([[-5000,540],[5000,540]]);
  assert.equal(sides.length,2);
  assert.ok(sides[0].some(([,y])=>y<0)!==sides[1].some(([,y])=>y<0));
});

test('cut cells retain thin visible fragments even when no sample falls inside',()=>{
  const cells=cutCells([[[99,-200],[99,200]]],[0,0],100,[-100,-100,100,100]);
  assert.equal(cells.length,2);
  const thin=cells.find(cell=>cell.poly.every(([x])=>x>=99));
  assert.equal(thin.n,0);
  assert.ok(thin.c.every(Number.isFinite));
  assert.ok(thin.c[0]>99 && thin.c[0]<100);
  assert.equal(cutCells([[[99,-200],[99,200]]],[0,0],100,[-100,-100,0,100]).length,1);
});
