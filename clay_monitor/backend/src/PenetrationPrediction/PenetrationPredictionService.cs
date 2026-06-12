using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ClayMonitor.PenetrationPrediction;

public record SculptureLayer
{
    public string LayerName { get; init; } = string.Empty;
    public double ThicknessMm { get; init; }
    public double Porosity { get; init; }
    public double PoreRadiusNm { get; init; }
    public double Tortuosity { get; init; } = 1.5;
}

public record PenetrationInput
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public double Porosity { get; init; }
    public double PoreRadiusNm { get; init; }
    public double ViscosityPaS { get; init; }
    public double SurfaceTensionNm { get; init; }
    public double ContactAngleDeg { get; init; }
    public double TimeSeconds { get; init; }
    public double TemperatureC { get; init; } = 25.0;
    public SculptureLayer[]? Layers { get; init; }
    public bool UseLayeredModel { get; init; } = false;
}

public record LayerPenetrationInfo
{
    public string LayerName { get; init; } = string.Empty;
    public double DepthInLayerMm { get; init; }
    public double TimeToTraverseSeconds { get; init; }
    public double LayerPorosity { get; init; }
    public double LayerPoreRadiusNm { get; init; }
    public bool FullyPenetrated { get; init; }
}

public record PenetrationResult
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public double PredictedDepthMm { get; init; }
    public double PenetrationRateMmPerS { get; init; }
    public double TimeToReach5mm { get; init; }
    public double[] DepthProfile { get; init; } = Array.Empty<double>();
    public double[] TimePoints { get; init; } = Array.Empty<double>();
    public string PenetrationGrade { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public double CapillaryPressurePa { get; init; }
    public double EffectiveDiffusivity { get; init; }
    public DateTime CalculatedAt { get; init; } = DateTime.Now;
    public bool UsedLayeredModel { get; init; }
    public LayerPenetrationInfo[] LayerBreakdown { get; init; } = Array.Empty<LayerPenetrationInfo>();
}

public interface IPenetrationPredictionService
{
    Task<PenetrationResult> PredictAsync(PenetrationInput input, CancellationToken ct = default);
    Task<PenetrationResult[]> PredictBatchAsync(PenetrationInput[] inputs, CancellationToken ct = default);
    double CalculateLucasWashburn(double t, double r, double gamma, double theta, double eta, double phi);
    double CalculateLayeredLucasWashburn(double t, double gamma, double theta, double eta, SculptureLayer[] layers);
    double CalculateCapillaryPressure(double r, double gamma, double theta);
    SculptureLayer[] GetDefaultClayLayers();
}

public class PenetrationPredictionService : BackgroundService, IPenetrationPredictionService
{
    private readonly IMessageBus _bus;
    private readonly PenetrationPredictionOptions _options;

    private static readonly Dictionary<string, MaterialProperties> MaterialDatabase = new()
    {
        ["TEOS (正硅酸乙酯)"] = new MaterialProperties
        {
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            MolarMass = 208.33,
            Density = 0.933,
            TypicalContactAngle = 95
        },
        ["纳米石灰 (Ca(OH)₂)"] = new MaterialProperties
        {
            ViscosityPaS = 0.0012,
            SurfaceTensionNm = 0.045,
            MolarMass = 74.09,
            Density = 2.24,
            TypicalContactAngle = 75
        },
        ["丙烯酸树脂 (Paraloid B72)"] = new MaterialProperties
        {
            ViscosityPaS = 0.05,
            SurfaceTensionNm = 0.032,
            MolarMass = 100000,
            Density = 1.05,
            TypicalContactAngle = 90
        },
        ["硅丙乳液"] = new MaterialProperties
        {
            ViscosityPaS = 0.008,
            SurfaceTensionNm = 0.038,
            MolarMass = 50000,
            Density = 1.02,
            TypicalContactAngle = 85
        }
    };

