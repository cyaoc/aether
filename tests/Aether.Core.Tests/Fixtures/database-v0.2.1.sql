-- Schema from tag v0.2.1, src/Aether.Core/Database.cs; all rows below are synthetic.
CREATE TABLE credential (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    cookies TEXT NOT NULL, refresh_token TEXT NOT NULL,
    saved_at TEXT NOT NULL, checked_at TEXT
);
CREATE TABLE blind_box (
    id INTEGER PRIMARY KEY, room_id INTEGER NOT NULL, uid INTEGER NOT NULL,
    nickname TEXT NOT NULL, blind_gift_id INTEGER NOT NULL, blind_gift_name TEXT NOT NULL,
    blind_gift_price INTEGER NOT NULL, opened_gift_id INTEGER NOT NULL, opened_gift_name TEXT NOT NULL,
    opened_gift_price INTEGER NOT NULL, num INTEGER NOT NULL, spend INTEGER NOT NULL,
    opened_value INTEGER NOT NULL, timestamp INTEGER NOT NULL, tid TEXT UNIQUE, raw_message TEXT NOT NULL
);
CREATE INDEX blind_box_room_viewer_time ON blind_box (room_id, uid, timestamp);
CREATE TABLE lottery (
    id TEXT PRIMARY KEY, room_id INTEGER NOT NULL, mode TEXT NOT NULL,
    stop_word TEXT NOT NULL, started_at TEXT NOT NULL, ended_at TEXT,
    phase INTEGER NOT NULL, uid INTEGER, draw_until TEXT,
    keyword TEXT NOT NULL DEFAULT '', hidden INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX lottery_active_room ON lottery(room_id) WHERE ended_at IS NULL;
CREATE TABLE lottery_prize (
    lottery_id TEXT NOT NULL REFERENCES lottery(id), position INTEGER NOT NULL,
    name TEXT NOT NULL, weight INTEGER NOT NULL CHECK(weight > 0), grade INTEGER NOT NULL,
    PRIMARY KEY(lottery_id, position)
);
CREATE TABLE lottery_draw (
    id TEXT PRIMARY KEY, lottery_id TEXT NOT NULL REFERENCES lottery(id), result TEXT NOT NULL
);
CREATE INDEX lottery_draw_lottery ON lottery_draw(lottery_id);
CREATE TABLE lottery_award (
    lottery_id TEXT NOT NULL REFERENCES lottery(id), position INTEGER NOT NULL,
    name TEXT NOT NULL, prize TEXT NOT NULL, capacity INTEGER NOT NULL CHECK(capacity > 0), grade INTEGER NOT NULL,
    PRIMARY KEY(lottery_id, position)
);
CREATE TABLE lottery_entry (
    lottery_id TEXT NOT NULL REFERENCES lottery(id), uid INTEGER NOT NULL,
    nickname TEXT NOT NULL, entered_at TEXT NOT NULL,
    PRIMARY KEY(lottery_id, uid)
);
INSERT INTO credential VALUES (1,
    '{"SESSDATA":"legacy-session","bili_jct":"legacy-csrf","DedeUserID":"123","buvid3":"legacy-buvid"}',
    'legacy-token', '2023-12-10T00:00:00.0000000+00:00', '2023-12-10T01:00:00.0000000+00:00');
INSERT INTO blind_box VALUES (1, 7734200, 42, '测试观众', 100, '测试盲盒', 1000,
    200, '测试礼物', 2000, 2, 2000, 4000, 1702204169, 'legacy-tid', '{"cmd":"SEND_GIFT"}');
INSERT INTO lottery VALUES ('legacy-pool', 7734200, 'pool', '开',
    '2023-12-10T00:00:00.0000000+00:00', '2023-12-10T00:10:00.0000000+00:00', 0, 42, NULL, '', 0);
INSERT INTO lottery_prize VALUES ('legacy-pool', 0, '键盘', 1, 0);
INSERT INTO lottery_draw VALUES ('legacy-draw', 'legacy-pool',
    '{"Id":"legacy-draw","Prize":{"Name":"键盘","Weight":1,"Grade":0},"Uid":42,"Nickname":"测试观众","StoppedByHost":false,"DrawnAt":"2023-12-10T00:05:00.0000000+00:00","Candidates":[{"Name":"键盘","Weight":1,"Grade":0}]}');
PRAGMA user_version = 3;
