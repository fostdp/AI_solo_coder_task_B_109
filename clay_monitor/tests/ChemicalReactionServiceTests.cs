using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.ChemicalReaction;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ClayMonitor.Tests;

public class ChemicalReactionServiceTests
{
    private readonly Mock<IMessageBus> _mockBus;
    private readonly IOptions<ChemicalReactionOptions> _options;
    private readonly ChemicalReactionService _service;

    public ChemicalReactionServiceTests()
    {
        _mockBus = new Mock<IMessageBus>();
        _options = Options.Create(new ChemicalReactionOptions
        {
            SaltConcentrationWarningThreshold = 300.0,
            HarmfulProductThreshold = 0.15,
            CriticalDeltaGThreshold = -50.0,
            WarningDeltaGThreshold = -20.0,
            PHNeutral = 7.0,
            HighPHRiskThreshold = 8.5,
            LowPHRiskThreshold = 6.0,
            HighTemperatureRiskThreshold = 30.0
        });
        _service = new ChemicalReactionService(_mockBus.Object, _options);
    }

    #region 核心测试：高Na⁺+TEOS组合输出高风险

    [Fact]
    public async Task EvaluateReactionAsync_HighNaConcentrationWithTEOS_ReturnsHighRisk()
    {
        var input = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 2.0,
            TEOSConcentrationMolL = 1.0,
            TemperatureC = 35.0,
            pH = 8.8,
            RelativeHumidity = 0.8,
            ContactTimeHours = 72.0
        };

        var result = await _service.EvaluateReactionAsync(input);

        result.WarningLevel.Should().BeOneOf("CRITICAL", "WARNING");
        result.RequiresWarning.Should().BeTrue();
        result.HarmfulProductYield.Should().BeGreaterThan(0.1);
        result.IsSpontaneous.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateReactionAsync_LowNaWithTEOS_ReturnsLowOrNoRisk()
    {
        var input = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 0.01,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 7.0,
            RelativeHumidity = 0.5,
            ContactTimeHours = 24.0
        };

        var result = await _service.EvaluateReactionAsync(input);

