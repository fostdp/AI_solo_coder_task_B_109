namespace ClayMonitor.Core.Messages;

public record SensorDataReceived
{
    public string SensorCode { get; init; } = string.Empty;
    public int SculptureId { get; init; }
    public string SensorType { get; init; } = string.Empty;
    public double? NaConcentration { get; init; }
    public double? KConcentration { get; init; }
    public double? CaConcentration { get; init; }
    public double? SaltConcentration { get; init; }
    public double? SurfaceCoverage { get; init; }
    public double? Temperature { get; init; }
    public double? Humidity { get; init; }
    public double? SignalStrength { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string RawPayload { get; init; } = string.Empty;
}

public record SaltMigrationCompleted
{
    public int SculptureId { get; init; }
    public double SurfaceEvaporationRate { get; init; }
    public double SurfaceEnrichmentRatio { get; init; }
    public double MaxConcentration { get; init; }
    public double AverageConcentration { get; init; }
    public double[] DepthProfile { get; init; } = Array.Empty<double>();
    public double[] MoistureProfile { get; init; } = Array.Empty<double>();
    public DateTime PredictionTime { get; init; }
    public int PredictionHours { get; init; }
    public bool RequiresAlert { get; init; }
    public string AlertReason { get; init; } = string.Empty;
}

public record MaterialScoreCalculated
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public double TotalScore { get; init; }
    public double ContactAngle { get; init; }
    public double PenetrationDepth { get; init; }
    public double StrengthMatch { get; init; }
    public double WeatherResistance { get; init; }
    public double Reversibility { get; init; }
    public double CostPerformance { get; init; }
    public string Recommendation { get; init; } = string.Empty;
    public DateTime CalculatedAt { get; init; } = DateTime.Now;
}

public record AlertTriggered
{
    public string AlertId { get; init; } = Guid.NewGuid().ToString("N");
    public int SculptureId { get; init; }
    public string SensorCode { get; init; } = string.Empty;
    public string SculptureName { get; init; } = string.Empty;
    public string AlertType { get; init; } = string.Empty;
    public string AlertLevel { get; init; } = "WARNING";
    public string Message { get; init; } = string.Empty;
    public double CurrentValue { get; init; }
    public double Threshold { get; init; }
    public DateTime TriggeredAt { get; init; } = DateTime.Now;
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public record EvaporationEnvironmentUpdated
{
    public int SculptureId { get; init; }
    public double Temperature { get; init; }
    public double RelativeHumidity { get; init; }
    public double WindSpeed { get; init; }
    public double SolarRadiation { get; init; }
    public double AtmosphericPressure { get; init; } = 101.325;
    public DateTime UpdatedAt { get; init; } = DateTime.Now;
}

public record PenetrationPredictionCompleted
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public double PredictedDepthMm { get; init; }
    public double PenetrationRate { get; init; }
    public double CapillaryPressurePa { get; init; }
    public string PenetrationGrade { get; init; } = string.Empty;
    public DateTime CalculatedAt { get; init; } = DateTime.Now;
}

public record ChemicalReactionWarning
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

public record BreathabilityAssessment
{
    public int SculptureId { get; init; }
    public string AlertId { get; init; } = Guid.NewGuid().ToString("N");
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public double SelfRegulationScore { get; init; }
    public double Threshold { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public record VirtualReinforcementApplied
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public double AveragePenetrationMm { get; init; }
    public double GlossChangePercent { get; init; }
    public double ReinforcedVolumePercent { get; init; }
    public DateTime AppliedAt { get; init; } = DateTime.Now;
}
