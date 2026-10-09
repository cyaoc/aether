# Aether

CLI 支持 macOS / Windows / Linux，GUI 支持 macOS / Windows 的 B站直播 bot。
支持扫码登录、退出登录和以登录账号身份观看直播间弹幕。

需要 .NET 10 SDK：

```sh
dotnet build
dotnet test
dotnet run --project src/Aether.Cli -- login
dotnet run --project src/Aether.Cli -- logout
dotnet run --project src/Aether.Cli -- watch 1768506153
dotnet run --project src/Aether.Gui
```

发布 CLI 后，可执行文件名为 `aether`：

```sh
dotnet publish src/Aether.Cli -c Release -o artifacts/cli
./artifacts/cli/aether watch 1768506153
```

发布 Linux x64 的独立 CLI（服务器无需安装 .NET SDK 或运行时）：

```sh
dotnet publish src/Aether.Cli -c Release -r linux-x64 --self-contained true -o artifacts/linux-x64
```

把 `artifacts/linux-x64/` 整个目录复制到 Linux x64 服务器上可写的目录，例如 `~/aether/`，
然后在服务器终端运行（首次需要用手机扫描终端里的二维码）：

```sh
chmod +x ~/aether/aether
~/aether/aether watch 1768506153
```

数据保存在程序旁边的 `~/aether/data/`，与终端当前目录无关，遵循 ADR 0002。
迁移已有登录凭据时，一同复制原来的 `data/`；它包含明文凭据，应仅允许自己的账号访问。

CLI 和 GUI 共用 bot 设置文件 `data/aether.yml`（数据目录位置见下文 ADR 0002）。首次运行时自动生成带中文注释的模板，默认值是生效的行：

```yaml
log:
  retention_days: 30
  level: Information
blind_box:
  enabled: true
  keyword: 今日盲盒
send:
  interval_seconds: 5
```

`log.retention_days` 是日志保留天数，必须为正整数，默认 30；`log.level` 是文件日志最低级别，
可选 Trace、Debug、Information、Warning、Error、Critical，默认 Information。日志设置在进程启动时读取，修改后重启生效。
`blind_box.enabled` 只接受 `true` 或 `false`，默认 `true`，下次观看直播间时生效，自动重连不重新读取。
启用时把 V1 `SEND_GIFT` 的 gold 盲盒记入 `data/aether.db`，保存原始消息；金额为整数金瓜子，
投入按 `total_coin`、开出价值按 `price × num`。相同 `tid` 只记录一次：内容也相同时视为重复推送，警告里带上被忽略的开出礼物、个数和投入；内容不同时警告 `tid` 可能不是按礼物项唯一，金额可从警告补回。数字形式的 `tid` 按其数字文本保存。以下情况都未实测确认，所以在默认日志级别下可见：uid 为 0 的盲盒（可能是神秘人）不记录，记一条 Information；带 `blind_gift` 但不是 gold 的礼物不记录并警告；`switch` 为 false 的盲盒照常记录并警告。
当前不记录 `SEND_GIFT_V2`，也不累计 `COMBO_SEND` / `COMBO_END`。每条记录的观众、礼物和金额可在 Information 日志中查看。

启用盲盒统计时，观众发送整条关键字（默认“今日盲盒”，忽略弹幕首尾空白），bot 会 @回复这位观众在本直播间、
收到关键字时的北京时间自然日内的投入与盈亏。只统计连接期间记录的盲盒，日期按固定 UTC+8 和礼物送出时间计算。
回复为“投入X电池 赚Y电池”“投入X电池 亏Y电池”“投入X电池 不赚不亏”或“今日没有盲盒记录”，
1 电池 = 100 金瓜子，非整数保留一位小数。账号自己发关键字也会触发；uid 为 0 时跳过并写 Debug 日志。
`blind_box.keyword` 必须为非空的一行文字；`send.interval_seconds` 必须为正整数，默认 5 秒。
关键字按设置原文匹配，请勿在关键字设置值的首尾添加空白。
两项均在下次观看直播间时生效。回复在发送时才查询金额，所以排队期间收到的同一天盲盒也会计入。
发送队列不阻塞接收和心跳，自动重连时保留待发弹幕，直播间连接结束时清空并记录丢弃条数。
成功写 Information；接口拒绝、非空 message（即使 code 为 0）或网络失败写 Warning 并丢弃，不重试。

