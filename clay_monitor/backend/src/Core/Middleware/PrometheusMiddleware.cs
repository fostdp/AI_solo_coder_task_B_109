using Prometheus;
using System.Diagnostics;

namespace ClayMonitor.Core.Middleware;

public class PrometheusMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Counter RequestTotal = Metrics
        .CreateCounter("clay_http_requests_total", "HTTP请求总数", new[] { "method", "endpoint", "status" });
    private static readonly Histogram RequestDuration = Metrics
        .CreateHistogram("clay_http_request_duration_seconds", "HTTP请求耗时",
            new HistogramConfiguration { LabelNames = new[] { "method", "endpoint" }, Buckets = Histogram.ExponentialBuckets(0.001, 2, 12) });
    private static readonly Gauge ActiveSensors = Metrics
        .CreateGauge("clay_active_sensors", "活跃传感器数量");
    private static readonly Counter SensorDataReceived = Metrics
        .CreateCounter("clay_sensor_data_received_total", "传感器数据接收数", new[] { "type" });
    private static readonly Counter AlertsTriggered = Metrics
        .CreateCounter("clay_alerts_triggered_total", "告警触发数", new[] { "level", "type" });
    private static readonly Histogram SaltMigrationDuration = Metrics
        .CreateHistogram("clay_salt_migration_duration_seconds", "盐分迁移模拟耗时",
            new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.01, 2, 10) });
    private static readonly Gauge SurfaceEnrichmentRatio = Metrics
        .CreateGauge("clay_surface_enrichment_ratio", "表层盐分富集比", new[] { "sculpture_id" });
    private static readonly Gauge EvaporationRate = Metrics
        .CreateGauge("clay_evaporation_rate_cm_per_hour", "表面蒸发速率", new[] { "sculpture_id" });

    public PrometheusMiddleware(RequestDelegate next) { _next = next; }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var method = context.Request.Method;
            var endpoint = context.Request.Path.Value ?? "/unknown";
            var status = context.Response.StatusCode.ToString();

            RequestTotal.WithLabels(method, endpoint, status).Inc();
            RequestDuration.WithLabels(method, endpoint).Observe(sw.Elapsed.TotalSeconds);
        }
    }

    public static void RecordSensorData(string type) => SensorDataReceived.WithLabels(type).Inc();
    public static void RecordAlert(string level, string type) => AlertsTriggered.WithLabels(level, type).Inc();
    public static void RecordMigration(double durationSeconds, double enrichmentRatio, double evapRate, int sculptureId)
    {
        SaltMigrationDuration.Observe(durationSeconds);
        SurfaceEnrichmentRatio.WithLabels(sculptureId.ToString()).Set(enrichmentRatio);
        EvaporationRate.WithLabels(sculptureId.ToString()).Set(evapRate);
    }
    public static void SetActiveSensors(double count) => ActiveSensors.Set(count);
}
