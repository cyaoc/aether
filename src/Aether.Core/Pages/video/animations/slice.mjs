import {inside, k, out, inq, quint, on2, rnd, logicalTime, spline, pathOf, chord, seamsOf, sidesOf, shakes, drawing, beginFx, stroke, sparks, charge, seamEmbers, risingEmbers, steelGlint} from './toolkit.mjs';
import {createStage, createFx, createFigure, createStrike, createName, createCaption, framing, sky, bloodSky, revealName, caption, impactFrame, whiteFlash, strikeFilters, aim} from './scenes.mjs';

export class Slice {
  constructor({app, images, character, kit, nickname}) {
    const P = PIXI;
    const {tex, silhouette, post} = kit;
    this.palette=character.palette;
    const {moon, impact, 'moon-red': moonRed} = images;

    // Per pose: the hips and the soles in image pixels, and the scale that makes the head the same size on screen.
    const scales = [0.63,0.7,0.7,0.77,0.77];
    const POSE = character.poses.slice.map((pose,i)=>({...pose,s:scales[i]}));
    const [p0, p1, p2, p3, p4] = POSE.map(pose => images[pose.image]);
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
    // tip), run right across the sky.
    const ca = slash.at(0.5).slice(0, 2), cd = [H[0] - ca[0], H[1] - ca[1]], cm = Math.hypot(cd[0], cd[1]);
    const tangent = [cd[0] / cm, cd[1] / cm], n = [-tangent[1], tangent[0]];
    const cutLine = chord(ca, H);
    const sides = sidesOf(cutLine);

    const {world, ui, dim} = createStage(P, app, kit, [3000, 2000]);

    // What the cut reveals: the same sky's blood-moon side, sitting behind the moonlit halves.
    const red = new P.Container(); red.pivot.set(960, 540); red.position.set(960, 540);
    sky(P,kit,moonRed,red,[-120,-200,2160,1440],[2600,1700]);
    world.addChild(red);

    // Backdrop: one moonlit sky, the moon up on the right, ahead of the hero and clear of the cut (splitting the moon is 60 档's).
    // After the zanshin the side with the moon slides off along the straight cut.
    const moonSide = inside(sides[0], 1295, 210) ? 0 : 1;
    const halves = [sides[moonSide], sides[1 - moonSide]].map(poly => {
      const c = new P.Container();
      sky(P,kit,moon,c,[-120,-200,2160,1440],[2600,1700]);
      const m = new P.Graphics().poly(poly.flat()).fill(0xffffff); c.addChild(m); c.mask = m;
      c.pivot.set(H[0], H[1]); c.position.set(H[0], H[1]);
      world.addChild(c); return c;
    });
    const seam = createFx(P, kit, world, 1920, 1080);

    const poses = [p0, p1, p2, p3, p4].map(tex);
    const {rim, ghosts, hero} = createFigure(P, kit, world, this.palette, poses[0], 3);

    const fx = createFx(P, kit, world, 1920, 1080);

    const nameG = new P.Container(); nameG.position.set(70, 330); nameG.rotation = -0.12; ui.addChild(nameG);
    const {nameGhosts,nameT,nameMask}=createName(P,nameG,nickname,this.palette.nameGhost);
    // Entry caption: says who is coming in from the cut-in, until the strike sweeps it away.
    const cap = new P.Container(); cap.position.set(1000, 900); ui.addChild(cap);
    const {capName,capMask}=createCaption(P,cap,nickname,780);

    const {imp, impBg, impLines, flash} = createStrike(P, kit, world, impact, [-400, -400, 2720, 1880], 4400);
    const silB = new P.Sprite(silhouette(p2, '#000')), silW = new P.Sprite(silhouette(p2, '#fff'));
    imp.addChild(silB, silW);
    const silTexB = silB.texture, silTexW = silW.texture;

    const bars = [new P.Graphics(), new P.Graphics()]; app.stage.addChild(...bars);

    post.shock.center = post.zoom.center = { x: H[0], y: H[1] };

    this.S = { world, ui, dim, red, halves, n, tangent, H, cutLine, slash, seams: seamsOf([cutLine]), seam, rim, poses, silTexB, silTexW, POSE, GROUND, X0, X1, hilt: placed(0, X0 + 16, [1224, 557]), steel: [placed(3, X1, [850, 394]), placed(3, X1, [1030, 394])], click: placed(4, X1, [908, 392]), ghosts, hero, fx, nameG, cap, capName, capMask, nameGhosts, nameT, nameMask, imp, impBg, impLines, silB, silW, flash, bars, ...post };

  }

