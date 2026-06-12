using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ClayMonitor.SaltTransport;

public interface ISaltTransportService
{
    Task<SaltMigrationCompleted> RunSimulationAsync(
        int sculptureId,
        double initialConcentration,
        int predictionHours,
        EvaporationEnvironment? env = null,
        CancellationToken ct = default);

    double CalculatePenmanEvaporation(EvaporationEnvironment env);
}

public class EvaporationEnvironment
{
    public double Temperature { get; set; } = 25.0;
    public double RelativeHumidity { get; set; } = 0.55;
    public double WindSpeed { get; set; } = 2.0;
    public double SolarRadiation { get; set; } = 300.0;
    public double AtmosphericPressure { get; set; } = 101.325;
}

public class SaltTransportService : BackgroundService, ISaltTransportService
{
    private readonly IMessageBus _bus;
    private readonly SaltTransportOptions _options;

    public SaltTransportService(IMessageBus bus, IOptions<SaltTransportOptions> options)
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
                var env = new EvaporationEnvironment
                {
                    Temperature = sensorData.Temperature ?? 25.0,
                    RelativeHumidity = (sensorData.Humidity ?? 55) / 100.0,
                    WindSpeed = 2.0,
                    SolarRadiation = CalculateSolarRadiation(sensorData.Timestamp)
                };

                var result = await RunSimulationAsync(
                    sensorData.SculptureId,
                    sensorData.SaltConcentration ?? 100,
                    _options.DefaultPredictionHours,
                    env,
                    stoppingToken);

