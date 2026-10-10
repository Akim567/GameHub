using StackExchange.Redis;

namespace GameHub.Services;

public sealed class NotificationConsumer(
    IConnectionMultiplexer connection,
    ILogger<NotificationConsumer> logger) : BackgroundService
{
    private const string StreamKey = "notifications";
    private const string GroupName = "notifications-group";

    private readonly string _consumerName = $"gamehub-{Environment.MachineName}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var database = connection.GetDatabase();
        await EnsureConsumerGroupAsync(database);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var entries = await database.StreamReadGroupAsync(
                    StreamKey,
                    GroupName,
                    _consumerName,
                    ">",
                    count: 10);

                if (entries.Length == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                foreach (var entry in entries)
                {
                    var fields = string.Join(
                        ", ",
                        entry.Values.Select(value => $"{value.Name}={value.Value}"));

                    logger.LogInformation(
                        "Notification {NotificationId} processed: {Fields}",
                        entry.Id,
                        fields);

                    await database.StreamAcknowledgeAsync(StreamKey, GroupName, entry.Id);
                }
            }
            catch (RedisException exception)
            {
                logger.LogWarning(exception, "Unable to read notifications; retrying");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private static async Task EnsureConsumerGroupAsync(IDatabase database)
    {
        try
        {
            await database.StreamCreateConsumerGroupAsync(
                StreamKey,
                GroupName,
                "$",
                createStream: true);
        }
        catch (RedisServerException exception) when (
            exception.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
        {
            // The group is shared by all application instances and is created only once.
        }
    }
}
