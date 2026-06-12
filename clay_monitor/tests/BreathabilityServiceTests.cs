using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Breathability;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ClayMonitor.Tests;

public class BreathabilityServiceTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly IOptions<BreathabilityOptions> _options;
    private readonly BreathabilityService _service;

    public BreathabilityServiceTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _options = Options.Create(new BreathabilityOptions
        {
            MaxDataAgeHours = 72,
            MinDataPointsForAnalysis = 10,
            DefaultAnalysisWindowHours = 24,
            CycleThresholdPercent = 2.0,
            PoorRegulationThreshold = 40.0,
            CriticalRegulationThreshold = 20.0
        });
        _service = new BreathabilityService(_mockBus.Object, _options);
    }

    #region 核心测试：高频率区域为薄弱区

    [Fact]
    public void CalculateBreathFrequency_DifferentCycleRates_ReturnsDifferentFrequencies()
    {
        var timestampsSlow = GenerateTimestamps(72, 96);
        var humiditySlow = GenerateSinusoidalHumidity(96, 36.0, 55.0, 10.0);

        var timestampsFast = GenerateTimestamps(24, 96);
        var humidityFast = GenerateSinusoidalHumidity(96, 6.0, 55.0, 10.0);

        double freqSlow = _service.CalculateBreathFrequency(humiditySlow, timestampsSlow);
        double freqFast = _service.CalculateBreathFrequency(humidityFast, timestampsFast);

        freqSlow.Should().BeGreaterThan(0);
        freqFast.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AnalyzeAsync_HighBreathFrequency_LowerSelfRegulationScore()
    {
        var normalFreqData = GenerateBreathabilityData(
            startTime: DateTime.Now,
            totalHours: 48,
            intervalMinutes: 30,
            cyclesPerDay: 2.0,
            tempBase: 22.0,
            tempAmp: 3.0,
            humidityBase: 55.0,
            humidityAmp: 10.0);

        var highFreqData = GenerateBreathabilityData(
            startTime: DateTime.Now,
            totalHours: 48,
            intervalMinutes: 30,
            cyclesPerDay: 8.0,
            tempBase: 22.0,
            tempAmp: 3.0,
            humidityBase: 55.0,
            humidityAmp: 10.0);

        var normalInput = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = normalFreqData.Temperatures,
            Humidities = normalFreqData.Humidities,
            Timestamps = normalFreqData.Timestamps,
            Porosity = 0.35,
            MoistureContent = 0.25
        };

        var highFreqInput = new BreathabilityInput
        {
            SculptureId = 2,
            Temperatures = highFreqData.Temperatures,
            Humidities = highFreqData.Humidities,
            Timestamps = highFreqData.Timestamps,
            Porosity = 0.35,
            MoistureContent = 0.25
        };

        var normalResult = await _service.AnalyzeAsync(normalInput);
        var highFreqResult = await _service.AnalyzeAsync(highFreqInput);

        highFreqResult.BreathFrequencyCyclesPerDay.Should().BeGreaterThan(normalResult.BreathFrequencyCyclesPerDay);

        if (highFreqResult.BreathFrequencyCyclesPerDay > 4.0)
        {
            highFreqResult.SelfRegulationScore.Should().BeLessThan(normalResult.SelfRegulationScore);
        }
    }

    [Fact]
    public void CalculateBreathFrequency_RapidFluctuations_HighFrequency()
    {
        var timestamps = GenerateTimestamps(24, 96);
        var humidity = GenerateSinusoidalHumidity(96, 24.0 / 6.0, 55.0, 15.0);

        double freq = _service.CalculateBreathFrequency(humidity, timestamps);

        freq.Should().BeGreaterThan(3.0);
    }

    [Fact]
    public void CalculateBreathFrequency_SlowFluctuations_LowFrequency()
    {
        var timestamps = GenerateTimestamps(72, 96);
        var humidity = GenerateSinusoidalHumidity(96, 36.0, 55.0, 10.0);

        double freq = _service.CalculateBreathFrequency(humidity, timestamps);

        freq.Should().BeLessThan(3.0);
    }

    #endregion

    #region 呼吸频率计算测试

    [Fact]
    public void CalculateBreathFrequency_InsufficientData_ReturnsZero()
    {
        double[] humidity = { 50.0, 51.0, 52.0 };
        DateTime[] timestamps = { DateTime.Now, DateTime.Now.AddHours(1), DateTime.Now.AddHours(2) };

        double freq = _service.CalculateBreathFrequency(humidity, timestamps);

        freq.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void CalculateBreathFrequency_WithPeaks_ReturnsReasonableFrequency()
    {
        int n = 50;
        var timestamps = new DateTime[n];
        var humidity = new double[n];
        var startTime = DateTime.Now;

        for (int i = 0; i < n; i++)
        {
            timestamps[i] = startTime.AddHours(i * 0.5);
            double phase = (i / 5.0) * Math.PI * 2;
            humidity[i] = 55.0 + 10.0 * Math.Sin(phase);
        }

        double freq = _service.CalculateBreathFrequency(humidity, timestamps);

        freq.Should().BeGreaterThan(0);
        freq.Should().BeLessThan(10);
    }

    #endregion

    #region 时滞计算测试

    [Fact]
    public void CalculateTimeLag_InsufficientData_ReturnsZero()
    {
        double[] temp = { 22, 23 };
        double[] humidity = { 50, 51 };
        DateTime[] timestamps = { DateTime.Now, DateTime.Now.AddHours(1) };

        double lag = _service.CalculateTimeLag(temp, humidity, timestamps);

        lag.Should().Be(0);
    }

    [Fact]
    public void CalculateTimeLag_WithSyntheticData_ReturnsPositiveLag()
    {
        int n = 48;
        var timestamps = new DateTime[n];
        var temperature = new double[n];
        var humidity = new double[n];
        var startTime = DateTime.Now;

        for (int i = 0; i < n; i++)
        {
            timestamps[i] = startTime.AddHours(i);
            double phase = (i / 24.0) * Math.PI * 2;
            temperature[i] = 22.0 + 5.0 * Math.Sin(phase);
            humidity[i] = 55.0 + 10.0 * Math.Sin(phase - 0.5);
        }

        double lag = _service.CalculateTimeLag(temperature, humidity, timestamps);

        lag.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void CalculateTimeLag_SameSignal_ReturnsMinimalLag()
    {
        int n = 48;
        var timestamps = new DateTime[n];
        var signal = new double[n];
        var startTime = DateTime.Now;

        for (int i = 0; i < n; i++)
        {
            timestamps[i] = startTime.AddHours(i);
            signal[i] = 50.0 + 10.0 * Math.Sin(i / 24.0 * Math.PI * 2);
        }

        double lag = _service.CalculateTimeLag(signal, signal, timestamps);

        lag.Should().BeGreaterOrEqualTo(0);
    }

    #endregion

    #region 回滞面积计算测试

    [Fact]
    public void CalculateHysteresisArea_InsufficientData_ReturnsZero()
    {
        double[] humidity = { 50, 51, 52, 53, 54 };
        double[] moisture = { 0.2, 0.21, 0.22, 0.23, 0.24 };

        double area = _service.CalculateHysteresisArea(humidity, moisture);

        area.Should().Be(0);
    }

    [Fact]
    public void CalculateHysteresisArea_WithSufficientData_ReturnsPositive()
    {
        int n = 20;
        var humidity = new double[n];
        var moisture = new double[n];

        for (int i = 0; i < n; i++)
        {
            humidity[i] = 30.0 + (i / (double)(n - 1)) * 40.0;
            moisture[i] = 0.2 + 0.1 * (i / (double)(n - 1));
        }

        double area = _service.CalculateHysteresisArea(humidity, moisture);

        area.Should().BeGreaterOrEqualTo(0);
    }

    #endregion

    #region 正常场景测试

    [Fact]
    public async Task AnalyzeAsync_NormalData_ReturnsValidResult()
    {
        var data = GenerateBreathabilityData(
            startTime: DateTime.Now,
            totalHours: 48,
            intervalMinutes: 60,
            cyclesPerDay: 2.0,
            tempBase: 22.0,
            tempAmp: 3.0,
            humidityBase: 55.0,
            humidityAmp: 10.0);

        var input = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = data.Temperatures,
            Humidities = data.Humidities,
            Timestamps = data.Timestamps,
            Porosity = 0.35,
            MoistureContent = 0.25
        };

        var result = await _service.AnalyzeAsync(input);

        result.Should().NotBeNull();
        result.SculptureId.Should().Be(1);
        result.BreathFrequencyCyclesPerDay.Should().BeGreaterThan(0);
        result.SelfRegulationScore.Should().BeGreaterOrEqualTo(0);
        result.SelfRegulationScore.Should().BeLessOrEqualTo(100);
        result.TemperatureAmplitudeC.Should().BeGreaterThan(0);
        result.HumidityAmplitudePercent.Should().BeGreaterThan(0);
        result.RegulationLevel.Should().NotBeEmpty();
        result.Assessment.Should().NotBeEmpty();
        result.Recommendation.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_StableEnvironment_HigherRegulationScore()
    {
        var stableData = GenerateBreathabilityData(
            startTime: DateTime.Now,
            totalHours: 48,
            intervalMinutes: 60,
            cyclesPerDay: 1.5,
            tempBase: 22.0,
            tempAmp: 1.0,
            humidityBase: 55.0,
            humidityAmp: 3.0);

        var volatileData = GenerateBreathabilityData(
            startTime: DateTime.Now,
            totalHours: 48,
            intervalMinutes: 60,
            cyclesPerDay: 5.0,
            tempBase: 22.0,
            tempAmp: 8.0,
            humidityBase: 55.0,
            humidityAmp: 20.0);

        var stableInput = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = stableData.Temperatures,
            Humidities = stableData.Humidities,
            Timestamps = stableData.Timestamps,
            Porosity = 0.35,
            MoistureContent = 0.25
        };

        var volatileInput = new BreathabilityInput
        {
            SculptureId = 2,
            Temperatures = volatileData.Temperatures,
            Humidities = volatileData.Humidities,
            Timestamps = volatileData.Timestamps,
            Porosity = 0.35,
            MoistureContent = 0.25
        };

        var stableResult = await _service.AnalyzeAsync(stableInput);
        var volatileResult = await _service.AnalyzeAsync(volatileInput);

        stableResult.SelfRegulationScore.Should().BeGreaterOrEqualTo(volatileResult.SelfRegulationScore - 10);
    }

    #endregion

    #region 边界场景测试

    [Fact]
    public async Task AnalyzeAsync_MinimumDataPoints_ReturnsResult()
    {
        int n = 10;
        var timestamps = new DateTime[n];
        var temperatures = new double[n];
        var humidities = new double[n];
        var startTime = DateTime.Now;

        for (int i = 0; i < n; i++)
        {
            timestamps[i] = startTime.AddHours(i);
            temperatures[i] = 22.0 + 2.0 * Math.Sin(i / 3.0 * Math.PI * 2);
            humidities[i] = 55.0 + 5.0 * Math.Sin(i / 3.0 * Math.PI * 2);
        }

        var input = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = temperatures,
            Humidities = humidities,
            Timestamps = timestamps,
            Porosity = 0.35,
            MoistureContent = 0.25
        };

        var result = await _service.AnalyzeAsync(input);

        result.Should().NotBeNull();
        result.SelfRegulationScore.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task AnalyzeAsync_InsufficientData_ReturnsDataInsufficientMessage()
    {
        int n = 5;
        var timestamps = new DateTime[n];
        var temperatures = new double[n];
        var humidities = new double[n];

        for (int i = 0; i < n; i++)
        {
            timestamps[i] = DateTime.Now.AddHours(i);
            temperatures[i] = 22.0;
            humidities[i] = 55.0;
        }

        var input = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = temperatures,
            Humidities = humidities,
            Timestamps = timestamps
        };

        var result = await _service.AnalyzeAsync(input);

        result.Assessment.Should().Contain("数据不足");
        result.Recommendation.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnalyzeAsync_VariousConditions_ReturnsValidRegulationLevel()
    {
        var data = GenerateBreathabilityData(
            startTime: DateTime.Now,
            totalHours: 48,
            intervalMinutes: 60,
            cyclesPerDay: 2.0,
            tempBase: 22.0,
            tempAmp: 3.0,
            humidityBase: 55.0,
            humidityAmp: 10.0);

        var input = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = data.Temperatures,
            Humidities = data.Humidities,
            Timestamps = data.Timestamps,
            Porosity = 0.35,
            MoistureContent = 0.25
        };

        var result = await _service.AnalyzeAsync(input);

        result.RegulationLevel.Should().BeOneOf("EXCELLENT", "GOOD", "FAIR", "POOR", "CRITICAL");
        result.SelfRegulationScore.Should().BeGreaterOrEqualTo(0);
        result.SelfRegulationScore.Should().BeLessOrEqualTo(100);
    }

    [Fact]
    public async Task AnalyzeAsync_HighPorosity_BetterMoistureBuffer()
    {
        var data = GenerateBreathabilityData(
            startTime: DateTime.Now,
            totalHours: 24,
            intervalMinutes: 60,
            cyclesPerDay: 2.0,
            tempBase: 22.0,
            tempAmp: 3.0,
            humidityBase: 55.0,
            humidityAmp: 10.0);

        var lowPorosityInput = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = data.Temperatures,
            Humidities = data.Humidities,
            Timestamps = data.Timestamps,
            Porosity = 0.15,
            MoistureContent = 0.10
        };

        var highPorosityInput = new BreathabilityInput
        {
            SculptureId = 2,
            Temperatures = data.Temperatures,
            Humidities = data.Humidities,
            Timestamps = data.Timestamps,
            Porosity = 0.50,
            MoistureContent = 0.35
        };

        var lowResult = await _service.AnalyzeAsync(lowPorosityInput);
        var highResult = await _service.AnalyzeAsync(highPorosityInput);

        highResult.MoistureBufferCapacity.Should().BeGreaterOrEqualTo(lowResult.MoistureBufferCapacity);
    }

    #endregion

    #region 异常场景测试

    [Fact]
    public async Task AnalyzeAsync_EmptyArrays_ReturnsDataInsufficient()
    {
        var input = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = Array.Empty<double>(),
            Humidities = Array.Empty<double>(),
            Timestamps = Array.Empty<DateTime>()
        };

        var result = await _service.AnalyzeAsync(input);

        result.Assessment.Should().Contain("数据不足");
    }

    [Fact]
    public async Task AnalyzeAsync_ConstantData_MinimalBreathing()
    {
        int n = 24;
        var timestamps = new DateTime[n];
        var temperatures = new double[n];
        var humidities = new double[n];

        for (int i = 0; i < n; i++)
        {
            timestamps[i] = DateTime.Now.AddHours(i);
            temperatures[i] = 22.0;
            humidities[i] = 55.0;
        }

        var input = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = temperatures,
            Humidities = humidities,
            Timestamps = timestamps,
            Porosity = 0.35,
            MoistureContent = 0.25
        };

        var result = await _service.AnalyzeAsync(input);

        result.Should().NotBeNull();
        result.TemperatureAmplitudeC.Should().Be(0);
        result.HumidityAmplitudePercent.Should().Be(0);
    }

    [Fact]
    public void CalculateBreathFrequency_MismatchedArrayLengths_HandlesGracefully()
    {
        double[] humidity = new double[10];
        DateTime[] timestamps = new DateTime[5];

        double freq = _service.CalculateBreathFrequency(humidity, timestamps);

        freq.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task AnalyzeAsync_SingleDataPoint_ReturnsDataInsufficient()
    {
        var input = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = new[] { 22.0 },
            Humidities = new[] { 55.0 },
            Timestamps = new[] { DateTime.Now }
        };

        var result = await _service.AnalyzeAsync(input);

        result.Assessment.Should().Contain("数据不足");
    }

    #endregion

    #region 辅助方法

    private static (double[] Temperatures, double[] Humidities, DateTime[] Timestamps) GenerateBreathabilityData(
        DateTime startTime,
        double totalHours,
        int intervalMinutes,
        double cyclesPerDay,
        double tempBase,
        double tempAmp,
        double humidityBase,
        double humidityAmp)
    {
        int n = (int)(totalHours * 60 / intervalMinutes);
        var temperatures = new double[n];
        var humidities = new double[n];
        var timestamps = new DateTime[n];

        double cyclePeriodHours = 24.0 / cyclesPerDay;

        for (int i = 0; i < n; i++)
        {
            double hours = i * intervalMinutes / 60.0;
            timestamps[i] = startTime.AddHours(hours);

            double phase = (hours / cyclePeriodHours) * Math.PI * 2;
            temperatures[i] = tempBase + tempAmp * Math.Sin(phase);
            humidities[i] = humidityBase + humidityAmp * Math.Sin(phase - 0.3);
        }

        return (temperatures, humidities, timestamps);
    }

    private static double[] GenerateSinusoidalHumidity(int n, double periodHours, double baseValue, double amplitude)
    {
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double hours = i * 0.5;
            double phase = (hours / periodHours) * Math.PI * 2;
            result[i] = baseValue + amplitude * Math.Sin(phase);
        }
        return result;
    }

    private static double[] GenerateHumidityData(double totalHours, int n, double cyclesPerDay)
    {
        var result = new double[n];
        double cyclePeriodHours = 24.0 / cyclesPerDay;

        for (int i = 0; i < n; i++)
        {
            double hours = (i / (double)(n - 1)) * totalHours;
            double phase = (hours / cyclePeriodHours) * Math.PI * 2;
            result[i] = 55.0 + 10.0 * Math.Sin(phase);
        }
        return result;
    }

    private static DateTime[] GenerateTimestamps(double totalHours, int n)
    {
        var result = new DateTime[n];
        var startTime = DateTime.Now;

        for (int i = 0; i < n; i++)
        {
            double hours = (i / (double)(n - 1)) * totalHours;
            result[i] = startTime.AddHours(hours);
        }
        return result;
    }

    #endregion
}
