import {cutCells,canvasOf, k, out, inq, quint, smooth, on2, rnd, spline, pathOf, chordOf, dense, onScreen, textures, slash as drawSlash, at as placedPoints, drawing, shakeAt, stroke, sparks, trace, clipTo} from './toolkit.mjs';
import {createName, createCaption, framing, sky, caption, revealName, impactFrame, whiteFlash, pieces as makePieces} from './scenes.mjs';

export class Swallow {
  constructor({app,canvas,images,character,nickname}) {
    const P=PIXI,F=P.filters;
    const {tex,silhouette,radial,dispose}=textures(P);
    this.disposeTextures=dispose;this.palette=character.palette;
    const [moon, impact, moonRed] = images.slice(13);

    // The action chain: five cuts, each mid-cut pose -> finishing pose. Every pose is placed by its hips (hip, in its own
    // image's pixels) at the scale s that makes the blade the same length. The images hold the figure only: the light is drawn
    // here, along the path of the blade tip. AT is where the hips sit in the world (the ground is y = 1180): he starts on the
    // left and the moon hangs ahead of him on the right. Every cut starts where the last one ended, and only the last is a
    // full chop: he rises, cuts back down his own line, turns on through a level sweep, and the same turn carries the blade up
    // behind his head, so it is already raised at the top of the leap.
    const keys = ['stance', 'smear1', 'cut1', 'smear2', 'cut2', 'smear3', 'cut3', 'smear4', 'cut4', 'wind', 'smear5', 'land', 'into', 'home'];
    const image = [0, 1, 2, 3, 4, 5, 5, 6, 7, 8, 9, 10, 11, 12];
    const layout = [0.79, 0.8, 0.78, 0.71, 0.62, 0.77, 0.77, 0.82, 0.89, 0.69, 0.83, 0.71, 0.74, 0.74];
    const PO = {};
    keys.forEach((key, i) => { const c = images[image[i]]; PO[key] = { img: c, tex: tex(c), hip: character.anchors.swallow[key].hip, s: layout[i] }; });
    const AT = { stance: [700, 706], smear1: [860, 560], cut1: [990, 430], smear2: [1200, 200], cut2: [980, 240], smear3: [960, 300], cut3: [1000, 300], smear4: [960, 160], cut4: [960, 130], wind: [960, -40], smear5: [1010, 460], land: [1060, 824], into: [1060, 681], home: [1060, 679] };
    const W = (key, q, hip = AT[key]) => { const p = PO[key]; return [hip[0] + (q[0] - p.hip[0]) * p.s, hip[1] + (q[1] - p.hip[1]) * p.s]; };
    const TIP0 = W('stance', [428, 873]), TIP1 = W('cut1', [1084, 30]), TIP2 = W('cut2', [150, 1037]), TIP3 = W('cut3', [1500, 156]), TIP4 = W('cut4', [274, 31]), TIPW = W('wind', [189, 68]), TIP5 = W('land', [1393, 867]);
    // Cut 1 carries the hero up this far while it holds, so cut 2 starts from the risen tip.
    const CLIMB = 160, TIP1R = [TIP1[0] + 15, TIP1[1] - CLIMB];

    // A slash through measured points; the spans listed in behind pass behind the hero's body.
    const slashPath = (pts, behind = []) => { const s = spline(pts); return pathOf(s.pts, s.seg.map(i => behind.includes(i))); };
    const at = (key,list) => placedPoints(W,key,list);

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
    const C0 = [960, 540], RR = 5000;

    // TAILS: how much of each slash has already drawn in by the frame it lands (the slashes below use the same values).
    const TAILS = [0.4, 0.35, 0.4, 0.35, 0.55];

    const scars = TAILS.map((u0, i) => chordOf(blades[i], u0));

    // The five cuts all run on right across the sky, so the sky breaks along the very lines the viewer saw land. They
    // fan out from near one point above the hero's leap; each cell is a big disc clipped by one side of each line. Every
    // cell the closing wide shot can see is kept, however thin, so no hole opens before the break.
    const VIEW = [-300, -800, 2600, 1300];
    const cells = cutCells(scars,C0,RR,VIEW);

    const stage = app.stage;
    const world = new P.Container(); stage.addChild(world);
    const dim = new P.Sprite(radial([[0, 'rgba(6,6,12,0.55)'], [0.6, 'rgba(4,4,8,0.85)'], [1, 'rgba(0,0,0,1)']]));
    dim.anchor.set(0.5); dim.position.set(960, 540); dim.width = 4400; dim.height = 3400; world.addChild(dim);

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
    const pieces = makePieces(P,cells,world,host=>skyOf(moonTex,host),BURST,{base:240,max:120,factor:.25,rotation:.03,down:.5});
    // Effects canvases cover a margin round the screen, since the camera swings a long way and pulls back to 0.71.
    const OX = 760, OY = 560;
    const backC = document.createElement('canvas'); backC.width = 1920 + 2 * OX; backC.height = 1080 + 2 * OY;
    const backTex = tex(backC), backSprite = new P.Sprite(backTex); backSprite.blendMode = 'add'; backSprite.position.set(-OX, -OY); world.addChild(backSprite);

    const rim = new P.Sprite(radial(this.palette.rimStops));
    rim.anchor.set(0.5); rim.width = 1100; rim.height = 1100; world.addChild(rim);
    const ghosts = [0, 1].map(() => { const s = new P.Sprite(PO.stance.tex); s.alpha = 0; world.addChild(s); return s; });
    const hero = new P.Sprite(PO.stance.tex); hero.tint = 0xe0e0e0; world.addChild(hero);

    const fx = document.createElement('canvas'); fx.width = backC.width; fx.height = backC.height;
    const fxTex = tex(fx), fxSprite = new P.Sprite(fxTex); fxSprite.blendMode = 'add'; fxSprite.position.set(-OX, -OY); world.addChild(fxSprite);

    const ui = new P.Container(); stage.addChild(ui);
    // The name is cut out in the closing wide shot, in the open sky to the left of where the cuts fan out.
    const NAME_AT = [640, 260];
    const nameG = new P.Container(); nameG.position.set(...NAME_AT); nameG.rotation = -0.06; ui.addChild(nameG);
    const {nameGhosts,nameT,nameMask}=createName(P,nameG,nickname,this.palette.nameGhost);
    // Entry caption: says who is coming in from the cut-in, until the take-off's wind sweeps it away. It sits on the
    // ground in front of the hero and is drawn larger, since the opening shot is wide.
    const cap = new P.Container(); cap.position.set(1240, 1040); cap.scale.set(1.3); ui.addChild(cap);
    const {capName,capMask}=createCaption(P,cap,nickname,600);

    const imp = new P.Container(); world.addChild(imp);
    const impBg = new P.Graphics().rect(-1800, -1800, 5520, 4680).fill(0xffffff); imp.addChild(impBg);
    const impLines = new P.Sprite(tex(impact)); impLines.anchor.set(0.5); impLines.width = impLines.height = 5200; imp.addChild(impLines);
    const silB = new P.Sprite(silhouette(PO.land.img, '#000')), silW = new P.Sprite(silhouette(PO.land.img, '#fff'));
    for (const s of [silB, silW]) { s.pivot.set(...PO.land.hip); s.scale.set(PO.land.s); s.position.set(...AT.land); imp.addChild(s); }

    const flash = new P.Graphics().rect(-1800, -1800, 5520, 4680).fill(0xffffff); world.addChild(flash);
    const bars = [new P.Graphics(), new P.Graphics()]; stage.addChild(...bars);
    stage.setChildIndex(ui, stage.children.indexOf(world) + 1);

    const bloom = new F.AdvancedBloomFilter({ threshold: 0.9, bloomScale: 1.15, brightness: 1, blur: 7, quality: 5 });
    const shock = new F.ShockwaveFilter({ center: { x: 0, y: 0 }, speed: 1500, amplitude: 54, wavelength: 240, brightness: 1.15, radius: -1 });
    const shock2 = new F.ShockwaveFilter({ center: { x: 0, y: 0 }, speed: 1100, amplitude: 30, wavelength: 420, brightness: 1.08, radius: -1 });
    const zoom = new F.ZoomBlurFilter({ strength: 0, center: { x: 0, y: 0 }, innerRadius: 60 });
    const rgb = new F.RGBSplitFilter({ red: { x: 0, y: 0 }, green: { x: 0, y: 0 }, blue: { x: 0, y: 0 } });

    this.S = {
      app, canvas, world, ui, dim, red, pieces, scars, blades, TAILS, PO, AT, W, TIP5, SKYD: 400, NAME_AT, OX, OY, backC, backTex, rim, ghosts, hero, fx, fxTex,
      emberSrc: scars.map(s => pathOf(dense(s).filter(p=>onScreen(p,VIEW)))),
      nameG, cap, capName, capMask, nameGhosts, nameT, nameMask, imp, impBg, impLines, silB, silW, flash, bars, bloom, shock, shock2, zoom, rgb
    };

  }

