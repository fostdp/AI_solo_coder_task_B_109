using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using reaction_warning;
using respiration_eval;
using SkiaSharp;
using virtual_coating;
using WashburnPenetration;
using WashburnPenetration.Models;
using Xunit;

namespace ClayMonitor.Workers.Tests;

public class RegressionTests
{
    [Fact]
    public void WashburnWorker_LayeredModel_ShouldMatchOriginalImplementation()
    {
        var options = Options.Create(new PenetrationPredictionOptions
        {
            DefaultPorosity = 0.35,
            DefaultPoreRadiusNm = 500.0
        });
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var service = new PenetrationPredictionWorker(mockBus.Object, options);

        var layers = new[]
        {
            new SculptureLayer { LayerName = "彩绘层", ThicknessMm = 0.3, Porosity = 0.15, PoreRadiusNm = 100, Tortuosity = 2.5 },
            new SculptureLayer { LayerName = "地仗层", ThicknessMm = 3.0, Porosity = 0.35, PoreRadiusNm = 500, Tortuosity = 1.8 },
            new SculptureLayer { LayerName = "胎体层", ThicknessMm = 20.0, Porosity = 0.45, PoreRadiusNm = 1500, Tortuosity = 1.4 }
        };

        double t = 3600;
        double gamma = 0.0235;
        double theta = 95.0 * Math.PI / 180.0;
        double eta = 0.00085;

        double depth = service.CalculateLayeredLucasWashburn(t, gamma, theta, eta, layers);

        depth.Should().BeGreaterThan(0);
        depth.Should().BeLessThan(50.0);
    }

