'use strict';
const byId = id => document.getElementById(id);
const scene = byId('scene'), summon = byId('summon'), ring = byId('ring');
const grades = { Grand:'大奖', Second:'二奖', Third:'三奖' };
// Each theme's wording, sizes as [landscape, portrait] and particles; the views below are shared.
const themes = {
  StarSummon:{
    ranks:{ Grand:'SSR', Second:'SR', Third:'R' },
    labels:{ themeTitle:'星辰召唤', registrationPhase:'报名中', joinLabel:'召唤条件', countLabel:'位召唤师已报名', latestLabel:'刚刚加入', awardsTitle:'奖品' },
    sizes:{ keyword:[76,58], entryCount:[130,116], rank:[84,76], prize:[112,96], viewer:[58,50], winnerName:[72,48] },
    latest:item => text('',item.nickname),
    cell:candidate => candidate.label || grades[candidate.grade],
    rank:(code, title) => [title],
    viewer:({entry, prize, viewer}) => entry ? prize : viewer,
    banner:({entry, title, viewer}) => entry ? `正在召唤 · ${title}` : viewer ? `${viewer} 发起召唤！` : '星辰召唤',
    winners:['召唤结果','目前的召唤结果'],
    award:award => award.name,
    particles:starSummon,
  },
  MechaLock:{
    ranks:{ Grand:'S', Second:'A', Third:'B' },
    labels:{ themeTitle:'目标锁定', registrationPhase:'STANDBY · 报名中', joinLabel:'HOW TO JOIN · 报名方式', countLabel:'人已报名', latestLabel:'NEW PILOT · 刚刚加入', awardsTitle:'作战奖励' },
    sizes:{ keyword:[76,56], entryCount:[136,120], rank:[60,46], prize:[150,116], viewer:[58,46], winnerName:[70,50] },
    latest:(item, index, entry) => {
      const row = text('','');
      row.append(text('pilotCode',`P-${String(entry.entryCount - index).padStart(3,'0')}`),text('pilotName',item.nickname),text('pilotStatus','已同步'));
      return row;
    },
    cell:(candidate, index) => candidate.viewer ? `PILOT ${String(index + 1).padStart(3,'0')}` : `${themes.MechaLock.ranks[candidate.grade]} · ${grades[candidate.grade]}`,
    rank:(code, title, entry) => [text('rankCode',`${code} 级`), entry ? `${title} · 目标确认` : title],
    viewer:({entry, prize, viewer}) => entry ? `获得 ${prize}` : viewer && `${viewer} 获得`,
    banner:({entry, title, viewer}) => entry ? `锁定 ${title} 中奖者` : viewer ? `${viewer} 发出锁定指令` : '机甲锁定',
    winners:['作战结果','目前战果'],
    award:award => `${award.name} · ${award.prize}`,
    particles:mechaLock,
    scan,
  },
  CapsuleGacha:{
    ranks:{ Grand:'', Second:'', Third:'' },
    labels:{ themeTitle:'扭蛋抽奖', registrationPhase:'报名中', joinLabel:'扭蛋抽奖', countLabel:'颗扭蛋', latestLabel:'刚刚加入', awardsTitle:'奖品' },
    sizes:{ keyword:[72,50], entryCount:[120,92], rank:[56,46], prize:[120,100], viewer:[56,44], winnerName:[62,46] },
    latest:item => text('',item.nickname),
    cell:candidate => grades[candidate.grade],
    cells:candidates => Object.keys(grades).flatMap(grade => {
      const names = candidates.filter(c => c.grade === grade).map(c => c.name);
      return names.length ? [{grade,name:[...new Set(names)].join('、')}] : [];
    }),
    rank:(code, title) => [title],
    viewer:({entry, prize, viewer}) => entry ? `抽中 ${prize}` : viewer && `${viewer} 扭到了`,
    banner:({entry, title, viewer}) => entry ? `扭出 ${title} 中奖者！` : viewer ? `${viewer} 来扭蛋啦！` : '来扭蛋啦！',
    shout:grade => ({Grand:'大奖!!',Second:'POP!',Third:'pop'})[grade],
    winners:['中奖啦！','已经扭出来的'],
    award:award => `${award.name} · ${award.prize}`,
    registration:entry => {
      byId('entryCounter').append(byId('gachaMachine'));
      fillCapsules(entry.entryCount, capsuleEntry?.id === entry.id ? capsuleEntry.count : entry.entryCount);
      capsuleEntry = {id:entry.id,count:entry.entryCount};
      byId('machineLabel').textContent = '报名中';
    },
    stage:() => {
      summon.append(byId('gachaMachine')); capsuleEntry = null;
      fillCapsules(18); byId('machineLabel').textContent = '等你来扭';
    },
    composite:'source-over',
    particles:capsuleGacha,
  },
};
const theme = () => themes[scene.dataset.theme] || themes.StarSummon;
const starIcon = '<svg width="38" height="38" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2l2.4 7.2H22l-6.2 4.5 2.4 7.3-6.2-4.5-6.2 4.5 2.4-7.3L2 9.2h7.6z"/></svg>';
let shown = '', hiding, paging, scanning, frame, fitting = [], portrait = false;
let capsuleEntry;
const canvas = byId('particles'), context = canvas.getContext('2d');
function resize() {
  portrait = window.innerWidth < window.innerHeight;
  const [width, height] = portrait ? [1080, 1920] : [1920, 1080];
  scene.dataset.orientation = portrait ? 'portrait' : 'landscape';
  scene.style.transform = `scale(${Math.min(window.innerWidth / width, window.innerHeight / height)}) translate(-50%,-50%)`;
  if (canvas.width !== width) { canvas.width = width; canvas.height = height; }
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
  const height = parseFloat(getComputedStyle(node).maxHeight) || Infinity;
  const sticker = scene.dataset.theme === 'CapsuleGacha' && (node === byId('prize') || node === byId('viewer'));
  while (maximum > (portrait ? 40 : 56) && (node.scrollHeight > (sticker ? Math.min(height, node.clientHeight) : height + 1) || node.scrollWidth > node.clientWidth + 1))
    node.style.fontSize = --maximum + 'px';
}
function fitAll() {
  // Start all flex siblings at full size before shrinking; growing one later would squeeze the others.
  fitting.forEach(([node, size, portraitSize]) => node.style.fontSize = (portrait ? portraitSize : size) + 'px');
  fitting.forEach(([node, size, portraitSize]) => fit(node, portrait ? portraitSize : size));
}
document.fonts.ready.then(fitAll);
document.fonts.addEventListener('loadingdone', fitAll);
function clear() {
  clearTimeout(hiding); clearInterval(paging); clearInterval(scanning); cancelAnimationFrame(frame);
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
  const key = lottery ? [state.theme,lottery.id,lottery.phase,lottery.draws.length,lottery.draws.at(-1)?.id,
    lottery.drawUntil,lottery.entryCount,lottery.hidden].join(' ') : '';
  if (key === shown) return;
  shown = key; clear();
  if (!lottery || entry?.hidden || entry?.phase === 'Preparing' || pool?.phase === 'Idle') return;
  if (entry?.phase === 'Open') {
    view('registration');
    const t = theme();
    for (const [id, value] of Object.entries(t.labels)) byId(id).textContent = value;
    byId('keyword').textContent = `发弹幕「${entry.keyword}」`;
    const count = byId('entryCount');
    count.textContent = entry.entryCount;
    // Restart the board's pop on every new entry; the newest name slides in by CSS.
    count.className = 'gold'; void count.offsetWidth; count.className = 'gold pop';
    byId('registration').dataset.awards = entry.awards.length;
    byId('latestEntries').replaceChildren(...entry.latestEntries.slice(0,3).map((item,index) => t.latest(item,index,entry)));
    byId('awardRows').replaceChildren(...entry.awards.map(award => {
      const row = text(`awardRow ${award.grade}`, '');
      row.append(text('rank',t.ranks[award.grade]),text('awardName',award.name),text('awardPrize',award.prize),text('capacity',`×${award.capacity}`));
      return row;
    }));
    fitting = [[byId('keyword'),...t.sizes.keyword],[byId('entryCount'),...t.sizes.entryCount]]; fitAll();
    t.registration?.(entry);
    return;
  }
  if (pool?.phase === 'Spinning') {
    view('spinning'); summon.className = 'spinning';
    byId('banner').textContent = '';
    makeRing(pool.prizes);
    theme().scan?.(Date.now());
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
  theme().stage?.();
  // Ten visual cells fill the stage; Core has already chosen the result.
  const cells = theme().cells?.(candidates) || Array.from({length:10},(_,index) => candidates[index % candidates.length]);
  ring.replaceChildren(...cells.map((candidate,index) => {
    const card = text(`candidate ${candidate.grade}${candidate.viewer ? ' viewer' : ''}`, '');
    card.style.transform = `rotateY(${index * 36}deg) translateZ(var(--ring-radius))`;
    card.append(text('candidateLabel',theme().cell(candidate,index)),text('candidateName',candidate.name));
    return card;
  }));
  return cells;
}
// Board W2's lock frame: hops a cell every 0.4 s while spinning; on a draw it slows down and locks the winner at the 28% hit.
function scan(start, winner) {
  const path = winner === undefined ? [0,6,3,8,1,5,9,2,7,4] : [5,0,8,3,6,1,9,4,7,2];
  const update = () => {
    const age = Math.max(0,Date.now() - start), locked = winner !== undefined && age >= 1960;
    const step = winner === undefined ? Math.floor(age / 400) : Math.floor(Math.sqrt(Math.min(1,age / 1960)) * 22);
    const active = locked ? winner : path[step % path.length];
    Array.from(ring.children).forEach((card,index) => card.dataset.lock = index === active ? locked ? 'LOCK ON' : 'SCANNING' : '');
    if (locked) clearInterval(scanning);
  };
  scanning = setInterval(update,50); update();
}
function drawResult(draw, entry) {
  const grade = entry ? draw.award.grade : draw.prize.grade;
  const elapsed = Math.max(0, Date.now() - Date.parse(draw.drawnAt));
  view('drawing');
  summon.className = grade;
  const candidates = entry ? draw.candidates.map(name => ({name,grade,label:'★',viewer:true})) : draw.candidates;
  const cells = makeRing(candidates.length ? candidates : [{name:entry ? draw.winner.nickname : draw.prize.name,grade}]);
  const name = entry ? draw.winner.nickname : draw.prize.name;
  const winnerIndex = Math.max(0, cells.findIndex(candidate => candidate.name === name && candidate.grade === grade));
  summon.style.setProperty('--stop', `${-720 - winnerIndex * 36}deg`);
  summon.style.setProperty('--elapsed', `${-elapsed / 1000}s`);
  const t = theme(), words = {entry, title:entry ? draw.award.name : grades[grade], prize:entry ? draw.award.prize : '',
    viewer:draw.nickname || (draw.uid ? `UID ${draw.uid}` : '')};
  byId('rank').replaceChildren(...t.rank(t.ranks[grade], words.title, entry));
  byId('stars').innerHTML = starIcon.repeat(grade === 'Grand' ? 5 : grade === 'Second' ? 4 : 3);
  byId('prize').textContent = name;
  // The glitch copies of the name are drawn from this attribute.
  byId('prize').dataset.text = name;
  byId('viewer').textContent = t.viewer(words);
  byId('banner').textContent = t.banner(words);
  byId('warning').textContent = grade === 'Grand' ? 'WARNING · S 级奖励出现' : 'ALERT · 锁定程序启动';
  byId('shout').textContent = t.shout?.(grade) || (entry ? '★ 恭喜召唤成功 ★' : '★ 大奖降临 ★');
  byId('machineLabel').textContent = '扭扭扭…';
  fitting = [[byId('rank'),...t.sizes.rank],[byId('prize'),...t.sizes.prize],[byId('viewer'),...t.sizes.viewer]];
  // Flush the previous draw's animation before applying the same grade again.
  void summon.offsetWidth;
  summon.className = `drawing ${grade}`;
  fitAll();
  t.scan?.(Date.parse(draw.drawnAt), winnerIndex);
  particles(Date.parse(draw.drawnAt), grade);
}
function showWinners(lottery) {
  view('winners');
  const t = theme();
  byId('winnerTitle').textContent = t.winners[lottery.phase === 'Ended' ? 0 : 1];
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
      card.style.setProperty('--index', index);
      card.append(text('rank',t.ranks[draw.award.grade]),name,text('winnerAward',t.award(draw.award)));
      fitting.push([name,...t.sizes.winnerName]);
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
function hex(x, y, r) {
  for (let k = 0; k < 6; k++) { const angle = Math.PI / 6 + k * Math.PI / 3; context.lineTo(x + Math.cos(angle) * r, y + Math.sin(angle) * r); }
  context.closePath(); context.stroke();
}
// Canvas geometry of boards W1 and PW1: burst centre, idle motes and rising sparkles (left, spread, y), star field height, meteor path and burst spread.
const stages = {
  landscape:{cx:960,cy:520,idle:[660,600,880],rise:[560,800,900],skyHeight:820,meteor:{dx:840,y:820,dy:860},spread:[1,.7]},
  portrait:{cx:459,cy:760,idle:[199,520,1140],rise:[159,600,1180],skyHeight:1500,meteor:{dx:600,y:1100,dy:1100},spread:[.8,1]},
};
// Moves and paints the motes; the theme adds them on the draw timeline and may paint a backdrop first.
function particles(start, grade) {
  const spawn = theme().particles(start, grade);
  let motes = [], previous = Date.now();
  const add = mote => motes.push({drag:1,grav:0,max:mote.life,...mote});
  function paint() {
    const now = Date.now(), dt = Math.min(3,(now - previous) / 16.667); previous = now;
    context.clearRect(0,0,canvas.width,canvas.height); context.globalCompositeOperation = theme().composite || 'lighter';
    spawn(now, dt, add);
    for (const p of motes) {
      const drag = p.drag ** dt;
      p.vx *= drag; p.vy = p.vy * drag + p.grav * dt; p.x += p.vx * dt; p.y += p.vy * dt; p.life -= dt;
      const alpha = Math.max(0, Math.min(1, p.life / (p.max * .4))) * (p.twinkle ? .5 + .5 * Math.sin(p.life * .3) : 1);
      context.globalAlpha = alpha; context.fillStyle = context.strokeStyle = p.c; context.beginPath();
      if (p.kind === 'star') star(p.x,p.y,p.s * 2.2);
      else if (p.kind === 'hex') { context.lineWidth = 2; hex(p.x,p.y,p.s); }
      else if (p.kind === 'spark') { context.lineWidth = p.s; context.moveTo(p.x,p.y); context.lineTo(p.x - p.vx * 2.6,p.y - p.vy * 2.6); context.stroke(); }
      else if (p.kind === 'ember') { context.arc(p.x,p.y,p.s,0,6.283); context.fill(); }
      else if (p.kind === 'heart') {
        context.moveTo(p.x,p.y+p.s*.9);
        context.bezierCurveTo(p.x-p.s*1.6,p.y-p.s*.2,p.x-p.s*.7,p.y-p.s*1.4,p.x,p.y-p.s*.5);
        context.bezierCurveTo(p.x+p.s*.7,p.y-p.s*1.4,p.x+p.s*1.6,p.y-p.s*.2,p.x,p.y+p.s*.9);
        context.fill(); context.strokeStyle = '#4b2a4f'; context.lineWidth = 2; context.stroke();
      }
      else if (p.kind === 'confetti') {
        context.save(); context.translate(p.x,p.y); context.rotate(p.life*.07);
        context.fillRect(-p.s,-p.s*.6,p.s*2,p.s*1.2); context.restore();
      }
      else {
        context.globalAlpha = alpha * .2; context.arc(p.x,p.y,p.s * 3.5,0,6.283); context.fill();
        context.globalAlpha = alpha; context.beginPath(); context.arc(p.x,p.y,p.s,0,6.283); context.fill();
      }
    }
    motes = motes.filter(p => p.life > 0);
    context.globalAlpha = 1; context.globalCompositeOperation = 'source-over';
    frame = requestAnimationFrame(paint);
  }
  frame = requestAnimationFrame(paint);
}
// Board W1's canvas on this page's timeline: spinning motes, or sky stars, meteor trail, the burst at the 28% hit and rising sparkles.
function starSummon(start, grade) {
  const power = grade === 'Grand' ? 3 : grade === 'Second' ? 2 : 1;
  const colors = start === null ? idleColors : burstColors[grade];
  const sky = start === null ? [] : Array.from({length:160},() => ({x:Math.random(),y:Math.random(),s:.6+Math.random()*1.8,p:Math.random()*6.28}));
  const pick = () => colors[Math.floor(Math.random()*colors.length)];
  let fired = start !== null && Date.now() - start >= 2600;
  return (now, dt, add) => {
    const age = start === null ? 0 : now - start;
    const {cx,cy,idle,rise,skyHeight,meteor,spread} = stages[scene.dataset.orientation];
    if (start === null) {
      if (Math.random() < .35 * dt) add({x:idle[0]+Math.random()*idle[1],y:idle[2],vx:(Math.random()-.5)*.6,vy:-1.2-Math.random()*1.6,life:220,s:2+Math.random()*3,c:pick(),kind:'star',twinkle:true});
      return;
    }
    const fade = Math.min(1, age / 280);
    context.fillStyle = '#fff';
    for (const s of sky) { context.globalAlpha = fade * (.35 + .65 * Math.abs(Math.sin(s.p + now * .0016))); context.fillRect(s.x*canvas.width,s.y*skyHeight,s.s,s.s); }
    if (power > 1 && age > 1540 && age < 1960) {
      const eased = 1 - (1 - (age - 1540) / 420) ** 2, x = cx + meteor.dx * (1 - eased), y = meteor.y - meteor.dy * (1 - eased);
      for (let j = 0; j < 6 * dt; j++) add({x,y,vx:(Math.random()-.3)*3,vy:(Math.random()-.7)*3,life:40,s:1.5+Math.random()*3,c:colors[j%colors.length],drag:.96,grav:.02});
    }
    if (!fired && age >= 1960) {
      fired = true;
      for (let j = 0; j < 90 + 165 * (power - 1); j++) {
        const angle = Math.random() * Math.PI * 2, speed = 3 + Math.random() * (8 + 7 * power);
        add({x:cx,y:cy,vx:Math.cos(angle)*speed*spread[0],vy:Math.sin(angle)*speed*spread[1]-3,life:70+Math.random()*90,max:160,s:2+Math.random()*(2+power),c:colors[j%colors.length],drag:.95,grav:.06,kind:j%4===0 ? 'star' : 'dot'});
      }
    }
    if (power > 1 && age > 2300 && age < 6500 && Math.random() < .3 * power * dt)
      add({x:rise[0]+Math.random()*rise[1],y:rise[2],vx:(Math.random()-.5)*.8,vy:-1.5-Math.random()*2.5,life:200,s:3+Math.random()*5,c:pick(),kind:'star',twinkle:true});
  };
}
// Board W2's canvas, shifted from its 35% lock to this page's 28%: sparks and hex shards sized by grade just after the lock, then embers rising from the bottom edge for the two higher grades.
function mechaLock(start, grade) {
  if (start === null) return () => {};
  const power = grade === 'Grand' ? 3 : grade === 'Second' ? 2 : 1;
  const colors = grade === 'Grand' ? ['#ff3d3d','#ff7a1a','#ffb067','#fff'] : grade === 'Second' ? ['#ff7a1a','#ffb067','#fff'] : ['#8fd3ff','#fff'];
  let fired = Date.now() - start >= 2800;
  return (now, dt, add) => {
    const age = now - start, [cx,cy,left,width,bottom] = portrait ? [459,760,54,810,1930] : [960,560,200,1520,1090];
    if (!fired && age >= 2090) {
      fired = true;
      for (let k = 0; k < [60,180,380][power - 1]; k++) {
        const angle = Math.random() * Math.PI * 2, speed = 5 + Math.random() * (8 + 7 * power);
        add({x:cx,y:cy,vx:Math.cos(angle)*speed,vy:Math.sin(angle)*speed*.7,life:50+Math.random()*70,max:120,s:1.5+Math.random()*3,c:colors[k%colors.length],drag:.93,grav:.18,kind:'spark'});
      }
      for (let k = 0; k < [6,20,46][power - 1]; k++) {
        const angle = Math.random() * Math.PI * 2, speed = 4 + Math.random() * 12;
        add({x:cx,y:cy,vx:Math.cos(angle)*speed,vy:Math.sin(angle)*speed,life:90+Math.random()*50,max:140,s:10+Math.random()*26,c:colors[k%colors.length],drag:.96,kind:'hex'});
      }
    }
    if (power > 1 && age > 2410 && age < 5510 && Math.random() < .4 * power * dt)
      add({x:left+Math.random()*width,y:bottom,vx:(Math.random()-.5)*.8,vy:-2-Math.random()*3,life:220,s:1.5+Math.random()*2.5,c:colors[0],kind:'ember',twinkle:true});
  };
}
// W3/PW3's dome has 18 visible slots; new entries keep falling after it fills, while the counter shows the full total.
function fillCapsules(count, previous = count) {
  const spots = [[52,268],[112,272],[172,270],[232,268],[292,262],[26,214],[82,216],[140,212],[198,214],[256,210],[312,206],[58,160],[116,158],[174,162],[232,156],[100,106],[160,104],[222,108]];
  const colors = ['#5fd3b0','#5fd3b0','#ff7aae','#5fd3b0','#ffd45e'];
  const visible = Math.min(count,spots.length);
  byId('capsules').replaceChildren(...spots.slice(0,visible).map(([x,y],i) => {
    const number = count - visible + i, ball = text(`capsule${number >= previous ? ' falling' : ''}`,'');
    ball.style.left = x + 'px'; ball.style.top = y + 'px';
    ball.style.setProperty('--cap',colors[number % colors.length]);
    ball.style.setProperty('--tilt',`${number * 47 % 90 - 45}deg`);
    return ball;
  }));
}
function capsuleGacha(start, grade) {
  if (start === null) return () => {};
  const power = grade === 'Grand' ? 3 : grade === 'Second' ? 2 : 1;
  const colors = power === 3 ? ['#ff7aae','#ffd45e','#5fd3b0','#a991ff','#7ccbff','#fff']
    : power === 2 ? ['#ff7aae','#ffb3cf','#fff','#ffd45e'] : ['#5fd3b0','#bff0df','#fff'];
  let fired = Date.now() - start >= 2800;
  return (now, dt, add) => {
    const age = now - start, [cx,cy] = portrait ? [459,600] : [1180,520];
    if (!fired && age >= 1960) {
      fired = true;
      for (let i = 0; i < [30,70,140][power-1]; i++) {
        const angle = Math.random()*Math.PI*2, speed = 5+Math.random()*(6+5*power);
        add({x:cx,y:cy,vx:Math.cos(angle)*speed,vy:Math.sin(angle)*speed-4,life:120+Math.random()*60,s:6+Math.random()*8,c:colors[i%colors.length],drag:.97,grav:.32,kind:power > 1 && i%3===0 ? 'heart' : 'confetti'});
      }
    }
    if (power > 1 && age > 2600 && age < 6200 && Math.random() < .12*power*dt)
      add({x:cx-250+Math.random()*500,y:portrait?1000:760,vx:0,vy:-1.4-Math.random()*1.2,life:180,s:9+Math.random()*8,c:colors[0],kind:'heart'});
    if (power === 3 && age > 2100 && age < 5600)
      for (let i = 0; i < 3*dt; i++) add({x:Math.random()*canvas.width,y:-20,vx:0,vy:5+Math.random()*4,life:400,s:8,c:colors[i%colors.length],kind:'confetti'});
  };
}
const events = new EventSource('/lottery/events');
events.addEventListener('state',event => show(JSON.parse(event.data)));
events.onerror = () => { shown=''; clear(); };