  destroy() {
    this.disposeTextures();
    for(const filter of [this.S.bloom,this.S.shock,this.S.shock2,this.S.zoom,this.S.rgb])filter.destroy();
  }

  frame(t) {
    const S = this.S;
    // Five hit stops, at logical times L1–L5: cut 1 holds 90 ms of real time, the air cuts 60, 50 and 50 as they come
    // faster and faster, and the landing 160 (after its white frame). Real time keeps running (shake, impact frames)
    // while everything else holds. HR are the real times they start; REL5 is when the landing lets go.
    const L1 = 1724, L2 = 1920, L3 = 2040, L4 = 2130, L5 = 2719;
    const HS = [[L1, 90], [L2, 60], [L3, 50], [L4, 50], [L5, 180]], HR = [1724, 2010, 2190, 2330, 2969], REL5 = 3149;
    let l = null, acc = 0;
    for (const [s, d] of HS) { if (t < s + acc) break; if (t < s + acc + d) { l = s; break; } acc += d; }
    if (l === null) l = t - acc;

    const mix = (a, b, p) => a + (b - a) * p;

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
    for (const p of S.pieces) {
      p.dx = p.dir[0] * p.dist * e + drift; p.dy = p.dir[1] * p.dist * e + skyY; p.r = p.rot * e;
      p.c.position.set(p.c.pivot.x + p.dx, p.c.pivot.y + p.dy); p.c.rotation = p.r;
      p.c.alpha = 0.95 * out(k(600, 1250, l)) * fade;
    }
    // The blood-moon side shows through the openings, holds a beat once the moonlit pieces are gone, then gives way to the stream.
    S.red.alpha = (l >= SPLIT ? 1 : 0) * (1 - inq(k(RED0, RED1, l)));
    S.red.scale.set(1 + 0.03 * k(SPLIT, RED1, l)); S.red.position.set(960 + drift, 540 + skyY);

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
    let sx = 0, sy = 0;
    // Shake runs on real time. With a direction it mostly moves along the cut; each air cut shakes harder than the last.
    const shake = (...args) => { const [dx,dy]=shakeAt(t,...args); sx+=dx;sy+=dy; };
    shake(HR[0], 16, 240, [0.36, -0.93]); shake(HR[1], 10, 170, [-0.66, 0.75]); shake(HR[2], 12, 170, [1, 0]); shake(HR[3], 15, 200, [-0.85, -0.52]);
    shake(REL5, 34, 520, [0.19, 0.98]); shake(REL5 + 180, 18, 300, [0, 1]); shake(SPLIT + 430 + 260, 5, 160);
    for (const c of [S.world, S.ui]) { c.pivot.set(cx, cy); c.scale.set(z); c.rotation = rot; c.position.set(960 + sx, 540 + sy); }
    const toScreen = q => { const x = (q[0] - cx) * z, y = (q[1] - cy) * z, c = Math.cos(rot), s = Math.sin(rot); return { x: 960 + sx + x * c - y * s, y: 540 + sy + x * s + y * c }; };

    // The plunge's effects: one white frame, two impact frames, then two shockwaves from the buried tip (the blade
    // biting the ground, then the knee coming down), with the radial blur and colour split.
    whiteFlash(S.flash,l,LAND,L5);
    const impA = t >= HR[4] && t < HR[4] + 40, impB = t >= HR[4] + 40 && t < HR[4] + 80;
    impactFrame(S,impA,impB,()=>S.impLines.position.set(S.TIP5[0],S.TIP5[1]));
    const hit = toScreen(S.TIP5);
    const released = t >= REL5, sw = released && l < L5 + 520, sw2 = l >= WAVE2 && l < WAVE2 + 700;
    S.shock.center = hit; S.shock2.center = hit; S.zoom.center = hit;
    S.shock.time = (l - L5) / 1000; S.shock.amplitude = 54 * (1 - k(L5, L5 + 520, l));
    // The second wave starts already 275 px out, so its centre never twists the buried tip into a swirl.
    S.shock2.time = 0.25 + (l - WAVE2) / 1000; S.shock2.amplitude = 30 * (1 - k(WAVE2, WAVE2 + 700, l));
    S.zoom.strength = 0.17 * (1 - out(k(L5, L5 + 300, l)));
    const rs = 13 * (1 - out(k(L5, L5 + 480, l)));
    S.rgb.red = { x: -rs, y: 0 }; S.rgb.blue = { x: rs, y: 0 };
    const filters = [S.bloom];
    if (sw) filters.push(S.shock);
    if (sw2) filters.push(S.shock2);
    if (released && S.zoom.strength > 0.005) filters.push(S.zoom);
    if (released && rs > 0.5) filters.push(S.rgb);
    S.world.filters = S.imp.visible ? null : filters;

    // Name: cut out from left to right, with red afterimages.
    revealName(S,l,{start:NAME,end:NAME+260,ghostIn:NAME+50,ghostOut:NAME+180,ghostEnd:NAME+570,exit:EXIT,finish:END,x:S.NAME_AT[0],y:S.NAME_AT[1],dx:80});

    // Entry caption: the name wipes in on the ground in front of the hero; the take-off's wind sweeps it off.
    caption(S,l,{start:420,revealStart:560,revealEnd:860,exitStart:1590,exitEnd:1700,x:1240,y:1040});

    this.drawFx(l, k, out, { scarY, L: [L1, L2, L3, L4, L5], SM: [SM1, SM2, SM3, SM4, SM5], LAND, WIND, WAVE2, ZAN, INTO, HOME, FLARE, SPLIT, END, hx, hy, drift, fade, stanceX, keyAt });
    S.fxTex.source.update(); S.backTex.source.update();
  }

