using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ClayMonitor.MaterialScore;

public record MaterialProperties
{
    public string Name { get; init; } = string.Empty;
    public double ContactAngle { get; init; }
    public double PenetrationDepth { get; init; }
    public double StrengthMatch { get; init; }
    public double WeatherResistance { get; init; }
    public double Reversibility { get; init; }
    public double CostPerformance { get; init; }
}

public record SculptureCondition
{
    public double SaltConcentration { get; init; }
    public double SurfaceCoverage { get; init; }
    public double Porosity { get; init; }
    public double Hardness { get; init; }
    public double MoistureContent { get; init; }
    public string[] DegradationTypes { get; init; } = Array.Empty<string>();
}

public interface IMaterialScoreService
{
    Task<MaterialScoreCalculated> CalculateScoreAsync(
        int sculptureId,
        MaterialProperties material,
        SculptureCondition condition,
        CancellationToken ct = default);

    Task<MaterialScoreCalculated[]> RankMaterialsAsync(
        int sculptureId,
        MaterialProperties[] materials,
        SculptureCondition condition,
        CancellationToken ct = default);
}

public class MaterialScoreService : BackgroundService, IMaterialScoreService
{
    private readonly IMessageBus _bus;
    private readonly MaterialScoreOptions _options;

    private static readonly MaterialProperties[] DefaultMaterials = new[]
    {
        new MaterialProperties
        {
            Name = "TEOS (正硅酸乙酯)",
            ContactAngle = 95,
            PenetrationDepth = 92,
            StrengthMatch = 88,
            WeatherResistance = 90,
            Reversibility = 65,
            CostPerformance = 70
        },
        new MaterialProperties
        {
            Name = "纳米石灰 (Ca(OH)₂)",
            ContactAngle = 75,
            PenetrationDepth = 95,
            StrengthMatch = 92,
            WeatherResistance = 85,
            Reversibility = 90,
            CostPerformance = 85
        },
        new MaterialProperties
        {
            Name = "丙烯酸树脂 (Paraloid B72)",
            ContactAngle = 90,
            PenetrationDepth = 75,
            StrengthMatch = 80,
            WeatherResistance = 88,
            Reversibility = 70,
            CostPerformance = 92
        },
        new MaterialProperties
        {
            Name = "硅丙乳液",
            ContactAngle = 85,
            PenetrationDepth = 82,
            StrengthMatch = 85,
            WeatherResistance = 92,
            Reversibility = 60,
            CostPerformance = 88
        }
    };

    public MaterialScoreService(IMessageBus bus, IOptions<MaterialScoreOptions> options)
    {
        _bus = bus;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var migrationResult in _bus.SubscribeAsync<SaltMigrationCompleted>(stoppingToken))
        {
            try
            {
                var condition = new SculptureCondition
                {
                    SaltConcentration = migrationResult.AverageConcentration,
                    SurfaceCoverage = migrationResult.AverageConcentration > 300 ? 40 : 20,
                    Porosity = 0.35,
                    Hardness = 70,
                    MoistureContent = 0.25
                };

                var results = await RankMaterialsAsync(
                    migrationResult.SculptureId,
                    DefaultMaterials,
                    condition,
                    stoppingToken);

                foreach (var result in results)
                {
                    await _bus.PublishAsync(result, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }

    public async Task<MaterialScoreCalculated> CalculateScoreAsync(
        int sculptureId,
        MaterialProperties material,
        SculptureCondition condition,
        CancellationToken ct = default)
    {
        double[] weights =
        {
            _options.ContactAngleWeight,
            _options.PenetrationDepthWeight,
            _options.StrengthMatchWeight,
            _options.WeatherResistanceWeight,
            _options.ReversibilityWeight,
            _options.CostPerformanceWeight
        };

        double[] rawScores =
        {
            AdjustByCondition(material.ContactAngle, condition, "ContactAngle"),
            AdjustByCondition(material.PenetrationDepth, condition, "PenetrationDepth"),
            AdjustByCondition(material.StrengthMatch, condition, "StrengthMatch"),
            AdjustByCondition(material.WeatherResistance, condition, "WeatherResistance"),
            AdjustByCondition(material.Reversibility, condition, "Reversibility"),
            AdjustByCondition(material.CostPerformance, condition, "CostPerformance")
        };

        double totalScore = 0;
        for (int i = 0; i < 6; i++)
        {
            totalScore += weights[i] * rawScores[i];
        }

        string recommendation = GenerateRecommendation(totalScore, material, condition);

        return await Task.FromResult(new MaterialScoreCalculated
        {
            SculptureId = sculptureId,
            MaterialName = material.Name,
            TotalScore = Math.Round(totalScore, 2),
            ContactAngle = Math.Round(rawScores[0], 2),
            PenetrationDepth = Math.Round(rawScores[1], 2),
            StrengthMatch = Math.Round(rawScores[2], 2),
            WeatherResistance = Math.Round(rawScores[3], 2),
            Reversibility = Math.Round(rawScores[4], 2),
            CostPerformance = Math.Round(rawScores[5], 2),
            Recommendation = recommendation,
            CalculatedAt = DateTime.Now
        });
    }

    public async Task<MaterialScoreCalculated[]> RankMaterialsAsync(
        int sculptureId,
        MaterialProperties[] materials,
        SculptureCondition condition,
        CancellationToken ct = default)
    {
        var results = new List<MaterialScoreCalculated>();

        foreach (var material in materials)
        {
            var score = await CalculateScoreAsync(sculptureId, material, condition, ct);
            results.Add(score);
        }

        return results.OrderByDescending(s => s.TotalScore).ToArray();
    }

    private double AdjustByCondition(double baseScore, SculptureCondition condition, string dimension)
    {
        double factor = 1.0;

        switch (dimension)
        {
            case "PenetrationDepth":
                if (condition.Porosity < 0.25) factor *= 0.85;
                if (condition.SaltConcentration > 300) factor *= 1.15;
                break;

            case "StrengthMatch":
                if (condition.Hardness < 50) factor *= 1.1;
                if (condition.DegradationTypes.Contains("粉化")) factor *= 1.2;
                break;

            case "WeatherResistance":
                if (condition.MoistureContent > 0.3) factor *= 0.9;
                if (condition.SurfaceCoverage > 30) factor *= 1.15;
                break;

            case "Reversibility":
                if (condition.DegradationTypes.Contains("开裂")) factor *= 1.2;
                break;
        }

        return Math.Clamp(baseScore * factor, 0, 100);
    }

    private string GenerateRecommendation(double totalScore, MaterialProperties material, SculptureCondition condition)
    {
        if (totalScore >= _options.ExcellentThreshold)
        {
            return $"【优先推荐】{material.Name} 综合适配度优秀（{totalScore:F1}分），"
                 + $"适用于当前盐分浓度 {condition.SaltConcentration:F0}ppm 的修复场景。";
        }
        else if (totalScore >= _options.GoodThreshold)
        {
            return $"【推荐】{material.Name} 综合适配度良好（{totalScore:F1}分），"
                 + $"建议配合脱盐预处理使用。";
        }
        else if (totalScore >= _options.FairThreshold)
        {
            return $"【备选】{material.Name} 综合适配度一般（{totalScore:F1}分），"
                 + $"需评估长期稳定性后使用。";
        }
        else
        {
            return $"【不推荐】{material.Name} 综合适配度较低（{totalScore:F1}分），"
                 + $"不建议在当前病害条件下使用。";
        }
    }
}
