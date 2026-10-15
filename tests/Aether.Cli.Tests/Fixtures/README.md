`welcome.mp4` is a synthetic one-second H.264 + AAC clip for the production CLI / Chrome playback test. It contains no captured user media.

Regenerate with:

```sh
ffmpeg -f lavfi -i 'color=c=orange:s=160x90:r=24:d=1' -f lavfi -i 'sine=f=440:d=1' -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart -shortest welcome.mp4
```

Run the browser acceptance with `AETHER_TEST_CHROME` set to the Chrome executable, then:

```sh
dotnet test --project tests/Aether.Cli.Tests -- --filter-class Aether.Cli.Tests.WelcomeAnimationBrowserTests
```

This uses the same `WatchCommand` as CLI `watch`, the embedded production animation page, real loopback HTTP/SSE and Chrome video decoding. Only B站 HTTP/WebSocket inputs are simulated with `WatchHarness` / `EntryV2Fixture`. Chrome's autoplay flag models streaming software's audible autoplay. This is not a live-room or streaming-software acceptance test.

The default HTTP port case additionally needs an available port 80 that the current user can bind; it reports a skip when that prerequisite is absent. The random-port case always runs when Chrome is supplied.
