using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using reaction_warning;
using respiration_eval;
using Serilog;
using virtual_coating;
using WashburnPenetration;
using WashburnPenetration.Models;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.Configure<PenetrationPredictionOptions>(
        builder.Configuration.GetSection("PenetrationPrediction"));
    builder.Services.Configure<ChemicalReactionOptions>(
        builder.Configuration.GetSection("ChemicalReaction"));
    builder.Services.Configure<BreathabilityOptions>(
        builder.Configuration.GetSection("Breathability"));
    builder.Services.Configure<VirtualReinforcementOptions>(
        builder.Configuration.GetSection("VirtualReinforcement"));

    builder.Services.AddSingleton<IMessageBus, MessageBus>();

    builder.Services.AddHostedService<PenetrationPredictionWorker>();
    builder.Services.AddScoped<IPenetrationPredictionService>(sp =>
        sp.GetServices<IHostedService>().OfType<PenetrationPredictionWorker>().First()
        ?? new PenetrationPredictionWorker(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<PenetrationPredictionOptions>>()));

    builder.Services.AddHostedService<ChemicalReactionWorker>();
    builder.Services.AddScoped<IChemicalReactionService>(sp =>
        sp.GetServices<IHostedService>().OfType<ChemicalReactionWorker>().First()
        ?? new ChemicalReactionWorker(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<ChemicalReactionOptions>>()));

    builder.Services.AddHostedService<BreathabilityWorker>();
    builder.Services.AddScoped<IBreathabilityService>(sp =>
        sp.GetServices<IHostedService>().OfType<BreathabilityWorker>().First()
        ?? new BreathabilityWorker(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<BreathabilityOptions>>()));

    builder.Services.AddHostedService<VirtualCoatingWorker>();
    builder.Services.AddScoped<IVirtualCoatingService>(sp =>
        sp.GetServices<IHostedService>().OfType<VirtualCoatingWorker>().First()
        ?? new VirtualCoatingWorker(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<VirtualReinforcementOptions>>(),
            sp.GetRequiredService<IPenetrationPredictionService>>()));

    builder.Services.AddControllers();
    builder.Services.AddSwaggerGen();

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors();

    app.UseAuthorization();

    app.MapControllers();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
