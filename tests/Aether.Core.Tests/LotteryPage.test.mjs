// Run with: node --test tests/Aether.Core.Tests/LotteryPage.test.mjs
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';

const script = readFileSync(new URL('../../src/Aether.Core/LotteryPage.js', import.meta.url), 'utf8');
function page(width = 1920, height = 1080) {
  let now = Date.parse('2026-10-17T12:00:00Z'), nextId = 0, source;
  const timers = new Map(), elements = new Map(), listeners = new Map();
  const graphics = new Proxy({}, { get: () => () => {} });
  const element = () => ({ hidden:true, children:[], dataset:{}, style:{setProperty(k,v){this[k]=v;}},
    className:'', offsetWidth:0, scrollHeight:0, clientHeight:0, scrollWidth:0, clientWidth:0,
    set textContent(value) { this.text = String(value); this.children = []; },
    get textContent() { return (this.text || '') + this.children.map(c=>c.textContent ?? c).join(''); },
    append(...children) { this.children.push(...children); },
    replaceChildren(...children) { this.text=''; this.children=children; },
    getContext:()=>graphics });
  const document = { createElement:element, getElementById:id=>{
    if(!elements.has(id)) elements.set(id,element());
    return elements.get(id);
  }, fonts:{ready:Promise.resolve(),addEventListener(){}} };
  const timer = (callback,delay,repeat) => { const id=++nextId; timers.set(id,{callback,at:now+delay,repeat}); return id; };
  const window = {innerWidth:width,innerHeight:height,addEventListener(name,fn){listeners.set(name,fn);}};
  vm.runInNewContext(script, { document, window,
    Date:class extends Date { static now(){return now;} }, getComputedStyle:()=>({fontSize:'112px'}),
    EventSource:class { constructor(){source=this;} addEventListener(name,fn){listeners.set(name,fn);} },
    requestAnimationFrame:()=>0,cancelAnimationFrame:()=>{},
    setTimeout:(fn,ms)=>timer(fn,ms,0),setInterval:(fn,ms)=>timer(fn,ms,ms),
    clearTimeout:id=>timers.delete(id),clearInterval:id=>timers.delete(id) });
  return { element:id=>document.getElementById(id), state:state=>listeners.get('state')({data:JSON.stringify(state)}),
    resize:(width,height)=>{window.innerWidth=width; window.innerHeight=height; listeners.get('resize')();},
    disconnect:()=>source.onerror(), advance:ms=>{
      const end=now+ms;
      while(true){
        const due=[...timers].filter(([,t])=>t.at<=end).sort((a,b)=>a[1].at-b[1].at)[0];
        if(!due)break;
        const [id,t]=due; now=t.at;
        if(t.repeat)t.at+=t.repeat;else timers.delete(id);
        t.callback();
      }
      now=end;
    } };
}
const award = {name:'一等奖',prize:'键盘',capacity:8,grade:'Grand'};
const entry = {id:'entry',phase:'Open',keyword:'<img onerror=alert(1)>',entryCount:7,
  latestEntries:[{nickname:'甲'},{nickname:'乙'},{nickname:'丙'}],awards:[award],draws:[],remaining:8,hidden:false,drawUntil:null};
const draws = Array.from({length:7},(_,i)=>({id:`d${i}`,awardIndex:0,award,winner:{uid:i+1,nickname:`观众${i+1}`},
  candidates:['甲','乙'],drawnAt:'2026-10-17T12:00:00Z'}));

test('Capsule Gacha drops a capsule beside the count and matches the newest name colour',()=>{
  const p=page(), show=e=>p.state({theme:'CapsuleGacha',entryLottery:{...e,latestEntries:e.latestEntries.slice(0,e.entryCount)}});
  show({...entry,entryCount:0});
  assert.equal(p.element('entryCount').textContent,'0');
  show({...entry,entryCount:7});
  assert.equal(p.element('entryCounter').className,'count pop');
  assert.equal(p.element('capsuleDrop').style['--cap'],'#a991ff');
  assert.equal(p.element('latestEntries').children[0].style['--cap'],'#a991ff');
  assert.equal(p.element('latestEntries').children[1].style['--tint'],'#ddf7ee');
  const last=p.element('latestEntries').children[0];
  show({...entry,entryCount:7});
  assert.equal(p.element('latestEntries').children[0],last);
  show({...entry,entryCount:21});
  show({...entry,entryCount:22});
  assert.equal(p.element('capsules').children.length,18);
  assert.equal(p.element('capsuleDrop').style['--cap'],p.element('latestEntries').children[0].style['--cap']);
  assert.equal(p.element('entryCount').textContent,'22');
  show({...entry,id:'next',entryCount:0});
  assert.equal(p.element('entryCount').textContent,'0');
});

