# Aether 应用图标

用户已选定蓝白配色、对话气泡与双眼的造型，并授权用 SVG 几何重绘。
`aether.svg` 是唯一母版：背景和双眼为 `#4255FF`，气泡为 `#FFFFFF`，
圆角方块外透明；无渐变、纹理、阴影、字体和外部图片。

GUI 项目直接引用本目录的 ICO 和 ICNS 并嵌入程序集；Windows EXE 同时嵌入 ICO。
macOS 运行时使用 ICNS 设置 Dock 图标，不新增 `.app` 打包或迁移数据。
接入任务：[GitHub Issue #106](https://github.com/cyaoc/aether/issues/106)。

- `aether.svg`：1024 × 1024 坐标的矢量母版。
- `aether-*.png`：16、24、32、48、64、128、256、512、1024 像素，RGBA。
- `aether.ico`：16、24、32、48、64、128、256 像素帧，供 Windows 使用。
- `aether.icns`：供 macOS 使用，最大 1024 像素；不代表已经支持 `.app` 发布。
- `preview.png`：浅色／深色背景下的 256 像素和 16–64 像素预览。
- `export.py`：从 SVG 导出上述文件，并检查透明边界、纯色填充及容器解码。

重新导出只需要独立的素材工具环境，不向 Aether 添加运行时依赖：

```sh
uv venv /tmp/aether-icon-tools
uv pip install --python /tmp/aether-icon-tools/bin/python cairosvg pillow
/tmp/aether-icon-tools/bin/python docs/design/app-icon/export.py
```

CairoSVG 需要本机 Cairo。macOS 若已用 Homebrew 安装 Cairo、但动态库未被找到，
可为最后一条命令设置 `DYLD_FALLBACK_LIBRARY_PATH="$(brew --prefix)/lib"`。

2026-10-13 验证记录：

- 已检查上述尺寸在浅色和深色背景上的静态预览，以及 PNG／ICO／ICNS 解码。
- GUI Headless 测试验证主窗口和退出确认窗口加载图标。
- 项目构建为 0 警告、0 错误；完整测试 805 通过、0 失败，4 个截图测试因未设置输出目录跳过。
- Windows x64 Release 交叉发布后，EXE 内的七个图标帧与 ICO 完全一致。
- macOS 原生检查在初始化真实 App 后读取 NSApplication 图标，确认图像有效；
  转回 sRGB 后的蓝／白像素与透明边界符合母版（颜色管理舍入误差不超过 1）。
- 用户确认 Debug 版、仓库外的 Release 版及 Release 关闭重开后 Dock 图标正确。
  Release 可执行文件在 `/private/tmp/aether-106-release/`，启动工作目录为 `/private/tmp`。
- 尚未进行 Windows 任务栏／资源管理器实机验收，也未完成两平台深浅色桌面的完整组合验收。

macOS `.app` 打包会触及 ADR 0002 的数据目录约定，应另行解决，不能仅为图标改动
登录凭据、设置或数据目录。
