# 智慧油田注水开发动态调控系统

## 项目概述

本系统是一套完整的智慧油田注水开发动态调控全栈应用，实现油田注水井和采油井的实时数据采集、可视化展示、智能调配优化和告警管理。

### 业务场景
- **注水井**：300口，每日上报注水量、注水压力、吸水指数
- **采油井**：500口，每日上报产液量、产油量、含水率、动液面
- **数据传输**：4G DTU通过MQTT协议上报
- **核心目标**：基于注采平衡和水驱特征曲线，优化日注水量，减缓含水率上升，最大化产油量

---

## 技术架构

```
┌─────────────────┐    MQTT    ┌─────────────────┐    HTTP    ┌─────────────────┐
│  4G DTU 模拟器  │───────────►│  SpringBoot 后端│◄───────────│   Web 前端      │
│  (Python)       │            │  (Java 17)      │            │  (Canvas+Leaflet)│
└─────────────────┘            └────────┬────────┘            └─────────────────┘
                                        │
                                        ▼
                              ┌─────────────────┐
                              │ PostgreSQL      │
                              │  + PostGIS      │
                              └─────────────────┘
```

### 核心技术栈

#### 后端
- **框架**：Spring Boot 3.2.0
- **ORM**：Spring Data JPA + Hibernate Spatial 6.4.0
- **空间计算**：JTS (Java Topology Suite) 1.19.0
- **优化算法**：Apache Commons Math 3.6.1 (Simplex线性规划)
- **消息队列**：Eclipse Paho MQTT Client
- **数据库**：PostgreSQL 14+ + PostGIS 3.2+
- **定时任务**：Spring @Scheduled

#### 前端
- **地图框架**：Leaflet 1.9.4
- **绘制引擎**：HTML5 Canvas
- **图表库**：Chart.js 4.4.0
- **HTTP客户端**：Axios
- **样式**：原生CSS3

#### 模拟器
- **语言**：Python 3.8+
- **MQTT客户端**：paho-mqtt

---

## 项目结构

```
AI_solo_coder_task_A_035/
├── database/
│   └── init_schema.sql          # PostgreSQL+PostGIS数据库初始化脚本
├── backend/
│   ├── pom.xml                  # Maven配置
│   └── src/main/
│       ├── resources/
│       │   └── application.yml  # 应用配置文件
│       └── java/com/oilfield/
│           ├── SmartWaterFloodingApplication.java
│           ├── entity/          # 实体类（7个）
│           ├── repository/      # 数据访问层（7个）
│           ├── service/         # 业务逻辑层
│           │   ├── AllocationOptimizationService.java  # 调配优化核心
│           │   ├── AlarmService.java                   # 告警服务
│           │   ├── BlockSummaryService.java            # 区块汇总
│           │   └── MqttDataListener.java               # MQTT数据监听
│           └── controller/      # REST API控制层（6个）
├── frontend/
│   ├── index.html               # 主页面
│   ├── css/
│   │   └── style.css            # 样式文件
│   └── js/
│       ├── config.js            # 配置文件
│       ├── api.js               # API调用封装
│       ├── map.js               # 地图管理
│       ├── charts.js            # 图表管理
│       └── app.js               # 主应用逻辑
├── simulator/
│   ├── dtu_simulator.py         # 4G DTU模拟器
│   └── requirements.txt         # Python依赖
└── README.md                    # 本文档
```

---

## 核心功能模块

### 1. 数据库设计

#### 核心数据表
| 表名 | 说明 | 关键字段 |
|------|------|----------|
| `wells` | 井基础信息 | well_id, well_type, location(Point), block_name, design_pressure |
| `water_injection_data` | 注水井日数据 | water_volume, injection_pressure, absorption_index |
| `production_data` | 采油井日数据 | fluid_volume, oil_volume, water_cut, fluid_level |
| `injection_production_relation` | 注采对应关系 | injection_well_id, production_well_id, effectiveness_type, effectiveness_degree |
| `allocation_suggestion` | 调配建议 | current_water_volume, suggested_water_volume, adjustment_direction |
| `alarms` | 告警信息 | alarm_level, alarm_type, alarm_message, acknowledged |
| `block_daily_summary` | 区块日汇总 | daily_oil_production, daily_water_injection, comprehensive_water_cut |
| `water_flood_curve` | 水驱曲线 | cumulative_water_injection, cumulative_oil_production, curve_slope |

