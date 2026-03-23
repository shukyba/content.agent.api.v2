using System.Threading.Channels;

namespace ContentAgent.Api.HostedServices;

public class AgentBackgroundService : BackgroundService
{
    private readonly Channel<bool> _channel;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentBackgroundService> _logger;

    public AgentBackgroundService(
        Channel<bool> channel,
        IServiceProvider serviceProvider,
        ILogger<AgentBackgroundService> logger)
    {
        _channel = channel;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _channel.Reader.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<Services.IAgentPipelineService>();
                await pipeline.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent pipeline failed");
            }
        }
    }
}
