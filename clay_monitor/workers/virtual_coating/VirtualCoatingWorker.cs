using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Messages;
using ClayMonitor.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SkiaSharp;
using WashburnPenetration;
using WashburnPenetration.Models;

namespace virtual_coating;

public record VirtualCoatingInput
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public double Porosity { get; init; } = 0.35;
    public double PoreRadiusNm { get; init; } = 500.0;
    public double ApplicationTimeSeconds { get; init; } = 3600.0;
    public int CoordinateResolutionX { get; init; } = 40;
    public int CoordinateResolutionY { get; init; } = 60;
    public int CoordinateResolutionZ { get; init; } = 30;
    public double SculptureThicknessCm { get; init; } = 5.0;
    public double SculptureWidthCm { get; init; } = 40.0;
    public double SculptureHeightCm { get; init; } = 60.0;
    public double SurfaceRoughness { get; init; } = 0.3;
    public double OriginalGloss { get; init; } = 30.0;
    public double TemperatureC { get; init; } = 25.0;
    public string ViewMode { get; init; } = "PENETRATION";
}

public record Voxel3D
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Concentration { get; init; }
    public double Gloss { get; init; }
    public double Hardness { get; init; }
    public bool IsReinforced { get; init; }
    public string MaterialPhase { get; init; } = string.Empty;
}

public record VirtualCoatingResult
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public Voxel3D[] Voxels { get; init; } = Array.Empty<Voxel3D>();
    public double[] DepthProfile { get; init; } = Array.Empty<double>();
    public double[] GlossProfile { get; init; } = Array.Empty<double>();
    public double AveragePenetrationDepthMm { get; init; }
    public double MaximumPenetrationDepthMm { get; init; }
    public double AverageSurfaceGloss { get; init; }
    public double GlossChangePercent { get; init; }
    public double ReinforcedVolumePercent { get; init; }
    public double HardnessImprovementPercent { get; init; }
    public double[] IsoSurfaces { get; init; } = Array.Empty<double>();
    public string VisualizationConfig { get; init; } = string.Empty;
    public string[] EnhancementSuggestions { get; init; } = Array.Empty<string>();
    public DateTime CalculatedAt { get; init; } = DateTime.Now;
}

public record VirtualCoatingApplied
{
    public int SculptureId { get; init; }
    public string MaterialName { get; init; } = string.Empty;
    public double AveragePenetrationMm { get; init; }
    public double GlossChangePercent { get; init; }
    public double ReinforcedVolumePercent { get; init; }
    public DateTime AppliedAt { get; init; } = DateTime.Now;
}

public interface IVirtualCoatingService
{
    Task<VirtualCoatingResult> SimulateAsync(VirtualCoatingInput input, CancellationToken ct = default);
    Task<VirtualCoatingResult[]> CompareMaterialsAsync(
        int sculptureId,
        string[] materialNames,
        double porosity,
        CancellationToken ct = default);
    Task<byte[]> RenderImageAsync(VirtualCoatingInput input, CancellationToken ct = default);
    Voxel3D[] Generate3DVoxels(VirtualCoatingInput input, double[] depthProfile, double[] glossProfile);
    double[] CalculateGlossProfile(double[] depthProfile, double originalGloss, double materialGloss);
}

public class VirtualCoatingWorker : BackgroundService, IVirtualCoatingService
{
    private readonly IMessageBus _bus;
    private readonly VirtualReinforcementOptions _options;
    private readonly IPenetrationPredictionService _penetrationService;

    private static readonly Dictionary<string, MaterialVisualProperties> MaterialVisuals = new()
    {
        ["TEOS (正硅酸乙酯)"] = new MaterialVisualProperties
        {
            RefractiveIndex = 1.42,
            FilmGloss = 85.0,
            FilmHardness = 2.5,
            Transparency = 0.85,
            ColorShift = new[] { 0.98, 0.98, 1.02 },
            TypicalFilmThicknessUm = 5.0,
            CureTimeHours = 24.0
        },
        ["纳米石灰 (Ca(OH)₂)"] = new MaterialVisualProperties
        {
            RefractiveIndex = 1.55,
            FilmGloss = 35.0,
            FilmHardness = 1.2,
            Transparency = 0.60,
            ColorShift = new[] { 0.95, 0.95, 0.90 },
            TypicalFilmThicknessUm = 20.0,
            CureTimeHours = 72.0
        },
        ["丙烯酸树脂 (Paraloid B72)"] = new MaterialVisualProperties
        {
            RefractiveIndex = 1.50,
            FilmGloss = 75.0,
            FilmHardness = 1.8,
            Transparency = 0.92,
            ColorShift = new[] { 0.99, 0.99, 1.00 },
            TypicalFilmThicknessUm = 15.0,
            CureTimeHours = 6.0
        },
        ["硅丙乳液"] = new MaterialVisualProperties
        {
            RefractiveIndex = 1.48,
            FilmGloss = 55.0,
            FilmHardness = 2.0,
            Transparency = 0.88,
            ColorShift = new[] { 0.97, 0.97, 0.98 },
            TypicalFilmThicknessUm = 12.0,
            CureTimeHours = 12.0
        }
    };

