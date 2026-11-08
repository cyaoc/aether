# 来源和做法

所有图都在 2026-11-08 由 Codex CLI 0.166 的内置生图功能（OpenAI 图像生成）生成，再用 Pillow 和 `cwebp` 转成 WebP。
这里记录来源，也是以后给她画新图的步骤。人设图、Q 版、启动页的定稿**直接用，不要重新生成**。

用户给了两张参考：一张人物原型照片（直播截图，有真人，不放进仓库，只在前两轮当长相参考）和 Q 版九宫格（`textures/chibi-sheet.webp`）。

## 怎么走到定稿的

| 轮次 | 做了什么 | 结论 |
|---|---|---|
| 1 | 照片加 Q 版出人设 A（四视图）、B（表情） | 偏写实、像韩漫，眼睛小；创可贴贴到右膝 |
| 2 | 加原启动页当画风参考 | 够二次元了，但脸偏成熟 |
| 3 | 原型本人想更像 Q 版：6.5 头身、圆脸大眼、纯黑厚发、Q 版的腮红斜线、深棕线条；B 改成和 Q 版一一对应的九宫格 | 选 A5、B6；徽章照 Q 版改成右胸盾形 |
| 4 | 放进启动页的夜空实测：平涂和 SSR 背景不搭，黑色部分和夜空亮度一样 | 定 SSR 为主；出蓝调时刻和原夜空各两张对比（`preview/background-options.webp`） |
| 5 | 用户喜欢 S2b 的人和星空 | 人以 S2b 为准重出 SSR 人设（A8、B8）；Q 版按原图补全身（C3）；背景是星空加她身后的余晖（F1） |
| 6 | 把 F1 拆成两层 | 抠图模型抠不好细发丝，改用绿幕，见下文 |
| 8 | 用户说 Q 版身体比例再小点 | 原来那张约 2.8 头身；提示词写明约 2 头身、下巴到腰不到半个头、腰到脚底约半个头，出四张选最矮的 C6（约 2.1）。侧面远侧的手被身体挡住，用 codex 补上指尖，只把那一小块贴回原图 |
| 7 | 登录页、礼物榜姿势图 | 登录页出两张选 L1（光带从手机流过卡片后面，头离标题更远）并拆层；姿势图每个姿势两张，选截断线最接近的一对 |

## 生图

脚本都在 `tools/`，第一个参数是输出目录，生成的 PNG 和 codex 的日志都放在那里。codex 没有单独的负向参数，负向词写进提示词正文。

| 脚本 | 用途 | 参考图 |
|---|---|---|
| `gen-sheet.sh <目录> <名字> A\|B` | SSR 人设图 | `refs/face-s2b`、对应的人设图；B 另加 Q 版九宫格 |
| `gen-chibi.sh <目录> <名字>` | Q 版全身 | Q 版九宫格、`refs/chibi-neutral`、人设图 A（只参考下半身） |
| `gen-scene.sh <目录> <名字> <姿势文件> <天空文件>` | SSR 场景图；启动页用的是 `scene-pose.txt` 和 `scene-sky.txt` | `refs/face-s2b`、人设图 A、`refs/afterglow-s1a` |
| `gen-login.sh <目录> <名字>` | 登录页，提示词是 `login-base.txt`（场景段去掉启动页的姿势和版式）、`login-pose.txt`、`login-sky.txt`、`login-negative.txt` | `refs/splash-art`、人设图 A |
| `gen-peek.sh <目录> <名字> idle\|cheer` | 礼物榜姿势图，透明底；提示词照原礼物榜设计包，换成她 | `refs/face-s2b`、人设图 A、对应的旧姿势图（只参考构图） |

`login-windows.py`、`peek-check.py` 分别检查登录页的窗口裁切和姿势图的截断线。

固定段落：`sheet-base.txt`、`sheet-anatomy.txt`、`sheet-negative.txt`（人设图）；`scene-base.txt`、`scene-anatomy.txt`、`scene-negative.txt`（场景，人体结构段照抄原启动页）。

## 拆层

1. `clean-plate.sh <整张> <目录> <名字>`：让 codex 把她去掉、补出后面的景色。出两张挑一张，她身后不能留人形。
2. `greenscreen.sh <整张> <目录> <名字>`：让 codex 把她以外全涂成 `#00FF00`。和整张对位要一致（F1 这张平移 0 时最吻合，人身上平均差 6.9）。
3. `python split.py <整张> <去人的> <绿幕的> <地平线 y> <目录>`：出 `background.png` 和 `character.png`，并打印叠回去的差。地平线 y 启动页是 690，登录页是 790。
4. `cwebp`：背景 `-q 80 -m 6`，角色层 `-q 85 -alpha_q 100 -m 6`。

拆层脚本需要 Pillow 和 numpy。试过 isnet-anime 抠图模型：身体轮廓没问题，但细的飘发会断，叠回去发丝之间出现生硬的色块，所以改用绿幕。

`windows.py <整张> <输出>` 按 `UniformToFill` 模拟三种窗口的裁切，并画上标题和进度条的位置，用来查裁切安全区。

## 生成时发现的问题

都已写进负向词或提示词：

| 问题 | 写进哪里 |
|---|---|
| 创可贴贴到右膝 | `sheet-base.txt` 写明"正面看在右边"；`sheet-negative.txt` |
| 徽章在左胸、圆形 | `sheet-base.txt`、`scene-base.txt` 写明右胸盾形；两个负向词文件 |
| 偏写实、眼睛小、韩漫感 | `sheet-negative.txt` |
| 发呆、打哈欠在写实画风下像放电 | `sheet-negative.txt`（sultry、heavy-lidded with parted lips） |
| 侧面只画出一只手 | `sheet-A.txt` 写明远侧的手在臀后露出指尖；`sheet-negative.txt` |
| 头发偏棕 | `sheet-base.txt` 写明纯黑；`sheet-negative.txt` |
| Q 版身体偏长（约 2.8 头身） | `chibi.txt` 写明约 2 头身和躯干、腿各占多少；负向词加上 more than 2.2 heads tall、long legs |
| 头顶太靠上、挥手贴近右边 | `scene-pose.txt` 写明头顶约 15%、指尖离右边至少 10% |
| 裙子短、露大腿 | `scene-negative.txt` |

检查时用 2 倍裁图看每只手，顺着前臂追到袖口和肩膀；张开的手数够五根手指，拇指朝向身体。
