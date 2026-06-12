namespace ClayMonitor.Core.Models;

public record SculptureLayer
{
    public string LayerName { get; init; } = string.Empty;
    public double ThicknessMm { get; init; }
    public double Porosity { get; init; }
    public double PoreRadiusNm { get; init; }
    public double Tortuosity { get; init; } = 1.5;
}
