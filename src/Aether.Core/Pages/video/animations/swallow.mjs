import {C0, cutCells, canvasOf, k, out, inq, quint, smooth, on2, rnd, mix, lerp, logicalTime, realTimes, spline, pathOf, scarsOf, seamsOf, shakes, projection, drawing, beginFx, gather, glow, spray, pillar, stones, seamEmbers, risingEmbers, flareScars, pieceLips, steelGlint} from './toolkit.mjs';
import {createStage, createFx, createFigure, createStrike, createName, createCaption, createPieces, scatter, framing, bloodSky, caption, revealName, impactFrame, whiteFlash, strikeFilters, aim} from './scenes.mjs';

export class Swallow {
  constructor({app,images,character,kit,nickname}) {
    const P=PIXI;
    const {tex,silhouette,radial,post}=kit;
    this.palette=character.palette;
    const {moon, impact, 'moon-red': moonRed} = images;

    // The action chain: five cuts, each mid-cut pose -> finishing pose. Every pose is placed by its hips (hip, in its own
    // image's pixels) at the scale s that makes the blade the same length. The images hold the figure only: the light is drawn
    // here, along the path of the blade tip. AT is where the hips sit in the world (the ground is y = 1180): he starts on the
    // left and the moon hangs ahead of him on the right. Every cut starts where the last one ended, and only the last is a
    // full chop: he rises, cuts back down his own line, turns on through a level sweep, and the same turn carries the blade up
    // behind his head, so it is already raised at the top of the leap.
    const keys = ['stance', 'smear1', 'cut1', 'smear2', 'cut2', 'smear3', 'cut3', 'smear4', 'cut4', 'wind', 'smear5', 'land', 'into', 'home'];
    const layout = [0.79, 0.8, 0.78, 0.71, 0.62, 0.77, 0.77, 0.82, 0.89, 0.69, 0.83, 0.71, 0.74, 0.74];
    const PO = {};
    keys.forEach((key, i) => { const {image, hip} = character.poses.swallow[key], c = images[image]; PO[key] = { img: c, tex: tex(c), hip, s: layout[i] }; });
    const AT = { stance: [700, 706], smear1: [860, 560], cut1: [990, 430], smear2: [1200, 200], cut2: [980, 240], smear3: [960, 300], cut3: [1000, 300], smear4: [960, 160], cut4: [960, 130], wind: [960, -40], smear5: [1010, 460], land: [1060, 824], into: [1060, 681], home: [1060, 679] };
    const W = (key, q, hip = AT[key]) => { const p = PO[key]; return [hip[0] + (q[0] - p.hip[0]) * p.s, hip[1] + (q[1] - p.hip[1]) * p.s]; };
    const TIP0 = W('stance', [428, 873]), TIP1 = W('cut1', [1084, 30]), TIP2 = W('cut2', [150, 1037]), TIP3 = W('cut3', [1500, 156]), TIP4 = W('cut4', [274, 31]), TIPW = W('wind', [189, 68]), TIP5 = W('land', [1393, 867]);
    // Cut 1 carries the hero up this far while it holds, so cut 2 starts from the risen tip.
    const CLIMB = 160, TIP1R = [TIP1[0] + 15, TIP1[1] - CLIMB];

    // A slash through measured points; the spans listed in behind pass behind the hero's body.
    const slashPath = (pts, behind = []) => { const s = spline(pts); return pathOf(s.pts, s.seg.map(i => behind.includes(i))); };

    // 1 rising: from the tip down by the back foot, low past the legs (behind them) and up into the raised tip.
    // 2 swallow-return: from the risen tip straight back down the way it came, to the lower left.
    // 3 sweep: the turn goes on; the tip comes round low in front of him and runs out flat to the right.
    // 4 rising: the same turn lifts it, a long diagonal from the right across the top of him to the upper left.
    // 5 plunge: from the blade already behind his head, over and down, the one full-power cut, into the ground.
    // 6 the blade coming back out of the ground, up to the scabbard mouth as he stands.
    const blades = [
      slashPath([TIP0, [640, 1070], [1000, 980], [1320, 720], W('smear1', [1102, 389]), [1460, 70], TIP1], [0, 1]),
      slashPath([TIP1R, [1130, -260], W('smear2', [125, 300]), [760, 210], TIP2]),
      slashPath([TIP2, [850, 300], [1100, 180], [1420, 140], TIP3]),
      slashPath([TIP3, [1560, -50], [1240, -310], W('smear4', [480, 31]), TIP4]),
      slashPath([TIPW, [760, -580], [1100, -530], [1400, -280], W('smear5', [1074, 764]), TIP5]),
      slashPath([TIP5, [1570, 830], [1370, 680], W('into', [850, 394])])
    ];

    // The cuts a straight blade leaves are straight: each is the chord of the stretch of its slash still lit on the frame
    // it lands (from the slash's tail to the blade tip), run right across the sky. The moon sits whole in the wedge the
    // cuts leave open on the right (cutting the moon is 60 档's).

    // TAILS: how much of each slash has already drawn in by the frame it lands (the slashes below use the same values).
    const TAILS = [0.4, 0.35, 0.4, 0.35, 0.55];

    const scars = scarsOf(blades, TAILS);

    // The five cuts all run on right across the sky, so the sky breaks along the very lines the viewer saw land. They
    // fan out from near one point above the hero's leap; each cell is a big disc clipped by one side of each line. Every
    // cell the closing wide shot can see is kept, however thin, so no hole opens before the break.
    const VIEW = [-300, -800, 2600, 1300];
    const cells = cutCells(scars,C0,5000,VIEW);

    const {world, ui, dim} = createStage(P, app, kit, [4400, 3400]);

    // The sky is set so the moon is at MOON (radius about 297), up and right of the leap, ahead of the hero.
    // It is padded so the wide shots and tilts never run off its edge: mirror images of itself at the sides (no moon
    // near them), its top and bottom strips flipped and stretched above and below (a full mirror there would show a
    // second moon), the top one fading into the dark of the zenith.
    const MOON = [2000, -220], SKY = 1.5625, PAD = 520;
    const padded = img => {
      const w = img.width + 2 * PAD, h = img.height;
      const row = canvasOf(w, h, g => { for (let i = -1; i <= 1; i++) { const fx = i !== 0; g.save(); g.translate(PAD + i * img.width + (fx ? img.width : 0), 0); g.scale(fx ? -1 : 1, 1); g.drawImage(img, 0, 0); g.restore(); } });
      return canvasOf(w, h + 2 * PAD, g => {
        g.drawImage(row, 0, PAD);
        g.save(); g.translate(0, PAD); g.scale(1, -1); g.drawImage(row, 0, 0, w, 100, 0, 0, w, PAD); g.restore();
        const top = g.createLinearGradient(0, 0, 0, PAD + 60); top.addColorStop(0, 'rgba(6,8,13,1)'); top.addColorStop(1, 'rgba(6,8,13,0)'); g.fillStyle = top; g.fillRect(0, 0, w, PAD + 60);
        g.save(); g.translate(0, h + 2 * PAD); g.scale(1, -1); g.drawImage(row, 0, h - 100, w, 100, 0, 0, w, PAD); g.restore();
      });
    };
    const moonTex = tex(padded(moon)), redTex = tex(padded(moonRed));
    const shadeTex = radial([[0, 'rgba(0,0,0,0)'], [0.7, 'rgba(0,0,0,0.35)'], [1, 'rgba(0,0,0,0.8)']]);
    const skyOf = (t, parent) => {
      const sky = new P.Sprite(t); sky.scale.set(SKY); sky.position.set(MOON[0] - (1006 + PAD) * SKY, MOON[1] - (292 + PAD) * SKY); parent.addChild(sky);
      const shade = new P.Sprite(shadeTex); shade.anchor.set(0.5); shade.position.set(960, 540); shade.width = 3800; shade.height = 2800; parent.addChild(shade);
    };
    const red = new P.Container(); red.pivot.set(960, 540); red.position.set(960, 540); skyOf(redTex, red); world.addChild(red);
    // Pieces fly out from where the cuts fan out, above the leap.
    const BURST = [1400, -100];
    const pieces = createPieces(P,cells,world,host=>skyOf(moonTex,host),BURST,{base:240,max:120,factor:.25,rotation:.03,down:.5});
    // Effects canvases cover a margin round the screen, since the camera swings a long way and pulls back to 0.71.
    const OX = 760, OY = 560;
    const back = createFx(P, kit, world, 1920 + 2 * OX, 1080 + 2 * OY, -OX, -OY);

    const {rim, ghosts, hero} = createFigure(P, kit, world, this.palette, PO.stance.tex, 2);

    const fx = createFx(P, kit, world, 1920 + 2 * OX, 1080 + 2 * OY, -OX, -OY);

    // The name is cut out in the closing wide shot, in the open sky to the left of where the cuts fan out.
    const NAME_AT = [640, 260];
    const nameG = new P.Container(); nameG.position.set(...NAME_AT); nameG.rotation = -0.06; ui.addChild(nameG);
    const {nameGhosts,nameT,nameMask}=createName(P,nameG,nickname,this.palette.nameGhost);
    // Entry caption: says who is coming in from the cut-in, until the take-off's wind sweeps it away. It sits on the
    // ground in front of the hero and is drawn larger, since the opening shot is wide.
    const cap = new P.Container(); cap.position.set(1240, 1040); cap.scale.set(1.3); ui.addChild(cap);
    const {capName,capMask}=createCaption(P,cap,nickname,600);

    const {imp, impBg, impLines, flash} = createStrike(P, kit, world, impact, [-1800, -1800, 5520, 4680], 5200);
    const silB = new P.Sprite(silhouette(PO.land.img, '#000')), silW = new P.Sprite(silhouette(PO.land.img, '#fff'));
    for (const s of [silB, silW]) { s.pivot.set(...PO.land.hip); s.scale.set(PO.land.s); s.position.set(...AT.land); imp.addChild(s); }

    const bars = [new P.Graphics(), new P.Graphics()]; app.stage.addChild(...bars);

    this.S = {
      world, ui, dim, red, pieces, scars, blades, TAILS, PO, AT, W, TIP5, SKYD: 400, NAME_AT, OX, OY, back, rim, ghosts, hero, fx,
      seams: seamsOf(scars, VIEW),
      nameG, cap, capName, capMask, nameGhosts, nameT, nameMask, imp, impBg, impLines, silB, silW, flash, bars, ...post
    };

  }

