import {canvasOf, inside, k, out, inq, quint, on2, rnd, spline, pathOf, reach, onScreen, textures, drawing, shakeAt, stroke, sparks} from './toolkit.mjs';
import {fitText, framing, sky, revealName, caption, impactFrame, whiteFlash} from './scenes.mjs';

export class Slice {
  constructor({app, canvas, images, character, nickname}) {
    const P = PIXI, F = P.filters;
    const {tex, silhouette, radial, dispose} = textures(P);
    this.disposeTextures=dispose;
    this.palette=character.palette;
    const [p0, p1, p2, p3, p4, moon, impact, moonRed] = images;

    // Per pose: the hips and the soles in image pixels, and the scale that makes the head the same size on screen.
    const scales = [0.63,0.7,0.7,0.77,0.77];
    const POSE = character.anchors.slice.map((pose,i)=>({...pose,s:scales[i]}));
    // He stands on the ground line; X is where his hips are. He starts on the left, facing the moon on the right, and the
    // draw carries him across the frame.
    const GROUND = 930, X0 = 530, X1 = 920;
    const placed = (i, X, p) => { const q = POSE[i]; return [X + (p[0] - q.hip[0]) * q.s, GROUND + (p[1] - q.sole) * q.s]; };
    // The slash is the path of the blade tip: from the scabbard mouth in the charge, one long flat line that rises a little to
    // the tip at the stop. H, the hit, is that tip.
    const mouth = placed(0, X0, [1190, 557]), H = placed(2, X1, [1485, 317]);
    const slashPts = spline([0, 0.25, 0.5, 0.75, 1].map(u => [mouth[0] + (H[0] - mouth[0]) * u, mouth[1] + (H[1] - mouth[1]) * u + 22 * Math.sin(Math.PI * u)])).pts;
    const slash = pathOf(slashPts);
    // The cut a straight blade leaves is straight: the chord of the slash as it lands (from its tail at the hit to the
    // tip), run right across the sky. Each side of it is a polygon closed round a big circle.
    const C0 = [960, 540], RR = 5000;

    const ca = slash.at(0.5).slice(0, 2), cd = [H[0] - ca[0], H[1] - ca[1]], cm = Math.hypot(cd[0], cd[1]);
    const tangent = [cd[0] / cm, cd[1] / cm], n = [-tangent[1], tangent[0]];
    const cutLine = [reach(ca, H), reach(H, ca)];
    const sides = [1, -1].map(dir => {
      const ang = p => Math.atan2(p[1] - C0[1], p[0] - C0[0]), a0 = ang(cutLine[0]), a1 = ang(cutLine[cutLine.length - 1]);
      let da = a0 - a1; if (dir > 0) while (da <= 0) da += 2 * Math.PI; else while (da >= 0) da -= 2 * Math.PI;
      return [...cutLine, ...Array.from({ length: 65 }, (_, i) => [C0[0] + RR * Math.cos(a1 + da * i / 64), C0[1] + RR * Math.sin(a1 + da * i / 64)])];
    });

    const stage = app.stage;
    const world = new P.Container(); world.pivot.set(960, 540); world.position.set(960, 540); stage.addChild(world);
    const dim = new P.Sprite(radial([[0, 'rgba(6,6,12,0.55)'], [0.6, 'rgba(4,4,8,0.85)'], [1, 'rgba(0,0,0,1)']]));
    dim.anchor.set(0.5); dim.position.set(960, 540); dim.width = 3000; dim.height = 2000; world.addChild(dim);

    // What the cut reveals: the same sky's blood-moon side, sitting behind the moonlit halves.
    const red = new P.Container(); red.pivot.set(960, 540); red.position.set(960, 540);
    sky(P,tex,radial,moonRed,red,[-120,-200,2160,1440],[2600,1700]);
    world.addChild(red);

    // Backdrop: one moonlit sky, the moon up on the right, ahead of the hero and clear of the cut (splitting the moon is 60 档's).
    // After the zanshin the side with the moon slides off along the straight cut.
    const moonSide = inside(sides[0], 1295, 210) ? 0 : 1;
    const halves = [sides[moonSide], sides[1 - moonSide]].map(poly => {
      const c = new P.Container();
      sky(P,tex,radial,moon,c,[-120,-200,2160,1440],[2600,1700]);
      const m = new P.Graphics().poly(poly.flat()).fill(0xffffff); c.addChild(m); c.mask = m;
      c.pivot.set(H[0], H[1]); c.position.set(H[0], H[1]);
      world.addChild(c); return c;
    });
    const seamC = document.createElement('canvas'); seamC.width = 1920; seamC.height = 1080;
    const seamTex = tex(seamC), seamSprite = new P.Sprite(seamTex); seamSprite.blendMode = 'add'; world.addChild(seamSprite);

    const rim = new P.Sprite(radial(this.palette.rimStops));
    rim.anchor.set(0.5); rim.width = 1100; rim.height = 1100; world.addChild(rim);

    const poses = [p0, p1, p2, p3, p4].map(tex);
    const ghosts = [0, 1, 2].map(() => { const s = new P.Sprite(poses[0]); s.alpha = 0; world.addChild(s); return s; });
    const hero = new P.Sprite(poses[0]); hero.tint = 0xe0e0e0; world.addChild(hero);

    const fx = document.createElement('canvas'); fx.width = 1920; fx.height = 1080;
    const fxTex = tex(fx), fxSprite = new P.Sprite(fxTex); fxSprite.blendMode = 'add'; world.addChild(fxSprite);

    const ui = new P.Container(); ui.pivot.set(960, 540); stage.addChild(ui);
    const nameG = new P.Container(); nameG.position.set(70, 330); nameG.rotation = -0.12; ui.addChild(nameG);
    // The strokes are thickened: at this size the Kai's own weight is too light against the blade light.
    const nameStyle = { fontFamily: ['LXGW WenKai','sans-serif'], fontSize: 132, fill: 0xffffff, stroke: { color: 0xffffff, width: 4, join: 'round' }, letterSpacing: 16 };
    const nameGhosts = [0, 1].map(() => { const t = new P.Text({ text: nickname, style: { ...nameStyle, fill: this.palette.nameGhost, stroke: { color: this.palette.nameGhost, width: 4, join: 'round' } } }); t.anchor.set(0, 0.5); nameG.addChild(t); return t; });
    const nameT = new P.Text({ text: nickname, style: nameStyle }); nameT.anchor.set(0, 0.5); nameG.addChild(nameT);
    fitText([nameT, ...nameGhosts], 1100);
    const nameMask = new P.Graphics(); nameG.addChild(nameMask); nameT.mask = nameMask;
    // Entry caption: says who is coming in from the cut-in, until the strike sweeps it away.
    const cap = new P.Container(); cap.position.set(1000, 900); ui.addChild(cap);
    const capName = new P.Text({ text: nickname, style: { fontFamily: ['LXGW WenKai','sans-serif'], fontSize: 52, fill: 0xffffff, stroke: { color: 0xffffff, width: 1.6, join: 'round' }, letterSpacing: 8 } });
    fitText([capName], 820);
    capName.anchor.set(0, 1); capName.position.set(30, -10); cap.addChild(capName);
    const capMask = new P.Graphics(); cap.addChild(capMask); capName.mask = capMask;

    const imp = new P.Container(); world.addChild(imp);
    const impBg = new P.Graphics().rect(-400, -400, 2720, 1880).fill(0xffffff); imp.addChild(impBg);
    const impLines = new P.Sprite(tex(impact)); impLines.anchor.set(0.5); impLines.width = impLines.height = 4400; imp.addChild(impLines);
    const silB = new P.Sprite(silhouette(p2, '#000')), silW = new P.Sprite(silhouette(p2, '#fff'));
    imp.addChild(silB, silW);
    const silTexB = silB.texture, silTexW = silW.texture;

    const flash = new P.Graphics().rect(-400, -400, 2720, 1880).fill(0xffffff); world.addChild(flash);
    const bars = [new P.Graphics(), new P.Graphics()]; stage.addChild(...bars);
    stage.setChildIndex(ui, stage.children.indexOf(world) + 1);

    const bloom = new F.AdvancedBloomFilter({ threshold: 0.9, bloomScale: 1.15, brightness: 1, blur: 7, quality: 5 });
    const shock = new F.ShockwaveFilter({ center: { x: H[0], y: H[1] }, speed: 1500, amplitude: 46, wavelength: 240, brightness: 1.15, radius: -1 });
    const zoom = new F.ZoomBlurFilter({ strength: 0, center: { x: H[0], y: H[1] }, innerRadius: 60 });
    const rgb = new F.RGBSplitFilter({ red: { x: 0, y: 0 }, green: { x: 0, y: 0 }, blue: { x: 0, y: 0 } });

    this.S = { app, canvas, world, ui, dim, red, halves, n, tangent, H, cutLine, slash, seamPath: pathOf(Array.from({ length: 1001 }, (_, i) => [cutLine[0][0] + (cutLine[1][0] - cutLine[0][0]) * i / 1000, cutLine[0][1] + (cutLine[1][1] - cutLine[0][1]) * i / 1000]).filter(p=>onScreen(p))), seamC, seamTex, rim, poses, silTexB, silTexW, POSE, GROUND, X0, X1, hilt: placed(0, X0 + 16, [1224, 557]), steel: [placed(3, X1, [850, 394]), placed(3, X1, [1030, 394])], click: placed(4, X1, [908, 392]), ghosts, hero, fx, fxTex, nameG, cap, capName, capMask, nameGhosts, nameT, nameMask, imp, impBg, impLines, silB, silW, flash, bars, bloom, shock, zoom, rgb };

  }