                await _bus.PublishAsync(result, stoppingToken);
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }

    public async Task<SaltMigrationCompleted> RunSimulationAsync(
        int sculptureId,
        double initialConcentration,
        int predictionHours,
        EvaporationEnvironment? env = null,
        CancellationToken ct = default)
    {
        env ??= new EvaporationEnvironment();

        double dt = _options.TimeStepHours;
        double dz = _options.SpaceStepCm;
        double maxDepth = _options.MaxDepthCm;

        int nz = (int)(maxDepth / dz) + 1;
        int nt = (int)(predictionHours / dt);

        double[] z = new double[nz];
        double[] c = new double[nz];
        double[] theta = new double[nz];

        for (int i = 0; i < nz; i++)
        {
            z[i] = i * dz;
            c[i] = initialConcentration * Math.Exp(-z[i] / 10.0);
            theta[i] = _options.Porosity;
        }

        double E0 = CalculatePenmanEvaporation(env);
        double qBase = _options.BaseVelocity;

        double[] D = new double[nz];
        double[] q = new double[nz];

        for (int t = 0; t < nt; t++)
        {
            double currentTimeHours = (t + 1) * dt;
            double diurnalFactor = 0.4 + 0.6 * Math.Max(0,
                Math.Sin((currentTimeHours % 24 - 6) * Math.PI / 12));
            double Et = Math.Min(_options.MaxEvaporationRate, E0 * diurnalFactor);

            for (int i = 0; i < nz; i++)
            {
                D[i] = CalculateDiffusionCoefficient(theta[i]);
                double depthFactor = Math.Exp(-z[i] / 15.0);
                q[i] = qBase - Et * depthFactor;
            }

            c = ApplyBoundaryConditionsWithEvaporation(c, theta, Et, dz, initialConcentration);
            theta = UpdateMoistureWithEvaporation(theta, Et, dt, dz);
            c = SolveFDMWithEvaporation(c, theta, D, q, Et, dt, dz);

            for (int i = 0; i < nz; i++)
            {
                if (c[i] < 0) c[i] = 0;
                theta[i] = Math.Clamp(theta[i], 0.02, _options.Porosity * 1.1);
            }
        }

        double maxC = 0, sumC = 0;
        for (int i = 0; i < nz; i++)
        {
            if (c[i] > maxC) maxC = c[i];
            sumC += c[i];
        }
        double avgC = sumC / nz;
        double enrichmentRatio = c[0] / (avgC + 1e-9);

        bool requiresAlert = enrichmentRatio > 2.0 || maxC > 400;
        string reason = requiresAlert
            ? enrichmentRatio > 2.0
                ? "表层盐分富集倍数超过2倍，建议排查蒸发源"
                : "最大浓度超过400ppm，接近告警阈值"
            : string.Empty;

        return await Task.FromResult(new SaltMigrationCompleted
        {
            SculptureId = sculptureId,
            SurfaceEvaporationRate = Math.Round(E0, 6),
            SurfaceEnrichmentRatio = Math.Round(enrichmentRatio, 4),
            MaxConcentration = Math.Round(maxC, 4),
            AverageConcentration = Math.Round(avgC, 4),
            DepthProfile = c.Select(x => Math.Round(x, 4)).ToArray(),
            MoistureProfile = theta.Select(x => Math.Round(x, 4)).ToArray(),
            PredictionTime = DateTime.Now,
            PredictionHours = predictionHours,
            RequiresAlert = requiresAlert,
            AlertReason = reason
        });
    }

    public double CalculatePenmanEvaporation(EvaporationEnvironment env)
    {
        double T = env.Temperature;
        double RH = env.RelativeHumidity;
        double u2 = env.WindSpeed;
        double Rn = env.SolarRadiation;
        double P = env.AtmosphericPressure;

        double es = 0.6108 * Math.Exp(17.27 * T / (T + 237.3));
        double ea = es * RH;
        double delta = 4098 * es / Math.Pow(T + 237.3, 2);
        double gamma = 0.000665 * P;
        double G = 0.1 * Rn;

        double radiativeTerm = delta * (Rn - G) / (delta + gamma);
        double aerodynamicTerm = gamma * (900.0 / (T + 273.0)) * u2 * (es - ea) / (delta + gamma);

        double LE_mmPerHour = (radiativeTerm + aerodynamicTerm) / 2450.0 / 3600.0 * 1e6;
        double E_cmPerHour = LE_mmPerHour * 0.1;

        return Math.Max(0, E_cmPerHour);
    }

    private double CalculateDiffusionCoefficient(double theta)
    {
        double D0 = _options.DiffusionCoefficient;
        double tau = _options.Tortuosity;
        return D0 * tau * Math.Max(theta, 0.01) * 3600.0 * 10000.0;
    }

    private double[] ApplyBoundaryConditionsWithEvaporation(
        double[] concentration, double[] theta, double E, double dz, double bottomConcentration)
    {
        int n = concentration.Length;
        if (n < 2) return concentration;

        double surfaceWaterLoss = E;
        if (theta[0] > 0.05)
        {
            double enrichmentFactor = 1.0 + surfaceWaterLoss * _options.EnrichmentFactor;
            concentration[0] = concentration[1] * enrichmentFactor;
        }
        else
        {
            concentration[0] = concentration[1];
        }

        if (concentration[0] < 0) concentration[0] = 0;
        concentration[n - 1] = bottomConcentration;

        return concentration;
    }

    private double[] UpdateMoistureWithEvaporation(double[] theta, double E, double dt, double dz)
    {
        int n = theta.Length;
        double[] thetaNew = new double[n];
        Array.Copy(theta, thetaNew, n);

        double surfaceLoss = Math.Min(E * dt / dz, thetaNew[0] - 0.03);
        thetaNew[0] -= Math.Max(0, surfaceLoss);

        for (int i = 1; i < Math.Min(5, n); i++)
        {
            double capillaryFactor = Math.Exp(-i * 0.8);
            double upwardFlow = _options.CapillaryPressure * capillaryFactor * dt / dz;
            thetaNew[i] -= upwardFlow;
            thetaNew[i - 1] += upwardFlow * 0.5;
        }

        for (int i = 0; i < n; i++)
        {
            thetaNew[i] = Math.Clamp(thetaNew[i], 0.02, _options.Porosity * 1.1);
        }

        return thetaNew;
    }

    private double[] SolveFDMWithEvaporation(
        double[] concentration, double[] theta, double[] D, double[] q, double E, double dt, double dz)
    {
        int n = concentration.Length;
        double[] cNew = new double[n];
        Array.Copy(concentration, cNew, n);

        for (int i = 1; i < n - 1; i++)
        {
            double thetaMean = 0.5 * (theta[i + 1] + theta[i - 1]);
            if (thetaMean < 1e-6) thetaMean = 0.01;

            double DMean = 0.5 * (D[i] + D[i + 1]);
            double r = DMean * dt / (dz * dz);
            double s = q[i] * dt / (2.0 * dz * thetaMean);

            cNew[i] = concentration[i]
                + r * (concentration[i + 1] - 2.0 * concentration[i] + concentration[i - 1])
                - s * (concentration[i + 1] - concentration[i - 1]);
        }

        if (n >= 2)
        {
            double evapConc = E * dt / (dz * Math.Max(theta[0], 0.05));
            cNew[0] = concentration[0] + evapConc * (concentration[1] + 0.001);
            if (cNew[0] < 0) cNew[0] = 0;
            cNew[n - 1] = concentration[n - 1];
        }

        return cNew;
    }

    private double CalculateSolarRadiation(DateTime time)
    {
        double hour = time.Hour + time.Minute / 60.0;
        double solarFactor = Math.Max(0, Math.Sin((hour - 6) * Math.PI / 12));
        return 100 + 700 * solarFactor;
    }
}
