'use strict';
const byId = id => document.getElementById(id);
const scene = byId('scene'), summon = byId('summon'), ring = byId('ring');
const grades = { Grand:'大奖', Second:'二奖', Third:'三奖' };
const ranks = { Grand:'SSR', Second:'SR', Third:'R' };
const starIcon = '<svg width="38" height="38" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2l2.4 7.2H22l-6.2 4.5 2.4 7.3-6.2-4.5-6.2 4.5 2.4-7.3L2 9.2h7.6z"/></svg>';
let shown = '', hiding, paging, frame, fitting = [], portrait = false;
const canvas = byId('particles'), context = canvas.getContext('2d');
function resize() {
  portrait = window.innerWidth < window.innerHeight;
  scene.dataset.orientation = portrait ? 'portrait' : 'landscape';
  const scale = Math.min(window.innerWidth / (portrait ? 1080 : 1920), window.innerHeight / (portrait ? 1920 : 1080));
  scene.style.transform = `scale(${scale}) translate(-50%,-50%)`;
  const width = portrait ? 1080 : 1920, height = portrait ? 1920 : 1080;
  if (canvas.width !== width || canvas.height !== height) { canvas.width = width; canvas.height = height; }
  fitAll();
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
  while (maximum > (portrait ? 40 : 56) && (node.scrollHeight > height + 1 || node.scrollWidth > node.clientWidth + 1))
    node.style.fontSize = --maximum + 'px';
}
function fitAll() { fitting.forEach(([node, size, portraitSize]) => fit(node, portrait ? portraitSize : size)); }
document.fonts.ready.then(fitAll);
document.fonts.addEventListener('loadingdone', fitAll);
function clear() {
  clearTimeout(hiding); clearInterval(paging); cancelAnimationFrame(frame);
  scene.hidden = true; summon.className = ''; fitting = [];
  for (const id of ['registration','summon','winners']) byId(id).hidden = true;
  context.clearRect(0, 0, canvas.width, canvas.height);
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
    const count = byId('entryCount');
    count.textContent = entry.entryCount;
    // Restart the board's pop on every new entry; the newest name slides in by CSS.
    count.className = 'gold'; void count.offsetWidth; count.className = 'gold pop';
    byId('latestEntries').replaceChildren(...entry.latestEntries.slice(0,3).map(item => text('',item.nickname)));
    byId('awardRows').replaceChildren(...entry.awards.map(award => {
      const row = text(`awardRow ${award.grade}`, '');
      row.append(text('rank',ranks[award.grade]),text('awardName',award.name),text('awardPrize',award.prize),text('capacity',`×${award.capacity}`));
      return row;
    }));
    fitting = [[byId('keyword'),76,58],[byId('entryCount'),130,116]]; fitAll();
    return;
  }
  if (pool?.phase === 'Spinning') {
    view('spinning'); summon.className = 'spinning';
    byId('banner').textContent = '';
    makeRing(pool.prizes);
    particles(null);
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
    const card = text(`candidate ${candidate.grade}${candidate.viewer ? ' viewer' : ''}`, '');
    card.style.transform = `rotateY(${index * 36}deg) translateZ(var(--ring-radius))`;
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
  const candidates = entry ? draw.candidates.map(name => ({name,grade,label:'★',viewer:true})) : draw.candidates;
  const cells = makeRing(candidates.length ? candidates : [{name:entry ? draw.winner.nickname : draw.prize.name,grade}]);
  const name = entry ? draw.winner.nickname : draw.prize.name;
  const winnerIndex = Math.max(0, cells.findIndex(candidate => candidate.name === name));
  summon.style.setProperty('--stop', `${-720 - winnerIndex * 36}deg`);
  summon.style.setProperty('--elapsed', `${-elapsed / 1000}s`);
  const viewer = draw.nickname || (draw.uid ? `UID ${draw.uid}` : '');
  byId('rank').textContent = entry ? draw.award.name : grades[grade];
  byId('stars').innerHTML = starIcon.repeat(grade === 'Grand' ? 5 : grade === 'Second' ? 4 : 3);
  byId('prize').textContent = name;
  byId('viewer').textContent = entry ? draw.award.prize : viewer;
  byId('banner').textContent = entry ? `正在召唤 · ${draw.award.name}` : viewer ? `${viewer} 发起召唤！` : '星辰召唤';
  byId('shout').textContent = entry ? '★ 恭喜召唤成功 ★' : '★ 大奖降临 ★';
  fitting = [[byId('rank'),84,76],[byId('prize'),112,96],[byId('viewer'),58,50]];
  // Flush the previous draw's animation before applying the same grade again.
  void summon.offsetWidth;
  summon.className = `drawing ${grade}`;
  fitAll();
  particles(Date.parse(draw.drawnAt), grade);
}
function showWinners(lottery) {
  view('winners');
  byId('winnerTitle').textContent = lottery.phase === 'Ended' ? '召唤结果' : '目前的召唤结果';
  byId('remaining').hidden = lottery.phase === 'Ended';
  byId('remaining').textContent = `报名已截止 · 还剩 ${lottery.remaining} 个名额`;
  const winners = lottery.awards.flatMap((_, index) => lottery.draws.filter(draw => draw.awardIndex === index));
  if (!winners.length) {
    byId('winnerCards').replaceChildren();
    byId('pageNumber').textContent = lottery.phase === 'Ended' ? '本场没有开奖' : '等待开奖'; return;
  }
  let page = 0;
  const count = Math.ceil(winners.length / 6);
  const render = () => {
    fitting = [];
    byId('winnerCards').replaceChildren(...winners.slice(page * 6,page * 6 + 6).map((draw,index) => {
      const card = text(`winnerCard ${draw.award.grade}`, '');
      const name = text('winnerName',draw.winner.nickname);
      card.style.animationDelay = `${index * .14}s`;
      card.append(text('rank',ranks[draw.award.grade]),name,text('winnerAward',draw.award.name));
      fitting.push([name,72,48]);
      return card;
    }));
    byId('pageNumber').textContent = count > 1 ? `${page + 1} / ${count}` : '';
    fitAll(); page = (page + 1) % count;
  };
  render(); if (count > 1) paging = setInterval(render,5000);
}
const idleColors = ['#ffe7a8','#c59bff','#7cc4ff'];
const burstColors = { Grand:['#fff','#fff1c2','#ffd36b','#e8a93a'], Second:['#fff','#e3d2ff','#c59bff','#9f7bff'], Third:['#fff','#cdebff','#7cc4ff'] };
function star(x, y, r) {
  context.beginPath();
  for (let k = 0; k < 8; k++) {
    const angle = k * Math.PI / 4, radius = k % 2 === 0 ? r : r * .32;
    context.lineTo(x + Math.cos(angle) * radius, y + Math.sin(angle) * radius);
  }
  context.closePath(); context.fill();
}
// Board W1's canvas on this page's timeline: spinning motes, or sky stars, meteor trail, the burst at the 28% hit and rising sparkles.
function particles(start, grade) {
  const power = grade === 'Grand' ? 3 : grade === 'Second' ? 2 : 1;
  const colors = start === null ? idleColors : burstColors[grade];
  const sky = start === null ? [] : Array.from({length:160},() => ({x:Math.random(),y:Math.random(),s:.6+Math.random()*1.8,p:Math.random()*6.28}));
  let motes = [], fired = start !== null && Date.now() - start >= 2600, previous = Date.now();
  const pick = () => colors[Math.floor(Math.random()*colors.length)];
  const add = mote => motes.push({drag:1,grav:0,max:mote.life,...mote});
  function paint() {
    const now = Date.now(), age = start === null ? 0 : now - start;
    const cx = portrait ? 459 : 960, cy = portrait ? 760 : 520;
    const dt = Math.min(3,(now - previous) / 16.667); previous = now;
    context.clearRect(0,0,canvas.width,canvas.height); context.globalCompositeOperation = 'lighter';
    if (start === null) {
      if (Math.random() < .35 * dt) add({x:cx-(portrait?260:300)+Math.random()*(portrait?520:600),y:portrait?1140:880,vx:(Math.random()-.5)*.6,vy:-1.2-Math.random()*1.6,life:220,s:2+Math.random()*3,c:pick(),star:true,twinkle:true});
    } else {
      const fade = Math.min(1, age / 280);
      context.fillStyle = '#fff';
      for (const s of sky) { context.globalAlpha = fade * (.35 + .65 * Math.abs(Math.sin(s.p + now * .0016))); context.fillRect(s.x*canvas.width,s.y*(portrait?1500:820),s.s,s.s); }
      if (power > 1 && age > 1540 && age < 1960) {
        const eased = 1 - (1 - (age - 1540) / 420) ** 2, x = cx + (portrait?600:840) * (1 - eased), y = portrait ? 1100*eased : 820 - 860 * (1 - eased);
        for (let j = 0; j < 6 * dt; j++) add({x,y,vx:(Math.random()-.3)*3,vy:(Math.random()-.7)*3,life:40,s:1.5+Math.random()*3,c:colors[j%colors.length],drag:.96,grav:.02});
      }
      if (!fired && age >= 1960) {
        fired = true;
        for (let j = 0; j < 90 + 165 * (power - 1); j++) {
          const angle = Math.random() * Math.PI * 2, speed = 3 + Math.random() * (8 + 7 * power);
          add({x:cx,y:cy,vx:Math.cos(angle)*speed*(portrait?.8:1),vy:Math.sin(angle)*speed*(portrait?1:.7)-3,life:70+Math.random()*90,max:160,s:2+Math.random()*(2+power),c:colors[j%colors.length],drag:.95,grav:.06,star:j%4===0});
        }
      }
      if (power > 1 && age > 2300 && age < 6500 && Math.random() < .3 * power * dt)
        add({x:cx-(portrait?300:400)+Math.random()*(portrait?600:800),y:portrait?1180:900,vx:(Math.random()-.5)*.8,vy:-1.5-Math.random()*2.5,life:200,s:3+Math.random()*5,c:pick(),star:true,twinkle:true});
    }
    for (const p of motes) {
      const drag = p.drag ** dt;
      p.vx *= drag; p.vy = p.vy * drag + p.grav * dt; p.x += p.vx * dt; p.y += p.vy * dt; p.life -= dt;
      const alpha = Math.max(0, Math.min(1, p.life / (p.max * .4))) * (p.twinkle ? .5 + .5 * Math.sin(p.life * .3) : 1);
      context.fillStyle = p.c;
      if (p.star) { context.globalAlpha = alpha; star(p.x,p.y,p.s * 2.2); continue; }
      context.globalAlpha = alpha * .2; context.beginPath(); context.arc(p.x,p.y,p.s * 3.5,0,6.283); context.fill();
      context.globalAlpha = alpha; context.beginPath(); context.arc(p.x,p.y,p.s,0,6.283); context.fill();
    }
    motes = motes.filter(p => p.life > 0);
    context.globalAlpha = 1; context.globalCompositeOperation = 'source-over';
    frame = requestAnimationFrame(paint);
  }
  frame = requestAnimationFrame(paint);
}
const events = new EventSource('/lottery/events');
events.addEventListener('state',event => show(JSON.parse(event.data)));
events.onerror = () => { shown=''; clear(); };
