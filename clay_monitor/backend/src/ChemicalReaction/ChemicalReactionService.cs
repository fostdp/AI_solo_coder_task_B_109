using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ClayMonitor.ChemicalReaction;

public record ReactionInput
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public double Na2SO4ConcentrationMolL { get; init; }
    public double TEOSConcentrationMolL { get; init; }
    public double TemperatureC { get; init; } = 25.0;
    public double pH { get; init; } = 7.0;
    public double RelativeHumidity { get; init; } = 0.55;
    public double ContactTimeHours { get; init; } = 24.0;
}

public record ReactionResult
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public string ReactionName { get; init; } = string.Empty;
    public double GibbsFreeEnergyKJmol { get; init; }
    public double EquilibriumConstant { get; init; }
    public double ReactionRateMolLs { get; init; }
    public double ConversionRate { get; init; }
    public double ProductConcentrationMolL { get; init; }
    public double HarmfulProductYield { get; init; }
    public bool IsSpontaneous { get; init; }
    public bool RequiresWarning { get; init; }
    public string WarningLevel { get; init; } = string.Empty;
    public string WarningMessage { get; init; } = string.Empty;
    public string[] HarmfulProducts { get; init; } = Array.Empty<string>();
    public string Recommendation { get; init; } = string.Empty;
    public double ActivationEnergyKJmol { get; init; }
    public double HalfLifeHours { get; init; }
    public DateTime CalculatedAt { get; init; } = DateTime.Now;
}

public record ReactionWarning
{
    public int SculptureId { get; init; }
    public string AlertId { get; init; } = Guid.NewGuid().ToString("N");
    public string WarningType { get; init; } = string.Empty;
    public string WarningLevel { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public double CurrentValue { get; init; }
    public double Threshold { get; init; }
    public string[] InvolvedChemicals { get; init; } = Array.Empty<string>();
    public DateTime TriggeredAt { get; init; } = DateTime.Now;
}

public record ThermodynamicLookupKey : IEquatable<ThermodynamicLookupKey>
{
    public string ReactionKey { get; init; } = string.Empty;
    public int TemperatureC { get; init; }
    public int PHx10 { get; init; }
    public int HumidityPct { get; init; }

    public bool Equals(ThermodynamicLookupKey? other)
    {
        if (other is null) return false;
        return ReactionKey == other.ReactionKey
               && TemperatureC == other.TemperatureC
               && PHx10 == other.PHx10
               && HumidityPct == other.HumidityPct;
    }

    public override bool Equals(object? obj) => Equals(obj as ThermodynamicLookupKey);

    public override int GetHashCode()
    {
        return HashCode.Combine(ReactionKey, TemperatureC, PHx10, HumidityPct);
    }
}

public record ThermodynamicCachedValues
{
    public double DeltaGkJmol { get; init; }
    public double EquilibriumConstant { get; init; }
    public double RateConstant { get; init; }
}

public interface IChemicalReactionService
{
    Task<ReactionResult> EvaluateReactionAsync(ReactionInput input, CancellationToken ct = default);
    Task<ReactionResult[]> EvaluateAllMaterialsAsync(
        int sculptureId,
        double na2so4Concentration,
        double temperatureC,
        CancellationToken ct = default);
    double CalculateGibbsFreeEnergy(double deltaH, double deltaS, double temperatureK);
    double CalculateArrheniusRate(double A, double Ea, double temperatureK);
    double CalculateReactionQuotient(double[] products, double[] reactants);
    bool TryGetCachedThermodynamics(ThermodynamicLookupKey key, out ThermodynamicCachedValues values);
    int GetCacheHitCount();
    int GetCacheSize();
}

public class ChemicalReactionService : BackgroundService, IChemicalReactionService
{
    private readonly IMessageBus _bus;
    private readonly ChemicalReactionOptions _options;
    private readonly Dictionary<ThermodynamicLookupKey, ThermodynamicCachedValues> _thermoCache;
    private int _cacheHitCount;

