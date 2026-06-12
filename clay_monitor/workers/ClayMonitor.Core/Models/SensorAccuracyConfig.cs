namespace ClayMonitor.Core.Models;

public record SensorAccuracyConfig
{
    public double TemperatureAccuracyC { get; init; } = 0.3;
    public double HumidityAccuracyPercent { get; init; } = 2.0;
    public double SensorResponseTimeSeconds { get; init; } = 15.0;
    public double HysteresisErrorPercent { get; init; } = 1.5;
    public bool ApplyCorrection { get; init; } = true;
}
