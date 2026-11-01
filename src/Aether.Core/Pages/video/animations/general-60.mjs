import {cutCells,canvasOf,k,out,inq,quint,smooth,on2,rnd,trace,clipTo,spline,pathOf,reach as extend,chordOf,onScreen,dense,at as placePoints,textures,drawing,shakeAt,stroke as strokePath,sparks as emitSparks,slash as sharedSlash} from './toolkit.mjs';
import {createName, createCaption,pieces as makePieces,framing,caption,revealName} from './scenes.mjs';

export class Moon {
  constructor({app,canvas,images,character,nickname}) {
    const P=PIXI,F=P.filters;
    this.character=character;
    const {tex,silhouette,radial,dispose}=textures(P);
    this.disposeTextures=dispose;
    const [oni1Img, oni2Img, oni3Img, oni4Img, moon, impact, moonRed, day] = images.slice(20);

    // The action chain. Every pose is placed by its hips (hip, in its own image's pixels) at the scale s that makes the blade
    // the same length, and may be turned by rot. AT is where the hips sit in the world (the ground is y = 1180, the moon hangs
    // high on the right). The images hold the figure only: the light is drawn here, along the path of the blade tip. He
    // starts on the left facing the moon, bursts up off the ground and climbs the sky left of the moon in four more cuts, all
    // five afterimages strike at once, he hangs at the top and sheathes, then dives through the moon with the oni.
    const keys = ['charge', 'smear1', 'cut1', 'smear2', 'cut2', 'smear3', 'cut3', 'smear4', 'cut4', 'smear5', 'cut5', 'noto', 'iai', 'cutin', 'smear6', 'cut6', 'turn', 'slam', 'fall', 'slamin'];
    // MEASURED: points on the poses and the oni, in their own image pixels.
    const THR = -0.6, DIVE = 0.3;
    const placements = {charge:{s:0.66},smear1:{s:0.78},cut1:{s:0.8},smear2:{s:0.654},cut2:{s:0.654},smear3:{s:0.92},cut3:{s:0.855},smear4:{s:0.694,rot:THR},cut4:{s:0.705,rot:THR},smear5:{s:0.737},cut5:{s:0.737},noto:{s:0.7},iai:{s:0.7},cutin:{s:1},smear6:{s:0.787,rot:.45},cut6:{s:0.648},turn:{s:0.71},slam:{s:0.7},fall:{s:0.62},slamin:{s:1}};
    const M = Object.fromEntries(Object.entries(character.anchors.moon).map(([key,points])=>[key,{...points,...placements[key]}]));
    const PO = {};
    keys.forEach((key, i) => { PO[key] = { img: images[i], tex: tex(images[i]), hip: M[key].hip, s: M[key].s, rot: M[key].rot || 0 }; });
    const GROUND = 1180, onGround = (key, x) => [x, GROUND - (M[key].sole - M[key].hip[1]) * M[key].s];
    const SKY = 2, MR = 192, MOON_R = MR * SKY, MC = [1006, 292], MOON = [2000, -280];
    // The moon cut's line runs through the moon's centre, from the hover at the upper left to the exit at the lower right.
    const U6 = [Math.cos(DIVE), Math.sin(DIVE)], N6 = [U6[1], -U6[0]], ANG6 = Math.atan2(U6[1], U6[0]);
    const onCut = d => [MOON[0] + U6[0] * d, MOON[1] + U6[1] * d];
    const AT = { charge: onGround('charge', 720), cut1: [770, 640], cut2: [860, 470], cut3: [800, -20], cut4: [860, -240], cut5: [860, -480], noto: [840, -605], iai: [840, -605] };
    const W = (key, q, hip = AT[key]) => { const p = PO[key], x = (q[0] - p.hip[0]) * p.s, y = (q[1] - p.hip[1]) * p.s, c = Math.cos(p.rot), n = Math.sin(p.rot); return [hip[0] + x * c - y * n, hip[1] + x * n + y * c]; };
    const onto = (key, q, tip) => { const a = W(key, q, [0, 0]); return [tip[0] - a[0], tip[1] - a[1]]; };
    // A mid-cut pose and the pose that finishes its cut share the blade tip, except the wheel and the gathered thrust, whose
    // blades are elsewhere: those two are placed by hand.
    const [TIP1, TIP2, TIP3, TIP4, TIP5] = [1, 2, 3, 4, 5].map(i => { const tip = W('cut' + i, M['cut' + i].tip); AT['smear' + i] = onto('smear' + i, M['smear' + i].tip, tip); return tip; });
    Object.assign(AT, { smear3: [800, 40], smear4: [860, -60] });
    // He leaves the moon with his blade tip on the moon cut.
    const T6 = onCut(MOON_R + 800); AT.cut6 = onto('cut6', M.cut6.tip, T6);
    // He stays where the dash ended: the flick, the slam and the fall all start from there.
    Object.assign(AT, { turn: [AT.cut6[0] + 10, AT.cut6[1] + 10], slam: [AT.cut6[0] + 10, AT.cut6[1] - 20], fall: [AT.cut6[0] + 40, AT.cut6[1] - 20] });
    const MOUTH0 = W('iai', M.iai.mouth), TIP8 = W('turn', M.turn.tip);
    const ringOf = (key, [cx, cy, a, b], from, to, n = 70) => Array.from({ length: n + 1 }, (_, i) => { const th = from + (to - from) * i / n; return { p: W(key, [cx + a * Math.cos(th), cy + (b ?? a) * Math.sin(th)]), th }; });
    // Catmull-Rom through the measured points, about every 10 px; seg says which span each point came from.

    // A polyline walked by arc length: at(u) gives the point, the normal and whether that stretch passes behind the hero.

    const slashPath = (pts, behind = []) => { const s = spline(pts); return pathOf(s.pts, s.seg.map(i => behind.includes(i))); };
    const at = (key,list) => placePoints(W,key,list);

    // 1 the rising draw, out of the scabbard mouth, forward and then steeply up. 2 the level ring, whose far half passes behind
    // him. 3 the wheel: the blade goes straight on down from the ring, right round him and over to the lower right. 4 the
    // straight thrust out of the gathered stance. 5 the corkscrew, which starts at the thrust's tip and winds up round his
    // body. 6 and 7 the moon cut, his and the oni's, along the dive. 8 the flick. 9 the thin line as the blade drops from the
    // top of the climb to his hip for the sheathing.
    const ring2 = ringOf('smear2', M.smear2.ring, 0, 2 * Math.PI), ring3 = ringOf('smear3', M.smear3.ring, 0.2, 6.98);
    const [hx0, ha, hy0, hy1, turns] = M.smear5.helix;
    const helix = Array.from({ length: 121 }, (_, i) => { const f = i / 120, th = f * turns * 2 * Math.PI; return { p: W('smear5', [hx0 + ha * (0.7 + 0.3 * f) * Math.cos(th), hy0 + (hy1 - hy0) * f]), back: Math.sin(th) < 0 }; });
    const M1 = W('smear1', M.smear1.mouth);
    const blades = [
      slashPath([M1, [M1[0] + 270, M1[1] + 24], [TIP1[0] - 60, TIP1[1] + 330], TIP1]),
      pathOf([...ring2.map(q => q.p), TIP2], [...ring2.map(q => Math.sin(q.th) < 0), false]),
      pathOf([...ring3.map(q => q.p), TIP3], [...ring3.map(q => q.th > 1.6 && q.th < 5), false]),
      slashPath([W('smear4', M.smear4.tip), TIP4]),
      pathOf([TIP4, ...helix.map(q => q.p), TIP5], [false, ...helix.map(q => q.back), false]),
      slashPath([MOUTH0, onCut(-MOON_R - 260), onCut(-MOON_R), MOON, onCut(MOON_R), T6])
    ];

    // The cuts a straight blade leaves are straight: each is the chord of the stretch of its slash still lit on the frame
    // it lands (from the slash's tail to the blade tip), run right across the sky.
    const C0 = [960, 540], RR = 6000;
    const reach = (p,q) => extend(p,q,6000);

    const TAILS = [0.45, 0.76, 0.6, 0.2, 0.86, 0.5];

    const scars = [0, 1, 2, 3, 4].map(i => chordOf(blades[i], TAILS[i],6000));
    scars.push([reach(onCut(-100), onCut(100)), reach(onCut(100), onCut(-100))]);

    // The oni mirrors the hero about twice as large. In the stance its hands are on his. Its dash runs along the moon cut,
    // its tip beyond his, and it stops past the moon with the hero in front of it; it slams the sword home where it stopped.
    const SO = 1.26;
    const rotv = (v, a) => [v[0] * Math.cos(a) - v[1] * Math.sin(a), v[0] * Math.sin(a) + v[1] * Math.cos(a)];
    const OTIP = onCut(MOON_R + 960), ROT_O = 0.5;
    const ONI = {
      oni1: { img: oni1Img, anchor: M.oni1.tsuba, at: W('iai', M.iai.tsuba), rot: 0 },
      oni2: { img: oni2Img, anchor: M.oni2.tip, at: OTIP, rot: ROT_O },
      oni3: { img: oni3Img, anchor: M.oni3.tip, at: OTIP, rot: 0 }
    };
    const OW = (o, q) => { const v = rotv([(q[0] - o.anchor[0]) * SO, (q[1] - o.anchor[1]) * SO], o.rot); return [o.at[0] + v[0], o.at[1] + v[1]]; };
    ONI.oni4 = { img: oni4Img, anchor: M.oni4.head, at: [AT.slam[0] - 340, AT.slam[1] - 470], rot: 0 };
    for (const o of Object.values(ONI)) o.tex = tex(o.img);
    blades.push(slashPath([[MOUTH0[0] + N6[0] * 300, MOUTH0[1] + N6[1] * 300], (d => [d[0] + N6[0] * 140, d[1] + N6[1] * 140])(onCut(-MOON_R - 320)), (d => [d[0] + N6[0] * 50, d[1] + N6[1] * 50])(onCut(-MOON_R)), MOON, onCut(MOON_R + 300), OTIP]));
    blades.push(slashPath([T6, [T6[0] + 40, T6[1] - 150], TIP8]));
    blades.push(slashPath([TIP5, [TIP5[0] - 30, TIP5[1] + 240], W('noto', M.noto.tsuba)]));
    // The afterimages strike together from where they hang: the impact lines burst from their middle.
    const SC = [860, 40];

    // The six cuts run right across the sky, so it breaks along the very lines the viewer saw land. Each cell is a big
    // disc clipped by one side of each line; every cell the wide shot can see is kept, however thin.
    const VIEW = [-800, -1500, 3700, 1500];
    const cells = cutCells(scars,C0,RR,VIEW);

    // The sky is the moonlit night, scaled so the moon sits at MOON, high on the right. The moon is lifted out into two
    // halves split along the moon cut (the sky under it is painted dark), so the halves can part. The sky is padded for
    // the wide shots and swings: to the left by tiling its moonless left strip, to the right by a mirror
    // (the mirrored moon stays outside the pad), above and below by its edge strips flipped and stretched.
    const PADL = 2100, PADR = 300, PADV = 340, STRIP = 620, OV = 200;
    const noMoon = (img, rgb) => canvasOf(img.width, img.height, g => {
      g.drawImage(img, 0, 0);
      const r = g.createRadialGradient(MC[0], MC[1], 0, MC[0], MC[1], MR + 44); r.addColorStop(0, 'rgba(' + rgb + ',1)'); r.addColorStop((MR + 8) / (MR + 44), 'rgba(' + rgb + ',1)'); r.addColorStop(1, 'rgba(' + rgb + ',0)');
      g.fillStyle = r; g.fillRect(MC[0] - MR - 44, MC[1] - MR - 44, 2 * MR + 88, 2 * MR + 88);
    });
    const DR = MR + 36;
    const discOf = img => canvasOf(2 * DR, 2 * DR, g => {
      g.drawImage(img, MC[0] - DR, MC[1] - DR, 2 * DR, 2 * DR, 0, 0, 2 * DR, 2 * DR);
      g.globalCompositeOperation = 'destination-in';
      const r = g.createRadialGradient(DR, DR, MR + 4, DR, DR, DR); r.addColorStop(0, 'rgba(0,0,0,1)'); r.addColorStop(1, 'rgba(0,0,0,0)');
      g.fillStyle = r; g.fillRect(0, 0, 2 * DR, 2 * DR);
    });
    const padded = img => {
      const w = img.width + PADL + PADR, h = img.height;
      const row = canvasOf(w, h, g => {
        g.drawImage(img, PADL, 0);
        g.save(); g.translate(PADL + 2 * img.width, 0); g.scale(-1, 1); g.drawImage(img, 0, 0); g.restore();
        // Tiles of its moonless left side, not mirrored (a mirror reads as a butterfly in the wide shots), each fading into the next.
        const tile = canvasOf(STRIP + OV, h, c => {
          c.drawImage(img, 0, 0, STRIP + OV, h, 0, 0, STRIP + OV, h);
          const r = c.createLinearGradient(STRIP, 0, STRIP + OV, 0); r.addColorStop(0, 'rgba(0,0,0,1)'); r.addColorStop(1, 'rgba(0,0,0,0)');
          c.globalCompositeOperation = 'destination-in'; c.fillStyle = r; c.fillRect(0, 0, STRIP + OV, h);
        });
        for (let x = PADL - STRIP; x > -STRIP; x -= STRIP) g.drawImage(tile, x, 0);
      });
      return canvasOf(w, h + 2 * PADV, g => {
        g.drawImage(row, 0, PADV);
        g.save(); g.translate(0, PADV); g.scale(1, -1); g.drawImage(row, 0, 0, w, 100, 0, 0, w, PADV); g.restore();
        const top = g.createLinearGradient(0, 0, 0, PADV + 60); top.addColorStop(0, 'rgba(6,8,13,1)'); top.addColorStop(1, 'rgba(6,8,13,0)'); g.fillStyle = top; g.fillRect(0, 0, w, PADV + 60);
        g.save(); g.translate(0, h + 2 * PADV); g.scale(1, -1); g.drawImage(row, 0, h - 100, w, 100, 0, 0, w, PADV); g.restore();
      });
    };
    const moonTex = tex(padded(moon)), redTex = tex(padded(noMoon(moonRed, '40,4,8'))), dayTex = tex(day);
    const redDisc = tex(discOf(moonRed));
    const shadeTex = radial([[0, 'rgba(0,0,0,0)'], [0.7, 'rgba(0,0,0,0.35)'], [1, 'rgba(0,0,0,0.8)']]);
    const lightShadeTex = radial([[0, 'rgba(0,0,0,0)'], [0.75, 'rgba(0,0,0,0.12)'], [1, 'rgba(0,0,0,0.4)']]);
    const skyOf = (t, parent, tint = 0xffffff, shadeT = shadeTex) => {
      const sky = new P.Sprite(t); sky.scale.set(SKY); sky.position.set(MOON[0] - (PADL + MC[0]) * SKY, MOON[1] - (PADV + MC[1]) * SKY); sky.tint = tint; parent.addChild(sky);
      const shade = new P.Sprite(shadeT); shade.anchor.set(0.5); shade.position.set(MOON[0] - 700, 400); shade.width = 5600; shade.height = 3800; parent.addChild(shade);
      return sky;
    };
    // A moon half: the disc clipped to one side of the moon cut, in a container pivoting on the moon's centre.
    const half = (t, up, tint) => {
      const c = new P.Container(), L = DR * SKY * 1.2, n = up ? N6 : [-N6[0], -N6[1]];
      const m = new P.Graphics().poly([-U6[0] * L, -U6[1] * L, U6[0] * L, U6[1] * L, U6[0] * L + n[0] * L, U6[1] * L + n[1] * L, -U6[0] * L + n[0] * L, -U6[1] * L + n[1] * L]).fill(0xffffff);
      const s = new P.Sprite(t); s.anchor.set(0.5); s.scale.set(SKY); s.tint = tint;
      c.addChild(m); s.mask = m; c.addChild(s); c.position.set(...MOON);
      return c;
    };

    const stage = app.stage;
    const world = new P.Container(); stage.addChild(world);
    const dim = new P.Sprite(radial([[0, 'rgba(6,6,12,0.55)'], [0.6, 'rgba(4,4,8,0.85)'], [1, 'rgba(0,0,0,1)']]));
    dim.anchor.set(0.5); dim.position.set(960, 540); dim.width = 6400; dim.height = 4400; world.addChild(dim);
    // The moonlit night the stream sees first, until the stance's ink has dyed it red. Under the blood-moon world, the
    // real world in daylight: whole, never cut, shown only once the blood-moon world breaks away.
    const base = new P.Container(); world.addChild(base); skyOf(moonTex, base);
    // It needs no moon to line up with, so it is simply scaled to fill the closing wide shot (placed below, once that is known).
    const dayLayer = new P.Container(); world.addChild(dayLayer);
    const daySky = new P.Sprite(dayTex); daySky.anchor.set(0.5); daySky.scale.set(2.05); daySky.tint = 0xe2e2e2; dayLayer.addChild(daySky);
    const dayShade = new P.Sprite(lightShadeTex); dayShade.anchor.set(0.5); dayShade.width = 3600; dayShade.height = 2200; dayLayer.addChild(dayShade);
    // The blood-moon world the stance opens, dimmed so the red never fills the frame at full strength. Until the
    // break it is one piece; at the break the same sky is cut into cells along the six cuts.
    const redWrap = new P.Container(); world.addChild(redWrap);
    const waveMask = new P.Graphics(); redWrap.addChild(waveMask);
    const redAll = new P.Container(); redWrap.addChild(redAll);
    const redWhole = new P.Container(); redAll.addChild(redWhole); skyOf(redTex, redWhole, 0x6c6c6c);
    const pieceLayer = new P.Container(); redAll.addChild(pieceLayer);
    // Pieces are blasted out from the hero's hands as he slams the sword home.
    const MOUTH2 = W('slam', M.slam.mouth), BURST = MOUTH2;
    const pieces = makePieces(P,cells,pieceLayer,inner=>skyOf(redTex,inner,0x6c6c6c),BURST,{base:1000,max:600,factor:.4,rotation:.22,down:.6});
    const redHalves = [half(redDisc, true, 0xc8c8c8), half(redDisc, false, 0xc8c8c8)]; redAll.addChild(...redHalves);

    // The oni stands in front of the sky and behind the hero.
    const oniWrap = new P.Container(); world.addChild(oniWrap);
    const oniMask = new P.Graphics(); oniWrap.addChild(oniMask);
    const oni = new P.Sprite(ONI.oni1.tex); oni.tint = 0xd0c0c0; oniWrap.addChild(oni); oni.mask = oniMask;

    // Effects canvases follow the camera (the screen plus a margin for the shake), drawn in world units.
    const FM = 220, FW = 1920 + 2 * FM, FH = 1080 + 2 * FM;
    const backC = document.createElement('canvas'); backC.width = FW; backC.height = FH;
    const backTex = tex(backC), backSprite = new P.Sprite(backTex); backSprite.blendMode = 'add'; world.addChild(backSprite);

    // Each cut leaves an afterimage of ink where it finished; they all strike together, then melt into the oni.
    const inkOf = img => tex(canvasOf(img.width, img.height, g => { g.drawImage(img, 0, 0); g.globalCompositeOperation = 'color'; g.fillStyle = '#c01424'; g.fillRect(0, 0, img.width, img.height); g.globalCompositeOperation = 'multiply'; g.fillStyle = '#8a7878'; g.fillRect(0, 0, img.width, img.height); g.globalCompositeOperation = 'destination-in'; g.drawImage(img, 0, 0); }));
    const shades = [1, 2, 3, 4, 5].map(i => { const key = 'cut' + i, p = PO[key], st = new P.Sprite(inkOf(p.img)); st.pivot.set(...p.hip); st.scale.set(p.s); st.rotation = p.rot; st.position.set(...AT[key]); st.alpha = 0; st.tint = 0xff7070; world.addChild(st); return st; });

    const rim = new P.Sprite(radial([[0, 'rgba(255,60,70,0.55)'], [0.45, 'rgba(200,20,40,0.18)'], [1, 'rgba(0,0,0,0)']]));
    rim.anchor.set(0.5); rim.width = 1100; rim.height = 1100; world.addChild(rim);
    const ghosts = [0, 1].map(() => { const s = new P.Sprite(PO.charge.tex); s.alpha = 0; world.addChild(s); return s; });
    const hero = new P.Sprite(PO.charge.tex); hero.tint = 0xe0e0e0; world.addChild(hero);

    const fx = document.createElement('canvas'); fx.width = FW; fx.height = FH;
    const fxTex = tex(fx), fxSprite = new P.Sprite(fxTex); fxSprite.blendMode = 'add'; world.addChild(fxSprite);

    const ui = new P.Container(); stage.addChild(ui);
    // Entry caption: says who is coming in from the cut-in, until the take-off's wind sweeps it away. It sits on the
    // ground in front of the hero and is drawn larger, since the opening shot is wide.
    const cap = new P.Container(); cap.position.set(1180, 1118); cap.scale.set(1.5); ui.addChild(cap);
    const {capName,capMask}=createCaption(P,cap,nickname,620);
    // The name, cut out in the daylight beside the hero; a soft dark shadow keeps the white type readable on the bright sky.
    const nameG = new P.Container(); nameG.rotation = -0.05; ui.addChild(nameG);
    const {nameGhosts,nameT,nameMask}=createName(P,nameG,nickname,character.palette.nameGhost,{color:0x000000,alpha:.85,blur:18,distance:0});

    // Three sets of impact frames: the afterimages' strike (every afterimage and the cuts), the moon cut (the oni in grey
    // behind the hero, the cut across the moon) and the slam (the oni, the hero sheathing, every cut at once).
    const imp = new P.Container(); world.addChild(imp);
    const impBg = new P.Graphics().rect(-4000, -4000, 12000, 10000).fill(0xffffff); imp.addChild(impBg);
    const impLines = new P.Sprite(tex(impact)); impLines.anchor.set(0.5); impLines.width = impLines.height = 9000; imp.addChild(impLines);
    const impG = new P.Graphics(); imp.addChild(impG);
    const silOf = (img, place) => { const sb = new P.Sprite(silhouette(img, '#000')), sw = new P.Sprite(silhouette(img, '#fff')); for (const s of [sb, sw]) { place(s); imp.addChild(s); } return [sb, sw]; };
    const placeHero = (key, hipAt) => s => { s.pivot.set(...PO[key].hip); s.scale.set(PO[key].s); s.rotation = PO[key].rot; s.position.set(...hipAt); };
    const placeOni = o => s => { s.pivot.set(...o.anchor); s.scale.set(SO); s.rotation = o.rot; s.position.set(...o.at); s.alpha = 0.5; };
    // A slightly larger silhouette in the opposite colour sits under the hero, so he never melts into the lines.
    const halo = (key, hipAt) => silOf(PO[key].img, s => { placeHero(key, hipAt)(s); s.scale.set(PO[key].s * 1.03); }).reverse();
    const sils = {
      shade: [1, 2, 3, 4, 5].map(i => silOf(PO['cut' + i].img, placeHero('cut' + i, AT['cut' + i]))),
      cut: [silOf(oni3Img, placeOni(ONI.oni3)), halo('cut6', AT.cut6), silOf(PO.cut6.img, placeHero('cut6', AT.cut6))],
      click: [silOf(oni4Img, placeOni(ONI.oni4)), halo('slam', AT.slam), silOf(PO.slam.img, placeHero('slam', AT.slam))]
    };

    const flash = new P.Graphics().rect(-4000, -4000, 12000, 10000).fill(0xffffff); world.addChild(flash);
    // The cut-in: a close shot of the left hip as the thumb pushes the guard out.
    const cutin = new P.Container(); stage.addChild(cutin);
    const cutBg = new P.Sprite(redTex); cutBg.tint = 0x3a3a3a; cutBg.scale.set(3.2); cutBg.position.set(-1500, -2600); cutin.addChild(cutBg);
    cutBg.filters = [new F.KawaseBlurFilter({ strength: 18, quality: 4 })];
    const cutPose = new P.Sprite(PO.cutin.tex); cutPose.anchor.set(0.5); cutPose.position.set(960, 540); cutin.addChild(cutPose);
    const cutFx = new P.Graphics(); cutin.addChild(cutFx);
    const bars = [new P.Graphics(), new P.Graphics()]; stage.addChild(...bars);

    const bloom = new F.AdvancedBloomFilter({ threshold: 0.9, bloomScale: 1.15, brightness: 1, blur: 7, quality: 5 });
    const shock = new F.ShockwaveFilter({ center: { x: 0, y: 0 }, speed: 1500, amplitude: 54, wavelength: 240, brightness: 1.15, radius: -1 });
    const shock2 = new F.ShockwaveFilter({ center: { x: 0, y: 0 }, speed: 1100, amplitude: 30, wavelength: 420, brightness: 1.08, radius: -1 });
    const zoom = new F.ZoomBlurFilter({ strength: 0, center: { x: 0, y: 0 }, innerRadius: 60 });
    const rgb = new F.RGBSplitFilter({ red: { x: 0, y: 0 }, green: { x: 0, y: 0 }, blue: { x: 0, y: 0 } });

    // Camera framings: the wide shot of the dive, from the hover to past the moon (the sky breaks in it too), and the shot
    // the oni rises in, up to its horns.
    const WIDE = [1950, -230, 0.7], RISECAM = [1100, -556, 0.66], CLOSE = [2330, 100, 0.82];
    for (const s of [daySky, dayShade]) s.position.set(CLOSE[0], CLOSE[1] - 40);
    // The name sits in the closing wide shot, in the open sky to the left of the falling hero.
    nameG.position.set(CLOSE[0] + (260 - 960) / CLOSE[2], CLOSE[1] + (470 - 540) / CLOSE[2]); nameG.scale.set(1.4);

    this.S = {
      app, canvas, cutBg, world, ui, dim, base, dayLayer, shades, redWrap, waveMask, redAll, redWhole, pieceLayer, pieces, redHalves, oniWrap, oniMask, oni, ONI, OW, SO, M,
      scars, blades, TAILS, PO, AT, W, GROUND, SC, T6, OTIP, MOUTH2, MOUTH0, BURST, MOON, MOON_R, U6, N6, ANG6, WIDE, RISECAM, CLOSE, backC, backTex, backSprite, rim, ghosts, hero, fx, fxTex, fxSprite, FM,
      emberSrc: scars.map(s => pathOf(dense(s,1200).filter(p=>onScreen(p,VIEW)))),
      cap, capName, capMask, nameG, nameGhosts, nameT, nameMask,
      imp, impBg, impLines, impG, sils, flash, cutin, cutPose, cutFx, bars, bloom, shock, shock2, zoom, rgb
    };

  }

