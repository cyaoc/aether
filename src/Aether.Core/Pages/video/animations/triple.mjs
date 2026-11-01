import {inside, k, out, inq, quint, on2, rnd, logicalTime, realTimes, spline, pathOf, scarsOf, seamsOf, sidesOf, shakes, drawing, beginFx, charge, seamEmbers, risingEmbers, flareScars, pieceLips, steelGlint} from './toolkit.mjs';
import {createStage, createFx, createFigure, createStrike, createName, createCaption, createPieces, scatter, framing, sky, bloodSky, caption, revealName, impactFrame, whiteFlash, strikeFilters, aim} from './scenes.mjs';

export class Triple {
  constructor({app,images,character,kit,nickname}) {
    const P=PIXI;
    const {tex,silhouette,post}=kit;
    this.palette=character.palette;
    const points = pts => spline(pts).pts;
    const {moon, impact, 'moon-red': moonRed} = images;

    // The action chain. Every pose stands on the ground line with its hips at X, at the scale that makes the blade the same
    // length, and each one takes him further right. The images hold the figure only: the light is drawn here, along the path
    // of the blade tip, and each slash ends exactly at the tip of the pose that finishes the cut.
    const keys = ['charge', 'draw', 'cut1', 'wind', 'smear2', 'cut2', 'spin', 'smear3', 'cut3', 'into', 'home'];
    const layout = [[0.7, 640, 0], [0.7, 720, 0], [0.65, 780, 50], [0.658, 880, 0], [0.683, 900, -30], [0.627, 1000, -50], [0.72, 800, 0], [0.706, 800, 0], [0.679, 840, 0], [0.7, 1010, -50], [0.7, 1010, -50]];
    const PO = {}, GROUND = 1010;
    keys.forEach((key, i) => { const {image,hip,sole} = character.poses.triple[key]; const [s,X,dy] = layout[i]; PO[key] = { img: images[image], tex: tex(images[image]), hip, s, sole, X, dy }; });
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
    // length, so it opens along the very lines the viewer saw land; the moon sits whole in one piece. Each piece is one
    // side of every line, kept as nested masks and thresholded on a coarser grid than cutCells: EV2 breaks along these cells.

    // TAILS: how much of each slash has already drawn in by the frame it lands (the slashes below use the same values).
    const TAILS = [0.35, 0.68, 0.3];

    const scars = scarsOf(blades, TAILS);

    const [s1, s2, s3] = scars.map(sidesOf);
    const cells = [];
    for (const r1 of s1) for (const r2 of s2) for (const r3 of s3) {
      const regions = [r3, r2, r1];
      let n = 0, cx = 0, cy = 0;
      for (let x = -60; x <= 1980; x += 30) for (let y = -60; y <= 1140; y += 30) if (regions.every(r => inside(r, x, y))) { n++; cx += x; cy += y; }
      if (n >= 12) cells.push({ regions, c: [cx / n, cy / n], n });
    }

    const {world, ui, dim} = createStage(P, app, kit, [3400, 2400]);

    // The moon hangs up on the right, ahead of the hero, clear of every cut (splitting the moon is 60 档's).

    const skyOf = (img,parent) => sky(P,kit,img,parent,[-13,-394,2600,1733],[2800,1900]);
    const red = new P.Container(); red.pivot.set(960, 540); red.position.set(960, 540); skyOf(moonRed, red); world.addChild(red);
    const pieces = createPieces(P,cells,world,host=>skyOf(moon,host),[900,560],{base:240,max:120,factor:.25,rotation:.03,down:.5});
    // Effects canvases cover a margin round the screen, since the camera tilts and zooms.
    const OX = 300, OY = 400;
    const back = createFx(P, kit, world, 1920 + 2 * OX, 1080 + 2 * OY, -OX, -OY);

    const {rim, ghosts, hero} = createFigure(P, kit, world, this.palette, PO.charge.tex, 2);

    const fx = createFx(P, kit, world, 1920 + 2 * OX, 1080 + 2 * OY, -OX, -OY);

    const nameG = new P.Container(); nameG.position.set(30, 420); nameG.rotation = -0.06; ui.addChild(nameG);
    const {nameGhosts,nameT,nameMask}=createName(P,nameG,nickname,this.palette.nameGhost);
    // Entry caption: says who is coming in from the cut-in, until the dash's wind sweeps it away.
    const cap = new P.Container(); cap.position.set(1060, 900); ui.addChild(cap);
    const {capName,capMask}=createCaption(P,cap,nickname,760);

    const {imp, impBg, impLines, flash} = createStrike(P, kit, world, impact, [-1200, -1200, 4320, 3480], 4600);
    const silB = new P.Sprite(silhouette(PO.cut2.img, '#000')), silW = new P.Sprite(silhouette(PO.cut2.img, '#fff'));
    for (const s of [silB, silW]) { s.scale.set(PO.cut2.s); s.position.set(PO.cut2.X - PO.cut2.hip[0] * PO.cut2.s, GROUND + PO.cut2.dy - PO.cut2.sole * PO.cut2.s); imp.addChild(s); }

    const bars = [new P.Graphics(), new P.Graphics()]; app.stage.addChild(...bars);

    const CAM3 = -30;
    post.shock.center = post.zoom.center = { x: H3[0], y: H3[1] + CAM3 };

    this.S = {
      world, ui, dim, red, pieces, scars, blades, H3, PO, GROUND, CAM3, W, OX, OY, back, rim, ghosts, hero, fx,
      seams: seamsOf(scars),
      nameG, cap, capName, capMask, nameGhosts, nameT, nameMask, imp, impBg, impLines, silB, silW, flash, bars, ...post
    };

  }

