-- Workshop initialization script for PostgreSQL
-- This script is executed automatically on first container startup.
CREATE TABLE IF NOT EXISTS m_item_group (
    item_group_id integer PRIMARY KEY,
    item_group_div varchar(20) NOT NULL UNIQUE,
    item_group_nm varchar(50) NOT NULL,
    del_flg varchar(1) NOT NULL DEFAULT '0',
    modified_date timestamp,
    modified_user bigint
);

CREATE TABLE IF NOT EXISTS m_items (
    item_id integer PRIMARY KEY,
    item_group_id integer NOT NULL REFERENCES m_item_group(item_group_id),
    item_nm varchar(50) NOT NULL,
    quantity bigint NOT NULL,
    unit varchar(20) NOT NULL,
    selling_price bigint,
    jan_code varchar(13),
    alert_threshold bigint,
    del_flg varchar(1) NOT NULL DEFAULT '0',
    modified_date timestamp,
    modified_user bigint
);

CREATE TABLE IF NOT EXISTS t_inventory (
    item_id integer PRIMARY KEY REFERENCES m_items(item_id),
    stock_quantity bigint NOT NULL CHECK (stock_quantity >= 0),
    del_flg varchar(1) NOT NULL DEFAULT '0',
    modified_date timestamp,
    modified_user bigint
);

CREATE TABLE IF NOT EXISTS t_inventory_history (
    inventory_history_id bigserial PRIMARY KEY,
    item_id integer NOT NULL REFERENCES m_items(item_id),
    operation_kbn varchar(20) NOT NULL,
    change_quantity bigint NOT NULL,
    after_quantity bigint NOT NULL CHECK (after_quantity >= 0),
    note varchar(200),
    created_date timestamp NOT NULL DEFAULT NOW(),
    created_user bigint
);

CREATE INDEX IF NOT EXISTS idx_t_inventory_history_item_id_created_date
    ON t_inventory_history (item_id, created_date DESC);

INSERT INTO m_item_group (item_group_id, item_group_div, item_group_nm, del_flg, modified_date, modified_user)
VALUES
    (1, 'SUPPLY', '備品', '0', '2026-06-08 11:48:27.529438', 1),
    (2, 'SNACK', 'お菓子', '0', '2026-06-08 11:48:27.529438', 1),
    (3, 'DRINK', '飲料', '0', '2026-06-08 11:48:27.529438', 1)
ON CONFLICT (item_group_id) DO NOTHING;

INSERT INTO m_items (
    item_id, item_group_id, item_nm, quantity, unit, selling_price, jan_code, alert_threshold, del_flg, modified_date, modified_user
)
VALUES
    (1, 1, 'ボールペン', 12, '本', 200, NULL, 3, '0', '2026-06-11 10:51:37.508346', 1),
    (2, 1, '付箋', 6, '冊', 180, NULL, 2, '0', '2026-06-08 11:48:27.531707', 1),
    (3, 1, 'アルコールティッシュ', 2, '個', 320, NULL, 5, '0', '2026-06-08 11:48:27.531707', 1),
    (4, 2, 'クッキー', 18, '袋', 150, NULL, 4, '0', '2026-06-08 11:48:27.531707', 1),
    (5, 2, 'チョコレート', 9, '個', 100, NULL, 3, '0', '2026-06-08 11:48:27.531707', 1),
    (6, 3, 'ミネラルウォーター', 24, '本', 90, NULL, 6, '0', '2026-06-08 11:48:27.531707', 1)
ON CONFLICT (item_id) DO NOTHING;

INSERT INTO t_inventory (item_id, stock_quantity, del_flg, modified_date, modified_user)
VALUES
    (1, 12, '0', '2026-06-11 10:58:45.213458', 1),
    (2, 6, '0', '2026-06-11 10:58:45.213458', 1),
    (3, 2, '0', '2026-06-11 10:58:45.213458', 1),
    (4, 18, '0', '2026-06-11 10:58:45.213458', 1),
    (5, 9, '0', '2026-06-11 10:58:45.213458', 1),
    (6, 24, '0', '2026-06-11 10:58:45.213458', 1)
ON CONFLICT (item_id) DO NOTHING;