test('Capsule Gacha groups winners by award, pages at three awards and uses its closed wording',()=>{
  const awards=['甲','乙','丙','丁'].map((name,i)=>({name:`${name}等奖`,prize:`奖品${i}`,capacity:2,grade:['Grand','Second','Third','Third'][i]}));
  const won=[0,1,1,2,3].map((awardIndex,i)=>({id:`w${i}`,awardIndex,award:awards[awardIndex],winner:{uid:i,nickname:`观众${i}`},candidates:[],drawnAt:'2026-10-17T12:00:00Z'}));
  const p=page();
  p.state({theme:'CapsuleGacha',entryLottery:{...entry,phase:'Closed',awards,draws:won,remaining:3}});
  const rows=p.element('winnerCards').children;
  assert.equal(rows.length,3);
  assert.equal(rows[1].className,'winnerGroup Second');
  assert.equal(rows[1].children[1].textContent,'乙等奖奖品1');
  assert.equal(rows[1].children[2].children.length,2);
  assert.equal(rows[1].children[2].children[0].textContent,'观众1');
  assert.equal(p.element('pageNumber').textContent,'1 / 2');
  assert.equal(p.element('remaining').textContent,'报名截止啦 · 还剩 3 个名额');
  p.advance(5000);
  assert.equal(p.element('winnerCards').children.length,1);
  assert.match(p.element('winnerCards').textContent,/丁等奖.*观众4/);
});

test('Capsule Gacha repages on rotation and leaves room for remaining places until the lottery ends',()=>{
  const p=page(), show=phase=>p.state({theme:'CapsuleGacha',entryLottery:{...entry,phase,draws,remaining:1}});
  show('Closed');
  assert.equal(p.element('winnerCards').children[0].children[2].children.length,6);
  p.resize(1080,1920);
  assert.equal(p.element('winnerCards').children[0].children[2].children.length,5);
  p.resize(1920,1080);
  assert.equal(p.element('winnerCards').children[0].children[2].children.length,6);
  p.resize(1080,1920);
  show('Ended');
  assert.equal(p.element('winnerCards').children[0].children[2].children.length,6);
});

test('Capsule Gacha switches mid-draw without replaying, shows all three grades and preserves text safely',()=>{
  for(const [grade,pop] of [['Grand','大奖!!'],['Second','POP!'],['Third','pop']]){
    const p=page(), prize={name:'<b>手办</b>',grade};
    const lottery={id:'pool',phase:'Drawing',drawUntil:'2026-10-17T12:00:07Z',
      draws:[{id:'d',prize,uid:42,candidates:[prize],drawnAt:'2026-10-17T12:00:00Z'}]};
    p.state({lottery}); p.advance(1500);
    p.state({lottery,theme:'CapsuleGacha'});
    assert.equal(p.element('summon').style['--elapsed'],'-1.5s');
    assert.equal(p.element('shout').textContent,pop);
    assert.equal(p.element('prize').textContent,'<b>手办</b>');
    assert.equal(p.element('prize').children.length,0);
    assert.equal(p.element('viewer').textContent,'UID 42 扭到了');
    p.resize(1080,1920); p.advance(5500);
    assert.equal(p.element('scene').hidden,true);
  }
});

test('switching to Mecha Lock refreshes a live draw, locks the winning cell and keeps the deadline',()=>{
  const p=page(), prize={name:'键盘',grade:'Grand'}, other={name:'签名照',grade:'Third'};
  const lottery={id:'pool',phase:'Drawing',drawUntil:'2026-10-17T12:00:07Z',
    draws:[{id:'d',prize,uid:42,candidates:[other,prize],drawnAt:'2026-10-17T12:00:00Z'}]};
  p.state({lottery}); p.advance(1000);
  p.state({lottery,theme:'MechaLock'});
  assert.equal(p.element('rank').textContent,'S 级大奖');
  assert.equal(p.element('summon').style['--elapsed'],'-1s');
  assert.equal(p.element('prize').textContent,'键盘');
  p.advance(1100);
  const locked=p.element('ring').children.filter(c=>c.dataset.lock==='LOCK ON');
  assert.equal(locked.length,1);
  assert.match(locked[0].textContent,/键盘/);
  p.state({lottery,theme:'MechaLock'});
  assert.equal(p.element('summon').style['--elapsed'],'-1s');
  p.resize(1080,1920);
  p.advance(4899); assert.equal(p.element('scene').hidden,false);
  p.advance(1); assert.equal(p.element('scene').hidden,true);
});

test('Mecha Lock spinning hops the scanning frame between cells',()=>{
  const p=page(), prizes=[{name:'键盘',grade:'Grand'},{name:'签名照',grade:'Third'}];
  p.state({lottery:{id:'pool',phase:'Spinning',prizes,draws:[]},theme:'MechaLock'});
  const scanned=()=>p.element('ring').children.findIndex(c=>c.dataset.lock==='SCANNING');
  const first=scanned();
  assert.notEqual(first,-1);
  p.advance(400);
  assert.notEqual(scanned(),first);
});

