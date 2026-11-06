# Aether

[![CI](https://github.com/cyaoc/aether/actions/workflows/ci.yml/badge.svg)](https://github.com/cyaoc/aether/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/cyaoc/aether)](https://github.com/cyaoc/aether/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

B站直播 bot：用你的 B站账号连接直播间，看弹幕、统计盲盒盈亏、欢迎进场观众。
图形界面（GUI）支持 macOS / Windows，命令行（CLI）支持 macOS / Windows / Linux。

## 功能

- **扫码登录**：用 B站 App 扫码，登录凭据保存在本地并自动刷新。
- **弹幕**：显示真实昵称、大航海身份、粉丝团和荣耀等级；断线自动重连。
- **盲盒统计**：记录观众的盲盒投入和开出价值；观众发“今日盲盒”“本月盲盒”时 @回复盈亏，送完一轮后主动播报。
- **进场欢迎**：指定观众，或粉丝团等级、大航海身份、荣耀等级达到门槛的观众进场时，发一条欢迎弹幕。
- **欢迎动画**：给观众 UID 配上视频，进场时在视频页播放，供直播姬 / OBS 的浏览器素材叠加到直播画面。
- **礼物榜**：观众送的大礼物（盲盒按开出的礼物算）和开通、续费大航海，按价值排成榜，在礼物榜页显示，同样叠加到直播画面；新的第一名出现时看板娘欢呼，响一声。像抽奖一样在 GUI 里开始、结束一期，只有统计中的一期记礼物、上画面；点断开或关掉 Aether 时这一期也跟着结束。
- **发送队列**：所有弹幕排队发送，控制间隔，相同内容至少隔 5 秒，被限流时重试一次。
- **便携**：数据都在程序旁边的 `data/` 里，整个目录拷走即可。

## 下载

从 [Releases](https://github.com/cyaoc/aether/releases/latest) 下载对应系统的包：`aether-gui-*` 是图形界面，`aether-cli-*` 是命令行。

| 系统 | 包名中的平台 | GUI | .NET 10 运行时（仅需运行时版） |
| --- | --- | --- | --- |
| Windows x64 | `win-x64` | ✓ | [dotnet-runtime-win-x64.exe](https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe) |
| macOS Apple 芯片 | `osx-arm64` | ✓ | [dotnet-runtime-osx-arm64.pkg](https://aka.ms/dotnet/10.0/dotnet-runtime-osx-arm64.pkg) |
| macOS Intel | `osx-x64` | ✓ | [dotnet-runtime-osx-x64.pkg](https://aka.ms/dotnet/10.0/dotnet-runtime-osx-x64.pkg) |
| Linux x64 | `linux-x64` | — | 用包管理器装 `dotnet-runtime-10.0`，见[安装说明](https://learn.microsoft.com/dotnet/core/install/linux) |

**一般选独立版**（文件名不带 `-framework-dependent`），自带 .NET，解压即用。
带 `-framework-dependent` 的是需运行时版，体积小，要先装 **.NET 10 运行时 10.0.13 或更新版本**：上表链接下载的是最新的 10.0 运行时，
只要 Runtime，不需要 SDK；其他安装方式见 [.NET 10 下载页](https://dotnet.microsoft.com/download/dotnet/10.0)。
旧运行时在 macOS 上有连接处理缺陷：本机程序连上网页服务后立即重置连接，可能让整个 Aether 进程退出；
该问题已在 .NET 10.0.13 [修复](https://github.com/dotnet/runtime/pull/133567)。独立版自带修复后的运行时。

解压到自己的可写目录，例如用户目录下；不要放进 `Program Files`，程序要在旁边写 `data/`。

### 首次启动

- **Windows**：双击 `Aether.Gui.exe`；CLI 在终端运行 `aether.exe watch <房间号>`。
  SmartScreen 拦截时点“更多信息 → 仍要运行”。
- **macOS**：发布包没有签名和公证，先在终端解除隔离：`xattr -dr com.apple.quarantine <解压目录>`。
  GUI 是目录里的 `Aether.Gui`（Finder 双击会同时打开一个终端窗口）；CLI 运行 `./aether watch <房间号>`。
- **Linux**：在解压目录运行 `./aether watch <房间号>`。

第一次连接直播间时，用 B站 App 扫码登录。

### 更新

#### 自动更新

GUI 每次打开时检查 GitHub 上有没有新版本，有就在启动页上下载、安装，然后自动重启进新版本，`data/` 里的登录、设置和记录都在。
下载时可以点“跳过，先用当前版本”。想一直用当前版本，就在通用设置里关掉“打开时自动更新”并保存。
自动更新以后不能回到旧版本，见 [ADR 0015](docs/adr/0015-gui-updates-itself-in-place.md)。

#### 手动更新

CLI 只能手动更新；关掉自动更新的 GUI 也这样更新。

1. 关闭旧版本，把新版本解压到新目录。
2. 把旧目录的整个 `data/` **复制**到新目录，再启动新版本。

0.2.x 及以后版本的 `data/` 复制过来就能用，设置文件会自动升级并保留原有设置，见 [ADR 0011](docs/adr/0011-data-upgrade-from-0-2-3.md)。
升级失败时文件保持原样；按错误提示修正文件或写入权限后，下次读取会自动重试。
旧目录保持不动，回滚时直接运行旧程序。

## 使用

**GUI**：扫码登录后输入房间号，点“连接”。左侧栏切换弹幕、盲盒统计、进场欢迎（含欢迎动画）、送礼动画、礼物榜、抽奖、发送队列和通用设置。

**CLI**：

```sh
aether watch <房间号>   # 观看直播间；没登录时在终端显示二维码
aether login            # 只扫码登录
aether logout           # 退出登录，删除本地凭据
```

弹幕写到 stdout，例如 `[20:15:03] 昵称 [舰长][粉丝团 21]: 内容`，可以重定向成记录文件；状态和日志写到 stderr。按 Ctrl+C 退出。

## 配置

第一次运行时生成 `data/aether.yml`，每一项都有中文注释；也可以在 GUI 的设置页里改。

| 分组 | 内容 | 默认 | 生效时机 |
| --- | --- | --- | --- |
| `blind_box` | 盲盒统计开关、查询关键字、播报开关 | 开启 | 下次连接直播间 |
| `welcome_danmaku` | 欢迎弹幕开关、欢迎语、欢迎条件 | 关闭 | 下次连接直播间 |
| `entry_animation` | 进场动画和欢迎动画的开关、粉丝团等级门槛、音量 | 关闭 | GUI 保存后立即应用；CLI 下次 watch |
| `entry_animation_viewers` | 动画名单：观众 UID → 进场档、0（不播）或视频文件名 | 空 | GUI 保存后立即应用；CLI 下次 watch |
| `gift_animation` | 送礼动画开关、四档投入门槛 | 关闭 | GUI 保存后立即应用；CLI 下次 watch |
| `web_service` | 视频页、抽奖页、礼物榜页共用端口 | 17890 | GUI 保存后立即重启服务；CLI 下次 watch |
| `send` | 两次发送之间的秒数 | 1 | 下次连接直播间 |
| `log` | 日志保留天数、日志级别 | 30 天、Information | 重启 |

`data/` 里还有：

- `aether.db`：登录凭据（**明文**）、收礼记录（盲盒统计和礼物榜共用）、礼物榜的期和抽奖记录。不要分享 `data/`；macOS / Linux 上权限为 0600。
- `logs/`：按天滚动的日志。`log.level` 设为 `Trace` 会记下每条原始消息，含观众个人信息，只在抓包时临时开启。

CLI 和 GUI 放在同一目录时共用 `data/`，分开放则各自登录。同一个 `data/` 里，一个直播间同时只能有一个连接。
`data/` 请放在本地磁盘，部分网络文件系统不支持直播间锁。

各项设置和程序行为的细节见 [行为参考](docs/reference.md)。

### 欢迎动画

**GUI**：在“进场欢迎”的“动画”分页打开开关，点“添加观众”，选欢迎动画和 MP4 或 WebM 视频、填 UID，保存时视频复制到 `data/videos/`。
不用连接直播间：GUI 开着就运行网页服务（登录页上也在运行），与功能开关无关。登录后，“通用设置”显示端口，以及视频页、抽奖页和礼物榜页的地址、浏览器素材的宽高和各自的连接数；视频保存后就能试播，不看开关。

**CLI**：把 MP4 或 WebM 视频放到 `data/videos/`，在 `data/aether.yml` 中打开开关并填写映射：

```yaml
entry_animation:
  enabled: true
web_service:
  port: 17890
entry_animation_viewers:
  12345: 小明进场.mp4
```

CLI 只在 `aether watch <房间号>` 期间、并且进场动画或送礼动画开启时运行网页服务，视频页地址打印到 stdout。礼物榜和抽奖只在 GUI 里用。

先打开 GUI 或运行 `watch`，再在同一台电脑上的直播姬添加“浏览器”素材或 OBS 添加“浏览器”来源，
视频页地址填 `http://localhost:17890/video`，尺寸 1920 × 1080。若画面一直空白，右键素材 → 刷新。
抽奖页地址为 `http://localhost:17890/lottery`，横屏 1920 × 1080、竖屏 1080 × 1920 都行，在没有抽奖时透明。
礼物榜页地址为 `http://localhost:17890/gift-board`，建议 480 × 1000，可以等比放大；它只有榜那么大，放在画面的哪里由你在推流软件里摆。根地址 `/` 返回 404。
端口在“通用设置”或 `web_service.port` 中修改；GUI 保存后立即生效，所有浏览器素材的地址都要跟着改。
关闭欢迎动画不会停止服务或断开页面。0.x 不迁移旧地址、`welcome_animation.port` 或视频目录；旧键只警告，新端口默认 17890。
视频等比居中显示；播放结束恢复透明。普通 MP4 会遮住素材框里的直播画面，位置和大小在推流软件里调整。
欢迎动画与欢迎弹幕各自判断；连接、播放和跳过原因写入日志。

### 奖池抽奖（GUI）

连接直播间后进入“抽奖”，填写 1–10 件奖品的名称、占比和档次，点“开始”。填了观众 UID 时，只有这位观众发出完全匹配的口令（默认“停”，忽略首尾空白）才开奖；也可以由主播点“停”。UID 留空时只由主播点停。

在直播姬 / OBS 添加抽奖页浏览器素材，地址从“通用设置”复制。转动中显示奖品，开奖后显示结果 7 秒，再恢复透明；期间不能开始下一次。取消不留下开奖记录，奖池不扣减，不发送弹幕。

“新开一场”先选报名抽奖或奖池抽奖；当前奖池抽奖自动结束。选奖池抽奖会带出这个直播间上一场奖池的奖品、口令和 UID，不带开奖记录。重连同一个直播间或重启后再连接，会恢复没结束的那场和开奖记录。抽奖数据存入数据库，不写入 `aether.yml`。

两种抽奖共用“星辰召唤”“机甲锁定”“萌系扭蛋”三套主题，点击主题卡片立即切换，每次启动默认星辰召唤。抽奖页按浏览器素材的尺寸自动选择横竖屏排版；“试一下”无需连接直播间，用示例数据播放两种抽奖，不写开奖记录。

### 报名抽奖（GUI）

连接直播间，在“抽奖”点“新开一场”选择报名抽奖，填报名关键字和 1–5 个奖项（名称、奖品、正整数名额、档次）。开始报名后不能修改；观众发的弹幕去掉首尾空白后和关键字完全一样就报名，同一 UID 一场只算一次，不能靠送礼物报名。

点“截止报名”后，在奖项右边逐个开奖。每次从还没中过奖的报名观众中均匀随机抽一位，开奖 7 秒内不能继续抽。确认“结束这场抽奖”后，抽奖页保留完整中奖名单；超过 6 人时每 5 秒翻页。可“收起画面”或“重新显示”，不发送弹幕。

报名中、已截止时须先结束才能新开。下一场报名抽奖带出这个直播间上一场的关键字和奖项，报名名单和开奖记录清空；重连或重启后再连接原直播间，会恢复阶段、报名、开奖结果及画面是否收起。

抽奖页头的“本场 / 历史”可切换查看当前直播间的所有场次，历史后的数字包含进行中的场次。列表按时间倒序，选中后显示报名抽奖各奖项的中奖人、已抽名额和报名关键字，或奖池抽奖每件奖品的中奖人、次数和口令。历史只能查看；进行中的场次可“回到本场”。GUI 重启后未连接时，仍可查看上次直播间的历史；连接后才能返回该场的操作界面。

## 开发

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) **10.0.402 或更新版本**，最低版本由 `global.json` 指定，CI 和发布构建也从该文件安装 SDK。

```sh
dotnet build
dotnet test
dotnet run --project src/Aether.Cli -- watch <房间号>
dotnet run --project src/Aether.Gui
```

浏览器验收测试（抽奖页、礼物榜页、CLI 欢迎动画）只在本机 macOS 跑，CI 不跑，没设开关时 `dotnet test` 会全部跳过：把 `AETHER_TEST_CHROME` 设成 Chrome 的路径（如 `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome`）再运行 `dotnet test`，全量约 18 分钟。

Debug 版使用仓库根目录的 `data/`。推送 `v*` tag 后，[Release 工作流](.github/workflows/release.yml) 构建发布包并上传到 GitHub Release。

- [术语表](GLOSSARY.md) · [架构决策](docs/adr/) · [行为参考](docs/reference.md) · [协议测试数据](tests/Aether.Core.Tests/Fixtures/README.md)
- 任务和人工验证清单在 [Issues](https://github.com/cyaoc/aether/issues)：Windows [#15](https://github.com/cyaoc/aether/issues/15)、Linux [#34](https://github.com/cyaoc/aether/issues/34)、macOS [#121](https://github.com/cyaoc/aether/issues/121)

## 致谢

B站 协议参考 [bilibili-API-collect](https://github.com/pskdje/bilibili-API-collect)。

## 许可证

[MIT](LICENSE)