INSERT INTO t_inventory_history (
    inventory_history_id, item_id, operation_kbn, change_quantity, after_quantity, note, created_date, created_user
)
VALUES
    (1, 2, 'OUTBOUND', 1, 7, '販売 #50', '2026-06-11 11:02:01.78717', 1),
    (2, 2, 'OUTBOUND', 4, 10, '販売 #44', '2026-06-11 09:02:01.78717', 1),
    (3, 2, 'OUTBOUND', -2, 4, '販売 #38', '2026-06-11 07:02:01.78717', 1),
    (4, 2, 'OUTBOUND', 1, 7, '販売 #32', '2026-06-11 05:02:01.78717', 1),
    (5, 2, 'OUTBOUND', 4, 10, '販売 #26', '2026-06-11 03:02:01.78717', 1),
    (6, 2, 'OUTBOUND', -2, 4, '販売 #20', '2026-06-11 01:02:01.78717', 1),
    (7, 2, 'OUTBOUND', 1, 7, '販売 #14', '2026-06-10 23:02:01.78717', 1),
    (8, 2, 'OUTBOUND', 4, 10, '販売 #8', '2026-06-10 21:02:01.78717', 1),
    (9, 2, 'OUTBOUND', -2, 4, '販売 #2', '2026-06-10 19:02:01.78717', 1),
    (10, 3, 'ADJUST', -4, 0, '棚卸調整 #45', '2026-06-11 09:22:01.78717', 1),
    (11, 3, 'ADJUST', -1, 1, '棚卸調整 #39', '2026-06-11 07:22:01.78717', 1),
    (12, 3, 'ADJUST', 2, 4, '棚卸調整 #33', '2026-06-11 05:22:01.78717', 1),
    (13, 3, 'ADJUST', -4, 0, '棚卸調整 #27', '2026-06-11 03:22:01.78717', 1),
    (14, 3, 'ADJUST', -1, 1, '棚卸調整 #21', '2026-06-11 01:22:01.78717', 1),
    (15, 3, 'ADJUST', 2, 4, '棚卸調整 #15', '2026-06-10 23:22:01.78717', 1),
    (16, 3, 'ADJUST', -4, 0, '棚卸調整 #9', '2026-06-10 21:22:01.78717', 1),
    (17, 3, 'ADJUST', -1, 1, '棚卸調整 #3', '2026-06-10 19:22:01.78717', 1),
    (18, 4, 'INBOUND', -3, 15, '仕入れ #46', '2026-06-11 09:42:01.78717', 1),
    (19, 4, 'INBOUND', 0, 18, '仕入れ #40', '2026-06-11 07:42:01.78717', 1),
    (20, 4, 'INBOUND', 3, 21, '仕入れ #34', '2026-06-11 05:42:01.78717', 1),
    (21, 4, 'INBOUND', -3, 15, '仕入れ #28', '2026-06-11 03:42:01.78717', 1),
    (22, 4, 'INBOUND', 0, 18, '仕入れ #22', '2026-06-11 01:42:01.78717', 1),
    (23, 4, 'INBOUND', 3, 21, '仕入れ #16', '2026-06-10 23:42:01.78717', 1),
    (24, 4, 'INBOUND', -3, 15, '仕入れ #10', '2026-06-10 21:42:01.78717', 1),
    (25, 4, 'INBOUND', 0, 18, '仕入れ #4', '2026-06-10 19:42:01.78717', 1),
    (26, 5, 'OUTBOUND', -2, 7, '販売 #47', '2026-06-11 10:02:01.78717', 1),
    (27, 5, 'OUTBOUND', 1, 10, '販売 #41', '2026-06-11 08:02:01.78717', 1),
    (28, 5, 'OUTBOUND', 4, 13, '販売 #35', '2026-06-11 06:02:01.78717', 1),
    (29, 5, 'OUTBOUND', -2, 7, '販売 #29', '2026-06-11 04:02:01.78717', 1),
    (30, 5, 'OUTBOUND', 1, 10, '販売 #23', '2026-06-11 02:02:01.78717', 1),
    (31, 5, 'OUTBOUND', 4, 13, '販売 #17', '2026-06-11 00:02:01.78717', 1),
    (32, 5, 'OUTBOUND', -2, 7, '販売 #11', '2026-06-10 22:02:01.78717', 1),
    (33, 5, 'OUTBOUND', 1, 10, '販売 #5', '2026-06-10 20:02:01.78717', 1),
    (34, 6, 'ADJUST', -1, 23, '棚卸調整 #48', '2026-06-11 10:22:01.78717', 1),
    (35, 6, 'ADJUST', 2, 26, '棚卸調整 #42', '2026-06-11 08:22:01.78717', 1),
    (36, 6, 'ADJUST', -4, 20, '棚卸調整 #36', '2026-06-11 06:22:01.78717', 1),
    (37, 6, 'ADJUST', -1, 23, '棚卸調整 #30', '2026-06-11 04:22:01.78717', 1),
    (38, 6, 'ADJUST', 2, 26, '棚卸調整 #24', '2026-06-11 02:22:01.78717', 1),
    (39, 6, 'ADJUST', -4, 20, '棚卸調整 #18', '2026-06-11 00:22:01.78717', 1),
    (40, 6, 'ADJUST', -1, 23, '棚卸調整 #12', '2026-06-10 22:22:01.78717', 1),
    (41, 6, 'ADJUST', 2, 26, '棚卸調整 #6', '2026-06-10 20:22:01.78717', 1),
    (42, 1, 'INBOUND', 0, 12, '仕入れ #49', '2026-06-11 10:42:01.78717', 1),
    (43, 1, 'INBOUND', 3, 15, '仕入れ #43', '2026-06-11 08:42:01.78717', 1),
    (44, 1, 'INBOUND', -3, 9, '仕入れ #37', '2026-06-11 06:42:01.78717', 1),
    (45, 1, 'INBOUND', 0, 12, '仕入れ #31', '2026-06-11 04:42:01.78717', 1),
    (46, 1, 'INBOUND', 3, 15, '仕入れ #25', '2026-06-11 02:42:01.78717', 1),
    (47, 1, 'INBOUND', -3, 9, '仕入れ #19', '2026-06-11 00:42:01.78717', 1),
    (48, 1, 'INBOUND', 0, 12, '仕入れ #13', '2026-06-10 22:42:01.78717', 1),
    (49, 1, 'INBOUND', 3, 15, '仕入れ #7', '2026-06-10 20:42:01.78717', 1),
    (50, 1, 'INBOUND', -3, 9, '仕入れ #1', '2026-06-10 18:42:01.78717', 1)
ON CONFLICT (inventory_history_id) DO NOTHING;

SELECT setval(
    pg_get_serial_sequence('t_inventory_history', 'inventory_history_id'),
    GREATEST((SELECT COALESCE(MAX(inventory_history_id), 1) FROM t_inventory_history), 1),
    true
);
