# 盲盒礼物事件调研

调研日期：2026-10-07。用途：设计"盲盒统计"。观众送盲盒时，记录每次送达的实付金额和爆出礼物的价值，再按观众按天汇总盈亏（电池）。

标记说明：

- **[文档]**：pskdje/bilibili-API-collect（下称 BAC，`master` 分支 `cfc5fdd`，与 `main` 分支的这几份文件完全相同），或 B站直播开放平台官方文档。
- **[源码]**：在维护的开源客户端代码。
- **[实测]**：公开的真实抓包或实测记录。
- **[推断]**：本文推断，来源没有直接证实。
- **[冲突]**：来源之间不一致。

## 0. 先看这条：SEND_GIFT_V2

- 2026-07 起，B站对送礼事件灰度推送新的 `SEND_GIFT_V2`。外层仍是 JSON，礼物数据在 `data.pb` 里（base64 + protobuf）。[源码] blivedm 的注释：`# 礼物（2026-07 灰度的新协议，protobuf 编码）`，见 [handlers.py#L149-L152](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/handlers.py#L149-L152)；另见 [blivedm issue #85](https://github.com/xfgryujk/blivedm/issues/85)。
- 已切到 V2 的房间只发 V2，不再发 `SEND_GIFT`；房间还会在 V1 和 V2 之间来回切换，机制未知。[实测] PR 作者原话："切换到了V2的直播间，礼物只会发V2，不会发原版信息"（[PR #86 评论](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5101811876)）；"有时候前一天是V2的直播间，第二天又改回V1了，机制未知"（[PR #86 评论](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5231530390)）。
- 一条 V2 消息里有 `repeated gift_list`，每项是一种礼物（[pb.py#L81-L88](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/pb.py#L81-L88)）。BAC 存档停在 2026-01，完全没有提到 V2。
- 弹幕姬 Bilibili_Danmuji 也已经解析 V2，并逐项遍历 `gift_list`（[ParseMessageThread.java#L261-L279](https://github.com/BanqiJane/Bilibili_Danmuji/blob/d659cbc511f44f36ac54c2880f0d6687570ca1bf/src/main/java/xyz/acproject/danmuji/thread/core/ParseMessageThread.java#L261-L279)）。

## 1. SEND_GIFT 字段（V1，JSON）

BAC 的字段表见 [message_stream.md#L1495-L1606](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1495-L1606)，大部分字段写着"待调查"。下表合并了其他来源。

| 字段 | 含义 | 单件还是合计 | 来源 |
| --- | --- | --- | --- |
| `uid` / `uname` | 送礼者。连接未登录时 uid 为 0，昵称打码 | — | [文档] [BAC#L126](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L126)；[源码] blivedm 的"不填也可以连接，但是收到弹幕的用户名会打码，UID会变成0"（[sample.py#L21-L22](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/sample.py#L21-L22)）；[实测] [Pcrab 日志](https://github.com/Pcrab/bili-welcome/blob/5600c7be57ec5fe8b6a6a2c0c6702026ec575914/log.json)里有 `"uid":0`, `"uname":"南***"` |
| `giftId` / `giftName` | 盲盒时是**爆出的礼物**，不是盲盒本身 | — | [文档] 官方开放平台："道具id(盲盒:爆出道具id)"（[开放平台 长链命令说明](https://open-live.bilibili.com/document/f9ce25be-312e-1f4a-85fd-fef21f1637f8)，LIVE_OPEN_PLATFORM_SEND_GIFT 一节）；[实测] Pcrab 日志：`giftId 32698 小蛋糕`，`blind_gift.original_gift_id 32649 星月盲盒` |
| `num` | 该次投喂的数量 | — | [文档] [BAC#L1545](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1545) |
| `price` | 单价（瓜子）。盲盒时是爆出礼物的单价 | 单件 | [源码] "礼物单价瓜子数，送盲盒则是爆出礼物的单价"（[web.py#L286-L287](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/web.py#L286-L287)）；[文档] 开放平台："礼物爆出单价…盲盒:爆出道具的价值" |
| `total_coin` | **实付**总价。普通礼物 = 折后单价 × num；盲盒 = 盲盒单价 × num | 合计 | [源码] [web.py#L292-L293](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/web.py#L292-L293)；[文档] "实际金银瓜子总价值，不是总等于 num*price"（[BAC#L1563](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1563)）；[实测] 见第 2 节 |
| `discount_price` | 普通礼物时等于 price；盲盒时实测也等于爆出单价 | 单件 | [文档] BAC 写"待调查"，示例为 `100`（[BAC#L1651](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1651)）；[实测] Pcrab 日志中盲盒礼物 `discount_price 1500 = price 1500`。**[冲突]** lovelyyoshino 的逆向文档写的是"盲盒场景下的盲盒原价"，后经证实是字段号对调造成的误读（见第 2.3 节） |
| `combo_total_coin` | 连击累计；实测是按**爆出价值**计算的 | 合计 | [文档] BAC 写"待调查"（[#L1521](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1521)）；[实测] Pcrab 日志：盲盒 num=1 时为 `1500`（等于爆出价值，不是 5000），10 个小花花时为 `1000`。[推断] 盈亏计算不要用这个字段 |
| `coin_type` | `gold`（付费）或 `silver` | — | [源码] "'silver'或'gold'，1000金瓜子 = 1元"（[web.py#L290-L291](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/web.py#L290-L291)） |
| `tid` / `rnd` | 见第 5 节 | — | |
| `timestamp` | 送礼时间，Unix 秒 | — | [文档] [BAC#L1561](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1561) |
| `batch_combo_id`, `combo_send{combo_id, combo_num, gift_num}`, `batch_combo_send{batch_combo_id, batch_combo_num, gift_num, blind_gift}` | 连击和批次的标识与计数 | — | [文档] [BAC#L1511-L1519](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1511-L1519)，[#L1566-L1580](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1566-L1580)；[实测] 盲盒的 `batch_combo_id` 是 UUID，普通礼物是 `batch:gift:combo_id:{uid}:{ruid}:{giftId}:{ts}`；盲盒的 `batch_combo_send` 里也带 `blind_gift`（Pcrab 日志） |
| `super_gift_num` / `super_batch_gift_num` | 未记载。实测一次送 10 个小花花时分别为 `10` 和 `1` | — | [文档] BAC 写"待调查"（[#L1555-L1556](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1555-L1556)）；[实测] Pcrab 日志 |
| `switch` | 未记载。B站前端代码只在 `switch` 为真时显示礼物 | — | [文档] BAC 写"待调查"（[#L1558](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1558)）；blivedm 作者在 [PR #86 评论](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5104705486)里贴出的前端代码：`if (decoded.switch) {...} else if (message.data.switch) {...}`。[推断] 这段代码像是反混淆后的 B站 Web 前端代码，评论没有说明出处 |

**单件与合计**：`price`、`discount_price`、`blind_gift.gift_tip_price`、`blind_gift.original_gift_price` 是单件；`total_coin` 和 `combo_total_coin` 是合计。

## 2. blind_gift 对象

### 2.1 V1 真实样本

BAC 对 `blind_gift` 只写了"待调查"，示例里是 `null`（[#L1515](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1515)、[#L1632](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1632)）。下面的真实消息摘自 [Pcrab/bili-welcome log.json](https://github.com/Pcrab/bili-welcome/blob/5600c7be57ec5fe8b6a6a2c0c6702026ec575914/log.json)（2024-11 抓取，只保留相关字段）：

```json
{"cmd":"SEND_GIFT","data":{
  "giftId":32698,"giftName":"小蛋糕","num":1,"price":1500,"discount_price":1500,
  "total_coin":5000,"combo_total_coin":1500,"coin_type":"gold",
  "tid":"4578879044749173248","rnd":"4578879044749173248","timestamp":1732634266,
  "batch_combo_id":"ae760dae-9ecb-4780-8ae3-6b22a2f741a7",
  "blind_gift":{"blind_gift_config_id":67,"from":0,"gift_action":"爆出","gift_tip_price":1500,
                "original_gift_id":32649,"original_gift_name":"星月盲盒","original_gift_price":5000}}}
```

同一份日志里还有一条：爆出 `冲鸭`，`price 9900`、`gift_tip_price 9900`、`total_coin 5000`。

### 2.2 字段含义

| 字段 | 含义 | 来源 |
| --- | --- | --- |
| `original_gift_id` / `original_gift_name` | 盲盒本身的礼物 id 和名字 | [实测] 上面的样本；[源码] blivedm 取 `original_gift_name` 作盲盒名（[web.py#L323-L329](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/web.py#L323-L329)） |
| `original_gift_price` | 盲盒**单价**（金瓜子） | [源码] "盲盒单价瓜子数"（[web.py#L306-L307](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/web.py#L306-L307)）；[实测] 一条 V1 消息 `num=3` 时 `total_coin=15000`，而幸运盲盒单价是 5000（[PR #86 评论](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5102189761)），所以是单件；lovelyyoshino："一次性送多个盲盒时仅显示该盲盒一个的原价"（[API.live_websocket.md#L255](https://github.com/lovelyyoshino/Bilibili-Live-API/blob/b7766edfe486787f0167fd4bee80738bbf936419/API.live_websocket.md#L255)） |
| `gift_tip_price` | 爆出礼物的**单价**；样本里等于 `price` | [实测] 上面的样本；[源码] Ayabot 因为没有乘 num 出过 bug，修复脚本写道："gift_tip_price 是单个物品价格（金瓜子），×num 得总价"（[repair_blindbox_values.py#L1-L7, L38](https://github.com/yujianke100/Ayabot/blob/5cfff691a50a20937e0bcc023be1722e426efcd4/scripts/repair_blindbox_values.py#L1-L40)）；blivedm-go："RevealedTotalCoin is the revealed gift value; TotalCoin remains the paid cost"，按 `GiftTipPrice * num` 计算（[gift.go#L217-L231](https://github.com/Akegarasu/blivedm-go/blob/957b543d2bfa8c2ac8a7af2fb42ae73eb92443b2/message/gift.go#L217-L231)） |
| `gift_action` | 恒为 `"爆出"` | [实测] |
| `blind_gift_config_id`, `from` | 含义未记载 | — |

**结论**（单位都是金瓜子）：

- 实付 = `total_coin` = `original_gift_price × num`
- 爆出价值 = `price × num` = `gift_tip_price × num`
- 两者在一条消息里都能取到。

### 2.3 V2 中的盲盒字段

- protobuf 定义来自 blivedm 作者贴出的前端代码与 proto（[PR #86 评论](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5104705486)，包名 `bilibili.live.gift.v1`），blivedm 已按它实现（[pb.py#L52-L88](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/pb.py#L52-L88)）：
  - `SendGiftBroadcast`：`uid=1, uname=2, blind_gift=9, gift_list=10 (repeated)`
  - `BlindGift`：`blind_gift_config_id=1, original_gift_id=2, original_gift_name=3, from=4, gift_action=5, original_gift_price=6, gift_tip_price=7`
  - `GiftItem`：`gift_id=1, gift_name=2, num=3, price=5, discount_price=6, total_coin=7, coin_type=8, tid=9, timestamp=10, super_batch_gift_num=11, batch_combo_id=12, combo_total_coin=14, gift_tip_price=36`
- `blind_gift` 在**整条广播**上只有一份；爆出单价放在每个 `gift_list` 项的 `gift_tip_price` 里。前端代码会把它拷进 blindGift：`if (gift.gift_tip_price) { giftItem.blindGift = {...giftItem.blindGift, gift_tip_price: ...} }`（同一条评论）。
- **[冲突]** lovelyyoshino 的逆向文档写的是 `total_coin = 6`、`discount_price = 7`，`gift = 10` 也不是 repeated（[API.live_websocket.md#L250-L270](https://github.com/lovelyyoshino/Bilibili-Live-API/blob/b7766edfe486787f0167fd4bee80738bbf936419/API.live_websocket.md#L250-L270)）。该文件已在 2026-08-03 的提交 `d9fd5f30` 中从仓库删除。实测支持 blivedm 的版本：按 lovelyyoshino 字段号解出来的一项是 `"星光铃铛" num 8, price 5200, "total_coin" 5200, "discount_price" 40000`，而 40000 = 5000 × 8 正是实付，说明 6 和 7 两个字段号标反了（[评论](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5103541545)，[更正](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5105694004)）。同一 PR 早先的 `[v2] ... total_coin=1500` 日志也是在标反的字段号下得出的。

## 3. 单位

- **1 电池 = 100 金瓜子；1 元 = 1000 金瓜子；所以 1 电池 = 0.1 元。**
  - [文档] "金瓜子数量 / 100 = 电池数量"（[live_bill.md#L28](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/live_bill.md#L28)）
  - [文档] "该值/1000的单位为元"（[gift.md#L36](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/gift.md#L36)）
  - [文档] "20 电池 (2 CNY) 对应 2000 金瓜子"（[message_stream.md#L3266](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L3266)）
  - [文档] 官方开放平台："(1000 = 1元 = 10电池)"（[长链命令说明](https://open-live.bilibili.com/document/f9ce25be-312e-1f4a-85fd-fef21f1637f8)）
- **银瓜子**：`coin_type = "silver"`，此时 `price` 是银瓜子数（[live_bill.md#L28](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/live_bill.md#L28)）。2024 年的实测里，`人气票` 是 `silver`，price 和 total_coin 都为 0（Pcrab 日志）。
  - 没有任何来源出现过 silver 盲盒，所有盲盒样本都是 `gold`。BAC 写的是 `coin_type` "一般为gold，即电池"（[gift.md#L38](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/gift.md#L38)）。[推断] 盲盒只用电池购买。
- **盲盒价格与概率接口**：`GET https://api.live.bilibili.com/xlive/general-interface/v1/blindFirstWin/getInfo?gift_id=<盲盒id>`，返回 `blind_price`，以及各爆出礼物的 `price` 和 `chance`。例如心动盲盒 `blind_price: 15000`，即 150 电池（[gift.md#L54-L117](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/gift.md#L54-L117)）。

## 4. 批量与连击

**普通礼物批量**：一次送 10 个小花花，只来 1 条 `SEND_GIFT`，其中 `num=10, price=100, total_coin=1000`。[实测] Pcrab 日志。

**盲盒批量**：

- **V1**：每种爆出礼物各一条 `SEND_GIFT`，`num` 是该礼物的个数，`total_coin` = 盲盒单价 × 该条的 num。[实测] PR 作者"送了几个盒子"后收到三条：`好运柚叶 num=3 total_coin=15000`、`幸运泡泡 num=2 total_coin=10000`、`星光铃铛 num=5 total_coin=25000`（[评论](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5102189761)）。[推断] 三条的数量合计正好是 10，很可能来自同一次送 10 个；评论里没有明说。
- **V2**：只来一条 `SEND_GIFT_V2`，`gift_list` 中每种爆出礼物一项。[实测] 一次性送 10 个幸运盲盒，得到 3 项：1 + 8 + 1（[评论](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5103541545)）。前端代码也是 `for` 循环遍历 `gift_list`。
- 所以一行记录既不等于一个盒子，也不等于一次投喂。一次投喂的盒子数 = 各项 `num` 之和。

**连击**：

- `COMBO_SEND` 的字段：`combo_id, combo_num, batch_combo_id, batch_combo_num, total_num, combo_total_coin, gift_id, gift_name, uid`，其中 `gift_num` 恒为 0。示例：小花花连击 2 次，`combo_num 2, total_num 2, combo_total_coin 200`（[BAC#L1842-L1937](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1842-L1937)）。字段表里没有 `blind_gift`。
- 它是连击的**累计值**，和 `SEND_GIFT` 一起计入会重复计算。[推断] 依据是主流客户端只用 `SEND_GIFT` 和 `SEND_GIFT_V2` 生成礼物事件，对 `COMBO_SEND` 不做处理：blivedm 把它列入已知、不处理的 cmd（[handlers.py#L15-L16](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/handlers.py#L15-L16)）；弹幕姬对 `COMBO_SEND` 和 `COMBO_END` 都直接 `break`（[ParseMessageThread.java#L295-L303](https://github.com/BanqiJane/Bilibili_Danmuji/blob/d659cbc511f44f36ac54c2880f0d6687570ca1bf/src/main/java/xyz/acproject/danmuji/thread/core/ParseMessageThread.java#L295-L303)）。"连击的每次点击都会单独推一条 `SEND_GIFT`"这一点没有文档明说。
- `COMBO_END`：BAC 存档里没有。2020 年的样本里有 `combo_num, gift_num, price, start_time, end_time`（[yjqiang/bili-doc combo0.md](https://github.com/yjqiang/bili-doc/blob/8811f065e805ff9d47b70b53ce950b454236ebff/docs/bili_live_danmu/danmu/combo0.md)）。现在是否还推送未知。

## 5. 唯一标识与去重

- Web 协议里没有文档声明哪个字段唯一。官方开放平台的礼物消息有 `msg_id`"消息唯一id"，但那是另一套协议，Web 协议里没有对应字段。
- 各来源对 `tid` 的说法：
  - BAC："礼物发送时的时间戳，以及后面9位未知数字"，"似乎与rnd字段相同"，示例为 `"1673622464121900003"`（[#L1560](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1560)、[#L1704](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L1704)）
  - 2024 年实测：`tid` 等于 `rnd`，形如 `"4578879044749173248"`，不再以时间戳开头；7 条消息的 tid 各不相同（Pcrab 日志）
  - blivedm：tid "可能是事务ID，有时和rnd相同"；rnd "有时是时间戳+去重ID，有时是UUID"（[web.py#L288-L295](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/web.py#L288-L295)）
  - lovelyyoshino：tid 是"交易流水号（字符串大数）"
  - 类型：BAC 字段表写 num，但示例、实测和 V2 proto 里都是字符串（[pb.py#L71](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/pb.py#L71)）
- **盲盒批量时，多条消息（V1）或多项（V2）是否共用同一个 tid：没有真实数据。** blivedm-go 的十连测试数据里 3 项共用 `"test-ten-draw-transaction"`，但这是作者构造的合成数据（[ten_blind_gift_v2.json](https://github.com/Akegarasu/blivedm-go/blob/957b543d2bfa8c2ac8a7af2fb42ae73eb92443b2/message/testdata/ten_blind_gift_v2.json)），不能当证据。
- **重连重放**：认证包只有 `uid/roomid/protover/platform/type/key` 几个字段，没有偏移量或续传字段；包头的 sequence 只写"每次发包时向上递增"（[BAC#L154](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L154)、[#L180-L190](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L180-L190)）。[推断] 重连后服务端不会补发断线期间的消息，那段时间的礼物会**丢失**，而不是重复到达。
- 同一份礼物会不会被推两次（同一连接内，或 V1 和 V2 同时推）：没有来源记载。实测里，切到 V2 的房间只推 V2。

## 6. 发弹幕：msg/send

以下来自 [danmaku.md#L1701-L1776](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1701-L1776)。

- **请求**：`POST https://api.live.bilibili.com/msg/send`，正文用 `application/x-www-form-urlencoded` 或 `multipart/form-data`。
- **鉴权**：Cookie `SESSDATA`；"Cookie中`bili_jct`的值正确并与`csrf`相同"（[#L1707-L1709](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1707-L1709)）。URL 参数 `w_rid` / `wts`（wbi 签名）"不强制需要"。
- **正文参数**（[#L1720-L1737](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1720-L1737)）：
  - 必要：`csrf`、`roomid`、`msg`、`rnd`（当前 Unix 秒）、`fontsize`（默认 25）、`color`（十进制，"实际无效果"）
  - 非必要：`mode`（默认 1）、`bubble`（0）、`room_type`、`jumpfrom`、`reply_mid`、`reply_attr`、`reply_uname`、`replay_dmid`、`statistics`、`csrf_token`（同 csrf）
- **错误码**（[#L1745](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1745)）：`-101` 未登录，`-111` csrf 校验失败，`-400` 请求错误，`1003212` 超出限制长度，`10031` 发送频率过快。
- **[冲突] 10030 和 10031 的含义**：
  - lanyangyin/OBSscripts（150★）：`10031: "发送频率过快"`，`10030: "重复弹幕"`，`10032: "弹幕包含敏感词"`（[send_danmaku.py#L321-L324](https://github.com/lanyangyin/OBSscripts-bilibili-live/blob/68f0452d809556b5fd913e5616e59e2a5b2feafd/function/api/Special/Csrf/send_danmaku.py#L321-L324)）
  - vtb_toolkit（0★）的说法相反：`10030` 是限流，`10031` 是重复（[send.rs#L34-L36](https://github.com/DongSky/vtb_toolkit/blob/efb147a841d6ca3ba1f0db2ef6d82e2d78679085/crates/vtb-danmaku/src/send.rs#L34-L36)）
  - BAC 只记了 `10031` = 频率过快
- **code 0 也可能没发出去**：`message` 为 `"f"` 表示命中 B站屏蔽词，为 `"k"` 表示命中主播设置的屏蔽词（[send_danmaku.py#L277-L295](https://github.com/lanyangyin/OBSscripts-bilibili-live/blob/68f0452d809556b5fd913e5616e59e2a5b2feafd/function/api/Special/Csrf/send_danmaku.py#L277-L295)；vtb_toolkit 对 `"f"` 的说法相同）。BAC 没有记载。
- **长度上限**：BAC 没写具体字数。
  - 一份 `getInfoByUser` 的响应样本里有 `property.danmu.length: 20`（[champkeh/blive-ws getInfoByUser.md#L71-L76](https://github.com/champkeh/blive-ws/blob/b3b749865e8dc36971cd8adcfb303a21a130dea4/docs/apis/getInfoByUser.md#L71-L76)，接口为 `https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByUser?room_id=`）
  - lanyangyin 的注释："B站限制通常为20个字符"（[#L216](https://github.com/lanyangyin/OBSscripts-bilibili-live/blob/68f0452d809556b5fd913e5616e59e2a5b2feafd/function/api/Special/Csrf/send_danmaku.py#L216)）
  - [推断] 普通账号上限 20 字，以 `getInfoByUser` 实时返回的值为准
- **发送频率**：所有来源都没有给出具体间隔。

## 7. 回复弹幕（@回复）

回复弹幕就是在 `msg/send` 里多带几个 `reply_*` 参数。观众端在正文前显示"@昵称"，正文本身不含昵称。

本节另用到一份旁证：2019 年泄露的 B站后端直播弹幕服务代码 `live-dm`（GitHub 上有多份镜像，下文引用 [changwh/go-common](https://github.com/changwh/go-common/tree/beccf3fe4313be190d655c9f0620a6b4fac0b522/app/service/live/live-dm)）。它不是客户端，也不是官方公开资料，而且比回复功能早，标为 **[源码·泄露]**，只用来佐证客户端里观察到的行为。

### 7.1 参数含义

| 参数 | 含义与取值 | 来源 |
| --- | --- | --- |
| `reply_mid` | 被回复观众的 uid，取触发弹幕的 `info[2][0]`（即 `info[0][15].user.uid`）。`0` 表示不回复 | [文档] "要“@”的用户mid"，默认 `0`（[danmaku.md#L1732](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1732)）；[源码] PiliPlus 点"@TA"时取 `rawText: item.extra.mid.toString()`（[controller.dart#L769-L778](https://github.com/bggRGjQaUbCoE/PiliPlus/blob/d94ae8da10fea4579fc8a9fdf30f8572e4a55162/lib/pages/live_room/controller.dart#L769-L778)），发送时作为 `replyMid`（[view.dart#L173-L193](https://github.com/bggRGjQaUbCoE/PiliPlus/blob/d94ae8da10fea4579fc8a9fdf30f8572e4a55162/lib/pages/live_room/send_danmaku/view.dart#L173-L193)）；BilibiliDanmuRobot-Core 取 `uid := fmt.Sprintf("%.0f", from[0].(float64))`，`from` 是 `info[2]`（[danmu_bullet.go#L44-L65](https://github.com/xbclub/BilibiliDanmuRobot-Core/blob/bc8da47dbcf104b04100a3693307abad263b8ddb/logic/danmu/danmu_bullet.go#L44-L65)） |
| `reply_uname` | 被回复观众的昵称。**给了 `reply_mid` 就不用传** | [文档] "提供reply\_mid时不需要提供"（[#L1734](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1734)）；[源码] PiliPlus 回复时也传 `'reply_uname': ''`（[live.dart#L60-L65](https://github.com/bggRGjQaUbCoE/PiliPlus/blob/d94ae8da10fea4579fc8a9fdf30f8572e4a55162/lib/http/live.dart#L60-L65)），MagicalDanmaku 和 BilibiliDanmuRobot-Core 都不传。[推断] 服务端按 `reply_mid` 补全昵称和颜色，回显里的 `reply_uname` 有值（见 7.3） |
| `replay_dmid`（原文拼写） | 被回复那条弹幕的 id，取触发弹幕 `info[0][15].extra` 里的 `id_str` | [文档] "要回复的弹幕id"，默认 `""`（[#L1735](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1735)）；[源码] PiliPlus 解析时 `id: extra['id_str']`（[controller.dart#L599-L600](https://github.com/bggRGjQaUbCoE/PiliPlus/blob/d94ae8da10fea4579fc8a9fdf30f8572e4a55162/lib/pages/live_room/controller.dart#L599-L600)），回复时作为 `replayDmid`；BilibiliDanmuRobot-Core 用 `ReplyMsgId: tagExtra.IdStr`，非空才传（[bullet.go#L51-L55](https://github.com/xbclub/BilibiliDanmuRobot-Core/blob/bc8da47dbcf104b04100a3693307abad263b8ddb/http/bullet.go#L51-L55)） |
| `reply_attr` | 含义未知，所有来源都传 `0` | [文档] "(?)"，`0`（[#L1733](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1733)） |
| `reply_type` | BAC 没列。照抄网页端表单的客户端都带它，值为 `0`；PiliPlus 回复时也是 `0` | [源码] Bilibili-Evolved（[sender.ts#L70-L90](https://github.com/the1812/Bilibili-Evolved/blob/fa06dcec095dbccaba82f50ae2318c8d61c26671/registry/lib/components/live/live-danmaku-helper/sender.ts#L70-L90)）；PiliPlus（[live.dart#L64](https://github.com/bggRGjQaUbCoE/PiliPlus/blob/d94ae8da10fea4579fc8a9fdf30f8572e4a55162/lib/http/live.dart#L64)） |

- **哪些参数是必需的**：[推断] 只有 `reply_mid`。依据：MagicalDanmaku（1.2k★）只往表单里写 `reply_mid`（[bili_liveservice.cpp#L4857-L4872](https://github.com/iwxyi/MagicalDanmaku/blob/db912bc1518e82a8bf12276cce6963de16f9ba6d/services/live_services/bilibili/bili_liveservice.cpp#L4857-L4872)），不写 `replay_dmid` 和 `reply_uname`；它源码注释里有一条 @ 显示正常的真实回显（见 7.3）。但不能确定那条回显就是它自己只传 `reply_mid` 时发出的。
- **`replay_dmid` 的作用未知**：回显的 `extra` 里没有任何字段记录被回复弹幕的 id（见 7.3）。
- **[冲突] 拼写**：livehime-macos 的补丁注释说 Windows 版直播姬用 `reply_dmid`，所以它两个都传："LiveHime for Windows names the latter `reply_dmid`, so both are sent"（[0041 patch#L292-L303](https://github.com/greyoak111/livehime-macos/blob/1f3aee1b70113596366479256df7df7b2dda2ad1/obs-fork/patches/0041-Reply-to-viewers-from-the-danmaku-feed-keep-the-floa.patch#L292-L303)）。该仓库 6★，也没有给出抓包。BAC、Bilibili-Evolved、PiliPlus 用的都是 `replay_dmid`。

### 7.2 观众看到什么；长度怎么算

- **"@昵称"由客户端根据回显字段渲染，正文 `msg` 里不放昵称。**
  - [源码] PiliPlus 发送时把 @ 片段从正文中剔除：`if (e.type == .at) { replyMid = int.parse(e.rawText); replyDmid = e.id!; } else { buffer.write(e.rawText); }`（[view.dart#L176-L186](https://github.com/bggRGjQaUbCoE/PiliPlus/blob/d94ae8da10fea4579fc8a9fdf30f8572e4a55162/lib/pages/live_room/send_danmaku/view.dart#L176-L186)）；显示时在正文前拼上 `'@${reply.name} '`（[chat_panel.dart#L123-L133](https://github.com/bggRGjQaUbCoE/PiliPlus/blob/d94ae8da10fea4579fc8a9fdf30f8572e4a55162/lib/pages/live_room/widgets/chat_panel.dart#L123-L133)）。
  - [源码] MagicalDanmaku 发送时用 `msg.replace(match.captured(0), "")` 删掉正文里的 "@uid" 或 "@昵称 "（[bili_liveservice.cpp#L4741-L4776](https://github.com/iwxyi/MagicalDanmaku/blob/db912bc1518e82a8bf12276cce6963de16f9ba6d/services/live_services/bilibili/bili_liveservice.cpp#L4741-L4776)）；显示时 `QString at = "@" + danmaku.getReplyUname();` 拼在正文前（[livedanmakuwindow.cpp#L826-L831](https://github.com/iwxyi/MagicalDanmaku/blob/db912bc1518e82a8bf12276cce6963de16f9ba6d/mainwindow/live_danmaku/livedanmakuwindow.cpp#L826-L831)）。
  - [实测] MagicalDanmaku 注释里的真实回显：`"content": " 居然还能@人了"`，`"reply_uname": "懒一夕智能科技官方"`。正文里没有被 @ 者的昵称。[推断] 开头的空格是网页输入框里"@昵称 "后面剩下的。
  - B站官方 Web 和 App 怎么显示，没有官方文档或截图。[推断] 也是"@昵称 正文"。依据：上面几个客户端都按同一组字段渲染；PiliPlus 的表单带 `statistics {"appId":100,"platform":5}`，是照搬网页端的。**未验证**：画面上滚动的弹幕（不是聊天列表）会不会显示 @。
- **长度**：[推断] @ 部分不计入字数，20 字全部留给正文。依据：昵称不在 `msg` 里；[源码·泄露] 服务端长度检查只数正文：`ml := len([]rune(sdm.SendMsgReq.Msg))`（[dmcheck.go#L200-L216](https://github.com/changwh/go-common/blob/beccf3fe4313be190d655c9f0620a6b4fac0b522/app/service/live/live-dm/service/v1/dmcheck.go#L200-L216)）。这份代码早于回复功能，也没有实测记录，**未验证**。

### 7.3 DANMU_MSG 里的回复弹幕

- **发送者 uid**：`info[2][0]`，"同 `info[0][15].user.uid`"（[文档] [message_stream.md#L506](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L506)、[#L471](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L471)；[源码] blivedm `uid=info[2][0]`，[web.py#L167](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/web.py#L167)）。连接未登录时为 0（第 1 节），这时没法回复。
- **弹幕自己的 id**：`info[0][15].extra`（一个 JSON 字符串）里的 `id_str`，形如 `"364b06e3c561af3d5921f1253d66c1d575"`（[文档] [#L445](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L445)；历史弹幕接口也有 `id_str`"弹幕ID?"，[danmaku.md#L377](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L377)）。根对象上的 `msg_id` 标注为"极低概率存在"（[#L342](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L342)），不能依赖。
- **回复信息**也在 `extra` 里：`show_reply`、`reply_mid`、`reply_uname`、`reply_uname_color`、`reply_is_mystery`、`reply_type_enum`。BAC 的字段列表没有 `reply_type_enum`（[#L452-L456](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L452-L456)），blivedm 的样本里有（[web.py#L244-L245](https://github.com/xfgryujk/blivedm/blob/3bf17fe862b8c2fc6bc04c81c3f0c5db95de93d6/blivedm/models/web.py#L244-L245)）。
  - 回复弹幕 [实测]（[bili_livecmds.cpp#L851-L877](https://github.com/iwxyi/MagicalDanmaku/blob/db912bc1518e82a8bf12276cce6963de16f9ba6d/services/live_services/bilibili/bili_livecmds.cpp#L851-L877)）：`"id_str": "5b07eaab49ab75ed743f954e8566f3b694"`, `"reply_mid": 20285041`, `"reply_uname": "懒一夕智能科技官方"`, `"reply_uname_color": "#FB7299"`, `"reply_type_enum": 1`
  - 普通弹幕：`reply_mid 0`、`reply_uname ""`、`reply_type_enum 0`（[danmaku.md#L1787](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1787)）
  - 各客户端都用 `reply_mid > 0`（或 `!= 0`）判断一条弹幕是不是回复，例如 BilibiliDanmuRobot-Core 的 `if tagExtra.ReplyMid > 0 { danmumsg = fmt.Sprintf("@%s %s", tagExtra.ReplyUname, danmumsg) }`（[danmu_bullet.go#L105-L107](https://github.com/xbclub/BilibiliDanmuRobot-Core/blob/bc8da47dbcf104b04100a3693307abad263b8ddb/logic/danmu/danmu_bullet.go#L105-L107)）
- `extra` 里**没有**字段记录"被回复弹幕的 id"。
- **`msg/send` 的成功响应**里，`data.mode_info` "基本上与…`info[0][15]`对象相同"（[danmaku.md#L1758-L1760](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/danmaku.md#L1758-L1760)）。它的 `extra` 带有 bot 自己这条弹幕的 `id_str` 和 `reply_*`。[推断] 可以用它确认服务端是否接受了回复，即 `reply_mid` 非 0、`reply_uname` 已补全。
- bot 发的回复也会作为 `DANMU_MSG` 推回来，`info[2][0]` 是 bot 自己的 uid。BilibiliDanmuRobot-Core 用 `uid != svcCtx.RobotID` 跳过自己的弹幕（[danmu_bullet.go#L76](https://github.com/xbclub/BilibiliDanmuRobot-Core/blob/bc8da47dbcf104b04100a3693307abad263b8ddb/logic/danmu/danmu_bullet.go#L76)）。
- **类似项目**：BilibiliDanmuRobot-Core 也有盲盒统计。观众发"N月盲盒"，它就以回复弹幕的形式发出"%s月共开%d个, 赚了＋%.2f元"，正文不带昵称（[blindBoxStat.go#L44-L117](https://github.com/xbclub/BilibiliDanmuRobot-Core/blob/bc8da47dbcf104b04100a3693307abad263b8ddb/logic/danmu/blindBoxStat.go#L44-L117)）。

### 7.4 重复内容

- [源码·泄露] `//LimitSameMsg 同一个用户同一房间5s 只能发送一条相同弹幕`。键是 `房间号 + md5(uid + msg)`，命中时返回 `ecode.Error(0, "msg repeat")`，即 **code 0** 加 message `"msg repeat"`（[ratelimit.go#L46-L60](https://github.com/changwh/go-common/blob/beccf3fe4313be190d655c9f0620a6b4fac0b522/app/service/live/live-dm/dao/ratelimit.go#L46-L60)）。键里没有任何回复字段。[推断] 如果现在还是这个逻辑，5 秒内用同一段文字 @ 不同观众也算重复。
- [源码] 客户端的处理与此一致：
  - 弹幕姬在 `code == 0` 时检查 `message` 是否为 `"msg in 1s"` 或 `"msg repeat"`，是就重新排队（[HttpUserData.java#L575-L584](https://github.com/BanqiJane/Bilibili_Danmuji/blob/d659cbc511f44f36ac54c2880f0d6687570ca1bf/src/main/java/xyz/acproject/danmuji/http/HttpUserData.java#L575-L584)）
  - MagicalDanmaku 遇到 `"msg repeat"` 或 `"频率过快"`，4.2 秒后重试（[bili_liveservice.cpp#L4900-L4911](https://github.com/iwxyi/MagicalDanmaku/blob/db912bc1518e82a8bf12276cce6963de16f9ba6d/services/live_services/bilibili/bili_liveservice.cpp#L4900-L4911)）
  - chatterbox："re-sending the exact same text would be dropped by B站's duplicate filter"（[normal-send-tab.tsx#L105-L107](https://github.com/laplace-live/chatterbox/blob/d85976cbcac5b410d05f734c71bff3188ec830a5/src/components/normal-send-tab.tsx#L105-L107)）。它的做法是插入一个不可见字符（默认软连字符 U+00AD），并注明"B站 may filter any one of them"（[const.ts#L50-L52](https://github.com/laplace-live/chatterbox/blob/d85976cbcac5b410d05f734c71bff3188ec830a5/src/lib/const.ts#L50-L52)）
- **[冲突] 重复时返回什么**：
  - lanyangyin 写的是 `10030: "重复弹幕"`，但它会用自己的文字覆盖服务端返回的 `message`（[send_danmaku.py#L317-L329](https://github.com/lanyangyin/OBSscripts-bilibili-live/blob/68f0452d809556b5fd913e5616e59e2a5b2feafd/function/api/Special/Csrf/send_danmaku.py#L317-L329)），看不出服务端原文
  - vtb_toolkit 写的是 10031（第 6 节）
  - 泄露代码和弹幕姬：code 0 加 `"msg repeat"`
  - B站可能改过返回值。不管是哪种，都不是静默成功：不是 code 非 0，就是 code 0 加非空 `message`。

### 7.5 频率、禁言、长度上限

- **频率**：
  - [源码·泄露] 同一 uid 每秒 1 条，超出时返回 `ecode.Error(0, "msg in 1s")`（[ratelimit.go#L30-L44](https://github.com/changwh/go-common/blob/beccf3fe4313be190d655c9f0620a6b4fac0b522/app/service/live/live-dm/dao/ratelimit.go#L30-L44)）；整个房间每秒也有总条数上限，超出返回 `"max limit"`（[#L62-L111](https://github.com/changwh/go-common/blob/beccf3fe4313be190d655c9f0620a6b4fac0b522/app/service/live/live-dm/dao/ratelimit.go#L62-L111)）
  - [实测] 2018 年有人截图报告收到 `msg in 1s`（[YjMonitor issue #5](https://github.com/yjqiang/YjMonitor/issues/5)）
  - [文档] BAC 只记了 `10031` "发送频率过快"
  - [源码] 各客户端的发送间隔：弹幕姬 `Thread.sleep(1455)`（[SendBarrageThread.java#L53](https://github.com/BanqiJane/Bilibili_Danmuji/blob/d659cbc511f44f36ac54c2880f0d6687570ca1bf/src/main/java/xyz/acproject/danmuji/thread/SendBarrageThread.java#L53)、[#L77](https://github.com/BanqiJane/Bilibili_Danmuji/blob/d659cbc511f44f36ac54c2880f0d6687570ca1bf/src/main/java/xyz/acproject/danmuji/thread/SendBarrageThread.java#L77)）；MagicalDanmaku `#define AUTO_MSG_CD 1500`（[coderunner.h#L22](https://github.com/iwxyi/MagicalDanmaku/blob/db912bc1518e82a8bf12276cce6963de16f9ba6d/services/code_runner/coderunner.h#L22)）；BilibiliDanmuRobot-Core `time.Sleep(1 * time.Second) // 防止弹幕发送过快`（[send_bullet.go#L63](https://github.com/xbclub/BilibiliDanmuRobot-Core/blob/bc8da47dbcf104b04100a3693307abad263b8ddb/logic/send_bullet.go#L63)）
  - [推断] 每两条之间隔 1.5 秒左右，可以避开每秒 1 条的限制。没有来源提到更长时间窗口的上限，比如每分钟多少条。
- **禁言**：
  - 没有任何来源记载"发得太快会被自动禁言"。**未知**。
  - 房间级禁言：`ROOM_SILENT_ON` 的 `type` 可以是 `level`、`medal` 或 `member`（[文档] [message_stream.md#L5038-L5075](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L5038-L5075)）；`ROOM_BLOCK_MSG` 是"指定观众禁言"（[#L5112](https://github.com/pskdje/bilibili-API-collect/blob/cfc5fddcc8a94b74d91970bb5b4eaeb349addc47/docs/live/message_stream.md#L5112)）。
  - [源码·泄露] 主播本人和房管不受房间禁言限制：`if sdm.SendMsgReq.Uid == sdm.RoomConf.UID || sdm.UserInfo.RoomAdmin { return nil }`（[dmcheck.go#L219-L224](https://github.com/changwh/go-common/blob/beccf3fe4313be190d655c9f0620a6b4fac0b522/app/service/live/live-dm/service/v1/dmcheck.go#L219-L224)）。被主播禁言时返回 `ecode.Error(1003, "你在本房间被禁言至 …")`（[#L331-L362](https://github.com/changwh/go-common/blob/beccf3fe4313be190d655c9f0620a6b4fac0b522/app/service/live/live-dm/service/v1/dmcheck.go#L331-L362)）。在全站黑名单里时返回 code 0 加 `"你被禁言啦"`（[#L85-L131](https://github.com/changwh/go-common/blob/beccf3fe4313be190d655c9f0620a6b4fac0b522/app/service/live/live-dm/service/v1/dmcheck.go#L85-L131)）。[推断] 把 bot 账号设为房管，开了等级禁言或全员禁言也能照常回复。这是 2019 年的逻辑。
- **长度上限**：
  - [源码] MagicalDanmaku 原先按身份推算：`// UL等级：20级30字`，`// 大航海：舰长20，提督/总督40`，默认 20。这段代码现已注释掉（[bili_liveservice.cpp#L4682-L4697](https://github.com/iwxyi/MagicalDanmaku/blob/db912bc1518e82a8bf12276cce6963de16f9ba6d/services/live_services/bilibili/bili_liveservice.cpp#L4682-L4697)），改为读 `getInfoByUser` 的 `property.danmu.length`（[#L926-L942](https://github.com/iwxyi/MagicalDanmaku/blob/db912bc1518e82a8bf12276cce6963de16f9ba6d/services/live_services/bilibili/bili_liveservice.cpp#L926-L942)）
  - [源码] BilibiliDanmuRobot-Core 默认 `DanmuLen … default=20`（[config.go#L16](https://github.com/xbclub/BilibiliDanmuRobot-Core/blob/bc8da47dbcf104b04100a3693307abad263b8ddb/config/config.go#L16)），超长时按 rune 切成多条（[send_bullet.go#L40-L55](https://github.com/xbclub/BilibiliDanmuRobot-Core/blob/bc8da47dbcf104b04100a3693307abad263b8ddb/logic/send_bullet.go#L40-L55)）
  - [源码·泄露] 按 rune 计数，上限取自每个用户的弹幕配置 `sdm.DMconf.Length`，超出时返回 `-500 "超出限制长度"`（[dmcheck.go#L200-L216](https://github.com/changwh/go-common/blob/beccf3fe4313be190d655c9f0620a6b4fac0b522/app/service/live/live-dm/service/v1/dmcheck.go#L200-L216)）。现在的错误码是 `1003212`（第 6 节）
  - 20/30/40 只出现在一个客户端已注释掉的代码里，没有官方出处。以 `getInfoByUser` 实时返回的值为准。

### 7.6 小结（给"今日盲盒"回复用）

- 参数：`reply_mid` = 触发弹幕的 `info[2][0]`；`replay_dmid` = 触发弹幕 `extra.id_str`（[推断] 非必需，但网页端和 PiliPlus 都会带）；`reply_uname` 传空；`reply_attr` 和 `reply_type` 传 `0`。
- 正文不写昵称，20 字（以 `getInfoByUser` 为准）全部给正文。
- 判断成功：要 `code == 0` 且 `message` 为空。`"msg in 1s"`、`"msg repeat"`、`"f"`、`"k"` 等非空 message 都表示没发出去。
- 同一 bot 账号 5 秒内不要发两条相同正文，即使 @ 的人不同。经发送队列控制，每条间隔不少于 1.5 秒。

## 对设计的影响

1. 必须同时处理 `SEND_GIFT`（JSON）和 `SEND_GIFT_V2`（`data.pb` 是 base64 编码的 protobuf）。一条 V2 消息要展开成多条礼物项。房间会在两种协议间切换，所以不能只实现其中一种。
2. 一行记录 = 一个礼物项：V1 是一条消息，V2 是 `gift_list` 中的一项。它不等于一个盒子，也不等于一次投喂。建议的列：
   - 观众：`uid`、`uname`
   - 时间与标识：`timestamp`（秒）、`tid`（TEXT）、`rnd`（TEXT）
   - 盲盒：`blind_box_gift_id`（`original_gift_id`）、`blind_box_name`、`blind_box_unit_price`（`original_gift_price`）
   - 爆出礼物：`opened_gift_id`（`giftId`）、`opened_gift_name`、`opened_unit_price`（`price`）
   - 数量与金额：`num`、`paid_total`（`total_coin`）
   - 原始消息（便于以后重算）
3. 金额一律用整数**金瓜子**存储，展示时再除以 100 换成电池。盈亏 = `price × num − total_coin`。入库时校验 `total_coin == original_gift_price × num`，不等就记日志：未解决第 4 项可能导致两者不一致。
4. 只有 `blind_gift` 非空且 `coin_type == "gold"` 的礼物才入库。V2 中通过 `blind_gift.original_gift_id != 0` 判断是否有盲盒信息。
5. 不要计入 `COMBO_SEND` 和 `COMBO_END`，也不要用 `combo_total_coin`，否则会重复计算，或者把爆出价值当成实付。
6. 去重不能只用 `tid` 做唯一键：批量盲盒的多项可能共用同一个 tid。建议用 `UNIQUE(tid, opened_gift_id)`。[推断] 同一批次里每种爆出礼物只占一项，同种礼物的个数已经合并在 `num` 里，所以这个组合足以区分各项。
7. 必须以登录身份连接，否则 `uid = 0`，无法按观众统计。`uid = 0` 的事件不计入任何观众。
8. 断线期间的礼物会丢。统计结果只代表"bot 在线期间看到的"，回复文案不要声称精确。
9. 回复弹幕要控制在 20 字以内。要处理 `1003212`、`10031`（以及可能的 `10030`），还要处理 code 0 但 message 为 `"f"` 或 `"k"` 的情况。发送间隔需要实测后再定，经过已有的发送队列控制。

## 未解决（需要在真实直播间抓包确认）

1. V1 一次送 N 个盲盒时，多条 `SEND_GIFT` 是否共用 `tid`、`rnd` 和 `batch_combo_id`；V2 `gift_list` 各项的 `tid` 是否相同。
2. 同一批次爆出同一种礼物时，是否总是合并成一条 `num > 1` 的记录。
3. 盲盒连击（多次点击）时，每次点击是否各推一组 `SEND_GIFT`；盲盒是否会推 `COMBO_SEND`，内容是什么。
4. `total_coin` 是否可能不等于 `original_gift_price × num`，例如打折、首抽福利（`is_first`）、从包裹里送出的盲盒（V2 有 `bag_gift` 字段）。从包裹送出时，"实付"应该算多少。
5. 同一礼物是否会被推两次（同一连接内，或 V1 与 V2 同时推）；`switch = false` 的消息是否应该计入。
6. 盲盒场景下 `discount_price` 的确切含义。目前只有一份实测，值等于爆出单价。
7. 是否存在 silver 盲盒。
8. `COMBO_END` 现在是否还推送。
9. `msg/send` 的安全发送间隔；`10030` 和 `10031` 的确切含义；不同用户等级或大航海身份的长度上限。
10. V2 的灰度规则；`data.pb` 和 `data.data.pb` 两种路径是否都存在。lovelyyoshino 文档写的是 `data.data.pb`，blivedm PR 作者实测只见过 `data.pb`（[评论](https://github.com/xfgryujk/blivedm/pull/86#issuecomment-5103018495)）。
11. 回复弹幕只传 `reply_mid`、不传 `replay_dmid` 时，官方 Web 和 App 是否照样显示 @。`replay_dmid` 有什么作用，`reply_dmid` 这种拼写是否也有效。`reply_attr`、`reply_type` 和回显里 `reply_type_enum` 的含义。
12. 官方 Web 和 App 的聊天列表、画面上的滚动弹幕是否都显示"@昵称"；@ 部分是否计入长度上限。
13. 现在重复弹幕返回的是 `10030` 还是 code 0 加 `"msg repeat"`；去重窗口是否仍是 5 秒；同一段文字 @ 不同观众是否算重复。
14. 每秒 1 条之外，账号是否还有更长时间窗口的发送上限；发得太频繁会不会触发自动禁言或风控，返回什么。
15. 被回复的观众开了神秘人（`reply_is_mystery`）时能否回复，显示成什么。
