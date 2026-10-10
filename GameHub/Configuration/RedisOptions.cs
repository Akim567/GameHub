namespace GameHub.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ServiceName { get; init; } = string.Empty;

    public string[] Sentinels { get; init; } = [];

    public int ConnectTimeoutMilliseconds { get; init; } = 5000;

    public int OperationTimeoutMilliseconds { get; init; } = 5000;

    public int ConnectRetryCount { get; init; } = 3;

    public int StartupRetryCount { get; init; } = 5;

    public int StartupRetryDelayMilliseconds { get; init; } = 1000;
}
