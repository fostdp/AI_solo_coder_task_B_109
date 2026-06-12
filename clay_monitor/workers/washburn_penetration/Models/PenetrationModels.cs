using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Models;

namespace WashburnPenetration.Models;

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
    Task<PenetrationResult[]> ParallelPredictBatchAsync(PenetrationInput[] inputs, CancellationToken ct = default);
    double CalculateLucasWashburn(double t, double r, double gamma, double theta, double eta, double phi);
    double CalculateLayeredLucasWashburn(double t, double gamma, double theta, double eta, SculptureLayer[] layers);
    double CalculateCapillaryPressure(double r, double gamma, double theta);
    SculptureLayer[] GetDefaultClayLayers();
}
