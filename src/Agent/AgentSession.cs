using Neobash.PrintBridge.Lan;

namespace Neobash.PrintBridge.Agent;

public sealed class AgentSession(IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunSessionAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch
        {
            Environment.ExitCode = 1;
            throw;
        }
    }

    private async Task RunSessionAsync(CancellationToken stoppingToken)
    {
        var config = new AgentConfig();
        configuration.GetSection("Agent").Bind(config);
        var secrets = CurrentUserSecretStore.Create(config.ResolveDataDirectory());
        using var engine = new WindowsPrintEngine();
        using var session = LanSession.Open(config, secrets, engine);
        session.Describe(Console.Out, session.IssuePairingCode());
        await using var host = await session.ListenAsync(config.ResolveListenAddress(), config.Port, stoppingToken).ConfigureAwait(false);
        Console.Out.WriteLine("Escucha: " + host.BaseUri);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
