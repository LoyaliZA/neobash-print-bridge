using Neobash.PrintBridge.Agent;
using Neobash.PrintBridge.Lan;
using Microsoft.Extensions.Configuration;

if (args.Length > 0 && args[0] is "pair" or "revoke" or "renew" or "stage")
{
    var config = LoadConfig();
    var secrets = CurrentUserSecretStore.Create(config.ResolveDataDirectory());
    using var engine = new WindowsPrintEngine();
    return await AgentConsole.RunAsync(args, Console.Out, Console.Error, secrets, engine, config);
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<AgentSession>();
await builder.Build().RunAsync();
return 0;

static AgentConfig LoadConfig()
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    var config = new AgentConfig();
    configuration.GetSection("Agent").Bind(config);
    return config;
}
