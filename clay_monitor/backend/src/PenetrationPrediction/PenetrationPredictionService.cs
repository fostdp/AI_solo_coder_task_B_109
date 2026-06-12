using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ClayMonitor.PenetrationPrediction;

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
}

public interface IPenetrationPredictionService
{
    Task<PenetrationResult> PredictAsync(PenetrationInput input, CancellationToken ct = default);
    Task<PenetrationResult[]> PredictBatchAsync(PenetrationInput[] inputs, CancellationToken ct = default);
    double CalculateLucasWashburn(double t, double r, double gamma, double theta, double eta, double phi);
    double CalculateCapillaryPressure(double r, double gamma, double theta);
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
        double phi = input.Porosity;
        double r = input.PoreRadiusNm * 1e-9;
        double gamma = input.SurfaceTensionNm;
        double theta = input.ContactAngleDeg * Math.PI / 180.0;
        double eta = input.ViscosityPaS;
        double t = input.TimeSeconds;

        double temperatureFactor = 1.0 + 0.02 * (input.TemperatureC - 25.0);
        eta /= temperatureFactor;

        double Pc = CalculateCapillaryPressure(r, gamma, theta);
        double Deff = CalculateEffectiveDiffusivity(phi, r, gamma, theta, eta);
        double depth = CalculateLucasWashburn(t, r, gamma, theta, eta, phi);

        double penetrationRate = t > 0 ? depth / t : 0;
        double timeTo5mm = CalculateTimeToTargetDepth(5.0, r, gamma, theta, eta, phi);

        int timeSteps = 50;
        double[] timePoints = new double[timeSteps];
        double[] depthProfile = new double[timeSteps];
        double maxTime = Math.Max(t, timeTo5mm * 1.5);

        for (int i = 0; i < timeSteps; i++)
        {
            timePoints[i] = (i + 1) * maxTime / timeSteps;
            depthProfile[i] = CalculateLucasWashburn(timePoints[i], r, gamma, theta, eta, phi);
        }

        string grade = ClassifyPenetration(depth);
        string recommendation = GenerateRecommendation(depth, input.MaterialName, phi);

        return await Task.FromResult(new PenetrationResult
        {
            SculptureId = input.SculptureId,
            MaterialName = input.MaterialName,
            PredictedDepthMm = Math.Round(depth, 4),
            PenetrationRateMmPerS = Math.Round(penetrationRate, 6),
            TimeToReach5mm = Math.Round(timeTo5mm, 2),
            DepthProfile = depthProfile.Select(x => Math.Round(x, 4)).ToArray(),
            TimePoints = timePoints.Select(x => Math.Round(x, 2)).ToArray(),
            PenetrationGrade = grade,
            Recommendation = recommendation,
            CapillaryPressurePa = Math.Round(Pc, 2),
            EffectiveDiffusivity = Math.Round(Deff, 10),
            CalculatedAt = DateTime.Now
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
}

public class MaterialProperties
{
    public double ViscosityPaS { get; set; }
    public double SurfaceTensionNm { get; set; }
    public double MolarMass { get; set; }
    public double Density { get; set; }
    public double TypicalContactAngle { get; set; }
}
