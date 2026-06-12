-- ============================================
-- 智慧油田注水开发动态调控系统 - 数据库初始化脚本
-- PostgreSQL + PostGIS
-- ============================================

-- 启用PostGIS扩展
CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS postgis_topology;

-- 创建数据库
-- CREATE DATABASE smart_oilfield WITH OWNER = postgres ENCODING = 'UTF8';

-- 连接到数据库
-- \c smart_oilfield

-- ============================================
-- 1. 井基础信息表
-- ============================================
DROP TABLE IF EXISTS wells CASCADE;
CREATE TABLE wells (
    well_id VARCHAR(32) PRIMARY KEY,
    well_name VARCHAR(100) NOT NULL,
    well_type VARCHAR(20) NOT NULL CHECK (well_type IN ('INJECTION', 'PRODUCTION')),
    block_name VARCHAR(100) NOT NULL,
    design_pressure DECIMAL(10,2),
    geom GEOMETRY(Point, 4326) NOT NULL,
    status VARCHAR(20) DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE', 'INACTIVE', 'MAINTENANCE')),
    create_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    update_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- 创建空间索引
CREATE INDEX idx_wells_geom ON wells USING GIST (geom);
CREATE INDEX idx_wells_type ON wells(well_type);
CREATE INDEX idx_wells_block ON wells(block_name);

COMMENT ON TABLE wells IS '井基础信息表';
COMMENT ON COLUMN wells.well_id IS '井ID';
COMMENT ON COLUMN wells.well_name IS '井名称';
COMMENT ON COLUMN wells.well_type IS '井类型：INJECTION-注水井，PRODUCTION-采油井';
COMMENT ON COLUMN wells.block_name IS '区块名称';
COMMENT ON COLUMN wells.design_pressure IS '设计压力(MPa)';
COMMENT ON COLUMN wells.geom IS '井位坐标（WGS84经纬度）';

-- ============================================
-- 2. 注水井每日生产数据表
-- ============================================
DROP TABLE IF EXISTS water_injection_data CASCADE;
CREATE TABLE water_injection_data (
    id BIGSERIAL PRIMARY KEY,
    well_id VARCHAR(32) NOT NULL REFERENCES wells(well_id),
    report_date DATE NOT NULL,
    water_volume DECIMAL(10,2) NOT NULL,
    injection_pressure DECIMAL(10,2) NOT NULL,
    water_absorption_index DECIMAL(10,2) NOT NULL,
    create_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(well_id, report_date)
);

CREATE INDEX idx_injection_date ON water_injection_data(report_date);
CREATE INDEX idx_injection_well_date ON water_injection_data(well_id, report_date DESC);

COMMENT ON TABLE water_injection_data IS '注水井每日生产数据表';
COMMENT ON COLUMN water_injection_data.water_volume IS '日注水量(m³)';
COMMENT ON COLUMN water_injection_data.injection_pressure IS '注水压力(MPa)';
COMMENT ON COLUMN water_injection_data.water_absorption_index IS '吸水指数(m³/d·MPa)';

-- ============================================
-- 3. 采油井每日生产数据表
-- ============================================
DROP TABLE IF EXISTS production_data CASCADE;
CREATE TABLE production_data (
    id BIGSERIAL PRIMARY KEY,
    well_id VARCHAR(32) NOT NULL REFERENCES wells(well_id),
    report_date DATE NOT NULL,
    liquid_volume DECIMAL(10,2) NOT NULL,
    oil_volume DECIMAL(10,2) NOT NULL,
    water_cut DECIMAL(5,2) NOT NULL CHECK (water_cut >= 0 AND water_cut <= 100),
    dynamic_fluid_level DECIMAL(10,2) NOT NULL,
    create_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(well_id, report_date)
);

CREATE INDEX idx_production_date ON production_data(report_date);
CREATE INDEX idx_production_well_date ON production_data(well_id, report_date DESC);

COMMENT ON TABLE production_data IS '采油井每日生产数据表';
COMMENT ON COLUMN production_data.liquid_volume IS '日产液量(t)';
COMMENT ON COLUMN production_data.oil_volume IS '日产油量(t)';
COMMENT ON COLUMN production_data.water_cut IS '含水率(%)';
COMMENT ON COLUMN production_data.dynamic_fluid_level IS '动液面(m)';

-- ============================================
-- 4. 注采对应关系表
-- ============================================
DROP TABLE IF EXISTS injection_production_relation CASCADE;
CREATE TABLE injection_production_relation (
    id BIGSERIAL PRIMARY KEY,
    injection_well_id VARCHAR(32) NOT NULL REFERENCES wells(well_id),
    production_well_id VARCHAR(32) NOT NULL REFERENCES wells(well_id),
    effectiveness_type VARCHAR(20) NOT NULL CHECK (effectiveness_type IN ('HIGH', 'MEDIUM', 'LOW')),
    effectiveness_degree DECIMAL(5,2),
    distance DECIMAL(10,2),
    create_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    update_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(injection_well_id, production_well_id)
);

CREATE INDEX idx_relation_injection ON injection_production_relation(injection_well_id);
CREATE INDEX idx_relation_production ON injection_production_relation(production_well_id);
CREATE INDEX idx_relation_effectiveness ON injection_production_relation(effectiveness_type);

COMMENT ON TABLE injection_production_relation IS '注采对应关系表';
COMMENT ON COLUMN injection_production_relation.effectiveness_type IS '受效类型：HIGH-高效，MEDIUM-中等，LOW-无效';
COMMENT ON COLUMN injection_production_relation.effectiveness_degree IS '受效程度(%)';
COMMENT ON COLUMN injection_production_relation.distance IS '井间距(m)';

-- ============================================
-- 5. 注水调配建议表
-- ============================================
DROP TABLE IF EXISTS allocation_suggestion CASCADE;
CREATE TABLE allocation_suggestion (
    id BIGSERIAL PRIMARY KEY,
    well_id VARCHAR(32) NOT NULL REFERENCES wells(well_id),
    suggestion_date DATE NOT NULL,
    current_water_volume DECIMAL(10,2) NOT NULL,
    suggested_water_volume DECIMAL(10,2) NOT NULL,
    adjustment_direction VARCHAR(10) NOT NULL CHECK (adjustment_direction IN ('INCREASE', 'DECREASE', 'KEEP')),
    adjustment_amount DECIMAL(10,2) NOT NULL,
    reason VARCHAR(500),
    model_version VARCHAR(50),
    create_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(well_id, suggestion_date)
);

CREATE INDEX idx_allocation_date ON allocation_suggestion(suggestion_date);
CREATE INDEX idx_allocation_well ON allocation_suggestion(well_id);

COMMENT ON TABLE allocation_suggestion IS '注水调配建议表';
COMMENT ON COLUMN allocation_suggestion.current_water_volume IS '当前日注水量(m³)';
COMMENT ON COLUMN allocation_suggestion.suggested_water_volume IS '建议日注水量(m³)';
COMMENT ON COLUMN allocation_suggestion.adjustment_direction IS '调整方向：INCREASE-增加，DECREASE-减少，KEEP-保持';

-- ============================================
-- 6. 告警信息表
-- ============================================
DROP TABLE IF EXISTS alarms CASCADE;
CREATE TABLE alarms (
    id BIGSERIAL PRIMARY KEY,
    alarm_id VARCHAR(64) NOT NULL UNIQUE,
    well_id VARCHAR(32) NOT NULL REFERENCES wells(well_id),
    alarm_level VARCHAR(20) NOT NULL CHECK (alarm_level IN ('LEVEL_1', 'LEVEL_2')),
    alarm_type VARCHAR(50) NOT NULL,
    alarm_message VARCHAR(500) NOT NULL,
    alarm_value DECIMAL(10,2),
    threshold_value DECIMAL(10,2),
    alarm_time TIMESTAMP NOT NULL,
    is_pushed BOOLEAN DEFAULT FALSE,
    is_acknowledged BOOLEAN DEFAULT FALSE,
    acknowledge_time TIMESTAMP,
    create_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_alarms_well ON alarms(well_id);
CREATE INDEX idx_alarms_level ON alarms(alarm_level);
CREATE INDEX idx_alarms_time ON alarms(alarm_time DESC);
CREATE INDEX idx_alarms_pushed ON alarms(is_pushed);

COMMENT ON TABLE alarms IS '告警信息表';
COMMENT ON COLUMN alarms.alarm_level IS '告警级别：LEVEL_1-一级水淹预警，LEVEL_2-二级井筒异常告警';
COMMENT ON COLUMN alarms.is_pushed IS '是否已推送至调度大屏';

-- ============================================
-- 7. 区块日度汇总表
-- ============================================
DROP TABLE IF EXISTS block_daily_summary CASCADE;
CREATE TABLE block_daily_summary (
    id BIGSERIAL PRIMARY KEY,
    block_name VARCHAR(100) NOT NULL,
    summary_date DATE NOT NULL,
    total_oil_production DECIMAL(12,2) NOT NULL,
    total_water_injection DECIMAL(12,2) NOT NULL,
    average_water_cut DECIMAL(5,2) NOT NULL,
    create_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(block_name, summary_date)
);

CREATE INDEX idx_summary_date ON block_daily_summary(summary_date DESC);
CREATE INDEX idx_summary_block ON block_daily_summary(block_name);

COMMENT ON TABLE block_daily_summary IS '区块日度汇总表';
COMMENT ON COLUMN block_daily_summary.total_oil_production IS '区块日产油量(t)';
COMMENT ON COLUMN block_daily_summary.total_water_injection IS '区块日注水量(m³)';
COMMENT ON COLUMN block_daily_summary.average_water_cut IS '综合含水率(%)';

-- ============================================
-- 8. 水驱特征曲线数据表
-- ============================================
DROP TABLE IF EXISTS water_flood_curve CASCADE;
CREATE TABLE water_flood_curve (
    id BIGSERIAL PRIMARY KEY,
    block_name VARCHAR(100) NOT NULL,
    statistical_date DATE NOT NULL,
    cumulative_oil_production DECIMAL(15,2) NOT NULL,
    cumulative_water_production DECIMAL(15,2) NOT NULL,
    water_cut DECIMAL(5,2) NOT NULL,
    curve_slope DECIMAL(10,4),
    curve_intercept DECIMAL(10,4),
    create_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(block_name, statistical_date)
);

COMMENT ON TABLE water_flood_curve IS '水驱特征曲线数据表';

-- ============================================
-- 初始化测试数据 - 井基础信息
-- ============================================

-- 生成300口注水井（经度：116.0-116.5，纬度：38.5-39.0）
INSERT INTO wells (well_id, well_name, well_type, block_name, design_pressure, geom)
SELECT 
    'INJ-' || lpad(gs::text, 4, '0'),
    '注' || gs || '井',
    'INJECTION',
    CASE 
        WHEN gs <= 100 THEN '东区'
        WHEN gs <= 200 THEN '西区'
        ELSE '南区'
    END,
    35 + random() * 10,
    ST_SetSRID(ST_MakePoint(
        116.0 + random() * 0.5,
        38.5 + random() * 0.5
    ), 4326)
FROM generate_series(1, 300) AS gs;

-- 生成500口采油井
INSERT INTO wells (well_id, well_name, well_type, block_name, geom)
SELECT 
    'PRO-' || lpad(gs::text, 4, '0'),
    '采' || gs || '井',
    'PRODUCTION',
    CASE 
        WHEN gs <= 170 THEN '东区'
        WHEN gs <= 330 THEN '西区'
        ELSE '南区'
    END,
    ST_SetSRID(ST_MakePoint(
        116.0 + random() * 0.5,
        38.5 + random() * 0.5
    ), 4326)
FROM generate_series(1, 500) AS gs;

-- ============================================
-- 初始化注采对应关系
-- ============================================
INSERT INTO injection_production_relation (
    injection_well_id, 
    production_well_id, 
    effectiveness_type, 
    effectiveness_degree,
    distance
)
SELECT 
    i.well_id,
    p.well_id,
    CASE 
        WHEN ST_Distance(i.geom::geography, p.geom::geography) < 300 THEN 'HIGH'
        WHEN ST_Distance(i.geom::geography, p.geom::geography) < 600 THEN 'MEDIUM'
        ELSE 'LOW'
    END,
    CASE 
        WHEN ST_Distance(i.geom::geography, p.geom::geography) < 300 THEN 80 + random() * 20
        WHEN ST_Distance(i.geom::geography, p.geom::geography) < 600 THEN 50 + random() * 30
        ELSE 10 + random() * 40
    END,
    ST_Distance(i.geom::geography, p.geom::geography)
FROM wells i
CROSS JOIN wells p
WHERE i.well_type = 'INJECTION' 
  AND p.well_type = 'PRODUCTION'
  AND i.block_name = p.block_name
  AND ST_Distance(i.geom::geography, p.geom::geography) < 800
  AND random() < 0.15;

-- ============================================
-- 初始化近90天的生产数据
-- ============================================

-- 注水井数据
INSERT INTO water_injection_data (well_id, report_date, water_volume, injection_pressure, water_absorption_index)
SELECT 
    w.well_id,
    d::date,
    80 + random() * 120,
    20 + random() * 15,
    5 + random() * 15
FROM wells w
CROSS JOIN generate_series(
    (CURRENT_DATE - INTERVAL '90 days')::date, 
    CURRENT_DATE, 
    '1 day'
) AS d
WHERE w.well_type = 'INJECTION';

-- 采油井数据
INSERT INTO production_data (well_id, report_date, liquid_volume, oil_volume, water_cut, dynamic_fluid_level)
SELECT 
    w.well_id,
    d::date,
    30 + random() * 70,
    2 + random() * 15,
    60 + random() * 35,
    500 + random() * 1500
FROM wells w
CROSS JOIN generate_series(
    (CURRENT_DATE - INTERVAL '90 days')::date, 
    CURRENT_DATE, 
    '1 day'
) AS d
WHERE w.well_type = 'PRODUCTION';

-- ============================================
-- 创建视图：最新井位数据视图
-- ============================================
CREATE OR REPLACE VIEW v_wells_latest AS
SELECT 
    w.well_id,
    w.well_name,
    w.well_type,
    w.block_name,
    w.design_pressure,
    ST_X(w.geom) as longitude,
    ST_Y(w.geom) as latitude,
    w.status,
    CASE 
        WHEN w.well_type = 'INJECTION' THEN (
            SELECT water_volume FROM water_injection_data 
            WHERE well_id = w.well_id 
            ORDER BY report_date DESC LIMIT 1
        )
        ELSE NULL
    END as latest_water_volume,
    CASE 
        WHEN w.well_type = 'INJECTION' THEN (
            SELECT injection_pressure FROM water_injection_data 
            WHERE well_id = w.well_id 
            ORDER BY report_date DESC LIMIT 1
        )
        ELSE NULL
    END as latest_injection_pressure,
    CASE 
        WHEN w.well_type = 'PRODUCTION' THEN (
            SELECT oil_volume FROM production_data 
            WHERE well_id = w.well_id 
            ORDER BY report_date DESC LIMIT 1
        )
        ELSE NULL
    END as latest_oil_volume,
    CASE 
        WHEN w.well_type = 'PRODUCTION' THEN (
            SELECT water_cut FROM production_data 
            WHERE well_id = w.well_id 
            ORDER BY report_date DESC LIMIT 1
        )
        ELSE NULL
    END as latest_water_cut
FROM wells w;

-- ============================================
-- 创建更新时间触发器函数
-- ============================================
CREATE OR REPLACE FUNCTION update_modified_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.update_time = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- 为需要自动更新update_time的表添加触发器
DROP TRIGGER IF EXISTS update_wells_modtime ON wells;
CREATE TRIGGER update_wells_modtime BEFORE UPDATE ON wells
    FOR EACH ROW EXECUTE FUNCTION update_modified_column();

DROP TRIGGER IF EXISTS update_relation_modtime ON injection_production_relation;
CREATE TRIGGER update_relation_modtime BEFORE UPDATE ON injection_production_relation
    FOR EACH ROW EXECUTE FUNCTION update_modified_column();
