# 登录页插画的来源

`textures/login.webp` 在 2026-11-05 由 Codex CLI 0.166 的内置生图功能（OpenAI 图像生成）生成，分两步，最后用 Pillow
转成 WebP（quality 80，method 6）。这里只作来源记录，**实现时直接用定稿，不要重新生成**。

1. **竖图 1024×1536**：`tools/gen.sh`，参考图是启动页插画（人物）和 `docs/design/app-icon/aether-1024.png`（气泡精灵）。
   出了三张候选，选了第 1 张：双手拿手机、朝右看（"更像扫码"）。
2. **扩成横图 1536×1024**：把那张竖图按高度缩成 683×1024 放在左边，右边用它最右一列像素拉伸占位，就是
   `tools/extend-input.webp`；再用 `tools/extend.sh` 只重画右边。三张里选了第 1 张：光带从手机一直流到右边，从卡片后面穿过。

固定段落，生成时总会接上：

- `tools/prompt-base.txt`：竖图的画风、角色、精灵、配色、版式和裁切安全区。画风、角色和精灵的写法照抄启动页。
- `../splash/tools/prompt-anatomy.txt`、`../splash/tools/prompt-negative.txt`：人体结构和负向词，直接用启动页的。
- `tools/prompt-negative-login.txt`（竖图）、`tools/prompt-negative-landscape.txt`（横图）：登录页另加的负向词，
  不画二维码、手机上不画 logo 和界面、人物不看镜头也不朝左。

选中的竖图场景描述：

> Framed from mid-thigh up. She holds a slim plain smartphone in front of her chest with both hands, as if about to scan a code just beyond the right edge: the back of the phone faces the right edge and its screen faces her, showing only a soft blank glow. Her right hand, nearer the viewer, holds the lower half of the phone; her left hand, on the far side, holds its upper edge; both forearms are visible from elbow to wrist. She smiles warmly toward the right edge. The sprite floats ahead of her face, between her and the right edge, its eyes also turned to the right. Hair and ribbons drift gently toward the right.

## 检查

- 用 2 倍裁图看每只手，顺着前臂追到袖口和肩膀：两只手都握着手机，张开的手指数量对，没有多余的手。
- 扩图时模型把左边也重画了一遍（和输入的平均差约 8 到 11），所以又对着竖图核对了一次手，握法和手指都没变。
- 这次没有发现新的问题，负向词没有因此增加。
