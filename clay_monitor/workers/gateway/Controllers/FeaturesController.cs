using Microsoft.AspNetCore.Mvc;
using reaction_warning;
using respiration_eval;
using virtual_coating;
using WashburnPenetration;
using WashburnPenetration.Models;

namespace gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FeaturesController : ControllerBase
{
    private readonly IPenetrationPredictionService _penetrationService;
    private readonly IChemicalReactionService _reactionService;
    private readonly IBreathabilityService _breathabilityService;
    private readonly IVirtualCoatingService _virtualCoatingService;

    public FeaturesController(
        IPenetrationPredictionService penetrationService,
        IChemicalReactionService reactionService,
        IBreathabilityService breathabilityService,
        IVirtualCoatingService virtualCoatingService)
    {
        _penetrationService = penetrationService;
        _reactionService = reactionService;
        _breathabilityService = breathabilityService;
        _virtualCoatingService = virtualCoatingService;
    }

    [HttpPost("penetration/predict")]
    public async Task<ActionResult<PenetrationResult>> PredictPenetration(
        [FromBody] PenetrationInput input,
        CancellationToken ct)
    {
        var result = await _penetrationService.PredictAsync(input, ct);
        return Ok(result);
    }

    [HttpPost("penetration/predict-batch")]
    public async Task<ActionResult<PenetrationResult[]>> PredictPenetrationBatch(
        [FromBody] PenetrationInput[] inputs,
        CancellationToken ct)
    {
        var results = await _penetrationService.PredictBatchAsync(inputs, ct);
        return Ok(results);
    }

    [HttpPost("penetration/predict-parallel")]
    public async Task<ActionResult<PenetrationResult[]>> PredictPenetrationParallel(
        [FromBody] PenetrationInput[] inputs,
        CancellationToken ct)
    {
        var results = await _penetrationService.ParallelPredictBatchAsync(inputs, ct);
        return Ok(results);
    }

    [HttpPost("reaction/evaluate")]
    public async Task<ActionResult<ReactionResult>> EvaluateReaction(
        [FromBody] ReactionInput input,
        CancellationToken ct)
    {
        var result = await _reactionService.EvaluateReactionAsync(input, ct);
        return Ok(result);
    }

    [HttpPost("reaction/evaluate-all")]
    public async Task<ActionResult<ReactionResult[]>> EvaluateAllMaterials(
        [FromQuery] int sculptureId,
        [FromQuery] double na2so4Concentration,
        [FromQuery] double temperatureC,
        CancellationToken ct)
    {
        var results = await _reactionService.EvaluateAllMaterialsAsync(
            sculptureId,
            na2so4Concentration,
            temperatureC,
            ct);
        return Ok(results);
    }

    [HttpGet("reaction/cache-stat")]
    public ActionResult GetReactionCacheStat()
    {
        var cacheSize = _reactionService.GetCacheSize();
        var cacheHitCount = _reactionService.GetCacheHitCount();
        return Ok(new
        {
            CacheSize = cacheSize,
            CacheHitCount = cacheHitCount
        });
    }

    [HttpPost("breathability/analyze")]
    public async Task<ActionResult<BreathabilityResult>> AnalyzeBreathability(
        [FromBody] BreathabilityInput input,
        CancellationToken ct)
    {
        var result = await _breathabilityService.AnalyzeAsync(input, ct);
        return Ok(result);
    }

    [HttpPost("virtual/simulate")]
    public async Task<ActionResult<VirtualCoatingResult>> SimulateVirtualCoating(
        [FromBody] VirtualCoatingInput input,
        CancellationToken ct)
    {
        var result = await _virtualCoatingService.SimulateAsync(input, ct);
        return Ok(result);
    }

    [HttpPost("virtual/compare")]
    public async Task<ActionResult<VirtualCoatingResult[]>> CompareVirtualCoatingMaterials(
        [FromQuery] int sculptureId,
        [FromBody] string[] materialNames,
        [FromQuery] double porosity,
        CancellationToken ct)
    {
        var results = await _virtualCoatingService.CompareMaterialsAsync(
            sculptureId,
            materialNames,
            porosity,
            ct);
        return Ok(results);
    }

    [HttpPost("virtual/render-image")]
    public async Task<IActionResult> RenderVirtualCoatingImage(
        [FromBody] VirtualCoatingInput input,
        CancellationToken ct)
    {
        var imageBytes = await _virtualCoatingService.RenderImageAsync(input, ct);
        return File(imageBytes, "image/png");
    }
}