    private static readonly Dictionary<string, ChemicalReactionModel> ReactionDatabase = new()
    {
        ["TEOS_Na2SO4"] = new ChemicalReactionModel
        {
            Name = "TEOS 与硫酸钠反应生成硅酸钠",
            Reactants = new[] { "Si(OC2H5)4", "Na2SO4", "H2O" },
            Products = new[] { "Na2SiO3", "C2H5OH", "H2SO4" },
            HarmfulProducts = new[] { "Na2SiO3 (硅酸钠)", "H2SO4 (硫酸)" },
            DeltaHkJmol = -125.6,
            DeltaSJmolK = 85.2,
            ActivationEnergyKJmol = 68.5,
            PreExponentialFactor = 1.25e6,
            ReactionOrder = 2.0,
            Stoichiometry = new Dictionary<string, double>
            {
                ["Si(OC2H5)4"] = 1,
                ["Na2SO4"] = 1,
                ["H2O"] = 2,
                ["Na2SiO3"] = 1,
                ["C2H5OH"] = 4,
                ["H2SO4"] = 1
            }
        },
        ["TEOS_NaCl"] = new ChemicalReactionModel
        {
            Name = "TEOS 与氯化钠反应",
            Reactants = new[] { "Si(OC2H5)4", "NaCl", "H2O" },
            Products = new[] { "Na4SiO4", "C2H5OH", "HCl" },
            HarmfulProducts = new[] { "HCl (盐酸)" },
            DeltaHkJmol = -98.3,
            DeltaSJmolK = 72.8,
            ActivationEnergyKJmol = 75.2,
            PreExponentialFactor = 8.9e5,
            ReactionOrder = 2.0,
            Stoichiometry = new Dictionary<string, double>()
        },
        ["Ca(OH)2_Na2SO4"] = new ChemicalReactionModel
        {
            Name = "纳米石灰与硫酸钠反应生成石膏",
            Reactants = new[] { "Ca(OH)2", "Na2SO4" },
            Products = new[] { "CaSO4·2H2O", "NaOH" },
            HarmfulProducts = new[] { "NaOH (氢氧化钠)" },
            DeltaHkJmol = -15.8,
            DeltaSJmolK = 52.3,
            ActivationEnergyKJmol = 45.6,
            PreExponentialFactor = 3.2e4,
            ReactionOrder = 1.5,
            Stoichiometry = new Dictionary<string, double>()
        },
        ["Acrylic_Na2SO4"] = new ChemicalReactionModel
        {
            Name = "丙烯酸树脂与硫酸钠相互作用",
            Reactants = new[] { "C5H8O2", "Na2SO4" },
            Products = new[] { "Complex", "Na+" },
            HarmfulProducts = new[] { "络合物可能加速老化" },
            DeltaHkJmol = -45.2,
            DeltaSJmolK = 35.6,
            ActivationEnergyKJmol = 95.0,
            PreExponentialFactor = 1.5e7,
            ReactionOrder = 1.0,
            Stoichiometry = new Dictionary<string, double>()
        }
    };

    private static readonly Dictionary<string, string> MaterialReactionMap = new()
    {
        ["TEOS (正硅酸乙酯)"] = "TEOS_Na2SO4",
        ["纳米石灰 (Ca(OH)₂)"] = "Ca(OH)2_Na2SO4",
        ["丙烯酸树脂 (Paraloid B72)"] = "Acrylic_Na2SO4",
        ["硅丙乳液"] = "Acrylic_Na2SO4"
    };

    public ChemicalReactionService(IMessageBus bus, IOptions<ChemicalReactionOptions> options)
    {
        _bus = bus;
        _options = options.Value;
        _thermoCache = new Dictionary<ThermodynamicLookupKey, ThermodynamicCachedValues>();
        _cacheHitCount = 0;
        PrecomputeThermodynamicCache();
    }

    private void PrecomputeThermodynamicCache()
    {
        double[] temperaturesC = { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50 };
        double[] phValues = { 5.0, 5.5, 6.0, 6.5, 7.0, 7.5, 8.0, 8.5, 9.0 };
        double[] humidities = { 0.3, 0.4, 0.5, 0.55, 0.6, 0.7, 0.8, 0.9 };
        double R = 8.314;

        foreach (var reactionKvp in ReactionDatabase)
        {
            string reactionKey = reactionKvp.Key;
            var reaction = reactionKvp.Value;

            foreach (double tC in temperaturesC)
            {
                double T = tC + 273.15;
                double deltaG = CalculateGibbsFreeEnergy(
                    reaction.DeltaHkJmol * 1000, reaction.DeltaSJmolK, T) / 1000.0;
                double K = Math.Exp(-deltaG * 1000 / (R * T));
                double baseRate = CalculateArrheniusRate(
                    reaction.PreExponentialFactor, reaction.ActivationEnergyKJmol * 1000, T);

                foreach (double ph in phValues)
                {
                    double phFactor = 1.0 + 0.5 * Math.Abs(ph - 7.0);

                    foreach (double rh in humidities)
                    {
                        double humidityFactor = 1.0 + 2.0 * Math.Max(0, rh - 0.6);
                        double adjustedRate = baseRate * phFactor * humidityFactor;

                        var key = new ThermodynamicLookupKey
                        {
                            ReactionKey = reactionKey,
                            TemperatureC = (int)Math.Round(tC),
                            PHx10 = (int)Math.Round(ph * 10),
                            HumidityPct = (int)Math.Round(rh * 100)
                        };

                        _thermoCache[key] = new ThermodynamicCachedValues
                        {
                            DeltaGkJmol = deltaG,
                            EquilibriumConstant = K,
                            RateConstant = adjustedRate
                        };
                    }
                }
            }
        }
    }

