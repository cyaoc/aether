(async () => {
  const x = dc; await new Promise(r => setTimeout(r, 800));
  const SP = { hold: ['sp3', 1], hold2: ['sp3', 2], lift: ['sp3', 3], lift2: ['sp3', 4], lid: ['sp3', 5], pat: ['sp2', 1], surprised: ['sp1', 3] };
  const cellOf = (kind, key) => kind === 'h' ? [x.cell(key), 250 * 0.5] : kind === 'p' ? [x.pkCell(key), 112]
    : kind === 's' ? [SP[key] ? x.cells[SP[key][0]][SP[key][1]] : x.cells[key][0], 76 * 1.35] : [x.tweens[key[0]][key[1]], 250 * 0.5];
  const thumb = (kind, key) => {
    const [c, h] = cellOf(kind, key), s = h / c.ruler, cv = document.createElement('canvas');
    cv.width = Math.ceil(c.w * s); cv.height = Math.ceil(c.h * s);
    cv.getContext('2d').drawImage(c.im, c.x, c.y, c.w, c.h, 0, 0, cv.width, cv.height);
    return cv.toDataURL('image/png');
  };
  const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;');
  // an item: [kind, key, name, expression] or a plain note string; '→' between items in a cell
  const fig = it => typeof it === 'string' ? (it === '→' ? '<span class="arrow">→</span>' : `<p class="note">${esc(it)}</p>`)
    : `<figure><div class="pic"><img src="${thumb(it[0], it[1])}"></div><figcaption><b>${esc(it[2])}</b><span>${esc(it[3])}</span><code>${esc(Array.isArray(it[1]) ? it[1][0] : it[1])}</code></figcaption></figure>`;
  const cell = list => `<td>${list.map(fig).join('')}</td>`;
  const P = '照常演她的心情';
  const sections = [
    ['1　它来了（还没落地）', '三分钟没人送礼才来，半小时最多一次；她手上的演出演完再来。', [
      ['探头', '从页面左边探进来', '它：「嘘——别告诉她」', [P + '，心情台词这时不冒气泡'], [['p', 'peek', '探头', '坏笑，眯着眼']], ['平时']],
      ['吊着宝箱飞过她头顶', '约 9 秒，一顿一顿地飞', '', [P], [['p', 'carry', '拎绳吊箱飞', '咬牙、挤着一只眼，很吃力；越飞越低，快到时冒汗打晃']], ['平时']],
      ['有人发弹幕', '最多 3 次，每次僵住 1.8 秒', '', [['h', 'lookUp', '抬头看', '抬头四处找，手指点着下巴']], [['p', 'carryBag', '套纸袋装死', '纸袋套头，只露两只眼'], '/', ['p', 'carryCrown', '戴纸王冠装死', '眼睛往上翻、噘嘴吹口哨，装无辜']], [['s', 'sLook', '抬头看', '跟着她抬头']]],
      ['撑不住，连人带箱掉下来', '0.48 秒落到她右边', '', [P + '，落地一震才反应'], [['p', 'fall', '往下掉', '瞪眼尖叫，两手举起']], ['平时']],
    ]],
    ['2　箱子是空的', '', [
      ['摔在箱盖上 → 甩头 → 掀开：空的', '摔晕 1.3 秒，飞出一只蛾子', '它：「……空的？白偷了！」', [['h', 'startled', '吓一跳', '瞪圆眼、张嘴，往后蹦一步'], '→', ['h', 'sorry', '委屈', '低头揪着手，不好意思']], [['p', 'dazed', '摔晕', '转圈眼、头上肿包、吐舌头'], '→', ['p', 'shake', '甩头', '闭眼甩甩头'], '→', ['p', 'peer', '掀箱看', '眯眼往箱子里看']], ['平时']],
      ['嫌弃，坐上去赖着', '最多 45 秒，每 20 秒嘲讽一句', '它：「我坐这儿等，有货再拿～」等', [['h', 'sorry', '委屈', '同上']], [['p', 'shrug', '嫌弃', '眯眼撇嘴'], '→', ['p', 'sit', '坐着赖着', '抱着手斜眼看'], '/', ['p', 'laugh', '指着她笑', '嘲讽的时候']], ['平时']],
      ['45 秒没人送礼', '把空箱从她头顶扔回家，飞走', '它：「空的不要，还你！」', [['h', 'sorry', '委屈', '同上']], [['p', 'shrug', '嫌弃', ''], '→', ['p', 'throw', '扔箱子', '眯一只眼使劲扔'], '→', ['p', 'laugh', '笑', '']], ['平时']],
      ['赖着时有人送礼', '任何礼物都把它一下打飞；接「打跑了」4.3 秒', '她：「把它打跑啦」类', [['h', 'surprised', '吃惊', '两手捧脸'], '→', ['h', 'cheer', '欢呼', '眯眼大笑，两手举高']], [['p', 'knocked', '被一下打飞', '转圈眼、肿包，转着圈往右上方飞出页面（约 1.5 秒）']], [['s', 'lid', '压箱盖', '等它离开箱盖再过去，先把弹开的盖子压上，守在箱子上等它飞远'], '→', ['s', 'lift', '拎绳吊箱', '把宝箱直着吊上去，从她头顶飞回家']]],
    ]],
    ['3　箱子里有货：一分钟拔河', '她拽这头的金环往家（左）拽，它拽钩在那头金环上的绳子往右拽；观众送礼砸它，发「捣蛋」的人送礼砸她。', [
      ['箱子落地', '0–0.5 秒', '她：「啊！我的宝箱！」', [['h', 'startled', '吓一跳', '往后蹦一步']], [['p', 'dazed', '摔晕', '趴在箱盖上']], [['s', 'surprised', '吓一跳', '瞪圆眼']]],
      ['扑上去', '0.5–0.85 秒', '', [['h', 'lunge', '扑过去攥环', '皱眉瞪眼、张嘴喊，两手攥住金环']], [['p', 'dazed', '摔晕', '还没醒']], [['s', 'surprised', '吓一跳', '']]],
      ['开始拽', '0.85 秒起；它 2.2 秒起拽', '它：「哎呀，被发现了！」', [['h', 'tug', '攥环拽', '眼睛挤成 > <、咬牙，身子往后仰']], [['p', 'tumble', '翻跟头', '被她拽下箱盖，转圈眼'], '→', ['p', 'shake', '甩头', ''], '→', ['p', 'tug', '拽绳', '> < 咬牙，后仰着拽']], [['s', 'hold', '抓箱盖拽', '> < 使劲，抓着掀开的箱盖跟她一起拽'], ['s', 'hold2', '抓箱盖拽 2', '两张轮流换，像在使劲往后拽']]],
    ]],
    ['4　拔河里发生的事', '', [
      ['观众送礼砸它', '每份按电池扣它的血', '她：「{昵称}打得好！」', [['h', 'tug', '攥环拽', '接着拽']], [['p', 'ouch', '挨砸', '头上肿包、一只眼含泪、疼得咧嘴（0.45 秒）']], [['s', 'hold', '抓箱盖拽', '接着拽']]],
      ['发「捣蛋」的人送礼砸她', '她松手 0.8 秒，箱子被带过去一截，它回血', '她：「{昵称}！怎么砸我呀！」', [['h', 'bonk', '挨砸捂头', '两手捂头、挤着眼、眼角一滴泪']], [['p', 'laugh', '指着她笑', '眯眼笑、吐舌头']], [['s', 'hold', '抓箱盖拽', '不松手，一个人接着拽']]],
      ['帮它的人连着第 5 份', '压扁 1.2 秒 → 趴着攥环被拖 0.7 秒 → 爬起来', '', [['h', 'flat', '压扁', '趴平，转圈眼'], '→', ['h', 'dragged', '趴着攥环被拖', '鼓着腮、含泪，不服气']], [['p', 'laugh', '指着她笑', '']], [['s', 'hold', '抓箱盖拽', '不松手，不然它就跑了']]],
      ['它的血掉过一半：放闪电', '倒计时停 2.4 秒；之后她一直是爆炸头', '它：「看我的闪电！」', [['h', 'tugZap', '爆炸头攥环拽', '脸上带灰，> < 咬牙更使劲'], '其他也换爆炸头版：', ['h', 'bonkZap', '爆炸头挨砸', ''], ['h', 'flatZap', '爆炸头压扁', '']], [['p', 'thunder', '放闪电', '怒目大吼']], [['s', 'hold', '抓箱盖拽', '接着拽']]],
      ['最后 10 秒', '它把绳子扛上肩往外拖，倒计时 30 秒起越来越急', '它：「拜拜咯～」', [['h', 'dragged', '趴着攥环被拖', '死死攥着，被砸只缩一下'], '/', ['h', 'draggedZap', '爆炸头趴着被拖', '> < 咬牙']], [['p', 'haul', '扛绳拖走', '咬牙瞪眼，绳子扛在肩上']], [['s', 'hold', '抓箱盖拽', '还拽着']]],
    ]],
    ['5　结局', '', [
      ['守住了', '它的血打光，或来一个中礼物；4.8 秒，被电过 7.2 秒', '它：「你们给我等着——！」　她：「守住啦！最后一下是{昵称}！连守 N 次！」', [['h', 'cheer', '欢呼', '眯眼大笑'], '被电过：', ['h', 'cheerZap', '爆炸头欢呼', '顶着爆炸头欢呼'], '→', ['t', ['cheerZap>cheer', 0], '头发塌一半', '过渡帧，0.3 秒'], '→', ['t', ['cheerZap>cheer', 1], '差不多顺了', '过渡帧，0.3 秒'], '→', ['h', 'cheer', '欢呼', '']], [['p', 'flat', '漏气飞走', '瘪了、转圈眼，像漏气的气球左右乱晃着往右上方飘出画面（约 2 秒）']], [['s', 'lid', '压箱盖', ''], '→', ['s', 'lift', '拎绳吊箱', '吊回家'], '→', ['s', 'lift2', '拎绳吊箱 2', '飞过她头顶'], '被电过：', ['s', 'pat', '摸头', '飞回来摸两下，星星围着头转、柔光、亮片'], '→', ['s', 'sHappy', '开心', '回到她身边']]],
      ['没守住', '一分钟到；它拖着箱子从右边走，她趴 1.6 秒再跪起来哭 5.6 秒', '它：「多谢款待，下次再来！」　她：「呜…宝箱被拿走了…」', [['h', 'fallen', '松手趴着', '鼓腮含泪'], '→', ['h', 'robbed', '跪着哭', '大哭流泪，一手伸向箱子被拖走的方向；台词 4.6 秒消失'], '→', ['t', ['robbed>gloomy', 0], '抹眼泪', '过渡帧，0.26 秒'], '→', ['t', ['robbed>gloomy', 1], '缩成蹲姿', '过渡帧，0.26 秒'], '→', ['h', 'gloomy', '沮丧', '蹲着画圈圈，接第 7 节'], '被电过：', ['h', 'fallenZap', '爆炸头松手趴着', ''], '→', ['h', 'robbedZap', '爆炸头跪着哭', ''], '→', ['t', ['robbedZap>robbed', 0], '头发塌一半', '过渡帧'], '→', ['t', ['robbedZap>robbed', 1], '差不多顺了', '过渡帧'], '→', ['h', 'robbed', '跪着哭', '']], [['p', 'haul', '扛绳拖走', '']], [['s', 'pat', '摸头', '一直在她头上安慰她；被电过就摸两下把头发摸顺（星星、柔光、亮片）']]],
    ]],
    ['6　被大事件或 PK 吓跑', '舰长、提督、总督、抽奖任何一步，或盲盒播报开出城堡，或主播开始 PK，它都吓跑；拔河中算守住，大航海的电池算进宝箱。PK 后面不演别的。', [
      ['它吓跑，精灵把宝箱吊回家', '约 4 秒，事件的演出等宝箱到家', '', ['箱子要掉在她身上就先吓得蹦开', '→', ['h', 'startled', '吓一跳', '箱子落地那一下'], '她等箱子吊过头顶再蹦回中间，然后照常演那个事件（PK 就站着）'], [['p', 'startle', '受惊松手', '吓一跳、两手一松；吊着的箱子拖着绳钩掉下来'], '→', ['p', 'flee', '逃跑', '慌张回头看，往右边一颠一颠地跑出画面（约 2 秒）']], [['s', 'surprised', '发现', '躲开掉下来的箱子，头上冒“！”愣半秒'], '→', ['s', 'lid', '压箱盖', '压好就守在箱子上，等它跑出画面'], '→', ['s', 'lift', '拎绳吊箱', '再把宝箱吊回家，不和它并排走']]],
      ['拔河里被电过', '精灵送完宝箱回来摸顺头发，约多等 3 秒；事件的演出等头发顺了才开始', '', [['h', 'cheerZap', '爆炸头欢呼', ''], '→', ['t', ['cheerZap>cheer', 0], '头发塌一半', '过渡帧'], '→', ['t', ['cheerZap>cheer', 1], '差不多顺了', '过渡帧'], '→', ['h', 'cheer', '欢呼', '']], [['p', 'flee', '逃跑', '']], [['s', 'lift', '拎绳吊箱', ''], '→', ['s', 'pat', '摸头', '摸两下把头发摸顺（星星、柔光、亮片）']]],
    ]],
    ['7　之后的心情', '沮丧、开心时都不做日常小动作。', [
      ['没守住以后', '直到有人送礼（直接答谢，被哄好），最多 1 分钟', '她：「我的宝箱…」「下次一定守住…」「（偷偷看）下次有人帮我守吗…」', [['h', 'gloomy', '沮丧', '蹲着画圈圈']], ['已经走了'], [['s', 'pat', '摸头', '一直摸她的头']]],
      ['守住了以后', '1 分钟', '', [['h', 'idle', '开心', '原地轻轻蹦']], ['已经走了'], [['s', 'sIdle', '绕着她转', '']]],
    ]],
  ];
  const rows = sections.map(([title, lead, list]) => `<h2>${esc(title)}</h2>${lead ? `<p class="lead">${esc(lead)}</p>` : ''}<table><thead><tr><th>情况</th><th>她</th><th>捣蛋鬼</th><th>精灵</th></tr></thead><tbody>${
    list.map(([what, when, said, h, p, s]) => `<tr><th><b>${esc(what)}</b><span>${esc(when)}</span>${said ? `<q>${esc(said)}</q>` : ''}</th>${cell(h)}${cell(p)}${cell(s)}</tr>`).join('')}</tbody></table>`).join('');
  return `<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>捣蛋鬼来了 · 动作表</title><style>
  body{margin:0;padding:24px 28px 60px;background:#f4f2f7;color:#2d2438;font:14px/1.5 "PingFang SC","Hiragino Sans GB",sans-serif}
  h1{font-size:24px;margin:0 0 4px} .sub{color:#776d86;margin:0 0 18px} h2{font-size:19px;margin:28px 0 4px} .lead{color:#5b5168;margin:0 0 8px}
  table{border-collapse:separate;border-spacing:0 6px;width:100%;table-layout:fixed} thead th:nth-child(1){width:190px} thead th:nth-child(2){width:37%} thead th:nth-child(3){width:31%} thead th{text-align:left;color:#8a8098;font-weight:600;padding:0 10px}
  tbody th{width:190px;vertical-align:top;text-align:left;background:#e7e2ef;border-radius:10px 0 0 10px;padding:10px 12px}
  tbody th b{display:block;font-size:15px} tbody th span{display:block;color:#6d6380;font-weight:400;font-size:12px;margin-top:2px}
  tbody th q{display:block;margin-top:6px;color:#7b4a8c;font-size:12px;font-weight:400;quotes:none}
  td{background:#fff;vertical-align:top;padding:8px 10px} td:last-child{border-radius:0 10px 10px 0}
  td>*{display:inline-block;vertical-align:top} figure{margin:0 6px 4px 0;width:132px}
  .pic{height:128px;display:flex;align-items:flex-end;justify-content:center;background:#ebe8f0;border-radius:8px;overflow:hidden}
  .pic img{max-width:126px;max-height:124px} figcaption b{display:block;font-size:13px;margin-top:4px} figcaption span{display:block;font-size:12px;color:#5b5168}
  figcaption code{display:block;font-size:11px;color:#a49bb2} .arrow{font-size:20px;color:#a49bb2;margin:50px 4px 0 0} .note{margin:4px 8px 0 0;max-width:150px;color:#5b5168}
  </style></head><body><h1>捣蛋鬼来了：谁在什么情况下做什么动作</h1><p class="sub">图都直接从画板上取（同一个角色同一比例）。爆炸头版本只在它放过闪电以后用。灰色小字是画板里的姿势名。</p>${rows}</body></html>`;
})()
