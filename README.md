# Aether

macOS / Windows 上的 B站直播 bot。支持扫码登录、退出登录和匿名观看直播间弹幕。

需要 .NET 10 SDK：

```sh
dotnet build
dotnet test
dotnet run --project src/Aether.Cli -- login
dotnet run --project src/Aether.Cli -- logout
dotnet run --project src/Aether.Cli -- watch 1768506153
```

发布 CLI 后，可执行文件名为 `aether`：

```sh
dotnet publish src/Aether.Cli -c Release -o artifacts/cli
./artifacts/cli/aether watch 1768506153
```

弹幕以 UTF-8 写到 stdout：`[HH:mm:ss] 昵称: 内容`；状态和日志写到 stderr。
Ctrl+C 关闭连接并正常退出，错误返回非零退出码。匿名观看通常显示打码昵称；
部分直播间会返回真实昵称。`watch` 当前仍匿名连接、不重连，服务器断开后命令报错退出；
观看时使用登录凭据由后续任务接入。

`login` 在 stderr 显示字符画二维码，用 B站 App 扫码并确认。每两秒轮询一次，
过期后自动打印新二维码，成功保存凭据后提示“已登录”。Ctrl+C 取消并正常退出。
`logout` 删除本地登录凭据，可重复执行。

数据目录遵循 [ADR 0002](docs/adr/0002-portable-data-directory.md)：Release 用程序目录下的 `data/`；
Debug 从程序目录向上寻找 `Aether.slnx`，用其所在目录下的 `data/`，找不到则退回程序目录。
与启动命令所在目录无关。SQLite 文件是 `data/aether.db`，使用 WAL；`PRAGMA user_version`
记录表结构版本，当前为 1。后续升级使用手写 SQL，并与版本号在同一事务中提交。
`credential` 表只存一行，保存完整 cookie 名值 JSON、refresh_token 和 UTC 保存时间。
凭据暂存明文，`data/` 已被 Git 忽略。CLI 与未来 GUI 放在同一发布目录时共用这些数据。

Core 的业务入口都在 `AetherClient`：`LoginAsync` 返回 `LoginQrCode`、`LoggedIn` 更新流；
`LogoutAsync` 删除凭据；`WatchAsync` 返回 `Connecting`、`Connected`、`Danmaku` 更新流。
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

测试使用 xUnit v3 的 Microsoft Testing Platform 运行器（由 `global.json` 选择）：

```sh
dotnet test --project tests/Aether.Core.Tests --filter-class Aether.Core.Tests.ProtocolTests
```

协议依据：[ADR 0001](docs/adr/0001-web-danmaku-protocol-with-qr-login.md)、
[弹幕流](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/live/message_stream.md)、
[直播间信息](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/live/info.md)、
[扫码登录](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/login/login_action/QR.md)、
[wbi 签名](https://github.com/pskdje/bilibili-API-collect/blob/master/docs/misc/sign/wbi.md)。
