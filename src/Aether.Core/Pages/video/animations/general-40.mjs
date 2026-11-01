import { inside, k, out, inq, quint, on2, rnd, spline, pathOf, chordOf, dense, onScreen, textures, slash as drawSlash, drawing, shakeAt, stroke, sparks, trace, clipTo} from './toolkit.mjs';
import {createName, createCaption, framing, sky, caption, revealName, impactFrame, whiteFlash, pieces as makePieces} from './scenes.mjs';

export class Triple {
  constructor({app,canvas,images,character,nickname}) {
    const P=PIXI,F=P.filters;
    const {tex,silhouette,radial,dispose}=textures(P);
    this.disposeTextures=dispose;this.palette=character.palette;
    const points = pts => spline(pts).pts;
    const [moon, impact, moonRed] = images.slice(11);

    // The action chain. Every pose stands on the ground line with its hips at X, at the scale that makes the blade the same
    // length, and each one takes him further right. The images hold the figure only: the light is drawn here, along the path
    // of the blade tip, and each slash ends exactly at the tip of the pose that finishes the cut.
    const keys = ['charge', 'draw', 'cut1', 'wind', 'smear2', 'cut2', 'spin', 'smear3', 'cut3', 'into', 'home'];
    const layout = [[0.7, 640, 0], [0.7, 720, 0], [0.65, 780, 50], [0.658, 880, 0], [0.683, 900, -30], [0.627, 1000, -50], [0.72, 800, 0], [0.706, 800, 0], [0.679, 840, 0], [0.7, 1010, -50], [0.7, 1010, -50]];
    const PO = {}, GROUND = 1010;
    keys.forEach((key, i) => { const {hip,sole} = character.anchors.triple[key]; const [s,X,dy] = layout[i]; PO[key] = { img: images[i], tex: tex(images[i]), hip, s, sole, X, dy }; });
    const W = (key, q, X = PO[key].X) => { const p = PO[key]; return [X + (q[0] - p.hip[0]) * p.s, GROUND + p.dy + (q[1] - p.sole) * p.s]; };

    // Cut 1, the draw on the run: out of the scabbard mouth at the hip, a flat level line to the tip at full stretch.
    const H1 = W('cut1', [1520, 194]);
    const t1 = points([W('draw', [860, 400]), W('draw', [1200, 372]), W('draw', [1509, 337]), H1]);
    // Cut 2: the level cut carries him round. The tip goes on past him, behind him (the far half of the loop), comes out on
    // his left (the pose facing us), and rises across his front to the raised tip.
    const TL = W('spin', [125, 389]), loop = [], loopBack = [], CX = TL[0] + 568, CY = TL[1] + 67;
    for (let i = 0; i <= 50; i++) {
      const th = -0.7 - (Math.PI - 0.2) * i / 50;
      loop.push([CX + 568 * Math.cos(th), CY + 120 * Math.sin(th) + 67 * Math.cos(th)]); loopBack.push(Math.sin(th) < 0);
    }
    const H2 = W('cut3', [1069, 31]);
    const lead = points([H1, [(H1[0] + loop[0][0]) / 2 + 12, (H1[1] + loop[0][1]) / 2 - 8], loop[0]]).slice(0, -1);
    const t3rise = points([loop[loop.length - 1], [560, 735], [820, 640], [1000, 360], [1120, 120], H2]).slice(1);
    // The blade does not stop up there: it carries on over his head, where his left hand comes up to the hilt.
    const TW = W('wind', [335, 56]);
    const t5 = points([H2, [1030, -90], [860, -60], TW]);
    // Cut 3, the heavy chop: over the top and down through the tip of the mid-cut pose into the low tip of the finish. He
    // steps forward as it falls, which draws the arc out into a long diagonal. It ends low, so the sword can go straight home.
    const H3 = W('cut2', [1480, 866]);
    const t2 = points([TW, [920, 30], [1170, 150], W('smear2', [1423, 45]), [1490, 620], H3]);
    // From the low finish the blade comes back up to the scabbard mouth at his hip.
    const t6 = points([H3, [1400, 740], [1240, 620], W('into', [850, 394])]);
    const blades = [pathOf(t1), pathOf([...lead, ...loop, ...t3rise], [...lead.map(() => false), ...loopBack, ...t3rise.map(() => false)]), pathOf(t2), pathOf(t6), pathOf(t5)];

    // The cuts a straight blade leaves are straight: each is the chord of the stretch of its slash still lit on the frame
    // it lands (from the slash's tail to the blade tip), run right across the sky. Each slash's tail catches up as it
    // swings, so what is lit at the hit is nearly straight and the cut lies along it. The sky breaks along all three, full
    // length, so it opens along the very lines the viewer saw land; the moon sits whole in one piece. Each side of a line
    // is a polygon closed round a big circle, and each piece is one side of every line.
    const C0 = [960, 540], RR = 5000;

    const sides = line => [1, -1].map(dir => {
      const ang = p => Math.atan2(p[1] - C0[1], p[0] - C0[0]), a0 = ang(line[0]), a1 = ang(line[line.length - 1]);
      let da = a0 - a1; if (dir > 0) while (da <= 0) da += 2 * Math.PI; else while (da >= 0) da -= 2 * Math.PI;
      return [...line, ...Array.from({ length: 65 }, (_, i) => [C0[0] + RR * Math.cos(a1 + da * i / 64), C0[1] + RR * Math.sin(a1 + da * i / 64)])];
    });

    // TAILS: how much of each slash has already drawn in by the frame it lands (the slashes below use the same values).
    const TAILS = [0.35, 0.68, 0.3];

    const scars = TAILS.map((u0, i) => chordOf(blades[i], u0));

    const [s1, s2, s3] = scars.map(sides);
    const cells = [];
    for (const r1 of s1) for (const r2 of s2) for (const r3 of s3) {
      const regions = [r3, r2, r1];
      let n = 0, cx = 0, cy = 0;
      for (let x = -60; x <= 1980; x += 30) for (let y = -60; y <= 1140; y += 30) if (regions.every(r => inside(r, x, y))) { n++; cx += x; cy += y; }
      if (n >= 12) cells.push({ regions, c: [cx / n, cy / n], n });
    }

    const stage = app.stage;
    const world = new P.Container(); world.pivot.set(960, 540); world.position.set(960, 540); stage.addChild(world);
    const dim = new P.Sprite(radial([[0, 'rgba(6,6,12,0.55)'], [0.6, 'rgba(4,4,8,0.85)'], [1, 'rgba(0,0,0,1)']]));
    dim.anchor.set(0.5); dim.position.set(960, 540); dim.width = 3400; dim.height = 2400; world.addChild(dim);

    // The moon hangs up on the right, ahead of the hero, clear of every cut (splitting the moon is 60 档's).

    const skyOf = (img,parent) => sky(P,tex,radial,img,parent,[-13,-394,2600,1733],[2800,1900]);
    const red = new P.Container(); red.pivot.set(960, 540); red.position.set(960, 540); skyOf(moonRed, red); world.addChild(red);
    const pieces = makePieces(P,cells,world,host=>skyOf(moon,host),[900,560],{base:240,max:120,factor:.25,rotation:.03,down:.5});
    // Effects canvases cover a margin round the screen, since the camera tilts and zooms.
    const OX = 300, OY = 400;
    const backC = document.createElement('canvas'); backC.width = 1920 + 2 * OX; backC.height = 1080 + 2 * OY;
    const backTex = tex(backC), backSprite = new P.Sprite(backTex); backSprite.blendMode = 'add'; backSprite.position.set(-OX, -OY); world.addChild(backSprite);

    const rim = new P.Sprite(radial(this.palette.rimStops));
    rim.anchor.set(0.5); rim.width = 1100; rim.height = 1100; world.addChild(rim);
    const ghosts = [0, 1].map(() => { const s = new P.Sprite(PO.charge.tex); s.alpha = 0; world.addChild(s); return s; });
    const hero = new P.Sprite(PO.charge.tex); hero.tint = 0xe0e0e0; world.addChild(hero);

    const fx = document.createElement('canvas'); fx.width = backC.width; fx.height = backC.height;
    const fxTex = tex(fx), fxSprite = new P.Sprite(fxTex); fxSprite.blendMode = 'add'; fxSprite.position.set(-OX, -OY); world.addChild(fxSprite);

    const ui = new P.Container(); ui.pivot.set(960, 540); stage.addChild(ui);
    const nameG = new P.Container(); nameG.position.set(30, 420); nameG.rotation = -0.06; ui.addChild(nameG);
    const {nameGhosts,nameT,nameMask}=createName(P,nameG,nickname,this.palette.nameGhost);
    // Entry caption: says who is coming in from the cut-in, until the dash's wind sweeps it away.
    const cap = new P.Container(); cap.position.set(1060, 900); ui.addChild(cap);
    const {capName,capMask}=createCaption(P,cap,nickname,760);

    const imp = new P.Container(); world.addChild(imp);
    const impBg = new P.Graphics().rect(-1200, -1200, 4320, 3480).fill(0xffffff); imp.addChild(impBg);
    const impLines = new P.Sprite(tex(impact)); impLines.anchor.set(0.5); impLines.width = impLines.height = 4600; imp.addChild(impLines);
    const silB = new P.Sprite(silhouette(PO.cut2.img, '#000')), silW = new P.Sprite(silhouette(PO.cut2.img, '#fff'));
    for (const s of [silB, silW]) { s.scale.set(PO.cut2.s); s.position.set(PO.cut2.X - PO.cut2.hip[0] * PO.cut2.s, GROUND + PO.cut2.dy - PO.cut2.sole * PO.cut2.s); imp.addChild(s); }

    const flash = new P.Graphics().rect(-1200, -1200, 4320, 3480).fill(0xffffff); world.addChild(flash);
    const bars = [new P.Graphics(), new P.Graphics()]; stage.addChild(...bars);
    stage.setChildIndex(ui, stage.children.indexOf(world) + 1);

    const CAM3 = -30;
    const bloom = new F.AdvancedBloomFilter({ threshold: 0.9, bloomScale: 1.15, brightness: 1, blur: 7, quality: 5 });
    const shock = new F.ShockwaveFilter({ center: { x: H3[0], y: H3[1] + CAM3 }, speed: 1500, amplitude: 54, wavelength: 240, brightness: 1.15, radius: -1 });
    const zoom = new F.ZoomBlurFilter({ strength: 0, center: { x: H3[0], y: H3[1] + CAM3 }, innerRadius: 60 });
    const rgb = new F.RGBSplitFilter({ red: { x: 0, y: 0 }, green: { x: 0, y: 0 }, blue: { x: 0, y: 0 } });

    this.S = {
      app, canvas, world, ui, dim, red, pieces, scars, blades, H3, PO, GROUND, CAM3, W, OX, OY, backC, backTex, rim, ghosts, hero, fx, fxTex,
      emberSrc: scars.map(s => pathOf(dense(s).filter(onScreen))),
      nameG, cap, capName, capMask, nameGhosts, nameT, nameMask, imp, impBg, impLines, silB, silW, flash, bars, bloom, shock, zoom, rgb
    };

  }

