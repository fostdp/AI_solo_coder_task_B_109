using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WashburnPenetration;
using WashburnPenetration.Models;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    services.Configure<PenetrationPredictionOptions>(
        context.Configuration.GetSection("PenetrationPrediction"));

    services.AddSingleton<IMessageBus, MessageBus>();

    services.AddHostedService<PenetrationPredictionWorker>();
    services.AddScoped<IPenetrationPredictionService>(sp =>
        sp.GetServices<IHostedService>().OfType<PenetrationPredictionWorker>().First()
        ?? new PenetrationPredictionWorker(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<PenetrationPredictionOptions>>()));
});

var host = builder.Build();
await host.RunAsync();