缺少的项使用默认值；未知键会列在警告中，仍照常运行。设置格式遵循 [ADR 0004](docs/adr/0004-settings-file-line-editing.md)：
英文 snake_case 键，分组下每个设置占一行。有效的多行标量仍可读取，但多行写法、嵌套结构和行内分组标为不能逐行编辑；
日志两项只接受整数或级别名，盲盒开关只接受布尔值，不接受嵌套对象。
已知分组和设置名不能重复，即使键使用了不同的 YAML 标签；错误会定位到重复项，保存时文件保持不变。

GUI 主窗口点“设置”打开独立窗口，可修改日志保留天数、级别、盲盒统计开关、查询关键字和发送间隔，点“保存”写入 `aether.yml`，按各项提示的时机生效。
保存只修改改过的值；手动添加的注释、行尾注释、未知键和空行会原样保留。缺少的设置追加到分组末尾，
缺少的分组追加到文档末尾。输入不合法时立即显示 Core 的提示并禁用保存；文件有错时显示路径和行号，修好后重新打开设置窗口。
多行等不能逐行编辑的项显示为只读，可点“打开设置文件”用系统默认程序手改。
文件不是 UTF-8（或带 BOM 的 UTF-16/32）编码时仍能读取，但所有项都只读，另存为 UTF-8 后即可在 GUI 里修改。
“只保留最近 1000 条”仍在主窗口，仍单独保存在 `gui.yml`。

每次开始新的直播间连接前，Core 重新读取并校验设置。有错就返回文件路径、行号和设置名，不发起网络请求、不重试；
同一个直播间连接中的重连不重新读取。YAML 语法或整体结构错误标为 `YAML`，文件读写错误标为 `文件`（第 1 行）。
启动时文件有错，日志仍按默认值接入，并在日志中记下错误。CLI 的 `login` 和 `logout` 在 stderr 输出一行警告后照常运行，
`watch` 输出错误并以非零退出码结束；GUI 照常启动，点“连接”时显示拒绝原因。修好文件后可以再次连接。`data/gui.yml` 仍只保存 GUI 显示设置，行为不变。

CLI 和 GUI 的文件日志位于 `data/logs/`，默认记录 Information 及以上级别，由 `log.level` 控制。
公共日志写到 `aether-yyyyMMdd.log`，允许多个进程共享写入；解析到真实房间号后，
包括扫码和重连在内的 Core 日志写到 `<真实房间号>/aether-yyyyMMdd.log`。
直播间连接因错误结束时，该直播间的日志记下错误；CLI 或 GUI 另在公共日志记一条，房间号确定前的失败只记在公共日志。
文件按本地日期每天滚动，默认保留 30 天，由 `log.retention_days` 控制：启动时清理所有目录的过期文件，打开或滚动某组日志时再清理该组；
空直播间目录保留，避免与其他进程创建日志文件发生竞态而丢失日志。
每条写完即关闭文件，切换直播间不会积累句柄，其他进程可在断开后接管同一直播间。
每条包含时间、级别、进程号、类别和消息，异常附完整堆栈。日志同步写入，不在内存中缓冲。
进程未处理异常、未观察到的 Task 异常和 GUI UI 线程未处理异常立即记录，不标记为已处理，
仍遵循运行时原本的退出行为（未观察到的 Task 异常默认不终止 .NET 进程）。
跳过无法解析的直播间消息时，警告附原始 JSON；超过 2KB 按 UTF-8 字节截断并注明。
`log.level` 设为 `Trace` 时，每条直播间消息另记一条完整的原始 JSON，不截断，含观众的 uid、昵称等个人信息，
只在抓包时临时开启，步骤见 [测试数据说明](tests/Aether.Core.Tests/Fixtures/README.md)。
每个直播间连接第一次收到礼物消息时记一条 Information，写明是 V1（`SEND_GIFT`）还是 V2（`SEND_GIFT_V2`），版本切换时再记一条；
收到不认识、以 `DANMU_MSG` 或 `SEND_GIFT` 开头的 cmd 时记一条警告，提示消息格式可能变了，同一直播间连接中每个 cmd 只记一次。
自动重连不重新提示。