  frame(t) {
    const S = this.S;
    // Three hit stops, at logical times L1–L3 for 70, 70 and 160 ms of real time: real time keeps running (shake,
    // impact frames) while everything else holds. HR are the real times they start.
    const L1 = 1624, L2 = 1875, L3 = 2127, HS = [[L1, 70], [L2, 70], [L3, 160]], HR = realTimes(HS), REL3 = HR[2] + HS[2][1];
    const l = logicalTime(t, HS);

    // The chain, one beat per pose. The heaviest cut comes last and ends low, so the sword can go straight home from there.
    const DASH = 1400, DRAW = 1540, SPIN = 1666, SM3 = 1750, RISE = 1834, WIND = 1917, SM2 = 2001, P8 = 2085, CAM3 = S.CAM3;
    const NAME = 2427, INTO = 2747, HOME = 2947, FLARE = 2977, SPLIT = 3157, RED0 = 4700, RED1 = 5300, EXIT = 6750, END = 7170;

    // Letterbox and dimming.
    framing(S,t,l,{height:118,barIn:[0,280],barOut:[EXIT+120,EXIT+500],dimIn:[[.42,0,300],[.4,600,1300],[-.4,RED0,RED1]],dimOut:[EXIT,END]});

    // Backdrop: whole through the combo; after the zanshin the scars flare in cut order, then the sky breaks along all
    // three and every piece slides off, faster and faster, and dissolves.
    const sp = k(SPLIT, SPLIT + 850, l), e = sp * sp, drift = -l * 0.006;
    const fade = 1 - inq(k(SPLIT + 400, SPLIT + 1000, l));
    scatter(S.pieces, e, drift, 0, 0.95 * out(k(600, 1250, l)) * fade);
    bloodSky(S.red, l, SPLIT, RED0, RED1, drift);

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
    const [sx, sy] = shakes(t, [[HR[0], 14, 220, [1, 0]], [HR[1], 16, 220, [0.42, -0.91]], [P8 + 140, 8, 140],
      [REL3, 32, 520, [0.6, 0.8]], [HOME + 300, 7, 130], [SPLIT + 300 + 120, 5, 160]]);
    aim(S, Fp, [Fp[0] + sx, Fp[1] + sy + camY], cam, rot);

    // Cut 3's effects: one white frame, two impact frames, then the shockwave, radial blur and colour split at its tip.
    whiteFlash(S.flash,l,P8,L3);
    impactFrame(S,t,HR[2],()=>S.impLines.position.set(S.H3[0],S.H3[1]));
    strikeFilters(S,t,l,{hit:L3,release:REL3,amplitude:54,zoom:.17,split:13});

    // Name: cut out from left to right, with red afterimages.
    revealName(S,l,{start:NAME,end:NAME+260,ghostIn:NAME+50,ghostOut:NAME+180,ghostEnd:NAME+570,exit:EXIT,finish:END,x:30,y:420});

    // Entry caption: the name wipes in at the hero's feet; the dash's wind sweeps it off.
    caption(S,l,{start:420,revealStart:560,revealEnd:860,exitStart:1450,exitEnd:1570,x:1060,y:900});

    this.drawFx(l, { L1, L2, L3, DASH, WIND, SM2, SPIN, INTO, HOME, FLARE, SPLIT, END, hx, drift, fade, charge: keyAt === 'charge' && l < DASH ? hx : null });
    S.fx.update(); S.back.update();
  }