  // Slashes, scars, particles, in world coordinates. Two additive canvases: one behind the hero (scars, the part of each
  // slash that passes behind the body), one in front.
  drawFx(l, k, out, T) {
    const S = this.S, g = S.fx.getContext('2d'), gb = S.backC.getContext('2d');
    const draw = drawing(g,gb), {band,arcLine,glint} = draw;
    for (const c of [g, gb]) { c.setTransform(1, 0, 0, 1, 0, 0); c.clearRect(0, 0, c.canvas.width, c.canvas.height); c.setTransform(1, 0, 0, 1, S.OX, S.OY); c.globalCompositeOperation = 'lighter'; c.lineCap = 'round'; c.lineJoin = 'round'; }

    // A slash: a sharp crescent whose head is the blade tip, held bright through its hit stop, then frayed into strands.
    const slash = (...args) => drawSlash(draw,this.palette,...args);
    // Sparks thrown off a hit at the blade tip, mostly across the cut.
    const hitSparks = (path,t0,count,power,seed) => sparks(g,l-t0,count,power,seed,(i,s)=>path.at(.82+rnd(i,s+1)*.18),this.palette.spark);

    const glow = (x, y, r, rgb, a) => {
      if (a <= 0.003) return;
      const gr = g.createRadialGradient(x, y, 0, x, y, r); gr.addColorStop(0, 'rgba(' + rgb + ',' + a + ')'); gr.addColorStop(1, 'rgba(' + rgb + ',0)');
      g.fillStyle = gr; g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2); g.fill();
    };
    const lerp = (a, b, f) => [a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f];
    // Motes drawn in to the blade (hilt to tip) while a wind-up holds.
    const gather = (hilt, tip, t0, t1, count, seed, rgb) => {
      for (let i = 0; i < count; i++) {
        const s0 = t0 + rnd(i, seed) * (t1 - t0 - 300), dur = 300 + rnd(i, seed + 1) * 260, p = (l - s0) / dur;
        if (p < 0 || p > 1) continue;
        const to = lerp(hilt, tip, rnd(i, seed + 6)), ang = rnd(i, seed + 3) * Math.PI * 2, r0 = 260 + rnd(i, seed + 4) * 360, r = r0 * (1 - p * p), r2 = r0 * (1 - Math.max(0, p - 0.12) ** 2);
        g.strokeStyle = i % 3 ? 'rgba(' + rgb + ',' + (0.9 * Math.sin(p * Math.PI)) + ')' : 'rgba(255,255,255,' + Math.sin(p * Math.PI) + ')';
        g.lineWidth = 1.5 + rnd(i, seed + 5) * 2;
        g.beginPath(); g.moveTo(to[0] + Math.cos(ang) * r, to[1] + Math.sin(ang) * r); g.lineTo(to[0] + Math.cos(ang) * r2, to[1] + Math.sin(ang) * r2); g.stroke();
      }
    };

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
    // The ground bursts where he kicks off and where the blade lands: a flat flash, grit both ways, stones thrown up, and on
    // the take-off a column of light up to where he has gone (60 档 does this bigger).
    const burst = (x0, y0, t0, top, seed) => {
      const q = (l - t0) / 620; if (q <= 0 || q >= 1) return;
      spray(x0, y0, t0, 130, 1, seed); spray(x0, y0, t0, 130, -1, seed + 20);
      g.save(); g.translate(x0, y0); g.scale(1, 0.14); glow(0, 0, 260 + 1300 * out(q), '255,70,80', 0.8 * (1 - q)); g.restore();
      // The column is gone before the next cut lands.
      const c = Math.min(1, q * 2.2);
      if (top && c < 1) {
        const w = 96 * (1 - c) * (1 - c) + 4;
        for (const [m, c] of [[3.2, '255,40,60,0.3'], [1.4, '255,120,130,0.7'], [0.5, '255,255,255,1']]) { const v = c.split(','); gb.fillStyle = 'rgba(' + v.slice(0, 3).join(',') + ',' + v[3] * (1 - c) + ')'; gb.beginPath(); gb.moveTo(x0 - w * m, y0); gb.lineTo(x0 + w * m, y0); gb.lineTo(top[0] + 40, top[1]); gb.lineTo(top[0] + 20, top[1]); gb.closePath(); gb.fill(); }
      }
      for (let i = 0; i < 48; i++) {
        const s = (l - t0) / 1000, a0 = -1.57 + (rnd(i, seed + 40) - 0.5) * 1.5, v = 700 + rnd(i, seed + 41) * 1500, x = x0 + Math.cos(a0) * v * s, y = y0 + Math.sin(a0) * v * s + 900 * s * s, r = 6 + rnd(i, seed + 42) * 20, a2 = rnd(i, seed + 43) * 6 + s * 9;
        g.fillStyle = 'rgba(255,' + (70 + (i % 3) * 40) + ',80,' + (1 - q) + ')'; g.beginPath(); for (let j = 0; j < 4; j++) { const b = a2 + j * 1.57; j ? g.lineTo(x + Math.cos(b) * r, y + Math.sin(b) * r) : g.moveTo(x + Math.cos(b) * r, y + Math.sin(b) * r); } g.closePath(); g.fill();
      }
    };
    burst(S.AT.stance[0], 1094, T.SM[0], [S.AT.cut1[0], -500], 300);
    burst(S.TIP5[0], S.TIP5[1], T.L[4], null, 360);
    // Speed lines rush up past him as he falls.
    if (T.keyAt === 'smear5') {
      for (let i = 0; i < 44; i++) {
        const x = T.hx + (rnd(i, 95) - 0.5) * 1700, y0 = -560 + rnd(i, 96) * 900, n = 180 + rnd(i, 97) * 420;
        g.strokeStyle = i % 4 ? 'rgba(255,90,100,0.45)' : 'rgba(255,255,255,0.7)'; g.lineWidth = 1.5 + rnd(i, 98) * 2.5;
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
        g.strokeStyle = i % 4 ? 'rgba(255,120,110,' + (1 - q) + ')' : 'rgba(255,240,220,' + (1 - q) + ')'; g.lineWidth = 1.5 + rnd(i, 74) * 2;
        g.beginPath(); g.moveTo(px, py); g.lineTo(px - vx * 0.025, py - vy * 0.025); g.stroke();
      }
    }
    if (T.keyAt === 'stance' && l > 250) {
      const hip = [T.stanceX(l), S.AT.stance[1]], hilt = S.W('stance', [893, 672], hip), tip = S.W('stance', [428, 873], hip);
      glow(tip[0], tip[1], 70 + 50 * k(520, T.SM[0], l), '255,60,70', 0.22 + 0.4 * k(520, T.SM[0], l));
      if (l > 650) gather(hilt, tip, 650, T.SM[0], 90, 1, '255,120,130');
      const gp = k(1490, 1630, l);
      if (gp > 0 && gp < 1) glint(...lerp(hilt, tip, gp), Math.min(0.99, 0.15 + gp * 0.7), 80);
    }
    // The wind-up before the plunge, the longest hold: motes are drawn into the raised blade and it glints once.
    if (T.keyAt === 'wind') {
      const hip = [T.hx, T.hy], hilt = S.W('wind', [763, 170], hip), tip = S.W('wind', [189, 68], hip);
      gather(hilt, tip, T.WIND + 40, T.SM[4], 70, 81, '255,120,130');
      glint(...S.W('wind', [480, 120], hip), k(T.SM[4] - 130, T.SM[4] - 40, l), 90);
    }