  destroy() {
    this.disposeTextures();
    for(const filter of [this.S.bloom,this.S.shock,this.S.zoom,this.S.rgb])filter.destroy();
  }

  frame(t) {
    const S = this.S;
    // Hit stop: real time keeps running (shake, impact frames) while everything else holds for 120 ms.
    const HS0 = 2080, HS1 = 2200, l = t < HS0 ? t : t < HS1 ? HS0 : t - (HS1 - HS0);

    const EXIT = 6180, END = 6600, SPLIT = 3000, FLASH = 1950, HIT = 2034, SHEATHE = 2700, CLICK = 2930;

    // Letterbox and dimming.
    framing(S,t,l,{height:118,barIn:[0,280],barOut:[EXIT+120,EXIT+500],dimIn:[[.42,0,300],[.4,600,1300],[-.4,4400,5000]],dimOut:[EXIT,END]});

    // Backdrop: stays whole through the strike; after the zanshin flick the upper piece slides off along the cut and tips,
    // the lower piece eases back the other way, and both dissolve.
    const sp = k(SPLIT, SPLIT + 850, l), e = sp * sp, o = out(sp);
    const cut = {
      up: [S.tangent[0] * 250 * e - S.n[0] * 16 * o, S.tangent[1] * 250 * e - S.n[1] * 16 * o], upRot: 0.035 * e,
      dn: [-S.tangent[0] * 80 * e + S.n[0] * 10 * o, -S.tangent[1] * 80 * e + S.n[1] * 10 * o], dnRot: -0.01 * e,
      fade: 1 - inq(k(SPLIT + 300, SPLIT + 950, l)), drift: -l * 0.006
    };
    S.halves[0].position.set(S.H[0] + cut.up[0] + cut.drift, S.H[1] + cut.up[1]); S.halves[0].rotation = cut.upRot;
    S.halves[1].position.set(S.H[0] + cut.dn[0] + cut.drift, S.H[1] + cut.dn[1]); S.halves[1].rotation = cut.dnRot;
    for (const h of S.halves) h.alpha = 0.95 * out(k(600, 1250, l)) * cut.fade;
    // The blood-moon side shows through the opening, holds a beat once the moonlit pieces are gone, then gives way to the stream.
    S.red.alpha = (l >= SPLIT ? 1 : 0) * (1 - inq(k(4400, 5000, l)));
    S.red.scale.set(1 + 0.03 * k(SPLIT, 5000, l)); S.red.position.set(960 + cut.drift, 540);
    this.drawSeam(l, k, out, cut);

    // Hero poses: the coiled lunge (it slides in for the cut-in and holds through the charge), the draw as he shoots along
    // the ground, the stop with the blade at full stretch; then he stands and the sword goes home, and only then the sky opens.
    const pose = l < FLASH ? 0 : l < HIT ? 1 : l < SHEATHE ? 2 : l < CLICK ? 3 : 4;
    const stand = (sp, i, X, y) => { const q = S.POSE[i]; sp.texture = S.poses[i]; sp.scale.set(q.s); sp.position.set(X - q.hip[0] * q.s, S.GROUND - q.sole * q.s + y); };
    const dash = k(FLASH, HIT, l), XS = S.X0 + 16;
    let X = S.X1, y = 0;
    if (pose === 0) { X = S.X0 - 980 * (1 - quint(k(250, 520, l))) + 16 * k(660, FLASH, on2(l)); y = 20 * (1 - out(k(520, 660, l))); }
    if (pose === 1) X = XS + (S.X1 - XS) * dash;
    if (pose >= 3) X += 20 * k(CLICK, 6000, on2(l));
    X -= 70 * k(EXIT, END, l);
    stand(S.hero, pose, X, y);
    S.hero.alpha = (l < 250 ? 0 : 1) * (1 - k(EXIT, END, l));
    // Afterimages trail the cut-in and hang along the ground he has just crossed.
    S.ghosts.forEach((g, i) => {
      const cutIn = l >= 250 && l < 520, trail = l >= FLASH && l < HIT + 120;
      if (cutIn) stand(g, 0, X - (i + 1) * 70, y);
      if (trail) stand(g, 1, XS + (S.X1 - XS) * Math.max(0, dash - (i + 1) * 0.22), 0);
      g.alpha = (cutIn || trail ? [0.34, 0.22, 0.14][i] : 0) * (1 - k(HIT, HIT + 120, l)); g.tint = this.palette.ghost;
    });
    S.rim.position.set(l < FLASH ? S.X0 : S.X1, 660);
    S.rim.alpha = (0.5 * out(k(400, 900, l)) + 0.5 * out(k(1500, 1950, l)) - 0.4 * k(2300, 3000, l)) * (1 - k(EXIT, END, l));
    S.rim.scale.set((1100 + 300 * k(1500, 1950, l)) / 512);

    // Camera: slow push during the charge, a punch on the strike, a long settle.
    let cam = 1 + 0.07 * inq(k(520, 1950, l));
    if (l >= 1950) cam = 1.12 - 0.12 * out(k(HS0, 2700, l)) + 0.025 * k(2700, 6000, l);
    let rot = -0.014 * inq(k(520, 1950, l)) * (l < 1950 ? 1 : 0);
    let sx = 0, sy = 0;
    const shake = (...args) => { const d = shakeAt(t,...args); sx+=d[0];sy+=d[1]; };
    shake(1950, 8, 140); shake(HS1, 30, 520); shake(CLICK + 120, 7, 130); shake(SPLIT + 120, 5, 160);
    for (const c of [S.world, S.ui]) { c.scale.set(cam); c.rotation = rot; c.position.set(960 + sx, 540 + sy); }

    // Strike effects.
    whiteFlash(S.flash,t,2034,2080);
    const impA = t >= HS0 && t < 2120, impB = t >= 2120 && t < 2160;
    impactFrame(S,impA,impB,()=>{
      S.impLines.position.set(S.H[0],S.H[1]);
      for (const sprite of [S.silB,S.silW]) { stand(sprite,2,S.X1,0);sprite.texture=sprite===S.silB?S.silTexB:S.silTexW; }
    });
    const released = t >= HS1, sw = released && l < HS0 + 520;
    S.shock.time = (l - HS0) / 1000; S.shock.amplitude = 46 * (1 - k(HS0, HS0 + 520, l));
    S.zoom.strength = 0.15 * (1 - out(k(HS0, HS0 + 300, l)));
    const rs = 11 * (1 - out(k(HS0, HS0 + 480, l)));
    S.rgb.red = { x: -rs, y: 0 }; S.rgb.blue = { x: rs, y: 0 };
    const filters = [S.bloom];
    if (sw) filters.push(S.shock);
    if (released && S.zoom.strength > 0.005) filters.push(S.zoom);
    if (released && rs > 0.5) filters.push(S.rgb);
    S.world.filters = S.imp.visible ? null : filters;

    // Name: cut out along the slash, with red afterimages.
    revealName(S,l,{start:2380,end:2640,ghostIn:2430,ghostOut:2560,ghostEnd:2950,exit:EXIT,finish:END});

    // Entry caption: the name wipes in at the hero's feet; the strike's wind sweeps it off.
    caption(S,l,{start:420,revealStart:560,revealEnd:860,exitStart:1950,exitEnd:2060,x:1000,y:900});

    this.drawFx(t, l, k, out, inq, SHEATHE, CLICK);
    S.fxTex.source.update();
  }