#### 空间特性
- PostGIS Geometry类型存储井位坐标（Point, SRID=4326）
- 空间索引加速地理位置查询
- 支持空间距离计算、缓冲区分析

### 2. 注水调配优化模型

#### 算法原理
基于**注采平衡原理**和**水驱特征曲线**，使用**线性规划（单纯形法）**求解最优解。

#### 水驱特征曲线
```
lg(Lp) = a + b * lg(Np)
其中：
- Lp: 累计产液量
- Np: 累计产油量
- a, b: 回归系数
```

#### 目标函数
```
Maximize: Σ(Wi * Ki) - λ * Σ(ΔWi)
约束条件：
- Σ(Wi) = W_total （注采平衡）
- Wi_min ≤ Wi ≤ Wi_max （单井上下限）
- ΔWi ≤ 0.2 * Wi_current （增幅≤20%）
- ΔWi ≥ -0.3 * Wi_current （降幅≤30%）
```

### 3. 两级告警系统

| 告警级别 | 触发条件 | 告警类型 | 推送方式 |
|---------|----------|----------|----------|
| **一级（水淹预警）** | 采油井含水率月上升 > 5% | WATER_CUT_RISE | MQTT + 前端展示 |
| **二级（井筒异常）** | 注水井压力 > 设计压力 * 80% | PRESSURE_ANOMALY | MQTT + 前端展示 |

### 4. 前端可视化

#### 井位绘制
- **注水井**：蓝色圆圈，带"注"字标识
- **采油井**：红色三角，带"采"字标识
- **注采连线**：颜色根据受效程度
  - 绿色：高效受效（>70%）
  - 黄色：中等受效（40%-70%）
  - 红色：无效受效（<40%）

#### 详情面板
点击井位弹出，包含：
- 井基础信息
- 近90天生产趋势曲线（Chart.js）
- 注采对应分析图
- 最新调配建议（注水井）
- 注采对应关系列表

#### 核心指标
- 区块日产油量（t）
- 区块日注水量（m³）
- 综合含水率（%）

---

## 部署说明

### 1. 数据库部署

#### 系统要求
- PostgreSQL 14+
- PostGIS 3.2+

#### 初始化步骤
```bash
# 1. 创建数据库
createdb -U postgres oilfield_db

# 2. 启用PostGIS扩展
psql -U postgres -d oilfield_db -c "CREATE EXTENSION postgis;"

# 3. 执行初始化脚本
psql -U postgres -d oilfield_db -f database/init_schema.sql
```

### 2. 后端部署

#### 系统要求
- JDK 17+
- Maven 3.8+

#### 配置文件
修改 `backend/src/main/resources/application.yml`：
```yaml
spring:
  datasource:
    url: jdbc:postgresql://localhost:5432/oilfield_db
    username: postgres
    password: your_password

mqtt:
  broker: tcp://localhost:1883
  username: admin
  password: admin
```

#### 启动命令
```bash
cd backend
mvn clean package
java -jar target/smart-water-flooding-1.0.0.jar
```

### 3. MQTT Broker部署
使用EMQX或Mosquitto：
```bash
# Docker方式启动EMQX
docker run -d --name emqx -p 1883:1883 -p 8083:8083 -p 8883:8883 emqx/emqx:5.0
```

### 4. 前端部署

#### 系统要求
- Node.js 16+ 或任意HTTP服务器

#### 启动方式
```bash
# 方式1：使用Python启动
cd frontend
python -m http.server 8080

# 方式2：使用Nginx
# 将frontend目录复制到nginx/html下
```

访问地址：`http://localhost:8080`

### 5. DTU模拟器部署

#### 安装依赖
```bash
cd simulator
pip install -r requirements.txt
```

#### 运行模式

**单日数据上报**：
```bash
python dtu_simulator.py --mode daily --end-date 2024-01-01
```

**历史数据回填**：
```bash
python dtu_simulator.py --mode backfill --start-date 2024-01-01 --end-date 2024-03-31 --speed 5.0
```

**实时模拟**：
```bash
python dtu_simulator.py --mode realtime
```

