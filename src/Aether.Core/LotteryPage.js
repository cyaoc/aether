'use strict';
const byId = id => document.getElementById(id);
const scene = byId('scene'), summon = byId('summon'), ring = byId('ring');
const grades = { Grand:'大奖', Second:'二奖', Third:'三奖' };
const ranks = { Grand:'SSR', Second:'SR', Third:'R' };
let shown = '', hiding, paging, frame, fitting = [];
const canvas = byId('particles'), context = canvas.getContext('2d');
function resize() {
  const scale = Math.min(window.innerWidth / 1920, window.innerHeight / 1080);
  scene.style.transform = `scale(${scale}) translate(-50%,-50%)`;
}
window.addEventListener('resize', resize);
resize();
function text(className, value) {
  const node = document.createElement('div');
  node.className = className; node.textContent = value;
  return node;
}
function fit(node, maximum) {
  node.style.fontSize = maximum + 'px';
  const height = parseFloat(getComputedStyle(node).maxHeight) || Infinity;
  while (maximum > 56 && (node.scrollHeight > height + 1 || node.scrollWidth > node.clientWidth + 1))
    node.style.fontSize = --maximum + 'px';
}
function fitAll() { fitting.forEach(([node, size]) => fit(node, size)); }
document.fonts.ready.then(fitAll);
document.fonts.addEventListener('loadingdone', fitAll);
function clear() {
  clearTimeout(hiding); clearInterval(paging); cancelAnimationFrame(frame);
  scene.hidden = true; summon.className = ''; fitting = [];
  for (const id of ['registration','summon','winners']) byId(id).hidden = true;
  context.clearRect(0, 0, 1920, 1080);
}
function view(name) {
  scene.hidden = false; scene.dataset.view = name;
  byId(name === 'drawing' || name === 'spinning' ? 'summon' : name).hidden = false;
}
function show(state) {
  scene.dataset.theme = state.theme || 'StarSummon';
  const entry = state.entryLottery, pool = state.lottery;
  const lottery = entry || pool;
  // Repeated state snapshots must not restart a draw, including on a room reconnect.
  const key = lottery ? [lottery.id,lottery.phase,lottery.draws.length,lottery.draws.at(-1)?.id,
    lottery.drawUntil,lottery.entryCount,lottery.hidden].join(' ') : '';
  if (key === shown) return;
  shown = key; clear();
  if (!lottery || entry?.hidden || entry?.phase === 'Preparing' || pool?.phase === 'Idle') return;
  if (entry?.phase === 'Open') {
    view('registration');
    byId('keyword').textContent = `发弹幕「${entry.keyword}」`;
    byId('entryCount').textContent = entry.entryCount;
    byId('latestEntries').replaceChildren(...entry.latestEntries.slice(0,2).map(item => text('',item.nickname)));
    byId('awardRows').replaceChildren(...entry.awards.map(award => {
      const row = text(`awardRow ${award.grade}`, '');
      row.append(text('rank',ranks[award.grade]),text('awardPrize',award.prize),text('capacity',`×${award.capacity}`));
      return row;
    }));
    fitting = [[byId('keyword'),76],[byId('entryCount'),130]]; fitAll();
    return;
  }
  if (pool?.phase === 'Spinning') {
    view('spinning'); summon.className = 'Second';
    byId('banner').textContent = '';
    makeRing(pool.prizes);
    particles(null, 'Second');
    return;
  }
  const draw = lottery.draws.at(-1), remaining = Date.parse(lottery.drawUntil) - Date.now();
  if (draw && remaining > 0) {
    drawResult(draw, !!entry);
    hiding = setTimeout(() => {
      if (entry) show({ ...state, entryLottery:{...entry,drawUntil:null} });
      else clear();
    }, remaining);
    return;
  }
  if (entry) showWinners(entry);
}
function makeRing(candidates) {
  // Ten visual cells fill the circle; Core has already chosen the result.
  const cells = Array.from({length:10},(_,index) => candidates[index % candidates.length]);
  ring.replaceChildren(...cells.map((candidate,index) => {
    const card = text(`candidate ${candidate.grade}`, '');
    card.style.transform = `rotateY(${index * 36}deg) translateZ(430px)`;
    card.append(text('candidateLabel',candidate.label || grades[candidate.grade]),text('candidateName',candidate.name));
    return card;
  }));
  return cells;
}
function drawResult(draw, entry) {
  const grade = entry ? draw.award.grade : draw.prize.grade;
  const elapsed = Math.max(0, Date.now() - Date.parse(draw.drawnAt));
  view('drawing');
  summon.className = grade;
  const candidates = entry ? draw.candidates.map(name => ({name,grade,label:'★'})) : draw.candidates;
  const cells = makeRing(candidates.length ? candidates : [{name:entry ? draw.winner.nickname : draw.prize.name,grade}]);
  const name = entry ? draw.winner.nickname : draw.prize.name;
  const winnerIndex = Math.max(0, cells.findIndex(candidate => candidate.name === name));
  summon.style.setProperty('--stop', `${-720 - winnerIndex * 36}deg`);
  summon.style.setProperty('--elapsed', `${-elapsed / 1000}s`);
  byId('rank').textContent = ranks[grade];
  byId('grade').textContent = entry ? draw.award.name : grades[grade];
  byId('stars').textContent = '★'.repeat(grade === 'Grand' ? 5 : grade === 'Second' ? 4 : 3);
  byId('prize').textContent = name;
  byId('viewer').textContent = entry ? draw.award.prize : draw.nickname || (draw.uid ? `UID ${draw.uid}` : '');
  byId('banner').textContent = entry ? `正在召唤 · ${draw.award.name}` : '星辰召唤';
  byId('shout').textContent = entry ? '★ 恭喜召唤成功 ★' : '★ 大奖降临 ★';
  fitting = [[byId('prize'),112],[byId('viewer'),58]];
  // Flush the previous draw's animation before applying the same grade again.
  void summon.offsetWidth;
  summon.className = `drawing ${grade}`;
  fitAll();
  particles(Date.parse(draw.drawnAt), grade);
}
function showWinners(lottery) {
  view('winners');
  byId('winnerTitle').textContent = lottery.phase === 'Ended' ? '召唤结果' : '目前的召唤结果';
  byId('remaining').textContent = lottery.phase === 'Ended' ? '抽奖已结束' : `报名已截止 · 还剩 ${lottery.remaining} 个名额`;
  const winners = lottery.awards.flatMap((_, index) => lottery.draws.filter(draw => draw.awardIndex === index));
  if (!winners.length) {
    byId('winnerCards').replaceChildren(); byId('pageNumber').textContent = '等待开奖'; return;
  }
  let page = 0;
  const count = Math.ceil(winners.length / 6);
  const render = () => {
    fitting = [];
    byId('winnerCards').replaceChildren(...winners.slice(page * 6,page * 6 + 6).map(draw => {
      const card = text(`winnerCard ${draw.award.grade}`, '');
      const name = text('winnerName',draw.winner.nickname);
      card.append(text('rank',ranks[draw.award.grade]),name,text('winnerAward',draw.award.name),text('winnerPrize',draw.award.prize));
      fitting.push([name,72]);
      return card;
    }));
    byId('pageNumber').textContent = count > 1 ? `${page + 1} / ${count}` : '';
    fitAll(); page = (page + 1) % count;
  };
  render(); if (count > 1) paging = setInterval(render,5000);
}
function particles(start, grade) {
  const power = grade === 'Grand' ? 3 : grade === 'Second' ? 2 : 1;
  const colors = grade === 'Grand' ? ['#fff','#fff1c2','#ffd36b','#e8a93a']
    : grade === 'Second' ? ['#fff','#e3d2ff','#c59bff'] : ['#fff','#cdebff','#7cc4ff'];
  const stars = Array.from({length:power * 50},() => ({x:Math.random()*1920,y:Math.random()*1080,r:1+Math.random()*2,phase:Math.random()*6.28}));
  let motes = [], fired = start !== null && Date.now() - start >= 2800, previous = Date.now();
  const add = (x,y,vx,vy,life) => motes.push({x,y,vx,vy,life,max:life,r:2+Math.random()*power*2,color:colors[Math.floor(Math.random()*colors.length)]});
  function paint() {
    const now = Date.now(), age = start === null ? 0 : now-start;
    const dt = Math.min(3,(now-previous)/16.667); previous=now;
    context.clearRect(0,0,1920,1080); context.globalCompositeOperation='lighter';
    if (start !== null) {
      for (const star of stars) {
        context.globalAlpha=.25+.65*Math.abs(Math.sin(star.phase+now*.0016)); context.fillStyle='#fff';
        context.fillRect(star.x,star.y,star.r,star.r);
      }
      if (!fired && age >= 2000) {
        fired=true;
        for(let i=0;i<(power===3?420:power===2?220:50);i++){
          const angle=Math.random()*Math.PI*2, speed=3+Math.random()*(8+7*power);
          add(960,520,Math.cos(angle)*speed,Math.sin(angle)*speed*.7-3,90+Math.random()*130);
        }
      }
    }
    if (start === null || age > 2500 && power > 1) {
      if(Math.random()<.35*dt) add(560+Math.random()*800,900,(Math.random()-.5)*.8,-1.5-Math.random()*2.5,180);
    }
    for(const p of motes){
      p.x+=p.vx*dt;p.y+=p.vy*dt;p.life-=dt;
      p.vx*=Math.pow(.98,dt);
      context.fillStyle=p.color;context.globalAlpha=Math.max(0,Math.min(1,p.life/60));
      context.beginPath();
      for(let k=0;k<8;k++){
        const angle=k*Math.PI/4,r=k%2===0?p.r:p.r*.32;
        context.lineTo(p.x+Math.cos(angle)*r,p.y+Math.sin(angle)*r);
      }
      context.closePath();context.fill();
    }
    motes=motes.filter(p=>p.life>0);
    context.globalAlpha=1;context.globalCompositeOperation='source-over';
    frame=requestAnimationFrame(paint);
  }
  frame=requestAnimationFrame(paint);
}
const events = new EventSource('/lottery/events');
events.addEventListener('state',event => show(JSON.parse(event.data)));
events.onerror = () => { shown=''; clear(); };