    public VirtualCoatingWorker(
        IMessageBus bus,
        IOptions<VirtualReinforcementOptions> options,
        IPenetrationPredictionService penetrationService)
    {
        _bus = bus;
        _options = options.Value;
        _penetrationService = penetrationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var penetrationResult in _bus.SubscribeAsync<PenetrationPredictionCompleted>(stoppingToken))
        {
            try
            {
                var input = new VirtualCoatingInput
                {
                    SculptureId = penetrationResult.SculptureId,
                    MaterialName = penetrationResult.MaterialName,
                    Porosity = _options.DefaultPorosity,
                    PoreRadiusNm = _options.DefaultPoreRadiusNm,
                    ApplicationTimeSeconds = _options.DefaultApplicationTimeSeconds,
                    SculptureThicknessCm = _options.DefaultThicknessCm,
                    OriginalGloss = 30.0,
                    TemperatureC = 25.0
                };

                var result = await SimulateAsync(input, stoppingToken);
                await _bus.PublishAsync(result, stoppingToken);
                await _bus.PublishAsync(new VirtualCoatingApplied
                {
                    SculptureId = result.SculptureId,
                    MaterialName = result.MaterialName,
                    AveragePenetrationMm = result.AveragePenetrationDepthMm,
                    GlossChangePercent = result.GlossChangePercent,
                    ReinforcedVolumePercent = result.ReinforcedVolumePercent
                }, stoppingToken);
            }
            catch (Exception ex)
            {
            }
        }
    }

    public async Task<VirtualCoatingResult> SimulateAsync(VirtualCoatingInput input, CancellationToken ct = default)
    {
        if (!MaterialVisuals.TryGetValue(input.MaterialName, out var visuals) ||
            !PenetrationPredictionWorker.TryGetMaterialProperties(input.MaterialName, out var matProps))
        {
            return await Task.FromResult(new VirtualCoatingResult
            {
                SculptureId = input.SculptureId,
                MaterialName = input.MaterialName,
                EnhancementSuggestions = new[] { "未知材料，无法进行虚拟涂层模拟" },
                CalculatedAt = DateTime.Now
            });
        }

        var penInput = new PenetrationInput
        {
            SculptureId = input.SculptureId,
            MaterialName = input.MaterialName,
            Porosity = input.Porosity,
            PoreRadiusNm = input.PoreRadiusNm,
            ViscosityPaS = matProps.ViscosityPaS,
            SurfaceTensionNm = matProps.SurfaceTensionNm,
            ContactAngleDeg = matProps.TypicalContactAngle,
            TimeSeconds = input.ApplicationTimeSeconds,
            TemperatureC = input.TemperatureC
        };

        var penResult = await _penetrationService.PredictAsync(penInput, ct);

        int depthSteps = Math.Min(input.CoordinateResolutionZ, 50);
        double[] depthProfile = new double[depthSteps];
        double[] zCoords = new double[depthSteps];
        double maxDepthMm = penResult.PredictedDepthMm * 1.2;

        for (int i = 0; i < depthSteps; i++)
        {
            zCoords[i] = (i / (double)(depthSteps - 1)) * maxDepthMm;
            double tEquiv = CalculateEquivalentTime(zCoords[i] / 1000.0,
                input.PoreRadiusNm * 1e-9, matProps.SurfaceTensionNm,
                matProps.TypicalContactAngle * Math.PI / 180.0, matProps.ViscosityPaS, input.Porosity);
            double factor = Math.Exp(-0.3 * zCoords[i] / Math.Max(penResult.PredictedDepthMm, 0.1));
            depthProfile[i] = factor * penResult.DepthProfile.Length > i
                ? penResult.DepthProfile[i]
                : penResult.PredictedDepthMm * Math.Exp(-0.5 * zCoords[i] / Math.Max(penResult.PredictedDepthMm, 0.1));
        }

        double[] glossProfile = CalculateGlossProfile(depthProfile, input.OriginalGloss, visuals.FilmGloss);
        Voxel3D[] voxels = Generate3DVoxels(input, depthProfile, glossProfile);

        double avgPen = depthProfile.Take(Math.Min(10, depthProfile.Length)).Average();
        double maxPen = depthProfile.Max();
        double avgGloss = glossProfile.Take(Math.Min(5, glossProfile.Length)).Average();
        double glossChange = (avgGloss - input.OriginalGloss) / Math.Max(input.OriginalGloss, 0.1) * 100.0;

        double reinforcedVol = voxels.Count(v => v.IsReinforced);
        double reinforcedPct = reinforcedVol / voxels.Length * 100.0;

        double hardnessImprovement = (visuals.FilmHardness - 1.0) * 100.0 / 1.0;

        double[] isoSurfaces = CalculateIsoSurfaces(depthProfile, zCoords);

        string visConfig = GenerateVisualizationConfig(input, visuals, penResult);
        string[] suggestions = GenerateEnhancementSuggestions(avgPen, glossChange, reinforcedPct, visuals, input);

        return await Task.FromResult(new VirtualCoatingResult
        {
            SculptureId = input.SculptureId,
            MaterialName = input.MaterialName,
            Voxels = voxels,
            DepthProfile = depthProfile.Select(x => Math.Round(x, 4)).ToArray(),
            GlossProfile = glossProfile.Select(x => Math.Round(x, 4)).ToArray(),
            AveragePenetrationDepthMm = Math.Round(avgPen, 3),
            MaximumPenetrationDepthMm = Math.Round(maxPen, 3),
            AverageSurfaceGloss = Math.Round(avgGloss, 2),
            GlossChangePercent = Math.Round(glossChange, 2),
            ReinforcedVolumePercent = Math.Round(reinforcedPct, 2),
            HardnessImprovementPercent = Math.Round(hardnessImprovement, 2),
            IsoSurfaces = isoSurfaces.Select(x => Math.Round(x, 4)).ToArray(),
            VisualizationConfig = visConfig,
            EnhancementSuggestions = suggestions,
            CalculatedAt = DateTime.Now
        });
    }

    public async Task<VirtualCoatingResult[]> CompareMaterialsAsync(
        int sculptureId,
        string[] materialNames,
        double porosity,
        CancellationToken ct = default)
    {
        var results = new List<VirtualCoatingResult>();
        foreach (var material in materialNames)
        {
            var input = new VirtualCoatingInput
            {
                SculptureId = sculptureId,
                MaterialName = material,
                Porosity = porosity,
                PoreRadiusNm = _options.DefaultPoreRadiusNm,
                ApplicationTimeSeconds = _options.DefaultApplicationTimeSeconds,
                CoordinateResolutionX = 20,
                CoordinateResolutionY = 30,
                CoordinateResolutionZ = 15,
                OriginalGloss = 30.0
            };
            results.Add(await SimulateAsync(input, ct));
        }
        return results.ToArray();
    }

    public async Task<byte[]> RenderImageAsync(VirtualCoatingInput input, CancellationToken ct = default)
    {
        var result = await SimulateAsync(input, ct);
        return RenderSkiaSharpImage(result, input, 800, 600);
    }

    public byte[] RenderSkiaSharpImage(VirtualCoatingResult result, VirtualCoatingInput input, int width = 800, int height = 600)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(245, 243, 238));

        int marginLeft = 90;
        int marginRight = 120;
        int marginTop = 80;
        int marginBottom = 100;
        int plotWidth = width - marginLeft - marginRight;
        int plotHeight = height - marginTop - marginBottom;

        double maxDepth = Math.Max(result.MaximumPenetrationDepthMm, 1.0);
        double widthCm = input.SculptureWidthCm;

        using var paintBg = new SKPaint { Color = new SKColor(235, 228, 218), IsAntialias = true };
        canvas.DrawRoundRect(marginLeft, marginTop, plotWidth, plotHeight, 8, 8, paintBg);

        int resolution = 100;
        for (int py = 0; py < resolution; py++)
        {
            double depthRatio = py / (double)(resolution - 1);
            double depth = depthRatio * maxDepth;
            double depthFactor = Math.Exp(-1.2 * depth / Math.Max(maxDepth, 0.1));

            for (int px = 0; px < resolution; px++)
            {
                double xRatio = px / (double)(resolution - 1);
                double distFromCenter = Math.Abs((xRatio - 0.5) * 2.0);
                double edgeFactor = Math.Exp(-1.5 * distFromCenter * distFromCenter);
                double concentration = depthFactor * (0.7 + 0.6 * edgeFactor);

                SKColor color = ViridisColorMap(concentration);
                using var p = new SKPaint { Color = color, IsAntialias = false };

                float rx = marginLeft + (float)xRatio * plotWidth;
                float ry = marginTop + (float)depthRatio * plotHeight;
                float cellW = plotWidth / resolution + 1;
                float cellH = plotHeight / resolution + 1;
                canvas.DrawRect(rx, ry, cellW, cellH, p);
            }
        }

        using (var isoPaint = new SKPaint
               {
                   Color = new SKColor(255, 255, 255, 220),
                   StrokeWidth = 1.5f,
                   IsStroke = true,
                   PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0),
                   IsAntialias = true
               })
        {
            double[] isoLevels = { 0.9, 0.7, 0.5, 0.3, 0.1 };
            string[] labels = { "90%", "70%", "50%", "30%", "10%" };
            for (int i = 0; i < isoLevels.Length && i < result.IsoSurfaces.Length; i++)
            {
                double isoDepth = result.IsoSurfaces[i];
                float y = marginTop + (float)(isoDepth / maxDepth) * plotHeight;
                if (y >= marginTop && y <= marginTop + plotHeight)
                {
                    canvas.DrawLine(marginLeft, y, marginLeft + plotWidth, y, isoPaint);
                    using var txtPaint = new SKPaint
                    {
                        Color = new SKColor(255, 255, 255),
                        TextSize = 10,
                        IsAntialias = true,
                        FakeBoldText = true
                    };
                    canvas.DrawText(labels[i], marginLeft + plotWidth - 30, y - 3, txtPaint);
                }
            }
        }

        using (var axisPaint = new SKPaint
               {
                   Color = new SKColor(60, 60, 60),
                   StrokeWidth = 1.5f,
                   IsStroke = true,
                   IsAntialias = true
               })
        {
            canvas.DrawLine(marginLeft, marginTop, marginLeft, marginTop + plotHeight, axisPaint);
            canvas.DrawLine(marginLeft, marginTop + plotHeight, marginLeft + plotWidth, marginTop + plotHeight, axisPaint);
        }

        using (var labelPaint = new SKPaint
               {
                   Color = new SKColor(40, 40, 40),
                   TextSize = 12,
                   IsAntialias = true,
                   TextAlign = SKTextAlign.Center
               })
        {
            canvas.DrawText("Width (cm)", marginLeft + plotWidth / 2, height - 50, labelPaint);
            labelPaint.TextAlign = SKTextAlign.Left;
            canvas.Save();
            canvas.Translate(25, marginTop + plotHeight / 2);
            canvas.RotateDegrees(-90);
            canvas.DrawText("Depth (mm)", 0, 0, labelPaint);
            canvas.Restore();

            for (int i = 0; i <= 5; i++)
            {
                float xRatio = i / 5.0f;
                float x = marginLeft + xRatio * plotWidth;
                canvas.DrawLine(x, marginTop + plotHeight, x, marginTop + plotHeight + 5, axisPaint);
                labelPaint.TextAlign = SKTextAlign.Center;
                canvas.DrawText($"{(xRatio * widthCm):F0}", x, marginTop + plotHeight + 20, labelPaint);
            }
            for (int i = 0; i <= 5; i++)
            {
                float yRatio = i / 5.0f;
                float y = marginTop + yRatio * plotHeight;
                canvas.DrawLine(marginLeft - 5, y, marginLeft, y, axisPaint);
                labelPaint.TextAlign = SKTextAlign.Right;
                canvas.DrawText($"{(yRatio * maxDepth):F1}", marginLeft - 8, y + 4, labelPaint);
            }
        }

        using (var titlePaint = new SKPaint
               {
                   Color = new SKColor(30, 30, 30),
                   TextSize = 18,
                   IsAntialias = true,
                   FakeBoldText = true
               })
        {
            canvas.DrawText($"Virtual Coating - {result.MaterialName}", marginLeft, 35, titlePaint);
        }

        using (var infoPaint = new SKPaint
               {
                   Color = new SKColor(60, 60, 60),
                   TextSize = 11,
                   IsAntialias = true
               })
        {
            int infoY = marginTop;
            canvas.DrawText($"Avg Penetration: {result.AveragePenetrationDepthMm:F2} mm", marginLeft + plotWidth + 10, infoY + 15, infoPaint);
            canvas.DrawText($"Max Penetration: {result.MaximumPenetrationDepthMm:F2} mm", marginLeft + plotWidth + 10, infoY + 32, infoPaint);
            canvas.DrawText($"Gloss Change: {result.GlossChangePercent:F1}%", marginLeft + plotWidth + 10, infoY + 49, infoPaint);
            canvas.DrawText($"Reinforced Vol: {result.ReinforcedVolumePercent:F1}%", marginLeft + plotWidth + 10, infoY + 66, infoPaint);
            canvas.DrawText($"Hardness +{result.HardnessImprovementPercent:F0}%", marginLeft + plotWidth + 10, infoY + 83, infoPaint);

            int legendY = infoY + 120;
            int legendH = 160;
            int legendW = 20;
            using var legendBg = new SKPaint { Color = new SKColor(255, 255, 255, 230), IsAntialias = true };
            canvas.DrawRoundRect(marginLeft + plotWidth + 5, legendY - 5, legendW + 60, legendH + 20, 4, 4, legendBg);

            for (int i = 0; i < 100; i++)
            {
                double t = i / 99.0;
                SKColor c = ViridisColorMap(1.0 - t);
                using var lp = new SKPaint { Color = c };
                canvas.DrawRect(marginLeft + plotWidth + 10, legendY + (float)(t * legendH), legendW, legendH / 100.0f + 1, lp);
            }
            infoPaint.TextAlign = SKTextAlign.Left;
            canvas.DrawText("High", marginLeft + plotWidth + 35, legendY + 10, infoPaint);
            canvas.DrawText("Low", marginLeft + plotWidth + 35, legendY + legendH, infoPaint);
            canvas.DrawText("Conc.", marginLeft + plotWidth + 35, legendY + legendH / 2 + 5, infoPaint);
        }

        if (result.EnhancementSuggestions.Length > 0)
        {
            using var sugPaint = new SKPaint
            {
                Color = new SKColor(50, 50, 50),
                TextSize = 10,
                IsAntialias = true
            };
            string firstSuggestion = result.EnhancementSuggestions[0];
            if (firstSuggestion.Length > 100) firstSuggestion = firstSuggestion.Substring(0, 97) + "...";
            canvas.DrawText($"Tip: {firstSuggestion}", marginLeft, height - 20, sugPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        return data.ToArray();
    }

    private static SKColor ViridisColorMap(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double[] r = { 0.267, 0.282, 0.254, 0.206, 0.163, 0.127, 0.135, 0.266, 0.477, 0.741, 0.993 };
        double[] g = { 0.004, 0.140, 0.305, 0.436, 0.557, 0.658, 0.749, 0.821, 0.861, 0.863, 0.906 };
        double[] b = { 0.329, 0.463, 0.524, 0.554, 0.568, 0.559, 0.518, 0.441, 0.326, 0.182, 0.144 };
        int n = r.Length - 1;
        double idx = t * n;
        int i = (int)Math.Floor(idx);
        double f = idx - i;
        if (i >= n) { i = n - 1; f = 1.0; }
        byte R = (byte)Math.Round((r[i] + f * (r[i + 1] - r[i])) * 255);
        byte G = (byte)Math.Round((g[i] + f * (g[i + 1] - g[i])) * 255);
        byte B = (byte)Math.Round((b[i] + f * (b[i + 1] - b[i])) * 255);
        return new SKColor(R, G, B, 255);
    }

    private byte[] GenerateBmpHeader(int width, int height, int pixelDataSize)
    {
        int fileSize = 54 + pixelDataSize;
        byte[] header = new byte[54];

        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BitConverter.GetBytes(fileSize).CopyTo(header, 2);
        BitConverter.GetBytes(54).CopyTo(header, 10);
        BitConverter.GetBytes(40).CopyTo(header, 14);
        BitConverter.GetBytes(width).CopyTo(header, 18);
        BitConverter.GetBytes(height).CopyTo(header, 22);
        BitConverter.GetBytes((short)1).CopyTo(header, 26);
        BitConverter.GetBytes((short)24).CopyTo(header, 28);
        BitConverter.GetBytes(pixelDataSize).CopyTo(header, 34);
        BitConverter.GetBytes(2835).CopyTo(header, 38);
        BitConverter.GetBytes(2835).CopyTo(header, 42);

        return header;
    }

    public Voxel3D[] Generate3DVoxels(VirtualCoatingInput input, double[] depthProfile, double[] glossProfile)
    {
        int nx = Math.Max(10, Math.Min(input.CoordinateResolutionX, 60));
        int ny = Math.Max(15, Math.Min(input.CoordinateResolutionY, 80));
        int nz = Math.Max(10, Math.Min(input.CoordinateResolutionZ, 40));

        var voxels = new List<Voxel3D>(nx * ny * nz);
        double thicknessMm = input.SculptureThicknessCm * 10.0;
        double maxDepth = depthProfile.Max();

        double centerX = input.SculptureWidthCm / 2.0;
        double centerY = input.SculptureHeightCm / 2.0;

        double roughnessFactor = 1.0 + input.SurfaceRoughness * 0.5;

        for (int i = 0; i < nx; i++)
        {
            double x = (i / (double)(nx - 1)) * input.SculptureWidthCm;
            for (int j = 0; j < ny; j++)
            {
                double y = (j / (double)(ny - 1)) * input.SculptureHeightCm;
                double distFromCenter = Math.Sqrt(
                    Math.Pow((x - centerX) / centerX, 2) +
                    Math.Pow((y - centerY) / (centerY * 1.2), 2));

                double edgeFactor = Math.Exp(-1.5 * Math.Pow(distFromCenter, 2));

                for (int k = 0; k < nz; k++)
                {
                    double z = (k / (double)(nz - 1)) * thicknessMm;
                    double depthFactor = Math.Exp(-0.8 * z / Math.Max(maxDepth, 0.1));

                    double localPen = depthProfile[Math.Min(k, depthProfile.Length - 1)] *
                                      (0.7 + 0.6 * edgeFactor) * roughnessFactor;

                    double localGloss = glossProfile[Math.Min(k, glossProfile.Length - 1)] *
                                       (0.85 + 0.3 * edgeFactor);

                    double concentration = depthFactor * edgeFactor * roughnessFactor;

                    double hardness = 1.0 + concentration * 1.5;

                    bool isReinforced = concentration > 0.1;

                    string phase = concentration > 0.8 ? "CONTINUOUS_FILM" :
                                   concentration > 0.4 ? "PARTIAL_SATURATION" :
                                   concentration > 0.1 ? "ISLANDS" : "UNREINFORCED";

                    voxels.Add(new Voxel3D
                    {
                        X = Math.Round(x, 3),
                        Y = Math.Round(y, 3),
                        Z = Math.Round(z, 3),
                        Concentration = Math.Round(concentration, 4),
                        Gloss = Math.Round(localGloss, 3),
                        Hardness = Math.Round(hardness, 3),
                        IsReinforced = isReinforced,
                        MaterialPhase = phase
                    });
                }
            }
        }

        return voxels.ToArray();
    }

    public double[] CalculateGlossProfile(double[] depthProfile, double originalGloss, double materialGloss)
    {
        int n = depthProfile.Length;
        double[] gloss = new double[n];
        double maxDepth = depthProfile.Max();

        for (int i = 0; i < n; i++)
        {
            double depthRatio = n > 1 ? i / (double)(n - 1) : 0;
            double penetrationFactor = depthProfile[i] / Math.Max(maxDepth, 0.1);

            double surfaceExposure = Math.Exp(-3.0 * depthRatio);
            double mixingRatio = surfaceExposure * (0.6 + 0.4 * penetrationFactor);

            gloss[i] = originalGloss * (1 - mixingRatio) + materialGloss * mixingRatio;
        }

        return gloss;
    }

    private double CalculateEquivalentTime(double h, double r, double gamma, double theta, double eta, double phi)
    {
        double cosTheta = Math.Cos(theta);
        if (cosTheta <= 0) return double.PositiveInfinity;
        return 2.0 * eta * phi * h * h / (gamma * cosTheta * r);
    }

    private double[] CalculateIsoSurfaces(double[] depthProfile, double[] zCoords)
    {
        double[] isoLevels = { 0.9, 0.7, 0.5, 0.3, 0.1 };
        double maxValue = depthProfile.Max();

        List<double> isoDepths = new List<double>();
        foreach (var level in isoLevels)
        {
            double target = level * maxValue;
            for (int i = 1; i < depthProfile.Length; i++)
            {
                if ((depthProfile[i - 1] - target) * (depthProfile[i] - target) <= 0)
                {
                    double t = (target - depthProfile[i - 1]) / Math.Max(depthProfile[i] - depthProfile[i - 1], 1e-9);
                    double z = zCoords[i - 1] + t * (zCoords[i] - zCoords[i - 1]);
                    isoDepths.Add(z);
                    break;
                }
            }
        }
        return isoDepths.ToArray();
    }

    private string GenerateVisualizationConfig(VirtualCoatingInput input, MaterialVisualProperties visuals, PenetrationResult penResult)
    {
        var config = new
        {
            mode = input.ViewMode,
            materialColor = new[]
            {
                (int)(255 * visuals.ColorShift[0]),
                (int)(255 * visuals.ColorShift[1]),
                (int)(255 * visuals.ColorShift[2])
            },
            transparency = visuals.Transparency,
            maxPenetration = penResult.PredictedDepthMm,
            colormap = input.ViewMode switch
            {
                "PENETRATION" => "viridis",
                "GLOSS" => "plasma",
                "HARDNESS" => "cividis",
                _ => "viridis"
            },
            isoLevels = new[] { 0.9, 0.7, 0.5, 0.3, 0.1 },
            showWireframe = false,
            slicePosition = 0.5,
            cameraPosition = new[] { 1.5, 1.2, 1.8 },
            lighting = new
            {
                ambient = 0.4,
                directional = 0.8,
                position = new[] { 1, 1, 1 }
            }
        };
        return System.Text.Json.JsonSerializer.Serialize(config);
    }

    private string[] GenerateEnhancementSuggestions(double avgPen, double glossChange,
        double reinforcedPct, MaterialVisualProperties visuals, VirtualCoatingInput input)
    {
        var suggestions = new List<string>();

        if (avgPen < 2.0)
            suggestions.Add("渗透深度较浅，建议延长材料接触时间或进行表面预处理。");
        else if (avgPen < 5.0)
            suggestions.Add("渗透深度适中，可满足表层加固需求。");
        else
            suggestions.Add("渗透深度良好，可实现深度加固效果。");

        if (glossChange > 30)
            suggestions.Add($"警告：光泽度将显著提升 {glossChange:F1}%，可能影响外观原貌。");
        else if (glossChange > 15)
            suggestions.Add($"光泽度将提升 {glossChange:F1}%，视觉变化可接受。");
        else if (glossChange < -10)
            suggestions.Add($"警告：光泽度将下降 {Math.Abs(glossChange):F1}%，材料表观偏哑。");
        else
            suggestions.Add($"光泽度变化 {glossChange:F1}%，外观影响极小。");

        if (reinforcedPct < 20)
            suggestions.Add("加固体积比例较低，建议采用多次涂刷工艺。");
        else if (reinforcedPct < 50)
            suggestions.Add("加固体积比例适中，可有效保护表层区域。");
        else
            suggestions.Add("加固体积比例良好，保护范围充分。");

        suggestions.Add($"材料固化时间约 {visuals.CureTimeHours:F0} 小时，期间需保持环境稳定。");

        if (input.SurfaceRoughness > 0.5)
            suggestions.Add("表面粗糙度较高，建议先进行适当打磨处理。");

        return suggestions.ToArray();
    }

    public static bool TryGetMaterialVisuals(string materialName, out MaterialVisualProperties visuals)
    {
        return MaterialVisuals.TryGetValue(materialName, out visuals!);
    }
}

public class MaterialVisualProperties
{
    public double RefractiveIndex { get; set; }
    public double FilmGloss { get; set; }
    public double FilmHardness { get; set; }
    public double Transparency { get; set; }
    public double[] ColorShift { get; set; } = new[] { 1.0, 1.0, 1.0 };
    public double TypicalFilmThicknessUm { get; set; }
    public double CureTimeHours { get; set; }
}
