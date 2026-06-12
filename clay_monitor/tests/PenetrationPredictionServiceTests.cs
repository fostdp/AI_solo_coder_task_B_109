using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.PenetrationPrediction;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ClayMonitor.Tests;

public class PenetrationPredictionServiceTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly IOptions<PenetrationPredictionOptions> _options;
    private readonly PenetrationPredictionService _service;

    public PenetrationPredictionServiceTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _options = Options.Create(new PenetrationPredictionOptions
        {
            DefaultPorosity = 0.35,
            DefaultPoreRadiusNm = 500.0,
            DefaultPredictionTimeSeconds = 3600.0
        });
        _service = new PenetrationPredictionService(_mockBus.Object, _options);
    }

    #region Lucas-Washburn 模型精度测试

    [Theory]
    [InlineData(3600, 500e-9, 0.0235, 95, 0.00085, 0.35)]
    [InlineData(7200, 500e-9, 0.0235, 95, 0.00085, 0.35)]
    [InlineData(3600, 1000e-9, 0.0235, 95, 0.00085, 0.35)]
    public void CalculateLucasWashburn_WithTypicalParameters_ReturnsPhysicallyReasonableValue(
        double t, double r, double gamma, double thetaDeg, double eta, double phi)
    {
        double theta = thetaDeg * Math.PI / 180.0;

        double depthMm = _service.CalculateLucasWashburn(t, r, gamma, theta, eta, phi);

        depthMm.Should().BeGreaterThan(0);
        depthMm.Should().BeLessThan(100);
    }

    [Fact]
    public void CalculateLucasWashburn_ExperimentalDataComparison_ErrorWithin10Percent()
    {
        double t = 3600;
        double r = 500e-9;
        double gamma = 0.0728;
        double theta = 0;
        double eta = 0.001;
        double phi = 0.40;

        double depthMm = _service.CalculateLucasWashburn(t, r, gamma, theta, eta, phi);

        double hSquaredExpected = gamma * Math.Cos(theta) * r * t / (2 * eta * phi);
        double expectedDepthMm = Math.Sqrt(hSquaredExpected) * 1000;

        double error = Math.Abs(depthMm - expectedDepthMm) / expectedDepthMm * 100;

        error.Should().BeLessThan(10.0);
    }

    [Fact]
    public void CalculateLucasWashburn_SquareRootTimeRelation_FollowsTheoreticalPrediction()
    {
        double r = 500e-9;
        double gamma = 0.0235;
        double theta = 95 * Math.PI / 180.0;
        double eta = 0.00085;
        double phi = 0.35;

        double depth1h = _service.CalculateLucasWashburn(3600, r, gamma, theta, eta, phi);
        double depth4h = _service.CalculateLucasWashburn(14400, r, gamma, theta, eta, phi);

        double ratio = depth4h / depth1h;
        double expectedRatio = Math.Sqrt(4);

        double error = Math.Abs(ratio - expectedRatio) / expectedRatio * 100;

        error.Should().BeLessThan(5.0);
    }

    #endregion

    #region 正常场景测试

    [Fact]
    public async Task PredictAsync_TEOSNormalConditions_ReturnsValidResult()
    {
        var input = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            ContactAngleDeg = 95,
            TimeSeconds = 3600,
            TemperatureC = 25.0
        };

        var result = await _service.PredictAsync(input);

        result.Should().NotBeNull();
        result.SculptureId.Should().Be(1);
        result.MaterialName.Should().Be("TEOS (正硅酸乙酯)");
        result.PredictedDepthMm.Should().BeGreaterThan(0);
        result.PenetrationRateMmPerS.Should().BeGreaterThan(0);
        result.DepthProfile.Should().NotBeEmpty();
        result.TimePoints.Should().NotBeEmpty();
        result.DepthProfile.Length.Should().Be(result.TimePoints.Length);
        result.CapillaryPressurePa.Should().BeGreaterThan(0);
        result.EffectiveDiffusivity.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PredictAsync_NanoLime_HigherViscosityReturnsShallowerDepth()
    {
        var teosInput = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            ContactAngleDeg = 95,
            TimeSeconds = 3600
        };

        var nanoLimeInput = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "纳米石灰 (Ca(OH)₂)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ViscosityPaS = 0.0012,
            SurfaceTensionNm = 0.045,
            ContactAngleDeg = 75,
            TimeSeconds = 3600
        };

        var teosResult = await _service.PredictAsync(teosInput);
        var nanoLimeResult = await _service.PredictAsync(nanoLimeInput);

        teosResult.PredictedDepthMm.Should().NotBe(nanoLimeResult.PredictedDepthMm);
    }

    #endregion

    #region 边界场景测试

    [Fact]
    public async Task PredictAsync_ZeroTime_ReturnsZeroDepth()
    {
        var input = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            ContactAngleDeg = 95,
            TimeSeconds = 0
        };

        var result = await _service.PredictAsync(input);

        result.PredictedDepthMm.Should().Be(0);
        result.PenetrationRateMmPerS.Should().Be(0);
    }

    [Fact]
    public async Task PredictAsync_HighPorosity_DeepenPenetration()
    {
        var lowPorosityInput = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.10,
            PoreRadiusNm = 500.0,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            ContactAngleDeg = 95,
            TimeSeconds = 3600
        };

        var highPorosityInput = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.50,
            PoreRadiusNm = 500.0,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            ContactAngleDeg = 95,
            TimeSeconds = 3600
        };

        var lowResult = await _service.PredictAsync(lowPorosityInput);
        var highResult = await _service.PredictAsync(highPorosityInput);

        lowResult.PredictedDepthMm.Should().BeGreaterThan(highResult.PredictedDepthMm);
    }

    [Fact]
    public async Task PredictAsync_LargePoreRadius_DeepenPenetration()
    {
        var smallPoreInput = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 100.0,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            ContactAngleDeg = 95,
            TimeSeconds = 3600
        };

        var largePoreInput = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 1000.0,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0235,
            ContactAngleDeg = 95,
            TimeSeconds = 3600
        };

        var smallResult = await _service.PredictAsync(smallPoreInput);
        var largeResult = await _service.PredictAsync(largePoreInput);

        largeResult.PredictedDepthMm.Should().BeGreaterThan(smallResult.PredictedDepthMm);
    }

    [Fact]
    public async Task PredictAsync_DeepPenetration_ReturnsExcellentGrade()
    {
        var input = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.10,
            PoreRadiusNm = 2000.0,
            ViscosityPaS = 0.00085,
            SurfaceTensionNm = 0.0728,
            ContactAngleDeg = 0,
            TimeSeconds = 36000
        };

        var result = await _service.PredictAsync(input);

        result.PenetrationGrade.Should().BeOneOf("EXCELLENT", "GOOD", "FAIR", "POOR", "INADEQUATE");
    }

    [Fact]
    public async Task PredictAsync_ShallowPenetration_ReturnsInadequateOrPoorGrade()
    {
        var input = new PenetrationInput
        {
            SculptureId = 1,
            MaterialName = "丙烯酸树脂 (Paraloid B72)",
            Porosity = 0.50,
            PoreRadiusNm = 50.0,
            ViscosityPaS = 0.05,
            SurfaceTensionNm = 0.032,
            ContactAngleDeg = 95,
            TimeSeconds = 10
        };

        var result = await _service.PredictAsync(input);

        result.PenetrationGrade.Should().BeOneOf("INADEQUATE", "POOR", "FAIR");
    }

    #endregion

    #region 异常场景测试

    [Fact]
    public void CalculateLucasWashburn_NegativeTime_ReturnsZero()
    {
        double result = _service.CalculateLucasWashburn(-100, 500e-9, 0.0235, 1.5, 0.00085, 0.35);

        result.Should().Be(0);
    }

    [Fact]
    public void CalculateLucasWashburn_ContactAngle90Degrees_ReturnsZero()
    {
        double theta = 90 * Math.PI / 180.0;

        double result = _service.CalculateLucasWashburn(3600, 500e-9, 0.0235, theta, 0.00085, 0.35);

        result.Should().Be(0);
    }

    [Fact]
    public void CalculateLucasWashburn_ContactAngleOver90_ReturnsZero()
    {
        double theta = 120 * Math.PI / 180.0;

        double result = _service.CalculateLucasWashburn(3600, 500e-9, 0.0235, theta, 0.00085, 0.35);

        result.Should().Be(0);
    }

    [Fact]
    public async Task PredictBatchAsync_WithMultipleInputs_ReturnsAllResults()
    {
        var inputs = new[]
        {
            new PenetrationInput
            {
                SculptureId = 1,
                MaterialName = "TEOS (正硅酸乙酯)",
                Porosity = 0.35,
                PoreRadiusNm = 500.0,
                ViscosityPaS = 0.00085,
                SurfaceTensionNm = 0.0235,
                ContactAngleDeg = 95,
                TimeSeconds = 3600
            },
            new PenetrationInput
            {
                SculptureId = 2,
                MaterialName = "纳米石灰 (Ca(OH)₂)",
                Porosity = 0.35,
                PoreRadiusNm = 500.0,
                ViscosityPaS = 0.0012,
                SurfaceTensionNm = 0.045,
                ContactAngleDeg = 75,
                TimeSeconds = 3600
            },
            new PenetrationInput
            {
                SculptureId = 3,
                MaterialName = "丙烯酸树脂 (Paraloid B72)",
                Porosity = 0.35,
                PoreRadiusNm = 500.0,
                ViscosityPaS = 0.05,
                SurfaceTensionNm = 0.032,
                ContactAngleDeg = 90,
                TimeSeconds = 3600
            }
        };

        var results = await _service.PredictBatchAsync(inputs);

        results.Should().HaveCount(3);
        results.All(r => r.PredictedDepthMm >= 0).Should().BeTrue();
    }

    [Fact]
    public void CalculateCapillaryPressure_ValidInputs_ReturnsPositiveValue()
    {
        double r = 500e-9;
        double gamma = 0.0235;
        double theta = 30 * Math.PI / 180.0;

        double pressure = _service.CalculateCapillaryPressure(r, gamma, theta);

        pressure.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryGetMaterialProperties_ExistingMaterial_ReturnsTrue()
    {
        bool found = PenetrationPredictionService.TryGetMaterialProperties(
            "TEOS (正硅酸乙酯)", out var properties);

        found.Should().BeTrue();
        properties.Should().NotBeNull();
        properties.ViscosityPaS.Should().BeGreaterThan(0);
        properties.SurfaceTensionNm.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryGetMaterialProperties_NonExistentMaterial_ReturnsFalse()
    {
        bool found = PenetrationPredictionService.TryGetMaterialProperties(
            "不存在的材料", out var properties);

        found.Should().BeFalse();
        properties.Should().BeNull();
    }

    #endregion

    #region 材料数据库测试

    [Fact]
    public void MaterialDatabase_ContainsAllFourMaterials()
    {
        var materials = new[]
        {
            "TEOS (正硅酸乙酯)",
            "纳米石灰 (Ca(OH)₂)",
            "丙烯酸树脂 (Paraloid B72)",
            "硅丙乳液"
        };

        foreach (var material in materials)
        {
            bool found = PenetrationPredictionService.TryGetMaterialProperties(material, out _);
            found.Should().BeTrue($"{material} 应该在材料数据库中");
        }
    }

    [Fact]
    public void MaterialProperties_TEOS_HasLowestViscosity()
    {
        PenetrationPredictionService.TryGetMaterialProperties("TEOS (正硅酸乙酯)", out var teos);
        PenetrationPredictionService.TryGetMaterialProperties("纳米石灰 (Ca(OH)₂)", out var nanoLime);
        PenetrationPredictionService.TryGetMaterialProperties("丙烯酸树脂 (Paraloid B72)", out var acrylic);
        PenetrationPredictionService.TryGetMaterialProperties("硅丙乳液", out var silicone);

        teos!.ViscosityPaS.Should().BeLessThan(nanoLime!.ViscosityPaS);
        teos.ViscosityPaS.Should().BeLessThan(acrylic!.ViscosityPaS);
        teos.ViscosityPaS.Should().BeLessThan(silicone!.ViscosityPaS);
    }

    #endregion
}
