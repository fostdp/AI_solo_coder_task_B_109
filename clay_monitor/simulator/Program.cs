using Serilog;
using Serilog.Events;
using System.Text;
using System.Text.Json;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter())
    .CreateLogger();

var backendUrl = Environment.GetEnvironmentVariable("BACKEND_URL") ?? "http://backend:5000";
var intervalMinutes = int.Parse(Environment.GetEnvironmentVariable("REPORT_INTERVAL_MINUTES") ?? "45");
var fastMode = Environment.GetEnvironmentVariable("FAST_MODE") == "true";
var actualInterval = fastMode ? TimeSpan.FromSeconds(2) : TimeSpan.FromMinutes(intervalMinutes);
var injectHighNa = Environment.GetEnvironmentVariable("INJECT_HIGH_NA") ?? "";
var highNaSculptures = injectHighNa.Split(',', StringSplitOptions.RemoveEmptyEntries)
    .Select(s => int.TryParse(s.Trim(), out var id) ? id : -1)
    .Where(id => id > 0)
    .ToHashSet();

var ionSensors = new List<SensorConfig>();
for (int i = 1; i <= 40; i++)
{
    var sculptureId = ((i - 1) % 30) + 1;
    ionSensors.Add(new SensorConfig
    {
        Code = $"ION-{i:D3}",
        SculptureId = sculptureId,
        Type = "ION_MIGRATION",
        BaseNa = 80 + sculptureId * 3,
        BaseK = 40 + sculptureId * 2,
        BaseCa = 60 + sculptureId * 2.5,
        IsHighNa = highNaSculptures.Contains(sculptureId)
    });
}

var envSensors = new List<SensorConfig>();
for (int i = 1; i <= 30; i++)
{
    envSensors.Add(new SensorConfig
    {
        Code = $"ENV-{i:D3}",
        SculptureId = i,
        Type = "ENVIRONMENT",
        BaseTemp = 18 + i * 0.3,
        BaseHumidity = 55 + i * 0.5
    });
}

var allSensors = ionSensors.Concat(envSensors).ToList();
Log.Information("模拟器启动: {Count}台传感器 (离子{Ion}/环境{Env}), 间隔={Interval}, 高Na⁺注入={HighNa}",
    allSensors.Count, ionSensors.Count, envSensors.Count,
    fastMode ? "2s(快速)" : $"{intervalMinutes}min",
    highNaSculptures.Count > 0 ? string.Join(",", highNaSculptures) : "无");

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var random = new Random();

while (true)
{
    try
    {
        var timestamp = DateTime.Now;
        var batch = new List<object>();

        foreach (var sensor in allSensors)
        {
            var data = GenerateSensorData(sensor, timestamp, random);
            batch.Add(data);
        }

        var json = JsonSerializer.Serialize(batch);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync($"{backendUrl}/api/sensor-data/batch", content);

        if (response.IsSuccessStatusCode)
        {
            Log.Information("[{Time}] 上报成功: {Count}台传感器 → {Status}",
                timestamp.ToString("HH:mm:ss"), allSensors.Count, (int)response.StatusCode);
        }
        else
        {
            Log.Warning("[{Time}] 上报失败: {Status} {Reason}",
                timestamp.ToString("HH:mm:ss"), (int)response.StatusCode, response.ReasonPhrase);
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "上报异常");
    }

    await Task.Delay(actualInterval);
}

object GenerateSensorData(SensorConfig sensor, DateTime timestamp, Random rng)
{
    var hourFactor = 1 + 0.1 * Math.Sin((timestamp.Hour - 6) * Math.PI / 12);
    var randomFactor = 0.9 + rng.NextDouble() * 0.2;

    if (sensor.Type == "ION_MIGRATION")
    {
        var naBase = sensor.BaseNa * hourFactor * randomFactor;
        var kBase = sensor.BaseK * hourFactor * randomFactor;
        var caBase = sensor.BaseCa * hourFactor * randomFactor;

        if (sensor.IsHighNa)
        {
            naBase *= 4.5 + rng.NextDouble() * 2.0;
            kBase *= 1.8;
            caBase *= 1.5;
        }

        var na = Math.Round(naBase, 1);
        var k = Math.Round(kBase, 1);
        var ca = Math.Round(caBase, 1);
        var salt = Math.Round((na * 2.54 + k * 1.91 + ca * 2.50) / 3, 1);
        var coverage = Math.Round(Math.Min(100, Math.Max(0, salt / 15)), 1);

        return new
        {
            sensorCode = sensor.Code,
            sculptureId = sensor.SculptureId,
            sensorType = sensor.Type,
            naConcentration = na,
            kConcentration = k,
            caConcentration = ca,
            saltConcentration = salt,
            surfaceCoverage = coverage,
            signalStrength = -40 + rng.Next(-15, 15),
            timestamp = timestamp
        };
    }
    else
    {
        var temp = Math.Round(sensor.BaseTemp + (rng.NextDouble() - 0.5) * 4 + hourFactor * 2 - 1, 1);
        var humidity = Math.Round(sensor.BaseHumidity + (rng.NextDouble() - 0.5) * 10 - hourFactor * 3, 1);
        humidity = Math.Clamp(humidity, 30, 95);

        return new
        {
            sensorCode = sensor.Code,
            sculptureId = sensor.SculptureId,
            sensorType = sensor.Type,
            temperature = temp,
            humidity = humidity,
            signalStrength = -35 + rng.Next(-12, 12),
            timestamp = timestamp
        };
    }
}

class SensorConfig
{
    public string Code { get; set; } = "";
    public int SculptureId { get; set; }
    public string Type { get; set; } = "";
    public double BaseNa { get; set; }
    public double BaseK { get; set; }
    public double BaseCa { get; set; }
    public double BaseTemp { get; set; }
    public double BaseHumidity { get; set; }
    public bool IsHighNa { get; set; }
}
