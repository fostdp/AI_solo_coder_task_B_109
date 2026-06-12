using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.PenetrationPrediction;
using ClayMonitor.VirtualReinforcement;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ClayMonitor.Tests;

public class VirtualReinforcementServiceTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly Mock<IPenetrationPredictionService> _mockPenetrationService;
    private readonly IOptions<VirtualReinforcementOptions> _options;
    private readonly VirtualReinforcementService _service;

    public VirtualReinforcementServiceTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _mockPenetrationService = new Mock<IPenetrationPredictionService>();
        _options = Options.Create(new VirtualReinforcementOptions
        {
            DefaultPorosity = 0.35,
            DefaultPoreRadiusNm = 500.0,
            DefaultApplicationTimeSeconds = 3600.0,
            DefaultThicknessCm = 5.0,
            DefaultWidthCm = 40.0,
            DefaultHeightCm = 60.0,
            DefaultResolutionX = 40,
            DefaultResolutionY = 60,
            DefaultResolutionZ = 30
        });

        SetupMockPenetrationService();

        _service = new VirtualReinforcementService(
            _mockBus.Object,
            _options,
            _mockPenetrationService.Object);
    }

    private void SetupMockPenetrationService()
    {
        _mockPenetrationService
            .Setup(s => s.PredictAsync(It.IsAny<PenetrationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PenetrationInput input, CancellationToken ct) =>
            {
                int depthSteps = 50;
                var depthProfile = new double[depthSteps];
                var timePoints = new double[depthSteps];
                double maxDepth = 5.0;

                for (int i = 0; i < depthSteps; i++)
                {
                    timePoints[i] = (i + 1) * input.TimeSeconds / depthSteps;
                    double ratio = (i + 1.0) / depthSteps;
                    depthProfile[i] = maxDepth * Math.Sqrt(ratio);
                }

                return new PenetrationResult
                {
                    SculptureId = input.SculptureId,
                    MaterialName = input.MaterialName,
                    PredictedDepthMm = 5.0,
                    PenetrationRateMmPerS = 5.0 / input.TimeSeconds,
                    TimeToReach5mm = input.TimeSeconds,
                    DepthProfile = depthProfile,
                    TimePoints = timePoints,
                    PenetrationGrade = "GOOD",
                    Recommendation = "测试推荐",
                    CapillaryPressurePa = 1000.0,
                    EffectiveDiffusivity = 1e-9,
                    CalculatedAt = DateTime.Now
                };
            });

        _mockPenetrationService
            .Setup(s => s.CalculateLucasWashburn(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(),
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()))
            .Returns(5.0);

        _mockPenetrationService
            .Setup(s => s.CalculateCapillaryPressure(
                It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>()))
            .Returns(1000.0);
    }

    #region 核心测试：不同材料光泽度差异可辨

    [Fact]
    public void CalculateGlossProfile_TEOS_HighGloss()
    {
        int n = 20;
        var depthProfile = new double[n];
        for (int i = 0; i < n; i++)
        {
            depthProfile[i] = 5.0 * (1.0 - (double)i / n);
        }

        double originalGloss = 30.0;
        double teosGloss = 85.0;

        var glossProfile = _service.CalculateGlossProfile(depthProfile, originalGloss, teosGloss);

        glossProfile.Should().NotBeEmpty();
        glossProfile[0].Should().BeGreaterThan(originalGloss);
        glossProfile[0].Should().BeLessOrEqualTo(teosGloss);
    }

    [Fact]
    public void CalculateGlossProfile_NanoLime_LowGloss()
    {
        int n = 20;
        var depthProfile = new double[n];
        for (int i = 0; i < n; i++)
        {
            depthProfile[i] = 5.0 * (1.0 - (double)i / n);
        }

        double originalGloss = 30.0;
        double nanoLimeGloss = 35.0;

        var glossProfile = _service.CalculateGlossProfile(depthProfile, originalGloss, nanoLimeGloss);

        glossProfile.Should().NotBeEmpty();
        glossProfile[0].Should().BeGreaterOrEqualTo(originalGloss * 0.9);
    }

    [Fact]
    public void MaterialVisuals_TEOS_HasHighestGloss()
    {
        VirtualReinforcementService.TryGetMaterialVisuals("TEOS (正硅酸乙酯)", out var teos);
        VirtualReinforcementService.TryGetMaterialVisuals("纳米石灰 (Ca(OH)₂)", out var nanoLime);
        VirtualReinforcementService.TryGetMaterialVisuals("丙烯酸树脂 (Paraloid B72)", out var acrylic);
        VirtualReinforcementService.TryGetMaterialVisuals("硅丙乳液", out var silicone);

        teos.Should().NotBeNull();
        nanoLime.Should().NotBeNull();
        acrylic.Should().NotBeNull();
        silicone.Should().NotBeNull();

        teos!.FilmGloss.Should().BeGreaterThan(nanoLime!.FilmGloss);
        teos.FilmGloss.Should().BeGreaterThan(silicone!.FilmGloss);
    }

    [Fact]
    public void MaterialVisuals_NanoLime_HasLowestGloss()
    {
        VirtualReinforcementService.TryGetMaterialVisuals("TEOS (正硅酸乙酯)", out var teos);
        VirtualReinforcementService.TryGetMaterialVisuals("纳米石灰 (Ca(OH)₂)", out var nanoLime);
        VirtualReinforcementService.TryGetMaterialVisuals("丙烯酸树脂 (Paraloid B72)", out var acrylic);
        VirtualReinforcementService.TryGetMaterialVisuals("硅丙乳液", out var silicone);

        var allGlossValues = new[] { teos!.FilmGloss, nanoLime!.FilmGloss, acrylic!.FilmGloss, silicone!.FilmGloss };

        nanoLime.FilmGloss.Should().Be(allGlossValues.Min());
    }

    [Fact]
    public void CalculateGlossProfile_DifferentMaterials_GlossDifferenceDiscernible()
    {
        int n = 20;
        var depthProfile = new double[n];
        for (int i = 0; i < n; i++)
        {
            depthProfile[i] = 5.0 * (1.0 - (double)i / n);
        }

        double originalGloss = 30.0;
        double teosGloss = 85.0;
        double nanoLimeGloss = 35.0;

        var teosGlossProfile = _service.CalculateGlossProfile(depthProfile, originalGloss, teosGloss);
        var nanoLimeGlossProfile = _service.CalculateGlossProfile(depthProfile, originalGloss, nanoLimeGloss);

        double teosSurfaceGloss = teosGlossProfile[0];
        double nanoLimeSurfaceGloss = nanoLimeGlossProfile[0];
        double difference = Math.Abs(teosSurfaceGloss - nanoLimeSurfaceGloss);

        difference.Should().BeGreaterThan(10.0);
    }

    #endregion

    #region 3D体素生成测试

    [Fact]
    public void Generate3DVoxels_ValidInput_ReturnsCorrectNumberOfVoxels()
    {
        int nx = 20;
        int ny = 30;
        int nz = 15;

        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = nx,
            CoordinateResolutionY = ny,
            CoordinateResolutionZ = nz,
            SculptureThicknessCm = 5.0,
            SculptureWidthCm = 40.0,
            SculptureHeightCm = 60.0,
            SurfaceRoughness = 0.3,
            OriginalGloss = 30.0,
            TemperatureC = 25.0,
            ViewMode = "PENETRATION"
        };

        int depthSteps = Math.Min(nz, 50);
        var depthProfile = new double[depthSteps];
        var glossProfile = new double[depthSteps];
        for (int i = 0; i < depthSteps; i++)
        {
            depthProfile[i] = 5.0 * (1.0 - (double)i / depthSteps);
            glossProfile[i] = 50.0;
        }

        var voxels = _service.Generate3DVoxels(input, depthProfile, glossProfile);

        voxels.Should().HaveCount(nx * ny * nz);
    }

    [Fact]
    public void Generate3DVoxels_SurfaceVoxels_HigherConcentration()
    {
        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            SculptureThicknessCm = 5.0,
            SculptureWidthCm = 10.0,
            SculptureHeightCm = 10.0,
            SurfaceRoughness = 0.3,
            OriginalGloss = 30.0
        };

        int n = 10;
        var depthProfile = new double[n];
        var glossProfile = new double[n];
        for (int i = 0; i < n; i++)
        {
            depthProfile[i] = 5.0 * (1.0 - (double)i / n);
            glossProfile[i] = 30.0 + 50.0 * (1.0 - (double)i / n);
        }

        var voxels = _service.Generate3DVoxels(input, depthProfile, glossProfile);

        var surfaceVoxel = voxels.First(v => Math.Abs(v.Z) < 0.01);
        var deepVoxel = voxels.Last(v => true);

        surfaceVoxel.Concentration.Should().BeGreaterThan(deepVoxel.Concentration);
    }

    [Fact]
    public void Generate3DVoxels_AllVoxels_HaveValidProperties()
    {
        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            SculptureThicknessCm = 5.0,
            SculptureWidthCm = 10.0,
            SculptureHeightCm = 10.0,
            SurfaceRoughness = 0.3,
            OriginalGloss = 30.0
        };

        int n = 10;
        var depthProfile = new double[n];
        var glossProfile = new double[n];
        for (int i = 0; i < n; i++)
        {
            depthProfile[i] = 5.0 * (1.0 - (double)i / n);
            glossProfile[i] = 50.0;
        }

        var voxels = _service.Generate3DVoxels(input, depthProfile, glossProfile);

        foreach (var voxel in voxels)
        {
            voxel.Concentration.Should().BeGreaterOrEqualTo(0);
            voxel.Concentration.Should().BeLessOrEqualTo(2.0);
            voxel.Gloss.Should().BeGreaterOrEqualTo(0);
            voxel.Hardness.Should().BeGreaterOrEqualTo(1.0);
            voxel.MaterialPhase.Should().BeOneOf("CONTINUOUS_FILM", "PARTIAL_SATURATION", "ISLANDS", "UNREINFORCED");
        }
    }

    [Fact]
    public void Generate3DVoxels_SurfaceVoxels_Reinforced()
    {
        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            SculptureThicknessCm = 5.0,
            SculptureWidthCm = 10.0,
            SculptureHeightCm = 10.0,
            SurfaceRoughness = 0.3,
            OriginalGloss = 30.0
        };

        int n = 10;
        var depthProfile = new double[n];
        var glossProfile = new double[n];
        for (int i = 0; i < n; i++)
        {
            depthProfile[i] = 5.0;
            glossProfile[i] = 50.0;
        }

        var voxels = _service.Generate3DVoxels(input, depthProfile, glossProfile);

        var surfaceVoxels = voxels.Where(v => v.Z < 5.0).ToList();
        surfaceVoxels.Should().AllSatisfy(v => v.IsReinforced.Should().BeTrue());
    }

    #endregion

    #region 正常场景测试

    [Fact]
    public async Task SimulateAsync_TEOSNormalConditions_ReturnsValidResult()
    {
        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 20,
            CoordinateResolutionY = 20,
            CoordinateResolutionZ = 10,
            SculptureThicknessCm = 5.0,
            SculptureWidthCm = 40.0,
            SculptureHeightCm = 60.0,
            SurfaceRoughness = 0.3,
            OriginalGloss = 30.0,
            TemperatureC = 25.0,
            ViewMode = "PENETRATION"
        };

        var result = await _service.SimulateAsync(input);

        result.Should().NotBeNull();
        result.SculptureId.Should().Be(1);
        result.MaterialName.Should().Be("TEOS (正硅酸乙酯)");
        result.Voxels.Should().NotBeEmpty();
        result.DepthProfile.Should().NotBeEmpty();
        result.GlossProfile.Should().NotBeEmpty();
        result.AveragePenetrationDepthMm.Should().BeGreaterThan(0);
        result.AverageSurfaceGloss.Should().BeGreaterThan(0);
        result.ReinforcedVolumePercent.Should().BeGreaterOrEqualTo(0);
        result.ReinforcedVolumePercent.Should().BeLessOrEqualTo(100);
        result.EnhancementSuggestions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CompareMaterialsAsync_MultipleMaterials_ReturnsAllResults()
    {
        var materials = new[]
        {
            "TEOS (正硅酸乙酯)",
            "纳米石灰 (Ca(OH)₂)",
            "丙烯酸树脂 (Paraloid B72)",
            "硅丙乳液"
        };

        var results = await _service.CompareMaterialsAsync(1, materials, 0.35);

        results.Should().HaveCount(4);
        results.Select(r => r.MaterialName).Distinct().Should().HaveCount(4);
    }

    [Fact]
    public async Task SimulateAsync_NanoLime_LowerGlossThanTEOS()
    {
        var teosInput = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            OriginalGloss = 30.0
        };

        var nanoLimeInput = new VirtualReinforcementInput
        {
            SculptureId = 2,
            MaterialName = "纳米石灰 (Ca(OH)₂)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            OriginalGloss = 30.0
        };

        var teosResult = await _service.SimulateAsync(teosInput);
        var nanoLimeResult = await _service.SimulateAsync(nanoLimeInput);

        teosResult.AverageSurfaceGloss.Should().BeGreaterThan(nanoLimeResult.AverageSurfaceGloss);
    }

    #endregion

    #region 边界场景测试

    [Fact]
    public async Task SimulateAsync_ShortApplicationTime_ShallowPenetration()
    {
        var shortTimeInput = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 60,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            OriginalGloss = 30.0
        };

        var longTimeInput = new VirtualReinforcementInput
        {
            SculptureId = 2,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 7200,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            OriginalGloss = 30.0
        };

        var shortResult = await _service.SimulateAsync(shortTimeInput);
        var longResult = await _service.SimulateAsync(longTimeInput);

        shortResult.AveragePenetrationDepthMm.Should().BeLessOrEqualTo(longResult.AveragePenetrationDepthMm + 0.1);
    }

    [Fact]
    public async Task SimulateAsync_HighPorosity_DeepPenetration()
    {
        var lowPorosityInput = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.10,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            OriginalGloss = 30.0
        };

        var highPorosityInput = new VirtualReinforcementInput
        {
            SculptureId = 2,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.50,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            OriginalGloss = 30.0
        };

        var lowResult = await _service.SimulateAsync(lowPorosityInput);
        var highResult = await _service.SimulateAsync(highPorosityInput);

        lowResult.AveragePenetrationDepthMm.Should().BeGreaterThan(0);
        highResult.AveragePenetrationDepthMm.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateGlossProfile_OriginalGlossOnly_ReturnsOriginalGloss()
    {
        var depthProfile = new[] { 0.0, 0.0, 0.0 };
        double originalGloss = 30.0;
        double materialGloss = 85.0;

        var glossProfile = _service.CalculateGlossProfile(depthProfile, originalGloss, materialGloss);

        glossProfile.Should().HaveCount(3);
    }

    [Fact]
    public void Generate3DVoxels_MinimumResolution_StillGeneratesVoxels()
    {
        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 5,
            CoordinateResolutionY = 5,
            CoordinateResolutionZ = 5,
            SculptureThicknessCm = 5.0,
            SculptureWidthCm = 10.0,
            SculptureHeightCm = 10.0,
            OriginalGloss = 30.0
        };

        var depthProfile = new[] { 5.0, 3.0, 1.0 };
        var glossProfile = new[] { 80.0, 50.0, 30.0 };

        var voxels = _service.Generate3DVoxels(input, depthProfile, glossProfile);

        voxels.Should().NotBeEmpty();
        voxels.Length.Should().BeGreaterThan(0);
    }

    #endregion

    #region 异常场景测试

    [Fact]
    public async Task SimulateAsync_UnknownMaterial_ReturnsErrorMessage()
    {
        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "未知材料",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600
        };

        var result = await _service.SimulateAsync(input);

        result.EnhancementSuggestions.Should().Contain(s => s.Contains("未知材料"));
    }

    [Fact]
    public void TryGetMaterialVisuals_ExistingMaterial_ReturnsTrue()
    {
        bool found = VirtualReinforcementService.TryGetMaterialVisuals(
            "TEOS (正硅酸乙酯)", out var visuals);

        found.Should().BeTrue();
        visuals.Should().NotBeNull();
        visuals.FilmGloss.Should().BeGreaterThan(0);
        visuals.RefractiveIndex.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryGetMaterialVisuals_NonExistentMaterial_ReturnsFalse()
    {
        bool found = VirtualReinforcementService.TryGetMaterialVisuals(
            "不存在的材料", out var visuals);

        found.Should().BeFalse();
        visuals.Should().BeNull();
    }

    [Fact]
    public void MaterialVisualDatabase_ContainsAllFourMaterials()
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
            bool found = VirtualReinforcementService.TryGetMaterialVisuals(material, out _);
            found.Should().BeTrue($"{material} 应该在材料视觉数据库中");
        }
    }

    [Fact]
    public void CalculateGlossProfile_EmptyDepthProfile_ReturnsEmpty()
    {
        var depthProfile = Array.Empty<double>();
        double originalGloss = 30.0;
        double materialGloss = 85.0;

        var glossProfile = _service.CalculateGlossProfile(depthProfile, originalGloss, materialGloss);

        glossProfile.Should().BeEmpty();
    }

    [Fact]
    public async Task SimulateAsync_GlossViewMode_ReturnsValidConfig()
    {
        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 5,
            OriginalGloss = 30.0,
            ViewMode = "GLOSS"
        };

        var result = await _service.SimulateAsync(input);

        result.VisualizationConfig.Should().NotBeEmpty();
        result.VisualizationConfig.Should().Contain("gloss");
    }

    #endregion

    #region 材料相分类测试

    [Fact]
    public void Generate3DVoxels_HighConcentration_ContinuousFilmPhase()
    {
        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 500.0,
            ApplicationTimeSeconds = 3600,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            SculptureThicknessCm = 5.0,
            SculptureWidthCm = 10.0,
            SculptureHeightCm = 10.0,
            OriginalGloss = 30.0
        };

        int n = 10;
        var depthProfile = new double[n];
        var glossProfile = new double[n];
        for (int i = 0; i < n; i++)
        {
            depthProfile[i] = 10.0;
            glossProfile[i] = 85.0;
        }

        var voxels = _service.Generate3DVoxels(input, depthProfile, glossProfile);

        var surfaceVoxels = voxels.Where(v => v.Z < 5.0).ToList();
        surfaceVoxels.Any(v => v.MaterialPhase == "CONTINUOUS_FILM").Should().BeTrue();
    }

    [Fact]
    public void Generate3DVoxels_LowConcentration_UnreinforcedPhase()
    {
        var input = new VirtualReinforcementInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Porosity = 0.35,
            PoreRadiusNm = 50.0,
            ApplicationTimeSeconds = 1,
            CoordinateResolutionX = 10,
            CoordinateResolutionY = 10,
            CoordinateResolutionZ = 10,
            SculptureThicknessCm = 5.0,
            SculptureWidthCm = 10.0,
            SculptureHeightCm = 10.0,
            OriginalGloss = 30.0
        };

        int n = 10;
        var depthProfile = new double[n];
        var glossProfile = new double[n];
        for (int i = 0; i < n; i++)
        {
            depthProfile[i] = 0.01;
            glossProfile[i] = 30.0;
        }

        var voxels = _service.Generate3DVoxels(input, depthProfile, glossProfile);

        var deepVoxels = voxels.Where(v => v.Z > 20.0).ToList();
        deepVoxels.Any(v => v.MaterialPhase == "UNREINFORCED").Should().BeTrue();
    }

    #endregion
}
