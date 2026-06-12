using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClayMonitor.AlertDispatch;

public interface IAlertDispatchService
{
    Task<AlertTriggered> CheckAndTriggerAlertAsync(
        SensorDataReceived sensorData,
        CancellationToken ct = default);

    Task<bool> PushToDingTalkAsync(AlertTriggered alert, CancellationToken ct = default);
}

public class AlertDispatchService : BackgroundService, IAlertDispatchService
{
    private readonly IMessageBus _bus;
    private readonly AlertDispatchOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, DateTime> _lastAlertTimes = new();

    public AlertDispatchService(
        IMessageBus bus,
        IOptions<AlertDispatchOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        _bus = bus;
        _options = options.Value;
        _httpClient = httpClientFactory.CreateClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sensorTask = ProcessSensorDataAsync(stoppingToken);
        var migrationTask = ProcessMigrationResultAsync(stoppingToken);

        await Task.WhenAll(sensorTask, migrationTask);
    }

    private async Task ProcessSensorDataAsync(CancellationToken stoppingToken)
    {
        await foreach (var sensorData in _bus.SubscribeAsync<SensorDataReceived>(stoppingToken))
        {
            try
            {
                var alert = await CheckAndTriggerAlertAsync(sensorData, stoppingToken);
                if (alert != null)
                {
                    await _bus.PublishAsync(alert, stoppingToken);

                    if (_options.EnableConsoleLog)
                    {
                        Console.WriteLine($"[ALERT] {alert.AlertLevel}: {alert.Message}");
                    }

                    if (_options.EnableDingTalkPush)
                    {
                        _ = PushToDingTalkAsync(alert, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }

    private async Task ProcessMigrationResultAsync(CancellationToken stoppingToken)
    {
        await foreach (var migration in _bus.SubscribeAsync<SaltMigrationCompleted>(stoppingToken))
        {
            try
            {
                if (migration.RequiresAlert && !string.IsNullOrEmpty(migration.AlertReason))
                {
                    var alert = new AlertTriggered
                    {
                        SculptureId = migration.SculptureId,
                        AlertType = "SALT_MIGRATION_WARNING",
                        AlertLevel = "WARNING",
                        Message = migration.AlertReason,
                        CurrentValue = Math.Round(migration.SurfaceEnrichmentRatio, 2),
                        Threshold = 2.0,
                        Metadata = new Dictionary<string, object>
                        {
                            ["MaxConcentration"] = migration.MaxConcentration,
                            ["SurfaceEvaporationRate"] = migration.SurfaceEvaporationRate
                        }
                    };

                    await _bus.PublishAsync(alert, stoppingToken);

                    if (_options.EnableDingTalkPush)
                    {
                        _ = PushToDingTalkAsync(alert, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log and continue
            }
        }
    }

    public async Task<AlertTriggered> CheckAndTriggerAlertAsync(
        SensorDataReceived sensorData,
        CancellationToken ct = default)
    {
        string suppressionKey = $"sculpture_{sensorData.SculptureId}";

        if (_lastAlertTimes.TryGetValue(suppressionKey, out var lastTime)
            && (DateTime.Now - lastTime).TotalMinutes < _options.AlarmSuppressionMinutes)
        {
            return null!;
        }

        AlertTriggered? alert = null;

        if (sensorData.SurfaceCoverage > _options.SurfaceCoverageThreshold)
        {
            alert = new AlertTriggered
            {
                SculptureId = sensorData.SculptureId,
                SensorCode = sensorData.SensorCode,
                AlertType = "SURFACE_COVERAGE_EXCEEDED",
                AlertLevel = "CRITICAL",
                Message = $"表面盐结晶覆盖率 {sensorData.SurfaceCoverage:F1}% 超过阈值 {_options.SurfaceCoverageThreshold}%",
                CurrentValue = Math.Round(sensorData.SurfaceCoverage ?? 0, 2),
                Threshold = _options.SurfaceCoverageThreshold,
                Metadata = new Dictionary<string, object>
                {
                    ["NaConcentration"] = sensorData.NaConcentration ?? 0,
                    ["SensorType"] = sensorData.SensorType
                }
            };
        }
        else if (sensorData.NaConcentration > _options.NaConcentrationThreshold)
        {
            alert = new AlertTriggered
            {
                SculptureId = sensorData.SculptureId,
                SensorCode = sensorData.SensorCode,
                AlertType = "NA_CONCENTRATION_EXCEEDED",
                AlertLevel = "CRITICAL",
                Message = $"Na⁺浓度 {sensorData.NaConcentration:F0}ppm 超过阈值 {_options.NaConcentrationThreshold}ppm",
                CurrentValue = Math.Round(sensorData.NaConcentration ?? 0, 2),
                Threshold = _options.NaConcentrationThreshold,
                Metadata = new Dictionary<string, object>
                {
                    ["KConcentration"] = sensorData.KConcentration ?? 0,
                    ["CaConcentration"] = sensorData.CaConcentration ?? 0
                }
            };
        }
        else if (sensorData.SaltConcentration > _options.NaConcentrationThreshold * 0.8)
        {
            alert = new AlertTriggered
            {
                SculptureId = sensorData.SculptureId,
                SensorCode = sensorData.SensorCode,
                AlertType = "SALT_CONCENTRATION_WARNING",
                AlertLevel = "WARNING",
                Message = $"总盐浓度 {sensorData.SaltConcentration:F0}ppm 接近告警阈值",
                CurrentValue = Math.Round(sensorData.SaltConcentration ?? 0, 2),
                Threshold = _options.NaConcentrationThreshold,
                Metadata = new Dictionary<string, object>()
            };
        }

        if (alert != null)
        {
            _lastAlertTimes[suppressionKey] = DateTime.Now;
        }

        return await Task.FromResult(alert!);
    }

    public async Task<bool> PushToDingTalkAsync(AlertTriggered alert, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.DingTalkWebhookUrl))
        {
            return false;
        }

        try
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string sign = GenerateDingTalkSignature(timestamp, _options.DingTalkSecret);

            string url = $"{_options.DingTalkWebhookUrl}&timestamp={timestamp}&sign={sign}";

            var message = BuildDingTalkMessage(alert);
            var content = new StringContent(
                JsonSerializer.Serialize(message),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(url, content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            var json = JsonNode.Parse(responseBody);
            int errcode = json?["errcode"]?.GetValue<int>() ?? -1;

            return errcode == 0;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    private string GenerateDingTalkSignature(long timestamp, string secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;

        string stringToSign = $"{timestamp}\n{secret}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] messageBytes = Encoding.UTF8.GetBytes(stringToSign);

        using var hmac = new HMACSHA256(keyBytes);
        byte[] hashBytes = hmac.ComputeHash(messageBytes);

        return Uri.EscapeDataString(Convert.ToBase64String(hashBytes));
    }

    private object BuildDingTalkMessage(AlertTriggered alert)
    {
        string levelEmoji = alert.AlertLevel switch
        {
            "CRITICAL" => "🔴",
            "WARNING" => "🟡",
            "INFO" => "🔵",
            _ => "⚪"
        };

        string markdown = $@"
{levelEmoji} **泥塑彩绘盐分告警** {levelEmoji}

**告警级别**：{alert.AlertLevel}
**告警类型**：{alert.AlertType}
**泥塑ID**：{alert.SculptureId}
**告警时间**：{alert.TriggeredAt:yyyy-MM-dd HH:mm:ss}

**告警详情**：
{alert.Message}

**数值对比**：
- 当前值：{alert.CurrentValue:F2}
- 阈值：{alert.Threshold:F2}

> 请及时检查并采取加固措施。
";

        return new
        {
            msgtype = "markdown",
            markdown = new
            {
                title = $"泥塑盐分告警 - {alert.AlertLevel}",
                text = markdown
            },
            at = new
            {
                isAtAll = alert.AlertLevel == "CRITICAL"
            }
        };
    }
}