    // The slashes: "da — da-da-da — (hold) — boom". Each head reaches its blade tip on the frame the finishing pose
    // appears; the air cuts fray quickly so they never pile up.
    const B = S.blades, widths = [56, 52, 56, 58, 72], frays = [420, 300, 300, 320, 520];
    for (let i = 0; i < 5; i++) {
      const s0 = T.SM[i], hit = i === 4 ? T.LAND : T.L[i], end = T.L[i];
      if (l >= s0 + 4 && l < end + frays[i]) slash(B[i], out(k(s0 + 4, hit - 4, l)), S.TAILS[i] * out(k(s0 + 20, hit, l)), out(k(s0 + 12, hit + 8, l)), widths[i], k(end, end + frays[i], l), 21 + 10 * i);
    }
    if (l >= T.INTO - 130 && l < T.INTO + 110) slash(B[5], k(T.INTO - 130, T.INTO, l), k(T.INTO - 90, T.INTO + 80, l), k(T.INTO - 130, T.INTO, l), 22, 0, 81, 0.75);
    [[80, 0.8], [60, 0.6], [70, 0.65], [90, 0.75], [170, 1]].forEach(([n, pw], i) => hitSparks(B[i], T.L[i], n, pw, 10 + 10 * i));

    // The landing's second wave: when the knee comes down, a low light spreads flat along the ground from the buried
    // tip and the dust is pushed out both ways.
    const tip5 = S.TIP5, w2 = l - T.WAVE2;
    if (w2 > 0 && w2 < 900) {
      const p = w2 / 900, e = out(p);
      g.save(); g.translate(tip5[0], tip5[1] - 10); g.scale(1, 0.1); glow(0, 0, 120 + 1000 * e, '255,80,90', 0.45 * (1 - p)); g.restore();
      for (let i = 0; i < 70; i++) {
        const side = i % 2 ? 1 : -1, v = 300 + rnd(i, 91) * 900, life = 0.5 + rnd(i, 92) * 0.5; if (p > life) continue;
        const q = p / life, x = tip5[0] + side * v * out(q) * 0.9, y = tip5[1] - 8 - rnd(i, 93) * 70 * out(q) - 30 * q;
        g.fillStyle = 'rgba(255,' + (140 + (i % 3) * 30) + ',130,' + 0.5 * (1 - q) + ')';
        g.beginPath(); g.arc(x, y, 1.5 + rnd(i, 94) * 3, 0, Math.PI * 2); g.fill();
      }
    }