  frame(t) {
    const S = this.S;
    // Five hit stops, at logical times L1–L5: cut 1 holds 90 ms of real time, the air cuts 60, 50 and 50 as they come
    // faster and faster, and the landing 160 (after its white frame). Real time keeps running (shake, impact frames)
    // while everything else holds. HR are the real times they start; REL5 is when the landing lets go.
    const L1 = 1724, L2 = 1920, L3 = 2040, L4 = 2130, L5 = 2719;
    const HS = [[L1, 90], [L2, 60], [L3, 50], [L4, 50], [L5, 180]], HR = realTimes(HS), REL5 = HR[4] + HS[4][1];
    const l = logicalTime(t, HS);

    // The chain, one beat per pose; smear frames hold two frames. The gaps between the hits shrink (286, 180, 140 ms of
    // real time) and the longest hold comes right before the last one: the blade raised overhead for 340 ms.
    const SM1 = 1640, SM2 = 1836, SM3 = 1956, SM4 = 2046, WIND = 2170, SM5 = 2510, LAND = 2677;
    const WAVE2 = 2899, BACK0 = 3083, ZAN = 3463, NAME = 3503, INTO = 3483, HOME = 3693;
    const FLARE = 3733, SPLIT = 4103, RED0 = 5770, RED1 = 6370, EXIT = 7550, END = 7970;

    // Letterbox and dimming.
    framing(S,t,l,{height:118,barIn:[0,280],barOut:[EXIT+120,EXIT+500],dimIn:[[.42,0,300],[.4,600,1300],[-.4,RED0,RED1]],dimOut:[EXIT,END]});

    // Backdrop: whole through the move; after the zanshin the five scars flare in cut order, then the sky breaks along
    // them and every piece slides off away from the hero, faster and faster, and dissolves.
    // The sky is far off, so from the ground it hangs lower: the moon is in the opening shot and the crossing of the cuts in the closing one.
    const scarY = l >= LAND ? S.SKYD : 0, skyY = l >= LAND ? S.SKYD : S.SKYD * (1 - k(SM1, L3, l));
    const sp = k(SPLIT, SPLIT + 850, l), e = sp * sp, drift = -l * 0.006;
    const fade = 1 - inq(k(SPLIT + 400, SPLIT + 1000, l));
    scatter(S.pieces, e, drift, skyY, 0.95 * out(k(600, 1250, l)) * fade);
    bloodSky(S.red, l, SPLIT, RED0, RED1, drift, skyY);

    // Hero: one pose per beat, placed by its hips.
    const keyAt = l < SM1 ? 'stance' : l < L1 ? 'smear1' : l < SM2 ? 'cut1' : l < L2 ? 'smear2' : l < SM3 ? 'cut2' : l < L3 ? 'smear3' : l < SM4 ? 'cut3' : l < L4 ? 'smear4' : l < WIND ? 'cut4' : l < SM5 ? 'wind' : l < LAND ? 'smear5' : l < INTO ? 'land' : l < HOME ? 'into' : 'home';
    const po = S.PO[keyAt];
    let [hx, hy] = S.AT[keyAt];
    // The stance slides in from the left dragging its tip, then edges forward through the charge.
    const stanceX = x => 700 - 1100 * (1 - quint(k(250, 520, x))) - 16 * (1 - k(660, SM1, on2(x)));
    if (keyAt === 'stance') { hx = stanceX(l); hy += 20 * (1 - out(k(520, 660, l))); }
    // The rising slash carries the hero up; the air cuts follow at once.
    if (keyAt === 'cut1') { const p = out(k(L1, SM2, on2(l))); hx += 15 * p; hy -= 160 * p; }
    if (keyAt === 'cut2') hy -= 10 * k(L2, SM3, l);
    // The chop's recoil lifts the hero into the wind-up, which settles a little higher while it holds.
    if (keyAt === 'wind') { const p = out(k(WIND, SM5, on2(l))); hx += 10 * (1 - p); hy += 40 * (1 - p); }
    // The plunge falls right through the frame, one step every 42 ms, and out of the bottom before the cut to the ground.
    const plunge = keyAt === 'smear5' ? (Math.floor((l - SM5) / 42) + 1) / 4 : 0;
    if (keyAt === 'smear5') hy = mix(S.AT.wind[1], hy, plunge);
    if (keyAt === 'into') hy += 80 * (1 - quint(k(INTO, INTO + 100, l)));
    if (l >= ZAN) hx += 20 * k(ZAN, ZAN + 3100, on2(l));
    hx -= 70 * k(EXIT, END, l);
    const place = (s, p, x, y) => { s.texture = p.tex; s.pivot.set(p.hip[0], p.hip[1]); s.scale.set(p.s); s.position.set(x, y); };
    place(S.hero, po, hx, hy);
    S.hero.alpha = (l < 250 ? 0 : 1) * (1 - k(EXIT, END, l));
    // Afterimages trail the cut-in, the take-off, the climb and the air cuts, opposite to the motion.
    const gOff = l < 520 ? [-70, 0] : keyAt === 'smear1' ? [-14, 90] : keyAt === 'cut1' && l > L1 ? [0, 70] : keyAt === 'smear2' ? [34, -38] : keyAt === 'smear3' ? [-40, 10] : keyAt === 'smear4' ? [0, 44] : keyAt === 'smear5' ? [0, -130] : null;
    S.ghosts.forEach((g, i) => {
      place(g, po, hx + (gOff ? (i + 1) * gOff[0] : 0), hy + (gOff ? (i + 1) * gOff[1] : 0));
      g.alpha = gOff && l >= 250 ? (i ? 0.16 : 0.32) : 0; g.tint = this.palette.ghost;
    });
    // The red glow stays low while the hero is in the air, so it never tints the moon.
    const air = k(L1, L1 + 120, l) * (1 - k(LAND, LAND + 120, l));
    S.rim.position.set(hx, hy - 140);
    S.rim.alpha = (0.5 * out(k(400, 900, l)) + 0.5 * out(k(1100, SM1, l)) - 0.4 * k(L5 + 240, L5 + 940, l)) * (1 - 0.85 * air) * (1 - k(EXIT, END, l));
    S.rim.scale.set((1100 + 300 * k(1100, SM1, l)) / 512);

    // Camera: a centre (cx, cy) in world units and a zoom. A wide low shot shows the tip on the ground through the
    // charge; it whips up with the rising slash, then follows each air cut's blade: down-left with the swallow-return,
    // up with the spin, down with the chop. It settles on the wind-up beside the moon and pushes in slowly while the
    // blade is held overhead; the plunge's two smear frames leave it behind (the smear shoots off the bottom of the frame),
    // then it cuts down onto the ground and the buried tip for the landing. As the hero rises it pulls back up into a wide
    // shot of the sky the five cuts crossed, at the zoom they were cut at, so the sky breaks along lines the viewer has seen
    // land: the crossing of the air cuts sits high in the middle with the moon whole beside it, as when they were cut, and
    // the hero stands and sheathes his sword under it, the whole figure in frame.
    let cx = 1100, cy = 656, z = 0.88 + 0.03 * inq(k(520, SM1, l)), rot = -0.008 * inq(k(520, SM1, l)), p;
    if (l >= SM1) { p = out(k(SM1, L1, l)); cx = mix(1100, 1180, p); cy = mix(656, 230, p); z = 0.91 + 0.01 * p; rot = -0.008 * (1 - p); }
    if (l >= L1) { cy = 230 - 100 * out(k(L1, SM2, l)); z = 0.92; rot = 0; }
    if (l >= SM2) { p = out(k(SM2, L2, l)); cx = mix(1180, 1000, p); cy = mix(130, 100, p); z = mix(0.92, 0.86, p); }
    if (l >= SM3) { p = out(k(SM3, L3, l)); cx = mix(1000, 1180, p); cy = mix(100, 200, p); z = 0.86; }
    if (l >= SM4) { p = out(k(SM4, L4, l)); cx = mix(1180, 1100, p); cy = mix(200, -160, p); z = 0.86; }
    if (l >= WIND) { p = out(k(WIND, WIND + 120, l)); cx = mix(1100, 1080, p); cy = mix(-160, -60, p); z = mix(0.86, 0.88, p) + 0.04 * inq(k(WIND + 120, SM5, l)); }
    if (l >= SM5) { cx = 1080; cy = -60; z = 0.92; }
    if (l >= LAND) { cx = 1240; cy = 676; z = 0.95; rot = 0.012 * (1 - out(k(L5, BACK0, l))); }
    if (l >= BACK0) { p = smooth(k(BACK0, ZAN, l)); cx = mix(1240, 1270, p); cy = mix(676, 620, p); z = mix(0.95, 0.9, p) + 0.015 * k(ZAN, ZAN + 3300, l); rot = 0; }
    // Each air cut shakes harder than the last.
    const [sx, sy] = shakes(t, [[HR[0], 16, 240, [0.36, -0.93]], [HR[1], 10, 170, [-0.66, 0.75]], [HR[2], 12, 170, [1, 0]], [HR[3], 15, 200, [-0.85, -0.52]],
      [REL5, 34, 520, [0.19, 0.98]], [REL5 + 180, 18, 300, [0, 1]], [SPLIT + 430 + 260, 5, 160]]);
    aim(S, [cx, cy], [960 + sx, 540 + sy], z, rot);

    // The plunge's effects: one white frame, two impact frames, then two shockwaves from the buried tip (the blade
    // biting the ground, then the knee coming down), with the radial blur and colour split.
    whiteFlash(S.flash,l,LAND,L5);
    impactFrame(S,t,HR[4],()=>S.impLines.position.set(S.TIP5[0],S.TIP5[1]));
    S.shock.center = S.shock2.center = S.zoom.center = projection(cx, cy, z, rot, sx, sy)(S.TIP5);
    // The second wave starts already 275 px out, so its centre never twists the buried tip into a swirl.
    S.shock2.time = 0.25 + (l - WAVE2) / 1000; S.shock2.amplitude = 30 * (1 - k(WAVE2, WAVE2 + 700, l));
    strikeFilters(S,t,l,{hit:L5,release:REL5,amplitude:54,zoom:.17,split:13},l >= WAVE2 && l < WAVE2 + 700 ? [S.shock2] : []);

    // Name: cut out from left to right, with red afterimages.
    revealName(S,l,{start:NAME,end:NAME+260,ghostIn:NAME+50,ghostOut:NAME+180,ghostEnd:NAME+570,exit:EXIT,finish:END,x:S.NAME_AT[0],y:S.NAME_AT[1],dx:80});

    // Entry caption: the name wipes in on the ground in front of the hero; the take-off's wind sweeps it off.
    caption(S,l,{start:420,revealStart:560,revealEnd:860,exitStart:1590,exitEnd:1700,x:1240,y:1040});

    this.drawFx(l, { scarY, L: [L1, L2, L3, L4, L5], SM: [SM1, SM2, SM3, SM4, SM5], LAND, WIND, WAVE2, ZAN, INTO, HOME, FLARE, SPLIT, END, hx, hy, drift, fade, stanceX, keyAt });
    S.fx.update(); S.back.update();
  }

