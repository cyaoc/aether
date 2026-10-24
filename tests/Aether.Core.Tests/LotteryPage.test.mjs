// Run with: node --test tests/Aether.Core.Tests/LotteryPage.test.mjs
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';

const script = readFileSync(new URL('../../src/Aether.Core/Pages/lottery/page.js', import.meta.url), 'utf8');
function page(width = 1920, height = 1080, loadedTextures = []) {
  let now = Date.parse('2026-10-17T12:00:00Z'), nextId = 0, source, animationFrame;
  const timers = new Map(), elements = new Map(), listeners = new Map();
  let rectangles = [], drawnImages = [], position;
  const graphics = new Proxy({}, { get: (_, name) => (...args) => {
    if (name === 'translate') position = args;
    if (name === 'rect') rectangles.push(position);
    if (name === 'drawImage') drawnImages.push(args);
  } });
  const element = () => ({ hidden:true, children:[], dataset:{}, style:{setProperty(k,v){this[k]=v;}},
    setAttribute(name,value) { this[name] = value; },
    className:'', offsetLeft:0, offsetTop:0, offsetWidth:0, scrollHeight:0, clientHeight:0, scrollWidth:0, clientWidth:0,
    set textContent(value) { this.text = String(value); this.children = []; },
    get textContent() { return (this.text || '') + this.children.map(c=>c.textContent ?? c).join(''); },
    append(...children) { this.children.push(...children); },
    replaceChildren(...children) { this.text=''; this.children=children; },
    getContext:()=>graphics });
  const document = { querySelectorAll:()=>loadedTextures.map(name=>({href:`http://localhost/lottery/textures/${name}.webp`})), createElement:element, getElementById:id=>{
    if(!elements.has(id)) elements.set(id,element());
    return elements.get(id);
  }, fonts:{ready:Promise.resolve(),addEventListener(){}} };
  const timer = (callback,delay,repeat) => { const id=++nextId; timers.set(id,{callback,at:now+delay,repeat}); return id; };
  const window = {innerWidth:width,innerHeight:height,addEventListener(name,fn){listeners.set(name,fn);}};
  vm.runInNewContext(readFileSync(new URL('../../src/Aether.Core/Pages/lottery/textures.js', import.meta.url),'utf8') + '\n' + script, { document, window,
    Image:class { decode(){return Promise.resolve();} },
    Date:class extends Date { static now(){return now;} }, getComputedStyle:()=>({fontSize:'112px'}),
    EventSource:class { constructor(){source=this;} addEventListener(name,fn){listeners.set(name,fn);} },
    requestAnimationFrame:fn=>{animationFrame=fn;return 1;},cancelAnimationFrame:()=>{animationFrame=undefined;},
    setTimeout:(fn,ms)=>timer(fn,ms,0),setInterval:(fn,ms)=>timer(fn,ms,ms),
    clearTimeout:id=>timers.delete(id),clearInterval:id=>timers.delete(id) });
  return { element:id=>document.getElementById(id), state:state=>listeners.get('state')({data:JSON.stringify(state)}),
    hasFrame:()=>!!animationFrame,
    frame:()=>{rectangles=[]; drawnImages=[]; animationFrame?.(); return rectangles;},
    drawnImages:()=>drawnImages,
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
const entry = {mode:'Entry',id:'entry',phase:'Open',keyword:'<img onerror=alert(1)>',entryCount:7,
  latestEntries:[{nickname:'甲'},{nickname:'乙'},{nickname:'丙'}],awards:[award],draws:[],remaining:8,hidden:false,drawUntil:null};
const draws = Array.from({length:7},(_,i)=>({id:`d${i}`,awardIndex:0,award,winner:{uid:i+1,nickname:`观众${i+1}`},
  candidates:['甲','乙'],drawnAt:'2026-10-17T12:00:00Z'}));

test('pinned pool results show visible draws newest first without canvas work and yield to active views',()=>{
  const p=page(), prize={name:'<b>键盘</b>',grade:'Grand'};
  const pool={mode:'Pool',id:'pool',phase:'Idle',prizes:[prize],drawUntil:null,
    draws:[{id:'old',prize,uid:42,nickname:'小明'}, {id:'hidden',prize,hiddenFromStream:true}, {id:'new',prize,stoppedByHost:true}]};
  p.state({lottery:pool}); assert.equal(p.element('scene').hidden,true);
  p.state({lottery:pool,pinnedResults:true});
  assert.equal(p.element('scene').dataset.view,'pinned');
  assert.equal(p.element('pinnedCount').textContent,'共 2 次');
  assert.equal(p.element('pinnedRows').children.length,2);
  assert.match(p.element('pinnedRows').children[0].textContent,/主播点停/);
  assert.match(p.element('pinnedRows').children[1].textContent,/<b>键盘<\/b>小明/);
  assert.equal(p.hasFrame(),false);
  p.state({lottery:{...pool,phase:'Spinning'},pinnedResults:true});
  assert.equal(p.element('pinned').hidden,true);
  assert.equal(p.element('scene').dataset.view,'spinning');
  p.state({lottery:pool,pinnedResults:true,isExamplePlaying:true});
  assert.equal(p.element('scene').hidden,true);
  p.state({lottery:null,pinnedResults:true}); assert.equal(p.element('scene').hidden,true);
});

test('a large pinned pool only renders the visible page',()=>{
  const p=page(), prize={name:'贴纸',grade:'Third'}, rows=p.element('pinnedRows');
  let rendered=0;
  const replace=rows.replaceChildren;
  rows.replaceChildren=function(...children){rendered+=children.length;replace.apply(this,children);};
  p.state({pinnedResults:true,lottery:{mode:'Pool',id:'pool',phase:'Idle',
    draws:Array.from({length:1000},(_,i)=>({id:`p${i}`,prize,uid:i+1}))}});
  assert.equal(rendered,5);
  assert.equal(p.element('pinnedPages').style.height,'','pool pages keep their CSS height');
  p.advance(8000);
  assert.equal(rendered,10);
  assert.equal(p.element('pinnedPageNumber').textContent,'2 / 200');
});

test('pinned pages keep their eight second clock on repeats and only a completed local draw arrives',()=>{
  const p=page(), prize={name:'贴纸',grade:'Third'};
  const pool={mode:'Pool',id:'pool',phase:'Idle',prizes:[prize],drawUntil:null,
    draws:Array.from({length:7},(_,i)=>({id:`p${i}`,prize,uid:i+1}))};
  const state={lottery:pool,pinnedResults:true};
  p.state(state);
  assert.equal(p.element('pinnedPageNumber').textContent,'1 / 2');
  p.advance(8000);
  assert.equal(p.element('pinnedPageNumber').textContent,'2 / 2');
  const last=p.element('pinnedRows').children[0];
  p.advance(3000); p.state(state);
  assert.equal(p.element('pinnedRows').children[0],last);
  p.advance(5000); assert.equal(p.element('pinnedPageNumber').textContent,'1 / 2');
  const newDraw={id:'new',prize:{name:'键盘',grade:'Grand'},uid:42,drawnAt:'2026-10-17T12:00:16Z',candidates:[prize]};
  const drawing={...state,lottery:{...pool,draws:[...pool.draws,newDraw],drawUntil:'2026-10-17T12:00:23Z'}};
  p.state(drawing); p.advance(7000);
  assert.equal(p.element('scene').dataset.view,'pinned');
  assert.match(p.element('pinned').className,/arriving/);
  assert.match(p.element('pinnedRows').children[0].className,/fresh/);
  const arrived=p.element('pinnedRows').children[0];
  p.advance(8000); assert.equal(p.element('pinnedPageNumber').textContent,'2 / 2');
  const pageTwo=p.element('pinnedRows').children[0];
  p.state(drawing); assert.equal(p.element('pinnedRows').children[0],pageTwo,'expired remote deadline does not reset paging');
  p.advance(8000);
  assert.doesNotMatch(p.element('pinnedRows').children[0].className,/fresh/);
  assert.match(p.element('pinnedRows').children[0].className,/ambient/,'the newest Grand resumes ambience after paging back');
  const late=page(); late.advance(30000); late.state(drawing);
  assert.doesNotMatch(late.element('pinned').className,/arriving/);
  assert.doesNotMatch(late.element('pinnedRows').children[0].className,/fresh/);
  p.state({...state,lottery:{...pool,draws:[...pool.draws,newDraw,{...newDraw,id:'later'}]}});
  assert.equal(p.element('pinnedPageNumber').textContent,'1 / 2');
  assert.notEqual(p.element('pinnedRows').children[0],arrived);
  assert.doesNotMatch(p.element('pinned').className,/arriving/);
});

test('SSE reconnect preserves the pinned page without replaying entrance but a cleared or new lottery resets it',()=>{
  const prize={name:'贴纸',grade:'Third'};
  const pool={mode:'Pool',id:'pool',phase:'Idle',draws:Array.from({length:7},(_,i)=>({id:`p${i}`,prize,uid:i+1}))};
  for(const lottery of [pool,{...entry,phase:'Ended',hidden:true,draws}]){
    const p=page(), state={pinnedResults:true,lottery};
    p.state(state); p.advance(8000);
    assert.equal(p.element('pinnedPageNumber').textContent,'2 / 2');
    p.disconnect(); assert.equal(p.element('scene').hidden,true);
    p.state(state);
    assert.equal(p.element('scene').hidden,false);
    assert.equal(p.element('pinnedPageNumber').textContent,'2 / 2');
    assert.doesNotMatch(p.element('pinned').className,/entering|arriving/);
    p.advance(8000); assert.equal(p.element('pinnedPageNumber').textContent,'1 / 2');
    p.advance(8000); p.disconnect();
    p.state({lottery:null}); p.state(state);
    assert.equal(p.element('pinnedPageNumber').textContent,'1 / 2');
    assert.match(p.element('pinned').className,/entering/);
    p.advance(8000); p.disconnect();
    p.state({...state,lottery:{...lottery,id:'new-lottery'}});
    assert.equal(p.element('pinnedPageNumber').textContent,'1 / 2');
    assert.match(p.element('pinned').className,/entering/);
  }
});

test('pinned entry groups visible winners by award, repages at six names or three awards, and yields until collapsed',()=>{
  const p=page(), awards=Array.from({length:4},(_,i)=>({...award,name:`奖项${i}`,prize:`奖品${i}`}));
  const won=[0,1,1,2,3,3,3].map((awardIndex,i)=>({...draws[i],awardIndex,award:awards[awardIndex]}));
  const ended={...entry,awards,draws:won,phase:'Ended',hidden:true};
  for(const phase of ['Preparing','Open','Closed','Ended']){
    p.state({pinnedResults:true,lottery:{...ended,phase,hidden:false}});
    assert.equal(p.element('pinned').hidden,true,phase);
  }
  p.state({pinnedResults:true,lottery:ended});
  assert.equal(p.element('scene').dataset.view,'pinned');
  assert.equal(p.element('pinnedCount').textContent,'7 人中奖');
  assert.equal(p.element('pinnedRows').children.length,3);
  assert.match(p.element('pinnedRows').children[1].textContent,/奖项1 · 奖品1观众2观众3/);
  assert.equal(p.element('pinnedPageNumber').textContent,'1 / 2');
  p.advance(8000); assert.match(p.element('pinnedRows').textContent,/奖项3/);
  assert.doesNotMatch(p.element('pinned').className,/arriving/);
  const hidden=won.map(draw=>({...draw,hiddenFromStream:draw.awardIndex===1}));
  p.state({pinnedResults:true,lottery:{...ended,draws:hidden}});
  p.advance(1400);
  assert.doesNotMatch(p.element('pinnedRows').textContent,/奖项1|观众2|观众3/);
  assert.equal(p.element('pinnedCount').textContent,'5 人中奖');
  assert.equal(p.element('pinnedPageNumber').textContent,'');
  p.resize(1080,1920); assert.equal(p.element('pinnedRows').children.length,3);
});

test('hiding a pinned result collapses it before filling, restores without tier effects, and cancels stale transitions',()=>{
  const p=page(), prize={name:'贴纸',grade:'Third'}, won=Array.from({length:6},(_,i)=>({id:`p${i}`,prize,uid:i+1}));
  const pool={mode:'Pool',id:'pool',phase:'Idle',draws:won};
  const show=lottery=>p.state({pinnedResults:true,lottery});
  show(pool);
  const removed=p.element('pinnedRows').children[1];
  const hidden={...pool,draws:won.map(draw=>({...draw,hiddenFromStream:draw.id==='p4'}))};
  show(hidden);
  assert.equal(p.element('pinnedRows').children[1],removed);
  assert.match(removed.className,/hiding/);
  assert.equal(p.element('pinnedCount').textContent,'共 5 次');
  assert.equal(p.element('pinnedPageLabels').hidden,true,'a single remaining page releases the header before collapse finishes');
  assert.equal(p.element('pinnedPageNumber').textContent,'');
  p.advance(1320);
  assert.doesNotMatch(p.element('pinnedRows').textContent,/UID 5/);
  assert.match(p.element('pinnedRows').textContent,/UID 1/);
  assert.match(p.element('pinnedRows').children.at(-1).className,/filling/);
  show(pool); assert.doesNotMatch(p.element('pinned').className,/arriving|entering/);
  assert.doesNotMatch(p.element('pinnedRows').children[1].className,/fresh|filling|hiding/);
  show({...pool,draws:won.map(draw=>({...draw,hiddenFromStream:true}))});
  assert.equal(p.element('pinned').hidden,false,'the last rows also fade before the board disappears');
  p.advance(1320); assert.equal(p.element('scene').hidden,true);
  show(pool); assert.doesNotMatch(p.element('pinned').className,/arriving|entering/,'restoring after hiding everything also stays still');
  show(hidden); show(null);
  p.advance(10000); assert.equal(p.element('scene').hidden,true,'old transition cannot restore cleared results');
});

test('flag changes keep the active reveal intact and Core completing it shows the latest results',()=>{
  const p=page(), prize={name:'键盘',grade:'Grand'}, won={id:'new',prize,uid:42,candidates:[prize],drawnAt:'2026-10-17T12:00:00Z'};
  const pool={mode:'Pool',id:'pool',phase:'Idle',draws:[{...won,id:'old'},won],drawUntil:'2026-10-17T12:00:07Z'};
  p.state({lottery:pool});
  const cell=p.element('ring').children[0];
  p.advance(3000);
  const latest={...pool,draws:[pool.draws[0],{...won,hiddenFromStream:true}]};
  p.state({lottery:latest,pinnedResults:true});
  assert.equal(p.element('scene').dataset.view,'drawing');
  assert.equal(p.element('ring').children[0],cell);
  p.state({lottery:{...latest,drawUntil:null},pinnedResults:true});
  assert.equal(p.element('scene').dataset.view,'pinned');
  assert.equal(p.element('pinnedCount').textContent,'共 1 次');
  assert.doesNotMatch(p.element('pinned').className,/arriving/,'a hidden result has no pinned entrance');
  assert.equal(p.hasFrame(),false);
});

test("Core's finished snapshot ends the reveal and the pinned pool arrival plays once",()=>{
  const p=page(), prize={name:'键盘',grade:'Grand'}, won={id:'new',prize,uid:42,candidates:[prize],drawnAt:'2026-10-17T12:00:00Z'};
  const drawing={pinnedResults:true,lottery:{mode:'Pool',id:'pool',phase:'Idle',draws:[{...won,id:'old'},won],drawUntil:'2026-10-17T12:00:07Z'}};
  p.state(drawing); p.advance(3000);
  assert.equal(p.element('scene').dataset.view,'drawing');
  const finished={...drawing,lottery:{...drawing.lottery,drawUntil:null}};
  p.state(finished);
  assert.equal(p.element('scene').dataset.view,'pinned','the page does not wait for its own clock');
  assert.match(p.element('pinned').className,/arriving/);
  const arrived=p.element('pinnedRows').children[0];
  assert.match(arrived.className,/fresh/);
  p.state(finished); p.advance(4000);
  assert.equal(p.element('pinnedRows').children[0],arrived,'neither a repeated snapshot nor the old local deadline replays it');
  p.advance(2000);
  assert.doesNotMatch(p.element('pinned').className,/arriving/);
  assert.doesNotMatch(arrived.className,/fresh/);
});

test('hidden winners leave full results, empty awards and pages, and restoring redraws the list',()=>{
  const second={name:'二等奖',prize:'抱枕',capacity:1,grade:'Second'};
  const won=[...draws,{...draws[0],id:'second',awardIndex:1,award:second,winner:{uid:8,nickname:'第八位'}}];
  for(const theme of ['StarSummon','MechaLock','CapsuleGacha']){
    const p=page(), show=(results,phase='Ended')=>p.state({theme,lottery:{...entry,phase,awards:[award,second],draws:results}});
    show(won);
    assert.equal(p.element('pageNumber').textContent,'1 / 2');
    const hidden=won.map((draw,index)=>({...draw,hiddenFromStream:index===0 || index===7}));
    show(hidden);
    assert.doesNotMatch(p.element('winnerCards').textContent,/观众1|第八位|二等奖/);
    assert.match(p.element('winnerCards').textContent,/观众7/);
    assert.equal(p.element('pageNumber').textContent,'');
    const cards=p.element('winnerCards').children;
    p.advance(5000);
    assert.equal(p.element('winnerCards').children,cards,'No previous paging timer survives the smaller list');
    show(hidden);
    assert.equal(p.element('winnerCards').children,cards,'An identical visibility snapshot does not redraw');
    show(won.map(draw=>({...draw,hiddenFromStream:draw.awardIndex===0})));
    assert.equal(p.element('winnerCards').children.length,1);
    assert.match(p.element('winnerCards').textContent,/第八位/);
    assert.doesNotMatch(p.element('winnerCards').textContent,/一等奖/);
    for(const phase of ['Closed','Ended']){
      show(won.map(draw=>({...draw,hiddenFromStream:true})),phase);
      assert.equal(p.element('winnerCards').children.length,0);
      assert.equal(p.element('pageNumber').textContent,phase==='Ended'?'本场没有开奖':'等待开奖');
    }
    show(won);
    assert.match(p.element('winnerCards').textContent,/观众1/);
    assert.equal(p.element('pageNumber').textContent,'1 / 2');
  }
});

test('hiding a winner during the draw keeps its animation and filters the latest result afterward',()=>{
  const p=page(), state={lottery:{...entry,phase:'Closed',draws:[draws[0]],drawUntil:'2026-10-17T12:00:07Z'}};
  p.state(state);
  const ring=p.element('ring').children;
  p.advance(2000);
  p.state({lottery:{...state.lottery,draws:[{...draws[0],hiddenFromStream:true}]}});
  assert.equal(p.element('ring').children,ring);
  assert.equal(p.element('summon').style['--elapsed'],'0s');
  assert.equal(p.element('prize').textContent,'观众1');
  p.advance(5000);
  assert.equal(p.element('scene').dataset.view,'winners');
  assert.equal(p.element('winnerCards').children.length,0);
  assert.equal(p.element('pageNumber').textContent,'等待开奖');
});

test('Capsule Gacha fires confetti upwards from both sides once for Second and twice for Grand',()=>{
  for (const [width,height] of [[1920,1080],[1080,1920]]) for (const grade of ['Grand','Second','Third']) {
    const p=page(width,height), draw={...draws[0],award:{...award,grade}};
    p.state({theme:'CapsuleGacha',lottery:{...entry,phase:'Closed',draws:[draw],drawUntil:'2026-10-17T12:00:07Z'}});
    p.advance(1900);
    assert.equal(p.frame().length,0);
    p.advance(120);
    const first=p.frame();
    if (grade === 'Third') assert.equal(first.length,0);
    else {
      const middle=width===1080 ? 459 : 1180, bottom=width===1080 ? 1150 : 960;
      assert.ok(first.some(([x])=>x<middle) && first.some(([x])=>x>middle));
      assert.ok(first.every(([,y])=>y<bottom && y>bottom-100),'Confetti starts inside the source and travels upwards');
    }
    p.advance(700);
    const second=p.frame();
    assert.equal(second.length,grade==='Grand' ? first.length*2 : first.length);
    p.advance(200);
    assert.equal(p.frame().length,second.length,'No extra volley on subsequent frames');
  }
});

test('Capsule Gacha drops a capsule beside the count and matches the newest name colour',()=>{
  const p=page(), show=e=>p.state({theme:'CapsuleGacha',lottery:{...e,latestEntries:e.latestEntries.slice(0,e.entryCount)}});
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
  p.state({theme:'CapsuleGacha',lottery:{...entry,phase:'Closed',awards,draws:won,remaining:3}});
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
  const p=page(), show=phase=>p.state({theme:'CapsuleGacha',lottery:{...entry,phase,draws,remaining:1}});
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
    const lottery={mode:'Pool',id:'pool',phase:'Idle',drawUntil:'2026-10-17T12:00:07Z',
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
  const lottery={mode:'Pool',id:'pool',phase:'Idle',drawUntil:'2026-10-17T12:00:07Z',
    draws:[{id:'d',prize,uid:42,candidates:[other,prize],drawnAt:'2026-10-17T12:00:00Z'}]};
  p.state({lottery}); p.advance(1000);
  p.state({lottery,theme:'MechaLock'});
  assert.equal(p.element('rank').textContent,'大奖');
  assert.equal(p.element('rank').children[0]['aria-label'],'S 级');
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
  p.state({lottery:{mode:'Pool',id:'pool',phase:'Spinning',prizes,draws:[]},theme:'MechaLock'});
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
  p.state({lottery:{...entry,phase:'Closed',draws:[draws[0]],drawUntil:'2026-10-17T12:00:07Z'}});
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
  const p=page(); p.state({lottery:entry});
  assert.equal(p.element('registration').hidden,false);
  assert.equal(p.element('keyword').textContent,'发弹幕「<img onerror=alert(1)>」');
  assert.equal(p.element('keyword').children.length,0);
  assert.equal(p.element('entryCount').textContent,'7');
  assert.equal(p.element('latestEntries').textContent,'甲乙丙');
  p.state({lottery:{...entry,phase:'Closed',draws,remaining:1}});
  assert.equal(p.element('winnerTitle').textContent,'目前的召唤结果');
  assert.match(p.element('remaining').textContent,/还剩 1 个名额/);
  assert.equal(p.element('winnerCards').children.length,6);
  p.advance(4999); assert.equal(p.element('pageNumber').textContent,'1 / 2');
  p.advance(1); assert.match(p.element('winnerCards').textContent,/观众7/);
  assert.equal(p.element('winnerCards').children.length,1);
  p.state({lottery:{...entry,phase:'Ended',draws}});
  assert.equal(p.element('winnerTitle').textContent,'召唤结果');
  assert.equal(p.element('remaining').hidden,true);
  assert.doesNotMatch(p.element('winnerCards').textContent,/键盘/);
  p.state({lottery:{...entry,phase:'Ended',draws,hidden:true}});
  assert.equal(p.element('scene').hidden,true);
  p.advance(5000);
  p.state({lottery:{...entry,phase:'Ended',draws}});
  assert.equal(p.element('winners').hidden,false);
  p.state({lottery:{...entry,phase:'Preparing'}});
  assert.equal(p.element('scene').hidden,true);
});

test('one draw runs to its deadline and repeat snapshots or reconnects do not replay it',()=>{
  const p=page(), state={lottery:{...entry,phase:'Closed',draws:[draws[0]],drawUntil:'2026-10-17T12:00:07Z'}};
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

test('an idle pool with a draw deadline reveals its prize until the deadline without replaying',()=>{
  const p=page(), prize={name:'手办',grade:'Grand',weight:1};
  const state={lottery:{mode:'Pool',id:'pool',phase:'Idle',drawUntil:'2026-10-17T12:00:07Z',
    draws:[{id:'d',prize,uid:42,candidates:[prize],drawnAt:'2026-10-17T12:00:00Z'}]}};
  p.state(state);
  assert.equal(p.element('scene').hidden,false);
  assert.equal(p.element('scene').dataset.view,'drawing');
  assert.equal(p.element('prize').textContent,'手办');
  p.advance(3000); p.state(state);
  assert.equal(p.element('summon').style['--elapsed'],'0s');
  p.advance(3999); assert.equal(p.element('scene').hidden,false);
  p.advance(1); assert.equal(p.element('scene').hidden,true);
  p.state(state); assert.equal(p.element('scene').hidden,true);
});

test('pool spinning has only prizes, each grade reveals the drawn prize, then becomes transparent',()=>{
  for(const grade of ['Grand','Second','Third']){
    const p=page(), prize={name:'手办',grade,weight:1};
    const pool={mode:'Pool',id:'pool',phase:'Spinning',prizes:[prize],draws:[],uid:42,stopWord:'停'};
    p.state({lottery:pool});
    assert.equal(p.element('scene').dataset.view,'spinning');
    assert.match(p.element('ring').textContent,/手办/);
    assert.equal(p.element('banner').textContent,'');
    p.state({lottery:{...pool,phase:'Idle',drawUntil:'2026-10-17T12:00:07Z',
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

function texturePage() {
  const calls = [], images = [], canvases = [];
  const context = vm.createContext({
    Image:class { constructor(){images.push(this);} decode(){return Promise.resolve();} },
    document:{querySelectorAll:()=>[{href:'http://localhost/lottery/textures/summon/flare.webp'}],
      createElement:()=>{ const canvas={getContext:()=>new Proxy({}, {set:(o,k,v)=>{calls.push([k,v]);return true;},get:(_,k)=>(...args)=>calls.push([k,...args])})}; canvases.push(canvas); return canvas;}},
  });
  vm.runInContext(readFileSync(new URL('../../src/Aether.Core/Pages/lottery/textures.js', import.meta.url),'utf8'),context);
  return {textures:vm.runInContext('textures',context), calls, images, canvases};
}
test('texture atlas positions match the supplied three-column and four-by-four sheets',()=>{
  const {textures:t}=texturePage();
  assert.deepEqual(['Grand','Second','Third'].map(g=>t.gradePosition(g)),['0%','50%','100%']);
  assert.deepEqual(Array.from(t.confettiCell('heart','#ff7aae','Grand')),[0,0,96,96]);
  assert.deepEqual(Array.from(t.confettiCell('star','#ffd45e','Second')),[96,96,96,96]);
  assert.deepEqual(Array.from(t.confettiCell('strip','#5fd3b0','Third')),[192,192,96,96]);
  assert.deepEqual(Array.from(t.confettiCell('candy','#7ccbff','Grand')),[288,288,96,96]);
  assert.deepEqual(Array.from(t.confettiCell('dot','#fff','Third')),[288,192,96,96]);
});
test('glow textures wait for decoding and cache each image, colour and size with a white core',async()=>{
  const {textures:t,calls,canvases}=texturePage();
  assert.equal(t.tinted('summon/flare','#fff',40,20),null);
  await t.ready;
  const first=t.tinted('summon/flare','#fff',40,20);
  assert.equal(first.width,40); assert.equal(first.height,20);
  assert.equal(t.tinted('summon/flare','#fff',40,20),first);
  assert.notEqual(t.tinted('summon/flare','#fc0',40,20),first);
  assert.notEqual(t.tinted('summon/flare','#fff',80,20),first);
  assert.equal(canvases.length,3);
  assert.ok(calls.some(c=>c[0]==='globalCompositeOperation'&&c[1]==='source-in'));
  assert.ok(calls.some(c=>c[0]==='globalCompositeOperation'&&c[1]==='lighter'));
  assert.ok(calls.some(c=>c[0]==='globalAlpha'&&c[1]===.6));
});


test('Mecha result badges expose the drawn rank in both modes',()=>{
  for (const [grade,rank] of [['Grand','S'],['Second','A'],['Third','B']]) for (const isEntry of [false,true]) {
    const p=page(), prize={name:'机械键盘',grade};
    const draw={...draws[0],prize,award:{...award,grade}};
    const lottery=isEntry ? {...entry,phase:'Drawing',draws:[draw]} : {mode:'Pool',id:'pool',phase:'Idle',prizes:[prize],draws:[draw]};
    p.state({theme:'MechaLock',lottery:{...lottery,drawUntil:'2026-10-17T12:00:07Z'}});
    const badge=p.element('rank').children.find(child=>child.role==='img');
    assert.ok(badge,'the rank is represented by an accessible badge');
    assert.equal(badge['aria-label'],`${rank} 级`);
  }
});

test('gacha opening uses the decoded confetti atlas with shape and grade colour crops',async()=>{
  const p=page(1920,1080,['gacha/confetti']), prize={name:'贴纸',grade:'Third'};
  await new Promise(resolve=>setImmediate(resolve));
  p.state({theme:'CapsuleGacha',lottery:{mode:'Pool',id:'pool',phase:'Idle',prizes:[prize],
    draws:[{id:'draw',prize,candidates:[prize],drawnAt:'2026-10-17T12:00:00Z'}],drawUntil:'2026-10-17T12:00:07Z'}});
  p.advance(2000); p.frame();
  const sprites=p.drawnImages();
  assert.ok(sprites.length>=30);
  assert.deepEqual([...new Set(sprites.map(args=>args[1]))].sort((a,b)=>a-b),[0,96,288]);
  assert.ok(sprites.every(args=>args[2]===192&&args[3]===96&&args[4]===96));
  assert.ok(sprites.every(args=>args[7]===args[8]&&args[7]>=21.6));
});

test('summon flares follow the hit window and portrait width with decoded sprites',async()=>{
  for (const [width,height] of [[1920,1080],[1080,1920]]) for (const [grade,scale] of [['Grand',1],['Second',.8],['Third',.55]]) {
    const p=page(width,height,['summon/flare']), prize={name:'星辰',grade};
    await new Promise(resolve=>setImmediate(resolve));
    p.state({theme:'StarSummon',lottery:{mode:'Pool',id:'pool',phase:'Idle',prizes:[prize],
      draws:[{id:'draw',prize,candidates:[prize],drawnAt:'2026-10-17T12:00:00Z'}],drawUntil:'2026-10-17T12:00:07Z'}});
    p.advance(2050); p.frame();
    assert.ok(p.drawnImages().some(a=>a[0].width===128),'burst particles use the shared decoded sprite');
    assert.ok(!p.drawnImages().some(a=>a[3]===a[4]*2),'flash has not started at 2050ms');
    p.advance(450); p.frame();
    const flash=p.drawnImages().find(a=>a[3]===a[4]*2);
    assert.ok(flash,'hit has a horizontal flare');
    assert.equal(flash[3],2*(width===1080 ? 260+220*.875 : 380+520*.875)*scale);
    assert.equal(flash[1]+flash[3]/2,width===1080 ? 459 : 960);
    p.advance(450); p.frame();
    assert.ok(!p.drawnImages().some(a=>a[3]===a[4]*2),'flash ends at 2950ms');
  }
});