  // The straight cut hangs in the sky behind the hero; when the sky gives way each piece carries a lit edge and the seam flares.
  drawSeam(l, k, out, cut) {
    const S = this.S, g = S.seamC.getContext('2d');
    g.clearRect(0, 0, 1920, 1080);
    if (l >= 2080 && l < 3950) {
      g.globalCompositeOperation = 'lighter'; g.lineCap = 'round'; g.lineJoin = 'round';
      const path = (pts, dx, dy, rot) => { const c = Math.cos(rot), s = Math.sin(rot), [hx, hy] = S.H; g.beginPath(); pts.forEach((p, i) => { const x = p[0] - hx, y = p[1] - hy, X = hx + x * c - y * s + dx, Y = hy + x * s + y * c + dy; i ? g.lineTo(X, Y) : g.moveTo(X, Y); }); };
        const line = (w,color) => stroke(g,w,color);
      const open = out(k(3000, 3260, l)), a = k(2080, 2200, l) * (1 - k(3450, 3950, l)), flick = 0.8 + 0.2 * Math.sin(l * 0.045);
      path(S.cutLine, cut.drift, 0, 0);
      line(12 + 40 * open, 'rgba('+this.palette.seamGlow+',' + 0.2 * a * flick + ')');
      line(3 + 10 * open, 'rgba('+this.palette.seamEdge+',' + 0.6 * a + ')');
      line(1.4 + 3 * open, 'rgba(255,255,255,' + a * (1 - 0.6 * open) + ')');
      if (l >= 3000) for (const [d, r] of [[cut.up, cut.upRot], [cut.dn, cut.dnRot]]) {
        path(S.cutLine, d[0] + cut.drift, d[1], r);
        line(10, 'rgba('+this.palette.seamLip+',' + 0.35 * cut.fade * open + ')');
        line(2, 'rgba('+this.palette.seamCore+',' + 0.9 * cut.fade * open + ')');
      }
      g.globalCompositeOperation = 'source-over';
    }
    S.seamTex.source.update();
  }