  destroy() {
    this.disposeTextures();
    for(const filter of [this.S.bloom,this.S.shock,this.S.zoom,this.S.rgb])filter.destroy();
  }

  frame(t) {
    const S = this.S;
    // Three hit stops, at logical times L1–L3 for 70, 70 and 160 ms of real time: real time keeps running (shake,
    // impact frames) while everything else holds. HR are the real times they start.
    const L1 = 1624, L2 = 1875, L3 = 2127, HS = [[L1, 70], [L2, 70], [L3, 160]], HR = [1624, 1945, 2267], REL3 = 2427;
    let l = null, acc = 0;
    for (const [s, d] of HS) { if (t < s + acc) break; if (t < s + acc + d) { l = s; break; } acc += d; }
    if (l === null) l = t - acc;

    // The chain, one beat per pose. The heaviest cut comes last and ends low, so the sword can go straight home from there.
    const DASH = 1400, DRAW = 1540, SPIN = 1666, SM3 = 1750, RISE = 1834, WIND = 1917, SM2 = 2001, P8 = 2085, CAM3 = S.CAM3;
    const NAME = 2427, INTO = 2747, HOME = 2947, FLARE = 2977, SPLIT = 3157, RED0 = 4700, RED1 = 5300, EXIT = 6750, END = 7170;

    // Letterbox and dimming.
    framing(S,t,l,{height:118,barIn:[0,280],barOut:[EXIT+120,EXIT+500],dimIn:[[.42,0,300],[.4,600,1300],[-.4,RED0,RED1]],dimOut:[EXIT,END]});

    // Backdrop: whole through the combo; after the zanshin the scars flare in cut order, then the sky breaks along all
    // three and every piece slides off, faster and faster, and dissolves.
    const sp = k(SPLIT, SPLIT + 850, l), e = sp * sp, drift = -l * 0.006;
    const fade = 1 - inq(k(SPLIT + 400, SPLIT + 1000, l));
    for (const p of S.pieces) {
      p.dx = p.dir[0] * p.dist * e + drift; p.dy = p.dir[1] * p.dist * e; p.r = p.rot * e;
      p.c.position.set(p.c.pivot.x + p.dx, p.c.pivot.y + p.dy); p.c.rotation = p.r;
      p.c.alpha = 0.95 * out(k(600, 1250, l)) * fade;
    }
    // The blood-moon side shows through the openings, holds a beat once the moonlit pieces are gone, then gives way to the stream.
    S.red.alpha = (l >= SPLIT ? 1 : 0) * (1 - inq(k(RED0, RED1, l)));
    S.red.scale.set(1 + 0.03 * k(SPLIT, RED1, l)); S.red.position.set(960 + drift, 540);

    // Hero: one pose per beat, placed by its hips.
    const keyAt = l < DRAW ? 'charge' : l < L1 ? 'draw' : l < SPIN ? 'cut1' : l < SM3 ? 'spin' : l < RISE ? 'smear3' : l < WIND ? 'cut3' : l < SM2 ? 'wind' : l < P8 ? 'smear2' : l < INTO ? 'cut2' : l < HOME ? 'into' : 'home';
    const po = S.PO[keyAt];
    let hx = po.X, lift = 0;
    if (keyAt === 'charge') { hx = (l < DASH ? po.X + 16 * k(660, DASH, on2(l)) : po.X + 16 + 64 * quint(k(DASH, DRAW, l))) - 980 * (1 - quint(k(250, 520, l))); lift = 20 * (1 - out(k(520, 660, l))); }
    // He rises out of the lunge and stands as the sword goes home.
    if (keyAt === 'into') lift = 70 * (1 - quint(k(INTO, INTO + 100, l)));
    if (l >= INTO) hx += 20 * k(INTO, 6400, on2(l));
    hx -= 70 * k(EXIT, END, l);
    S.hero.texture = po.tex; S.hero.scale.set(po.s); S.hero.position.set(hx - po.hip[0] * po.s, S.GROUND + po.dy - po.sole * po.s + lift);
    S.hero.alpha = (l < 250 ? 0 : 1) * (1 - k(EXIT, END, l));
    // Afterimages trail the cut-in, the dash and every mid-cut frame, opposite to the motion.
    const gOff = l < 520 ? [-70, 0] : keyAt === 'charge' && l >= DASH ? [-110, 0] : keyAt === 'draw' ? [-34, 0] : keyAt === 'smear2' ? [-26, -30] : keyAt === 'spin' ? [40, 0] : keyAt === 'smear3' ? [-22, 30] : keyAt === 'into' && l < INTO + 90 ? [0, 30] : null;
    const ghostOn = gOff && l >= 250;
    S.ghosts.forEach((g, i) => {
      g.texture = po.tex; g.scale.set(po.s);
      if (gOff) g.position.set(S.hero.x + (i + 1) * gOff[0], S.hero.y + (i + 1) * gOff[1]);
      g.alpha = ghostOn ? (i ? 0.16 : 0.32) : 0; g.tint = this.palette.ghost;
    });
    S.rim.position.set(hx, 640);
    S.rim.alpha = (0.5 * out(k(400, 900, l)) + 0.5 * out(k(1100, DASH, l)) - 0.4 * k(L3 + 240, L3 + 940, l)) * (1 - k(EXIT, END, l));
    S.rim.scale.set((1100 + 300 * k(1100, DASH, l)) / 512);

    // Camera follows the blade: a slow push in the charge, a punch on the draw, back level through the spin, up with the rising
    // cut, then a whip down with the chop and the big punch centred on its low tip, and a long settle. The zoom pivots on Fp,
    // which only moves while the zoom is 1.
    let Fp = [960, 540], cam = 1 + 0.05 * inq(k(520, DASH, l)), camY = 0, rot = -0.01 * inq(k(520, DASH, l));
    if (l >= DRAW) { cam = 1.05 + 0.02 * out(k(DRAW, L1, l)); rot = -0.01 * (1 - out(k(DRAW, 1700, l))); }
    if (l >= SPIN) { cam = 1.07 - 0.07 * out(k(SPIN, SM3, l)); rot = 0; }
    if (l >= SM3) { cam = 1; camY = 170 * out(k(SM3, RISE, l)); }
    if (l >= SM2) { Fp = S.H3; camY = 170 + (CAM3 - 170) * out(k(SM2, P8, l)); }
    if (l >= P8) { camY = CAM3; cam = 1 + 0.15 * out(k(P8, L3, l)); }
    if (l >= L3) { cam = 1.15 - 0.15 * out(k(L3, INTO, l)); camY = CAM3 * (1 - out(k(L3, INTO, l))); }
    if (l >= INTO) { Fp = [960, 540]; cam = 1 + 0.025 * k(INTO, 6600, l); camY = 0; }
    let sx = 0, sy = 0;
    // Shake runs on real time. With a direction it mostly moves along the cut.
    const shake = (...args) => { const [dx,dy]=shakeAt(t,...args); sx+=dx;sy+=dy; };
    shake(HR[0], 14, 220, [1, 0]); shake(HR[1], 16, 220, [0.42, -0.91]); shake(P8 + 140, 8, 140);
    shake(REL3, 32, 520, [0.6, 0.8]); shake(HOME + 300, 7, 130); shake(SPLIT + 300 + 120, 5, 160);
    for (const c of [S.world, S.ui]) { c.pivot.set(Fp[0], Fp[1]); c.scale.set(cam); c.rotation = rot; c.position.set(Fp[0] + sx, Fp[1] + sy + camY); }

    // Cut 3's effects: one white frame, two impact frames, then the shockwave, radial blur and colour split at its tip.
    whiteFlash(S.flash,l,P8,L3);
    const impA = t >= HR[2] && t < HR[2] + 40, impB = t >= HR[2] + 40 && t < HR[2] + 80;
    impactFrame(S,impA,impB,()=>S.impLines.position.set(S.H3[0],S.H3[1]));
    const released = t >= REL3, sw = released && l < L3 + 520;
    S.shock.time = (l - L3) / 1000; S.shock.amplitude = 54 * (1 - k(L3, L3 + 520, l));
    S.zoom.strength = 0.17 * (1 - out(k(L3, L3 + 300, l)));
    const rs = 13 * (1 - out(k(L3, L3 + 480, l)));
    S.rgb.red = { x: -rs, y: 0 }; S.rgb.blue = { x: rs, y: 0 };
    const filters = [S.bloom];
    if (sw) filters.push(S.shock);
    if (released && S.zoom.strength > 0.005) filters.push(S.zoom);
    if (released && rs > 0.5) filters.push(S.rgb);
    S.world.filters = S.imp.visible ? null : filters;

    // Name: cut out from left to right, with red afterimages.
    revealName(S,l,{start:NAME,end:NAME+260,ghostIn:NAME+50,ghostOut:NAME+180,ghostEnd:NAME+570,exit:EXIT,finish:END,x:30,y:420});

    // Entry caption: the name wipes in at the hero's feet; the dash's wind sweeps it off.
    caption(S,l,{start:420,revealStart:560,revealEnd:860,exitStart:1450,exitEnd:1570,x:1060,y:900});

    this.drawFx(l, k, out, { L1, L2, L3, DASH, WIND, SM2, SPIN, INTO, HOME, FLARE, SPLIT, END, hx, drift, fade, charge: keyAt === 'charge' && l < DASH ? hx : null });
    S.fxTex.source.update(); S.backTex.source.update();
  }