    // The straight cuts hang behind the hero from each hit, full length. Once the plunge lands, the four air cuts dim so its
    // cut holds the frame; before the break all five flare once each, in cut order, then the sky breaks along them.
    const open = out(k(T.SPLIT, T.SPLIT + 260, l)), flick = 0.8 + 0.2 * Math.sin(l * 0.045);

    const line = (w,color) => stroke(gb,w,color);
    S.scars.forEach((pts, i) => {
      const a = k(T.L[i], T.L[i] + 120, l) * (1 - k(T.SPLIT + 450, T.SPLIT + 950, l));
      if (a <= 0) return;
      const fl = Math.sin(Math.PI * k(T.FLARE + 60 * i, T.FLARE + 60 * i + 160, l)), wide = Math.max(open, 0.6 * fl);
      const pa = a * (i < 4 ? Math.max(1 - 0.7 * k(T.L[4], T.L[4] + 200, l), fl, open) : 1);
      gb.save(); gb.translate(T.drift, T.scarY); trace(gb, pts);
      line(12 + 40 * wide, 'rgba(255,40,70,' + (0.2 + 0.25 * fl) * pa * flick + ')');
      line(3 + 10 * wide, 'rgba(255,150,160,' + (0.6 + 0.3 * fl) * pa + ')');
      line(1.4 + 3 * wide, 'rgba(255,255,255,' + pa * (1 - 0.6 * open) + ')');
      gb.restore();
    });
    // Once open, each piece carries a lit lip along its own edges.
    if (l >= T.SPLIT) for (const p of S.pieces) {
      gb.save();
      gb.translate(p.c.pivot.x + p.dx, p.c.pivot.y + p.dy); gb.rotate(p.r); gb.translate(-p.c.pivot.x, -p.c.pivot.y);
      clipTo(gb, p.poly);
      for (const pts of S.scars) { trace(gb, pts); line(10, 'rgba(255,60,80,' + 0.35 * T.fade * open + ')'); line(2, 'rgba(255,235,235,' + 0.9 * T.fade * open + ')'); }
      gb.restore();
    }