  // Slash ribbon, charge particles, sparks and embers, drawn additively each frame.
  drawFx(t, l, k, out, inq, SHEATHE, CLICK) {
    const S = this.S, g = S.fx.getContext('2d');
    g.clearRect(0, 0, 1920, 1080);
    g.globalCompositeOperation = 'lighter';

    const draw = drawing(g, g, 70);
    const band = (u0,u1,w,inner,rgb,alpha,path=S.slash) => draw.band(path,u0,u1,w,inner,rgb,alpha);
    const arcLine = (u0,u1,off,width,color) => { g.lineCap='round';draw.arcLine(S.slash,u0,u1,off,width,color,g); };

    // Charge: sparks are pulled into the hilt while the hero holds the stance.
    const hilt = S.hilt;
    if (l > 650 && l < 1960) {
      g.lineCap = 'round';
      for (let i = 0; i < 110; i++) {
        const s0 = 650 + rnd(i, 1) * 1150, dur = 380 + rnd(i, 2) * 260, p = (l - s0) / dur;
        if (p < 0 || p > 1) continue;
        const ang = rnd(i, 3) * Math.PI * 2, r0 = 360 + rnd(i, 4) * 420, r = r0 * (1 - p * p), r2 = r0 * (1 - Math.max(0, p - 0.12) ** 2);
        g.strokeStyle = i % 3 ? 'rgba('+this.palette.charge+',' + (0.9 * Math.sin(p * Math.PI)) + ')' : 'rgba(255,255,255,' + Math.sin(p * Math.PI) + ')';
        g.lineWidth = 1.5 + rnd(i, 5) * 2;
        g.beginPath(); g.moveTo(hilt[0] + Math.cos(ang) * r, hilt[1] + Math.sin(ang) * r); g.lineTo(hilt[0] + Math.cos(ang) * r2, hilt[1] + Math.sin(ang) * r2); g.stroke();
      }
      // A glint runs up the scabbard just before the draw.
      const gl = k(1780, 1940, l);
      if (gl > 0) {
        const a = Math.sin(gl * Math.PI);
        g.strokeStyle = 'rgba(255,255,255,' + a + ')'; g.lineWidth = 3;
        g.beginPath(); g.moveTo(hilt[0] - 60, hilt[1]); g.lineTo(hilt[0] + 60, hilt[1]); g.moveTo(hilt[0], hilt[1] - 120 * a); g.lineTo(hilt[0], hilt[1] + 120 * a); g.stroke();
      }
    }

    // The slash: a sharp crescent sweeps out, holds bright through the hit stop, then frays into strands.
    if (l >= 1960 && l < 2700) {
      const head = out(k(1960, 2060, l)), tail = 0.5 * out(k(2000, 2120, l)), fray = k(2080, 2600, l);
      const w = 62 * (1 - 0.55 * fray), a = 1 - fray;
      band(tail, head, w * 2.6, 0.25, this.palette.slashAura, 0.16 * a);
      band(tail, head, w, 0.08, this.palette.slash, 0.8 * a);
      band(tail, head, w * 0.34, 0.03, this.palette.slashCore, a);
      for (let s = 0; s < 7; s++) {
        const off = 10 + s * w * 0.12, len = 0.55 + rnd(s, 21) * 0.4;
        arcLine(Math.max(tail, head - len), head - rnd(s, 22) * 0.08, off, 1 + (s % 2), 'rgba('+this.palette.strand[0]+',' + (this.palette.strand[1] - s * 18) + ',' + (this.palette.strand[2] - s * 18) + ',' + (0.55 * a) + ')');
      }
      const head2 = out(k(1985, 2085, l));
      arcLine(Math.max(tail, head2 - 0.7), head2, w * 1.35, 2, 'rgba(255,255,255,' + 0.8 * a + ')');
      if (fray > 0) {
        for (let s = 0; s < 9; s++) {
          const off = (s / 8 - 0.3) * 90 + fray * 50 * (s - 3), u0 = 0.08 + fray * (0.2 + rnd(s, 7) * 0.5), u1 = 1 - fray * (0.1 + rnd(s, 8) * 0.6);
          if (u1 > u0) arcLine(u0, u1, off, 1 + rnd(s, 9) * 1.6, 'rgba('+this.palette.fray[0]+',' + (this.palette.fray[1] + s * 12) + ','+this.palette.fray[2]+',' + (0.6 * (1 - fray)) + ')');
        }
      }
    }
    // The bare steel catches the light as it slides home; a hard glint on the guard marks the click, and then the sky gives way.
    if (l >= SHEATHE && l < CLICK) {
      const a = Math.sin(k(SHEATHE, CLICK, l) * Math.PI), [p, q] = S.steel;
      g.strokeStyle = 'rgba(255,255,255,' + 0.85 * a + ')'; g.lineWidth = 3; g.lineCap = 'round';
      g.beginPath(); g.moveTo(p[0], p[1]); g.lineTo(q[0], q[1]); g.stroke();
    }
    const fl = k(CLICK, CLICK + 200, l);
    if (fl > 0 && fl < 1) {
      const a = Math.sin(fl * Math.PI), [gx, gy] = S.click;
      g.strokeStyle = 'rgba(255,255,255,' + a + ')'; g.lineWidth = 2.5; g.lineCap = 'round';
      g.beginPath(); g.moveTo(gx - 120 * a, gy); g.lineTo(gx + 120 * a, gy); g.moveTo(gx, gy - 70 * a); g.lineTo(gx, gy + 70 * a); g.stroke();
    }
    // Embers peel off both lips of the seam as it opens.
    if (l >= 3000 && l < 4300) {
      const e = l - 3000;
      for (let i = 0; i < 130; i++) {
        const t0 = rnd(i, 30) * 520, life = 600 + rnd(i, 31) * 700, q = (e - t0) / life;
        if (q <= 0 || q >= 1) continue;
        const [x0, y0, nx, ny] = S.seamPath.at(rnd(i, 32)), side = i % 2 ? 1 : -1, v = 30 + rnd(i, 33) * 110;
        const px = x0 + (nx * v * 1.4 + ny * 50) * side * q, py = y0 + (ny * v * 1.4 - nx * 50) * side * q - 40 * q;
        g.fillStyle = 'rgba('+this.palette.seamEmber[0]+',' + (this.palette.seamEmber[1] + (i % 3) * 50) + ','+this.palette.seamEmber[2]+',' + Math.sin(q * Math.PI) * 0.9 + ')';
        g.beginPath(); g.arc(px, py, 1.2 + rnd(i, 34) * 2.2, 0, Math.PI * 2); g.fill();
      }
    }
    // Sparks thrown off the hit, mostly across the cut.
    g.lineCap='round';
    sparks(g,l-2080,160,1,10,(i,seed)=>S.slash.at(.2+rnd(i,seed+1)*.7),this.palette.spark);
    // Embers rise during the zanshin hold.
    if (l > 2300 && l < 6600) {
      const fade = k(2300, 2800, l) * (1 - k(6200, 6600, l));
      for (let i = 0; i < 46; i++) {
        const life = 2200 + rnd(i, 16) * 1800, ph = ((l + rnd(i, 17) * life) % life) / life;
        const px = 200 + rnd(i, 18) * 1700 + Math.sin(ph * 6 + i) * 20, py = 1000 - ph * 520 - rnd(i, 19) * 200;
        g.fillStyle = 'rgba('+this.palette.ember[0]+',' + (this.palette.ember[1] + (i % 3) * 50) + ','+this.palette.ember[2]+',' + (Math.sin(ph * Math.PI) * fade * 0.8) + ')';
        g.beginPath(); g.arc(px, py, 1.5 + rnd(i, 20) * 2.5, 0, Math.PI * 2); g.fill();
      }
    }
    g.globalCompositeOperation = 'source-over';
  }

}