弹幕以 UTF-8 写到 stdout：`[HH:mm:ss] 昵称: 内容`；状态和日志写到 stderr。
Ctrl+C 关闭连接并正常退出，错误返回非零退出码。每次 `watch` 开始直播间连接时，
先解析短号或真实房间号，成功后只使用这个真实房间号，重连不再请求 room_init。
解析时的临时网络失败按下面的重连节奏重试；不存在的直播间立即报错，不扫码、不连接、不重试。
随后获取直播间锁，再用保存的 cookie 调 nav 检查登录状态；没有凭据或已失效时，在 stderr 显示二维码，
扫码成功后保存凭据并继续连接，无需先运行 `login`。二维码过期自动换新，扫码期间也可按
Ctrl+C 取消。连接使用 nav 返回的 mid 和凭据中的 buvid3，HTTP 请求携带保存的 cookie，
以接收观众的真实昵称。网络失败、超时、B站 服务器 5xx 或弹幕服务器断开时，`watch`
提示“重连中”，按 1、2、4、8、16、30、30…… 秒自动重试；收到认证成功回复后重置为 1 秒。
60 秒没有收到任何数据也会重连；心跳回复正常的安静直播间不会断开。
每次重连重新获取 wbi key 和弹幕 token，中途凭据失效或被 `logout` 删除时暂停接收弹幕，
显示二维码重新登录，成功后继续连接同一个直播间。等待扫码时也会检查本地数据库，
可在另一个终端运行 `login` 或在 GUI 登录，经 nav 确认有效后恢复连接；新凭据未生效时记一条警告并重新显示二维码。扫码阶段的网络失败仍每 2 秒原地重试，
不提示“重连中”，也不作废已显示的二维码。B站 明确拒绝（错误码含房间号不存在、HTTP 4xx、
弹幕认证失败）、无法解析的 HTTP 或认证响应、损坏的弹幕帧、本地错误（如数据库）和程序缺陷，
重试也不会好转，反复请求还会加重风控，所以直接报错退出。单条直播间消息不是合法 JSON、
缺少有效 cmd，或 DANMU_MSG 取不到昵称或内容时，只跳过该条并记一条警告，能读取 cmd 时一同记录；
后续弹幕继续接收。包头无效、解压失败和协议版本不支持仍直接报错退出。重连等待期间 Ctrl+C 也会立即结束。

同一个 `data/` 中，同一个真实房间号只允许一个直播间连接（短号和真实房间号也会冲突）。
Core 独占打开 `data/room-<真实房间号>.lock`，在扫码和断线重连期间持续持有；
第二个 CLI 或 GUI 会在检查登录、显示二维码之前报“直播间 … 已有直播间连接”，不会重试。
CLI 输出“错误：……”并以非零退出码结束；GUI 显示连接错误，状态回到“未连接”。
取消、出错或释放更新流都会释放锁并删除锁文件；进程崩溃时由操作系统释放锁，
留下的锁文件在下次连接同一直播间时复用。不要在连接运行期间删除它。同一目录可同时连接不同直播间；不同数据目录互不影响。
macOS / Linux 上的锁是 flock，`data/` 应放在本地磁盘上，部分网络文件系统不支持。
`login` 和 `logout` 不获取直播间锁，观看期间可在另一个终端执行。

连接、重连前和连接期间，距上次检查满 24 小时才检查是否需要刷新登录凭据；
只在 B站 返回 `refresh=true` 时刷新。新凭据保存后才确认作废旧凭据，确认失败只记警告。
已发出的刷新请求不随直播间连接结束而取消，连接等它保存完再重连。
检查或刷新遇到临时网络错误时保留原检查时间，连接照常，连接期间 30 秒后再试；
`cookie/info` 返回错误码、刷新页面结构变化等其他失败记一条警告，24 小时后再查，连接照常，
凭据是否有效仍由每次连接前的 nav 判断。B站 拒绝刷新请求时删除仍匹配旧 refresh_token 的凭据，
结束当前连接，转入重新登录。连接期间凭据被另一个进程删除（如 `logout`）时停止检查，当前连接照常，
下次重连时要求扫码。刷新结果不会覆盖另一个进程刚保存的凭据，也不会把已经退出的登录写回来。