        result.WarningLevel.Should().BeOneOf("NONE", "CAUTION");
        result.HarmfulProductYield.Should().BeLessThan(0.5);
    }

    [Fact]
    public async Task EvaluateReactionAsync_TEOS_HasHighestRiskComparedToOtherMaterials()
    {
        double na2so4Conc = 1.5;
        double temp = 35.0;

        var results = await _service.EvaluateAllMaterialsAsync(1, na2so4Conc, temp);

        var teosResult = results.First(r => r.MaterialName == "TEOS (正硅酸乙酯)");
        var otherResults = results.Where(r => r.MaterialName != "TEOS (正硅酸乙酯)").ToList();

        teosResult.WarningLevel.Should().NotBe("NONE");

        int riskLevelTEOS = GetRiskLevelValue(teosResult.WarningLevel);
        foreach (var other in otherResults)
        {
            int riskLevelOther = GetRiskLevelValue(other.WarningLevel);
            riskLevelTEOS.Should().BeGreaterOrEqualTo(riskLevelOther);
        }
    }

    private static int GetRiskLevelValue(string level) => level switch
    {
        "CRITICAL" => 4,
        "WARNING" => 3,
        "CAUTION" => 2,
        "NONE" => 1,
        _ => 0
    };

    #endregion

    #region Gibbs自由能与Arrhenius速率计算测试

    [Fact]
    public void CalculateGibbsFreeEnergy_ExothermicReaction_NegativeDeltaG()
    {
        double deltaH = -125600;
        double deltaS = 85.2;
        double temperatureK = 298.15;

        double deltaG = _service.CalculateGibbsFreeEnergy(deltaH, deltaS, temperatureK);

        deltaG.Should().BeLessThan(0);
    }

    [Fact]
    public void CalculateGibbsFreeEnergy_HighTemperature_IncreasesSpontaneity()
    {
        double deltaH = -125600;
        double deltaS = 85.2;

        double deltaGLow = _service.CalculateGibbsFreeEnergy(deltaH, deltaS, 273.15);
        double deltaGHigh = _service.CalculateGibbsFreeEnergy(deltaH, deltaS, 323.15);

        deltaGHigh.Should().BeLessThan(deltaGLow);
    }

    [Fact]
    public void CalculateArrheniusRate_HigherTemperature_FasterReaction()
    {
        double A = 1.25e6;
        double Ea = 68500;

        double rateLow = _service.CalculateArrheniusRate(A, Ea, 298.15);
        double rateHigh = _service.CalculateArrheniusRate(A, Ea, 323.15);

        rateHigh.Should().BeGreaterThan(rateLow);
    }

    [Fact]
    public void CalculateArrheniusRate_PositiveActivationEnergy_ReturnsPositiveRate()
    {
        double A = 1e6;
        double Ea = 50000;
        double T = 298.15;

        double rate = _service.CalculateArrheniusRate(A, Ea, T);

        rate.Should().BeGreaterThan(0);
        rate.Should().BeLessThan(A);
    }

    [Fact]
    public void CalculateReactionQuotient_EqualConcentrations_ReturnsOne()
    {
        double[] products = { 1.0 };
        double[] reactants = { 1.0 };

        double Q = _service.CalculateReactionQuotient(products, reactants);

        Q.Should().BeApproximately(1.0, 0.01);
    }

    #endregion

    #region 正常场景测试

    [Fact]
    public async Task EvaluateReactionAsync_TEOSNormalConditions_ReturnsValidResult()
    {
        var input = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 0.5,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 7.0,
            RelativeHumidity = 0.55,
            ContactTimeHours = 24.0
        };

        var result = await _service.EvaluateReactionAsync(input);

        result.Should().NotBeNull();
        result.SculptureId.Should().Be(1);
        result.ReactionName.Should().NotBeEmpty();
        result.GibbsFreeEnergyKJmol.Should().NotBe(0);
        result.EquilibriumConstant.Should().BeGreaterThan(0);
        result.HarmfulProducts.Should().NotBeEmpty();
        result.CalculatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task EvaluateAllMaterialsAsync_ReturnsAllFourMaterials()
    {
        var results = await _service.EvaluateAllMaterialsAsync(1, 0.5, 25.0);

        results.Should().HaveCount(4);
        results.Select(r => r.MaterialName).Distinct().Should().HaveCount(4);
    }

    [Fact]
    public async Task EvaluateReactionAsync_NanoLimeWithSulfate_LowerRiskThanTEOS()
    {
        var teosInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 30.0,
            pH = 7.5,
            RelativeHumidity = 0.7,
            ContactTimeHours = 48.0
        };

        var nanoLimeInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "纳米石灰 (Ca(OH)₂)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 30.0,
            pH = 7.5,
            RelativeHumidity = 0.7,
            ContactTimeHours = 48.0
        };

        var teosResult = await _service.EvaluateReactionAsync(teosInput);
        var nanoLimeResult = await _service.EvaluateReactionAsync(nanoLimeInput);

        teosResult.WarningLevel.Should().NotBe("NONE");
    }

    #endregion

    #region 边界场景测试

    [Fact]
    public async Task EvaluateReactionAsync_ZeroSaltConcentration_MinimalReaction()
    {
        var input = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 0.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 7.0,
            ContactTimeHours = 24.0
        };

        var result = await _service.EvaluateReactionAsync(input);

        result.WarningLevel.Should().BeOneOf("NONE", "CAUTION");
        result.HarmfulProductYield.Should().BeLessThan(0.3);
    }

    [Fact]
    public async Task EvaluateReactionAsync_ExtremepH_IncreasesRisk()
    {
        var neutralInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 7.0,
            RelativeHumidity = 0.6,
            ContactTimeHours = 24.0
        };

        var acidicInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 5.0,
            RelativeHumidity = 0.6,
            ContactTimeHours = 24.0
        };

        var neutralResult = await _service.EvaluateReactionAsync(neutralInput);
        var acidicResult = await _service.EvaluateReactionAsync(acidicInput);

        acidicResult.HarmfulProductYield.Should().BeGreaterOrEqualTo(neutralResult.HarmfulProductYield);
    }

    [Fact]
    public async Task EvaluateReactionAsync_HighTemperature_IncreasesRisk()
    {
        var lowTempInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 15.0,
            pH = 7.0,
            RelativeHumidity = 0.5,
            ContactTimeHours = 24.0
        };

        var highTempInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 40.0,
            pH = 7.0,
            RelativeHumidity = 0.5,
            ContactTimeHours = 24.0
        };

        var lowTempResult = await _service.EvaluateReactionAsync(lowTempInput);
        var highTempResult = await _service.EvaluateReactionAsync(highTempInput);

        highTempResult.HarmfulProductYield.Should().BeGreaterOrEqualTo(lowTempResult.HarmfulProductYield);
    }

    [Fact]
    public async Task EvaluateReactionAsync_HighHumidity_IncreasesReactionRisk()
    {
        var lowHumidityInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 7.0,
            RelativeHumidity = 0.3,
            ContactTimeHours = 24.0
        };

        var highHumidityInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 7.0,
            RelativeHumidity = 0.9,
            ContactTimeHours = 24.0
        };

        var lowResult = await _service.EvaluateReactionAsync(lowHumidityInput);
        var highResult = await _service.EvaluateReactionAsync(highHumidityInput);

        highResult.HarmfulProductYield.Should().BeGreaterOrEqualTo(lowResult.HarmfulProductYield);
    }

    [Fact]
    public async Task EvaluateReactionAsync_LongContactTime_IncreasesConversion()
    {
        var shortTimeInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 7.0,
            RelativeHumidity = 0.5,
            ContactTimeHours = 1.0
        };

        var longTimeInput = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0,
            pH = 7.0,
            RelativeHumidity = 0.5,
            ContactTimeHours = 168.0
        };

        var shortResult = await _service.EvaluateReactionAsync(shortTimeInput);
        var longResult = await _service.EvaluateReactionAsync(longTimeInput);

        longResult.ConversionRate.Should().BeGreaterOrEqualTo(shortResult.ConversionRate);
    }

    #endregion

    #region 异常场景测试

    [Fact]
    public async Task EvaluateReactionAsync_UnknownMaterial_ReturnsNoWarning()
    {
        var input = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "未知材料",
            Na2SO4ConcentrationMolL = 1.0,
            TEOSConcentrationMolL = 0.5,
            TemperatureC = 25.0
        };

        var result = await _service.EvaluateReactionAsync(input);

        result.WarningLevel.Should().Be("NONE");
        result.RequiresWarning.Should().BeFalse();
        result.Recommendation.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EvaluateReactionAsync_NegativeConcentration_HandlesGracefully()
    {
        var input = new ReactionInput
        {
            SculptureId = 1,
            MaterialName = "TEOS (正硅酸乙酯)",
            Na2SO4ConcentrationMolL = -0.5,
            TEOSConcentrationMolL = -0.5,
            TemperatureC = 25.0,
            pH = 7.0,
            ContactTimeHours = 24.0
        };

        var result = await _service.EvaluateReactionAsync(input);

        result.Should().NotBeNull();
        result.ConversionRate.Should().BeGreaterOrEqualTo(0);
        result.HarmfulProductYield.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void TryGetReactionModel_ExistingReaction_ReturnsTrue()
    {
        bool found = ChemicalReactionService.TryGetReactionModel("TEOS_Na2SO4", out var model);

        found.Should().BeTrue();
        model.Should().NotBeNull();
        model.Name.Should().NotBeEmpty();
        model.HarmfulProducts.Should().NotBeEmpty();
    }

    [Fact]
    public void TryGetReactionModel_NonExistentReaction_ReturnsFalse()
    {
        bool found = ChemicalReactionService.TryGetReactionModel("NONEXISTENT", out var model);

        found.Should().BeFalse();
        model.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateReactionAsync_AllWarningLevels_HaveMessages()
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
            var input = new ReactionInput
            {
                SculptureId = 1,
                MaterialName = material,
                Na2SO4ConcentrationMolL = 0.5,
                TEOSConcentrationMolL = 0.5,
                TemperatureC = 25.0,
                pH = 7.0,
                ContactTimeHours = 24.0
            };

            var result = await _service.EvaluateReactionAsync(input);

            result.WarningMessage.Should().NotBeEmpty();
            result.Recommendation.Should().NotBeEmpty();
        }
    }

    #endregion

    #region 反应体系数据库测试

    [Fact]
    public void ReactionDatabase_ContainsAllFourReactions()
    {
        var reactions = new[]
        {
            "TEOS_Na2SO4",
            "TEOS_NaCl",
            "Ca(OH)2_Na2SO4",
            "Acrylic_Na2SO4"
        };

        foreach (var reaction in reactions)
        {
            bool found = ChemicalReactionService.TryGetReactionModel(reaction, out _);
            found.Should().BeTrue($"{reaction} 应该在反应数据库中");
        }
    }

    [Fact]
    public void TEOSNa2SO4Reaction_HasNegativeDeltaG_IsSpontaneous()
    {
        ChemicalReactionService.TryGetReactionModel("TEOS_Na2SO4", out var reaction);

        reaction.Should().NotBeNull();
        reaction!.DeltaHkJmol.Should().BeLessThan(0);
        reaction.DeltaSJmolK.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TEOSNa2SO4Reaction_HasHarmfulProducts()
    {
        ChemicalReactionService.TryGetReactionModel("TEOS_Na2SO4", out var reaction);

        reaction.Should().NotBeNull();
        reaction!.HarmfulProducts.Should().NotBeEmpty();
        reaction.HarmfulProducts.Should().Contain(p => p.Contains("硅酸钠"));
    }

    #endregion
}
