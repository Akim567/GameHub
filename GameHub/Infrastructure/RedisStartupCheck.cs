using StackExchange.Redis;

namespace GameHub.Infrastructure;

public sealed class RedisStartupCheck(
    IConnectionMultiplexer connection,
    ILogger<RedisStartupCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var latency = await connection
            .GetDatabase()
            .PingAsync(CommandFlags.DemandMaster)
            .WaitAsync(cancellationToken);

        logger.LogInformation(
            "Redis connection established through Sentinel. Master ping: {LatencyMilliseconds} ms",
            latency.TotalMilliseconds);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
