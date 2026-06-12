using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Messages;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace ClayMonitor.WifiIngest;

public interface IWifiIngestService
{
    Task<SensorDataReceived> ProcessSensorDataAsync(string sensorCode, string payload, CancellationToken ct);
    Task<SensorDataReceived[]> ProcessBatchAsync(string[] payloads, CancellationToken ct);
}

public class WifiIngestService : IWifiIngestService
{
    private readonly IMessageBus _bus;
    private readonly WifiIngestOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public WifiIngestService(IMessageBus bus, IOptions<WifiIngestOptions> options)
    {
        _bus = bus;
        _options = options.Value;
    }

    public async Task<SensorDataReceived> ProcessSensorDataAsync(
        string sensorCode, string payload, CancellationToken ct)
    {
        var sensorData = ParsePayload(sensorCode, payload);
        await ValidateAndNormalizeAsync(sensorData, ct);
        await _bus.PublishAsync(sensorData, ct);
        return sensorData;
    }

    public async Task<SensorDataReceived[]> ProcessBatchAsync(string[] payloads, CancellationToken ct)
    {
        var results = new List<SensorDataReceived>();
        var batch = new List<SensorDataReceived>();

        foreach (var payload in payloads)
        {
            try
            {
                var doc = JsonDocument.Parse(payload);
                var sensorCode = doc.RootElement.GetProperty("sensorCode").GetString() ?? "UNKNOWN";
                var data = ParsePayload(sensorCode, payload);
                await ValidateAndNormalizeAsync(data, ct);
                batch.Add(data);
                results.Add(data);

                if (batch.Count >= _options.BatchSize)
                {
                    foreach (var d in batch)
                    {
                        await _bus.PublishAsync(d, ct);
                    }
                    batch.Clear();
                }
            }
            catch (Exception ex)
            {
                results.Add(new SensorDataReceived
                {
                    SensorCode = "ERROR",
                    RawPayload = payload,
                    Timestamp = DateTime.Now
                });
            }
        }

        foreach (var d in batch)
        {
            await _bus.PublishAsync(d, ct);
        }

        return results.ToArray();
    }

    private SensorDataReceived ParsePayload(string sensorCode, string payload)
    {
        var data = JsonSerializer.Deserialize<SensorDataReceived>(payload, _jsonOptions)
            ?? new SensorDataReceived { SensorCode = sensorCode, RawPayload = payload };

        data.SensorCode = sensorCode;
        data.RawPayload = payload;
        data.Timestamp = data.Timestamp == default ? DateTime.Now : data.Timestamp;

        return data;
    }

    private Task ValidateAndNormalizeAsync(SensorDataReceived data, CancellationToken ct)
    {
        if (data.NaConcentration == null)
            data.NaConcentration = _options.DefaultNaConcentration;
        if (data.KConcentration == null)
            data.KConcentration = _options.DefaultKConcentration;
        if (data.CaConcentration == null)
            data.CaConcentration = _options.DefaultCaConcentration;

        if (data.SaltConcentration == null)
        {
            data.SaltConcentration = (data.NaConcentration * 2.54
                + data.KConcentration * 1.91
                + data.CaConcentration * 2.50) / 3;
        }

        if (string.IsNullOrEmpty(data.SensorType))
        {
            data.SensorType = data.Temperature.HasValue ? "ENVIRONMENT" : "ION_MIGRATION";
        }

        if (data.SurfaceCoverage == null)
        {
            data.SurfaceCoverage = Math.Min(100,
                Math.Max(0, (data.SaltConcentration ?? 0) / 15));
        }

        return Task.CompletedTask;
    }
}
