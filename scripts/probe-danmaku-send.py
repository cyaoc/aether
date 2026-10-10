#!/usr/bin/env python3
"""Probes B站's msg/send limits from the bot account (#49).

Sends real danmaku as the account logged in to data/aether.db, so use your own live room. The bot need not be watching.
The form is BilibiliApi.ReplyAsync's, without replay_dmid (it needs a real danmaku id; the bot omits it the same way).

  python3 scripts/probe-danmaku-send.py ROOM repeat --target UID [--target2 UID]
  python3 scripts/probe-danmaku-send.py ROOM pace --target UID [--target2 UID] [--count 12] [--interval 5]

repeat: the same text twice, 2 s apart (to the same viewer, without @, to a second viewer) and 6 s apart.
pace:   distinct replies at a fixed interval, alternating viewers; stops at the first failure.
"""
import argparse
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
        body = self.request("https://api.live.bilibili.com/msg/send", form)
        message = body.get("message") if isinstance(body.get("message"), str) else None
        result = classify(body.get("code"), message)
        print(f"{time.monotonic() - self.started:6.1f}s  {label}  {text!r} @{target or '无'}"
              f"  code={body.get('code')} message={message!r}  → {result}", flush=True)
        return result


def repeat(probe, a, b):
    cases = [("同一观众，间隔 2 秒", a, a, 2), ("第二条不带 @，间隔 2 秒", a, None, 2)]
    if b:
        cases.append(("另一位观众，间隔 2 秒", a, b, 2))
    cases.append(("同一观众，间隔 6 秒", a, a, 6))
    for number, (label, first, second, gap) in enumerate(cases):
        if number:
            time.sleep(7)  # Past the 5 s window and the 1 s limit, so the cases don't affect each other.
        text = "重复测试" + "甲乙丙丁"[number]
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
    probe = Probe(args.data, args.room)
    plan = "最多 8 条" if args.mode == "repeat" else f"最多 {args.count} 条，间隔 {args.interval:g} 秒"
    if input(f"将以 bot 账号向真实房间号 {probe.room} 发送真实弹幕（{plan}）。继续？[y/N] ").strip().lower() != "y":
        sys.exit("已取消。")
    if args.mode == "repeat":
        repeat(probe, args.target, args.target2)
    else:
        pace(probe, [args.target] + ([args.target2] if args.target2 else [None]), args.count, args.interval)


if __name__ == "__main__":
    main()
