using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using virtual_coating;
using WashburnPenetration;
using WashburnPenetration.Models;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    services.Configure<PenetrationPredictionOptions>(
        context.Configuration.GetSection("PenetrationPrediction"));
    services.Configure<VirtualReinforcementOptions>(
        context.Configuration.GetSection("VirtualReinforcement"));

    services.AddSingleton<IMessageBus, MessageBus>();

    services.AddHostedService<PenetrationPredictionWorker>();
    services.AddScoped<IPenetrationPredictionService>(sp =>
        sp.GetServices<IHostedService>().OfType<PenetrationPredictionWorker>().First()
        ?? new PenetrationPredictionWorker(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<PenetrationPredictionOptions>>()));

    services.AddHostedService<VirtualCoatingWorker>();
    services.AddScoped<IVirtualCoatingService>(sp =>
        sp.GetServices<IHostedService>().OfType<VirtualCoatingWorker>().First()
        ?? new VirtualCoatingWorker(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<VirtualReinforcementOptions>>(),
            sp.GetRequiredService<IPenetrationPredictionService>>()));
});

var host = builder.Build();
await host.RunAsync();
