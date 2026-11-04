-- Schema from tag v0.3.1, src/Aether.Core/Database.cs (user_version 5, the same in every release since v0.2.2); all rows below are synthetic.
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
    id TEXT PRIMARY KEY, lottery_id TEXT NOT NULL REFERENCES lottery(id), result TEXT NOT NULL,
    hidden_from_stream INTEGER NOT NULL DEFAULT 0
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
CREATE TABLE lottery_prize_set (
    room_id INTEGER NOT NULL, mode TEXT NOT NULL, name TEXT NOT NULL,
    saved_at TEXT NOT NULL, content TEXT NOT NULL,
    UNIQUE(room_id, mode, name)
);
INSERT INTO credential VALUES (1,
    '{"SESSDATA":"legacy-session","bili_jct":"legacy-csrf","DedeUserID":"123","buvid3":"legacy-buvid"}',
    'legacy-token', '2026-11-01T00:00:00.0000000+00:00', '2026-11-01T01:00:00.0000000+00:00');
-- Viewer 42 sent three boxes: one in October (Beijing time), one on 1 November and two on 2 November; one has no tid.
INSERT INTO blind_box VALUES (1, 7734200, 42, '测试观众', 100, '测试盲盒', 1000,
    200, '测试礼物', 2000, 1, 1000, 2000, 1793448000, 'legacy-tid-1',
    '{"cmd":"SEND_GIFT","data":{"uid":42,"uname":"测试观众","giftId":200,"num":1,"tid":"legacy-tid-1"}}');
INSERT INTO blind_box VALUES (2, 7734200, 42, '测试观众', 100, '测试盲盒', 1000,
    201, '另一个测试礼物', 500, 3, 3000, 1500, 1793516400, 'legacy-tid-2',
    '{"cmd":"SEND_GIFT_V2","data":{"pb":"CCo="}}');
INSERT INTO blind_box VALUES (3, 7734200, 42, '测试观众', 100, '测试盲盒', 1000,
    200, '测试礼物', 2000, 2, 2000, 4000, 1793584800, 'legacy-tid-3',
    '{"cmd":"SEND_GIFT","data":{"uid":42,"uname":"测试观众","giftId":200,"num":2,"tid":"legacy-tid-3"}}');
INSERT INTO blind_box VALUES (4, 7734200, 42, '测试观众', 100, '测试盲盒', 1000,
    201, '另一个测试礼物', 500, 1, 1000, 500, 1793626200, NULL,
    '{"cmd":"SEND_GIFT_V2","data":{"pb":"CCo="}}');
INSERT INTO blind_box VALUES (5, 7734200, 43, '测试观众二', 100, '测试盲盒', 1000,
    200, '测试礼物', 2000, 1, 1000, 2000, 1793584860, 'legacy-tid-5',
    '{"cmd":"SEND_GIFT","data":{"uid":43,"uname":"测试观众二","giftId":200,"num":1,"tid":"legacy-tid-5"}}');
INSERT INTO lottery VALUES ('legacy-pool', 7734200, 'pool', '开',
    '2026-11-01T12:00:00.0000000+00:00', '2026-11-01T12:10:00.0000000+00:00', 0, 42, NULL, '', 0);
INSERT INTO lottery_prize VALUES ('legacy-pool', 0, '键盘', 1, 0);
INSERT INTO lottery_draw VALUES ('legacy-pool-draw', 'legacy-pool',
    '{"Id":"legacy-pool-draw","Prize":{"Name":"键盘","Weight":1,"Grade":0},"Uid":42,"Nickname":"测试观众","StoppedByHost":false,"DrawnAt":"2026-11-01T12:05:00.0000000+00:00","Candidates":[{"Name":"键盘","Weight":1,"Grade":0}]}', 0);
INSERT INTO lottery VALUES ('legacy-entry', 7734200, 'entry', '',
    '2026-11-02T12:00:00.0000000+00:00', '2026-11-02T12:20:00.0000000+00:00', 3, NULL, NULL, '抽奖', 0);
INSERT INTO lottery_award VALUES ('legacy-entry', 0, '一等奖', '键盘', 1, 0);
INSERT INTO lottery_award VALUES ('legacy-entry', 1, '参与奖', '贴纸', 1, 2);
INSERT INTO lottery_entry VALUES ('legacy-entry', 42, '测试观众', '2026-11-02T12:01:00.0000000+00:00');
INSERT INTO lottery_entry VALUES ('legacy-entry', 43, '测试观众二', '2026-11-02T12:02:00.0000000+00:00');
INSERT INTO lottery_draw VALUES ('legacy-entry-draw-1', 'legacy-entry',
    '{"Id":"legacy-entry-draw-1","AwardIndex":0,"Award":{"Name":"一等奖","Prize":"键盘","Capacity":1,"Grade":0},"Winner":{"Uid":42,"Nickname":"测试观众","EnteredAt":"2026-11-02T12:01:00.0000000+00:00"},"DrawnAt":"2026-11-02T12:10:00.0000000+00:00","Candidates":["测试观众","测试观众二"]}', 0);
INSERT INTO lottery_draw VALUES ('legacy-entry-draw-2', 'legacy-entry',
    '{"Id":"legacy-entry-draw-2","AwardIndex":1,"Award":{"Name":"参与奖","Prize":"贴纸","Capacity":1,"Grade":2},"Winner":{"Uid":43,"Nickname":"测试观众二","EnteredAt":"2026-11-02T12:02:00.0000000+00:00"},"DrawnAt":"2026-11-02T12:15:00.0000000+00:00","Candidates":["测试观众二"]}', 1);
INSERT INTO lottery_prize_set VALUES (7734200, 'pool', '周末', '2026-11-01T11:00:00.0000000+00:00',
    '[{"Name":"键盘","Weight":1,"Grade":0},{"Name":"鼠标","Weight":3,"Grade":1}]');
INSERT INTO lottery_prize_set VALUES (7734200, 'entry', '周末', '2026-11-02T11:00:00.0000000+00:00',
    '[{"Name":"一等奖","Prize":"键盘","Capacity":1,"Grade":0},{"Name":"参与奖","Prize":"贴纸","Capacity":2,"Grade":2}]');
PRAGMA user_version = 5;
