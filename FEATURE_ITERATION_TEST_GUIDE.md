# 新增功能 Feature 迭代 - 验证测试文档

## 一、新增功能清单

| 功能模块 | 核心算法 | 主要文件 | API接口 |
|---------|---------|---------|---------|
| **渗透深度预测** | Lucas-Washburn方程 h² = γ·cosθ·r·t/(2ηφ) | [PenetrationPredictionService.cs](file:///d:/SOLO-2/AI_solo_coder_task_B_109/clay_monitor/backend/src/PenetrationPrediction/PenetrationPredictionService.cs) | POST `/api/AdvancedFeatures/penetration/predict` |
| **化学反应预警** | Gibbs自由能ΔG=ΔH-TΔS, Arrhenius速率方程 | [ChemicalReactionService.cs](file:///d:/SOLO-2/AI_solo_coder_task_B_109/clay_monitor/backend/src/ChemicalReaction/ChemicalReactionService.cs) | POST `/api/AdvancedFeatures/reaction/evaluate` |
| **呼吸性评估** | 峰值检测、互相关时滞分析、回滞曲线 | [BreathabilityService.cs](file:///d:/SOLO-2/AI_solo_coder_task_B_109/clay_monitor/backend/src/Breathability/BreathabilityService.cs) | POST `/api/AdvancedFeatures/breathability/analyze` |
| **虚拟加固3D展示** | 3D体素渲染、深度-光泽混合模型 | [VirtualReinforcementService.cs](file:///d:/SOLO-2/AI_solo_coder_task_B_109/clay_monitor/backend/src/VirtualReinforcement/VirtualReinforcementService.cs) | POST `/api/AdvancedFeatures/reinforcement/simulate` |

## 二、API接口清单

### 1. 渗透深度预测接口

#### POST `/api/AdvancedFeatures/penetration/predict`
**请求体：**
```json
{
    "sculptureId": 1,
    "materialName": "TEOS (正硅酸乙酯)",
    "porosity": 0.35,
    "poreRadiusNm": 500.0,
    "viscosityPaS": 0.00085,
    "surfaceTensionNm": 0.0235,
    "contactAngleDeg": 95,
    "timeSeconds": 3600,
    "temperatureC": 25.0
}
```

**响应示例：**
```json
{
    "sculptureId": 1,
    "materialName": "TEOS (正硅酸乙酯)",
    "predictedDepthMm": 5.8432,
    "penetrationRateMmPerS": 0.001623,
    "timeToReach5mm": 2634.58,
    "penetrationGrade": "GOOD",
    "capillaryPressurePa": 1456.23,
    "recommendation": "【良好】TEOS 预测渗透深度 5.84mm，可满足大部分加固需求。"
}
```

#### GET `/api/AdvancedFeatures/penetration/materials`
获取支持的材料列表。

#### POST `/api/AdvancedFeatures/penetration/compare/{sculptureId}`
比较所有材料在指定条件下的渗透表现。

---

### 2. 化学反应预警接口

#### POST `/api/AdvancedFeatures/reaction/evaluate`
**请求体：**
```json
{
    "sculptureId": 1,
    "materialName": "TEOS (正硅酸乙酯)",
    "na2SO4ConcentrationMolL": 0.05,
    "teosConcentrationMolL": 0.5,
    "temperatureC": 25.0,
    "pH": 7.5,
    "relativeHumidity": 0.6,
    "contactTimeHours": 72.0
}
```

**响应示例：**
```json
{
    "sculptureId": 1,
    "materialName": "TEOS (正硅酸乙酯)",
    "gibbsFreeEnergyKJmol": -95.6,
    "equilibriumConstant": 1245.32,
    "isSpontaneous": true,
    "requiresWarning": true,
    "warningLevel": "WARNING",
    "harmfulProductYield": 0.1845,
    "harmfulProducts": ["Na2SiO3 (硅酸钠)", "H2SO4 (硫酸)"],
    "recommendation": "【谨慎使用】TEOS 在当前盐分条件下可能发生不良反应。"
}
```

#### POST `/api/AdvancedFeatures/reaction/evaluate-all/{sculptureId}`
评估所有材料的化学反应风险。

#### GET `/api/AdvancedFeatures/reaction/systems`
获取已知反应体系列表。

---

### 3. 呼吸性评估接口

#### POST `/api/AdvancedFeatures/breathability/analyze`
**请求体：**
```json
{
    "sculptureId": 1,
    "temperatures": [22.1, 22.3, 22.5, ...],
    "humidities": [55.2, 54.8, 54.5, ...],
    "timestamps": ["2024-01-01T00:00:00", ...],
    "porosity": 0.35,
    "moistureContent": 0.25
}
```

**响应示例：**
```json
{
    "sculptureId": 1,
    "breathFrequencyCyclesPerDay": 2.34,
    "breathIntensity": 0.875,
    "temperatureAmplitudeC": 4.2,
    "humidityAmplitudePercent": 8.5,
    "timeLagMinutes": 28.5,
    "selfRegulationScore": 72.5,
    "regulationLevel": "GOOD",
    "recommendation": "【良好】泥塑呼吸性正常，可通过小幅优化环境进一步提升保护效果。"
}
```

#### GET `/api/AdvancedFeatures/breathability/scores/levels`
获取呼吸性等级说明。

---

### 4. 虚拟加固3D展示接口

#### POST `/api/AdvancedFeatures/reinforcement/simulate`
**请求体：**
```json
{
    "sculptureId": 1,
    "materialName": "TEOS (正硅酸乙酯)",
    "porosity": 0.35,
    "poreRadiusNm": 500.0,
    "applicationTimeSeconds": 3600,
    "coordinateResolutionX": 40,
    "coordinateResolutionY": 60,
    "coordinateResolutionZ": 30,
    "sculptureThicknessCm": 5.0,
    "originalGloss": 30.0,
    "viewMode": "PENETRATION"
}
```

**响应示例：**
```json
{
    "sculptureId": 1,
    "materialName": "TEOS (正硅酸乙酯)",
    "averagePenetrationDepthMm": 3.256,
    "maximumPenetrationDepthMm": 7.842,
    "averageSurfaceGloss": 62.5,
    "glossChangePercent": 108.3,
    "reinforcedVolumePercent": 68.4,
    "voxels": [
        {"x": 0.5, "y": 1.0, "z": 0.1, "concentration": 0.87, "gloss": 75.2, ...},
        ...
    ],
    "enhancementSuggestions": [
        "渗透深度良好，可实现深度加固效果。",
        "警告：光泽度将显著提升 108.3%，可能影响外观原貌。"
    ]
}
```

#### POST `/api/AdvancedFeatures/reinforcement/compare/{sculptureId}`
比较多种材料的加固效果。

#### GET `/api/AdvancedFeatures/reinforcement/materials/visuals`
获取材料视觉属性。

#### POST `/api/AdvancedFeatures/reinforcement/simulate/lightweight`
轻量级模拟（不含体素数据，适合列表展示）。

## 三、消息总线集成

新增消息类型在 [ChannelMessages.cs](file:///d:/SOLO-2/AI_solo_coder_task_B_109/clay_monitor/backend/src/Core/Messages/ChannelMessages.cs) 中定义：

| 消息类型 | 触发时机 | 订阅者 |
|---------|---------|-------|
| `PenetrationPredictionCompleted` | 渗透预测完成 | VirtualReinforcementService |
| `ChemicalReactionWarning` | 检测到有害化学反应风险 | AlertDispatchService |
| `BreathabilityAssessment` | 呼吸性评估结果异常 | AlertDispatchService |
| `VirtualReinforcementApplied` | 虚拟加固模拟完成 | 前端SignalR |

**数据流：**
```
SensorDataReceived 
    → PenetrationPredictionService (4种材料)
    → ChemicalReactionService (高盐时)
    → BreathabilityService (持续收集)
    → VirtualReinforcementService (收到渗透预测后)
    → PenetrationPredictionCompleted
    → VirtualReinforcementApplied
```

## 四、前端组件使用示例

### 1. 渗透深度预测图表
```javascript
import PenetrationDepthChart from './core/PenetrationDepthChart.js';

const chart = new PenetrationDepthChart('chart-container', {
    width: 600,
    height: 400,
    maxDepth: 10
});

fetch('/api/AdvancedFeatures/penetration/compare/1')
    .then(r => r.json())
    .then(data => chart.setData(data));
```

### 2. 化学反应预警组件
```javascript
import ChemicalReactionAlert from './core/ChemicalReactionAlert.js';

const alert = new ChemicalReactionAlert('alert-container', {
    width: 500,
    height: 350
});

fetch('/api/AdvancedFeatures/reaction/evaluate', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({...})
}).then(r => r.json()).then(data => alert.setData(data));
```

### 3. 呼吸性评估图表
```javascript
import BreathabilityAssessmentChart from './core/BreathabilityAssessmentChart.js';

const chart = new BreathabilityAssessmentChart('breath-container', {
    width: 650,
    height: 450
});

chart.setData(breathabilityResult);
```

### 4. 虚拟加固3D展示
```javascript
import VirtualReinforcement3D from './core/VirtualReinforcement3D.js';

const viewer = new VirtualReinforcement3D('3d-container', {
    width: 700,
    height: 500,
    interactive: true
});

viewer.setViewMode('PENETRATION'); // 或 'GLOSS', 'HARDNESS'

fetch('/api/AdvancedFeatures/reinforcement/simulate/lightweight', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({...})
}).then(r => r.json()).then(data => viewer.setData(data));
```

## 五、配置参数说明

新增配置在 [appsettings.json](file:///d:/SOLO-2/AI_solo_coder_task_B_109/clay_monitor/backend/src/appsettings.json) 中：

### PenetrationPrediction
| 参数 | 默认值 | 说明 |
|------|-------|------|
| `DefaultPorosity` | 0.35 | 默认孔隙率 |
| `DefaultPoreRadiusNm` | 500.0 | 默认孔径(nm) |
| `DefaultPredictionTimeSeconds` | 3600 | 默认预测时间(秒) |

### ChemicalReaction
| 参数 | 默认值 | 说明 |
|------|-------|------|
| `SaltConcentrationWarningThreshold` | 300.0 | 盐分警告阈值(ppm) |
| `HarmfulProductThreshold` | 0.15 | 有害产物阈值 |
| `HighTemperatureRiskThreshold` | 30.0 | 高温风险阈值(℃) |

### Breathability
| 参数 | 默认值 | 说明 |
|------|-------|------|
| `MaxDataAgeHours` | 72 | 数据最大有效期(小时) |
| `MinDataPointsForAnalysis` | 10 | 最小分析数据点数 |
| `PoorRegulationThreshold` | 40.0 | 差调节能力阈值 |

### VirtualReinforcement
| 参数 | 默认值 | 说明 |
|------|-------|------|
| `DefaultResolutionX/Y/Z` | 40/60/30 | 3D体素分辨率 |
| `DefaultThicknessCm` | 5.0 | 默认泥塑厚度(cm) |
| `PenetrationDecayFactor` | 0.5 | 深度衰减因子 |

## 六、验证测试步骤

### 环境要求
- .NET 8.0 SDK
- Docker Desktop (可选)

### 后端编译验证
```bash
cd clay_monitor/backend
dotnet restore
dotnet build --configuration Release
dotnet run --configuration Release
```

### 启动后访问
- Swagger文档: http://localhost:5000/swagger
- 健康检查: http://localhost:5000/health
- 指标: http://localhost:5000/metrics

### API手动测试

**测试1: 渗透深度预测**
```bash
curl -X POST http://localhost:5000/api/AdvancedFeatures/penetration/predict \
  -H "Content-Type: application/json" \
  -d '{
    "sculptureId": 1,
    "materialName": "TEOS (正硅酸乙酯)",
    "porosity": 0.35,
    "poreRadiusNm": 500,
    "viscosityPaS": 0.00085,
    "surfaceTensionNm": 0.0235,
    "contactAngleDeg": 95,
    "timeSeconds": 3600,
    "temperatureC": 25
  }'
```

**测试2: 化学反应评估**
```bash
curl -X POST http://localhost:5000/api/AdvancedFeatures/reaction/evaluate \
  -H "Content-Type: application/json" \
  -d '{
    "sculptureId": 1,
    "materialName": "TEOS (正硅酸乙酯)",
    "na2SO4ConcentrationMolL": 0.1,
    "teosConcentrationMolL": 0.5,
    "temperatureC": 30,
    "pH": 8.0,
    "relativeHumidity": 0.7,
    "contactTimeHours": 72
  }'
```

**测试3: 呼吸性分析**
```bash
# 生成测试数据
python3 -c "
import json, random, datetime
data = {'sculptureId': 1, 'temperatures': [], 'humidities': [], 'timestamps': []}
now = datetime.datetime.now()
for i in range(50):
    t = now - datetime.timedelta(minutes=30*i)
    data['temperatures'].append(22 + random.uniform(-2, 2) + 2*((i%24)/24 - 0.5))
    data['humidities'].append(55 + random.uniform(-5, 5) - 2*((i%24)/24 - 0.5))
    data['timestamps'].append(t.isoformat())
print(json.dumps(data))
" > test_breath.json

curl -X POST http://localhost:5000/api/AdvancedFeatures/breathability/analyze \
  -H "Content-Type: application/json" \
  -d @test_breath.json
```

**测试4: 虚拟加固模拟**
```bash
curl -X POST http://localhost:5000/api/AdvancedFeatures/reinforcement/simulate/lightweight \
  -H "Content-Type: application/json" \
  -d '{
    "sculptureId": 1,
    "materialName": "纳米石灰 (Ca(OH)₂)",
    "porosity": 0.35,
    "poreRadiusNm": 500,
    "applicationTimeSeconds": 3600,
    "originalGloss": 30
  }'
```

### Docker部署验证
```bash
cd clay_monitor
docker-compose build
docker-compose up -d

# 检查服务状态
docker-compose ps
docker-compose logs backend
```

## 七、单元测试建议

```csharp
// 测试 Lucas-Washburn 计算
[Fact]
public void CalculateLucasWashburn_ValidInputs_ReturnsCorrectDepth()
{
    var service = new PenetrationPredictionService(_bus, _options);
    double depth = service.CalculateLucasWashburn(
        t: 3600, r: 500e-9, gamma: 0.0235, theta: 95 * Math.PI / 180, 
        eta: 0.00085, phi: 0.35);
    
    Assert.InRange(depth, 5.0, 6.0); // 预期 5-6mm
}

// 测试 Gibbs 自由能计算
[Fact]
public void CalculateGibbsFreeEnergy_SpontaneousReaction_ReturnsNegative()
{
    var service = new ChemicalReactionService(_bus, _options);
    double deltaG = service.CalculateGibbsFreeEnergy(
        deltaH: -125600, deltaS: 85.2, temperatureK: 298.15);
    
    Assert.True(deltaG < 0); // 自发反应
}

// 测试呼吸频率计算
[Fact]
public void CalculateBreathFrequency_SinusoidalData_ReturnsCorrectFrequency()
{
    var service = new BreathabilityService(_bus, _options);
    double[] humidity = Enumerable.Range(0, 100)
        .Select(i => 55 + 10 * Math.Sin(i * Math.PI / 12))
        .ToArray();
    DateTime[] times = Enumerable.Range(0, 100)
        .Select(i => DateTime.Now.AddMinutes(-30 * i))
        .ToArray();
    
    double freq = service.CalculateBreathFrequency(humidity, times);
    
    Assert.InRange(freq, 1.8, 2.2); // 约每天2次
}
```

## 八、与现有系统的集成点

1. **消息总线**：4个新服务均已订阅 `SensorDataReceived` 消息，与现有管线无缝集成
2. **配置系统**：通过 `IOptions<TOptions>` 注入，与现有配置模式一致
3. **依赖注入**：遵循 `AddHostedService` + `AddScoped` 模式，支持API和后台处理
4. **告警系统**：`ChemicalReactionWarning` 和 `BreathabilityAssessment` 可被 `AlertDispatchService` 订阅
5. **前端扩展**：4个新组件与现有 `ClayImage` 和 `AdapRadar` 使用相同的Canvas渲染模式

## 九、新增文件清单

### 后端 (8个新文件)
```
backend/src/
├── Controllers/
│   └── AdvancedFeaturesController.cs
├── PenetrationPrediction/
│   └── PenetrationPredictionService.cs
├── ChemicalReaction/
│   └── ChemicalReactionService.cs
├── Breathability/
│   └── BreathabilityService.cs
└── VirtualReinforcement/
    └── VirtualReinforcementService.cs
```

### 前端 (4个新文件)
```
frontend/src/core/
├── PenetrationDepthChart.js
├── ChemicalReactionAlert.js
├── BreathabilityAssessmentChart.js
└── VirtualReinforcement3D.js
```

### 修改的文件 (5个)
- `Program.cs` - 服务注册和启动banner
- `appsettings.json` - 新增4个配置节
- `Core/Configuration/AppSettings.cs` - 新增4个配置类
- `Core/Messages/ChannelMessages.cs` - 新增4个消息类型
- `ClayMonitor.csproj` - 无需修改（已有全部依赖）

## 十、部署说明

### 使用Docker Compose
```bash
cd clay_monitor
docker-compose up -d --build
```

### 环境变量覆盖
```yaml
services:
  backend:
    environment:
      - PenetrationPrediction__DefaultPorosity=0.38
      - ChemicalReaction__HarmfulProductThreshold=0.12
      - Breathability__PoorRegulationThreshold=45
      - VirtualReinforcement__DefaultResolutionX=60
```

### 健康检查
- 后端健康: `GET /health`
- Swagger文档: `GET /swagger/v1/swagger.json`
- Prometheus指标: `GET /metrics`
