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

### 升级

1. 关闭旧版本，把新版本解压到新目录。
2. 把旧目录的整个 `data/` **复制**到新目录，再启动新版本。

0.x 期间不保证新版本能用旧的 `data/`，见 [ADR 0007](docs/adr/0007-no-upgrade-compat-in-0x.md)。
启动报错或设置不对时，删掉新目录里的 `data/`，重新扫码、重新配置。
旧目录保持不动，回滚时直接运行旧程序。

## 使用

**GUI**：扫码登录后输入房间号，点“连接”。左侧栏切换弹幕、盲盒统计、进场欢迎、欢迎动画、抽奖、发送队列和通用设置。

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
| `welcome` | 进场欢迎开关、欢迎语、欢迎条件 | 关闭 | 下次连接直播间 |
| `welcome_animation` | 欢迎动画开关 | 关闭 | 下次连接直播间；GUI 保存为关后立即停止推送 |
| `welcome_animation_videos` | 观众 UID → 视频文件名 | 空 | GUI 保存后立即应用；CLI 下次 watch |
| `web_service` | 视频页、抽奖页共用端口 | 17890 | GUI 保存后立即重启服务；CLI 下次 watch |
| `send` | 两次发送之间的秒数 | 1 | 下次连接直播间 |
| `log` | 日志保留天数、日志级别 | 30 天、Information | 重启 |

`data/` 里还有：

- `aether.db`：登录凭据（**明文**）、盲盒记录和抽奖记录。不要分享 `data/`；macOS / Linux 上权限为 0600。
- `logs/`：按天滚动的日志。`log.level` 设为 `Trace` 会记下每条原始消息，含观众个人信息，只在抓包时临时开启。

CLI 和 GUI 放在同一目录时共用 `data/`，分开放则各自登录。同一个 `data/` 里，一个直播间同时只能有一个连接。
`data/` 请放在本地磁盘，部分网络文件系统不支持直播间锁。

各项设置和程序行为的细节见 [行为参考](docs/reference.md)。

### 欢迎动画

**GUI**：在“欢迎动画”页打开开关，点“添加观众”选 MP4 或 WebM 视频、填 UID，保存时视频复制到 `data/videos/`。
不用登录或连接直播间：GUI 开着就运行网页服务，与功能开关无关。“通用设置”显示端口、视频页和抽奖页地址及各自的连接数；欢迎动画保存为开后可以试播。

**CLI**：把 MP4 或 WebM 视频放到 `data/videos/`，在 `data/aether.yml` 中打开开关并填写映射：

```yaml
welcome_animation:
  enabled: true
web_service:
  port: 17890
welcome_animation_videos:
  12345: 小明进场.mp4
```

CLI 只在 `aether watch <房间号>` 期间、并且欢迎动画开启时运行网页服务，视频页地址打印到 stdout。

先打开 GUI 或运行 `watch`，再在同一台电脑上的直播姬添加“浏览器”素材或 OBS 添加“浏览器”来源，
视频页地址填 `http://localhost:17890/video`；横屏尺寸 1920 × 1080，竖屏 1080 × 1920。若画面一直空白，右键素材 → 刷新。
抽奖页地址为 `http://localhost:17890/lottery`，在没有抽奖时透明。根地址 `/` 返回 404。
端口在“通用设置”或 `web_service.port` 中修改；GUI 保存后立即生效，所有浏览器素材的地址都要跟着改。
关闭欢迎动画不会停止服务或断开页面。0.x 不迁移旧地址、`welcome_animation.port` 或视频目录；旧键只警告，新端口默认 17890。
视频等比居中显示；播放结束恢复透明。普通 MP4 会遮住素材框里的直播画面，位置和大小在推流软件里调整。
欢迎动画与欢迎弹幕各自判断；连接、播放和跳过原因写入日志。

### 奖池抽奖（GUI）

连接直播间后进入“抽奖”，填写 1–10 件奖品的名称、占比和档次，点“开始”。填了观众 UID 时，只有这位观众发出完全匹配的口令（默认“停”，忽略首尾空白）才开奖；也可以由主播点“停”。UID 留空时只由主播点停。

在直播姬 / OBS 添加抽奖页浏览器素材，地址从“通用设置”复制。转动中显示奖品，开奖后显示结果 7 秒，再恢复透明；期间不能开始下一次。取消不留下开奖记录，奖池不扣减，不发送弹幕。

“新开一场”结束当前这场，并带出奖品和口令。重连同一个直播间或重启后再连接，会恢复没结束的那场和开奖记录。抽奖数据存入数据库，不写入 `aether.yml`；报名抽奖、历史页和主题尚未提供。

## 开发

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) **10.0.402 或更新版本**，最低版本由 `global.json` 指定，CI 和发布构建也从该文件安装 SDK。

```sh
dotnet build
dotnet test
dotnet run --project src/Aether.Cli -- watch <房间号>
dotnet run --project src/Aether.Gui
```

Debug 版使用仓库根目录的 `data/`。推送 `v*` tag 后，[Release 工作流](.github/workflows/release.yml) 构建发布包并上传到 GitHub Release。

- [术语表](GLOSSARY.md) · [架构决策](docs/adr/) · [行为参考](docs/reference.md) · [协议测试数据](tests/Aether.Core.Tests/Fixtures/README.md)
- 任务和人工验证清单在 [Issues](https://github.com/cyaoc/aether/issues)：Windows [#15](https://github.com/cyaoc/aether/issues/15)、Linux [#34](https://github.com/cyaoc/aether/issues/34)、macOS [#121](https://github.com/cyaoc/aether/issues/121)

## 致谢

B站 协议参考 [bilibili-API-collect](https://github.com/pskdje/bilibili-API-collect)。

## 许可证

[MIT](LICENSE)