  frame(t) {
    const S = this.S;
    // Hit stop: real time keeps running (shake, impact frames) while everything else holds for 120 ms.
    const HS0 = 2080, HS1 = 2200, l = logicalTime(t, [[HS0, HS1 - HS0]]);

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
    bloodSky(S.red, l, SPLIT, 4400, 5000, cut.drift);
    this.drawSeam(l, cut);

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
    const rot = -0.014 * inq(k(520, 1950, l)) * (l < 1950 ? 1 : 0);
    const [sx, sy] = shakes(t, [[1950, 8, 140], [HS1, 30, 520], [CLICK + 120, 7, 130], [SPLIT + 120, 5, 160]]);
    aim(S, [960, 540], [960 + sx, 540 + sy], cam, rot);

    // Strike effects.
    whiteFlash(S.flash,t,2034,2080);
    impactFrame(S,t,HS0,()=>{
      S.impLines.position.set(S.H[0],S.H[1]);
      for (const sprite of [S.silB,S.silW]) { stand(sprite,2,S.X1,0);sprite.texture=sprite===S.silB?S.silTexB:S.silTexW; }
    });
    strikeFilters(S,t,l,{hit:HS0,release:HS1,amplitude:46,zoom:.15,split:11});

    // Name: cut out along the slash, with red afterimages.
    revealName(S,l,{start:2380,end:2640,ghostIn:2430,ghostOut:2560,ghostEnd:2950,exit:EXIT,finish:END});

    // Entry caption: the name wipes in at the hero's feet; the strike's wind sweeps it off.
    caption(S,l,{start:420,revealStart:560,revealEnd:860,exitStart:1950,exitEnd:2060,x:1000,y:900});

    this.drawFx(l, SHEATHE, CLICK);
    S.fx.update();
  }

  // The straight cut hangs in the sky behind the hero; when the sky gives way each piece carries a lit edge and the seam flares.
  drawSeam(l, cut) {
    const S = this.S, g = S.seam.g;
    beginFx([g], [1, 0, 0, 1, 0, 0]);
    if (l >= 2080 && l < 3950) {
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
    }
    S.seam.update();
  }

  // Slash ribbon, charge particles, sparks and embers, drawn additively each frame.
  drawFx(l, SHEATHE, CLICK) {
    const S = this.S, palette = this.palette, g = S.fx.g;
    g.clearRect(0, 0, 1920, 1080);
    g.globalCompositeOperation = 'lighter'; g.lineCap = 'round';
    const {glint, slash} = drawing(g, g, palette, 70);

    // Charge: sparks are pulled into the hilt while the hero holds the stance.
    const hilt = S.hilt;
    if (l > 650 && l < 1960) {
      charge(g, l, {hilt, span: 1150, count: 110, color: palette.charge});
      // A glint runs up the scabbard just before the draw.
      const gl = k(1780, 1940, l);
      if (gl > 0) {
        const a = Math.sin(gl * Math.PI);
        g.strokeStyle = 'rgba(255,255,255,' + a + ')'; g.lineWidth = 3;
        g.beginPath(); g.moveTo(hilt[0] - 60, hilt[1]); g.lineTo(hilt[0] + 60, hilt[1]); g.moveTo(hilt[0], hilt[1] - 120 * a); g.lineTo(hilt[0], hilt[1] + 120 * a); g.stroke();
      }
    }

    // The slash: a sharp crescent sweeps out, holds bright through the hit stop, then frays into strands.
    if (l >= 1960 && l < 2700)
      slash(S.slash, {head: out(k(1960, 2060, l)), tail: 0.5 * out(k(2000, 2120, l)), head2: out(k(1985, 2085, l)), width: 62, fray: k(2080, 2600, l), seed: 21, span: 1, base: 0, fraySeed: 7});
    // The bare steel catches the light as it slides home; a hard glint on the guard marks the click, and then the sky gives way.
    steelGlint(g, l, SHEATHE, CLICK, ...S.steel);
    glint(...S.click, k(CLICK, CLICK + 200, l), 120, 70);
    seamEmbers(g, l, {start: 3000, span: 1300, count: 130, seams: S.seams, color: palette.seamEmber, shear: 50});
    // Sparks thrown off the hit, mostly across the cut.
    sparks(g,l-2080,160,1,10,(i,seed)=>S.slash.at(.2+rnd(i,seed+1)*.7),palette.spark);
    risingEmbers(g, l, {from: 2300, to: 6600, color: palette.ember});
  }

}
