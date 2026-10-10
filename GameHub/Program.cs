using GameHub.Configuration;
using GameHub.Infrastructure;
using GameHub.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
{
    var settings = builder.Configuration
        .GetRequiredSection(RedisOptions.SectionName)
        .Get<RedisOptions>()
        ?? throw new InvalidOperationException("Redis configuration is missing.");

    var logger = serviceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("RedisConnection");

    return RedisConnectionFactory.Connect(settings, logger);
});
builder.Services.AddHostedService<RedisStartupCheck>();
builder.Services.AddSingleton<GameHubRedisService>();
builder.Services.AddHostedService<NotificationConsumer>();

var app = builder.Build();

app.MapOpenApi();
app.MapControllers();

app.Run();
