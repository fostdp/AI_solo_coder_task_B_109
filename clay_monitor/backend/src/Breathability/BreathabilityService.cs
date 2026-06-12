using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ClayMonitor.Breathability;

public record SensorAccuracyConfig
{
    public double TemperatureAccuracyC { get; init; } = 0.3;
    public double HumidityAccuracyPercent { get; init; } = 2.0;
    public double SensorResponseTimeSeconds { get; init; } = 15.0;
    public double HysteresisErrorPercent { get; init; } = 1.5;
    public bool ApplyCorrection { get; init; } = true;
}

public record BreathabilityInput
{
    public int SculptureId { get; init; }
    public double[] Temperatures { get; init; } = Array.Empty<double>();
    public double[] Humidities { get; init; } = Array.Empty<double>();
    public DateTime[] Timestamps { get; init; } = Array.Empty<DateTime>();
    public double Porosity { get; init; } = 0.35;
    public double MoistureContent { get; init; } = 0.25;
    public int AnalysisWindowHours { get; init; } = 24;
    public SensorAccuracyConfig? SensorAccuracy { get; init; }
}

public record BreathabilityResult
{
    public int SculptureId { get; init; }
    public double BreathFrequencyCyclesPerDay { get; init; }
    public double BreathIntensity { get; init; }
    public double TemperatureAmplitudeC { get; init; }
    public double HumidityAmplitudePercent { get; init; }
    public double TimeLagMinutes { get; init; }
    public double HysteresisArea { get; init; }
    public double MoistureBufferCapacity { get; init; }
    public double SelfRegulationScore { get; init; }
    public string RegulationLevel { get; init; } = string.Empty;
    public string Assessment { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public double[] MoistureSorptionCurve { get; init; } = Array.Empty<double>();
    public double[] MoistureDesorptionCurve { get; init; } = Array.Empty<double>();
    public int AbsorptionCycles { get; init; }
    public int DesorptionCycles { get; init; }
    public double AverageCycleDurationMinutes { get; init; }
    public DateTime CalculatedAt { get; init; } = DateTime.Now;
    public bool SensorLagCorrectionApplied { get; init; }
    public double CorrectedTimeLagMinutes { get; init; }
    public double SensorLagContributionMinutes { get; init; }
    public double RawUncertaintyPercent { get; init; }
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

public interface IBreathabilityService
{
    Task<BreathabilityResult> AnalyzeAsync(BreathabilityInput input, CancellationToken ct = default);
    double CalculateBreathFrequency(double[] humidity, DateTime[] timestamps);
    double CalculateTimeLag(double[] temperature, double[] humidity, DateTime[] timestamps);
    double CalculateHysteresisArea(double[] humidity, double[] moisture);
    double[] ApplySensorLagCorrection(double[] signal, DateTime[] timestamps, double responseTimeSeconds);
    double CalculateSensorLagContribution(double[] timestamps, double sensorResponseTimeSeconds);
}

public class BreathabilityService : BackgroundService, IBreathabilityService
{
    private readonly IMessageBus _bus;
    private readonly BreathabilityOptions _options;
    private readonly Dictionary<int, List<SensorDataPoint>> _sensorDataBuffers = new();

    public BreathabilityService(IMessageBus bus, IOptions<BreathabilityOptions> options)
    {
        _bus = bus;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var sensorData in _bus.SubscribeAsync<SensorDataReceived>(stoppingToken))
        {
            try
            {
                if (sensorData.Temperature.HasValue && sensorData.Humidity.HasValue)
                {
                    await ProcessSensorDataAsync(sensorData, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }

    private async Task ProcessSensorDataAsync(SensorDataReceived sensorData, CancellationToken stoppingToken)
    {
        int sculptureId = sensorData.SculptureId;

        if (!_sensorDataBuffers.TryGetValue(sculptureId, out var buffer))
        {
            buffer = new List<SensorDataPoint>();
            _sensorDataBuffers[sculptureId] = buffer;
        }

        buffer.Add(new SensorDataPoint
        {
            Timestamp = sensorData.Timestamp,
            Temperature = sensorData.Temperature ?? 25.0,
            Humidity = sensorData.Humidity ?? 55.0
        });

        TimeSpan maxAge = TimeSpan.FromHours(_options.MaxDataAgeHours);
        buffer.RemoveAll(p => DateTime.Now - p.Timestamp > maxAge);

        int minPoints = _options.MinDataPointsForAnalysis;
        if (buffer.Count >= minPoints)
        {
            var orderedData = buffer.OrderBy(p => p.Timestamp).ToArray();
            var input = new BreathabilityInput
            {
                SculptureId = sculptureId,
                Temperatures = orderedData.Select(p => p.Temperature).ToArray(),
                Humidities = orderedData.Select(p => p.Humidity).ToArray(),
                Timestamps = orderedData.Select(p => p.Timestamp).ToArray(),
                Porosity = 0.35,
                MoistureContent = 0.25,
                AnalysisWindowHours = _options.DefaultAnalysisWindowHours
            };

            var result = await AnalyzeAsync(input, stoppingToken);
            await _bus.PublishAsync(result, stoppingToken);

            if (result.SelfRegulationScore < _options.PoorRegulationThreshold)
            {
                var assessment = new BreathabilityAssessment
                {
                    SculptureId = sculptureId,
                    Level = result.SelfRegulationScore < _options.CriticalRegulationThreshold ? "CRITICAL" : "WARNING",
                    Message = $"泥塑自调节能力{result.RegulationLevel}，{result.Assessment}",
                    SelfRegulationScore = Math.Round(result.SelfRegulationScore, 2),
                    Threshold = _options.PoorRegulationThreshold
                };

                await _bus.PublishAsync(assessment, stoppingToken);
            }
        }
    }

    public async Task<BreathabilityResult> AnalyzeAsync(BreathabilityInput input, CancellationToken ct = default)
    {
        int n = input.Temperatures.Length;
        if (n < 10)
        {
            return await Task.FromResult(new BreathabilityResult
            {
                SculptureId = input.SculptureId,
                Assessment = "数据不足，无法进行呼吸性分析",
                Recommendation = "请继续收集至少10个温湿度数据点后再进行分析",
                CalculatedAt = DateTime.Now
            });
        }

        double[] T = input.Temperatures;
        double[] RH = input.Humidities;
        DateTime[] t = input.Timestamps;

        var sensorCfg = input.SensorAccuracy ?? new SensorAccuracyConfig();
        bool applyCorrection = sensorCfg.ApplyCorrection;

        double[] correctedT = T;
        double[] correctedRH = RH;
        double sensorLagContribution = 0;
        double correctedTimeLag;

        if (applyCorrection)
        {
            correctedT = ApplySensorLagCorrection(T, t, sensorCfg.SensorResponseTimeSeconds);
            correctedRH = ApplySensorLagCorrection(RH, t, sensorCfg.SensorResponseTimeSeconds);
            sensorLagContribution = CalculateSensorLagContribution(t, sensorCfg.SensorResponseTimeSeconds);
        }

        double tempAmp = CalculateAmplitude(correctedT);
        double humAmp = CalculateAmplitude(correctedRH);

        double breathFreq = CalculateBreathFrequency(correctedRH, t);
        double breathIntensity = CalculateBreathIntensity(correctedRH, t);
        double timeLag = CalculateTimeLag(correctedT, correctedRH, t);
        correctedTimeLag = applyCorrection ? Math.Max(0, timeLag - sensorLagContribution) : timeLag;

        int absCycles, desCycles;
        double avgCycleDuration = CalculateCycleDuration(correctedRH, t, out absCycles, out desCycles);

        double[] sorption, desorption;
        double hysteresisArea = CalculateHysteresis(correctedT, correctedRH, input.MoistureContent, out sorption, out desorption);

        if (applyCorrection)
        {
            double hystError = sensorCfg.HysteresisErrorPercent / 100.0;
            hysteresisArea = Math.Max(0, hysteresisArea * (1.0 - hystError * 0.5));
        }

        double moistureBuffer = CalculateMoistureBufferCapacity(humAmp, input.Porosity, input.MoistureContent);
        double selfRegScore = CalculateSelfRegulationScore(
            breathFreq, breathIntensity, correctedTimeLag, hysteresisArea, moistureBuffer, tempAmp);

        double uncertainty = CalculateMeasurementUncertainty(
            sensorCfg, tempAmp, humAmp, correctedTimeLag);

        string regulationLevel = ClassifyRegulationLevel(selfRegScore);
        string assessment = GenerateAssessment(breathFreq, breathIntensity, correctedTimeLag, selfRegScore, tempAmp, humAmp);
        string recommendation = GenerateRecommendation(regulationLevel, breathFreq, correctedTimeLag);

        return await Task.FromResult(new BreathabilityResult
        {
            SculptureId = input.SculptureId,
            BreathFrequencyCyclesPerDay = Math.Round(breathFreq, 3),
            BreathIntensity = Math.Round(breathIntensity, 4),
            TemperatureAmplitudeC = Math.Round(tempAmp, 2),
            HumidityAmplitudePercent = Math.Round(humAmp, 2),
            TimeLagMinutes = Math.Round(timeLag, 2),
            HysteresisArea = Math.Round(hysteresisArea, 4),
            MoistureBufferCapacity = Math.Round(moistureBuffer, 4),
            SelfRegulationScore = Math.Round(selfRegScore, 2),
            RegulationLevel = regulationLevel,
            Assessment = assessment,
            Recommendation = recommendation,
            MoistureSorptionCurve = sorption.Select(x => Math.Round(x, 4)).ToArray(),
            MoistureDesorptionCurve = desorption.Select(x => Math.Round(x, 4)).ToArray(),
            AbsorptionCycles = absCycles,
            DesorptionCycles = desCycles,
            AverageCycleDurationMinutes = Math.Round(avgCycleDuration, 2),
            CalculatedAt = DateTime.Now,
            SensorLagCorrectionApplied = applyCorrection,
            CorrectedTimeLagMinutes = Math.Round(correctedTimeLag, 2),
            SensorLagContributionMinutes = Math.Round(sensorLagContribution, 2),
            RawUncertaintyPercent = Math.Round(uncertainty, 2)
        });
    }

    public double[] ApplySensorLagCorrection(double[] signal, DateTime[] timestamps, double responseTimeSeconds)
    {
        if (signal.Length < 2 || responseTimeSeconds <= 0)
            return (double[])signal.Clone();

        int n = signal.Length;
        var corrected = new double[n];
        corrected[0] = signal[0];

        double tau = Math.Max(responseTimeSeconds, 1.0);

        for (int i = 1; i < n; i++)
        {
            double dt = (timestamps[i] - timestamps[i - 1]).TotalSeconds;
            if (dt <= 0)
            {
                corrected[i] = corrected[i - 1];
                continue;
            }

            double alpha = 1.0 - Math.Exp(-dt / tau);
            double measuredRate = (signal[i] - signal[i - 1]) / dt;
            double trueRate = measuredRate / Math.Max(alpha, 1e-6);
            corrected[i] = corrected[i - 1] + trueRate * dt;
        }

        return corrected;
    }

    public double CalculateSensorLagContribution(double[] timestamps, double sensorResponseTimeSeconds)
    {
        if (timestamps.Length < 2) return 0;

        double avgIntervalMinutes = (timestamps.Last() - timestamps.First()).TotalMinutes / (timestamps.Length - 1);
        double responseMinutes = sensorResponseTimeSeconds / 60.0;

        double expectedLagRatio = responseMinutes / Math.Max(avgIntervalMinutes, 0.1);
        return Math.Min(responseMinutes * 0.8, avgIntervalMinutes * expectedLagRatio * 0.5);
    }

    private double CalculateMeasurementUncertainty(
        SensorAccuracyConfig cfg, double tempAmp, double humAmp, double timeLag)
    {
        double tempUncertainty = cfg.TemperatureAccuracyC / Math.Max(tempAmp, 0.1) * 100.0;
        double humUncertainty = cfg.HumidityAccuracyPercent / Math.Max(humAmp, 0.1) * 100.0;
        double lagUncertainty = cfg.SensorResponseTimeSeconds / 60.0 / Math.Max(timeLag, 1.0) * 100.0;

        return Math.Clamp(Math.Sqrt(tempUncertainty * tempUncertainty
                                    + humUncertainty * humUncertainty
                                    + lagUncertainty * lagUncertainty) / Math.Sqrt(3), 0, 50);
    }

    public double CalculateBreathFrequency(double[] humidity, DateTime[] timestamps)
    {
        if (humidity.Length < 4) return 0;

        int[] peaks = FindPeaks(humidity);
        int[] valleys = FindValleys(humidity);

        if (peaks.Length < 2 && valleys.Length < 2)
        {
            double rate = humidity.Last() - humidity.First();
            double hours = (timestamps.Last() - timestamps.First()).TotalHours;
            return Math.Abs(rate) > 1.0 ? Math.Max(0.5, hours / 12.0) : 0.5;
        }

        int cycles = Math.Max(peaks.Length, valleys.Length) - 1;
        double totalHours = (timestamps.Last() - timestamps.First()).TotalHours;
        double cyclesPerDay = cycles / Math.Max(totalHours, 0.1) * 24.0;

        return Math.Clamp(cyclesPerDay, 0.1, 10.0);
    }

    public double CalculateTimeLag(double[] temperature, double[] humidity, DateTime[] timestamps)
    {
        if (temperature.Length < 10 || humidity.Length < 10) return 0;

        int n = temperature.Length;
        int maxLag = Math.Min(n / 3, 24);

        double[] normT = Normalize(temperature);
        double[] normH = Normalize(humidity);

        double maxCorr = -1;
        int bestLag = 0;

        for (int lag = -maxLag; lag <= maxLag; lag++)
        {
            double corr = CalculateCrossCorrelation(normT, normH, lag);
            if (corr > maxCorr)
            {
                maxCorr = corr;
                bestLag = lag;
            }
        }

        if (timestamps.Length >= 2)
        {
            double avgInterval = (timestamps.Last() - timestamps.First()).TotalMinutes / (timestamps.Length - 1);
            return Math.Abs(bestLag) * avgInterval;
        }

        return Math.Abs(bestLag) * 15.0;
    }

    public double CalculateHysteresisArea(double[] humidity, double[] moisture)
    {
        if (humidity.Length < 10) return 0;

        int n = humidity.Length;
        int mid = n / 2;

        double[] absorptionRH = humidity.Take(mid).ToArray();
        double[] desorptionRH = humidity.Skip(mid).Take(n - mid).ToArray();

        double trapAbs = TrapezoidalIntegral(absorptionRH);
        double trapDes = TrapezoidalIntegral(desorptionRH);

        return Math.Abs(trapDes - trapAbs);
    }

    private double CalculateHysteresisArea(double[] T, double[] RH, double moistureContent,
        out double[] sorption, out double[] desorption)
    {
        int n = Math.Min(T.Length, RH.Length);
        int resolution = 50;

        sorption = new double[resolution];
        desorption = new double[resolution];

        double[] ascendingRH = new double[n / 2];
        double[] descendingRH = new double[n / 2];

        for (int i = 0; i < n / 2; i++)
        {
            ascendingRH[i] = 30.0 + (i / (double)(n / 2)) * 40.0;
            descendingRH[i] = 70.0 - (i / (double)(n / 2)) * 40.0;
        }

        for (int i = 0; i < resolution; i++)
        {
            double rh = (double)i / resolution * 100.0;
            double k = 0.05;
            double maxMoisture = moistureContent * 1.5;

            sorption[i] = maxMoisture * (1.0 - Math.Exp(-k * rh));
            desorption[i] = maxMoisture * (1.0 - Math.Exp(-k * rh)) * (1.0 + 0.15 * Math.Exp(-0.05 * rh));
        }

        double area = 0;
        for (int i = 0; i < resolution; i++)
        {
            area += Math.Abs(desorption[i] - sorption[i]);
        }

        return area / resolution;
    }

    private double CalculateAmplitude(double[] data)
    {
        return data.Max() - data.Min();
    }

    private double CalculateBreathIntensity(double[] humidity, DateTime[] timestamps)
    {
        if (humidity.Length < 2) return 0;

        double totalVariation = 0;
        for (int i = 1; i < humidity.Length; i++)
        {
            totalVariation += Math.Abs(humidity[i] - humidity[i - 1]);
        }

        double totalHours = (timestamps.Last() - timestamps.First()).TotalHours;
        return totalVariation / Math.Max(totalHours, 0.1);
    }

    private double CalculateCycleDuration(double[] humidity, DateTime[] timestamps,
        out int absorptionCycles, out int desorptionCycles)
    {
        absorptionCycles = 0;
        desorptionCycles = 0;

        if (humidity.Length < 3) return 0;

        double threshold = _options.CycleThresholdPercent;
        List<double> cycleDurations = new List<double>();

        int cycleStartIdx = 0;
        double startValue = humidity[0];
        bool isRising = humidity[1] > humidity[0];

        for (int i = 1; i < humidity.Length; i++)
        {
            double change = humidity[i] - humidity[i - 1];
            bool currentlyRising = change > 0;

            if (currentlyRising != isRising)
            {
                double magnitude = Math.Abs(humidity[i] - startValue);
                if (magnitude >= threshold)
                {
                    if (isRising)
                        absorptionCycles++;
                    else
                        desorptionCycles++;

                    double duration = (timestamps[i] - timestamps[cycleStartIdx]).TotalMinutes;
                    if (duration > 0) cycleDurations.Add(duration);

                    cycleStartIdx = i;
                    startValue = humidity[i];
                }
                isRising = currentlyRising;
            }
        }

        return cycleDurations.Count > 0 ? cycleDurations.Average() : 0;
    }

    private double CalculateMoistureBufferCapacity(double humidityAmplitude, double porosity, double moistureContent)
    {
        double bufferCapacity = porosity * moistureContent * 10.0 / Math.Max(humidityAmplitude, 1.0);
        return Math.Clamp(bufferCapacity, 0, 100);
    }

    private double CalculateSelfRegulationScore(double freq, double intensity, double timeLag,
        double hysteresis, double buffer, double tempAmp)
    {
        double freqScore = ScoreFrequency(freq);
        double intensityScore = ScoreIntensity(intensity);
        double lagScore = ScoreTimeLag(timeLag);
        double hysteresisScore = ScoreHysteresis(hysteresis);
        double bufferScore = buffer;

        double score =
            0.25 * freqScore +
            0.20 * intensityScore +
            0.20 * lagScore +
            0.15 * hysteresisScore +
            0.20 * bufferScore;

        return Math.Clamp(score, 0, 100);
    }

    private double ScoreFrequency(double freq)
    {
        double optimalFreq = 2.0;
        double deviation = Math.Abs(freq - optimalFreq);
        return Math.Max(0, 100 - deviation * 15);
    }

    private double ScoreIntensity(double intensity)
    {
        double optimalIntensity = 1.0;
        double ratio = intensity / Math.Max(optimalIntensity, 0.001);
        return Math.Max(0, 100 * Math.Exp(-0.5 * Math.Pow(ratio - 1, 2)));
    }

    private double ScoreTimeLag(double timeLagMinutes)
    {
        if (timeLagMinutes <= 0) return 50;
        double optimalLag = 30.0;
        double ratio = timeLagMinutes / optimalLag;
        return Math.Max(0, 100 * Math.Exp(-0.3 * Math.Abs(Math.Log(ratio))));
    }

    private double ScoreHysteresis(double hysteresis)
    {
        double optimalHysteresis = 0.05;
        double ratio = hysteresis / Math.Max(optimalHysteresis, 0.001);
        return Math.Max(0, 100 * Math.Exp(-0.8 * Math.Pow(ratio - 1, 2)));
    }

    private int[] FindPeaks(double[] data)
    {
        List<int> peaks = new List<int>();
        for (int i = 1; i < data.Length - 1; i++)
        {
            if (data[i] > data[i - 1] && data[i] > data[i + 1])
            {
                peaks.Add(i);
            }
        }
        return peaks.ToArray();
    }

    private int[] FindValleys(double[] data)
    {
        List<int> valleys = new List<int>();
        for (int i = 1; i < data.Length - 1; i++)
        {
            if (data[i] < data[i - 1] && data[i] < data[i + 1])
            {
                valleys.Add(i);
            }
        }
        return valleys.ToArray();
    }

    private double[] Normalize(double[] data)
    {
        double mean = data.Average();
        double std = Math.Sqrt(data.Sum(x => (x - mean) * (x - mean)) / data.Length);
        if (std == 0) return data.Select(_ => 0.0).ToArray();
        return data.Select(x => (x - mean) / std).ToArray();
    }

    private double CalculateCrossCorrelation(double[] x, double[] y, int lag)
    {
        int n = x.Length;
        double sum = 0;
        int count = 0;

        for (int i = Math.Max(0, lag); i < Math.Min(n, n + lag); i++)
        {
            int j = i - lag;
            if (j >= 0 && j < n)
            {
                sum += x[i] * y[j];
                count++;
            }
        }

        return count > 0 ? sum / count : 0;
    }

    private double TrapezoidalIntegral(double[] data)
    {
        if (data.Length < 2) return 0;
        double sum = 0;
        for (int i = 1; i < data.Length; i++)
        {
            sum += (data[i] + data[i - 1]) / 2.0;
        }
        return sum;
    }

    private string ClassifyRegulationLevel(double score)
    {
        if (score >= 80) return "EXCELLENT";
        if (score >= 60) return "GOOD";
        if (score >= 40) return "FAIR";
        if (score >= 20) return "POOR";
        return "CRITICAL";
    }

    private string GenerateAssessment(double freq, double intensity, double timeLag,
        double score, double tempAmp, double humAmp)
    {
        string freqDesc = freq < 1 ? "呼吸频率较低" : freq > 4 ? "呼吸频率偏高" : "呼吸频率正常";
        string intensityDesc = intensity < 0.5 ? "呼吸强度较弱" : intensity > 2.0 ? "呼吸强度偏高" : "呼吸强度适中";
        string lagDesc = timeLag < 10 ? "响应迅速" : timeLag > 60 ? "响应滞后明显" : "响应正常";

        return $"{freqDesc}，{intensityDesc}，{lagDesc}。" +
               $"温度波动幅度 {tempAmp:F1}℃，湿度波动幅度 {humAmp:F1}%。" +
               $"综合自调节能力评分 {score:F1} 分。";
    }

    private string GenerateRecommendation(string level, double freq, double timeLag)
    {
        switch (level)
        {
            case "EXCELLENT":
                return "【优秀】泥塑呼吸性良好，自调节能力强。建议维持当前环境条件。";
            case "GOOD":
                return "【良好】泥塑呼吸性正常，可通过小幅优化环境进一步提升保护效果。";
            case "FAIR":
                return "【一般】泥塑呼吸性一般，建议安装恒湿设备，维持环境湿度稳定。";
            case "POOR":
                return "【较差】泥塑呼吸性较差，环境波动响应滞后。强烈建议安装温湿度控制系统，" +
                       "并考虑使用透气性加固材料。";
            case "CRITICAL":
                return "【危险】泥塑呼吸功能严重受损，已失去自调节能力。" +
                       "必须立即采取环境控制措施，并在加固时优先考虑材料透气性。";
            default:
                return "建议定期监测泥塑呼吸状态，根据季节变化调整环境控制策略。";
        }
    }

    private class SensorDataPoint
    {
        public DateTime Timestamp { get; set; }
        public double Temperature { get; set; }
        public double Humidity { get; set; }
    }
}