    [Fact]
    public async Task WashburnWorker_ParallelPredict_ShouldProduceSameResultsAsSerial()
    {
        var options = Options.Create(new PenetrationPredictionOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var service = new PenetrationPredictionWorker(mockBus.Object, options);

        var inputs = Enumerable.Range(1, 10).Select(i => new PenetrationInput
        {
            SculptureId = i,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            ContactAngleDeg = 95,
            TimeSeconds = 3600
        }).ToArray();

        var serialResults = await service.PredictBatchAsync(inputs, CancellationToken.None);
        var parallelResults = await service.ParallelPredictBatchAsync(inputs, CancellationToken.None);

        parallelResults.Length.Should().Be(serialResults.Length);
        for (int i = 0; i < serialResults.Length; i++)
        {
            parallelResults[i].PredictedDepthMm.Should().BeApproximately(
                serialResults[i].PredictedDepthMm, 1e-6);
            parallelResults[i].PenetrationGrade.Should().Be(serialResults[i].PenetrationGrade);
        }
    }

    [Fact]
    public void WashburnWorker_GetDefaultClayLayers_ShouldHaveThreeLayers()
    {
        var options = Options.Create(new PenetrationPredictionOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var service = new PenetrationPredictionWorker(mockBus.Object, options);

        var layers = service.GetDefaultClayLayers();

        layers.Length.Should().Be(3);
        layers[0].LayerName.Should().Be("彩绘层");
        layers[0].Porosity.Should().Be(0.15);
        layers[1].LayerName.Should().Be("地仗层");
        layers[1].Porosity.Should().Be(0.35);
        layers[2].LayerName.Should().Be("胎体层");
        layers[2].Porosity.Should().Be(0.45);
    }

    [Fact]
    public void ReactionWorker_Cache_ShouldBePrecomputed()
    {
        var options = Options.Create(new ChemicalReactionOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var service = new ChemicalReactionWorker(mockBus.Object, options);

        var cacheSize = service.GetCacheSize();
        cacheSize.Should().Be(3168);

        var key = new ThermodynamicLookupKey
        {
            ReactionKey = "TEOS_Na2SO4",
            TemperatureC = 25,
            PHx10 = 70,
            HumidityPct = 55
        };

        bool hit = service.TryGetCachedThermodynamics(key, out var values);
        hit.Should().BeTrue();
        values.DeltaGkJmol.Should().BeLessThan(0);
        values.EquilibriumConstant.Should().BeGreaterThan(0);
        service.GetCacheHitCount().Should().Be(1);
    }

    [Fact]
    public async Task ReactionWorker_HighSalt_TEOS_ShouldReturnHighRisk()
    {
        var options = Options.Create(new ChemicalReactionOptions
        {
            HarmfulProductThreshold = 0.15,
            CriticalDeltaGThreshold = -50.0,
            WarningDeltaGThreshold = -20.0
        });
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var service = new ChemicalReactionWorker(mockBus.Object, options);

        var input = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 0.5,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 7.5,
            RelativeHumidity = 0.6,
            ContactTimeHours = 72.0
        };

        var result = await service.EvaluateReactionAsync(input, CancellationToken.None);

        result.WarningLevel.Should().BeOneOf("WARNING", "CRITICAL");
        result.RequiresWarning.Should().BeTrue();
        result.HarmfulProductYield.Should().BeGreaterThan(0.1);
        result.GibbsFreeEnergyKJmol.Should().BeLessThan(0);
    }

    [Fact]
    public async Task ReactionWorker_LookupTable_ShouldBeFasterThanDirect()
    {
        var options = Options.Create(new ChemicalReactionOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var service = new ChemicalReactionWorker(mockBus.Object, options);

        var key = new ThermodynamicLookupKey
        {
            ReactionKey = "TEOS_Na2SO4",
            TemperatureC = 25,
            PHx10 = 70,
            HumidityPct = 55
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            service.TryGetCachedThermodynamics(key, out _);
        }
        sw.Stop();
        long cacheTime = sw.ElapsedMilliseconds;

        cacheTime.Should().BeLessThan(100);
    }

    [Fact]
    public async Task RespirationWorker_SensorLagCorrection_ShouldReduceLag()
    {
        var options = Options.Create(new BreathabilityOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var service = new BreathabilityWorker(mockBus.Object, options);

        int n = 100;
        var timestamps = new DateTime[n];
        var rawT = new double[n];
        var rawRH = new double[n];
        var baseTime = DateTime.Now;

        double truePeriodMinutes = 60;
        for (int i = 0; i < n; i++)
        {
            timestamps[i] = baseTime.AddMinutes(i);
            double phase = 2 * Math.PI * i / truePeriodMinutes;
            rawT[i] = 25.0 + 3.0 * Math.Sin(phase - 0.2);
            rawRH[i] = 55.0 + 10.0 * Math.Sin(phase);
        }

        var input = new BreathabilityInput
        {
            SculptureId = 1,
            Temperatures = rawT,
            Humidities = rawRH,
            Timestamps = timestamps,
            Porosity = 0.35,
            MoistureContent = 0.25,
            SensorAccuracy = new SensorAccuracyConfig
            {
                TemperatureAccuracyC = 0.3,
                HumidityAccuracyPercent = 2.0,
                SensorResponseTimeSeconds = 15.0,
                HysteresisErrorPercent = 1.5,
                ApplyCorrection = true
            }
        };

        var result = await service.AnalyzeAsync(input, CancellationToken.None);

        result.SensorLagCorrectionApplied.Should().BeTrue();
        result.CorrectedTimeLagMinutes.Should().BeLessThan(result.TimeLagMinutes);
        result.SensorLagContributionMinutes.Should().BeGreaterThan(0);
        result.RawUncertaintyPercent.Should().BeGreaterThan(0);
        result.BreathFrequencyCyclesPerDay.Should().BeApproximately(24.0 / truePeriodMinutes * 60, 2.0);
    }

    [Fact]
    public void RespirationWorker_CorrectedSignal_ShouldPreserveAmplitude()
    {
        var options = Options.Create(new BreathabilityOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var service = new BreathabilityWorker(mockBus.Object, options);

        int n = 50;
        var timestamps = new DateTime[n];
        var raw = new double[n];
        var baseTime = DateTime.Now;

        for (int i = 0; i < n; i++)
        {
            timestamps[i] = baseTime.AddSeconds(i * 30);
            double phase = 2 * Math.PI * i / 20.0;
            double trueSignal = 50.0 + 10.0 * Math.Sin(phase);
            double tau = 15.0;
            double dt = 30.0;
            double alpha = 1 - Math.Exp(-dt / tau);
            raw[i] = i == 0 ? trueSignal : raw[i - 1] + alpha * (trueSignal - raw[i - 1]);
        }

        var corrected = service.ApplySensorLagCorrection(raw, timestamps, 15.0);

        double rawAmp = raw.Max() - raw.Min();
        double correctedAmp = corrected.Max() - corrected.Min();

        correctedAmp.Should().BeGreaterThan(rawAmp);
        correctedAmp.Should().BeApproximately(20.0, 3.0);
    }

    [Fact]
    public void RespirationWorker_HighFrequency_ShouldIdentifyAsWeak()
    {
        var options = Options.Create(new BreathabilityOptions
        {
            PoorRegulationThreshold = 40.0
        });
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var service = new BreathabilityWorker(mockBus.Object, options);

        int n = 100;
        var timestamps = new DateTime[n];
        var T = new double[n];
        var RH = new double[n];
        var baseTime = DateTime.Now;

        for (int i = 0; i < n; i++)
        {
            timestamps[i] = baseTime.AddMinutes(i * 5);
            double phase = 2 * Math.PI * i / 4.0;
            T[i] = 25.0 + 2.0 * Math.Sin(phase);
            RH[i] = 55.0 + 8.0 * Math.Sin(phase + 0.5);
        }

        var freq = service.CalculateBreathFrequency(RH, timestamps);

        freq.Should().BeGreaterThan(4.0);
    }

    [Fact]
    public async Task VirtualCoatingWorker_SkiaSharp_ShouldGeneratePngImage()
    {
        var penOptions = Options.Create(new PenetrationPredictionOptions());
        var coatOptions = Options.Create(new VirtualReinforcementOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var penService = new PenetrationPredictionWorker(mockBus.Object, penOptions);
        var coatService = new VirtualCoatingWorker(mockBus.Object, coatOptions, penService);

        var input = new VirtualCoatingInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500,
            ApplicationTimeSeconds = 3600,
            SculptureWidthCm = 40.0,
            SculptureHeightCm = 60.0,
            SculptureThicknessCm = 5.0,
            OriginalGloss = 30.0,
            CoordinateResolutionX = 20,
            CoordinateResolutionY = 30,
            CoordinateResolutionZ = 15
        };

        var imageBytes = await coatService.RenderImageAsync(input, CancellationToken.None);

        imageBytes.Should().NotBeNull();
        imageBytes.Length.Should().BeGreaterThan(1000);

        imageBytes[0].Should().Be(0x89);
        imageBytes[1].Should().Be(0x50);
        imageBytes[2].Should().Be(0x4E);
        imageBytes[3].Should().Be(0x47);

        using var ms = new MemoryStream(imageBytes);
        using var skImage = SKImage.FromEncodedData(ms);
        skImage.Should().NotBeNull();
        skImage.Width.Should().Be(800);
        skImage.Height.Should().Be(600);
    }

    [Fact]
    public async Task VirtualCoatingWorker_DifferentMaterials_ShouldHaveDifferentGloss()
    {
        var penOptions = Options.Create(new PenetrationPredictionOptions());
        var coatOptions = Options.Create(new VirtualReinforcementOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var penService = new PenetrationPredictionWorker(mockBus.Object, penOptions);
        var coatService = new VirtualCoatingWorker(mockBus.Object, coatOptions, penService);

        var materials = new[]
        {
            "TEOS (正硅酸乙酯)",
            "纳米石灰 (Ca(OH)₂)",
            "丙烯酸树脂 (Paraloid B72)",
            "硅丙乳液"
        };

        var results = new List<VirtualCoatingResult>();
        foreach (var mat in materials)
        {
            var input = new VirtualCoatingInput
            {
                SculptureId = 1,
                MaterialName = mat,
                Porosity = 0.35,
                PoreRadiusNm = 500,
                ApplicationTimeSeconds = 3600,
                OriginalGloss = 30.0
            };
            results.Add(await coatService.SimulateAsync(input, CancellationToken.None));
        }

        var glossChanges = results.Select(r => r.GlossChangePercent).ToArray();
        glossChanges.Distinct().Count().Should().BeGreaterThan(2);

        var teosResult = results.First(r => r.MaterialName == "TEOS (正硅酸乙酯)");
        var limeResult = results.First(r => r.MaterialName == "纳米石灰 (Ca(OH)₂)");
        teosResult.GlossChangePercent.Should().BeGreaterThan(limeResult.GlossChangePercent);
    }

    [Fact]
    public async Task VirtualCoatingWorker_Generate3DVoxels_ShouldHaveValidStructure()
    {
        var penOptions = Options.Create(new PenetrationPredictionOptions());
        var coatOptions = Options.Create(new VirtualReinforcementOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var penService = new PenetrationPredictionWorker(mockBus.Object, penOptions);
        var coatService = new VirtualCoatingWorker(mockBus.Object, coatOptions, penService);

        var input = new VirtualCoatingInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 15,
            CoordinateResolutionZ = 8
        };

        var result = await coatService.SimulateAsync(input, CancellationToken.None);

        result.Voxels.Length.Should().Be(10 * 15 * 8);
        result.Voxels.Count(v => v.IsReinforced).Should().BeGreaterThan(0);
        result.AveragePenetrationDepthMm.Should().BeGreaterThan(0);
        result.MaximumPenetrationDepthMm.Should().BeGreaterThanOrEqualTo(result.AveragePenetrationDepthMm);
        result.IsoSurfaces.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EndToEnd_PenetrationThenVirtualCoating_ShouldIntegrate()
    {
        var options = Options.Create(new PenetrationPredictionOptions());
        var coatOptions = Options.Create(new VirtualReinforcementOptions());
        var mockBus = new Mock<ClayMonitor.Core.Channels.IMessageBus>();
        var penService = new PenetrationPredictionWorker(mockBus.Object, options);
        var coatService = new VirtualCoatingWorker(mockBus.Object, coatOptions, penService);

        var penInput = new PenetrationInput
        {
            SculptureId = 42,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            ContactAngleDeg = 95,
            TimeSeconds = 3600,
            UseLayeredModel = true
        };

        var penResult = await penService.PredictAsync(penInput, CancellationToken.None);
        penResult.UsedLayeredModel.Should().BeTrue();
        penResult.LayerBreakdown.Length.Should().BeGreaterThan(0);
        penResult.PredictedDepthMm.Should().BeGreaterThan(0);

        var coatInput = new VirtualCoatingInput
        {
            SculptureId = 42,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500,
            ApplicationTimeSeconds = 3600
        };

        var coatResult = await coatService.SimulateAsync(coatInput, CancellationToken.None);
        coatResult.MaterialName.Should().Be(penResult.MaterialName);
        coatResult.SculptureId.Should().Be(penResult.SculptureId);
        coatResult.AveragePenetrationDepthMm.Should().BeGreaterThan(0);
    }
}