  frame(t) {
    const S=this.S;
    // Hit stops, at logical times: five cuts up the sky, faster and faster; the afterimages' strike; the moon cut (the
    // longest, 240 ms); the slam (the close-up, two impact frames, then everything holds). HR are their real times.
    const SM1 = 1500, L1 = 1584, SM2 = 1690, L2 = 1774, SM3 = 1840, L3 = 1924, SM4 = 1950, L4 = 2034, SM5 = 2040, L5 = 2124, SMS = 2206, LS = 2290, LAND = LS;
    const NOTO = 2360, IAI = 2640, ONI0 = 2660, ONI1 = 3160, CUTIN = 3220, CUTOUT = 3470, SM6 = 3580, HIT6 = 3664, L6 = 3706;
    const RISE2 = 4260, SLAM0 = 4600, CLICK = 4690, SHATTER = 4692, FALL = 4900, TITLE = 5100, BACK0 = 6950, BACK1 = 7550, EXIT = 7650, END = 8050;
    const HS = [[L1, 90], [L2, 60], [L3, 50], [L4, 50], [L5, 60], [LS, 130], [L6, 240], [CLICK, 234]];
    const HR = []; { let a = 0; for (const [s0, d] of HS) { HR.push(s0 + a); a += d; } }
    const REL5 = HR[5] + 130, REL6 = HR[6] + 240, REL7 = HR[7] + 234;
    let l = null, acc = 0;
    for (const [s, d] of HS) { if (t < s + acc) break; if (t < s + acc + d) { l = s; break; } acc += d; }
    if (l === null) l = t - acc;

    const mix = (a, b, p) => a + (b - a) * p;

    // Letterbox and dimming.
    framing(S,t,l,{height:118,barIn:[0,280],barOut:[EXIT+120,EXIT+500],dimIn:[[.42,0,300],[.4,600,1300],[-.4,BACK0,BACK1]],dimOut:[EXIT,END]});

    // The night fades in as he slides into the stance; his blade hand presses down and the blood-moon world opens from his feet.
    const back = (1 - inq(k(BACK0, BACK1, l)));
    // The moon is far off: as he climbs it hardly moves, so on the ground it hangs lower in the world and stays in the opening shot.
    const skyY = 420 * (1 - out(k(SM1, SM4, l)));
    S.base.position.y = skyY; S.redWhole.position.y = skyY;
    S.base.alpha = 0.95 * out(k(600, 1250, l)); S.base.visible = l < 1400;
    S.dayLayer.visible = l >= SHATTER - 2; S.dayLayer.alpha = back;
    const WAVE_O = [S.AT.charge[0], S.GROUND - 6];
    const wave = k(640, 1340, l);
    S.redWrap.visible = l >= 640;
    S.redAll.mask = wave < 1 ? S.waveMask : null; S.waveMask.visible = wave < 1;
    const waveR = 4200 * smooth(wave), wpts = [];
    for (let i = 0; i < 120; i++) { const a = i / 120 * Math.PI * 2, r = waveR * (1 + 0.05 * Math.sin(a * 7 + l * 0.003) + 0.035 * Math.sin(a * 17 - l * 0.005)); wpts.push(WAVE_O[0] + Math.cos(a) * r, WAVE_O[1] + Math.sin(a) * r); }
    S.waveMask.clear().poly(wpts).fill(0xffffff);
    // The moon cut leaves the blood moon split, the halves a hair apart; the slam blasts them away along the cut.
    const mb = out(k(SHATTER, SHATTER + 640, l)), mc = 6 * k(L6, L6 + 200, l);
    const halves = [[S.U6[0] * (mc + 700 * mb) + S.N6[0] * 260 * mb, S.U6[1] * (mc + 700 * mb) + S.N6[1] * 260 * mb, 0.5 * mb], [-S.U6[0] * 260 * mb - S.N6[0] * 380 * mb, -S.U6[1] * 260 * mb - S.N6[1] * 380 * mb, -0.4 * mb]];
    S.redHalves.forEach((h, i) => { h.position.set(S.MOON[0] + halves[i][0], S.MOON[1] + skyY + halves[i][1]); h.rotation = halves[i][2]; });
    // Then the blood-moon world bursts along the six cuts, blown out from the hero; the real world is there behind it, whole.
    const sp = k(SHATTER, SHATTER + 640, l), e = out(sp), drift = -Math.max(0, l - SHATTER) * 0.006;
    const fade = 1 - inq(k(SHATTER + 120, SHATTER + 400, l));
    S.redWhole.visible = l < SHATTER; S.pieceLayer.visible = l >= SHATTER;
    for (const p of S.pieces) {
      p.dx = p.dir[0] * p.dist * e + drift; p.dy = p.dir[1] * p.dist * e; p.r = p.rot * e;
      p.c.position.set(p.c.pivot.x + p.dx, p.c.pivot.y + p.dy); p.c.rotation = p.r; p.c.alpha = fade;
    }
    S.redHalves.forEach(h => { h.alpha = 1 - k(SHATTER + 120, SHATTER + 400, l); });
    // The afterimages each appear as he leaves them, flare as they strike together, and melt away towards him.
    const melt = k(IAI - 80, ONI0 + 300, l), shadeFrom = [SM2, SM3, SM4, SM5, NOTO], strike = Math.sin(Math.PI * k(SMS, LS + 160, l));
    S.shades.forEach((st, i) => { st.alpha = (l >= shadeFrom[i] ? 0.45 + 0.5 * strike : 0) * (1 - melt); });

    // Hero: one pose per beat, placed by its hips.
    const keyAt = l < SM1 ? 'charge' : l < L1 ? 'smear1' : l < SM2 ? 'cut1' : l < L2 ? 'smear2' : l < SM3 ? 'cut2' : l < L3 ? 'smear3' : l < SM4 ? 'cut3' : l < L4 ? 'smear4' : l < SM5 ? 'cut4' : l < L5 ? 'smear5'
      : l < NOTO ? 'cut5' : l < IAI ? 'noto' : l < SM6 ? 'iai' : l < HIT6 ? 'smear6' : l < RISE2 ? 'cut6' : l < SLAM0 ? 'turn' : l < FALL ? 'slam' : 'fall';
    const po = S.PO[keyAt];
    let [hx, hy] = S.AT[keyAt] || S.AT.iai;
    // The stance slides in from the left and settles lower through the charge.
    if (keyAt === 'charge') { hx -= 1100 * (1 - quint(k(250, 520, l))); hy += 16 * inq(k(640, SM1, on2(l))); }
    // The rising cut carries him on up; he drifts on through each hold.
    if (keyAt === 'cut1') { const q = out(k(L1, SM2, on2(l))); hx += 15 * q; hy -= 160 * q; }
    if (keyAt === 'noto' || keyAt === 'iai') hy += 6 * Math.sin(l * 0.004);
    // The dive: two smear frames along the moon cut, then he skids on into the finish.
    const dashAt = [0.42, 0.84], dashF = dashAt[l < SM6 + 42 ? 0 : 1];
    if (keyAt === 'smear6') { const a = S.W('iai', S.M.iai.tsuba), b = S.W('cut6', S.M.cut6.hip); hx = a[0] + (b[0] - a[0]) * dashF; hy = a[1] + (b[1] - a[1]) * dashF; }
    if (keyAt === 'cut6') { const q = 1 - out(k(HIT6, L6 + 160, l)); hx -= S.U6[0] * 90 * q; hy -= S.U6[1] * 90 * q; }
    if (keyAt === 'fall') hy += 90 * k(FALL, END, l);
    hx -= 70 * k(EXIT, END, l);
    const place = (s, p, x, y) => { s.texture = p.tex; s.pivot.set(p.hip[0], p.hip[1]); s.scale.set(p.s); s.rotation = p.rot; s.position.set(x, y); };
    place(S.hero, po, hx, hy);
    S.hero.visible = keyAt !== 'cutin';
    S.hero.alpha = k(250, 420, l) * (1 - k(EXIT, END, l));
    // Afterimages trail every smear frame, opposite to the motion.
    const gOff = l < 520 ? [-70, 0] : keyAt === 'smear1' ? [-20, 60] : keyAt === 'smear2' ? [-50, 30] : keyAt === 'smear3' ? [-40, 40] : keyAt === 'smear4' ? [-70, 48] : keyAt === 'smear5' ? [0, 50] : keyAt === 'smear6' ? [-S.U6[0] * 250, -S.U6[1] * 250] : null;
    S.ghosts.forEach((g, i) => {
      place(g, po, hx + (gOff ? (i + 1) * gOff[0] : 0), hy + (gOff ? (i + 1) * gOff[1] : 0));
      g.alpha = gOff && l >= 250 ? (i ? 0.16 : 0.32) : 0; g.tint = 0xff6070;
    });
    S.rim.position.set(hx, hy - 140);
    S.rim.alpha = (0.5 * out(k(400, 900, l)) + 0.5 * out(k(1100, SM1, l)) - 0.3 * k(LAND + 240, LAND + 940, l)) * (1 - k(SHATTER, SHATTER + 600, l));
    S.rim.scale.set((1100 + 300 * k(1100, SM1, l)) / 512);

    // The oni: it wells up out of the ink the afterimages melted into, holds the hero's stance, dives with him on the same
    // frames, slams its sword home with his and is blown apart with the world.
    const oKey = l < SM6 ? 'oni1' : l < HIT6 ? 'oni2' : l < SLAM0 ? 'oni3' : 'oni4', o = S.ONI[oKey];
    S.oni.texture = o.tex; S.oni.pivot.set(...o.anchor); S.oni.rotation = o.rot; S.oni.position.set(...o.at);
    const rise = k(ONI0, ONI1, l), gone = k(SHATTER, SHATTER + 420, l);
    S.oni.scale.set(S.SO * (1 + 0.1 * out(gone)));
    S.oniWrap.visible = l >= ONI0 && gone < 1;
    S.oniWrap.alpha = 0.8 * (1 - gone);
    S.oni.position.y -= 60 * (1 - out(rise));
    if (oKey === 'oni2') { const q = (1 - dashF) * 1500; S.oni.position.set(o.at[0] - S.U6[0] * q, o.at[1] - S.U6[1] * q); }
    // The ink edge rises from the bottom of the oni to above its horns.
    const ob = S.OW(S.ONI.oni1, [S.M.oni1.horn[0], S.M.oni1.bottom]), oh = S.OW(S.ONI.oni1, S.M.oni1.horn);
    const edgeY = mix(ob[1] + 80, oh[1] - 120, smooth(rise)), mpts = [];
    for (let x = -700; x <= 4300; x += 40) mpts.push(x, edgeY + 34 * Math.sin(x * 0.009 + l * 0.004) + 20 * Math.sin(x * 0.023 - l * 0.007));
    mpts.push(4300, 3000, -700, 3000);
    S.oniMask.clear().poly(mpts).fill(0xffffff);

    // Camera: a centre (cx, cy) in world units and a zoom. A low wide shot takes in the stance, the moon looming above and
    // the world turning red; it whips up with the launch and follows him up the sky, the moon always ahead of him. It pulls
    // far back for the afterimages' strike, settles on him at the top, tilts up to take in the oni, and the cut-in hides the
    // cut to the wide shot of the dive. The hit punches in hard on the moon and the skid, then eases back out.
    const WIDE = S.WIDE, RC = S.RISECAM;
    let cx = 1120, cy = 736, z = 0.88 + 0.03 * inq(k(520, SM1, l)), rot = -0.008 * inq(k(520, SM1, l)), p;
    let from = [1120, 736, 0.91];
    for (const [a, b, x, y, zz] of [[SM1, L1, 1060, 420, 0.92], [SM2, L2, 1100, 400, 0.92], [L2 + 1, SM3, 1150, -160, 0.82], [SM3, L3, 1120, -30, 0.92], [SM4, L4, 1140, -400, 0.88], [SM5, L5, 1100, -480, 0.82], [SMS, LS, 1200, -200, 0.58], [NOTO, NOTO + 200, 1040, -625, 0.95]]) {
      if (l < a) break;
      p = out(k(a, b, l)); cx = mix(from[0], x, p); cy = mix(from[1], y, p); z = mix(from[2], zz, p); rot = 0; from = [x, y, zz];
    }
    if (l >= IAI) { p = smooth(k(IAI, ONI1, l)); cx = mix(from[0], RC[0], p); cy = mix(from[1], RC[1], p); z = mix(from[2], RC[2], p) + 0.025 * inq(k(ONI1, CUTIN, l)); }
    if (l >= CUTOUT) { cx = WIDE[0]; cy = WIDE[1]; z = WIDE[2] + 0.008 * k(CUTOUT, SM6, l); }
    if (l >= HIT6) { p = 1 - smooth(k(L6, L6 + 700, l)); cx = mix(2300, 2400, p); cy = mix(-120, -40, p); z = mix(0.8, 0.95, p); }
    if (l >= SHATTER) { p = smooth(k(SHATTER, FALL + 200, l)); cx = mix(cx, S.CLOSE[0], p); cy = mix(cy, S.CLOSE[1], p); z = mix(z, S.CLOSE[2], p); p = 1 - out(k(SHATTER, SHATTER + 520, l)); cx = mix(cx, S.BURST[0], 0.3 * p); cy = mix(cy, S.BURST[1], 0.3 * p); z += 0.16 * p + 0.015 * k(TITLE, END, l); }
    let sx = 0, sy = 0;
    // Shake runs on real time. With a direction it mostly moves along the cut.
    const shake = (...args) => {const d=shakeAt(t,...args);sx+=d[0];sy+=d[1];};
    shake(HR[0], 24, 300, [0, -1]); shake(HR[1], 12, 170, [1, 0]); shake(HR[2], 14, 170, [0.65, 0.76]); shake(HR[3], 16, 190, [0.82, -0.56]); shake(HR[4], 18, 200, [0, -1]);
    shake(REL5, 30, 420); shake(REL6, 46, 660, S.U6); shake(REL6 + 180, 22, 320, [0, 1]); shake(REL7, 44, 620);
    for (const c of [S.world, S.ui]) { c.pivot.set(cx, cy); c.scale.set(z); c.rotation = rot; c.position.set(960 + sx, 540 + sy); }
    const toScreen = q => { const x = (q[0] - cx) * z, y = (q[1] - cy) * z, c = Math.cos(rot), s = Math.sin(rot); return { x: 960 + sx + x * c - y * s, y: 540 + sy + x * s + y * c }; };
    // The effects canvases cover the view plus a margin.
    const ox = cx - (960 + S.FM) / z, oy = cy - (540 + S.FM) / z;
    for (const s of [S.fxSprite, S.backSprite]) { s.position.set(ox, oy); s.scale.set(1 / z); }

    // The cut-ins: the left hip fills the frame. Before the moon cut the thumb pushes the guard out and a glint runs along
    // the bared steel; at the end the guard has been slammed home and everything stops on it.
    const shut = t >= HR[7] && t < HR[7] + 100, ci = (l >= CUTIN && l < CUTOUT) || shut;
    S.cutin.visible = ci;
    if (ci) {
      const q = shut ? 0.3 : k(CUTIN, CUTOUT, l), sc = 1.3 + 0.06 * q, bx = 960 - 20 * q + (shut ? 7 * Math.sin(t * 0.9) : 0), by = 560 + (shut ? 5 * Math.cos(t * 0.7) : 0);
      S.cutPose.texture = shut ? S.PO.slamin.tex : S.PO.cutin.tex;
      S.cutPose.scale.set(sc); S.cutPose.position.set(bx, by);
      S.cutFx.clear();
      if (shut) {
        const q2 = k(HR[7], HR[7] + 100, t), ring = [bx + (S.M.slamin.tsuba[0] - 768) * sc, by + (S.M.slamin.tsuba[1] - 512) * sc];
        S.cutFx.circle(ring[0], ring[1], 60 + 520 * q2).stroke({ width: 6 * (1 - q2) + 1, color: 0xffffff, alpha: 1 - q2 });
      } else {
        const [a, b] = S.M.cutin.glint.map(g => [bx + (g[0] - 768) * sc, by + (g[1] - 512) * sc]);
        const gq = k(CUTIN + 90, CUTIN + 210, l), gx = mix(a[0], b[0], gq), gy = mix(a[1], b[1], gq), ga = Math.sin(Math.PI * gq);
        if (ga > 0.01) S.cutFx.moveTo(gx - 120 * ga, gy).lineTo(gx + 120 * ga, gy).moveTo(gx, gy - 70 * ga).lineTo(gx, gy + 70 * ga).stroke({ width: 3, color: 0xffffff, alpha: ga });
      }
    }

    // The moon cut's white frame, then the three sets of impact frames, each two frames of black and white.
    S.flash.alpha = l >= HIT6 && l < L6 ? 0.85 : 0;
    const set = [['shade', HR[5]], ['cut', HR[6]], ['click', HR[7] + 100]].find(([, t0]) => t >= t0 && t < t0 + 84), white = set && t < set[1] + 42;
    S.imp.visible = !!set;
    if (set) {
      const ink = white ? 0x000000 : 0xffffff, kind = set[0];
      S.impBg.tint = white ? 0xffffff : 0x000000;
      S.impLines.tint = ink; S.impLines.position.set(...(kind === 'shade' ? S.SC : kind === 'cut' ? S.T6 : S.MOUTH2));
      for (const [name, list] of Object.entries(S.sils)) for (const [b, w] of list) { b.visible = name === kind && white; w.visible = name === kind && !white; }
      S.impG.clear().circle(S.MOON[0], S.MOON[1], S.MOON_R).fill(ink);
      const lines = kind === 'shade' ? S.scars.slice(0, 5) : kind === 'click' ? S.scars : [];
      for (const [a, b] of lines) S.impG.moveTo(...a).lineTo(...b).stroke({ width: 9, color: ink });
      if (kind !== 'shade') S.impG.moveTo(...S.scars[5][0]).lineTo(...S.scars[5][1]).stroke({ width: 12, color: white ? 0xffffff : 0x000000 });
    }
    const hit5 = toScreen(S.SC), hit6 = toScreen(S.T6);
    const sw5 = t >= REL5 && l < LAND + 520, sw6 = t >= REL6 && l < L6 + 600;
    // The moon cut and the slam both go past 50's landing (amplitude 54, blur 0.17, split 13), each with a second wave.
    const burst = l >= SHATTER && l < SHATTER + 420, center = burst ? toScreen(S.MOUTH2) : sw6 || l >= L6 ? hit6 : hit5;
    S.shock.center = center; S.zoom.center = center;
    if (burst) { S.shock.time = 0.22 + (l - SHATTER) / 1000; S.shock.amplitude = 66 * (1 - k(SHATTER, SHATTER + 420, l)); S.shock.wavelength = 340; }
    else if (l >= L6) { S.shock.time = 0.22 + (l - L6) / 1000; S.shock.amplitude = 66 * (1 - k(L6, L6 + 600, l)); S.shock.wavelength = 300; }
    else { S.shock.time = (l - LAND) / 1000; S.shock.amplitude = 34 * (1 - k(LAND, LAND + 520, l)); S.shock.wavelength = 240; }
    const zs = burst ? 0.18 * (1 - out(k(SHATTER, SHATTER + 240, l))) : l >= L6 ? 0.14 * (1 - out(k(L6, L6 + 200, l))) : 0.1 * (1 - out(k(LAND, LAND + 260, l)));
    S.zoom.strength = zs;
    const rs = burst ? 16 * (1 - out(k(SHATTER, SHATTER + 380, l))) : l >= L6 ? 16 * (1 - out(k(L6, L6 + 460, l))) : 7 * (1 - out(k(LAND, LAND + 400, l)));
    // Against the bright sky a sideways split would show a coloured stripe at the screen edge; up and down, the bars hide it.
    S.rgb.red = burst ? { x: 0, y: -rs } : { x: -rs, y: 0 }; S.rgb.blue = burst ? { x: 0, y: rs } : { x: rs, y: 0 };
    const released = (t >= REL5 && l < L6) || t >= REL6;
    const filters = [S.bloom];
    if (sw5 || sw6 || burst) filters.push(S.shock);
    if (released && zs > 0.005) filters.push(S.zoom);
    if (released && rs > 0.5) filters.push(S.rgb);
    // Each big hit has a second wave: after the moon cut from the blade tip, after the slam from the hero's hands. It starts
    // already out from the centre so nothing swirls.
    const w2 = l >= L6 + 180 && l < L6 + 780 ? [L6 + 180, toScreen(S.T6), 34] : l >= SHATTER + 160 && l < SHATTER + 760 ? [SHATTER + 160, toScreen(S.MOUTH2), 44] : null;
    if (w2) { S.shock2.center = w2[1]; S.shock2.time = 0.25 + (l - w2[0]) / 1000; S.shock2.amplitude = w2[2] * (1 - k(w2[0], w2[0] + 600, l)); filters.push(S.shock2); }
    // The daylight is bright: the glow eases off as it comes in.
    S.bloom.bloomScale = mix(1.15, 0.2, k(SHATTER, SHATTER + 160, l));
    S.world.filters = S.imp.visible ? null : filters;

    // Entry caption: the name wipes in on the ground in front of the hero; the take-off's wind sweeps it off.
    caption(S,l,{start:420,revealStart:560,revealEnd:860,exitStart:1450,exitEnd:1560,x:1180,y:1118});
    revealName(S,l,{start:TITLE,end:TITLE+260,ghostIn:TITLE+50,ghostOut:TITLE+180,ghostEnd:TITLE+570,exit:EXIT,finish:END,x:null,y:null});
    S.nameG.visible=l>=TITLE;

    this.drawFx(l, k, out, { skyY, L: [L1, L2, L3, L4, L5], SM: [SM1, SM2, SM3, SM4, SM5], SMS, LS, NOTO, IAI, ONI0, ONI1, CUTIN, CUTOUT, SM6, HIT6, L6, RISE2, SLAM0, CLICK, SHATTER, BACK0, END, hx, hy, keyAt, drift, fade, WAVE_O, wave, waveR, edgeY, rise, gone, z, ox, oy });
    S.fxTex.source.update(); S.backTex.source.update();
  }