---

## REST API 接口

### 井信息管理
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/wells` | 获取井列表 |
| GET | `/api/wells/{id}` | 获取单井详情 |
| GET | `/api/wells/{id}/trend?days=90` | 获取井生产趋势 |
| GET | `/api/wells/blocks` | 获取区块列表 |

### 生产数据
| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/data/report` | 生产数据上报（MQTT同时支持） |
| GET | `/api/data/injection/latest` | 获取最新注水数据 |
| GET | `/api/data/production/latest` | 获取最新采油数据 |

### 区块汇总
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/summary/core-indicators?block={block}` | 获取核心指标 |
| GET | `/api/summary/history?days=30` | 获取历史汇总 |

### 告警管理
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/alarms` | 获取告警列表 |
| PUT | `/api/alarms/{id}/acknowledge` | 确认告警 |
| POST | `/api/alarms/check` | 手动触发告警检查 |

### 调配优化
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/allocation/latest` | 获取最新调配建议 |
| POST | `/api/allocation/run` | 手动执行调配优化 |
| GET | `/api/allocation/history` | 获取历史调配建议 |

### 注采关系
| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/relations/map-data` | 获取地图连线数据 |
| GET | `/api/relations/well/{wellId}` | 获取井的注采关系 |

---

## 定时任务配置

| 任务 | 频率 | 说明 |
|------|------|------|
| 区块日汇总 | 每日 00:10 | 计算上一日区块汇总数据 |
| 告警检查 | 每日 08:00 | 检查两级告警条件 |
| 调配优化 | 每周一 02:00 | 生成周度注水调配建议 |

可在 `application.yml` 中配置：
```yaml
scheduling:
  enabled: true
  summary-cron: "0 10 0 * * ?"
  alarm-cron: "0 0 8 * * ?"
  allocation-cron: "0 0 2 ? * MON"
```

---

## 数据格式

### MQTT数据上报格式

**主题**：`oilfield/well/data`

**注水井数据**：
```json
{
  "wellId": "Z-0001",
  "wellType": "INJECTION",
  "reportTime": "2024-01-01T08:00:00",
  "waterVolume": 125.5,
  "injectionPressure": 22.3,
  "absorptionIndex": 4.2
}
```

**采油井数据**：
```json
{
  "wellId": "C-0001",
  "wellType": "PRODUCTION",
  "reportTime": "2024-01-01T08:00:00",
  "fluidVolume": 85.2,
  "oilVolume": 12.8,
  "waterCut": 85.0,
  "fluidLevel": 1250.5
}
```

### MQTT告警推送格式

**主题**：`oilfield/alarm`

```json
{
  "id": 1,
  "wellId": "C-0001",
  "alarmLevel": "LEVEL_1",
  "alarmType": "WATER_CUT_RISE",
  "alarmMessage": "采油井C-0001含水率月上升8.5%，超过5%阈值",
  "alarmTime": "2024-01-01T08:00:00",
  "threshold": 5.0,
  "actualValue": 8.5
}
```

---

## 常见问题

### 1. 数据库连接失败
- 检查PostgreSQL服务是否启动
- 确认PostGIS扩展已安装
- 验证用户名密码和端口配置

### 2. MQTT连接失败
- 检查EMQX/Mosquitto服务是否启动
- 确认防火墙已开放1883端口
- 验证MQTT用户名密码配置

### 3. 调配优化执行失败
- 检查是否有足够的历史数据（建议>30天）
- 查看日志确认线性规划求解是否收敛
- 确认井数据完整性

### 4. 前端地图不显示
- 检查Leaflet CDN是否可访问
- 确认浏览器控制台无CORS错误
- 检查后端API是否正常响应

---

## 性能优化建议

1. **数据库层**：
   - 为日期字段创建B-tree索引
   - 为空间字段创建GiST索引
   - 定期VACUUM ANALYZE优化查询性能

2. **后端层**：
   - 使用Redis缓存热点数据（井列表、最新数据）
   - 批量操作减少数据库交互
   - 异步处理MQTT数据上报

3. **前端层**：
   - 数据按需加载，避免一次性加载所有历史数据
   - Canvas绘制使用requestAnimationFrame
   - 图表数据抽样展示

---

## License

MIT License
