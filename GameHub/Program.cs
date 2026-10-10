using GameHub.Configuration;
using GameHub.Infrastructure;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);


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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