  // Slashes, scars, particles, in world coordinates. Two additive canvases: one behind the hero (scars, the oni's
  // slash, the part of each slash that passes behind the body), one in front.
  drawFx(l, k, out, T) {
    const S = this.S, g = S.fx.getContext('2d'), gb = S.backC.getContext('2d');
    for (const c of [g, gb]) { c.setTransform(1, 0, 0, 1, 0, 0); c.clearRect(0, 0, c.canvas.width, c.canvas.height); c.setTransform(T.z, 0, 0, T.z, -T.ox * T.z, -T.oy * T.z); c.globalCompositeOperation = 'lighter'; c.lineCap = 'round'; c.lineJoin = 'round'; }

    // A slash: a sharp crescent whose head is the blade tip, held bright through its hit stop, then frayed into strands.
    const draw=drawing(g,gb), {band,arcLine,glint}=draw;
    const slash=(path,head,tail,head2,w,fray,seed,alpha=1,cv)=>sharedSlash(draw,this.character.palette,path,head,tail,head2,w,fray,seed,alpha,cv);
    // Sparks thrown off a hit at the blade tip, mostly across the cut.
    const sparksAt=(x,y,nx,ny,t0,count,power,seed)=>emitSparks(g,l-t0,count,power,seed,()=>[x,y,nx,ny],this.character.palette.spark);
    const sparks = (P, t0, count, power, seed) => { for (let j = 0; j < 4; j++) { const [x0, y0, nx, ny] = P.at(0.82 + j * 0.06); sparksAt(x0, y0, nx, ny, t0, count / 4, power, seed + j * 7); } };

    const glow = (x, y, r, rgb, a, cv = g) => {
      if (a <= 0.003) return;
      const gr = cv.createRadialGradient(x, y, 0, x, y, r); gr.addColorStop(0, 'rgba(' + rgb + ',' + a + ')'); gr.addColorStop(1, 'rgba(' + rgb + ',0)');
      cv.fillStyle = gr; cv.beginPath(); cv.arc(x, y, r, 0, Math.PI * 2); cv.fill();
    };
    const lerp = (a, b, f) => [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f];
    // Motes drawn in to a blade (hilt to tip) while a wind-up holds.
    const gather = (hilt, tip, t0, t1, count, seed, rgb, reach = 1) => {
      for (let i = 0; i < count; i++) {
        const s0 = t0 + rnd(i, seed) * (t1 - t0 - 300), dur = 300 + rnd(i, seed + 1) * 260, p = (l - s0) / dur;
        if (p < 0 || p > 1) continue;
        const to = lerp(hilt, tip, rnd(i, seed + 6)), ang = rnd(i, seed + 3) * Math.PI * 2, r0 = (260 + rnd(i, seed + 4) * 360) * reach, r = r0 * (1 - p * p), r2 = r0 * (1 - Math.max(0, p - 0.12) ** 2);
        g.strokeStyle = i % 3 ? 'rgba(' + rgb + ',' + (0.9 * Math.sin(p * Math.PI)) + ')' : 'rgba(255,255,255,' + Math.sin(p * Math.PI) + ')';
        g.lineWidth = (1.5 + rnd(i, seed + 5) * 2) * reach;
        g.beginPath(); g.moveTo(to[0] + Math.cos(ang) * r, to[1] + Math.sin(ang) * r); g.lineTo(to[0] + Math.cos(ang) * r2, to[1] + Math.sin(ang) * r2); g.stroke();
      }
    };

    const O = T.WAVE_O;
    // Kicked-up grit, thrown one way along the ground.
    const spray = (x0, y0, t0, n, dir, seed) => {
      const sp = l - t0; if (sp <= 0 || sp >= 800) return;
      for (let i = 0; i < n; i++) {
        const life = 300 + rnd(i, seed) * 480; if (sp > life) continue;
        const q = sp / life, s = sp / 1000, v = 300 + rnd(i, seed + 1) * 1500, up = 300 + rnd(i, seed + 2) * 1300;
        const px = x0 + dir * v * s + (rnd(i, seed + 3) - 0.5) * 200, py = Math.min(y0, y0 - up * s + 1800 * s * s), n2 = 18 + rnd(i, seed + 4) * 40;
        g.strokeStyle = i % 5 ? 'rgba(255,50,60,' + (1 - q) + ')' : 'rgba(255,225,215,' + (1 - q) + ')'; g.lineWidth = 2 + rnd(i, seed + 5) * 3.5;
        g.beginPath(); g.moveTo(px, py); g.lineTo(px - dir * n2, py + (rnd(i, seed + 6) - 0.5) * 16); g.stroke();
      }
    };
    // The charge: the ground under him glows and cracks, stones lift off it, motes are drawn into the hilt.
    const hx0 = S.AT.charge[0], gy = S.GROUND - 6, ca = k(640, 1300, l) * (1 - k(T.SM[0] + 40, T.SM[0] + 200, l));
    if (ca > 0) {
      g.save(); g.translate(hx0, gy); g.scale(1, 0.12); glow(0, 0, 200 + 900 * ca, '255,60,70', 0.5 * ca); g.restore();
      for (let i = 0; i < 12; i++) {
        const dir = i % 2 ? 1 : -1, len = (160 + rnd(i, 3) * 620) * ca; let x = hx0 + dir * 30, y = gy;
        gb.strokeStyle = 'rgba(255,90,90,' + 0.9 * ca + ')'; gb.lineWidth = 2.5; gb.beginPath(); gb.moveTo(x, y);
        for (let j = 1; j <= 6; j++) { x += dir * len / 6; y += (rnd(i, j) - 0.45) * 22; gb.lineTo(x, y); }
        gb.stroke();
      }
      for (let i = 0; i < 46; i++) {
        const x = hx0 + (rnd(i, 5) - 0.5) * 1400, r = 7 + rnd(i, 6) * 24, h = (80 + rnd(i, 7) * 420) * inq(k(760 + rnd(i, 8) * 400, 1500, l)), a0 = rnd(i, 9) * 6.28 + l * 0.001 * (rnd(i, 10) - 0.5);
        const y = gy - h + Math.sin(l * 0.003 + i) * 6;
        g.fillStyle = 'rgba(255,70,80,' + 0.85 * ca + ')'; g.beginPath();
        for (let j = 0; j < 4; j++) { const a2 = a0 + j * 1.57, r2 = r * (0.7 + 0.4 * rnd(i + j, 11)); j ? g.lineTo(x + Math.cos(a2) * r2, y + Math.sin(a2) * r2) : g.moveTo(x + Math.cos(a2) * r2, y + Math.sin(a2) * r2); }
        g.closePath(); g.fill();
      }
    }
    if (T.keyAt === 'charge' && l > 520) {
      const hilt = S.W('charge', S.M.charge.tsuba, [T.hx, T.hy]);
      gather(hilt, hilt, 700, T.SM[0], 110, 1, '255,120,130', 1.4);
      glint(hilt[0], hilt[1], k(1370, 1480, l), 110);
    }
    // The launch: the ground bursts under him, stones are flung up, and a pillar of light stands where he left.
    const lq = (l - T.SM[0]) / 620;
    if (lq > 0 && lq < 1) {
      spray(hx0, gy, T.SM[0], 170, 1, 300); spray(hx0, gy, T.SM[0], 170, -1, 320);
      g.save(); g.translate(hx0, gy); g.scale(1, 0.14); glow(0, 0, 300 + 1500 * out(lq), '255,70,80', 0.8 * (1 - lq)); g.restore();
      const w = 130 * (1 - lq) * (1 - lq) + 4;
      for (const [m, c] of [[3.2, '255,40,60,0.3'], [1.4, '255,120,130,0.7'], [0.5, '255,255,255,1']]) { const v = c.split(','); gb.fillStyle = 'rgba(' + v.slice(0, 3).join(',') + ',' + v[3] * (1 - lq) + ')'; gb.beginPath(); gb.moveTo(hx0 - w * m, gy); gb.lineTo(hx0 + w * m, gy); gb.lineTo(S.AT.cut1[0] + 40, -600); gb.lineTo(S.AT.cut1[0] + 20, -600); gb.closePath(); gb.fill(); }
      for (let i = 0; i < 64; i++) {
        const s = (l - T.SM[0]) / 1000, a0 = -1.57 + (rnd(i, 13) - 0.5) * 1.5, v = 700 + rnd(i, 14) * 1500, x = hx0 + Math.cos(a0) * v * s, y = gy + Math.sin(a0) * v * s + 900 * s * s, r = 6 + rnd(i, 15) * 20, a2 = rnd(i, 16) * 6 + s * 9;
        g.fillStyle = 'rgba(255,' + (70 + (i % 3) * 40) + ',80,' + (1 - lq) + ')'; g.beginPath(); for (let j = 0; j < 4; j++) { const b = a2 + j * 1.57; j ? g.lineTo(x + Math.cos(b) * r, y + Math.sin(b) * r) : g.moveTo(x + Math.cos(b) * r, y + Math.sin(b) * r); } g.closePath(); g.fill();
      }
    }
    // Each kick off the air bursts a ring of ink under his feet.
    [[T.SM[1], S.AT.cut2], [T.SM[2], S.AT.cut3], [T.SM[3], S.AT.cut4], [T.SM[4], S.AT.cut5]].forEach(([t0, at], i) => {
      const q = (l - t0) / 380; if (q <= 0 || q >= 1) return;
      gb.save(); gb.translate(at[0] - 40, at[1] + 240); gb.rotate(-0.2 + 0.1 * i); gb.scale(1, 0.26);
      for (const [w, c] of [[30, 'rgba(255,40,55,'], [6, 'rgba(255,220,220,']]) { gb.strokeStyle = c + (1 - q) * 0.9 + ')'; gb.lineWidth = w * (1 - q) + 1; gb.beginPath(); gb.arc(0, 0, 70 + 460 * out(q), 0, Math.PI * 2); gb.stroke(); }
      gb.restore();
    });
    // The dive: a burn along its whole line, and a ring of ink where he kicks off the air.
    const ds = l - T.SM6, kick = S.MOUTH0, end = S.T6;
    if (ds > 0 && ds < 900) {
      const q = ds / 900, e = out(k(0, 84, ds));
      gb.save(); gb.translate(kick[0], kick[1]); gb.rotate(S.ANG6 + 1.57); gb.scale(1, 0.3);
      for (const [w, c] of [[40, 'rgba(255,40,55,'], [8, 'rgba(255,230,230,']]) { gb.strokeStyle = c + (1 - q) * 0.9 + ')'; gb.lineWidth = w * (1 - q) + 1; gb.beginPath(); gb.arc(0, 0, 90 + 700 * out(q), 0, Math.PI * 2); gb.stroke(); }
      gb.restore();
      for (const [w, c] of [[30, '255,40,60,0.28'], [7, '255,170,170,0.75'], [2.5, '255,255,255,1']]) {
        const v = c.split(','); gb.strokeStyle = 'rgba(' + v.slice(0, 3).join(',') + ',' + v[3] * (1 - q) + ')'; gb.lineWidth = w;
        gb.beginPath(); gb.moveTo(kick[0], kick[1]); gb.lineTo(kick[0] + (end[0] - kick[0]) * e, kick[1] + (end[1] - kick[1]) * e); gb.stroke();
      }
    }
    // The skid: a ring of ink off the air under his boots, thrown forward.
    const boot = S.W('cut6', S.M.cut6.boot), skq = (l - T.HIT6) / 620;
    if (skq > 0 && skq < 1) {
      gb.save(); gb.translate(boot[0], boot[1]); gb.rotate(1.2); gb.scale(1, 0.3);
      for (const [w, c] of [[34, 'rgba(255,40,55,'], [7, 'rgba(255,230,230,']]) { gb.strokeStyle = c + (1 - skq) * 0.9 + ')'; gb.lineWidth = w * (1 - skq) + 1; gb.beginPath(); gb.arc(0, 0, 60 + 520 * out(skq), 0, Math.PI * 2); gb.stroke(); }
      gb.restore();
      sparksAt(boot[0], boot[1], 0.9, -0.2, T.HIT6, 110, 1.1, 340);
    }
    // The ink front opening the blood-moon world: a thin red rim and a wider dark-red haze on the moving edge; the moon
    // takes a red halo as the front passes it.
    if (T.wave > 0 && T.wave < 1) {
      const r = T.waveR;
      for (const [w, a] of [[60, 0.16], [14, 0.4], [3, 0.9]]) {
        gb.strokeStyle = 'rgba(255,' + (w < 10 ? 120 : 40) + ',60,' + a + ')'; gb.lineWidth = w; gb.beginPath();
        for (let i = 0; i <= 120; i++) { const a2 = i / 120 * Math.PI * 2, rr = r * (1 + 0.05 * Math.sin(a2 * 7 + l * 0.003) + 0.035 * Math.sin(a2 * 17 - l * 0.005)); const x = O[0] + Math.cos(a2) * rr, y = O[1] + Math.sin(a2) * rr; i ? gb.lineTo(x, y) : gb.moveTo(x, y); }
        gb.stroke();
      }
    }
    const mdist = Math.hypot(S.MOON[0] - O[0], S.MOON[1] - O[1]), mHit = 640 + 700 * 0.6;
    if (T.waveR > mdist - S.MOON_R) glow(S.MOON[0], S.MOON[1] + T.skyY, S.MOON_R * 1.5, '255,40,50', 0.35 * Math.min(1, (T.waveR - mdist + S.MOON_R) / 400) * (1 - 0.5 * k(mHit + 300, mHit + 900, l)) * (1 - k(T.SHATTER, T.SHATTER + 600, l)), gb);
    // The slashes: five, each sooner after the last; all five again from the afterimages; then the moon cut.
    const B = S.blades, widths = [70, 60, 72, 44, 62], frays = [420, 300, 300, 280, 320];
    for (let i = 0; i < 5; i++) {
      const s0 = T.SM[i], hit = T.L[i], end2 = T.L[i];
      if (l >= s0 + 4 && l < end2 + frays[i]) slash(B[i], out(k(s0 + 4, hit - 4, l)), S.TAILS[i] * out(k(s0 + 20, hit, l)), out(k(s0 + 12, hit + 8, l)), widths[i], k(end2, end2 + frays[i], l), 21 + 10 * i);
      if (l >= T.SMS + 4 && l < T.LS + 460) slash(B[i], out(k(T.SMS + 4, T.LS - 4, l)), S.TAILS[i] * out(k(T.SMS + 20, T.LS, l)), out(k(T.SMS + 12, T.LS + 8, l)), widths[i] * 1.35, k(T.LS, T.LS + 460, l), 61 + 10 * i);
    }
    [[110, 1], [70, 0.7], [80, 0.75], [80, 0.8], [90, 0.85]].forEach(([n, pw], i) => { sparks(B[i], T.L[i], n, pw, 10 + 10 * i); sparks(B[i], T.LS, n, pw * 1.2, 110 + 10 * i); });
    if (l >= T.LS + 40 && l < T.NOTO + 90) slash(B[8], k(T.LS + 40, T.NOTO, l), k(T.LS + 70, T.NOTO + 60, l), k(T.LS + 40, T.NOTO, l), 22, 0, 161, 0.7);
    if (l >= T.RISE2 - 30 && l < T.RISE2 + 230) slash(B[7], out(k(T.RISE2 - 30, T.RISE2 + 30, l)), out(k(T.RISE2 - 10, T.RISE2 + 130, l)), out(k(T.RISE2 - 20, T.RISE2 + 40, l)), 34, 0, 91, 0.8 * (1 - k(T.RISE2 + 50, T.RISE2 + 230, l)));
    // The moon cut: the oni's huge crescent behind the hero's, on the same frames.
    if (l >= T.SM6 + 4 && l < T.L6 + 700) {
      const hd = out(k(T.SM6 + 4, T.HIT6 - 4, l)), tl = 0.5 * out(k(T.SM6 + 20, T.HIT6, l)), h2 = out(k(T.SM6 + 12, T.HIT6 + 8, l)), fr = k(T.L6, T.L6 + 700, l);
      slash(B[6], hd, tl, h2, 240, fr, 141, 1, gb);
      slash(B[5], hd, tl, h2, 84, fr, 131);
    }
    // Sparks where the cut crosses the moon's rim, and at both blade tips.
    for (const sgn of [-1, 1]) sparksAt(S.MOON[0] + sgn * S.U6[0] * S.MOON_R, S.MOON[1] + sgn * S.U6[1] * S.MOON_R, S.N6[0], S.N6[1], T.L6, 80, 1.2, 150 + sgn * 5);
    sparksAt(S.T6[0], S.T6[1], S.N6[0], S.N6[1], T.L6, 100, 1.3, 170);

    // Sheathing at the top of the leap: a glint runs down the back of the blade into the scabbard mouth.
    const na = S.W('noto', S.M.noto.tsuba), nb = S.W('noto', S.M.noto.mouth), nq = k(T.NOTO + 60, T.IAI - 40, l);
    if (nq > 0 && nq < 1) glint(...lerp(na, nb, nq), Math.sin(nq * Math.PI) * 0.99, 60);

    // The melted afterimages run through the air to him as ink.
    const mq = k(T.IAI - 80, T.ONI0 + 320, l);
    if (mq > 0 && mq < 1) {
      const to = S.W('iai', S.M.iai.tsuba);
      for (let i = 1; i <= 5; i++) {
        const a = S.AT['cut' + i], c2 = [a[0] + 260 * (i % 2 ? 1 : -1), (a[1] + to[1]) / 2];
        gb.strokeStyle = 'rgba(255,40,55,' + 0.7 * Math.sin(mq * Math.PI) + ')'; gb.lineWidth = 12; gb.beginPath();
        for (let j = 0; j <= 24; j++) { const f = (j / 24) * out(mq), f1 = 1 - f, x = f1 * f1 * a[0] + 2 * f1 * f * c2[0] + f * f * to[0], y = f1 * f1 * a[1] + 2 * f1 * f * c2[1] + f * f * to[1]; j ? gb.lineTo(x, y) : gb.moveTo(x, y); }
        gb.stroke(); gb.strokeStyle = 'rgba(255,200,200,' + 0.9 * Math.sin(mq * Math.PI) + ')'; gb.lineWidth = 2.5; gb.stroke();
      }
    }
    // The oni rising: a red rim of ink on its rising edge; then motes from the whole sky into both hilts, and after the
    // cut-in both guards glint on the same frame.
    if (T.rise > 0 && T.rise < 1) {
      gb.strokeStyle = 'rgba(255,50,60,0.5)'; gb.lineWidth = 10; gb.beginPath();
      for (let x = 1320; x <= 3120; x += 40) { const y = T.edgeY + 34 * Math.sin(x * 0.009 + l * 0.004) + 20 * Math.sin(x * 0.023 - l * 0.007); x > 1320 ? gb.lineTo(x, y) : gb.moveTo(x, y); }
      gb.stroke();
    }
    if (T.keyAt === 'iai') {
      const tsuH = S.W('iai', S.M.iai.tsuba), tsuO = S.OW(S.ONI.oni1, S.M.oni1.tsuba);
      if (l > T.ONI1 - 200) { gather(tsuH, tsuH, T.ONI1 - 200, T.CUTIN, 90, 201, '255,120,130', 2); gather(tsuO, tsuO, T.ONI1 - 200, T.CUTIN, 40, 211, '255,90,100', 2.6); }
      const gq = k(T.CUTOUT + 30, T.SM6 - 16, l); glint(...tsuH, gq, 90);
    }

    // The straight cuts hang behind the hero from each hit, full length. After the strike the five air cuts dim; the
    // moon cut lands bright across the moon. On the click all six flare at once, then the world breaks along them.
    const open = out(k(T.SHATTER, T.SHATTER + 160, l)), flickA = 0.8 + 0.2 * Math.sin(l * 0.045);

    const stroke=(w,color)=>strokePath(gb,w,color);
    const hitAt = [T.L[0], T.L[1], T.L[2], T.L[3], T.L[4], T.L6];
    S.scars.forEach((pts, i) => {
      // The cuts belong to the blood-moon world: they are gone with it, never left lying across the real one.
      const a = k(hitAt[i], hitAt[i] + 120, l) * (1 - k(T.SHATTER, T.SHATTER + 120, l));
      if (a <= 0) return;
      const fl = l >= T.SHATTER ? 1 - k(T.SHATTER, T.SHATTER + 300, l) : 0, wide = Math.max(open, 0.6 * fl);
      const pa = a * (i < 5 ? Math.max(1 - 0.75 * k(T.L[4] + 200, T.L[4] + 600, l) * (1 - 0.6 * k(T.L6, T.L6 + 200, l)), fl, open) : 1);
      gb.save(); if (l >= T.SHATTER) gb.translate(T.drift, 0); trace(gb, pts);
      stroke(12 + 56 * wide, 'rgba(255,' + (40 + 170 * open) + ',' + (70 + 110 * open) + ',' + (0.2 + 0.3 * fl) * pa * flickA + ')');
      stroke(3 + 18 * wide, 'rgba(255,' + (150 + 90 * open) + ',' + (160 + 60 * open) + ',' + (0.6 + 0.3 * fl) * pa + ')');
      stroke(1.4 + 3 * wide, 'rgba(255,255,255,' + pa * (1 - 0.6 * open) + ')');
      gb.restore();
    });
    // The moon's cut faces glow as the halves part, and red mist bleeds out of the gap.
    if (l >= T.CLICK) {
      const ga = out(k(T.CLICK, T.CLICK + 300, l)) * (1 - k(T.SHATTER + 100, T.SHATTER + 380, l));
      if (ga > 0) S.redHalves.forEach((h, i) => {
        const c = [h.position.x, h.position.y], u = S.U6, r = S.MOON_R, rot = h.rotation, ru = [u[0] * Math.cos(rot) - u[1] * Math.sin(rot), u[0] * Math.sin(rot) + u[1] * Math.cos(rot)];
        gb.lineWidth = 16; gb.strokeStyle = 'rgba(255,60,70,' + 0.4 * ga + ')'; gb.beginPath(); gb.moveTo(c[0] - ru[0] * r, c[1] - ru[1] * r); gb.lineTo(c[0] + ru[0] * r, c[1] + ru[1] * r); gb.stroke();
        gb.lineWidth = 3; gb.strokeStyle = 'rgba(255,235,235,' + 0.9 * ga + ')'; gb.stroke();
      });
      const e = l - T.CLICK;
      for (let i = 0; i < 70; i++) {
        const life = 900 + rnd(i, 41) * 900, t0 = rnd(i, 42) * 1400, q = (e - t0) / life;
        if (q <= 0 || q >= 1) continue;
        const f = rnd(i, 43) * 2 - 1, p0 = [S.MOON[0] + S.U6[0] * f * S.MOON_R * 0.9, S.MOON[1] + S.U6[1] * f * S.MOON_R * 0.9 + 40 * k(T.CLICK, T.CLICK + 1500, l)];
        g.fillStyle = 'rgba(255,' + (50 + (i % 3) * 40) + ',70,' + 0.7 * Math.sin(q * Math.PI) * (1 - k(T.SHATTER, T.SHATTER + 600, l)) + ')';
        g.beginPath(); g.arc(p0[0] + (rnd(i, 44) - 0.5) * 60 * q, p0[1] + 140 * q * q + 20 * q, 1.5 + rnd(i, 45) * 2.5, 0, Math.PI * 2); g.fill();
      }
    }
    // Once broken, each piece carries a lit lip along its own edges.
    if (l >= T.SHATTER) for (const p of S.pieces) {
      gb.save();
      gb.translate(p.c.pivot.x + p.dx, p.c.pivot.y + p.dy); gb.rotate(p.r); gb.translate(-p.c.pivot.x, -p.c.pivot.y);
      clipTo(gb, p.poly);
      for (const pts of S.scars) { trace(gb, pts); stroke(10, 'rgba(255,60,80,' + 0.35 * T.fade * open + ')'); stroke(2, 'rgba(255,235,235,' + 0.9 * T.fade * open + ')'); }
      gb.restore();
    }
    // The burst: daylight rushes in behind the hero, never over him, and shards of the blood-moon sky are thrown out.
    const bq = (l - T.SHATTER) / 640;
    if (bq >= 0 && bq < 1) {
      glow(S.BURST[0], S.BURST[1], 500 + 1500 * out(bq), '255,225,190', 0.42 * (1 - bq) * (1 - bq), gb);
      for (let i = 0; i < 170; i++) {
        const a = rnd(i, 60) * Math.PI * 2, r = (120 + rnd(i, 61) * 2600) * out(Math.min(1, bq * (0.7 + rnd(i, 62)))), n = 14 + rnd(i, 63) * 40, q = 1 - bq;
        const x = S.BURST[0] + Math.cos(a) * r, y = S.BURST[1] + Math.sin(a) * r;
        g.strokeStyle = i % 4 ? 'rgba(255,50,60,' + q + ')' : 'rgba(255,240,225,' + q + ')'; g.lineWidth = 2 + rnd(i, 64) * 5;
        g.beginPath(); g.moveTo(x, y); g.lineTo(x - Math.cos(a) * n, y - Math.sin(a) * n); g.stroke();
      }
    }
    // Embers peel off the lips of every seam as the world breaks.
    if (l >= T.SHATTER && l < T.SHATTER + 900) {
      const e = l - T.SHATTER;
      for (let i = 0; i < 220; i++) {
        const t0 = rnd(i, 30) * 520, life = 600 + rnd(i, 31) * 700, q = (e - t0) / life;
        if (q <= 0 || q >= 1) continue;
        const [x0, y0, nx, ny] = S.emberSrc[i % S.emberSrc.length].at(rnd(i, 32)), side = i % 2 ? 1 : -1, v = 30 + rnd(i, 33) * 110;
        const px = x0 + nx * v * 1.4 * side * q + T.drift, py = y0 + ny * v * 1.4 * side * q - 40 * q;
        g.fillStyle = 'rgba(255,' + (120 + (i % 3) * 50) + ',125,' + Math.sin(q * Math.PI) * 0.9 + ')';
        g.beginPath(); g.arc(px, py, 1.2 + rnd(i, 34) * 2.2, 0, Math.PI * 2); g.fill();
      }
    }
    // The oni blown away: embers stream off its body and drift up and back.
    if (T.gone > 0 && T.gone < 1) {
      const c = S.OW(S.ONI.oni4, S.M.oni4.head);
      for (let i = 0; i < 160; i++) {
        const t0 = rnd(i, 50) * 0.5, q = (T.gone - t0) / 0.5;
        if (q <= 0 || q >= 1) continue;
        const px = c[0] - 700 + rnd(i, 51) * 1100 - 260 * q, py = c[1] - 100 + rnd(i, 52) * 900 - 380 * q;
        g.fillStyle = 'rgba(255,' + (60 + (i % 3) * 40) + ',70,' + 0.8 * Math.sin(q * Math.PI) + ')';
        g.beginPath(); g.arc(px, py, 2 + rnd(i, 53) * 3, 0, Math.PI * 2); g.fill();
      }
    }
    // Embers rise from the landing on to the end.
    if (l > T.L[4] + 240 && l < T.END) {
      const fade = k(T.L[4] + 240, T.L[4] + 740, l) * (1 - k(T.SHATTER, T.SHATTER + 600, l));
      for (let i = 0; i < 60; i++) {
        const life = 2200 + rnd(i, 16) * 1800, ph = ((l + rnd(i, 17) * life) % life) / life;
        const px = -200 + rnd(i, 18) * 2800 + Math.sin(ph * 6 + i) * 20, py = 1100 - ph * 800 - rnd(i, 19) * 300;
        g.fillStyle = 'rgba(255,' + (90 + (i % 3) * 50) + ',100,' + (Math.sin(ph * Math.PI) * fade * 0.8) + ')';
        g.beginPath(); g.arc(px, py, 1.5 + rnd(i, 20) * 2.5, 0, Math.PI * 2); g.fill();
      }
    }
    for (const c of [g, gb]) c.globalCompositeOperation = 'source-over';
  }

  destroy() {
    this.disposeTextures();
    for(const filter of [this.S.bloom,this.S.shock,this.S.shock2,this.S.zoom,this.S.rgb,...this.S.cutBg.filters])filter.destroy();
  }
}
