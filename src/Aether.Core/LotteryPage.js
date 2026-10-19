'use strict';
const byId = id => document.getElementById(id);
const scene = byId('scene'), summon = byId('summon'), ring = byId('ring');
const grades = { Grand:'大奖', Second:'二奖', Third:'三奖' };
// How big each grade's effects get; star counts and particle amounts scale with it.
const powers = { Grand:3, Second:2, Third:1 };
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
    // Each entry gets the next capsule colour; the newest tag shares it with the capsule dropping beside the count.
    latest:(item, index, entry) => capsuleColor(text('',item.nickname), entry.entryCount - index),
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
    closed:'报名截止啦',
    // Name tags line up behind one capsule and award per row.
    group:(draws, cards) => {
      const rows = [];
      let list;
      draws.forEach((draw, index) => {
        if (draw.awardIndex !== draws[index - 1]?.awardIndex) {
          const row = text(`winnerGroup ${draw.award.grade}`, ''), award = text('winnerAward', draw.award.name);
          award.append(text('winnerPrize', draw.award.prize));
          list = text('winnerList', '');
          row.append(text('rank', ''), award, list);
          rows.push(row);
        }
        list.append(cards[index]);
      });
      return rows;
    },
    registration:entry => capsuleColor(byId('capsuleDrop'), entry.entryCount),
    composite:'source-over',
    particles:capsuleGacha,
  },
};
const theme = () => themes[scene.dataset.theme] || themes.StarSummon;
const starIcon = '<svg width="38" height="38" viewBox="0 0 24 24" fill="currentColor"><path d="M12 2l2.4 7.2H22l-6.2 4.5 2.4 7.3-6.2-4.5-6.2 4.5 2.4-7.3L2 9.2h7.6z"/></svg>';
let shown = '', hiding, paging, repageWinners, scanning, frame, fitting = [], portrait = false;
const canvas = byId('particles'), context = canvas.getContext('2d');
function resize() {
  const wasPortrait = portrait;
  portrait = window.innerWidth < window.innerHeight;
  const [width, height] = portrait ? [1080, 1920] : [1920, 1080];
  scene.dataset.orientation = portrait ? 'portrait' : 'landscape';
  scene.style.transform = `scale(${Math.min(window.innerWidth / width, window.innerHeight / height)}) translate(-50%,-50%)`;
  if (canvas.width !== width) { canvas.width = width; canvas.height = height; }
  if (wasPortrait !== portrait) repageWinners?.();
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
  while (maximum > (portrait ? 40 : 56) && (node.scrollHeight > height + 1 || node.scrollWidth > node.clientWidth + 1))
    node.style.fontSize = --maximum + 'px';
}
function fitAll() {
  // Start all flex siblings at full size before shrinking; growing one later would squeeze the others.
  fitting.forEach(([node, size, portraitSize]) => node.style.fontSize = (portrait ? portraitSize : size) + 'px');
  fitting.forEach(([node, size, portraitSize]) => fit(node, portrait ? portraitSize : size));
  // Gacha tilts a banner wider than 780px less, so its ends rise no higher than a 780px one's; a hidden banner measures 0 and keeps 1.
  byId('banner').style.setProperty('--tilt-scale', Math.min(1, 780 / byId('banner').offsetWidth));
}
document.fonts.ready.then(fitAll);
document.fonts.addEventListener('loadingdone', fitAll);
function clear() {
  clearTimeout(hiding); clearInterval(paging); clearInterval(scanning); cancelAnimationFrame(frame);
  repageWinners = undefined;
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
  const lottery = state.lottery;
  const entry = lottery?.mode === 'Entry' ? lottery : null, pool = lottery?.mode === 'Pool' ? lottery : null;
  // Repeated state snapshots must not restart a draw, including on a room reconnect.
  const key = lottery ? [state.theme,lottery.id,lottery.phase,lottery.draws.length,lottery.draws.at(-1)?.id,
    lottery.drawUntil,lottery.entryCount,lottery.hidden].join(' ') : '';
  if (key === shown) return;
  shown = key; clear();
  if (!lottery || entry?.hidden || entry?.phase === 'Preparing' || pool?.phase === 'Idle' && !pool.drawUntil) return;
  if (entry?.phase === 'Open') {
    view('registration');
    const t = theme();
    for (const [id, value] of Object.entries(t.labels)) byId(id).textContent = value;
    byId('keyword').textContent = `发弹幕「${entry.keyword}」`;
    byId('entryCount').textContent = entry.entryCount;
    // Restart the board's pop on every new entry; the newest name slides in by CSS.
    const counter = byId('entryCounter');
    counter.className = 'count'; void counter.offsetWidth; counter.className = 'count pop';
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
    hiding = setTimeout(() => show({ ...state, lottery:{...lottery,drawUntil:null} }), remaining);
    return;
  }
  if (entry) showWinners(entry);
}
function makeRing(candidates) {
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
  byId('stars').innerHTML = starIcon.repeat(powers[grade] + 2);
  byId('prize').textContent = name;
  // The glitch copies of the name are drawn from this attribute.
  byId('prize').dataset.text = name;
  byId('viewer').textContent = t.viewer(words);
  byId('banner').textContent = t.banner(words);
  byId('warning').textContent = grade === 'Grand' ? 'WARNING · S 级奖励出现' : 'ALERT · 锁定程序启动';
  byId('shout').textContent = t.shout?.(grade) || (entry ? '★ 恭喜召唤成功 ★' : '★ 大奖降临 ★');
  fitting = [[byId('rank'),...t.sizes.rank],[byId('prize'),...t.sizes.prize],[byId('viewer'),...t.sizes.viewer]];
  // Flush the previous draw's animation before applying the same grade again.
  void summon.offsetWidth;
  summon.className = `drawing ${grade}`;
  fitAll();
  t.scan?.(Date.parse(draw.drawnAt), winnerIndex);
  particles(Date.parse(draw.drawnAt), grade);
}
function showWinners(lottery) {
  clearInterval(paging);
  repageWinners = () => showWinners(lottery);
  view('winners');
  const t = theme();
  byId('winnerTitle').textContent = t.winners[lottery.phase === 'Ended' ? 0 : 1];
  byId('remaining').hidden = lottery.phase === 'Ended';
  byId('remaining').textContent = `${t.closed || '报名已截止'} · 还剩 ${lottery.remaining} 个名额`;
  const winners = lottery.awards.flatMap((_, index) => lottery.draws.filter(draw => draw.awardIndex === index));
  if (!winners.length) {
    byId('winnerCards').replaceChildren();
    byId('pageNumber').textContent = lottery.phase === 'Ended' ? '本场没有开奖' : '等待开奖'; return;
  }
  // Six winners a page. Grouped rows also stop at three awards, and a portrait page gives a name's room to the remaining-places line.
  const limit = t.group && portrait && lottery.phase !== 'Ended' ? 5 : 6, pages = [];
  for (const draw of winners) {
    const last = pages.at(-1);
    if (last?.length < limit && (!t.group || new Set([...last, draw].map(d => d.awardIndex)).size <= 3)) last.push(draw);
    else pages.push([draw]);
  }
  let page = 0;
  const render = () => {
    fitting = [];
    const cards = pages[page].map((draw,index) => {
      const card = text(`winnerCard ${draw.award.grade}`, '');
      const name = text('winnerName',draw.winner.nickname);
      card.style.setProperty('--index', index);
      card.append(...t.group ? [name] : [text('rank',t.ranks[draw.award.grade]),name,text('winnerAward',t.award(draw.award))]);
      fitting.push([name,...t.sizes.winnerName]);
      return card;
    });
    byId('winnerCards').replaceChildren(...t.group?.(pages[page], cards) || cards);
    byId('pageNumber').textContent = pages.length > 1 ? `${page + 1} / ${pages.length}` : '';
    fitAll(); page = (page + 1) % pages.length;
  };
  render(); if (pages.length > 1) paging = setInterval(render,5000);
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
// Canvas geometry of boards W1 and PW1, relative to the summon stage: burst centre, idle motes and rising sparkles (left, spread, y),
// portal stars and gathering streaks (x and y radius), meteor path, star ring speed and burst spread.
const stages = {
  landscape:{cx:960,cy:520,idle:[660,600,880],rise:[560,800,900],sky:[720,440],gather:[760,440],meteor:{dx:600,y:820,dy:600},ring:[16,10],spread:[1,.7]},
  portrait:{cx:459,cy:760,idle:[199,520,1140],rise:[159,600,1180],sky:[440,620],gather:[470,660],meteor:{dx:360,y:1100,dy:700},ring:[11,15],spread:[.8,1]},
};
// Moves and paints the motes; the theme adds them on the draw timeline and may paint a backdrop first.
function particles(start, grade) {
  const spawn = theme().particles(start, grade);
  let motes = [], previous = Date.now();
  const add = mote => motes.push({drag:1,grav:0,max:mote.life,...mote});
  function paint() {
    const now = Date.now(), dt = Math.min(3,(now - previous) / 16.667); previous = now;
    // Motes fade out over 140px before the source's 40px border, as on the boards; portrait shifts the stage inside the source.
    const left = summon.offsetLeft, top = summon.offsetTop;
    context.clearRect(0,0,canvas.width,canvas.height); context.globalCompositeOperation = theme().composite || 'lighter';
    spawn(now, dt, add);
    for (const p of motes) {
      const drag = p.drag ** dt;
      p.vx *= drag; p.vy = p.vy * drag + p.grav * dt; p.x += p.vx * dt; p.y += p.vy * dt; p.life -= dt;
      const edge = Math.min(p.x + left, canvas.width - p.x - left, p.y + top, canvas.height - p.y - top) - 40;
      const alpha = Math.max(0, Math.min(1, p.life / (p.max * .4))) * Math.max(0, Math.min(1, edge / 140)) * (p.twinkle ? .5 + .5 * Math.sin(p.life * .3) : 1);
      context.globalAlpha = alpha; context.fillStyle = context.strokeStyle = p.c; context.beginPath();
      if (p.kind === 'star') star(p.x,p.y,p.s * 2.2);
      else if (p.kind === 'hex') { context.lineWidth = 2; hex(p.x,p.y,p.s); }
      else if (p.kind === 'spark') { context.lineWidth = p.s; context.moveTo(p.x,p.y); context.lineTo(p.x - p.vx * (p.tail || 2.6),p.y - p.vy * (p.tail || 2.6)); context.stroke(); }
      else if (p.kind === 'ember') { context.arc(p.x,p.y,p.s,0,6.283); context.fill(); }
      else if (p.kind === 'sticker') sticker(p);
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
// Board W1's "加强" canvas, shifted from its 36.5% hit to this page's 28%: portal stars, gathering streaks and the meteor trail lead in,
// then a star ring, the burst and rising sparkles.
function starSummon(start, grade) {
  const power = powers[grade] ?? 1;
  const colors = start === null ? idleColors : burstColors[grade];
  // Portal stars thin out towards the rim.
  const sky = start === null ? [] : Array.from({length:220},() => ({a:Math.random()*6.283,d:Math.sqrt(Math.random()),s:.6+Math.random()*2,p:Math.random()*6.28}));
  const pick = () => colors[Math.floor(Math.random()*colors.length)];
  let fired = start !== null && Date.now() - start >= 2600;
  return (now, dt, add) => {
    const age = start === null ? 0 : now - start;
    const {cx,cy,idle,rise,sky:[rx,ry],gather:[gx,gy],meteor,ring,spread} = stages[scene.dataset.orientation];
    if (start === null) {
      if (Math.random() < .35 * dt) add({x:idle[0]+Math.random()*idle[1],y:idle[2],vx:(Math.random()-.5)*.6,vy:-1.2-Math.random()*1.6,life:220,s:2+Math.random()*3,c:pick(),kind:'star',twinkle:true});
      return;
    }
    const fade = Math.min(1, age / 280);
    context.fillStyle = '#fff';
    for (const s of sky) {
      context.globalAlpha = fade * (1 - s.d * s.d) * (.35 + .65 * Math.abs(Math.sin(s.p + now * .0016)));
      context.fillRect(cx + Math.cos(s.a)*s.d*rx,cy + Math.sin(s.a)*s.d*ry,s.s,s.s);
    }
    if (power > 1 && age > 1005 && age < 1885)
      for (let j = 0; j < (power - 1) * dt; j++) {
        const angle = Math.random() * 6.283, x = cx + Math.cos(angle)*gx, y = cy + Math.sin(angle)*gy;
        add({x,y,vx:(cx-x)/24,vy:(cy-y)/24,life:24,s:2.5,c:colors[j%colors.length],kind:'spark',tail:5});
      }
    if (power > 1 && age > 1540 && age < 1960) {
      // The trail follows the meteor's ease-in path and fades in with it.
      const progress = ((age - 1540) / 420) ** 2, x = cx + meteor.dx * (1 - progress), y = meteor.y - meteor.dy * (1 - progress);
      if (Math.random() < progress)
        for (let j = 0; j < 6 * dt; j++) add({x,y,vx:(Math.random()-.3)*3,vy:(Math.random()-.7)*3,life:40,s:1.5+Math.random()*3,c:colors[j%colors.length],drag:.96,grav:.02});
    }
    if (!fired && age >= 1960) {
      fired = true;
      if (power > 1) for (let j = 0; j < 36; j++) {
        const angle = j * Math.PI / 18;
        add({x:cx,y:cy,vx:Math.cos(angle)*ring[0],vy:Math.sin(angle)*ring[1],life:60,s:4+power,c:colors[j%colors.length],drag:.93,kind:'star'});
      }
      for (let j = 0; j < Math.round((90 + 165 * (power - 1)) * 1.5); j++) {
        const angle = Math.random() * Math.PI * 2, speed = 3 + Math.random() * (8 + 7 * power);
        add({x:cx,y:cy,vx:Math.cos(angle)*speed*spread[0],vy:Math.sin(angle)*speed*spread[1]-3,life:70+Math.random()*90,max:160,s:2+Math.random()*(2+power),c:colors[j%colors.length],drag:.95,grav:.06,kind:j%4===0 ? 'star' : 'dot'});
      }
    }
    if (power > 1 && age > 2300 && age < 6500 && Math.random() < .3 * power * dt)
      add({x:rise[0]+Math.random()*rise[1],y:rise[2],vx:(Math.random()-.5)*.8,vy:-1.5-Math.random()*2.5,life:200,s:3+Math.random()*5,c:pick(),kind:'star',twinkle:true});
  };
}
// W2 / PW2 "加强": sparks and hex shards just after the 28% lock, then embers rising inside the dome.
function mechaLock(start, grade) {
  if (start === null) return () => {};
  const power = powers[grade];
  const colors = grade === 'Grand' ? ['#ff3d3d','#ff7a1a','#ffb067','#fff'] : grade === 'Second' ? ['#ff7a1a','#ffb067','#fff'] : ['#8fd3ff','#fff'];
  let fired = Date.now() - start >= 2800;
  return (now, dt, add) => {
    const age = now - start, [cx,cy,left,width,bottom] = portrait ? [459,760,159,600,1300] : [960,560,520,880,980];
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
// W3/PW3's dome: 18 capsules in the board's slots and colours.
byId('capsules').replaceChildren(...[[52,268],[112,272],[172,270],[232,268],[292,262],[26,214],[82,216],[140,212],[198,214],[256,210],[312,206],[58,160],[116,158],[174,162],[232,156],[100,106],[160,104],[222,108]]
  .map(([x,y],i) => {
    const ball = text('capsule','');
    ball.style.cssText = `left:${x}px;top:${y}px;--cap:${['#5fd3b0','#5fd3b0','#ff7aae','#5fd3b0','#ffd45e','#5fd3b0','#ff7aae','#5fd3b0','#5fd3b0'][i % 9]};--tilt:${i * 47 % 90 - 45}deg`;
    return ball;
  }));
const capsuleColors = [['#ff7aae','#ffe3ee'],['#5fd3b0','#ddf7ee'],['#a991ff','#ece6ff'],['#ffd45e','#fff2c9'],['#7ccbff','#e0f2ff']];
function capsuleColor(node, number) {
  const [cap, tint] = capsuleColors[number % capsuleColors.length];
  node.style.setProperty('--cap', cap); node.style.setProperty('--tint', tint);
  return node;
}
// Board W3's outlined particles; the angle follows the remaining life so painting needs no extra state.
function sticker(p) {
  const angle = (p.turn || 0) + p.life * (p.spin || 0), r = p.s;
  context.save(); context.translate(p.x + (p.sway === undefined ? 0 : 13 * Math.sin(p.life * .06 + p.sway)), p.y); context.rotate(angle);
  context.strokeStyle = '#4b2a4f'; context.lineWidth = 3; context.lineJoin = 'round'; context.beginPath();
  if (p.shape === 'heart') {
    const h = r * .7;
    context.moveTo(0,h*.9); context.bezierCurveTo(-h*1.6,-h*.2,-h*.7,-h*1.4,0,-h*.5); context.bezierCurveTo(h*.7,-h*1.4,h*1.6,-h*.2,0,h*.9);
  }
  else if (p.shape === 'star') for (let k = 0; k < 10; k++) { const a = -Math.PI / 2 + k * Math.PI / 5, rr = k % 2 ? r * .45 : r; context.lineTo(Math.cos(a) * rr, Math.sin(a) * rr); }
  else if (p.shape === 'dot') context.arc(0,0,r * .55,0,6.283);
  else { context.scale(1,Math.cos(angle * 2)); context.rect(-7,-5,14,10); }
  context.closePath(); context.fill(); context.stroke(); context.restore();
}
// W3 / PW3 "加强", shifted from the board's 36.7% opening to this page's 28%: the opening pop, hearts that rise a little and sway,
// and side cannons (one volley for Second, two for Grand). Like the board, every particle fades over its last 40 frames (max 100).
function capsuleGacha(start, grade) {
  if (start === null) return () => {};
  const power = powers[grade];
  const colors = power === 3 ? ['#ff7aae','#ffd45e','#5fd3b0','#a991ff','#7ccbff','#fff']
    : power === 2 ? ['#ff7aae','#ffb3cf','#fff','#ffd45e'] : ['#5fd3b0','#bff0df','#fff'];
  let fired = Date.now() - start >= 2800;
  let volleys = fired ? 2 : 0;
  return (now, dt, add) => {
    const age = now - start, [cx,cy,left,width,bottom] = portrait ? [459,600,209,500,1000] : [1180,520,880,600,760];
    if (!fired && age >= 1960) {
      fired = true;
      for (let k = 0; k < [30,70,140][power - 1]; k++) {
        const angle = Math.random() * Math.PI * 2, speed = 5 + Math.random() * (6 + 5 * power);
        add({x:cx,y:cy,vx:Math.cos(angle)*speed,vy:Math.sin(angle)*speed-4,life:120+Math.random()*60,max:100,s:9+Math.random()*(6+4*power),c:colors[k%colors.length],drag:.97,grav:.32,
          kind:'sticker',shape:['heart','star','dot'][k%3],turn:Math.random()*6.28,spin:(Math.random()-.5)*.2});
      }
    }
    if (power > 1 && age > 2390 && age < 5390 && Math.random() < .12 * power * dt)
      add({x:left+Math.random()*width,y:bottom,vx:(Math.random()-.5)*.6,vy:-1.4-Math.random()*1.2,life:180,max:100,s:9+Math.random()*8,c:Math.random() < .6 ? '#ff7aae' : '#ffd45e',drag:.97,kind:'sticker',shape:'heart',sway:Math.random()*6.28});
    if (power > 1 && (volleys === 0 && age >= 2010 || volleys === 1 && power === 3 && age >= 2690)) {
      volleys++;
      const [west,east,y,count] = portrait ? [230,690,1150,power === 3 ? 50 : 26] : [820,1560,960,power === 3 ? 60 : 30];
      for (const [x,aim] of [[west,-1.15],[east,-1.99]]) for (let k = 0; k < count; k++) {
        const angle = aim + (Math.random()-.5)*.6, speed = 16 + Math.random()*14;
        add({x,y,vx:Math.cos(angle)*speed,vy:Math.sin(angle)*speed,life:150+Math.random()*50,max:100,s:8+Math.random()*6,c:colors[k%colors.length],drag:.97,grav:.32,
          kind:'sticker',shape:k%4 === 0 ? 'star' : 'confetti',turn:Math.random()*6.28,spin:(Math.random()-.5)*.3});
      }
    }
  };
}
const events = new EventSource('/lottery/events');
events.addEventListener('state',event => show(JSON.parse(event.data)));
events.onerror = () => { shown=''; clear(); };