  // Slashes, scars, particles, in world coordinates. Two additive canvases: one behind the hero (scars, the far half
  // of the spin loop, the draw leaving the scabbard), one in front.
  drawFx(l, T) {
    const S = this.S, palette = this.palette, g = S.fx.g, gb = S.back.g;
    const {glint, slash, tipSparks} = drawing(g, gb, palette);
    beginFx([g, gb], [1, 0, 0, 1, S.OX, S.OY]);

    // Charge: sparks are pulled into the hilt while the hero holds the stance; a glint on the hilt before the dash.
    if (T.charge && l > 650) {
      const hilt = S.W('charge', [1181, 417], T.charge);
      charge(g, l, {hilt, span: 620, count: 90, color: palette.charge});
      glint(hilt[0], hilt[1], k(1240, 1390, l), 100);
    }
    // Dash: a dust streak along the ground behind the hero.
    if (l >= T.DASH && l < 1800) {
      const p = k(T.DASH, 1540, l), a = 1 - k(1560, 1800, l);
      for (let s = 0; s < 6; s++) {
        const y = 945 + s * 9, x1 = 656 + 64 * p + 160, x0 = 160 + rnd(s, 40) * 120 + 300 * k(1540, 1800, l);
        g.strokeStyle = 'rgba(' + palette.dust[0] + ',' + (palette.dust[1] + s * 15) + ',' + palette.dust[2] + ',' + (0.35 * a * (1 - s / 7)) + ')'; g.lineWidth = 2 + (s % 2);
        g.beginPath(); g.moveTo(x0, y); g.lineTo(x1, y - 4); g.stroke();
      }
    }
    // The raised blade glints once as it comes over.
    glint(...S.W('wind', [556, 182]), k(T.WIND - 20, T.SM2 - 20, l), 70);

    // The slashes: "da, da, boom". Each head reaches its blade tip on the frame the finishing pose appears.
    const B = S.blades;
    if (l >= 1545 && l < T.L1 + 420) slash(B[0], {head: out(k(1545, 1620, l)), tail: 0.35 * out(k(1570, 1660, l)), head2: out(k(1560, 1635, l)), width: 60, fray: k(T.L1, T.L1 + 420, l), seed: 21});
    if (l >= T.SPIN && l < T.L2 + 160) slash(B[1], {head: k(T.SPIN, 1829, l), tail: 0.45 * out(k(1724, 1799, l)) + 0.23 * out(k(1789, 1854, l)), head2: k(T.SPIN + 20, 1844, l), width: 72, fray: k(T.L2, T.L2 + 160, l), seed: 31});
    if (l >= 1885 && l < T.SM2 + 60) slash(B[4], {head: k(1885, T.SM2 - 10, l), tail: k(1915, T.SM2 + 40, l), head2: k(1885, T.SM2 - 10, l), width: 22, fray: 0, seed: 71, alpha: 0.7});
    if (l >= T.SM2 + 4 && l < T.L3 + 520) slash(B[2], {head: out(k(T.SM2 + 4, 2081, l)), tail: 0.3 * out(k(T.SM2 + 8, 2081, l)), head2: out(k(T.SM2 + 16, 2096, l)), width: 92, fray: k(T.L3, T.L3 + 520, l), seed: 41});
    if (l >= T.INTO - 120 && l < T.INTO + 110) slash(B[3], {head: k(T.INTO - 120, T.INTO, l), tail: k(T.INTO - 80, T.INTO + 80, l), head2: k(T.INTO - 120, T.INTO, l), width: 22, fray: 0, seed: 81, alpha: 0.75});
    tipSparks(B[0], l, T.L1, 70, 0.7, 10); tipSparks(B[1], l, T.L2, 70, 0.7, 50); tipSparks(B[2], l, T.L3, 160, 1, 60);

    const open = out(k(T.SPLIT, T.SPLIT + 260, l));
    flareScars(gb, l, palette, {scars: S.scars, hits: [T.L1, T.L2, T.L3], split: T.SPLIT, flare: T.FLARE, step: 70, dim: [0.75, T.L3 + 200, T.L3 + 500], drift: T.drift, open});
    if (l >= T.SPLIT) pieceLips(gb, S.pieces, S.scars, palette, T.fade * open);

    // The bare steel catches the light as it slides home; a hard glint on the guard marks the click, and then the sky breaks.
    steelGlint(g, l, T.INTO, T.HOME, S.W('into', [850, 394], T.hx), S.W('into', [1030, 394], T.hx));
    glint(...S.W('home', [908, 392], T.hx), k(T.HOME, T.HOME + 200, l), 120);
    seamEmbers(g, l, {start: T.SPLIT, span: 1300, count: 160, seams: S.seams, color: palette.seamEmber, drift: T.drift});
    risingEmbers(g, l, {from: T.L3 + 240, to: T.END, color: palette.ember});
  }

}