test('viewport selects portrait only when taller than wide and resizing keeps the draw deadline',()=>{
  const p=page(1080,1920);
  assert.equal(p.element('scene').dataset.orientation,'portrait');
  assert.equal(p.element('scene').style.transform,'scale(1) translate(-50%,-50%)');
  p.state({entryLottery:{...entry,phase:'Closed',draws:[draws[0]],drawUntil:'2026-10-17T12:00:07Z'}});
  p.advance(3000);
  p.resize(800,600);
  assert.equal(p.element('scene').dataset.orientation,'landscape');
  assert.equal(p.element('scene').style.transform,'scale(0.4166666666666667) translate(-50%,-50%)');
  p.resize(800,800);
  assert.equal(p.element('scene').dataset.orientation,'landscape');
  p.resize(540,960);
  assert.equal(p.element('scene').dataset.orientation,'portrait');
  assert.equal(p.element('scene').style.transform,'scale(0.5) translate(-50%,-50%)');
  assert.equal(p.element('summon').style['--elapsed'],'0s');
  p.advance(4000);
  assert.equal(p.element('scene').dataset.view,'winners');
});

test('registration uses text nodes, closed and ended results page by six and hidden clears timers',()=>{
  const p=page(); p.state({entryLottery:entry});
  assert.equal(p.element('registration').hidden,false);
  assert.equal(p.element('keyword').textContent,'发弹幕「<img onerror=alert(1)>」');
  assert.equal(p.element('keyword').children.length,0);
  assert.equal(p.element('entryCount').textContent,'7');
  assert.equal(p.element('latestEntries').textContent,'甲乙丙');
  p.state({entryLottery:{...entry,phase:'Closed',draws,remaining:1}});
  assert.equal(p.element('winnerTitle').textContent,'目前的召唤结果');
  assert.match(p.element('remaining').textContent,/还剩 1 个名额/);
  assert.equal(p.element('winnerCards').children.length,6);
  p.advance(4999); assert.equal(p.element('pageNumber').textContent,'1 / 2');
  p.advance(1); assert.match(p.element('winnerCards').textContent,/观众7/);
  assert.equal(p.element('winnerCards').children.length,1);
  p.state({entryLottery:{...entry,phase:'Ended',draws}});
  assert.equal(p.element('winnerTitle').textContent,'召唤结果');
  assert.equal(p.element('remaining').hidden,true);
  assert.doesNotMatch(p.element('winnerCards').textContent,/键盘/);
  p.state({entryLottery:{...entry,phase:'Ended',draws,hidden:true}});
  assert.equal(p.element('scene').hidden,true);
  p.advance(5000);
  p.state({entryLottery:{...entry,phase:'Ended',draws}});
  assert.equal(p.element('winners').hidden,false);
  p.state({entryLottery:{...entry,phase:'Preparing'}});
  assert.equal(p.element('scene').hidden,true);
});

test('one draw runs to its deadline and repeat snapshots or reconnects do not replay it',()=>{
  const p=page(), state={entryLottery:{...entry,phase:'Closed',draws:[draws[0]],drawUntil:'2026-10-17T12:00:07Z'}};
  p.state(state);
  assert.equal(p.element('scene').dataset.view,'drawing');
  assert.match(p.element('ring').textContent,/甲/);
  assert.equal(p.element('prize').textContent,'观众1');
  p.advance(3000); p.state(state);
  assert.equal(p.element('summon').style['--elapsed'],'0s');
  p.advance(3999); assert.equal(p.element('scene').dataset.view,'drawing');
  p.advance(1); assert.equal(p.element('scene').dataset.view,'winners');
  p.state(state); assert.equal(p.element('scene').dataset.view,'winners');
  const late=page(); late.advance(6000); late.state(state);
  assert.equal(late.element('summon').style['--elapsed'],'-6s');
  late.advance(1000); assert.equal(late.element('scene').dataset.view,'winners');
  late.disconnect(); assert.equal(late.element('scene').hidden,true);
  late.state(state); assert.equal(late.element('scene').dataset.view,'winners');
});

test('pool spinning has only prizes, each grade reveals the drawn prize, then becomes transparent',()=>{
  for(const grade of ['Grand','Second','Third']){
    const p=page(), prize={name:'手办',grade,weight:1};
    const pool={id:'pool',phase:'Spinning',prizes:[prize],draws:[],uid:42,stopWord:'停'};
    p.state({lottery:pool});
    assert.equal(p.element('scene').dataset.view,'spinning');
    assert.match(p.element('ring').textContent,/手办/);
    assert.equal(p.element('banner').textContent,'');
    p.state({lottery:{...pool,phase:'Drawing',drawUntil:'2026-10-17T12:00:07Z',
      draws:[{id:'d',prize,uid:42,nickname:null,candidates:[prize],drawnAt:'2026-10-17T12:00:00Z'}]}});
    assert.equal(p.element('summon').className,`drawing ${grade}`);
    assert.equal(p.element('prize').textContent,'手办');
    assert.equal(p.element('viewer').textContent,'UID 42');
    assert.equal(p.element('banner').textContent,'UID 42 发起召唤！');
    assert.equal(p.element('rank').textContent,{Grand:'大奖',Second:'二奖',Third:'三奖'}[grade]);
    p.advance(7000); assert.equal(p.element('scene').hidden,true);
    p.advance(7000); assert.equal(p.element('scene').hidden,true);
  }
});
