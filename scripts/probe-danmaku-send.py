#!/usr/bin/env python3
"""Probes B站's msg/send limits from the bot account (#49, #67).

Sends real danmaku as the account logged in to data/aether.db, so use your own live room. The bot need not be watching.
The form is BilibiliApi.ReplyAsync's, without replay_dmid (it needs a real danmaku id; the bot omits it the same way).

  python3 scripts/probe-danmaku-send.py ROOM repeat --target UID [--target2 UID]
  python3 scripts/probe-danmaku-send.py ROOM pace --target UID [--target2 UID] [--count 12] [--interval 5]

repeat: the same text twice, 2/4/5/6 s apart to the same viewer; 2 s apart without @ or to a second viewer.
pace:   distinct replies at a fixed interval, alternating viewers; stops at the first failure.
Intervals are measured from the previous request's completion, like SendQueue.
"""
import argparse
from datetime import datetime
import json
import sqlite3
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path


def classify(code, message):
    # Same three kinds as BilibiliApi.CheckDanmakuSent.
    if code in (10030, 10031) or code == 0 and message in ("msg in 1s", "msg repeat"):
        return "频率类失败"
    return "成功" if code == 0 and message == "" else "其他失败"


class Probe:
    def __init__(self, data, room):
        row = sqlite3.connect(data / "aether.db").execute("SELECT cookies FROM credential").fetchone()
        if row is None:
            sys.exit(f"{data / 'aether.db'} 里没有登录凭据，先用 bot 扫码登录。")
        self.cookies = json.loads(row[0])
        self.room = self.request("https://api.live.bilibili.com/room/v1/Room/room_init?id=" + str(room))["data"]["room_id"]
        self.started = time.monotonic()

    def request(self, url, form=None):
        request = urllib.request.Request(url, None if form is None else urllib.parse.urlencode(form).encode(), {
            "User-Agent": "Mozilla/5.0", "Referer": "https://live.bilibili.com/",
            "Cookie": "; ".join(f"{name}={value}" for name, value in self.cookies.items())})
        with urllib.request.urlopen(request, timeout=15) as response:
            return json.load(response)

    def send(self, text, target, label):
        csrf = self.cookies["bili_jct"]
        form = {"roomid": self.room, "msg": text, "rnd": int(time.time()), "fontsize": 25, "color": 16777215,
                "mode": 1, "bubble": 0, "csrf": csrf, "csrf_token": csrf}
        if target:
            form.update(reply_mid=target, reply_uname="", reply_attr=0)
        print(f"开始={datetime.now().astimezone().isoformat(timespec='milliseconds')}"
              f"  {time.monotonic() - self.started:.3f}s  {label}  {text!r} @{target or '无'}", flush=True)
        try:
            body = self.request("https://api.live.bilibili.com/msg/send", form)
        except Exception as error:
            print(f"结束={datetime.now().astimezone().isoformat(timespec='milliseconds')}"
                  f"  {time.monotonic() - self.started:.3f}s  {label}  → 请求异常：{type(error).__name__}", flush=True)
            raise
        message = body.get("message") if isinstance(body.get("message"), str) else None
        result = classify(body.get("code"), message)
        print(f"完成={datetime.now().astimezone().isoformat(timespec='milliseconds')}"
              f"  {time.monotonic() - self.started:.3f}s  {label}  {text!r} @{target or '无'}"
              f"  code={body.get('code')} message={message!r}  → {result}", flush=True)
        return result


def repeat(probe, a, b):
    cases = [("同一观众，间隔 2 秒", a, a, 2), ("第二条不带 @，间隔 2 秒", a, None, 2)]
    if b:
        cases.append(("另一位观众，间隔 2 秒", a, b, 2))
    cases.extend((f"同一观众，间隔 {gap} 秒", a, a, gap) for gap in (4, 5, 6))
    for number, (label, first, second, gap) in enumerate(cases):
        if number:
            time.sleep(7)  # Past the 5 s window and the 1 s limit, so the cases don't affect each other.
        text = "重复测试" + "甲乙丙丁戊己"[number]
        probe.send(text, first, f"[{label}] 第一条")
        time.sleep(gap)
        probe.send(text, second, f"[{label}] 第二条")


def pace(probe, targets, count, interval):
    for number in range(count):
        if number:
            time.sleep(interval)  # Counted from the previous send's end, like SendQueue.
        if probe.send(f"节奏测试{number + 1}", targets[number % len(targets)], f"[间隔 {interval} 秒] 第 {number + 1} 条") != "成功":
            print("出现失败，停止。")
            return


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("room", type=int, help="直播间号（短号也行）")
    parser.add_argument("mode", choices=["repeat", "pace"])
    parser.add_argument("--target", type=int, required=True, help="被 @ 的观众 uid")
    parser.add_argument("--target2", type=int, help="第二位观众 uid，可选")
    parser.add_argument("--count", type=int, default=12)
    parser.add_argument("--interval", type=float, default=5)
    parser.add_argument("--data", type=Path, default=Path(__file__).resolve().parent.parent / "data")
    args = parser.parse_args()
    if args.target2 is not None and args.target2 == args.target:
        parser.error("--target2 必须与 --target 不同，才能验证两位不同观众。")
    probe = Probe(args.data, args.room)
    plan = f"最多 {12 if args.target2 else 10} 条" if args.mode == "repeat" else f"最多 {args.count} 条，间隔 {args.interval:g} 秒"
    if input(f"将以 bot 账号向真实房间号 {probe.room} 发送真实弹幕（{plan}）。继续？[y/N] ").strip().lower() != "y":
        sys.exit("已取消。")
    if args.mode == "repeat":
        repeat(probe, args.target, args.target2)
    else:
        pace(probe, [args.target] + ([args.target2] if args.target2 else [None]), args.count, args.interval)


if __name__ == "__main__":
    main()
