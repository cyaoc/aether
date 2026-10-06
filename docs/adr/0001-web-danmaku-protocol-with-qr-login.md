# 走 Web 弹幕协议加扫码登录，不用直播开放平台，协议自己实现

bot 要能连接任意直播间，以后还要以账号身份发弹幕。直播开放平台的身份码只有主播本人能生成，开放平台也没有发弹幕的接口，满足不了这两点。所以我们走非官方的 Web 弹幕 WebSocket，账号通过扫码登录拿到 cookie。不登录的话，观众昵称会被打码，uid 也是 0。

目前找不到还在维护的 C# 库，这套协议由 Core 自己实现。需要的能力 BCL 都有：WebSocket、zlib/brotli 解压、wbi 签名用的 MD5。实现时参考 BililiveRecorder（C#）和 blivedm（Python）。

## Consequences

- 协议随时可能被 B站改掉。2025 年就新增过 wbi 签名（5 月）和 buvid3（6 月）的要求，以后改了要跟着修。
- 协议资料先查 pskdje/bilibili-API-collect 的 master 分支（README 链接的就是它）。它是原仓库 SocialSisterYi/bilibili-API-collect 收到 B站律师函、于 2026-01 关闭前的存档，只读，内容停在 2026-01-25；之后的协议变化，靠读开源实现、自己抓包来跟进。
- 发弹幕有风控：发得太快会被禁言（错误码 10031），所以需要发送队列控制节奏。
