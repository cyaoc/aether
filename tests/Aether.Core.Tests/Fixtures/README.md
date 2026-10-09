# 真实流量测试数据

2026-10-05 从用户指定的直播间 `1768506153` 匿名抓取；认证 uid 为 0，
无 SESSDATA。分别用 protover 3 和 2 协商 brotli / zlib。

| 文件 | 捕获内容 | 原始帧 SHA-256 |
| --- | --- | --- |
| danmaku-brotli.bin | `DANMU_MSG`，`达***`：`[dog]运气抵抗有用吗？` | `1c7383eac9241510b6a79c228fe634f8cfc957e5aeadfe3bc29eecfae99d4c55` |
| danmaku-zlib.bin | `DANMU_MSG`，`琴***`：`小姐姐没有帮忙抗的吗[dog]` | `7ed90fba68ebb371562b0cc9f34942d1b229cc376e7653394de52d64f88d985a` |
| multiple-zlib.bin | 一个压缩帧内连续两个 `ENTRY_EFFECT` 包 | `4fec20ef7f376f44bb0183ceefbaf81d4c344400ebf83a848410b8cd0857bc3c` |
| authentication.bin | op 8，`{"code":0}` | `402363f1c668316331a59845ce419b9be5414f00705b034b3a6949ccbd150bc9` |
| heartbeat.bin | op 3，人气值 1 | `c0d1c3a214520a24a8acd4669d13e91c7e87c6887e4a047ed005425634dc9911` |

认证和心跳文件保留原始字节。三个消息文件源于真实捕获，但经过隐私处理，
**不是原始帧的逐字节副本**：弹幕保留原始文字、服务端打码昵称、零 uid，
清空 `info[0]`、`info[2]` 中其余字段以及 `info[3..]` 的头像、哈希、勋章等附加信息；
进场事件清空 `data`（匿名连接的进场事件仍可能返回真实 uid 和昵称）。
处理后保留协议版本、op、seq 和包顺序，更新长度并用原压缩算法重新压缩。
不得把临时目录里的原始帧直接提交。

采集流程：`room_init` → 匿名 `spi` 获取 buvid3 → `nav` 获取 wbi key →
签名 `getDanmuInfo` → 返回服务器的 WSS `/sub` → uid 0 认证 → 每 30 秒心跳。
按 WebSocket EndOfMessage 合并片段后保存消息，解压检查每个内部包的隐私字段。
不保存出站认证包、buvid3 或 token。合成边界样本在测试代码中明确构造，
用于覆盖多个弹幕包、分片、坏长度等不能依赖现场恰好出现的情况。

## 用 Trace 日志抓取直播间消息

1. 把程序实际使用的 `data/aether.yml` 中 `log.level` 改为 `Trace`：

   ```yaml
   log:
     level: Trace
   ```

2. 重启程序，正常连接目标直播间。开发时可在仓库根目录运行
   `dotnet run --project src/Aether.Cli -- watch <房间号>`，也可用 GUI 连接。
   数据目录见 ADR 0002：Debug 构建用仓库根目录下的 `data/`，Release 构建用程序目录下的 `data/`。
3. 查看 `data/logs/<真实房间号>/aether-yyyyMMdd.log`（日期按本地时间）。
   即使输入短房间号，目录名也是解析后的真实房间号。先用普通弹幕确认出现
   `[VRB] ... 直播间消息原始 JSON：{...}`，再采集所需事件。
4. 采集完成后把级别恢复为 `Information` 并重启。Trace 会记录大量含个人信息的消息。

每条成功解析的直播间消息记录一次，包括 bot 尚未处理的 cmd。记录的是解压后的原始 JSON，
不截断、不重新序列化；只移除 JSON 格式中的实际 CR/LF，让每条记录占一行，
字符串内的 `\n`、`\r` 转义保持原样。Debug 及以上级别不记录这些原始 JSON。
无法解析的消息仍只写 Warning，按原有规则最多保留 2KB JSON，不再另记 Trace。
认证包和心跳不是直播间消息，不在此抓取范围。

### 按 cmd 提取

以下命令需要 Python 3。在对应 `data/` 的上级目录执行，替换日志路径；
结果仍保存在被 Git 忽略的 `data/` 内，每行一个 JSON：

```sh
python3 - 'data/logs/7734200/aether-20261009.log' \
  SEND_GIFT SEND_GIFT_V2 COMBO_SEND DANMU_MSG > data/room-messages.jsonl <<'PY'
import json
import sys

commands = set(sys.argv[2:])
with open(sys.argv[1], encoding="utf-8") as log:
    for line in log:
        _, marker, raw = line.partition(" Aether.Core.AetherClient: 直播间消息原始 JSON：")
        if marker and json.loads(raw)["cmd"].split(":", 1)[0] in commands:
            print(raw, end="")
PY
```

参数可只保留需要的 cmd，例如 `SEND_GIFT_V2`。`DANMU_MSG` 也会匹配带版本后缀的 cmd。
脚本按日志类别 `Aether.Core.AetherClient` 匹配；有原始 JSON 却提取不到时，先确认日志里的类别名没变。
只想知道直播间当前推 V1 还是 V2 礼物消息时不必开 Trace：Information 日志会写明。
此方法得到 JSON，不是带包头的 WebSocket 二进制帧；保存为测试数据时应注明捕获日期、
事件背景、cmd 和脱敏范围，合成样本须明确标为合成。

### 提交前隐私处理

沿用上面的隐私规定：**不得直接提交原始日志、提取结果或真实 uid、昵称**。
复制所需样本后，将真实 uid 替换为固定测试值、昵称替换为测试昵称，清除头像、哈希、勋章等
与测试无关的个人信息；检查嵌套字段以及弹幕正文中的个人信息，不保存 cookie、buvid3 或 token。
V2 的 base64/protobuf 载荷也可能含个人信息，不能只修改外层 JSON：需解码、脱敏并重新编码；
尚不能解码检查的载荷仅留本地，不提交。保留待验证字段及其关联关系，检查脱敏后的样本仍能复现行为。