    // The bare steel catches the light as it slides home; a hard glint on the guard marks the click.
    if (l >= T.INTO && l < T.HOME) {
      const [a, b] = [S.W('into', [850, 394], [T.hx, T.hy]), S.W('into', [1030, 394], [T.hx, T.hy])];
      g.strokeStyle = 'rgba(255,255,255,' + 0.85 * Math.sin(k(T.INTO, T.HOME, l) * Math.PI) + ')'; g.lineWidth = 3;
      g.beginPath(); g.moveTo(a[0], a[1]); g.lineTo(b[0], b[1]); g.stroke();
    }
    glint(...S.W('home', [908, 392], [T.hx, T.hy]), k(T.HOME, T.HOME + 200, l), 120);
    // Embers peel off the lips of every seam as the sky opens.
    if (l >= T.SPLIT && l < T.SPLIT + 1300) {
      const e = l - T.SPLIT;
      for (let i = 0; i < 200; i++) {
        const t0 = rnd(i, 30) * 520, life = 600 + rnd(i, 31) * 700, q = (e - t0) / life;
        if (q <= 0 || q >= 1) continue;
        const [x0, y0, nx, ny] = S.emberSrc[i % S.emberSrc.length].at(rnd(i, 32)), side = i % 2 ? 1 : -1, v = 30 + rnd(i, 33) * 110;
        const px = x0 + nx * v * 1.4 * side * q + T.drift, py = y0 + ny * v * 1.4 * side * q - 40 * q + T.scarY;
        g.fillStyle = 'rgba(255,' + (120 + (i % 3) * 50) + ',125,' + Math.sin(q * Math.PI) * 0.9 + ')';
        g.beginPath(); g.arc(px, py, 1.2 + rnd(i, 34) * 2.2, 0, Math.PI * 2); g.fill();
      }
    }
    // Embers rise from the landing on through the zanshin.
    if (l > T.L[4] + 240 && l < T.END) {
      const fade = k(T.L[4] + 240, T.L[4] + 740, l) * (1 - k(T.END - 400, T.END, l));
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
