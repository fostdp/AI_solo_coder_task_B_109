using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using respiration_eval;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    services.Configure<BreathabilityOptions>(
        context.Configuration.GetSection("Breathability"));

    services.AddSingleton<IMessageBus, MessageBus>();

    services.AddHostedService<BreathabilityWorker>();
    services.AddScoped<IBreathabilityService>(sp =>
        sp.GetServices<IHostedService>().OfType<BreathabilityWorker>().First()
        ?? new BreathabilityWorker(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<BreathabilityOptions>>()));
});

var host = builder.Build();
await host.RunAsync();