    public PenetrationPredictionService(IMessageBus bus, IOptions<PenetrationPredictionOptions> options)
    {
        _bus = bus;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sensorTask = ProcessSensorDataAsync(stoppingToken);
        var scoreTask = ProcessMaterialScoreAsync(stoppingToken);

        await Task.WhenAll(sensorTask, scoreTask);
    }

    private async Task ProcessSensorDataAsync(CancellationToken stoppingToken)
    {
        await foreach (var sensorData in _bus.SubscribeAsync<SensorDataReceived>(stoppingToken))
        {
            try
            {
                var materials = MaterialDatabase.Keys.ToArray();
                foreach (var material in materials)
                {
                    var input = new PenetrationInput
                    {
                        SculptureId = sensorData.SculptureId,
                        MaterialName = material,
                        Porosity = _options.DefaultPorosity,
                        PoreRadiusNm = _options.DefaultPoreRadiusNm,
                        ViscosityPaS = MaterialDatabase[material].ViscosityPaS,
                        SurfaceTensionNm = MaterialDatabase[material].SurfaceTensionNm,
                        ContactAngleDeg = MaterialDatabase[material].TypicalContactAngle,
                        TimeSeconds = _options.DefaultPredictionTimeSeconds,
                        TemperatureC = sensorData.Temperature ?? 25.0
                    };

                    var result = await PredictAsync(input, stoppingToken);
                    await _bus.PublishAsync(result, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }

    private async Task ProcessMaterialScoreAsync(CancellationToken stoppingToken)
    {
        await foreach (var score in _bus.SubscribeAsync<MaterialScoreCalculated>(stoppingToken))
        {
            try
            {
                if (MaterialDatabase.TryGetValue(score.MaterialName, out var mat))
                {
                    var input = new PenetrationInput
                    {
                        SculptureId = score.SculptureId,
                        MaterialName = score.MaterialName,
                        Porosity = _options.DefaultPorosity,
                        PoreRadiusNm = _options.DefaultPoreRadiusNm,
                        ViscosityPaS = mat.ViscosityPaS,
                        SurfaceTensionNm = mat.SurfaceTensionNm,
                        ContactAngleDeg = score.ContactAngle,
                        TimeSeconds = _options.DefaultPredictionTimeSeconds,
                        TemperatureC = 25.0
                    };

                    var result = await PredictAsync(input, stoppingToken);
                    await _bus.PublishAsync(new PenetrationPredictionCompleted
                    {
                        SculptureId = result.SculptureId,
                        MaterialName = result.MaterialName,
                        PredictedDepthMm = result.PredictedDepthMm,
                        PenetrationRate = result.PenetrationRateMmPerS,
                        CapillaryPressurePa = result.CapillaryPressurePa,
                        PenetrationGrade = result.PenetrationGrade
                    }, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }

    public async Task<PenetrationResult> PredictAsync(PenetrationInput input, CancellationToken ct = default)
    {
        double gamma = input.SurfaceTensionNm;
        double theta = input.ContactAngleDeg * Math.PI / 180.0;
        double eta = input.ViscosityPaS;
        double t = input.TimeSeconds;

        double temperatureFactor = 1.0 + 0.02 * (input.TemperatureC - 25.0);
        eta /= temperatureFactor;

        double phi = input.Porosity;
        double r = input.PoreRadiusNm * 1e-9;
        double Pc = CalculateCapillaryPressure(r, gamma, theta);
        double Deff = CalculateEffectiveDiffusivity(phi, r, gamma, theta, eta);

        bool useLayered = input.UseLayeredModel && input.Layers != null && input.Layers.Length > 0;
        SculptureLayer[] layers = useLayered ? input.Layers! : GetDefaultClayLayers();

        double depth;
        LayerPenetrationInfo[] layerBreakdown;
        double[] depthProfile;
        double[] timePoints;

        if (useLayered)
        {
            depth = CalculateLayeredLucasWashburn(t, gamma, theta, eta, layers);
            layerBreakdown = CalculateLayerBreakdown(t, gamma, theta, eta, layers);
            (depthProfile, timePoints) = GenerateLayeredDepthProfile(t, gamma, theta, eta, layers);
        }
        else
        {
            depth = CalculateLucasWashburn(t, r, gamma, theta, eta, phi);
            layerBreakdown = Array.Empty<LayerPenetrationInfo>();

            int timeSteps = 50;
            timePoints = new double[timeSteps];
            depthProfile = new double[timeSteps];
            double timeTo5mm = CalculateTimeToTargetDepth(5.0, r, gamma, theta, eta, phi);
            double maxTime = Math.Max(t, timeTo5mm * 1.5);

            for (int i = 0; i < timeSteps; i++)
            {
                timePoints[i] = (i + 1) * maxTime / timeSteps;
                depthProfile[i] = CalculateLucasWashburn(timePoints[i], r, gamma, theta, eta, phi);
            }
        }

        double penetrationRate = t > 0 ? depth / t : 0;
        double timeTo5mmUniform = useLayered
            ? CalculateLayeredTimeToTargetDepth(5.0, gamma, theta, eta, layers)
            : CalculateTimeToTargetDepth(5.0, r, gamma, theta, eta, phi);

        string grade = ClassifyPenetration(depth);
        string recommendation = GenerateRecommendation(depth, input.MaterialName, phi);

        return await Task.FromResult(new PenetrationResult
        {
            SculptureId = input.SculptureId,
            MaterialName = input.MaterialName,
            PredictedDepthMm = Math.Round(depth, 4),
            PenetrationRateMmPerS = Math.Round(penetrationRate, 6),
            TimeToReach5mm = Math.Round(timeTo5mmUniform, 2),
            DepthProfile = depthProfile.Select(x => Math.Round(x, 4)).ToArray(),
            TimePoints = timePoints.Select(x => Math.Round(x, 2)).ToArray(),
            PenetrationGrade = grade,
            Recommendation = recommendation,
            CapillaryPressurePa = Math.Round(Pc, 2),
            EffectiveDiffusivity = Math.Round(Deff, 10),
            CalculatedAt = DateTime.Now,
            UsedLayeredModel = useLayered,
            LayerBreakdown = layerBreakdown
        });
    }

    public async Task<PenetrationResult[]> PredictBatchAsync(PenetrationInput[] inputs, CancellationToken ct = default)
    {
        var results = new List<PenetrationResult>();
        foreach (var input in inputs)
        {
            results.Add(await PredictAsync(input, ct));
        }
        return results.ToArray();
    }

    public double CalculateLucasWashburn(double t, double r, double gamma, double theta, double eta, double phi)
    {
        if (t <= 0) return 0;
        double cosTheta = Math.Cos(theta);
        if (cosTheta <= 0) return 0;

        double numerator = gamma * cosTheta * r * t;
        double denominator = 2.0 * eta * phi;
        double hSquared = numerator / denominator;

        return Math.Sqrt(Math.Max(0, hSquared)) * 1000.0;
    }

    public double CalculateCapillaryPressure(double r, double gamma, double theta)
    {
        double cosTheta = Math.Cos(theta);
        return 2.0 * gamma * cosTheta / Math.Max(r, 1e-9);
    }

    private double CalculateEffectiveDiffusivity(double phi, double r, double gamma, double theta, double eta)
    {
        double cosTheta = Math.Cos(theta);
        double tortuosity = 1.0 / (phi * phi);
        return (gamma * cosTheta * r * r) / (8.0 * eta * tortuosity);
    }

    private double CalculateTimeToTargetDepth(double targetDepthMm, double r, double gamma, double theta, double eta, double phi)
    {
        double h = targetDepthMm / 1000.0;
        double cosTheta = Math.Cos(theta);
        if (cosTheta <= 0) return double.PositiveInfinity;

        double numerator = 2.0 * eta * phi * h * h;
        double denominator = gamma * cosTheta * r;

        return numerator / Math.Max(denominator, 1e-9);
    }

    private string ClassifyPenetration(double depthMm)
    {
        if (depthMm >= 10.0) return "EXCELLENT";
        if (depthMm >= 5.0) return "GOOD";
        if (depthMm >= 2.0) return "FAIR";
        if (depthMm >= 0.5) return "POOR";
        return "INADEQUATE";
    }

    private string GenerateRecommendation(double depthMm, string material, double porosity)
    {
        if (depthMm >= 10.0)
        {
            return $"【优秀】{material} 预测渗透深度 {depthMm:F2}mm，完全满足加固要求。"
                 + $"孔隙率 {porosity:F2} 条件下渗透性能优异。";
        }
        else if (depthMm >= 5.0)
        {
            return $"【良好】{material} 预测渗透深度 {depthMm:F2}mm，可满足大部分加固需求。"
                 + $"建议延长浸泡时间以获得更深渗透。";
        }
        else if (depthMm >= 2.0)
        {
            return $"【一般】{material} 预测渗透深度 {depthMm:F2}mm，渗透能力有限。"
                 + $"建议采用多次涂刷工艺或考虑稀释处理。";
        }
        else if (depthMm >= 0.5)
        {
            return $"【较差】{material} 预测渗透深度 {depthMm:F2}mm，仅能形成表面涂层。"
                 + $"不建议作为主要加固材料使用。";
        }
        else
        {
            return $"【不适用】{material} 在当前孔隙率 {porosity:F2} 条件下几乎无法渗透。"
                 + $"建议选择低黏度材料或进行表面预处理。";
        }
    }

    public static bool TryGetMaterialProperties(string materialName, out MaterialProperties properties)
    {
        return MaterialDatabase.TryGetValue(materialName, out properties!);
    }

    public SculptureLayer[] GetDefaultClayLayers()
    {
        return new[]
        {
            new SculptureLayer
            {
                LayerName = "彩绘层",
                ThicknessMm = 0.3,
                Porosity = 0.15,
                PoreRadiusNm = 100.0,
                Tortuosity = 2.5
            },
            new SculptureLayer
            {
                LayerName = "地仗层",
                ThicknessMm = 3.0,
                Porosity = 0.35,
                PoreRadiusNm = 500.0,
                Tortuosity = 1.8
            },
            new SculptureLayer
            {
                LayerName = "胎体层",
                ThicknessMm = 20.0,
                Porosity = 0.45,
                PoreRadiusNm = 1500.0,
                Tortuosity = 1.4
            }
        };
    }

    public double CalculateLayeredLucasWashburn(double t, double gamma, double theta, double eta, SculptureLayer[] layers)
    {
        if (t <= 0 || layers.Length == 0) return 0;

        double cosTheta = Math.Cos(theta);
        if (cosTheta <= 0) return 0;

        double remainingTime = t;
        double totalDepth = 0;

        foreach (var layer in layers)
        {
            double r = layer.PoreRadiusNm * 1e-9;
            double phi = layer.Porosity;
            double tau = layer.Tortuosity;
            double layerThicknessM = layer.ThicknessMm / 1000.0;

            double numerator = gamma * cosTheta * r;
            double denominator = 2.0 * eta * phi * tau;
            double kEffective = numerator / denominator;

            double timeToTraverseLayer = (layerThicknessM * layerThicknessM) / Math.Max(kEffective, 1e-20);

            if (remainingTime >= timeToTraverseLayer)
            {
                totalDepth += layer.ThicknessMm;
                remainingTime -= timeToTraverseLayer;
            }
            else
            {
                double partialDepthM = Math.Sqrt(kEffective * remainingTime);
                totalDepth += partialDepthM * 1000.0;
                remainingTime = 0;
                break;
            }
        }

        return totalDepth;
    }

    private LayerPenetrationInfo[] CalculateLayerBreakdown(double t, double gamma, double theta, double eta, SculptureLayer[] layers)
    {
        if (layers.Length == 0) return Array.Empty<LayerPenetrationInfo>();

        double cosTheta = Math.Cos(theta);
        if (cosTheta <= 0) return layers.Select(l => new LayerPenetrationInfo
        {
            LayerName = l.LayerName,
            DepthInLayerMm = 0,
            TimeToTraverseSeconds = double.PositiveInfinity,
            LayerPorosity = l.Porosity,
            LayerPoreRadiusNm = l.PoreRadiusNm,
            FullyPenetrated = false
        }).ToArray();

        var result = new List<LayerPenetrationInfo>();
        double remainingTime = t;

        foreach (var layer in layers)
        {
            double r = layer.PoreRadiusNm * 1e-9;
            double phi = layer.Porosity;
            double tau = layer.Tortuosity;
            double layerThicknessM = layer.ThicknessMm / 1000.0;

            double numerator = gamma * cosTheta * r;
            double denominator = 2.0 * eta * phi * tau;
            double kEffective = numerator / Math.Max(denominator, 1e-20);

            double timeToTraverse = (layerThicknessM * layerThicknessM) / Math.Max(kEffective, 1e-20);

            double depthInLayer;
            bool fullyPenetrated;

            if (remainingTime >= timeToTraverse)
            {
                depthInLayer = layer.ThicknessMm;
                fullyPenetrated = true;
                remainingTime -= timeToTraverse;
            }
            else
            {
                double partialDepthM = Math.Sqrt(kEffective * remainingTime);
                depthInLayer = partialDepthM * 1000.0;
                fullyPenetrated = false;
                remainingTime = 0;
            }

            result.Add(new LayerPenetrationInfo
            {
                LayerName = layer.LayerName,
                DepthInLayerMm = Math.Round(depthInLayer, 4),
                TimeToTraverseSeconds = Math.Round(timeToTraverse, 2),
                LayerPorosity = layer.Porosity,
                LayerPoreRadiusNm = layer.PoreRadiusNm,
                FullyPenetrated = fullyPenetrated
            });

            if (!fullyPenetrated) break;
        }

        return result.ToArray();
    }

    private (double[] DepthProfile, double[] TimePoints) GenerateLayeredDepthProfile(
        double t, double gamma, double theta, double eta, SculptureLayer[] layers)
    {
        int timeSteps = 50;
        double totalThickness = layers.Sum(l => l.ThicknessMm);
        double timeToFull = CalculateLayeredTimeToTargetDepth(totalThickness, gamma, theta, eta, layers);
        double maxTime = Math.Max(t, timeToFull * 1.5);

        var timePoints = new double[timeSteps];
        var depthProfile = new double[timeSteps];

        for (int i = 0; i < timeSteps; i++)
        {
            timePoints[i] = (i + 1) * maxTime / timeSteps;
            depthProfile[i] = CalculateLayeredLucasWashburn(timePoints[i], gamma, theta, eta, layers);
        }

        return (depthProfile, timePoints);
    }

    private double CalculateLayeredTimeToTargetDepth(double targetDepthMm, double gamma, double theta, double eta, SculptureLayer[] layers)
    {
        double cosTheta = Math.Cos(theta);
        if (cosTheta <= 0) return double.PositiveInfinity;

        double remainingDepth = targetDepthMm;
        double totalTime = 0;

        foreach (var layer in layers)
        {
            if (remainingDepth <= 0) break;

            double r = layer.PoreRadiusNm * 1e-9;
            double phi = layer.Porosity;
            double tau = layer.Tortuosity;

            double numerator = gamma * cosTheta * r;
            double denominator = 2.0 * eta * phi * tau;
            double kEffective = numerator / Math.Max(denominator, 1e-20);

            double depthInLayer = Math.Min(remainingDepth, layer.ThicknessMm);
            double depthM = depthInLayer / 1000.0;
            totalTime += (depthM * depthM) / Math.Max(kEffective, 1e-20);

            remainingDepth -= depthInLayer;
        }

        return totalTime;
    }
}

public class MaterialProperties
{
    public double ViscosityPaS { get; set; }
    public double SurfaceTensionNm { get; set; }
    public double MolarMass { get; set; }
    public double Density { get; set; }
    public double TypicalContactAngle { get; set; }
}