    public bool TryGetCachedThermodynamics(ThermodynamicLookupKey key, out ThermodynamicCachedValues values)
    {
        bool hit = _thermoCache.TryGetValue(key, out values!);
        if (hit) Interlocked.Increment(ref _cacheHitCount);
        return hit;
    }

    public int GetCacheHitCount() => _cacheHitCount;
    public int GetCacheSize() => _thermoCache.Count;

    private ThermodynamicCachedValues GetOrComputeThermodynamics(
        string reactionKey, ChemicalReactionModel reaction, double temperatureC, double pH, double rh)
    {
        var lookupKey = new ThermodynamicLookupKey
        {
            ReactionKey = reactionKey,
            TemperatureC = (int)Math.Round(Math.Clamp(temperatureC, 0, 50) / 5) * 5,
            PHx10 = (int)Math.Round(Math.Clamp(pH, 5.0, 9.0) * 2) * 5,
            HumidityPct = (int)Math.Round(Math.Clamp(rh, 0.3, 0.9) * 10) * 10
        };

        if (_thermoCache.TryGetValue(lookupKey, out var cached))
        {
            Interlocked.Increment(ref _cacheHitCount);
            return cached;
        }

        double R = 8.314;
        double T = temperatureC + 273.15;
        double deltaG = CalculateGibbsFreeEnergy(
            reaction.DeltaHkJmol * 1000, reaction.DeltaSJmolK, T) / 1000.0;
        double K = Math.Exp(-deltaG * 1000 / (R * T));
        double baseRate = CalculateArrheniusRate(
            reaction.PreExponentialFactor, reaction.ActivationEnergyKJmol * 1000, T);
        double phFactor = 1.0 + 0.5 * Math.Abs(pH - 7.0);
        double humidityFactor = 1.0 + 2.0 * Math.Max(0, rh - 0.6);

        return new ThermodynamicCachedValues
        {
            DeltaGkJmol = deltaG,
            EquilibriumConstant = K,
            RateConstant = baseRate * phFactor * humidityFactor
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sensorTask = ProcessSensorDataAsync(stoppingToken);
        var migrationTask = ProcessMigrationResultAsync(stoppingToken);

        await Task.WhenAll(sensorTask, migrationTask);
    }

    private async Task ProcessSensorDataAsync(CancellationToken stoppingToken)
    {
        await foreach (var sensorData in _bus.SubscribeAsync<SensorDataReceived>(stoppingToken))
        {
            try
            {
                if (sensorData.NaConcentration > _options.SaltConcentrationWarningThreshold)
                {
                    double na2so4Conc = (sensorData.NaConcentration ?? 100) / 142.04 / 10.0;

                    var results = await EvaluateAllMaterialsAsync(
                        sensorData.SculptureId,
                        na2so4Conc,
                        sensorData.Temperature ?? 25.0,
                        stoppingToken);

                    foreach (var result in results)
                    {
                        await _bus.PublishAsync(result, stoppingToken);

                        if (result.RequiresWarning)
                        {
                            var warning = new ReactionWarning
                            {
                                SculptureId = result.SculptureId,
                                WarningType = $"CHEMICAL_{result.WarningLevel}",
                                WarningLevel = result.WarningLevel,
                                Message = result.WarningMessage,
                                CurrentValue = Math.Round(result.HarmfulProductYield * 100, 2),
                                Threshold = _options.HarmfulProductThreshold * 100,
                                InvolvedChemicals = result.HarmfulProducts
                            };

                            await _bus.PublishAsync(warning, stoppingToken);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }

    private async Task ProcessMigrationResultAsync(CancellationToken stoppingToken)
    {
        await foreach (var migration in _bus.SubscribeAsync<SaltMigrationCompleted>(stoppingToken))
        {
            try
            {
                if (migration.MaxConcentration > _options.SaltConcentrationWarningThreshold)
                {
                    double na2so4Conc = migration.MaxConcentration / 142.04 / 10.0;

                    var results = await EvaluateAllMaterialsAsync(
                        migration.SculptureId,
                        na2so4Conc,
                        25.0,
                        stoppingToken);

                    foreach (var result in results.Where(r => r.RequiresWarning))
                    {
                        var warning = new ReactionWarning
                        {
                            SculptureId = result.SculptureId,
                            WarningType = "CHEMICAL_MIGRATION",
                            WarningLevel = result.WarningLevel,
                            Message = $"盐分迁移预测检测到高浓度区域，{result.WarningMessage}",
                            CurrentValue = Math.Round(result.HarmfulProductYield * 100, 2),
                            Threshold = _options.HarmfulProductThreshold * 100,
                            InvolvedChemicals = result.HarmfulProducts
                        };

                        await _bus.PublishAsync(warning, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }

    public async Task<ReactionResult> EvaluateReactionAsync(ReactionInput input, CancellationToken ct = default)
    {
        if (!MaterialReactionMap.TryGetValue(input.MaterialName, out var reactionKey) ||
            !ReactionDatabase.TryGetValue(reactionKey, out var reaction))
        {
            return await Task.FromResult(new ReactionResult
            {
                SculptureId = input.SculptureId,
                MaterialName = input.MaterialName,
                ReactionName = "未知反应体系",
                RequiresWarning = false,
                WarningLevel = "NONE",
                WarningMessage = "无可用反应数据",
                Recommendation = "建议进行实验室兼容性测试",
                CalculatedAt = DateTime.Now
            });
        }

        var thermo = GetOrComputeThermodynamics(reactionKey, reaction, input.TemperatureC, input.pH, input.RelativeHumidity);

        double deltaG = thermo.DeltaGkJmol;
        double K = thermo.EquilibriumConstant;
        double adjustedRate = thermo.RateConstant;
        bool isSpontaneous = deltaG < 0;

        double reactionRate = adjustedRate *
            Math.Pow(input.TEOSConcentrationMolL, reaction.ReactionOrder) *
            Math.Pow(input.Na2SO4ConcentrationMolL, reaction.ReactionOrder);

        double[] reactantConc = { input.Na2SO4ConcentrationMolL, input.TEOSConcentrationMolL };
        double Q = CalculateReactionQuotient(
            new[] { 0.01 },
            reactantConc);

        double timeHours = input.ContactTimeHours;
        double kEff = adjustedRate * 3600;
        double conversionRate = 1.0 - Math.Exp(-kEff * timeHours *
            Math.Pow(input.Na2SO4ConcentrationMolL, reaction.ReactionOrder - 1));
        conversionRate = Math.Min(1.0, Math.Max(0.0, conversionRate * (K / (K + 1))));

        double productConc = input.TEOSConcentrationMolL * conversionRate;
        double harmfulYield = productConc / Math.Max(input.TEOSConcentrationMolL, 0.001);

        double tHalfHours = Math.Log(2) / Math.Max(kEff, 1e-10) / 3600.0;

        string warningLevel = ClassifyRisk(harmfulYield, deltaG, input);
        bool requiresWarning = warningLevel != "NONE";
        string warningMessage = GenerateWarningMessage(warningLevel, reaction, harmfulYield, deltaG);
        string recommendation = GenerateRecommendation(warningLevel, input.MaterialName, harmfulYield);

        return await Task.FromResult(new ReactionResult
        {
            SculptureId = input.SculptureId,
            MaterialName = input.MaterialName,
            ReactionName = reaction.Name,
            GibbsFreeEnergyKJmol = Math.Round(deltaG, 2),
            EquilibriumConstant = Math.Round(K, 4),
            ReactionRateMolLs = Math.Round(reactionRate, 10),
            ConversionRate = Math.Round(conversionRate * 100, 2),
            ProductConcentrationMolL = Math.Round(productConc, 6),
            HarmfulProductYield = Math.Round(harmfulYield, 4),
            IsSpontaneous = isSpontaneous,
            RequiresWarning = requiresWarning,
            WarningLevel = warningLevel,
            WarningMessage = warningMessage,
            HarmfulProducts = reaction.HarmfulProducts,
            Recommendation = recommendation,
            ActivationEnergyKJmol = reaction.ActivationEnergyKJmol,
            HalfLifeHours = Math.Round(tHalfHours, 2),
            CalculatedAt = DateTime.Now
        });
    }

    public async Task<ReactionResult[]> EvaluateAllMaterialsAsync(
        int sculptureId,
        double na2so4Concentration,
        double temperatureC,
        CancellationToken ct = default)
    {
        var results = new List<ReactionResult>();
        var materials = MaterialReactionMap.Keys.ToArray();

        foreach (var material in materials)
        {
            var input = new ReactionInput
            {
                SculptureId = sculptureId,
                MaterialName = material,
                Na2SO4ConcentrationMolL = na2so4Concentration,
                TEOSConcentrationMolL = 0.5,
                TemperatureC = temperatureC,
                pH = 7.5,
                RelativeHumidity = 0.6,
                ContactTimeHours = 72.0
            };

            results.Add(await EvaluateReactionAsync(input, ct));
        }

        return results.ToArray();
    }

    public double CalculateGibbsFreeEnergy(double deltaH, double deltaS, double temperatureK)
    {
        return deltaH - temperatureK * deltaS;
    }

    public double CalculateArrheniusRate(double A, double Ea, double temperatureK)
    {
        double R = 8.314;
        return A * Math.Exp(-Ea / (R * temperatureK));
    }

    public double CalculateReactionQuotient(double[] products, double[] reactants)
    {
        double productTerm = products.Aggregate(1.0, (acc, p) => acc * Math.Max(p, 1e-9));
        double reactantTerm = reactants.Aggregate(1.0, (acc, r) => acc * Math.Max(r, 1e-9));
        return productTerm / Math.Max(reactantTerm, 1e-9);
    }

    private string ClassifyRisk(double harmfulYield, double deltaG, ReactionInput input)
    {
        double threshold = _options.HarmfulProductThreshold;
        double phRisk = input.pH < 6.0 || input.pH > 8.5 ? 1.5 : 1.0;
        double tempRisk = input.TemperatureC > 30.0 ? 1.3 : 1.0;
        double adjustedThreshold = threshold / (phRisk * tempRisk);

        if (harmfulYield >= adjustedThreshold * 2.0 && deltaG < -50)
            return "CRITICAL";
        if (harmfulYield >= adjustedThreshold && deltaG < -20)
            return "WARNING";
        if (harmfulYield >= adjustedThreshold * 0.5 || deltaG < -10)
            return "CAUTION";
        return "NONE";
    }

    private string GenerateWarningMessage(string level, ChemicalReactionModel reaction, double yield, double deltaG)
    {
        string productList = string.Join("、", reaction.HarmfulProducts);

        return level switch
        {
            "CRITICAL" => $"检测到剧烈化学反应风险！ΔG = {deltaG:F1} kJ/mol，" +
                         $"有害产物({productList})生成率 {yield * 100:F1}%，" +
                         $"可能严重损害彩绘层结构。",
            "WARNING" => $"检测到化学反应风险。ΔG = {deltaG:F1} kJ/mol，" +
                         $"有害产物({productList})生成率 {yield * 100:F1}%，" +
                         $"建议评估材料兼容性后使用。",
            "CAUTION" => $"存在潜在化学反应风险。ΔG = {deltaG:F1} kJ/mol，" +
                         $"有害产物({productList})生成率 {yield * 100:F1}%，" +
                         $"建议先进行小范围试验。",
            _ => $"化学反应活性较低。ΔG = {deltaG:F1} kJ/mol，" +
                 $"有害产物生成率 {yield * 100:F1}%，在正常条件下相对安全。"
        };
    }

    private string GenerateRecommendation(string level, string material, double yield)
    {
        return level switch
        {
            "CRITICAL" => $"【禁止使用】{material} 在当前盐分条件下会剧烈反应，" +
                         $"有害产物生成率 {yield * 100:F1}%！必须更换加固材料。",
            "WARNING" => $"【谨慎使用】{material} 在当前盐分条件下可能发生不良反应。" +
                         $"建议先进行脱盐处理，或更换其他材料。",
            "CAUTION" => $"【小试先行】{material} 存在潜在反应风险。" +
                         $"建议先在隐蔽部位进行小范围试验，观察72小时无异常后再使用。",
            _ => $"【推荐使用】{material} 在当前条件下化学稳定性良好，" +
                 $"有害产物生成率仅 {yield * 100:F1}%，可安全使用。"
        };
    }

    public static bool TryGetReactionModel(string reactionKey, out ChemicalReactionModel model)
    {
        return ReactionDatabase.TryGetValue(reactionKey, out model!);
    }
}

public class ChemicalReactionModel
{
    public string Name { get; set; } = string.Empty;
    public string[] Reactants { get; set; } = Array.Empty<string>();
    public string[] Products { get; set; } = Array.Empty<string>();
    public string[] HarmfulProducts { get; set; } = Array.Empty<string>();
    public double DeltaHkJmol { get; set; }
    public double DeltaSJmolK { get; set; }
    public double ActivationEnergyKJmol { get; set; }
    public double PreExponentialFactor { get; set; }
    public double ReactionOrder { get; set; }
    public Dictionary<string, double> Stoichiometry { get; set; } = new();
}
