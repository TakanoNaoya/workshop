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

INSERT INTO m_item_group (item_group_id, item_group_div, item_group_nm, del_flg, modified_date, modified_user)
VALUES
    (1, 'SUPPLY', '備品', '0', NOW(), 1),
    (2, 'SNACK', 'お菓子', '0', NOW(), 1),
    (3, 'DRINK', '飲料', '0', NOW(), 1)
ON CONFLICT (item_group_id) DO NOTHING;

INSERT INTO m_items (
    item_id, item_group_id, item_nm, quantity, unit, selling_price, jan_code, alert_threshold, del_flg, modified_date, modified_user
)
VALUES
    (1, 1, 'ボールペン', 12, '本', 120, NULL, 3, '0', NOW(), 1),
    (2, 1, '付箋', 6, '冊', 180, NULL, 2, '0', NOW(), 1),
    (3, 1, 'アルコールティッシュ', 2, '個', 320, NULL, 5, '0', NOW(), 1),
    (4, 2, 'クッキー', 18, '袋', 150, NULL, 4, '0', NOW(), 1),
    (5, 2, 'チョコレート', 9, '個', 100, NULL, 3, '0', NOW(), 1),
    (6, 3, 'ミネラルウォーター', 24, '本', 90, NULL, 6, '0', NOW(), 1)
ON CONFLICT (item_id) DO NOTHING;
