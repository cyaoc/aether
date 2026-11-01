// One character's data: which image each pose uses, the points measured on those images, and its colours. Where a pose
// stands and how big it is belong to the move.
const poses = {
    slice: [
      {image:'pose-2',hip:[840,540],sole:953},
      {image:'pose-13',hip:[530,480],sole:899},
      {image:'pose-14',hip:[480,516],sole:888},
      {image:'pose-3',hip:[804,440],sole:1006},
      {image:'pose-4',hip:[804,440],sole:1009}
    ],
    triple: {
      charge: {image:'pose-6',hip:[960,420],sole:973},
      draw: {image:'pose-31',hip:[730,440],sole:934},
      cut1: {image:'pose-32',hip:[560,420],sole:986},
      wind: {image:'pose-9',hip:[642,787],sole:1483},
      smear2: {image:'pose-10',hip:[640,500],sole:983},
      cut2: {image:'pose-7',hip:[663,563],sole:983},
      spin: {image:'pose-11',hip:[1010,462],sole:996},
      smear3: {image:'pose-12',hip:[512,800],sole:1546},
      cut3: {image:'pose-8',hip:[558,814],sole:1576},
      into: {image:'pose-3',hip:[804,440],sole:1006},
      home: {image:'pose-4',hip:[804,440],sole:1009}
    },
    swallow: {
      stance: {image:'pose-15',hip:[900,480]},
      smear1: {image:'pose-16',hip:[350,590]},
      cut1: {image:'pose-17',hip:[510,790]},
      smear2: {image:'pose-23',hip:[600,640]},
      cut2: {image:'pose-24',hip:[570,640]},
      smear3: {image:'pose-34',hip:[510,380]},
      cut3: {image:'pose-34',hip:[510,380]},
      smear4: {image:'pose-25',hip:[530,770]},
      cut4: {image:'pose-26',hip:[537,800]},
      wind: {image:'pose-29',hip:[810,680]},
      smear5: {image:'pose-30',hip:[430,700]},
      land: {image:'pose-21',hip:[480,600]},
      into: {image:'pose-3',hip:[804,440]},
      home: {image:'pose-4',hip:[804,440]}
    },
    moon: {
      charge: {image:'pose-60',hip:[880,520],sole:993,tsuba:[1145,550]},
      smear1: {image:'pose-61',hip:[430,660],tip:[1078,43],mouth:[343,685]},
      cut1: {image:'pose-62',hip:[450,720],tip:[1086,28]},
      smear2: {image:'pose-64',hip:[640,420],tip:[1574,250],ring:[640,250,934,62]},
      cut2: {image:'pose-64',hip:[640,420],tip:[1574,250]},
      smear3: {image:'pose-65',hip:[720,870],tip:[760,30],ring:[730,600,570]},
      cut3: {image:'pose-66',hip:[420,400],tip:[996,778]},
      smear4: {image:'pose-67',hip:[600,540],tip:[1409,395]},
      cut4: {image:'pose-68',hip:[640,450],tip:[1597,390]},
      smear5: {image:'pose-70',hip:[545,740],tip:[946,28],helix:[500,330,740,110,3]},
      cut5: {image:'pose-70',hip:[545,740],tip:[946,28]},
      noto: {image:'pose-71',hip:[820,560],tsuba:[1122,465],mouth:[790,462]},
      iai: {image:'pose-72',hip:[770,520],tsuba:[1115,450],mouth:[1040,455]},
      cutin: {image:'pose-73',hip:[0,0],glint:[[740,565],[860,565]]},
      smear6: {image:'pose-75',hip:[600,500]},
      cut6: {image:'pose-76',hip:[520,480],tip:[1392,536],boot:[1290,740]},
      turn: {image:'pose-77',hip:[560,470],tip:[1424,416]},
      slam: {image:'pose-78',hip:[620,620],mouth:[740,592]},
      fall: {image:'pose-79',hip:[590,620]},
      slamin: {image:'pose-74',hip:[0,0],tsuba:[875,530]},
      oni1: {image:'oni-7',tsuba:[1135,445],horn:[1150,10],head:[1040,190],bottom:1016},
      oni2: {image:'oni-8',tip:[1783,91]},
      oni3: {image:'oni-9',tip:[1496,543],head:[462,160]},
      oni4: {image:'oni-10',head:[740,185]}
    }
};

const backdrops = ['moon', 'moon-red', 'impact', 'day'];
const textures = [...new Set([...Object.values(poses).flatMap(move => Object.values(move).map(pose => pose.image)), ...backdrops])];

export const general = {
  textures: Object.fromEntries(textures.map(name => [name, `textures/${name}.webp`])),
  audio: 'audio/events.json',
  poses,
  palette: {
    // The figure and the name.
    ghost: 0xff6070,
    nameGhost: 0xff4d5e,
    rim: '255,60,70',
    rimStops: [[0, 'rgba(255,60,70,0.55)'], [0.45, 'rgba(200,20,40,0.18)'], [1, 'rgba(0,0,0,0)']],
    // Slashes and what they throw off.
    slash: '255,58,80',
    slashAura: '255,30,60',
    slashCore: '255,240,240',
    strand: [255, 200, 210],
    fray: [255, 120, 140],
    spark: '255,110,120',
    charge: '255,120,130',
    chargeFar: '255,90,100',
    drag: '255,120,110',
    dragCore: '255,240,220',
    speedLine: '255,90,100',
    // The cuts left in the sky.
    seamGlow: '255,40,70',
    seamEdge: '255,150,160',
    seamLip: '255,60,80',
    seamCore: '255,235,235',
    seamEmber: [255, 120, 125],
    ember: [255, 90, 100],
    // The ground.
    dust: [255, 150, 150],
    grit: '255,50,60',
    gritCore: '255,225,215',
    groundGlow: '255,70,80',
    shard: [255, 70, 80],
    crack: '255,90,90',
    waveGlow: '255,80,90',
    waveDust: [255, 140, 130],
    // Columns of light, as [width, colour, alpha].
    pillar: [[3.2, '255,40,60', 0.3], [1.4, '255,120,130', 0.7], [0.5, '255,255,255', 1]],
    burn: [[30, '255,40,60', 0.28], [7, '255,170,170', 0.75], [2.5, '255,255,255', 1]],
    // Ink, the blood-moon world and the oni.
    ink: '255,40,55',
    inkCore: '255,230,230',
    inkSoft: '255,220,220',
    inkThread: '255,200,200',
    inkEdge: '255,50,60',
    inkTint: '#c01424',
    inkShade: '#8a7878',
    shadeTint: 0xff7070,
    oniTint: 0xd0c0c0,
    moonHalo: '255,40,50',
    moonMist: [255, 50, 70],
    waveEdge: [[60, '255,40,60', 0.16], [14, '255,40,60', 0.4], [3, '255,120,60', 0.9]],
    worldSky: '40,4,8',
    daylight: '255,225,190',
    shatter: '255,50,60',
    shatterCore: '255,240,225',
    oniEmber: [255, 60, 70],
    // Scars turning towards the daylight as the world breaks, as [night, day].
    scarGlow: [[255, 40, 70], [255, 210, 180]],
    scarEdge: [[255, 150, 160], [255, 240, 220]]
  }
};
