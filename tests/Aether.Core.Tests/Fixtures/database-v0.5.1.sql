-- Schema from tag v0.5.1, src/Aether.Core/Database.cs (user_version 7), made by running its upgrade steps on a new file; all rows below
-- are synthetic.
CREATE TABLE credential (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    cookies TEXT NOT NULL,
    refresh_token TEXT NOT NULL,
    saved_at TEXT NOT NULL,
    checked_at TEXT
);
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
, hidden_from_stream INTEGER NOT NULL DEFAULT 0);
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
CREATE TABLE gift_record (
    id INTEGER PRIMARY KEY, room_id INTEGER NOT NULL, uid INTEGER NOT NULL, nickname TEXT NOT NULL,
    face_url TEXT NOT NULL, gift_id INTEGER NOT NULL, gift_name TEXT NOT NULL, icon_url TEXT NOT NULL,
    guard_level INTEGER NOT NULL, gift_key TEXT NOT NULL, num INTEGER NOT NULL, spend INTEGER NOT NULL, value INTEGER NOT NULL,
    blind_gift_id INTEGER, blind_gift_name TEXT, blind_gift_price INTEGER, tid TEXT UNIQUE, timestamp INTEGER,
    for_blind_box INTEGER NOT NULL, board_period INTEGER
, viewer_guard_level INTEGER NOT NULL DEFAULT 0);
CREATE INDEX gift_record_blind_box ON gift_record (room_id, uid, timestamp) WHERE for_blind_box = 1;
CREATE INDEX gift_record_board ON gift_record (board_period) WHERE board_period IS NOT NULL;
CREATE TABLE gift_board_period (
    id INTEGER PRIMARY KEY, room_id INTEGER NOT NULL, started_at TEXT NOT NULL, ended_at TEXT,
    threshold INTEGER NOT NULL, threshold_by INTEGER NOT NULL, mascot INTEGER NOT NULL, volume INTEGER NOT NULL
);
CREATE INDEX gift_board_period_room ON gift_board_period (room_id);
CREATE UNIQUE INDEX gift_board_period_running ON gift_board_period (room_id) WHERE ended_at IS NULL;
INSERT INTO credential VALUES (1, '{"SESSDATA":"legacy-session","bili_jct":"legacy-csrf","DedeUserID":"123"}', 'legacy-token',
    '2026-11-01T12:00:00.0000000+00:00', '2026-11-01T12:00:00.0000000+00:00');
INSERT INTO lottery VALUES ('legacy-pool', 7734200, 'pool', '停', '2026-11-03T12:00:00.0000000+00:00',
    '2026-11-03T12:10:00.0000000+00:00', 0, NULL, NULL, '', 0);
INSERT INTO lottery_prize VALUES ('legacy-pool', 0, '键盘', 1, 0);
INSERT INTO lottery VALUES ('legacy-entry', 1768506153, 'entry', '', '2026-11-04T12:00:00.0000000+00:00',
    '2026-11-04T12:10:00.0000000+00:00', 2, NULL, NULL, '抽奖', 0);
INSERT INTO lottery_award VALUES ('legacy-entry', 0, '一等奖', '鼠标', 1, 0);
-- 周末 is a 奖池抽奖 set in both rooms, saved last in 1768506153; 日常 shares its name across the two 抽奖方式.
INSERT INTO lottery_prize_set VALUES (7734200, 'pool', '周末', '2026-11-02T12:00:00.0000000+00:00',
    '[{"Name":"抱枕","Weight":2,"Grade":0}]');
INSERT INTO lottery_prize_set VALUES (1768506153, 'pool', '周末', '2026-11-03T12:00:00.0000000+00:00',
    '[{"Name":"键盘","Weight":1,"Grade":0}]');
INSERT INTO lottery_prize_set VALUES (7734200, 'pool', '日常', '2026-11-01T12:00:00.0000000+00:00',
    '[{"Name":"贴纸","Weight":1,"Grade":2}]');
INSERT INTO lottery_prize_set VALUES (1768506153, 'entry', '日常', '2026-11-01T13:00:00.0000000+00:00',
    '[{"Name":"幸运奖","Prize":"贴纸","Capacity":3,"Grade":2}]');
INSERT INTO gift_board_period VALUES (1, 7734200, '2026-11-05T12:00:00.0000000+00:00', NULL, 100, 1, 1, 60);
INSERT INTO gift_record VALUES (1, 7734200, 42, '测试观众', 'https://i0.hdslb.com/bfs/face/legacy.jpg', 32698, '小蛋糕',
    'https://s1.hdslb.com/bfs/live/legacy.png', 0, 'gift:32698', 2, 3000, 3000, NULL, NULL, NULL, 'legacy-tid-1', 1793880000, 0, 1, 0);
INSERT INTO gift_record VALUES (2, 7734200, 42, '测试观众', '', 10003, '舰长', '', 3, 'guard:3', 1, 198000, 198000,
    NULL, NULL, NULL, 'legacy-payflow-2', NULL, 0, 1, 3);
INSERT INTO gift_record VALUES (3, 7734200, 43, '测试观众二', '', 200, '测试礼物', '', 0, 'gift:200', 1, 1000, 2000,
    100, '测试盲盒', 1000, 'legacy-tid-3', 1793880060, 1, NULL, 0);
PRAGMA user_version = 7;
