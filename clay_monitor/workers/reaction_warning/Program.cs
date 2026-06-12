using ClayMonitor.Core.Channels;
using ClayMonitor.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((context, services) =>
{
    services.Configure<ChemicalReactionOptions>(
        context.Configuration.GetSection("ChemicalReaction"));

    services.AddSingleton<IMessageBus, MessageBus>();

    services.AddHostedService<ChemicalReactionWorker>();
    services.AddScoped<IChemicalReactionService>(sp =>
        sp.GetServices<IHostedService>().OfType<ChemicalReactionWorker>().First()
        ?? new ChemicalReactionWorker(
            sp.GetRequiredService<IMessageBus>(),
            sp.GetRequiredService<IOptions<ChemicalReactionOptions>>()));
});

var host = builder.Build();
await host.RunAsync();
