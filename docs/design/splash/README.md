# 启动页

2026-11-01 在设计稿画布上定稿（画布是私有的，实现时以本目录为准）。GUI 打开后先显示一张看板娘插画，
确认账号状态后换成登录页或主界面，启动过程中用户看不到页面切换。实现任务：[GitHub Issue #282](https://github.com/cyaoc/aether/issues/282)。

- `textures/splash.webp`：定稿插画，1536×1024，约 220 KB。**直接用，不要重新生成**；来源见 `prompts.md`。
- `boards/`：画板源码，尺寸和位置以这里为准，本文只做导读。
- `preview/`：参考截图（画板原尺寸）。
- `tools/`：在本地打开画板的小工具，复用 `lottery-textures` 的渲染页；还有生图脚本和提示词。

## 画面

| 项目 | 规格 |
|---|---|
| 插画 | 铺满窗口，等比放大后居中裁切（Avalonia 是 `Stretch="UniformToFill"`） |
| 底色 | `#0F1550`，只在插画还没画出来时露出 |
| 标题 | `Aether`，由界面绘制，不在图里。左 44、上 56，30 号，粗体，白色，和登录页标题同一位置 |
| 其他内容 | 平时没有，不放版本号和说明文字。自动更新时在标题下方显示进度，见下文“自动更新” |
| 主题 | 浅色和深色共用这一张 |

| 窗口 | 插画显示 | 画板 |
|---|---|---|
| 默认 1200×720 | 上下各裁 51 px（按原图算） | `SP1-Splash.dc.html` |
| 最大化 1600×900 | 上下各裁 80 px | `SP2-SplashWide.dc.html` |
| 最小 1040×600 | 上下各裁 69 px | `SP3-SplashMinWindow.dc.html` |

插画里人物头顶离上边缘约 105 px，这三种窗口都裁不到。

## 动效

| 时机 | 做法 |
|---|---|
| 进入 | 窗口打开就显示，每次启动都一样，不淡入 |
| 停留 | 见下表 |
| 退出 | 启动页盖在目标页上面，淡出 150 ms |

| 情况 | 启动页停多久 |
|---|---|
| 刚打开 | 同时开始读账号（最多 3 秒）和检查新版本（最多 3 秒，自动更新关着就不检查），启动页上不显示任何东西 |
| 没有新版本 | 两边都有结果或超时以后淡出，至少 0.6 秒，最长约 3 秒。网页服务在检查有了结果、确定不更新以后才启动 |
| 有新版本 | 不淡出，标题下方显示进度（见“自动更新”），一直停到下载、安装完重启，没有时间上限。读账号的结果作废，新版本会重新读 |
| 更新中途跳过或失败 | 照“没有新版本”继续：启动网页服务，等读账号的结果（已经读完就马上）淡出 |

## 自动更新

2026-11-06 在设计稿画布上定稿（画布上的 UP1–UP7 就是本节的七张截图）。打开时发现新版本，就在启动页上下载、安装，
装完自动重启进新版本（ADR 0015）。这期间启动页停住、不淡出，标题下方显示进度；检查新版本时和平时一样，不显示任何东西。
通用设置里可以关掉，关掉以后不检查（见下文“通用设置里的开关”）。
实现任务：[GitHub Issue #327](https://github.com/cyaoc/aether/issues/327)。

| 状态 | 截图 |
|---|---|
| 下载中 | `preview/update-download-1200x720.webp`、`preview/update-download-1040x600.webp` |
| 下载中，带“跳过” | `preview/update-download-skip-1200x720.webp` |
| 安装中 | `preview/update-install-1200x720.webp` |

| 项目 | 规格 |
|---|---|
| 位置 | 一列，左 44、上 110，宽 360，行距 10。在标题正下方、星空最暗的一块 |
| 第一行 | 左边是状态文字，14 号、SemiBold、白色；右边是大小，`caption numeric`、白色、不透明度 0.8，两边间距 12 |
| 状态文字 | 下载：“正在下载新版本 v0.5.0”；安装：“正在安装 v0.5.0，马上就好” |
| 大小 | 只在下载时显示：“12.0 / 25.0 MB”，已下载 / 总大小，一位小数，1 MB = 1024×1024 字节 |
| 进度条 | 高 6、圆角 3，前景白色，轨道白色 20%（`#33FFFFFF`）。下载按字节，安装按文件数，各自从 0 走到 100 |
| 跳过按钮 | 只在下载时显示，下载一完成就消失；之后校验、解压、试跑、安装都不能跳过，那一刻才到的点击不算。进度条下方再空 6（离进度条 16）。“跳过，先用当前版本”，内边距 12,6、圆角 6，白字，底白色 15%（`#26FFFFFF`），边框 1、白色 30%（`#4DFFFFFF`）；悬停、按下底色改为白色 25%（`#40FFFFFF`） |
| 主题 | 浅色深色一样，不跟主题变 |

对比度（无头渲染截图上量的，白字叠在插画上）：状态文字均值 10.7，大小数字均值 8.6、最小窗口 7.9。
放在右下角和登录页版本号一样的位置试过，压在亮灯和白裙上，均值只有 5.3、最小窗口 3.9，没有用。

原型（截图就是按它渲染的，放在 `SplashScreen` 面板里标题后面）：

```xml
<StackPanel Width="360" Spacing="10" Margin="44,110,0,0" HorizontalAlignment="Left" VerticalAlignment="Top">
  <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
    <TextBlock FontSize="14" FontWeight="SemiBold" Foreground="White" Text="正在下载新版本 v0.5.0" />
    <TextBlock Grid.Column="1" Classes="caption numeric" Foreground="White" Opacity="0.8" Text="12.0 / 25.0 MB" VerticalAlignment="Center" />
  </Grid>
  <ProgressBar Minimum="0" Maximum="100" Value="48" Height="6" MinHeight="6" MinWidth="0" CornerRadius="3" Foreground="White" Background="#33FFFFFF" />
  <Button Content="跳过，先用当前版本" Margin="0,6,0,0" Padding="12,6" CornerRadius="6"
          Foreground="White" Background="#26FFFFFF" BorderBrush="#4DFFFFFF" BorderThickness="1" />
</StackPanel>
```

截图时为了让按钮不显示成禁用，临时去掉了根 `Panel` 的 `IsEnabled="{Binding !IsSplashOpaque}"`；实现时启动页不能跟着登录页和主界面一起被禁用。

### 通用设置里的开关

| 状态 | 截图 |
|---|---|
| 关掉以后、还没保存（浅色） | `preview/update-setting-dirty-1200x720.webp` |
| 默认开（深色） | `preview/update-setting-dark-1200x720.webp` |
| 最小窗口，保存以后 | `preview/update-setting-saved-1040x600.webp` |

| 项目 | 规格 |
|---|---|
| 位置 | 通用设置最上面一节，在“网页服务”前面，和“网页服务”一样下面空 14 |
| 标题行 | `section` “软件更新”，旁边 `hint numeric` “当前版本 v0.4.0”（实际版本号），间距 8 |
| 开关行 | 照盲盒播报那一行的写法：左边 `label` “打开时自动更新”，下面 `hint` “有新版本就在启动页上下载安装；关掉后一直用当前版本”，间距 4；右边 `ToggleSwitch.plain`，两列间距 12 |
| 默认 | 开 |
| 保存 | 和端口、日志一样：改了底部显示“有未保存的修改”，点“保存”才写进去，保存后显示“已保存。”。不做成一改就保存 |
| 页头说明 | 改成“端口保存后立即生效；日志设置重启后生效；自动更新下次打开时生效”，最小窗口也是一行 |

原型（截图按它渲染，接的是设置文件里的一项；实现时存进 GUI 设置，画面一样，所以没有设置文件那两行只读提示和错误提示）：

```xml
<StackPanel Spacing="10" Margin="0,0,0,14">
  <Grid ColumnDefinitions="Auto,*" ColumnSpacing="8">
    <TextBlock Classes="section" Text="软件更新" VerticalAlignment="Center" />
    <TextBlock Grid.Column="1" Classes="hint numeric" Text="当前版本 v0.4.0" VerticalAlignment="Center" />
  </Grid>
  <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
    <StackPanel Spacing="4">
      <TextBlock Classes="label" Text="打开时自动更新" />
      <TextBlock Classes="hint" Text="有新版本就在启动页上下载安装；关掉后一直用当前版本" />
    </StackPanel>
    <ToggleSwitch Grid.Column="1" x:Name="AutoUpdateInput" Classes="plain" AutomationProperties.Name="打开时自动更新" />
  </Grid>
</StackPanel>
```

试过、没有采用的两种：放在页面最下面，贴着“保存”栏；做成一改就保存、说明里写“改了不用保存”，和页面上其他项的保存方式不一致。
悬停、按下的底色没有画在画板上，按上表写。

## 在本地打开画板

```sh
node docs/design/splash/tools/server.mjs 8797
```

然后用浏览器打开 `http://localhost:8797/render.html?board=SP1-Splash.dc.html&props=<JSON>`。
渲染页不展开 `<dc-import>`，所以另外两种窗口用 `SP1` 加 props：

| 窗口 | props |
|---|---|
| 1200×720 | `{}` |
| 1600×900 | `{"width":1600,"height":900}` |
| 1040×600 | `{"width":1040,"height":600}` |

## 已验证

- 和 Avalonia 12.1.3 相同版本的 SkiaSharp 3.119.4 能解码这张 WebP，macOS 上每次约 11 ms。
- 画板和 `preview/` 用浏览器渲染，不是真实窗口截图；Windows 上没有验证。
- 1536 宽的图在 Retina 屏和最大化窗口上会被放大，略软。启动页只停留不到一秒，先不处理。
- 自动更新的七张截图是按上面的原型在 macOS 上无头 Skia 渲染的真实界面，进度、大小和版本号是写死的示例值；Windows 上没有验证。