`login` 先打开数据库、获取 buvid3，再在 stderr 显示字符画二维码，用 B站 App 扫码并确认。
每两秒轮询一次，过期后自动打印新二维码，成功保存凭据后提示“已登录”。网络错误（连不上、
HTTP 非 2xx、超时）记一条警告，两秒后重试，直到成功或 Ctrl+C；B站返回的错误码直接报错退出。
Ctrl+C 取消并正常退出。`logout` 先调用 B站 退出登录接口，让服务器端的 SESSDATA 失效，再删除本地登录凭据，可重复执行。
B站 端退出失败（网络错误、错误码、cookie 已失效）时记一条警告，仍删除本地凭据；
Ctrl+C 取消则保留本地凭据。

数据目录遵循 [ADR 0002](docs/adr/0002-portable-data-directory.md)：Release 用程序目录下的 `data/`；
Debug 从程序目录向上寻找 `Aether.slnx`，用其所在目录下的 `data/`，找不到则退回程序目录。
与启动命令所在目录无关。SQLite 文件是 `data/aether.db`，使用 WAL；`PRAGMA user_version`
记录表结构版本，当前为 3。升级使用手写 SQL，并与版本号在同一事务中提交。
v2 升级到 v3 保留登录凭据，新增 `blind_box` 表，按真实房间号、观众 uid 和送出时间建立索引。
`credential` 表只存一行，保存完整 cookie 名值 JSON、refresh_token、UTC 保存时间和检查时间 `checked_at`。
v1 升级后保留原凭据，检查时间为空，下次连接立即检查；扫码或刷新保存时检查时间等于保存时间。
凭据暂存明文，`data/` 已被 Git 忽略；连接开启 `secure_delete`，删除或覆盖的凭据不会残留在文件里。
macOS / Linux 上数据库文件（含 `-wal` / `-shm`）权限为 0600，只有当前用户可读写；Windows 依赖目录继承的 ACL。
`login` / `logout` / `watch` 都会打开数据库。CLI 与 GUI 放在同一发布目录时共用这些数据。

Core 的业务入口都在 `AetherClient`：`LoginAsync` 返回 `LoginQrCode`、`LoggedIn` 更新流；
`LogoutAsync` 删除凭据；`WatchAsync` 返回 `WatchQrCode`（需要登录时）、`Connecting`、`Connected`、`Reconnecting`、`Danmaku` 更新流。
取消或释放更新流会结束对应操作。测试从这些入口驱动，注入假的 HTTP、真实 BCL WebSocket 的本地对端、
`FakeTimeProvider` 和独立临时数据目录，使用真实 SQLite，不访问 B站。真实捕获测试帧的来源与脱敏方式见
[Fixtures/README.md](tests/Aether.Core.Tests/Fixtures/README.md)。

CLI 和 GUI 启动时各调用一次 `DataDirectory.Locate()`，把路径传给 `AetherClient`，GUI 同时把它传给
`GuiSettings.Load`。生产环境统一使用 `AetherClient(logger, dataDirectory)` 构造函数组装 HTTP、WebSocket
和系统时间；测试继续使用可注入这些依赖的构造函数。

macOS 手动验收：运行 `login` 并用手机扫码，确认提示“已登录”；
下面的查询只输出凭据行数与保存时间，不输出 cookie 或 token：

```sh
sqlite3 data/aether.db 'SELECT count(*), max(saved_at) FROM credential;'
dotnet run --project src/Aether.Cli -- logout
sqlite3 data/aether.db 'SELECT count(*) FROM credential;'
```

登录后应为 1 行，退出后应为 0 行。

