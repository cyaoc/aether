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
