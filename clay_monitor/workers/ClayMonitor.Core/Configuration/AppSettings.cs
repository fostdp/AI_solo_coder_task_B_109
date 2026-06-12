namespace ClayMonitor.Core.Configuration;

public class AppSettings
{
    public WifiIngestOptions WifiIngest { get; set; } = new();
    public SaltTransportOptions SaltTransport { get; set; } = new();
    public MaterialScoreOptions MaterialScore { get; set; } = new();
    public AlertDispatchOptions AlertDispatch { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
    public PenetrationPredictionOptions PenetrationPrediction { get; set; } = new();
    public ChemicalReactionOptions ChemicalReaction { get; set; } = new();
    public BreathabilityOptions Breathability { get; set; } = new();
    public VirtualReinforcementOptions VirtualReinforcement { get; set; } = new();
}

public class WifiIngestOptions
{
    public int ReportIntervalMinutes { get; set; } = 45;
    public int SensorTimeoutMinutes { get; set; } = 120;
    public bool AllowAnonymousReport { get; set; } = true;
    public int BatchSize { get; set; } = 50;
    public string[] ValidSensorTypes { get; set; } = { "ION_MIGRATION", "ENVIRONMENT" };
    public double DefaultNaConcentration { get; set; } = 100;
    public double DefaultKConcentration { get; set; } = 50;
    public double DefaultCaConcentration { get; set; } = 80;
}

public class SaltTransportOptions
{
    public double TimeStepHours { get; set; } = 1.0;
    public double SpaceStepCm { get; set; } = 0.5;
    public double MaxDepthCm { get; set; } = 50.0;
    public double Porosity { get; set; } = 0.35;
    public double Tortuosity { get; set; } = 0.6;
    public double DiffusionCoefficient { get; set; } = 1.5e-9;
    public double BaseVelocity { get; set; } = 0.01;
    public double MaxEvaporationRate { get; set; } = 0.1;
    public int DefaultPredictionHours { get; set; } = 168;
    public double EnrichmentFactor { get; set; } = 2.5;
    public double CapillaryPressure { get; set; } = 0.005;
}

public class MaterialScoreOptions
{
    public double ContactAngleWeight { get; set; } = 0.20;
    public double PenetrationDepthWeight { get; set; } = 0.25;
    public double StrengthMatchWeight { get; set; } = 0.20;
    public double WeatherResistanceWeight { get; set; } = 0.15;
    public double ReversibilityWeight { get; set; } = 0.10;
    public double CostPerformanceWeight { get; set; } = 0.10;
    public double ExcellentThreshold { get; set; } = 85;
    public double GoodThreshold { get; set; } = 70;
    public double FairThreshold { get; set; } = 55;
}

public class AlertDispatchOptions
{
    public double SurfaceCoverageThreshold { get; set; } = 30.0;
    public double NaConcentrationThreshold { get; set; } = 500.0;
    public double KConcentrationThreshold { get; set; } = 300.0;
    public double CaConcentrationThreshold { get; set; } = 400.0;
    public string DingTalkWebhookUrl { get; set; } = string.Empty;
    public string DingTalkSecret { get; set; } = string.Empty;
    public int AlarmSuppressionMinutes { get; set; } = 60;
    public string[] AlertLevels { get; set; } = { "INFO", "WARNING", "CRITICAL" };
    public bool EnableDingTalkPush { get; set; } = true;
    public bool EnableConsoleLog { get; set; } = true;
}

public class DatabaseOptions
{
    public string ConnectionString { get; set; } = "Data Source=../database/sculpture_monitor.db";
    public int CommandTimeout { get; set; } = 30;
    public bool EnableSensitiveDataLogging { get; set; } = false;
}

public class PenetrationPredictionOptions
{
    public double DefaultPorosity { get; set; } = 0.35;
    public double DefaultPoreRadiusNm { get; set; } = 500.0;
    public double DefaultPredictionTimeSeconds { get; set; } = 3600.0;
    public double TemperatureCorrectionFactor { get; set; } = 0.02;
    public double MinPenetrationForGrade { get; set; } = 0.5;
}

public class ChemicalReactionOptions
{
    public double SaltConcentrationWarningThreshold { get; set; } = 300.0;
    public double HarmfulProductThreshold { get; set; } = 0.15;
    public double CriticalDeltaGThreshold { get; set; } = -50.0;
    public double WarningDeltaGThreshold { get; set; } = -20.0;
    public double PHNeutral { get; set; } = 7.0;
    public double HighPHRiskThreshold { get; set; } = 8.5;
    public double LowPHRiskThreshold { get; set; } = 6.0;
    public double HighTemperatureRiskThreshold { get; set; } = 30.0;
}

public class BreathabilityOptions
{
    public int MaxDataAgeHours { get; set; } = 72;
    public int MinDataPointsForAnalysis { get; set; } = 10;
    public int DefaultAnalysisWindowHours { get; set; } = 24;
    public double CycleThresholdPercent { get; set; } = 2.0;
    public double PoorRegulationThreshold { get; set; } = 40.0;
    public double CriticalRegulationThreshold { get; set; } = 20.0;
}

public class VirtualReinforcementOptions
{
    public double DefaultPorosity { get; set; } = 0.35;
    public double DefaultPoreRadiusNm { get; set; } = 500.0;
    public double DefaultApplicationTimeSeconds { get; set; } = 3600.0;
    public double DefaultThicknessCm { get; set; } = 5.0;
    public double DefaultWidthCm { get; set; } = 40.0;
    public double DefaultHeightCm { get; set; } = 60.0;
    public double PenetrationDecayFactor { get; set; } = 0.5;
    public double EdgeDecayExponent { get; set; } = 1.5;
    public double FilmMixingRatio { get; set; } = 0.6;
    public int DefaultResolutionX { get; set; } = 40;
    public int DefaultResolutionY { get; set; } = 60;
    public int DefaultResolutionZ { get; set; } = 30;
}