  // Slashes, scars, particles, in world coordinates. Two additive canvases: one behind the hero (scars, the far half
  // of the spin loop, the draw leaving the scabbard), one in front.
  drawFx(l, k, out, T) {
    const S = this.S, g = S.fx.getContext('2d'), gb = S.backC.getContext('2d');
    const draw = drawing(g,gb), {band,arcLine,glint} = draw;
    for (const c of [g, gb]) { c.setTransform(1, 0, 0, 1, 0, 0); c.clearRect(0, 0, c.canvas.width, c.canvas.height); c.setTransform(1, 0, 0, 1, S.OX, S.OY); c.globalCompositeOperation = 'lighter'; c.lineCap = 'round'; c.lineJoin = 'round'; }

    // A slash: a sharp crescent whose head is the blade tip, held bright through its hit stop, then frayed into strands.
    const slash = (...args) => drawSlash(draw,this.palette,...args);
    // Sparks thrown off a hit at the blade tip, mostly across the cut.
    const hitSparks = (path,t0,count,power,seed) => sparks(g,l-t0,count,power,seed,(i,s)=>path.at(.82+rnd(i,s+1)*.18),this.palette.spark);

    // Charge: sparks are pulled into the hilt while the hero holds the stance; a glint on the hilt before the dash.
    if (T.charge && l > 650) {
      const hilt = S.W('charge', [1181, 417], T.charge);
      for (let i = 0; i < 90; i++) {
        const s0 = 650 + rnd(i, 1) * 620, dur = 380 + rnd(i, 2) * 260, p = (l - s0) / dur;
        if (p < 0 || p > 1) continue;
        const ang = rnd(i, 3) * Math.PI * 2, r0 = 360 + rnd(i, 4) * 420, r = r0 * (1 - p * p), r2 = r0 * (1 - Math.max(0, p - 0.12) ** 2);
        g.strokeStyle = i % 3 ? 'rgba(255,120,130,' + (0.9 * Math.sin(p * Math.PI)) + ')' : 'rgba(255,255,255,' + Math.sin(p * Math.PI) + ')';
        g.lineWidth = 1.5 + rnd(i, 5) * 2;
        g.beginPath(); g.moveTo(hilt[0] + Math.cos(ang) * r, hilt[1] + Math.sin(ang) * r); g.lineTo(hilt[0] + Math.cos(ang) * r2, hilt[1] + Math.sin(ang) * r2); g.stroke();
      }
      glint(hilt[0], hilt[1], k(1240, 1390, l), 100);
    }
    // Dash: a dust streak along the ground behind the hero.
    if (l >= T.DASH && l < 1800) {
      const p = k(T.DASH, 1540, l), a = 1 - k(1560, 1800, l);
      for (let s = 0; s < 6; s++) {
        const y = 945 + s * 9, x1 = 656 + 64 * p + 160, x0 = 160 + rnd(s, 40) * 120 + 300 * k(1540, 1800, l);
        g.strokeStyle = 'rgba(255,' + (150 + s * 15) + ',150,' + (0.35 * a * (1 - s / 7)) + ')'; g.lineWidth = 2 + (s % 2);
        g.beginPath(); g.moveTo(x0, y); g.lineTo(x1, y - 4); g.stroke();
      }
    }
    // The raised blade glints once as it comes over.
    glint(...S.W('wind', [556, 182]), k(T.WIND - 20, T.SM2 - 20, l), 70);

    // The slashes: "da, da, boom". Each head reaches its blade tip on the frame the finishing pose appears.
    const B = S.blades;
    if (l >= 1545 && l < T.L1 + 420) slash(B[0], out(k(1545, 1620, l)), 0.35 * out(k(1570, 1660, l)), out(k(1560, 1635, l)), 60, k(T.L1, T.L1 + 420, l), 21);
    if (l >= T.SPIN && l < T.L2 + 160) slash(B[1], k(T.SPIN, 1829, l), 0.45 * out(k(1724, 1799, l)) + 0.23 * out(k(1789, 1854, l)), k(T.SPIN + 20, 1844, l), 72, k(T.L2, T.L2 + 160, l), 31);
    if (l >= 1885 && l < T.SM2 + 60) slash(B[4], k(1885, T.SM2 - 10, l), k(1915, T.SM2 + 40, l), k(1885, T.SM2 - 10, l), 22, 0, 71, 0.7);
    if (l >= T.SM2 + 4 && l < T.L3 + 520) slash(B[2], out(k(T.SM2 + 4, 2081, l)), 0.3 * out(k(T.SM2 + 8, 2081, l)), out(k(T.SM2 + 16, 2096, l)), 92, k(T.L3, T.L3 + 520, l), 41);
    if (l >= T.INTO - 120 && l < T.INTO + 110) slash(B[3], k(T.INTO - 120, T.INTO, l), k(T.INTO - 80, T.INTO + 80, l), k(T.INTO - 120, T.INTO, l), 22, 0, 81, 0.75);
    hitSparks(B[0], T.L1, 70, 0.7, 10); hitSparks(B[1], T.L2, 70, 0.7, 50); hitSparks(B[2], T.L3, 160, 1, 60);

    // The straight cuts hang behind the hero from each hit, full length. Before the break they flare once each, in cut order.
    const open = out(k(T.SPLIT, T.SPLIT + 260, l)), flick = 0.8 + 0.2 * Math.sin(l * 0.045);

    const line = (w,color) => stroke(gb,w,color);
    S.scars.forEach((pts, i) => {
      const hit = [T.L1, T.L2, T.L3][i], a = k(hit, hit + 120, l) * (1 - k(T.SPLIT + 450, T.SPLIT + 950, l));
      if (a <= 0) return;
      const fl = Math.sin(Math.PI * k(T.FLARE + 70 * i, T.FLARE + 70 * i + 160, l)), wide = Math.max(open, 0.6 * fl);
      // Once cut 3 lands, cuts 1 and 2 dim, so cut 3's cut holds the frame until all three flare before the break.
      const pa = a * (i < 2 ? Math.max(1 - 0.75 * k(T.L3 + 200, T.L3 + 500, l), fl, open) : 1);
      gb.save(); gb.translate(T.drift, 0); trace(gb, pts);
      line(12 + 40 * wide, 'rgba(255,40,70,' + (0.2 + 0.25 * fl) * pa * flick + ')');
      line(3 + 10 * wide, 'rgba(255,150,160,' + (0.6 + 0.3 * fl) * pa + ')');
      line(1.4 + 3 * wide, 'rgba(255,255,255,' + pa * (1 - 0.6 * open) + ')');
      gb.restore();
    });
    // Once open, each piece carries a lit lip along its own edges.
    if (l >= T.SPLIT) for (const p of S.pieces) {
      gb.save();
      gb.translate(p.c.pivot.x + p.dx, p.c.pivot.y + p.dy); gb.rotate(p.r); gb.translate(-p.c.pivot.x, -p.c.pivot.y);
      for (const r of p.regions) clipTo(gb, r);
      for (const pts of S.scars) { trace(gb, pts); line(10, 'rgba(255,60,80,' + 0.35 * T.fade * open + ')'); line(2, 'rgba(255,235,235,' + 0.9 * T.fade * open + ')'); }
      gb.restore();
    }

    // The bare steel catches the light as it slides home; a hard glint on the guard marks the click, and then the sky breaks.
    if (l >= T.INTO && l < T.HOME) {
      const [a, b] = [S.W('into', [850, 394], T.hx), S.W('into', [1030, 394], T.hx)];
      g.strokeStyle = 'rgba(255,255,255,' + 0.85 * Math.sin(k(T.INTO, T.HOME, l) * Math.PI) + ')'; g.lineWidth = 3;
      g.beginPath(); g.moveTo(a[0], a[1]); g.lineTo(b[0], b[1]); g.stroke();
    }
    glint(...S.W('home', [908, 392], T.hx), k(T.HOME, T.HOME + 200, l), 120);
    // Embers peel off the lips of every seam as the sky opens.
    if (l >= T.SPLIT && l < T.SPLIT + 1300) {
      const e = l - T.SPLIT;
      for (let i = 0; i < 160; i++) {
        const t0 = rnd(i, 30) * 520, life = 600 + rnd(i, 31) * 700, q = (e - t0) / life;
        if (q <= 0 || q >= 1) continue;
        const [x0, y0, nx, ny] = S.emberSrc[i % 3].at(rnd(i, 32)), side = i % 2 ? 1 : -1, v = 30 + rnd(i, 33) * 110;
        const px = x0 + nx * v * 1.4 * side * q + T.drift, py = y0 + ny * v * 1.4 * side * q - 40 * q;
        g.fillStyle = 'rgba(255,' + (120 + (i % 3) * 50) + ',125,' + Math.sin(q * Math.PI) * 0.9 + ')';
        g.beginPath(); g.arc(px, py, 1.2 + rnd(i, 34) * 2.2, 0, Math.PI * 2); g.fill();
      }
    }
    // Embers rise during the zanshin hold.
    if (l > T.L3 + 240 && l < T.END) {
      const fade = k(T.L3 + 240, T.L3 + 740, l) * (1 - k(T.END - 400, T.END, l));
      for (let i = 0; i < 46; i++) {
        const life = 2200 + rnd(i, 16) * 1800, ph = ((l + rnd(i, 17) * life) % life) / life;
        const px = 200 + rnd(i, 18) * 1700 + Math.sin(ph * 6 + i) * 20, py = 1000 - ph * 520 - rnd(i, 19) * 200;
        g.fillStyle = 'rgba(255,' + (90 + (i % 3) * 50) + ',100,' + (Math.sin(ph * Math.PI) * fade * 0.8) + ')';
        g.beginPath(); g.arc(px, py, 1.5 + rnd(i, 20) * 2.5, 0, Math.PI * 2); g.fill();
      }
    }
    for (const c of [g, gb]) c.globalCompositeOperation = 'source-over';
  }

}
