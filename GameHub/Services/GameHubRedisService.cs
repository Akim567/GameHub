using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GameHub.Models;
using StackExchange.Redis;

namespace GameHub.Services;

public sealed class GameHubRedisService(IConnectionMultiplexer connection)
{
    private const string LeaderboardKey = "tournament:main";
    private const string NotificationsKey = "notifications";
    private const int ProfileCacheSeconds = 60;
    private const int LoginTtlSeconds = 24 * 60 * 60;
    private const int NotificationRetentionDays = 7;

    private const string LoginScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IDatabase Database => connection.GetDatabase();

    public async Task<PlayerProfile> UpsertPlayerAsync(
        string id,
        UpsertPlayerRequest request)
    {
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var profile = new PlayerProfile(id, request.Name, request.Level, request.Region, createdAt);

        await Database.HashSetAsync(PlayerKey(id), ToHashEntries(profile));
        await Database.KeyDeleteAsync(PlayerCacheKey(id));

        return profile;
    }

    public async Task<(PlayerProfile Profile, bool CacheHit)?> GetPlayerAsync(string id)
    {
        var cached = await Database.StringGetAsync(PlayerCacheKey(id));

        if (cached.HasValue)
        {
            try
            {
                var cachedProfile = JsonSerializer.Deserialize<PlayerProfile>(cached.ToString(), JsonOptions);
                if (cachedProfile is not null)
                {
                    return (cachedProfile, true);
                }
            }
            catch (JsonException)
            {
                await Database.KeyDeleteAsync(PlayerCacheKey(id));
            }
        }

        var entries = await Database.HashGetAllAsync(PlayerKey(id));
        if (entries.Length == 0)
        {
            return null;
        }

        var profile = FromHashEntries(id, entries);
        await Database.StringSetAsync(
            PlayerCacheKey(id),
            JsonSerializer.Serialize(profile, JsonOptions),
            TimeSpan.FromSeconds(ProfileCacheSeconds));

        return (profile, false);
    }

    public async Task<long?> UpdatePlayerLevelAsync(string id, int delta)
    {
        if (!await Database.KeyExistsAsync(PlayerKey(id)))
        {
            return null;
        }

        var level = await Database.HashIncrementAsync(PlayerKey(id), "level", delta);
        await Database.KeyDeleteAsync(PlayerCacheKey(id));
        await AddNotificationAsync(
            id,
            "level_changed",
            $"Player level changed by {delta}; new level is {level}.");

        return level;
    }

    public async Task<long> RecordLoginAsync(string id)
    {
        var result = await Database.ScriptEvaluateAsync(
            LoginScript,
            [LoginKey(id)],
            [LoginTtlSeconds]);

        return (long)result;
    }

    public async Task<double> AddLeaderboardScoreAsync(string playerId, double score)
    {
        return await Database.SortedSetIncrementAsync(LeaderboardKey, playerId, score);
    }

    public async Task<LeaderboardEntry[]> GetLeaderboardAsync(int limit)
    {
        var entries = await Database.SortedSetRangeByRankWithScoresAsync(
            LeaderboardKey,
            0,
            limit - 1,
            Order.Descending);

        return entries
            .Select((entry, index) => new LeaderboardEntry(
                entry.Element.ToString(),
                entry.Score,
                index + 1))
            .ToArray();
    }

    public async Task<long?> GetLeaderboardRankAsync(string playerId)
    {
        var zeroBasedRank = await Database.SortedSetRankAsync(
            LeaderboardKey,
            playerId,
            Order.Descending);

        return zeroBasedRank + 1;
    }

    public async Task<bool> AddAchievementAsync(string id, string achievementName)
    {
        return await Database.SetAddAsync(AchievementsKey(id), achievementName);
    }

    public async Task<bool> HasAchievementAsync(string id, string achievementName)
    {
        return await Database.SetContainsAsync(AchievementsKey(id), achievementName);
    }

    public async Task<string[]> GetCommonAchievementsAsync(string firstId, string secondId)
    {
        var achievements = await Database.SetCombineAsync(
            SetOperation.Intersect,
            AchievementsKey(firstId),
            AchievementsKey(secondId));

        return achievements
            .Select(value => value.ToString())
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<BatchCreatedResponse> CreatePlayersBatchAsync(BatchPlayerRequest[] players)
    {
        var stopwatch = Stopwatch.StartNew();
        var batch = Database.CreateBatch();
        var operations = new List<Task>(players.Length * 2);
        var createdAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var player in players)
        {
            var profile = new PlayerProfile(
                player.Id,
                player.Name,
                player.Level,
                player.Region,
                createdAt);

            operations.Add(batch.HashSetAsync(PlayerKey(player.Id), ToHashEntries(profile)));
            operations.Add(batch.KeyDeleteAsync(PlayerCacheKey(player.Id)));
        }

        batch.Execute();
        await Task.WhenAll(operations);
        stopwatch.Stop();

        return new BatchCreatedResponse(players.Length, stopwatch.ElapsedMilliseconds);
    }

    private async Task AddNotificationAsync(string playerId, string type, string message)
    {
        var timestamp = DateTimeOffset.UtcNow;

        await Database.StreamAddAsync(
            NotificationsKey,
            [
                new NameValueEntry("player_id", playerId),
                new NameValueEntry("type", type),
                new NameValueEntry("message", message),
                new NameValueEntry("timestamp", timestamp.ToUnixTimeSeconds())
            ]);

        var minimumId = $"{timestamp.AddDays(-NotificationRetentionDays).ToUnixTimeMilliseconds()}-0";
        await Database.StreamTrimByMinIdAsync(NotificationsKey, minimumId, useApproximateMaxLength: true);
    }

    private static HashEntry[] ToHashEntries(PlayerProfile profile) =>
    [
        new HashEntry("name", profile.Name),
        new HashEntry("level", profile.Level),
        new HashEntry("region", profile.Region),
        new HashEntry("created_at", profile.CreatedAt)
    ];

    private static PlayerProfile FromHashEntries(string id, HashEntry[] entries)
    {
        var values = entries.ToDictionary(
            entry => entry.Name.ToString(),
            entry => entry.Value.ToString(),
            StringComparer.Ordinal);

        values.TryGetValue("name", out var name);
        values.TryGetValue("region", out var region);
        values.TryGetValue("level", out var levelValue);
        values.TryGetValue("created_at", out var createdAtValue);

        long.TryParse(levelValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level);
        long.TryParse(createdAtValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var createdAt);

        return new PlayerProfile(id, name ?? string.Empty, level, region ?? string.Empty, createdAt);
    }

    private static RedisKey PlayerKey(string id) => $"player:{id}";

    private static RedisKey PlayerCacheKey(string id) => $"cache:player:{id}";

    private static RedisKey LoginKey(string id) => $"logins:{id}";

    private static RedisKey AchievementsKey(string id) => $"achievements:{id}";
}
