using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SteamAnaliticWorker.Services;

namespace SteamAnaliticWorker.Workers;

public class SteamAnalyticsWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SteamAnalyticsWorker> _logger;
    private readonly TimeSpan _period = TimeSpan.FromHours(1);

    public SteamAnalyticsWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SteamAnalyticsWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Steam Analytics Worker started. Will run every hour.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CollectAnalyticsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while collecting analytics");
            }

            await Task.Delay(_period, stoppingToken);
        }
    }

    private async Task CollectAnalyticsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting analytics collection at {Time}", DateTime.UtcNow);

        using var scope = _scopeFactory.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ICollectionOrchestrator>();
        await orchestrator.CollectAsync(cancellationToken);

        _logger.LogInformation("Analytics collection completed at {Time}", DateTime.UtcNow);
    }
}

