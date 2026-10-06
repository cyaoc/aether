# Aether

macOS / Windows 上的 B站直播 bot。支持扫码登录、退出登录和以登录账号身份观看直播间弹幕。

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

弹幕以 UTF-8 写到 stdout：`[HH:mm:ss] 昵称: 内容`；状态和日志写到 stderr。
Ctrl+C 关闭连接并正常退出，错误返回非零退出码。每次 `watch` 开始直播间连接前，
先用保存的 cookie 调 nav 检查登录状态；没有凭据或已失效时，直接在 stderr 显示二维码，
扫码成功后保存凭据并继续连接，无需先运行 `login`。二维码过期自动换新，扫码期间也可按
Ctrl+C 取消。连接使用 nav 返回的 mid 和凭据中的 buvid3，HTTP 请求携带保存的 cookie，
以接收观众的真实昵称。网络失败、超时、B站 服务器 5xx 或弹幕服务器断开时，`watch`
提示“重连中”，按 1、2、4、8、16、30、30…… 秒自动重试；收到认证成功回复后重置为 1 秒。
60 秒没有收到任何数据也会重连；心跳回复正常的安静直播间不会断开。
每次重连重新获取 wbi key 和弹幕 token，中途凭据失效或被 `logout` 删除时以匿名身份重连
（stderr 警告，昵称可能被打码），不要求重新扫码。扫码阶段的网络失败仍每 2 秒原地重试，
不提示“重连中”，也不作废已显示的二维码。B站 明确拒绝（错误码含房间号不存在、HTTP 4xx、
弹幕认证失败）、无法解析的响应、本地错误（如数据库）和程序缺陷，重试也不会好转，
反复请求还会加重风控，所以直接报错退出。重连等待期间 Ctrl+C 也会立即结束。

`login` 先打开数据库、获取 buvid3，再在 stderr 显示字符画二维码，用 B站 App 扫码并确认。
每两秒轮询一次，过期后自动打印新二维码，成功保存凭据后提示“已登录”。网络错误（连不上、
HTTP 非 2xx、超时）记一条警告，两秒后重试，直到成功或 Ctrl+C；B站返回的错误码直接报错退出。
Ctrl+C 取消并正常退出。`logout` 先调用 B站 退出登录接口，让服务器端的 SESSDATA 失效，再删除本地登录凭据，可重复执行。
B站 端退出失败（网络错误、错误码、cookie 已失效）时记一条警告，仍删除本地凭据；
Ctrl+C 取消则保留本地凭据。

数据目录遵循 [ADR 0002](docs/adr/0002-portable-data-directory.md)：Release 用程序目录下的 `data/`；
Debug 从程序目录向上寻找 `Aether.slnx`，用其所在目录下的 `data/`，找不到则退回程序目录。
与启动命令所在目录无关。SQLite 文件是 `data/aether.db`，使用 WAL；`PRAGMA user_version`
记录表结构版本，当前为 1。后续升级使用手写 SQL，并与版本号在同一事务中提交。
`credential` 表只存一行，保存完整 cookie 名值 JSON、refresh_token 和 UTC 保存时间。
凭据暂存明文，`data/` 已被 Git 忽略；连接开启 `secure_delete`，删除或覆盖的凭据不会残留在文件里。
macOS 上数据库文件（含 `-wal` / `-shm`）权限为 0600，只有当前用户可读写；Windows 依赖目录继承的 ACL。
`login` / `logout` / `watch` 都会打开数据库。CLI 与 GUI 放在同一发布目录时共用这些数据。

Core 的业务入口都在 `AetherClient`：`LoginAsync` 返回 `LoginQrCode`、`LoggedIn` 更新流；
`LogoutAsync` 删除凭据；`WatchAsync` 返回 `WatchQrCode`（需要登录时）、`Connecting`、`Connected`、`Reconnecting`、`Danmaku` 更新流。
取消或释放更新流会结束对应操作。测试从这些入口驱动，注入假的 HTTP、真实 BCL WebSocket 的本地对端、
`FakeTimeProvider` 和独立临时数据目录，使用真实 SQLite，不访问 B站。真实捕获测试帧的来源与脱敏方式见
[Fixtures/README.md](tests/Aether.Core.Tests/Fixtures/README.md)。

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

GUI 使用 Fluent 主题并跟随系统深色／浅色。输入房间号后点“连接”，需要时在弹幕列表的位置
显示二维码；扫码过期自动换码，成功后继续连接。点“断开”可取消扫码或结束连接，再输入新的
房间号。“退出登录”先结束当前连接或扫码，再删除凭据；关闭窗口也会先取消并等待当前操作结束。
窗口显示错误提示，日志仅写到调试控制台。列表开启虚拟化；自动滚动、保留条数和设置持久化留给 #7。
GUI 加入解决方案后，现有 macOS／Windows CI 的 `dotnet build` 会一同构建它。

GUI 手动验收（macOS）：

1. 运行 CLI `login` 并扫码，再启动 GUI，连接同一直播间应直接显示“已连接”，不出现二维码。
2. 点“退出登录”后再次连接，应显示二维码；等待过期应换码，扫码成功后显示弹幕的时间、昵称和内容。
3. 分别在扫码、已连接和重连中点“断开”，应回到“未连接”，房间号恢复可编辑；扫码期间关闭窗口应退出进程。
4. 输入非正整数应提示房间号格式错误；已登录时输入不存在的房间号应显示 Core 返回的错误。
5. 连接后断网，等待出现“重连中”，恢复网络应回到“已连接”并继续显示弹幕。
6. 切换系统深色／浅色，确认窗口跟随；缩小窗口后列表仍可滚动，中文提示和二维码可读。

测试使用 xUnit v3 的 Microsoft Testing Platform 运行器（由 `global.json` 选择）：

```sh
dotnet test --project tests/Aether.Core.Tests --filter-class Aether.Core.Tests.ProtocolTests
```

协议依据：[ADR 0001](docs/adr/0001-web-danmaku-protocol-with-qr-login.md)、
[弹幕流](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/live/message_stream.md)、
[直播间信息](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/live/info.md)、
[扫码登录](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/login/login_action/QR.md)、
[wbi 签名](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/misc/sign/wbi.md)。
