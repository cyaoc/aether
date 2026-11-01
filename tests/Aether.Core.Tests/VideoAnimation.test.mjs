import test from 'node:test';
import assert from 'node:assert/strict';
import { prepareCues, AudioTrack } from '../../src/Aether.Core/Pages/video/animations/audio.mjs';

const fakeContext = overrides => ({state:'running',currentTime:0,baseLatency:0,destination:{},
  createGain:()=>({gain:{value:1,setValueAtTime(){},linearRampToValueAtTime(){}},connect(){},disconnect(){}}),...overrides});

test('peak alignment and explicit starts retain the design timing', () => {
  const buffer = {duration: 1, sampleRate: 4, numberOfChannels: 1, getChannelData: () => [0,.5,1,0]};
  const cues = prepareCues([{file:'a',t:2,peak:-6},{file:'a',t:2,at:.7,peak:0}], {a:buffer});
  assert.equal(cues[0].startAt,1.5);
  assert.equal(cues[1].startAt,.7);
  assert.equal(cues[1].level,1);
});

test('frame schedules once with output latency; stop stops all active sources', () => {
  const calls = [], sources = [];
  const ctx = fakeContext({currentTime:10,baseLatency:.01,outputLatency:.02,
    createBufferSource:()=>{const s={playbackRate:{},connect:()=>({connect(){}}),start:(...args)=>calls.push(args),stop:()=>calls.push('stop')};sources.push(s);return s;}});
  const cue={buffer:{},startAt:1,rate:1,from:0,end:2,level:1};
  const track=new AudioTrack(ctx,[cue],80,3);
  track.frame(900); track.frame(950);
  assert.equal(sources.length,1);
  assert.ok(Math.abs(calls[0][0]-10.07)<1e-9);
  track.stop(); assert.equal(calls.at(-1),'stop');
});

import fs from 'node:fs';
test('every registered sound event fires once and looping percussion stops with the animation', () => {
  const events=JSON.parse(fs.readFileSync(new URL('../../src/Aether.Core/Pages/video/audio/events.json',import.meta.url)));
  for(const cues of Object.values(events)) {
    const sources=[];
    const ctx=fakeContext({createBufferSource:()=>{const source={playbackRate:{},connect:()=>({connect(){}}),start(){this.started=true},stop(){this.stopped=true}};sources.push(source);return source;}});
    const buffers=Object.fromEntries(cues.map(c=>[c.file,{duration:20,sampleRate:1,numberOfChannels:1,getChannelData:()=>[1,...Array(19).fill(0)]}]));
    const track=new AudioTrack(ctx,prepareCues(cues,buffers),0,10);
    for(let t=0;t<=10000;t+=16)track.frame(t);
    assert.equal(sources.length,cues.length);assert.ok(sources.some(s=>s.loop));
    track.stop();assert.ok(sources.every(s=>s.stopped));
  }
});
