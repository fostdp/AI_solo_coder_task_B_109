using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using ClayMonitor.Core.Middleware;
using ClayMonitor.AlertDispatch;
using ClayMonitor.MaterialScore;
using ClayMonitor.SaltTransport;
using ClayMonitor.WifiIngest;
using ClayMonitor.PenetrationPrediction;
using ClayMonitor.ChemicalReaction;
using ClayMonitor.Breathability;
using ClayMonitor.VirtualReinforcement;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "ClayMonitor")
    .Enrich.WithProperty("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production")
    .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter())
    .WriteTo.File(
        new Serilog.Formatting.Compact.RenderedCompactJsonFormatter(),
        "/app/logs/clay-monitor-.json",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 50 * 1024 * 1024)
    .CreateLogger();

try
{
    Log.Information("古代泥塑彩绘盐分迁移系统启动中...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    builder.Configuration
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args);

    builder.Services.Configure<AppSettings>(builder.Configuration);
    builder.Services.Configure<WifiIngestOptions>(builder.Configuration.GetSection("WifiIngest"));
    builder.Services.Configure<SaltTransportOptions>(builder.Configuration.GetSection("SaltTransport"));
    builder.Services.Configure<MaterialScoreOptions>(builder.Configuration.GetSection("MaterialScore"));
    builder.Services.Configure<AlertDispatchOptions>(builder.Configuration.GetSection("AlertDispatch"));
    builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
    builder.Services.Configure<PenetrationPredictionOptions>(builder.Configuration.GetSection("PenetrationPrediction"));
    builder.Services.Configure<ChemicalReactionOptions>(builder.Configuration.GetSection("ChemicalReaction"));
    builder.Services.Configure<BreathabilityOptions>(builder.Configuration.GetSection("Breathability"));
    builder.Services.Configure<VirtualReinforcementOptions>(builder.Configuration.GetSection("VirtualReinforcement"));

    builder.Services.AddSingleton<IMessageBus, MessageBus>();
    builder.Services.AddHttpClient();

    builder.Services.AddScoped<IWifiIngestService, WifiIngestService>();
    builder.Services.AddHostedService<SaltTransportService>();
    builder.Services.AddScoped<ISaltTransportService>(sp =>
        sp.GetServices<IHostedService>().OfType<SaltTransportService>().First()
        ?? new SaltTransportService(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<SaltTransportOptions>>()));
    builder.Services.AddHostedService<MaterialScoreService>();
    builder.Services.AddScoped<IMaterialScoreService>(sp =>
        sp.GetServices<IHostedService>().OfType<MaterialScoreService>().First()
        ?? new MaterialScoreService(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<MaterialScoreOptions>>()));
    builder.Services.AddHostedService<AlertDispatchService>();
    builder.Services.AddScoped<IAlertDispatchService>(sp =>
        sp.GetServices<IHostedService>().OfType<AlertDispatchService>().First()
        ?? new AlertDispatchService(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<AlertDispatchOptions>>(),
            sp.GetRequiredService<IHttpClientFactory>()));

    builder.Services.AddHostedService<PenetrationPredictionService>();
    builder.Services.AddScoped<IPenetrationPredictionService>(sp =>
        sp.GetServices<IHostedService>().OfType<PenetrationPredictionService>().First()
        ?? new PenetrationPredictionService(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<PenetrationPredictionOptions>>()));

    builder.Services.AddHostedService<ChemicalReactionService>();
    builder.Services.AddScoped<IChemicalReactionService>(sp =>
        sp.GetServices<IHostedService>().OfType<ChemicalReactionService>().First()
        ?? new ChemicalReactionService(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<ChemicalReactionOptions>>()));

    builder.Services.AddHostedService<BreathabilityService>();
    builder.Services.AddScoped<IBreathabilityService>(sp =>
        sp.GetServices<IHostedService>().OfType<BreathabilityService>().First()
        ?? new BreathabilityService(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<BreathabilityOptions>>()));

    builder.Services.AddHostedService<VirtualReinforcementService>();
    builder.Services.AddScoped<IVirtualReinforcementService>(sp =>
        sp.GetServices<IHostedService>().OfType<VirtualReinforcementService>().First()
        ?? new VirtualReinforcementService(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<VirtualReinforcementOptions>>(),
            sp.GetRequiredService<IPenetrationPredictionService>()));

    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "古代泥塑彩绘层盐分迁移与加固材料适配系统 API",
            Version = "v1",
            Description = "模块: WifiIngest / SaltTransport / MaterialScore / AlertDispatch / PenetrationPrediction / ChemicalReaction / Breathability / VirtualReinforcement"
        });
    });

    builder.Services.AddCors(options =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5173", "http://localhost:3000", "http://localhost:8080" };
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials()
                  .SetPreflightMaxAge(TimeSpan.FromHours(1));
        });
    });

    var app = builder.Build();

    ConfigureSqliteWal(app);

    app.UseMiddleware<PrometheusMiddleware>();
    app.UseHttpMetrics();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ClayMonitor v1"));
    }

    app.UseCors("AllowFrontend");
    app.UseAuthorization();
    app.MapControllers();
    app.MapMetrics();

    var lifetime = app.Lifetime;
    lifetime.ApplicationStarted.Register(() =>
    {
        Log.Information(@"
╔══════════════════════════════════════════════════════════════╗
║   古代泥塑彩绘层盐分迁移与加固材料适配系统 v2.0 启动           ║
╠══════════════════════════════════════════════════════════════╣
║  Serilog → Console + File(/app/logs/)                         ║
║  Prometheus → /metrics                                         ║
║  SQLite → WAL mode (journal_mode=WAL, synchronous=NORMAL)    ║
╠══════════════════════════════════════════════════════════════╣
║  核心管线:                                                     ║
║  ┌──────────┐   ┌──────────┐   ┌──────────┐   ┌──────────┐  ║
║  │ WifiIngest│──▶│SaltTrans │──▶│MaterialSc│──▶│AlertDisp │  ║
║  │ Wi-Fi接入 │   │ Richards │   │ 6维度评分 │   │ 钉钉推送  │  ║
║  └──────────┘   └──────────┘   └──────────┘   └──────────┘  ║
╠══════════════════════════════════════════════════════════════╣
║  新增功能 (v2.0):                                             ║
║  ┌────────────┐  ┌────────────┐  ┌────────────┐             ║
║  │ Penetration│  │ ChemReaction│  │ Breathability│            ║
║  │ 渗透预测   │  │ 化学预警   │  │ 呼吸评估   │             ║
║  └────────────┘  └────────────┘  └────────────┘             ║
║                ┌─────────────┐                               ║
║                │ VirtualReinf│                               ║
║                │ 虚拟加固    │                               ║
║                └─────────────┘                               ║
╠══════════════════════════════════════════════════════════════╣
║  API: /api/AdvancedFeatures                                   ║
╚══════════════════════════════════════════════════════════════╝
");
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "系统启动失败");
}
finally
{
    Log.CloseAndFlush();
}

void ConfigureSqliteWal(WebApplication app)
{
    var dbSection = app.Configuration.GetSection("Database");
    var dbPath = dbSection["ConnectionString"] ?? "Data Source=/app/data/sculpture_monitor.db";
    if (dbPath.StartsWith("Data Source="))
        dbPath = dbPath.Substring("Data Source=".Length);

    var dir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
    {
        Directory.CreateDirectory(dir);
        Log.Information("创建数据库目录: {Dir}", dir);
    }

    if (File.Exists(dbPath))
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA wal_autocheckpoint=1000; PRAGMA cache_size=-64000; PRAGMA temp_store=MEMORY;";
        cmd.ExecuteNonQuery();

        using var verify = conn.CreateCommand();
        verify.CommandText = "PRAGMA journal_mode";
        var mode = verify.ExecuteScalar()?.ToString();
        Log.Information("SQLite WAL模式: {Mode} (期望: wal)", mode);
    }
    else
    {
        Log.Warning("数据库文件不存在: {Path}，等待初始化", dbPath);
    }
}
