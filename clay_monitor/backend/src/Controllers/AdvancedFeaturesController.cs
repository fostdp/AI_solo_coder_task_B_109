using ClayMonitor.Breathability;
using ClayMonitor.ChemicalReaction;
using ClayMonitor.PenetrationPrediction;
using ClayMonitor.VirtualReinforcement;
using Microsoft.AspNetCore.Mvc;

namespace ClayMonitor.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdvancedFeaturesController : ControllerBase
{
    private readonly IPenetrationPredictionService _penetrationService;
    private readonly IChemicalReactionService _reactionService;
    private readonly IBreathabilityService _breathabilityService;
    private readonly IVirtualReinforcementService _virtualReinforcementService;

    public AdvancedFeaturesController(
        IPenetrationPredictionService penetrationService,
        IChemicalReactionService reactionService,
        IBreathabilityService breathabilityService,
        IVirtualReinforcementService virtualReinforcementService)
    {
        _penetrationService = penetrationService;
        _reactionService = reactionService;
        _breathabilityService = breathabilityService;
        _virtualReinforcementService = virtualReinforcementService;
    }

    [HttpPost("penetration/predict")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PenetrationResult))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PenetrationResult>> PredictPenetration(
        [FromBody] PenetrationInput input,
        CancellationToken ct)
    {
        if (input.Porosity <= 0 || input.Porosity >= 1)
            return BadRequest("Porosity must be between 0 and 1");
        if (input.PoreRadiusNm <= 0)
            return BadRequest("Pore radius must be positive");
        if (string.IsNullOrEmpty(input.MaterialName))
            return BadRequest("Material name is required");

        var result = await _penetrationService.PredictAsync(input, ct);
        return Ok(result);
    }

    [HttpPost("penetration/predict/batch")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PenetrationResult[]))]
    public async Task<ActionResult<PenetrationResult[]>> PredictPenetrationBatch(
        [FromBody] PenetrationInput[] inputs,
        CancellationToken ct)
    {
        var results = await _penetrationService.PredictBatchAsync(inputs, ct);
        return Ok(results);
    }

    [HttpGet("penetration/materials")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string[]))]
    public ActionResult<string[]> GetSupportedMaterials()
    {
        var materials = new[]
        {
            "TEOS (正硅酸乙酯)",
            "纳米石灰 (Ca(OH)₂)",
            "丙烯酸树脂 (Paraloid B72)",
            "硅丙乳液"
        };
        return Ok(materials);
    }

    [HttpPost("penetration/compare/{sculptureId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PenetrationResult[]))]
    public async Task<ActionResult<PenetrationResult[]>> CompareAllMaterialsPenetration(
        int sculptureId,
        [FromQuery] double porosity = 0.35,
        [FromQuery] double poreRadiusNm = 500.0,
        [FromQuery] double timeSeconds = 3600.0,
        CancellationToken ct = default)
    {
        var materials = new[]
        {
            "TEOS (正硅酸乙酯)",
            "纳米石灰 (Ca(OH)₂)",
            "丙烯酸树脂 (Paraloid B72)",
            "硅丙乳液"
        };

        var inputs = new List<PenetrationInput>();
        foreach (var material in materials)
        {
            if (PenetrationPredictionService.TryGetMaterialProperties(material, out var props))
            {
                inputs.Add(new PenetrationInput
                {
                    SculptureId = sculptureId,
                    MaterialName = material,
                    Porosity = porosity,
                    PoreRadiusNm = poreRadiusNm,
                    ViscosityPaS = props.ViscosityPaS,
                    SurfaceTensionNm = props.SurfaceTensionNm,
                    ContactAngleDeg = props.TypicalContactAngle,
                    TimeSeconds = timeSeconds,
                    TemperatureC = 25.0
                });
            }
        }

        var results = await _penetrationService.PredictBatchAsync(inputs.ToArray(), ct);
        return Ok(results);
    }

    [HttpPost("reaction/evaluate")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReactionResult))]
    public async Task<ActionResult<ReactionResult>> EvaluateReaction(
        [FromBody] ReactionInput input,
        CancellationToken ct)
    {
        if (input.Na2SO4ConcentrationMolL < 0)
            return BadRequest("Concentration cannot be negative");

        var result = await _reactionService.EvaluateReactionAsync(input, ct);
        return Ok(result);
    }

    [HttpPost("reaction/evaluate-all/{sculptureId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReactionResult[]))]
    public async Task<ActionResult<ReactionResult[]>> EvaluateAllReactions(
        int sculptureId,
        [FromQuery] double na2so4Concentration = 0.1,
        [FromQuery] double temperatureC = 25.0,
        CancellationToken ct = default)
    {
        var results = await _reactionService.EvaluateAllMaterialsAsync(
            sculptureId, na2so4Concentration, temperatureC, ct);
        return Ok(results);
    }

    [HttpGet("reaction/systems")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(object[]))]
    public ActionResult<object[]> GetReactionSystems()
    {
        var systems = new[]
        {
            new { Key = "TEOS_Na2SO4", Name = "TEOS与硫酸钠反应", Reactants = new[] { "TEOS", "Na2SO4", "H2O" }, Products = new[] { "Na2SiO3", "C2H5OH", "H2SO4" } },
            new { Key = "TEOS_NaCl", Name = "TEOS与氯化钠反应", Reactants = new[] { "TEOS", "NaCl", "H2O" }, Products = new[] { "Na4SiO4", "C2H5OH", "HCl" } },
            new { Key = "Ca(OH)2_Na2SO4", Name = "纳米石灰与硫酸钠反应", Reactants = new[] { "Ca(OH)2", "Na2SO4" }, Products = new[] { "CaSO4·2H2O", "NaOH" } },
            new { Key = "Acrylic_Na2SO4", Name = "丙烯酸树脂与硫酸钠相互作用", Reactants = new[] { "丙烯酸树脂", "Na2SO4" }, Products = new[] { "络合物" } }
        };
        return Ok(systems);
    }

    [HttpPost("breathability/analyze")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BreathabilityResult))]
    public async Task<ActionResult<BreathabilityResult>> AnalyzeBreathability(
        [FromBody] BreathabilityInput input,
        CancellationToken ct)
    {
        if (input.Temperatures.Length < 10)
            return BadRequest("At least 10 data points are required for breathability analysis");
        if (input.Temperatures.Length != input.Humidities.Length ||
            input.Temperatures.Length != input.Timestamps.Length)
            return BadRequest("Temperature, humidity, and timestamp arrays must have the same length");

        var result = await _breathabilityService.AnalyzeAsync(input, ct);
        return Ok(result);
    }

    [HttpGet("breathability/scores/levels")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(object[]))]
    public ActionResult<object[]> GetBreathabilityLevels()
    {
        var levels = new[]
        {
            new { Level = "EXCELLENT", MinScore = 80, Description = "自调节能力极强，可有效缓冲环境波动" },
            new { Level = "GOOD", MinScore = 60, Description = "自调节能力良好，正常环境下可稳定保持" },
            new { Level = "FAIR", MinScore = 40, Description = "自调节能力一般，需适当环境控制" },
            new { Level = "POOR", MinScore = 20, Description = "自调节能力较差，建议安装环境控制设备" },
            new { Level = "CRITICAL", MinScore = 0, Description = "自调节能力严重受损，必须立即采取保护措施" }
        };
        return Ok(levels);
    }

    [HttpPost("reinforcement/simulate")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(VirtualReinforcementResult))]
    public async Task<ActionResult<VirtualReinforcementResult>> SimulateReinforcement(
        [FromBody] VirtualReinforcementInput input,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input.MaterialName))
            return BadRequest("Material name is required");
        if (input.Porosity <= 0 || input.Porosity >= 1)
            return BadRequest("Porosity must be between 0 and 1");
        if (input.CoordinateResolutionX < 10 || input.CoordinateResolutionX > 100 ||
            input.CoordinateResolutionY < 10 || input.CoordinateResolutionY > 100 ||
            input.CoordinateResolutionZ < 10 || input.CoordinateResolutionZ > 100)
            return BadRequest("Coordinate resolution must be between 10 and 100");

        var result = await _virtualReinforcementService.SimulateAsync(input, ct);
        return Ok(result);
    }

    [HttpPost("reinforcement/compare/{sculptureId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(VirtualReinforcementResult[]))]
    public async Task<ActionResult<VirtualReinforcementResult[]>> CompareReinforcementMaterials(
        int sculptureId,
        [FromQuery] double porosity = 0.35,
        CancellationToken ct = default)
    {
        var materials = new[]
        {
            "TEOS (正硅酸乙酯)",
            "纳米石灰 (Ca(OH)₂)",
            "丙烯酸树脂 (Paraloid B72)",
            "硅丙乳液"
        };

        var results = await _virtualReinforcementService.CompareMaterialsAsync(
            sculptureId, materials, porosity, ct);
        return Ok(results);
    }

    [HttpGet("reinforcement/materials/visuals")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(object[]))]
    public ActionResult<object[]> GetMaterialVisualProperties()
    {
        var materials = new[]
        {
            new {
                Name = "TEOS (正硅酸乙酯)",
                RefractiveIndex = 1.42,
                FilmGloss = 85.0,
                FilmHardness = 2.5,
                Transparency = 0.85,
                CureTimeHours = 24.0
            },
            new {
                Name = "纳米石灰 (Ca(OH)₂)",
                RefractiveIndex = 1.55,
                FilmGloss = 35.0,
                FilmHardness = 1.2,
                Transparency = 0.60,
                CureTimeHours = 72.0
            },
            new {
                Name = "丙烯酸树脂 (Paraloid B72)",
                RefractiveIndex = 1.50,
                FilmGloss = 75.0,
                FilmHardness = 1.8,
                Transparency = 0.92,
                CureTimeHours = 6.0
            },
            new {
                Name = "硅丙乳液",
                RefractiveIndex = 1.48,
                FilmGloss = 55.0,
                FilmHardness = 2.0,
                Transparency = 0.88,
                CureTimeHours = 12.0
            }
        };
        return Ok(materials);
    }

    [HttpPost("reinforcement/simulate/lightweight")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(object))]
    public async Task<ActionResult<object>> SimulateReinforcementLightweight(
        [FromBody] VirtualReinforcementInput input,
        CancellationToken ct)
    {
        var fullResult = await _virtualReinforcementService.SimulateAsync(input, ct);
        var lightweight = new
        {
            fullResult.SculptureId,
            fullResult.MaterialName,
            fullResult.AveragePenetrationDepthMm,
            fullResult.MaximumPenetrationDepthMm,
            fullResult.AverageSurfaceGloss,
            fullResult.GlossChangePercent,
            fullResult.ReinforcedVolumePercent,
            fullResult.HardnessImprovementPercent,
            fullResult.DepthProfile,
            fullResult.GlossProfile,
            fullResult.IsoSurfaces,
            fullResult.EnhancementSuggestions,
            fullResult.CalculatedAt
        };
        return Ok(lightweight);
    }
}
