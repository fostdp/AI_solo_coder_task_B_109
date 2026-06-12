#!/bin/bash
set -e

DATA_DIR="${DATA_DIR:-./data}"
DB_FILE="$DATA_DIR/sculpture_monitor.db"
INIT_SQL="./database/init.sql"

mkdir -p "$DATA_DIR"

if [ ! -f "$DB_FILE" ]; then
    echo "[init] 初始化SQLite数据库: $DB_FILE"
    if [ -f "$INIT_SQL" ]; then
        sqlite3 "$DB_FILE" < "$INIT_SQL"
    else
        echo "[init] 创建基础表结构..."
        sqlite3 "$DB_FILE" <<'EOSQL'
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA wal_autocheckpoint=1000;
PRAGMA cache_size=-64000;
PRAGMA temp_store=MEMORY;

CREATE TABLE IF NOT EXISTS sculptures (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    location TEXT,
    era TEXT,
    description TEXT,
    image_url TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS sensors (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    code TEXT NOT NULL UNIQUE,
    sculpture_id INTEGER NOT NULL,
    sensor_type TEXT NOT NULL,
    status TEXT DEFAULT 'ONLINE',
    last_reading_at DATETIME,
    FOREIGN KEY (sculpture_id) REFERENCES sculptures(id)
);

CREATE TABLE IF NOT EXISTS sensor_data (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sensor_id INTEGER NOT NULL,
    na_concentration REAL,
    k_concentration REAL,
    ca_concentration REAL,
    salt_concentration REAL,
    surface_coverage REAL,
    temperature REAL,
    humidity REAL,
    signal_strength INTEGER,
    recorded_at DATETIME NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (sensor_id) REFERENCES sensors(id)
);

CREATE TABLE IF NOT EXISTS alerts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    sculpture_id INTEGER,
    alert_type TEXT NOT NULL,
    alert_level TEXT DEFAULT 'WARNING',
    message TEXT,
    current_value REAL,
    threshold_value REAL,
    is_resolved INTEGER DEFAULT 0,
    triggered_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    resolved_at DATETIME,
    FOREIGN KEY (sculpture_id) REFERENCES sculptures(id)
);

CREATE TABLE IF NOT EXISTS alert_thresholds (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    metric_name TEXT NOT NULL UNIQUE,
    threshold_value REAL NOT NULL,
    unit TEXT,
    description TEXT,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_sensor_data_sensor_time ON sensor_data(sensor_id, recorded_at);
CREATE INDEX IF NOT EXISTS idx_sensor_data_recorded ON sensor_data(recorded_at);
CREATE INDEX IF NOT EXISTS idx_alerts_sculpture ON alerts(sculpture_id, triggered_at);
CREATE INDEX IF NOT EXISTS idx_alerts_unresolved ON alerts(is_resolved, triggered_at);

INSERT OR IGNORE INTO alert_thresholds (metric_name, threshold_value, unit, description) VALUES
    ('surface_coverage', 30.0, '%', '表面盐结晶覆盖率'),
    ('na_concentration', 500.0, 'ppm', 'Na⁺浓度'),
    ('k_concentration', 300.0, 'ppm', 'K⁺浓度'),
    ('ca_concentration', 400.0, 'ppm', 'Ca²⁺浓度');
EOSQL
    fi
    echo "[init] 数据库初始化完成"
else
    echo "[init] 数据库已存在: $DB_FILE"
fi

sqlite3 "$DB_FILE" "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;" 2>/dev/null || true
MODE=$(sqlite3 "$DB_FILE" "PRAGMA journal_mode" 2>/dev/null || echo "unknown")
echo "[init] SQLite journal_mode=$MODE"
