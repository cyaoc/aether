# 进场动画：调查和样片记录

2026-10-25。问题：给粉丝团 30 级以上、或拿到隐藏款的观众做一段进场动画，怎样才不幼稚、不廉价，有冲击力；隐藏款怎么发才不踩平台规则。设计决定见 ADR 0009、0010，样片见 `docs/design/entry-animation/`。

标记：**实测**＝在本机或直播姬里实际做过；**文献**＝有出处；**推测**＝没有可靠来源也没实测。

## 一、打击感从哪来（文献）

- 一项研究用 NLP 分析中文玩家对动作游戏打击感的评论，对照 19 个特征，结论是**顿帧（hit stop）、声音一致性和镜头控制**三样最关键，少一样打击感就明显变差。样本只有排名前后各 8 款游戏，作者自己也说需要更大规模的后续研究。[Lin 等，What Features Influence Impact Feel?](https://arxiv.org/pdf/2208.06155)；[Designing Game Feel. A Survey](https://arxiv.org/pdf/2011.09201) 把顿帧描述为命中时的停顿，用来传达这一下有多重。
- 常见建议是重击时停 50–100 ms。这个数字来自实践博客，不是研究结论。[Where Does Game Feel Come From](https://eastondev.com/blog/en/posts/dev/20260521-game-feedback-feel/)
- 樱井政博讲过，顿帧的思路能用到别的演出上；命中前后短暂放慢、再马上放开效果更好。[Masahiro Sakurai on Creating Games](https://thetvdb.com/series/masahiro-sakurai-on-creating-games/episodes/9326387)
- 第一版画板这三样都没有：没有顿帧、没有声音、镜头不动。

## 二、动画节奏（文献）

- 日本动画常一张画停两三帧（一拍二、一拍三），打斗的接触和爆发换成每帧都动；冲击力更多来自节奏的落差，不是画的张数。[iD Tech：ones、twos、threes](https://www.idtech.com/blog/what-does-animating-on-ones-twos-and-threes-mean)；[Limited animation](https://en.wikipedia.org/wiki/Limited_animation)
- 拖影帧（smear frame）是手绘的风格化模糊，用很少的画表现很快的动作，放在观众本来就预期会糊的地方。[Prolific Studio：Smear frames](https://prolificstudio.co/blog/smear-frames/)
- 冲击帧是命中那一刻一两帧的变形或闪光（常见黑白反相加集中线），因为太短，观众感受到的是一下震动而不是一张画。

## 三、特效为什么显得廉价（文献 + 实测）

- 风格化的刀光、爆炸一般是一段序列帧，按 24–30 帧、有停顿地播，才有动画感；形状要简洁、边缘干净，结尾要收得利落，避免噪点。[RealtimeVFX：Overwatch FX study](https://realtimevfx.com/t/overwatch-fx-pharah-rocket-study/758)；[VFX Apprentice：Flipbooks](https://www.vfxapprentice.com/blog/what-are-flipbooks-in-games)；[RealtimeVFX：Stylized explosion critique](https://realtimevfx.com/t/stylized-ue4-explosion/4557)
- 第一版的问题（实测，用户评价"人物没动作、太幼稚、特效廉价、冲击力差"）：
  - 人物是一张站着的图，只是滑进滑出，没有出招。
  - 刀光是一张静态弧线图从头擦出来，没有形状变化。
  - 色带、圆环、称号牌各动各的，和人物的动作没有因果。设计技能的动效规则也要求每个动画表达因果，不做纯装饰。
  - 大块高饱和色带、卡通闪电、描边圆环、箭羽标签，加上圆润的得意黑斜体，整体像手游活动模板。
  - 全程同一速度，没有压暗、蓄力、停顿、爆发。

## 四、闪光要克制（文献）

- 星铁希儿的大招因为闪光太多被玩家抱怨晃眼，官方表示会加设置来减弱。[PC Gamer 转载](https://malaysia.news.yahoo.com/honkai-star-rails-best-dps-204042766.html)
- WCAG 2.3.1 要求任意一秒内闪光不超过三次（或低于一般闪光和红色闪光阈值）。[W3C：Three Flashes or Below Threshold](https://www.w3.org/WAI/WCAG21/Understanding/three-flashes-or-below-threshold.html)
- 样片里白闪一次、冲击帧两帧，不用大面积红色闪光。

## 五、渲染技术

- 直播姬 8.10 的浏览器素材是 CEF 105，WebGL 走显卡（D3D11），抠图测试没有丢帧（实测，见 `2026-10-15-welcome-animation.md`）。OBS 的浏览器来源没测。
- 辉光、冲击波扭曲、放射模糊、色散这些 CSS 做不了。PixiJS 8 有现成的滤镜库 pixi-filters 6（AdvancedBloom、Shockwave、ZoomBlur、RGBSplit、Godray、MotionBlur 等），MIT 许可，浏览器版挂在全局 `PIXI.filters` 下（文献：[pixijs/filters](https://github.com/pixijs/filters)；实测：样片用的是 8.12.0 和 6.1.4）。
- 角色动作的几条路：
  - **关键姿势**：用生图逐张出姿势，按一拍二、一拍三切换，配合位移和镜头。样片用的是这个，一致性好（实测）。
  - **Spine / Live2D 骨骼动画**：动作最顺，但要有人拆图层、绑骨骼，这里做不了。[spine-pixi runtime](https://hi.esotericsoftware.com/forum/d/26586-spine-pixi-runtime-released)
  - **预渲染透明视频**：特效质量高，但昵称要实时写进去，身份乘档次还有十几段视频，没采用（见 ADR 0010）。
- 本地预览和截图（实测）：
  - 无头 Chrome 在这台 Mac 上 WebGL 走 Metal（ANGLE Metal Renderer: Apple M4 Pro）。
  - 页面一直在渲染，`chrome --headless --screenshot` 等不到空闲，所以改用 DevTools 协议截图。
  - 画板的时钟用一个 Web Animation 驱动，定格时把它钉住，Pixi 每帧按它的时间算画面。这样截图可以精确到毫秒。

## 六、隐藏款不能做成"刷礼物抽奖"（文献）

- 广电总局、文旅部 2022 年发布的《网络主播行为规范》第十四条，列出了主播不得出现的行为，其中包括用"宣传'刷礼物抽奖'等手段，暗示、诱惑、鼓励用户大额'打赏'"。情节严重的，平台要封禁账号。[北京商报](https://www.bbtnews.com.cn/2022/0623/442007.shtml)；[新浪财经](https://finance.sina.com.cn/tech/2022-06-22/doc-imizirau9950425.shtml)；[中国新闻网](https://www.chinanews.com.cn/gn/2022/06-22/9785961.shtml)。没有核对规范原文。
- 讨论里先后考虑过三种做法：
  1. 送指定礼物，按主播设的爆率抽。
  2. 按盲盒开出的价值给更高的档，亏损时随机给一款安慰。
  3. 按一轮送礼的投入给档，档内按固定顺序给下一款，不做随机。

  前两种都是付费换随机奖励，第二种还奖励"开得大"、安慰"亏了"，诱导更强；最后定了第三种（ADR 0009）。

## 七、进场时能拿到的观众信息（代码）

- 进场消息解码后有 UID、昵称、大航海身份、本直播间粉丝团等级、荣耀等级；头像没解析。观众头像画风五花八门，会和动画画风冲突，所以不用头像。
- 礼物现在只解码盲盒；隐藏款要算所有付费礼物的投入，还要解码大航海开通、续费的消息，价格字段（续费可能打折）要先抓包确认。

## 八、招式怎么设计（文献，2026-10-30）

60 档两版动作都被否后查的。标记都是文献；真实剑术的部分只取动作结构，画面上会夸张。

- **全剑连居合第十一本「总切」是一套步步前进的五连斩**：右脚在前逐刀前进，依次是头侧、肩、腋下到腹、水平、竖劈，最后横向血振、纳刀。[AKBAN wiki：Sō-giri](https://www.akban.org/wiki/Seitei_gata_-_Sō-giri_総切り)；[Seiteigata](https://kampaibudokai.org/Seiteigata3.htm)
- **刀落和脚落同时**，不是踏完再砍；后脚跟上一步；水平斩不停刀，刀尖直接回到头顶接竖劈。[全剑连《指导上的留意点》英译](https://kendoargentina.org/wp-content/uploads/2025/04/ZNKR-Iai-Instructional-Considerations-2023-ENG.pdf)
- **拔刀靠左手和胯**：鞘引（saya-biki）是转胯把刀鞘往后拉，右拳对准对手。同上；[Nakakura 研习记录](https://www.kampaibudokai.org/Nakakura.htm)
- **序破急**：序是平静地积蓄，破在刀尖离鞘那一刻，急是快速收束，最后一刀带着前面积累的全部张力。这是一位练习者的解读。[The Iaido Journal](https://www.ejmas.com/tin/2007tin/tinart_boylan_0703.html)
- **连招衔接**：上一招的结束姿势要接近下一招的起始姿势；每个方向的攻击要有自己的胯、肩和步法，不是同一动作转个方向；双手武器靠转胯发力，收势深。[Combat Animation for Games](https://mocaponline.com/blogs/mocap-news/combat-animation-game-dev-guide)
- **力量感**：重招靠更长的预备和停顿；过冲姿势（overshoot）是打实了的“脆”感的来源。[Gnomon：Animating Stylized Attacks](https://bmc.thegnomonworkshop.com/workshops/animating-stylized-attacks-in-maya)
- **起手姿势要一眼看出要来了**；命中判定只有 2–4 帧，靠瞬间性。[Anatomy of an Enemy Attack in Dark Souls 3](https://www.gamedeveloper.com/game-platforms/anatomy-of-an-enemy-attack-in-dark-souls-3)
- **华丽的大招**：Omnislash 这类大招的结构是把对手打上天、空中连斩十几刀、最后一击收尾。[SmashWiki：Omnislash](https://www.ssbwiki.com/Omnislash)
- **纳刀当终结**：一类居合演出里，对手要等主角纳刀才倒下，刀镡碰鞘的声音就是结束的信号。这条来自玩家讨论。[DMC5 讨论](https://steamcommunity.com/app/601150/discussions/0/3110267314130769845/?l=dutch)
- **冲击帧的密度**：中村丰的一段名场面 31 秒里有 41 个冲击帧，还有大量方块状碎屑。[AnimeClick](https://www.animeclick.it/news/65864-larma-segreta-di-todoroki-e-di-studio-bones-lanimazione-di-yutaka-nakamura)
- 没查到绝区零、崩铁大招的逐帧拆解，也没查到超必杀演出结构的权威资料，上面只能作原则用。

## 九、人物生图的提示词（文献 + 实测，2026-10-30）

- OpenAI 的指南没有专门讲手和人体，但要求按场景、主体、细节、约束的顺序写，明确写出排除项和不许变的东西，并描述人物和物体的交互（例子是 “hands naturally gripping the handlebars”）。[GPT Image Prompting Guide](https://developers.openai.com/cookbook/examples/multimodal/image-gen-models-prompting-guide)
- 负面词的常见集合：bad anatomy、bad hands、extra fingers、missing fingers、fused fingers、extra arms、extra hands、malformed limbs。[Stable Diffusion Art](https://stable-diffusion-art.com/?p=8884)；[Segmind](https://blog.segmind.com/best-negative-prompts-in-stable-diffusion/)
- 只靠负面词不够：“模型不知道手该长什么样时，负面里堆 bad hands 没多大用”，要在正面描述手的状态；出了哪种错才加哪个词。[Anima 负面提示词](https://lilting.ch/en/articles/anima-negative-prompt-shorten-illustrious)。这些资料都是别的模型的经验，codex 没有单独的负面参数，写在提示词正文里。
- 实测：
  - 按新写法从零生成的图（pose-15 测试版），近处右手看到手背、远处左手看到指尖，是一对真正的左右手。
  - 在原图上加胳膊，模型抄出同一只手（pose-39 被用户指出两只右手）。
  - 写“刀和手臂成一条直线”，刀柄必然穿过手腕（pose-44、45）。
  - 只写“右手握刀柄”时，拔刀前的右手会落到刀鞘上，要写明沿刀从前到后的顺序。
  - 写明顺序后重测，右手在刀柄上，左手手指扣着鞘口，手和刀柄都对。拔刀拖影里左拳还是空的，没握住刀鞘，已补写法和负面词，这条还没重测。