  // Slashes, scars, particles, in world coordinates. Two additive canvases: one behind the hero (scars, the part of each
  // slash that passes behind the body), one in front.
  drawFx(l, T) {
    const S = this.S, palette = this.palette, g = S.fx.g, gb = S.back.g;
    const {glint, slash, swing, tipSparks} = drawing(g, gb, palette);
    beginFx([g, gb], [1, 0, 0, 1, S.OX, S.OY]);

    // The ground bursts where he kicks off and where the blade lands: a flat flash, grit both ways, stones thrown up, and on
    // the take-off a column of light up to where he has gone (60 档 does this bigger).
    const burst = (x0, y0, t0, top, seed) => {
      const q = (l - t0) / 620; if (q <= 0 || q >= 1) return;
      spray(g, l, palette, x0, y0, t0, 130, 1, seed); spray(g, l, palette, x0, y0, t0, 130, -1, seed + 20);
      g.save(); g.translate(x0, y0); g.scale(1, 0.14); glow(g, 0, 0, 260 + 1300 * out(q), palette.groundGlow, 0.8 * (1 - q)); g.restore();
      // The column is gone before the next cut lands.
      if (top && q * 2.2 < 1) pillar(gb, palette, x0, y0, top, 96, q * 2.2);
      stones(g, l, palette.shard, x0, y0, t0, q, 48, seed + 40);
    };
    burst(S.AT.stance[0], 1094, T.SM[0], [S.AT.cut1[0], -500], 300);
    burst(S.TIP5[0], S.TIP5[1], T.L[4], null, 360);
    // Speed lines rush up past him as he falls.
    if (T.keyAt === 'smear5') {
      for (let i = 0; i < 44; i++) {
        const x = T.hx + (rnd(i, 95) - 0.5) * 1700, y0 = -560 + rnd(i, 96) * 900, n = 180 + rnd(i, 97) * 420;
        g.strokeStyle = i % 4 ? 'rgba(' + palette.speedLine + ',0.45)' : 'rgba(255,255,255,0.7)'; g.lineWidth = 1.5 + rnd(i, 98) * 2.5;
        g.beginPath(); g.moveTo(x, y0); g.lineTo(x, y0 + n); g.stroke();
      }
    }

    // The stance: the tip drags along the ground through the cut-in and throws sparks back; through the charge the
    // ground under the tip glows, sparks are pulled into the blade, and a glint runs from the hilt down to the tip.
    if (l >= 250 && l < 1100) {
      for (let i = 0; i < 80; i++) {
        const tb = 250 + rnd(i, 70) * 330, life = 240 + rnd(i, 71) * 420, q = (l - tb) / life;
        if (q <= 0 || q >= 1) continue;
        const [x0, y0] = S.W('stance', [428, 873], [T.stanceX(tb), S.AT.stance[1]]), s = (l - tb) / 1000;
        const vx = -(120 + rnd(i, 72) * 520), vy = -(180 + rnd(i, 73) * 520), px = x0 + vx * s, py = Math.min(y0, y0 + vy * s + 1300 * s * s);
        g.strokeStyle = 'rgba(' + (i % 4 ? palette.drag : palette.dragCore) + ',' + (1 - q) + ')'; g.lineWidth = 1.5 + rnd(i, 74) * 2;
        g.beginPath(); g.moveTo(px, py); g.lineTo(px - vx * 0.025, py - vy * 0.025); g.stroke();
      }
    }
    if (T.keyAt === 'stance' && l > 250) {
      const hip = [T.stanceX(l), S.AT.stance[1]], hilt = S.W('stance', [893, 672], hip), tip = S.W('stance', [428, 873], hip);
      glow(g, tip[0], tip[1], 70 + 50 * k(520, T.SM[0], l), palette.rim, 0.22 + 0.4 * k(520, T.SM[0], l));
      if (l > 650) gather(g, l, {hilt, tip, start: 650, span: T.SM[0] - 950, count: 90, color: palette.charge});
      const gp = k(1490, 1630, l);
      if (gp > 0 && gp < 1) glint(...lerp(hilt, tip, gp), Math.min(0.99, 0.15 + gp * 0.7), 80);
    }
    // The wind-up before the plunge, the longest hold: motes are drawn into the raised blade and it glints once.
    if (T.keyAt === 'wind') {
      const hip = [T.hx, T.hy], hilt = S.W('wind', [763, 170], hip), tip = S.W('wind', [189, 68], hip);
      gather(g, l, {hilt, tip, start: T.WIND + 40, span: T.SM[4] - T.WIND - 340, count: 70, seed: 81, color: palette.charge});
      glint(...S.W('wind', [480, 120], hip), k(T.SM[4] - 130, T.SM[4] - 40, l), 90);
    }

    // The slashes: "da — da-da-da — (hold) — boom". Each head reaches its blade tip on the frame the finishing pose
    // appears; the air cuts fray quickly so they never pile up.
    const B = S.blades, widths = [56, 52, 56, 58, 72], frays = [420, 300, 300, 320, 520];
    for (let i = 0; i < 5; i++)
      swing(B[i], l, {from: T.SM[i], hit: i === 4 ? T.LAND : T.L[i], end: T.L[i], hold: frays[i], tail: S.TAILS[i], width: widths[i], seed: 21 + 10 * i});
    if (l >= T.INTO - 130 && l < T.INTO + 110) slash(B[5], {head: k(T.INTO - 130, T.INTO, l), tail: k(T.INTO - 90, T.INTO + 80, l), head2: k(T.INTO - 130, T.INTO, l), width: 22, fray: 0, seed: 81, alpha: 0.75});
    [[80, 0.8], [60, 0.6], [70, 0.65], [90, 0.75], [170, 1]].forEach(([n, pw], i) => tipSparks(B[i], l, T.L[i], n, pw, 10 + 10 * i));

    // The landing's second wave: when the knee comes down, a low light spreads flat along the ground from the buried
    // tip and the dust is pushed out both ways.
    const tip5 = S.TIP5, w2 = l - T.WAVE2;
    if (w2 > 0 && w2 < 900) {
      const p = w2 / 900, e = out(p);
      g.save(); g.translate(tip5[0], tip5[1] - 10); g.scale(1, 0.1); glow(g, 0, 0, 120 + 1000 * e, palette.waveGlow, 0.45 * (1 - p)); g.restore();
      for (let i = 0; i < 70; i++) {
        const side = i % 2 ? 1 : -1, v = 300 + rnd(i, 91) * 900, life = 0.5 + rnd(i, 92) * 0.5; if (p > life) continue;
        const q = p / life, x = tip5[0] + side * v * out(q) * 0.9, y = tip5[1] - 8 - rnd(i, 93) * 70 * out(q) - 30 * q;
        g.fillStyle = 'rgba(' + palette.waveDust[0] + ',' + (palette.waveDust[1] + (i % 3) * 30) + ',' + palette.waveDust[2] + ',' + 0.5 * (1 - q) + ')';
        g.beginPath(); g.arc(x, y, 1.5 + rnd(i, 94) * 3, 0, Math.PI * 2); g.fill();
      }
    }

    // Once the plunge lands the four air cuts dim; before the break all five flare once each, then the sky breaks along them.
    const open = out(k(T.SPLIT, T.SPLIT + 260, l));
    flareScars(gb, l, palette, {scars: S.scars, hits: T.L, split: T.SPLIT, flare: T.FLARE, step: 60, dim: [0.7, T.L[4], T.L[4] + 200], drift: T.drift, y: T.scarY, open});
    if (l >= T.SPLIT) pieceLips(gb, S.pieces, S.scars, palette, T.fade * open);

    // The bare steel catches the light as it slides home; a hard glint on the guard marks the click.
    steelGlint(g, l, T.INTO, T.HOME, S.W('into', [850, 394], [T.hx, T.hy]), S.W('into', [1030, 394], [T.hx, T.hy]));
    glint(...S.W('home', [908, 392], [T.hx, T.hy]), k(T.HOME, T.HOME + 200, l), 120);
    seamEmbers(g, l, {start: T.SPLIT, span: 1300, count: 200, seams: S.seams, color: palette.seamEmber, drift: T.drift, y: T.scarY});
    risingEmbers(g, l, {from: T.L[4] + 240, to: T.END, color: palette.ember});
  }

}