观看手动验收：运行 `watch <房间号>`，没有凭据时应直接显示二维码，扫码后依次提示
“连接中”“已连接”，收到的弹幕显示真实昵称。取消后再次运行 `watch` 应直接连接；
运行 `logout` 后再运行 `watch` 应重新显示二维码，扫码阶段按 Ctrl+C 应正常退出。
重连手动验收：观看期间断开网络，确认 stderr 出现“重连中”；等待后恢复网络，
确认再次出现“已连接”并继续打印弹幕。再次断网，在等待重连时按 Ctrl+C，应立即正常退出。

GUI 使用 Fluent 主题并跟随系统深色／浅色。输入房间号后点“连接”或回车，需要时在弹幕列表的位置
显示二维码；扫码过期自动换码，成功后继续连接。点“断开”可取消扫码或结束连接，再输入新的
房间号。“退出登录”先结束当前连接或扫码，再删除凭据；关闭窗口也会先取消并等待当前操作结束。
窗口显示错误提示，日志写到文件和调试控制台。列表开启虚拟化，在底部时跟随新弹幕；往上翻看历史时暂停，
滚回底部后恢复。默认勾选“只保留最近 1000 条”，超出时丢弃最旧的弹幕；取消勾选后保留全部，
重新勾选立即裁剪到最新 1000 条。已丢弃的弹幕不会恢复。
该设置一改就写入 `data/gui.yml`，重启后恢复；文件缺失、为空、解析失败或值留空时使用默认值，未知的键会被忽略。
macOS / Windows / Ubuntu CI 的 `dotnet build` 都会构建 GUI，`dotnet test` 运行 Core 和 GUI 无头测试；GUI 不支持 Linux 桌面运行。
`tests/Aether.Gui.Tests` 用 Avalonia.Headless 覆盖设置读写、保留条数和跟随最新弹幕；真实窗口的观感仍按下面步骤手动验收。

GUI 手动验收（macOS）：

1. 运行 CLI `login` 并扫码，再启动 GUI，连接同一直播间应直接显示“已连接”，不出现二维码。
2. 点“退出登录”后再次连接，应显示二维码；等待过期应换码，扫码成功后显示弹幕的时间、昵称和内容。
3. 分别在扫码、已连接和重连中点“断开”，应回到“未连接”，房间号恢复可编辑；扫码期间关闭窗口应退出进程。
4. 输入非正整数应提示房间号格式错误；输入不存在的房间号应在扫码前显示 Core 返回的错误。
5. 连接后断网，等待出现“重连中”，恢复网络应回到“已连接”并继续显示弹幕。
6. 切换系统深色／浅色，确认窗口跟随；缩小窗口后列表仍可滚动，中文提示和二维码可读。
7. 持续接收弹幕，在底部时确认跟随；往上翻后新弹幕不能拉回底部，滚回底部后恢复跟随。
   收到超过 1000 条后重复验证，包含多行弹幕。
8. 默认最多保留 1000 条；取消勾选后确认能超过 1000 条，再勾选应立即只留下最新 1000 条。
   修改复选框后重启，确认状态保留。
9. 关闭 GUI，备份 `data/gui.yml` 并把内容改为 `KeepRecentDanmaku: [`，重新启动仍应正常，且默认勾选。
   验证后恢复备份。
10. 点“设置”，检查日志分组、两项“重启后生效”提示、盲盒统计开关／查询关键字／发送间隔的“下次观看直播间时生效”提示及深色／浅色下的可读性；滚动查看全部字段，改值并保存应显示成功。关键字为空或发送间隔为非正整数时应禁用保存。
    点“打开设置文件”，确认系统默认程序打开的是同一份 `data/aether.yml`，手加的注释仍在。

测试使用 xUnit v3 的 Microsoft Testing Platform 运行器（由 `global.json` 选择）：

```sh
dotnet test --project tests/Aether.Core.Tests --filter-class Aether.Core.Tests.ProtocolTests
```

协议依据：[ADR 0001](docs/adr/0001-web-danmaku-protocol-with-qr-login.md)、
[弹幕流](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/live/message_stream.md)、
[直播间信息](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/live/info.md)、
[扫码登录](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/login/login_action/QR.md)、
[刷新登录凭据](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/login/cookie_refresh.md)、
[wbi 签名](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/misc/sign/wbi.md)。
