using GameHub.Configuration;
using StackExchange.Redis;

namespace GameHub.Infrastructure;

public static class RedisConnectionFactory
{
    public static IConnectionMultiplexer Connect(
        RedisOptions settings,
        ILogger logger)
    {
        Validate(settings);

        var configuration = new ConfigurationOptions
        {
            ServiceName = settings.ServiceName,
            AbortOnConnectFail = true,
            ConnectRetry = settings.ConnectRetryCount,
            ConnectTimeout = settings.ConnectTimeoutMilliseconds,
            SyncTimeout = settings.OperationTimeoutMilliseconds,
            AsyncTimeout = settings.OperationTimeoutMilliseconds,
            ReconnectRetryPolicy = new ExponentialRetry(1000)
        };

        foreach (var sentinel in settings.Sentinels)
        {
            configuration.EndPoints.Add(sentinel);
        }

        for (var attempt = 1; attempt <= settings.StartupRetryCount; attempt++)
        {
            try
            {
                logger.LogInformation(
                    "Connecting to Redis service {ServiceName} through Sentinel (attempt {Attempt}/{TotalAttempts})",
                    settings.ServiceName,
                    attempt,
                    settings.StartupRetryCount);

                return ConnectionMultiplexer.Connect(configuration);
            }
            catch (Exception exception) when (
                exception is RedisException or TimeoutException &&
                attempt < settings.StartupRetryCount)
            {
                var delay = settings.StartupRetryDelayMilliseconds * attempt;

                logger.LogWarning(
                    exception,
                    "Redis connection failed. Retrying in {DelayMilliseconds} ms",
                    delay);

                Thread.Sleep(delay);
            }
        }

        throw new InvalidOperationException("Redis connection attempts were exhausted.");
    }

    private static void Validate(RedisOptions settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ServiceName))
        {
            throw new InvalidOperationException("Redis:ServiceName must be configured.");
        }

        if (settings.Sentinels.Length == 0 || settings.Sentinels.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("At least one Redis Sentinel endpoint must be configured.");
        }

        if (settings.ConnectTimeoutMilliseconds <= 0 ||
            settings.OperationTimeoutMilliseconds <= 0 ||
            settings.ConnectRetryCount <= 0 ||
            settings.StartupRetryCount <= 0 ||
            settings.StartupRetryDelayMilliseconds <= 0)
        {
            throw new InvalidOperationException("Redis timeout and retry values must be greater than zero.");
        }
    }
}
