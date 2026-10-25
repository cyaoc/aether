# Bilibili 在线榜人物信息研究

研究日期：2026-10-25。按用户要求，方案依据限于 2026-07-01 之后发布的源码或本次实测。`blivem` 暂按 `xfgryujk/blivedm` 理解。本文件不修改应用代码。

## 结论与证据边界

可以从 HTTP 在线榜接口读取榜单快照，也可以从 WebSocket `ONLINE_RANK_V3` 读取榜单消息。不能把“榜单上的用户”当作“直播间全部在线观众”；这些接口没有给出完整在线成员名册或每个人持续在线的保证。源码中声明的字段也不等于服务端每次都会返回完整值。

已找到满足日期要求的源码：

- PHP `hejunjie/bililive` v1.0.15，提交 `3bf6ad36e23104add125c84f582079511bdca26e`，GitHub commit 时间 2026-07-24T05:01:45Z；[发布页](https://packagist.org/packages/hejunjie/bililive)、[固定源码](https://github.com/zxc7563598/php-bilibili-danmu-core/tree/3bf6ad36e23104add125c84f582079511bdca26e)。该版本发布改动是类型安全检查，不能声称在线榜实现于当天重新验证。
- Python `xfgryujk/blivedm` 提交 `3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6`，日期 2026-08-15；[源码](https://github.com/xfgryujk/blivedm/tree/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6)。
- Go `Akegarasu/blivedm-go` 提交 `957b543d2bfa8c2ac8a7af2fb42ae73eb92443b2`，日期 2026-09-26；[源码](https://github.com/Akegarasu/blivedm-go/tree/957b543d2bfa8c2ac8a7af2fb42ae73eb92443b2)。

这些是第三方实现的一手源码，并非 Bilibili 官方稳定 API 合约。以下把源码证据和实际响应分开。

## HTTP：获取榜单快照

PHP 版本直接调用：

```http
GET https://api.live.bilibili.com/xlive/general-interface/v1/rank/getOnlineGoldRank?ruid=<主播UID>&roomId=<真实房间号>&page=1&pageSize=50
Origin: https://live.bilibili.com
Referer: https://live.bilibili.com/<真实房间号>
Cookie: <当前登录会话>
```

`ruid` 是主播 UID，不能填查询者 UID；`roomId` 注意大小写。该实现附带 Cookie，没有为这个接口计算 WBI。检查 HTTP 状态和 JSON `code == 0` 后读取 `data.OnlineRankItem`。库实际写的是 `pageSize=5000`，这只能证明作者请求了 5000，不能证明服务端允许这一上限或能拿全；上面的 50 只是较保守的请求示例，分页上限仍需实测。[Live.php L253-L295](https://github.com/zxc7563598/php-bilibili-danmu-core/blob/3bf6ad36e23104add125c84f582079511bdca26e/src/Live.php#L253)、[API 配置](https://github.com/zxc7563598/php-bilibili-danmu-core/blob/3bf6ad36e23104add125c84f582079511bdca26e/src/Config/api.php)。

该版本实际读取的用户字段：

| 原始字段 | 含义 |
| --- | --- |
| `userRank` | 榜单名次 |
| `uid` | 返回的用户 UID |
| `name` | 显示名称 |
| `score` | 贡献值；不应直接解释为金额 |

另外读取 `data.onlineNum`。仅凭该字段名和第三方注释，不能证明它是完整、精确的实时在线人数。[同一方法](https://github.com/zxc7563598/php-bilibili-danmu-core/blob/3bf6ad36e23104add125c84f582079511bdca26e/src/Live.php#L282)。

### 本次匿名请求实测

通过官方 `room/v1/Room/get_info?room_id=5445` 得到 `uid=346059`、`live_status=0`。随后不带 Cookie 请求上述榜单接口（page=1、pageSize=50、Origin 和 Referer），返回 `code=0`，但该房间未开播，`OnlineRankItem=[]`，所以**未验证非空用户条目的字段和身份完整性**。

响应包含 `onlineNum=0`，以及 `ownInfo` 中的 `uid/name/face/rank/needScore/score/guard_level/wealth_level/is_mystery/uinfo`；匿名请求的 `uid=0`、`uinfo=null`。`ownInfo` 是自身栏位，不能据此推断每个榜单成员有相同结构。`tips_text` 明确写着“投喂、点赞、发弹幕均可上榜”，因此贡献值不能直接当成付费金额。[官方端点](https://api.live.bilibili.com/xlive/general-interface/v1/rank/getOnlineGoldRank?ruid=346059&roomId=5445&page=1&pageSize=50)。

尝试读取公开直播列表以选择正在直播的样本时，返回业务码 `-352`；没有绕过风控，也没有使用本机登录凭证。匿名空榜成功不代表登录态要求、非空榜返回量、分页或隐身用户规则已经验证。

## WebSocket：ONLINE_RANK_V3

Python `blivedm` 的 `handlers.py` 把 `ONLINE_RANK_V2`、`ONLINE_RANK_V3` 和 `ONLINE_RANK_COUNT` 列为已知但默认忽略的消息，没有提供对应人物榜单模型回调。接收这些消息需要在自定义 Handler 中复制 `BaseHandler._CMD_CALLBACK_DICT` 并注册原始 command 回调；不能误以为现成弹幕回调会自动产出榜单用户。[handlers.py](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/handlers.py)。

2026-09-26 的 Go 实现提供 `ONLINE_RANK_V3` 对应结构。其解码路径是读取 `command.data.pb`，以 `base64.StdEncoding` 解码，再按 Protobuf `EventOnlineRank` 反序列化；该消息有 `rank_type` 和 `online_list`。[parse.go](https://github.com/Akegarasu/blivedm-go/blob/957b543d2bfa8c2ac8a7af2fb42ae73eb92443b2/message/parse.go)。原始 Protobuf 用户条目声明以下字段：[online_rank_v3.go](https://github.com/Akegarasu/blivedm-go/blob/957b543d2bfa8c2ac8a7af2fb42ae73eb92443b2/message/online_rank_v3.go)、[events.proto](https://github.com/Akegarasu/blivedm-go/blob/957b543d2bfa8c2ac8a7af2fb42ae73eb92443b2/pb/events.proto)。

| 信息 | 字段 |
| --- | --- |
| 用户标识、名称、头像 | `uid`、`uname`、`face` |
| 名次、贡献值 | `rank`、`score`（字符串） |
| 大航海等级 | 可选 `guard_level`，以及 `uinfo.guard.level` |
| 神秘用户标记 | `is_mystery`、`uinfo.base.is_mystery` |
| 名称颜色 | `uinfo.base.name_color` |
| 粉丝牌 | `uinfo.medal.name/level/color/ruid/color_v2` |
| 荣耀等级 | `uinfo.wealth.level` |
| 大航海到期显示文字 | `uinfo.guard.expired_str` |

`uinfo` 还声明 `uid`、`base.name`、`base.face`。**这里必须区分原始协议与库封装：同版本 `userInfoFromProto` 只映射 uid/base/guard/wealth，没有映射 medal，因此现成 Go `OnlineRankV3.Uinfo` 会丢弃粉丝牌信息；需要读取原始 Protobuf 字段才能保留它。**[gift_v2.go](https://github.com/Akegarasu/blivedm-go/blob/957b543d2bfa8c2ac8a7af2fb42ae73eb92443b2/message/gift_v2.go)。粉丝牌的 `ruid` 是牌子关联的主播，不能未经比较就当作当前直播间的牌子。`expired_str` 是显示字符串，不保证可推算精确到期时间。声明存在不保证每个用户有值；对于神秘用户，只能按返回结果展示，不能承诺获取真实 UID 或完整身份。以上为模型证据，尚未进行本次真实 WebSocket 非空榜单抓包。

## 以榜单 UID 补充用户资料

同一 2026-07-24 PHP 版本给出两个独立请求路径：

- `GET /live_user/v1/Master/info?uid=<UID>`：`getMasterInfo()` 不传 Cookie，读取 `data.info.uid/uname/face`。[L502-L533](https://github.com/zxc7563598/php-bilibili-danmu-core/blob/3bf6ad36e23104add125c84f582079511bdca26e/src/Live.php#L502)。
- `GET https://api.bilibili.com/x/space/wbi/acc/info?mid=<UID>&...WBI参数`：`getStreamerInfo()` 计算 WBI、附带 Cookie，读取 `mid/name/sex/face/sign`，即公开名称、公开填写性别、头像和签名。[L459-L499](https://github.com/zxc7563598/php-bilibili-danmu-core/blob/3bf6ad36e23104add125c84f582079511bdca26e/src/Live.php#L459)。

这些是额外的公开资料查询，不是在线榜直接返回的信息；不能据此获取真实姓名、联系方式、地址等私人资料，也不能恢复神秘用户身份。本次未实测这两个补充接口。若只需列表名称和头像，优先使用榜单已经返回的内容，避免为每个用户增加网络请求。

## 未采纳为新方案的资料

`qydysky/bili_danmu` v0.21.10 的标签确实发布于 2026-09-24，但 `Json/getOnlineGoldRank.go` 最后修改是 2023-01-19，保留 `face`、`medalInfo`、`guard_level` 等旧字段不能充当 2026 年 7 月以后实际响应证据。[版本](https://github.com/qydysky/bili_danmu/commit/e98c611eac19dfcc20a7cae99ceea9393d9b42f6)、[字段文件历史](https://github.com/qydysky/bili_danmu/commits/master/Json/getOnlineGoldRank.go)。

本研究没有证明所有榜单用户的完整字段、登录与匿名差异、神秘用户具体脱敏规则、服务端分页上限或稳定调用频率。需要在目标直播间对照浏览器榜单做一次登录态非空响应采样后，才能把“模型支持”提升为“当前可读”。

## 目标直播间 1851401619 实测（2026-10-25）

本节更新前文的非空榜单验证边界。直接请求 Bilibili API，全程没有 Cookie、WBI 或使用本机账号凭据。房间信息返回 `live_status=1`、标题“Kitty的直播间”、主播 UID `3493282006960948`。

### 推荐读取入口

```sh
curl --max-time 20 --silent --show-error \
  -H 'Referer: https://live.bilibili.com/1851401619' \
  'https://api.live.bilibili.com/xlive/general-interface/v1/rank/queryContributionRank?ruid=3493282006960948&room_id=1851401619&page=1&page_size=100&type=online_rank&switch=contribution_rank&platform=web&web_location=444.8'
```

检查 `code == 0`，读取 `data.item`。本次 `page_size=100` 返回 100 个不同 UID、排名 1—100，`data.count=346`。它包含贡献值为 0 的观众。对比旧 `getOnlineGoldRank`：`pageSize=50` 仅返回 16 人；新接口首份 50 人样本也正好只有 16 人贡献值非零。这支持旧接口本次只列有贡献值人物的判断，不能泛化为官方永久规则。

### 返回范围与分页

| 请求 | 本次实际返回 |
| --- | --- |
| 旧接口 page=1, pageSize=50 | 16 人，排名 1—16，onlineNum=352 |
| 旧接口 page=2, pageSize=50 | 空列表 |
| 旧接口 page=2, pageSize=5 | 5 人，排名 6—10，UID 与首份样本对应位置一致 |
| 新接口 page=1, page_size=50 | 50 人，排名 1—50，count=338 |
| 新接口 page=2/7/8, page_size=50 | 每次仍是排名 1—50 |
| 新接口 page=1, page_size=100 | 100 人，排名 1—100，count=346 |
| 新接口 page=1, page_size=500 | 仍为 100 人，排名 1—100，count=343 |
| 新接口 page=2, page_size=100 | 仍为 100 人，排名 1—100，count=342 |

请求发生在不同时间，总数和成员会变化。**本次匿名参数组合下，新接口最多实测拿到前 100 人，page 没有带来后续排名，不能按 count 循环 page 并宣称拿全。** 未证明登录后是否不同，也未证明存在可读取后续排名的其他参数。

### 真实人物字段

新接口每条都有 `uid/name/face/rank/score/guard_level/wealth_level/is_mystery/uinfo`。`uinfo.base` 返回昵称、头像、昵称颜色及认证信息结构；`uinfo.wealth.level` 返回荣耀等级；`uinfo.guard` 返回 `level` 和 `expired_str`。100 人样本均 `guard_level=0`、`is_mystery=false`，所以没有验证非零大航海、有效到期文字或神秘人的脱敏行为。

新接口 100 人中只有 3 人的 `uinfo.medal` 非空；其余为空，不能解释成这些观众全站没有粉丝牌。旧接口 16 人中有 8 人携带粉丝牌资料。例如榜首“狸猫大舅最快乐”（UID `3494371104131469`）旧接口返回荣耀等级 31、粉丝牌“罗罗锤”25 级、所属主播 `33837428`（并非当前主播）；新接口中同 UID 的 `uinfo.medal=null`。因此两接口对粉丝牌的返回口径确实不同，不能互相代替或简单用 null 覆盖已知牌子。

拿在线榜名称/头像/荣耀等级，直接用新接口即可，不需要先接 blivedm。若需要旧接口额外返回的佩戴粉丝牌，可对其实际覆盖到的 UID 补充；不能保证覆盖新接口的 100 人。

样本保存在本机临时目录 `/tmp/aether-contribution-rank-1851401619-size100.json` 和 `/tmp/aether-online-rank-1851401619-p1.json`，未将完整观众列表写入仓库。本次未对照网页登录后的榜单，也未抓取 WebSocket。

## ONLINE_RANK_V3 目标房间实测（2026-10-25，UTC+8）

本节更新前文“未抓取 WebSocket”的边界。连接目标仍为 1851401619，匿名 uid=0，以公开匿名 buvid、WBI getDanmuInfo 获取服务器和 token，WebSocket 认证返回 code=0。没有读取用户登录凭据，没有改动 Aether 应用代码。临时采样程序复用项目的协议流程，处理分包、zlib/Brotli 和 Base64 protobuf，并对包解码及嵌套 protobuf 做了 assert 自检；直接枚举整个 payload，没有用户条数截断。

### 能否突破 HTTP 的前 100 人

**本房间本次观察不能：V3 推送覆盖范围远小于 HTTP 的前 100 人。**

第二段连接采样约 190 秒，榜单相关消息时间为 07:45:57—07:48:56：

| 指标 | 实测 |
| --- | --- |
| ONLINE_RANK_V3 | 22 条，rank_type 全为 online_rank |
| 每条 V3 人数 | 始终 7 人，rank 始终 1—7 |
| 多条 V3 累计去重 UID | 8 人，全部存在于采样开始时的 HTTP 前 100 人 |
| V3 到达间隔 | 7.71—9.18 秒，中位数 7.82 秒；只是本次观察值 |
| ONLINE_RANK_COUNT | 57 条，报告人数 372—407，但不携带人物名单 |
| 同期 HTTP 快照 | 返回 100 人，开始时 count=373 |

第一段连接另收到 5 条 V3，每条同样 7 人；因临时工具默认的标准 WebSocket ping 未收到 pong而超时，该段提前结束，不计入上表。第二段禁用这层 ping，仍每 30 秒发送 Bilibili 应用层心跳，正常采样结束。

客户端解析器没有自行限制 7 或 100 人：raw protobuf 顶层是 field1（rank_type）和 7 个 field3（用户），不存在未解出的后续名单。不存在证据把“本房间每条 7 人”推广为所有直播间的固定上限。也不能从当前样本推出更长时间累计 UID 永远小于 100；即便累计超过 100，也只是历史出现集合，不能证明这些人仍在线。

### 实际人物字段与缺失字段

22×7=154 个榜单条目都携带：`uid`、`uname`、`face`、字符串 `score`、`rank`、`uinfo.uid`、`uinfo.base.name/face`、`uinfo.guard`。其中 guard 是空 protobuf message，level 默认解释为 0；没有非零大航海样本或有效到期文字。

**所有 154 条的 uinfo 都未下发 medal 与 wealth 字段。** 对比同一 UID，HTTP 榜首荣耀等级为31，而 V3 完全没有 wealth；因此消费端应保留“未提供”状态，不能将缺失当作0去覆盖 HTTP 已知信息。用户项的可选 guard_level/is_mystery 也未在本段原始 wire 中出现，不能声称已验证神秘用户身份或脱敏行为。

`base` 还包含 field5、6、7。与 2026-08-04 的共享 UserInfo schema 及 HTTP 对照后，其结构分别对应 `risk_ctrl_info`、`origin_info`（昵称与头像）和 `official_info`。本样本前两者重复显示昵称/头像；认证字段只有 type 的 -1 编码。该参考 proto 把 type 声明为 uint64，与有符号解释存在差异，不应把 varint 的 uint64max 当作有效认证类型。[固定 proto](https://github.com/zxc7563598/bilibili-live-assistant/blob/304c7c09c4eadc909caa0c8a95cac7800f41aa6e/pkg/bilibili/internal/protobuf/interactwordv2/INTERACT_WORD_V2.proto#L119)。

### 更新语义与建议

V3 是一个带 rank_type 的榜单列表；当前解析实现没有分页游标、后续页请求、增删操作或全体在线名单完整标志。Rust 原始参考在 2026-07-28 的实现逐项 push，没有 take/truncate/100，因此客户端模型可容纳更多人，但不能推论服务端必定推送更多。[Rust 固定实现](https://github.com/Akegarasu/danmuji-next/blob/0ea257d4fd48895bb7f5617aac3cc332c73198db/crates/blivedm/src/message/online_rank_v3.rs)。2026-09-26 Go 实现同样如此，单人合成测试不构成服务端人数范围证据。[Go 固定实现](https://github.com/Akegarasu/blivedm-go/blob/957b543d2bfa8c2ac8a7af2fb42ae73eb92443b2/message/online_rank_v3.go)。没有找到 2026-07 之后证明 V3 服务端推送 100 人以上的一手证据。

Aether [issue #154](https://github.com/cyaoc/aether/issues/154)（2026-10-16）另有已登录小房间实测：名单 A/B→A→A/B，UID昵称有效，大航海 level=3 可见；medal/wealth 同样未下发。这补充了登录态可有大航海信息的证据，但不是本次房间登录态测试，也不能证明大房间名单完整。

建议继续 HTTP 获取前 100 人快照；V3 只能加速更新其实际覆盖的榜首人物的排名、贡献值及身份字段。不要用每条 7 人覆盖整份 100 人列表，不要把未出现在 V3 中当作离场，也不要用缺失字段清空已知的粉丝牌/荣耀等级。ONLINE_RANK_COUNT 可更新人数显示，不能补充名单。要判断本房间登录态能否下发更多，需要另做同房间登录态对照；当前匿名结果不足以回答该差异。

原始事件（含原始 pb 和解码值）：`/tmp/aether-v3-capture.jsonl`；摘要：`/tmp/aether-v3-summary.json`；采样脚本：`/tmp/aether_v3_probe.py`。事件文件 SHA-256：`86ac789708b7b3fe375e6326ac4e0306f1701bb72327e9f70117d0c5f5c078c8`。完整观众数据只存临时目录，没有提交入仓库。
